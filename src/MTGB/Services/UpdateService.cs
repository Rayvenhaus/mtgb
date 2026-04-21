using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MTGB.Config;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MTGB.Services;

// ── Update info model ─────────────────────────────────────────

public record ReleaseInfo
{
    [JsonPropertyName("version")]
    public string Version { get; init; } = string.Empty;

    [JsonPropertyName("release_date")]
    public string ReleaseDate { get; init; } = string.Empty;

    [JsonPropertyName("msix_url")]
    public string MsixUrl { get; init; } = string.Empty;

    [JsonPropertyName("zip_url")]
    public string ZipUrl { get; init; } = string.Empty;

    [JsonPropertyName("release_notes")]
    public string ReleaseNotes { get; init; } = string.Empty;
}

// ── Interface ─────────────────────────────────────────────────

public interface IUpdateService
{
    /// <summary>
    /// Check for a new version against the community endpoint.
    /// Returns release info if a newer version is available,
    /// null if up to date or check fails.
    /// </summary>
    Task<ReleaseInfo?> CheckForUpdateAsync(
        CancellationToken ct = default);

    /// <summary>
    /// Download the MSIX to a temp file and return the path.
    /// Reports progress via the provided callback.
    /// </summary>
    Task<string?> DownloadUpdateAsync(
        ReleaseInfo release,
        IProgress<int> progress,
        CancellationToken ct = default);

    /// <summary>
    /// Launch the downloaded MSIX installer and exit MTGB.
    /// </summary>
    void InstallUpdate(string msixPath);

    /// <summary>
    /// Returns the last release info found by CheckForUpdateAsync.
    /// Null if no update has been found this session.
    /// </summary>
    ReleaseInfo? GetCachedRelease();
}

// ── Implementation ────────────────────────────────────────────

/// <summary>
/// Checks for MTGB updates via community.myndworx.com.
/// Never touches GitHub directly — the endpoint owns the data.
/// Silent failure on all network errors.
/// The Ministry handles its own distribution.
/// </summary>
public class UpdateService : IUpdateService
{
    private readonly IOptions<AppSettings> _settings;
    private readonly ILogger<UpdateService> _logger;
    private readonly HttpClient _httpClient;

    // Cached release info from last successful check
    private ReleaseInfo? _cachedRelease;

    private const string UpdateUrl =
        "https://community.myndworx.com/mtgb/v1/release/latest";

    private static readonly JsonSerializerOptions JsonOptions =
        new() { PropertyNameCaseInsensitive = true };

    public UpdateService(
        IOptions<AppSettings> settings,
        ILogger<UpdateService> logger,
        HttpClient httpClient)
    {
        _settings = settings;
        _logger = logger;
        _httpClient = httpClient;
    }

    // ── Check ─────────────────────────────────────────────────

    public async Task<ReleaseInfo?> CheckForUpdateAsync(
        CancellationToken ct = default)
    {
        try
        {
            _logger.LogDebug(
                "Checking for updates at {Url}.", UpdateUrl);

            var response = await _httpClient
                .GetAsync(UpdateUrl, ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug(
                    "Update check returned {Status}.",
                    response.StatusCode);
                return null;
            }

            var json = await response.Content
                .ReadAsStringAsync(ct);

            var envelope = JsonSerializer
                .Deserialize<ApiEnvelope>(json, JsonOptions);

            if (envelope?.Status != true ||
                envelope.Data is null)
                return null;

            var release = JsonSerializer
                .Deserialize<ReleaseInfo>(
                    envelope.Data.ToString()!,
                    JsonOptions);

            if (release is null) return null;

            // Compare versions
            var current = GetCurrentVersion();
            var available = ParseVersion(release.Version);

            if (available is null || available <= current)
            {
                _logger.LogDebug(
                    "MTGB is up to date — " +
                    "current: {Current}, available: {Available}.",
                    current, available);
                return null;
            }

            // Don't notify twice for the same version
            if (_settings.Value.Update.LastNotifiedVersion
                == release.Version)
            {
                _logger.LogDebug(
                    "Already notified for v{Version} — skipping.",
                    release.Version);
                return null;
            }

            _logger.LogInformation(
                "Update available — v{Version}.",
                release.Version);

            // Cache the release for the toast action handler
            _cachedRelease = release;

            // Record check time
            _settings.Value.Update.LastChecked =
                DateTimeOffset.Now;

            return release;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex,
                "Update check failed silently. " +
                "The Ministry will try again later.");
            return null;
        }
    }

    // ── Download ──────────────────────────────────────────────

    public async Task<string?> DownloadUpdateAsync(
        ReleaseInfo release,
        IProgress<int> progress,
        CancellationToken ct = default)
    {
        try
        {
            var tempPath = Path.Combine(
                Path.GetTempPath(),
                $"MTGB-{release.Version}-x64.msix");

            _logger.LogInformation(
                "Downloading MTGB v{Version} to {Path}.",
                release.Version, tempPath);

            using var response = await _httpClient
                .GetAsync(
                    release.MsixUrl,
                    HttpCompletionOption.ResponseHeadersRead,
                    ct);

            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers
                .ContentLength ?? -1L;

            await using var stream = await response.Content
                .ReadAsStreamAsync(ct);
            await using var fileStream = new FileStream(
                tempPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                useAsync: true);

            var buffer = new byte[81920];
            var totalRead = 0L;
            int bytesRead;

            while ((bytesRead = await stream.ReadAsync(
                buffer, ct)) > 0)
            {
                await fileStream.WriteAsync(
                    buffer.AsMemory(0, bytesRead), ct);

                totalRead += bytesRead;

                if (totalBytes > 0)
                {
                    var percent = (int)(totalRead * 100
                        / totalBytes);
                    progress.Report(percent);
                }
            }

            progress.Report(100);

            _logger.LogInformation(
                "Download complete — {Path}.", tempPath);

            return tempPath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Download failed for v{Version}.",
                release.Version);
            return null;
        }
    }

    // ── Install ───────────────────────────────────────────────

    public void InstallUpdate(string msixPath)
    {
        _logger.LogInformation(
            "Launching MSIX installer — {Path}. " +
            "MTGB is exiting. Goodbye.",
            msixPath);

        System.Diagnostics.Process.Start(
            new System.Diagnostics.ProcessStartInfo
            {
                FileName = msixPath,
                UseShellExecute = true
            });

        System.Windows.Application.Current.Shutdown();
    }

    // ── Cached release ────────────────────────────────────────

    public ReleaseInfo? GetCachedRelease() => _cachedRelease;

    // ── Helpers ───────────────────────────────────────────────

    private static Version GetCurrentVersion() =>
        typeof(UpdateService).Assembly
            .GetName().Version
            ?? new Version(0, 0, 0, 0);

    private static Version? ParseVersion(string version)
    {
        var clean = version.TrimStart('v');
        return Version.TryParse(clean, out var v) ? v : null;
    }

    // ── API envelope ──────────────────────────────────────────

    private class ApiEnvelope
    {
        [JsonPropertyName("status")]
        public bool Status { get; init; }

        [JsonPropertyName("data")]
        public object? Data { get; init; }
    }
}