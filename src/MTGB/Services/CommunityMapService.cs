using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MTGB.Config;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MTGB.Services;

// ── Country data models ───────────────────────────────────────

public record CountryData
{
    [JsonPropertyName("code")]
    public string Code { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("has_states")]
    public bool HasStates { get; init; }

    [JsonPropertyName("states")]
    public List<string> States { get; init; } = new();
}

public record CountryList
{
    [JsonPropertyName("countries")]
    public List<CountryData> Countries { get; init; } = new();
}

// ── Interface ─────────────────────────────────────────────────

public interface ICommunityMapService
{
    /// <summary>
    /// Load the bundled country and state data.
    /// Returns null if the file cannot be loaded.
    /// </summary>
    CountryList? LoadCountries();

    /// <summary>
    /// Register on the community map.
    /// Returns true if registration was successful.
    /// </summary>
    Task<bool> RegisterAsync(
        string countryCode,
        string countryName,
        string? stateName,
        CancellationToken ct = default);

    /// <summary>
    /// Check registration status.
    /// Returns the display name if registered, null if not.
    /// </summary>
    Task<string?> GetStatusAsync(
        CancellationToken ct = default);

    /// <summary>
    /// Opt out of the community map.
    /// Returns true if removal was successful.
    /// </summary>
    Task<bool> UnregisterAsync(
        CancellationToken ct = default);
}

// ── Implementation ────────────────────────────────────────────

/// <summary>
/// Manages community map registration for MTGB.
/// Handles the register, status and opt-out calls to
/// community.myndworx.com.
///
/// State/territory level only — never city, never street.
/// The dots are anonymous. There is nothing to see here.
/// Except tiny little unassuming dots.
/// </summary>
public class CommunityMapService : ICommunityMapService
{
    private readonly IOptions<AppSettings> _settings;
    private readonly ILogger<CommunityMapService> _logger;
    private readonly HttpClient _httpClient;

    private const string BaseUrl =
        "https://community.myndworx.com/mtgb/v1/map";

    private static readonly string CountriesFilePath =
        Path.Combine(
            AppContext.BaseDirectory,
            "Assets", "countries.json");

    private static readonly JsonSerializerOptions JsonOptions =
        new()
        {
            PropertyNameCaseInsensitive = true
        };

    public CommunityMapService(
        IOptions<AppSettings> settings,
        ILogger<CommunityMapService> logger,
        HttpClient httpClient)
    {
        _settings = settings;
        _logger = logger;
        _httpClient = httpClient;
    }

    // ── Country data ──────────────────────────────────────────

    /// <inheritdoc/>
    public CountryList? LoadCountries()
    {
        try
        {
            if (!File.Exists(CountriesFilePath))
            {
                _logger.LogWarning(
                    "countries.json not found at {Path}.",
                    CountriesFilePath);
                return null;
            }

            var json = File.ReadAllText(CountriesFilePath);
            return JsonSerializer.Deserialize<CountryList>(
                json, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to load countries.json. " +
                "The Ministry's atlas is missing.");
            return null;
        }
    }

    // ── Registration ──────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<bool> RegisterAsync(
        string countryCode,
        string countryName,
        string? stateName,
        CancellationToken ct = default)
    {
        try
        {
            var installId = GetOrCreateInstallId();

            var payload = new
            {
                install_id = installId,
                mtgb_version = GetVersion(),
                country_code = countryCode,
                country_name = countryName,
                state_name = stateName
            };

            var response = await PostAsync(
                $"{BaseUrl}/register",
                payload,
                ct);

            if (response.Status)
            {
                // Persist the registration locally
                _settings.Value.CommunityMap.Registered = true;
                _settings.Value.CommunityMap.CountryCode = countryCode;
                _settings.Value.CommunityMap.CountryName = countryName;
                _settings.Value.CommunityMap.StateName = stateName;
                _settings.Value.CommunityMap.DisplayName =
                    stateName is not null
                        ? $"{stateName}, {countryName}"
                        : countryName;

                _logger.LogInformation(
                    "Community map registration complete — {DisplayName}.",
                    _settings.Value.CommunityMap.DisplayName);

                return true;
            }

            _logger.LogWarning(
                "Community map registration failed — {Message}.",
                response.Message);

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Community map registration request failed.");
            return false;
        }
    }

    // ── Status ────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<string?> GetStatusAsync(
        CancellationToken ct = default)
    {
        try
        {
            var installId = GetOrCreateInstallId();

            var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"{BaseUrl}/status/{installId}");

            var response = await _httpClient.SendAsync(
                request, ct);

            var json = await response.Content
                .ReadAsStringAsync(ct);

            var result = JsonSerializer.Deserialize<ApiResponse>(
                json, JsonOptions);

            if (result?.Status == true &&
                result.Data?.TryGetValue(
                    "registered",
                    out var registered) == true &&
                registered.ValueKind == JsonValueKind.True)
            {
                if (result.Data.TryGetValue(
                        "display_name",
                        out var displayName) &&
                    displayName.ValueKind == JsonValueKind.String)
                {
                    return displayName.GetString();
                }

                return string.Empty;
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Community map status check failed.");
            return null;
        }
    }

    // ── Unregister ────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<bool> UnregisterAsync(
        CancellationToken ct = default)
    {
        try
        {
            var installId = GetOrCreateInstallId();

            var payload = new
            {
                install_id = installId
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(
                json,
                Encoding.UTF8,
                "application/json");

            var request = new HttpRequestMessage(
                HttpMethod.Delete,
                $"{BaseUrl}/register")
            {
                Content = content
            };

            var response = await _httpClient.SendAsync(
                request, ct);

            var responseJson = await response.Content
                .ReadAsStringAsync(ct);

            var result = JsonSerializer.Deserialize<ApiResponse>(
                responseJson, JsonOptions);

            if (result?.Status == true)
            {
                // Clear local registration state
                _settings.Value.CommunityMap.Registered = false;
                _settings.Value.CommunityMap.CountryCode = null;
                _settings.Value.CommunityMap.CountryName = null;
                _settings.Value.CommunityMap.StateName = null;
                _settings.Value.CommunityMap.DisplayName = null;

                _logger.LogInformation(
                    "Community map registration removed. " +
                    "The Ministry has filed the departure form.");

                return true;
            }

            _logger.LogWarning(
                "Community map unregister failed — {Message}.",
                result?.Message);

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Community map unregister request failed.");
            return false;
        }
    }

    // ── Helpers ───────────────────────────────────────────────

    private async Task<ApiResponse> PostAsync(
        string url,
        object payload,
        CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(
            json,
            Encoding.UTF8,
            "application/json");

        var response = await _httpClient.PostAsync(
            url, content, ct);

        var responseJson = await response.Content
            .ReadAsStringAsync(ct);

        return JsonSerializer.Deserialize<ApiResponse>(
                   responseJson, JsonOptions)
               ?? new ApiResponse
               {
                   Status = false,
                   Message = "Empty response"
               };
    }

    private string GetOrCreateInstallId()
    {
        if (!string.IsNullOrWhiteSpace(
            _settings.Value.InstallId))
            return _settings.Value.InstallId;

        // Generate a new anonymous install ID
        var installId = Guid.NewGuid().ToString();
        _settings.Value.InstallId = installId;

        _logger.LogInformation(
            "Generated new anonymous install ID.");

        return installId;
    }

    private static string GetVersion() =>
        typeof(CommunityMapService).Assembly
            .GetName().Version
            ?.ToString(3) ?? "0.0.0";

    // ── API response model ────────────────────────────────────

    private class ApiResponse
    {
        [JsonPropertyName("status")]
        public bool Status { get; init; }

        [JsonPropertyName("message")]
        public string Message { get; init; } = string.Empty;

        [JsonPropertyName("data")]
        public Dictionary<string, JsonElement>? Data
        { get; init; }
    }
}