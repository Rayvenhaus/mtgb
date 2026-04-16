using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MTGB.Config;
using MTGB.Core.Security;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MTGB.Services;

// ── Response models ───────────────────────────────────────────────

/// <summary>
/// Base response wrapper — every SimplyPrint response has status + message.
/// </summary>
public class SimplyPrintResponse
{
    [JsonPropertyName("status")]
    public bool Status { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }
}

/// <summary>
/// Printer list response.
/// </summary>
public class PrinterListResponse : SimplyPrintResponse
{
    [JsonPropertyName("data")]
    public List<PrinterData>? Data { get; init; }

    [JsonPropertyName("page_amount")]
    public int PageAmount { get; init; }
}

/// <summary>
/// A single printer returned by /printers/Get.
/// </summary>
public class PrinterData
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("printer")]
    public PrinterInfo? Printer { get; init; }
}

/// <summary>
/// Printer state and telemetry.
/// </summary>
public class PrinterInfo
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("state")]
    public string State { get; init; } = string.Empty;

    [JsonPropertyName("online")]
    public bool Online { get; init; }

    [JsonPropertyName("temps")]
    public PrinterTemps? Temps { get; init; }

    [JsonPropertyName("hasPSU")]
    public int HasPsu { get; init; }

    [JsonPropertyName("psuOn")]
    public bool PsuOn { get; init; }

    [JsonPropertyName("hasFilSensor")]
    public bool HasFilamentSensor { get; init; }

    [JsonPropertyName("filSensor")]
    public bool FilamentSensorTriggered { get; init; }

    [JsonPropertyName("job")]
    public PrinterJob? Job { get; init; }
}

/// <summary>
/// Current and target temperatures for tool and bed.
/// </summary>
public class PrinterTemps
{
    [JsonPropertyName("ambient")]
    public double? Ambient { get; init; }

    [JsonPropertyName("current")]
    public TempReadings? Current { get; init; }

    [JsonPropertyName("target")]
    public TempReadings? Target { get; init; }
}

public class TempReadings
{
    [JsonPropertyName("tool")]
    public List<double?>? Tool { get; init; }

    [JsonPropertyName("bed")]
    public double? Bed { get; init; }
}

/// <summary>
/// Active print job on a printer.
/// </summary>
public class PrinterJob
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("filename")]
    public string? Filename { get; init; }

    [JsonPropertyName("started")]
    public long? Started { get; init; }

    [JsonPropertyName("percentage")]
    public double? Percentage { get; init; }

    [JsonPropertyName("time")]
    public JobTime? Time { get; init; }
}

public class JobTime
{
    [JsonPropertyName("elapsed")]
    public int? Elapsed { get; init; }

    [JsonPropertyName("remaining")]
    public int? Remaining { get; init; }
}

/// <summary>
/// Account test response.
/// </summary>
public class AccountTestResponse : SimplyPrintResponse { }

/// <summary>
/// Webhook registration response.
/// </summary>
public class WebhookResponse : SimplyPrintResponse
{
    [JsonPropertyName("id")]
    public int? Id { get; init; }
}

// ── Interface ─────────────────────────────────────────────────────

public interface ISimplyPrintApiClient
{
    /// <summary>Test the current credentials are valid.</summary>
    Task<bool> TestConnectionAsync(int? organisationId = null, CancellationToken ct = default);

    /// <summary>Get all printers for the organisation.</summary>
    Task<List<PrinterData>> GetPrintersAsync(CancellationToken ct = default);

    /// <summary>Pause a print job.</summary>
    Task<bool> PausePrintAsync(int printerId, CancellationToken ct = default);

    /// <summary>Resume a paused print job.</summary>
    Task<bool> ResumePrintAsync(int printerId, CancellationToken ct = default);

    /// <summary>Cancel a print job.</summary>
    Task<bool> CancelPrintAsync(int printerId, CancellationToken ct = default);

    /// <summary>Register a webhook with SimplyPrint.</summary>
    Task<int?> RegisterWebhookAsync(string callbackUrl, string secret, CancellationToken ct = default);

    /// <summary>Delete a registered webhook.</summary>
    Task<bool> DeleteWebhookAsync(int webhookId, CancellationToken ct = default);
}

// ── Implementation ────────────────────────────────────────────────

/// <summary>
/// Typed HTTP client for the SimplyPrint API.
/// All requests are authenticated and scoped to the configured organisation.
/// </summary>
public class SimplyPrintApiClient : ISimplyPrintApiClient
{
    private readonly HttpClient _http;
    private readonly ICredentialManager _credentials;
    private readonly IOptions<AppSettings> _settings;
    private readonly ILogger<SimplyPrintApiClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public SimplyPrintApiClient(
        HttpClient http,
        ICredentialManager credentials,
        IOptions<AppSettings> settings,
        ILogger<SimplyPrintApiClient> logger)
    {
        _http = http;
        _credentials = credentials;
        _settings = settings;
        _logger = logger;
    }

    // ── Auth header ───────────────────────────────────────────────

    /// <summary>
    /// Applies the correct auth header based on the configured auth mode.
    /// Called before every request.
    /// </summary>
    private void ApplyAuthHeader()
    {
        _http.DefaultRequestHeaders.Remove("X-API-KEY");
        _http.DefaultRequestHeaders.Remove("Authorization");

        if (_settings.Value.AuthMode == AuthMode.ApiKey)
        {
            var apiKey = _credentials.Load(CredentialKey.ApiKey);
            if (!string.IsNullOrWhiteSpace(apiKey))
                _http.DefaultRequestHeaders.Add("X-API-KEY", apiKey);
        }
        else
        {
            var token = _credentials.Load(CredentialKey.OAuthAccessToken);
            if (!string.IsNullOrWhiteSpace(token))
                _http.DefaultRequestHeaders.Add(
                    "Authorization", $"Bearer {token}");
        }
    }

    // ── Base URL helper ───────────────────────────────────────────

    private string OrgUrl(string endpoint) =>
        $"{_settings.Value.OrganisationId}/{endpoint}";

    // ── Endpoints ─────────────────────────────────────────────────

    // In the implementation
    public async Task<bool> TestConnectionAsync(
        int? organisationId = null,
        CancellationToken ct = default)
    {
        try
        {
            ApplyAuthHeader();
            var orgId = organisationId
                ?? _settings.Value.OrganisationId;
            var url = $"https://api.simplyprint.io/" + $"{orgId}/account/Test";

            _logger.LogInformation(
                "Testing connection: {Url}", url);

            var response = await _http
                .GetFromJsonAsync<AccountTestResponse>(
                    url, JsonOptions, ct);

            _logger.LogInformation(
                "Test response: {Status} — {Message}",
                response?.Status, response?.Message);

            return response?.Status == true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Connection test failed.");
            return false;
        }
    }

    public async Task<List<PrinterData>> GetPrintersAsync(
        CancellationToken ct = default)
    {
        try
        {
            ApplyAuthHeader();

            var response = await _http.PostAsJsonAsync(
                OrgUrl("printers/Get"),
                new { page = 1, page_size = 100 },
                ct);

            response.EnsureSuccessStatusCode();

            var result = await response.Content
                .ReadFromJsonAsync<PrinterListResponse>(JsonOptions, ct);

            if (result?.Status != true || result.Data is null)
            {
                _logger.LogWarning(
                    "GetPrinters returned status=false: {Message}",
                    result?.Message);
                return new List<PrinterData>();
            }

            _logger.LogDebug(
                "Retrieved {Count} printers.", result.Data.Count);

            return result.Data;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve printers.");
            return new List<PrinterData>();
        }
    }

    /// <inheritdoc/>
    public async Task<bool> PausePrintAsync(
        int printerId, CancellationToken ct = default)
    {
        try
        {
            ApplyAuthHeader();
            var response = await _http.PostAsync(
                OrgUrl($"printers/actions/Pause?pid={printerId}"),
                null, ct);
            response.EnsureSuccessStatusCode();
            var result = await response.Content
                .ReadFromJsonAsync<SimplyPrintResponse>(JsonOptions, ct);
            return result?.Status == true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to pause printer {PrinterId}.", printerId);
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> ResumePrintAsync(
        int printerId, CancellationToken ct = default)
    {
        try
        {
            ApplyAuthHeader();
            var response = await _http.PostAsync(
                OrgUrl($"printers/actions/Resume?pid={printerId}"),
                null, ct);
            response.EnsureSuccessStatusCode();
            var result = await response.Content
                .ReadFromJsonAsync<SimplyPrintResponse>(JsonOptions, ct);
            return result?.Status == true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to resume printer {PrinterId}.", printerId);
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> CancelPrintAsync(
        int printerId, CancellationToken ct = default)
    {
        try
        {
            ApplyAuthHeader();
            var response = await _http.PostAsJsonAsync(
                OrgUrl($"printers/actions/Cancel?pid={printerId}"),
                new { reason = 3 },
                ct);
            response.EnsureSuccessStatusCode();
            var result = await response.Content
                .ReadFromJsonAsync<SimplyPrintResponse>(JsonOptions, ct);
            return result?.Status == true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to cancel printer {PrinterId}.", printerId);
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<int?> RegisterWebhookAsync(
        string callbackUrl,
        string secret,
        CancellationToken ct = default)
    {
        try
        {
            ApplyAuthHeader();

            var response = await _http.PostAsJsonAsync(
                OrgUrl("webhooks/CreateOrUpdate"),
                new
                {
                    name = "MTGB",
                    url = callbackUrl,
                    secret = secret,
                    events = new[]
                    {
                        "job.started",
                        "job.finished",
                        "job.failed",
                        "job.cancelled",
                        "job.paused",
                        "job.resumed",
                        "queue.added",
                        "queue.emptied"
                    }
                },
                ct);

            response.EnsureSuccessStatusCode();

            var result = await response.Content
                .ReadFromJsonAsync<WebhookResponse>(JsonOptions, ct);

            if (result?.Status == true && result.Id.HasValue)
            {
                _logger.LogInformation(
                    "Webhook registered with ID {WebhookId}.", result.Id);
                return result.Id;
            }

            _logger.LogWarning(
                "Webhook registration failed: {Message}", result?.Message);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register webhook.");
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteWebhookAsync(
        int webhookId, CancellationToken ct = default)
    {
        try
        {
            ApplyAuthHeader();
            var response = await _http.DeleteAsync(
                OrgUrl($"webhooks/Delete?id={webhookId}"), ct);
            response.EnsureSuccessStatusCode();
            var result = await response.Content
                .ReadFromJsonAsync<SimplyPrintResponse>(JsonOptions, ct);
            return result?.Status == true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to delete webhook {WebhookId}.", webhookId);
            return false;
        }
    }
}