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

    [JsonPropertyName("setup_url")]
    public string SetupUrl { get; init; } = string.Empty;

    [JsonPropertyName("release_page_url")]
    public string ReleasePageUrl { get; init; } = string.Empty;

    [JsonPropertyName("msix_url")]
    public string LegacyMsixUrl { get; init; } = string.Empty;

    [JsonPropertyName("release_notes")]
    public string ReleaseNotes { get; init; } = string.Empty;

    [JsonPropertyName("is_beta")]
    public bool IsBeta { get; init; }

    public string DisplayVersion =>
        IsBeta ? $"{Version}-beta" : Version;
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
    /// Download the setup installer to a temp file and return the path.
    /// Reports progress via the provided callback.
    /// </summary>
    Task<string?> DownloadUpdateAsync(
        ReleaseInfo release,
        IProgress<int> progress,
        CancellationToken ct = default);

    /// <summary>
    /// Launch the downloaded setup installer and exit MTGB.
    /// </summary>
    void InstallUpdate(string setupPath);

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
    private const long MinimumInstallerBytes = 1_000_000;

    private readonly IOptions<AppSettings> _settings;
    private readonly ISettingsStore _settingsStore;
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
        ISettingsStore settingsStore,
        ILogger<UpdateService> logger,
        HttpClient httpClient)
    {
        _settings = settings;
        _settingsStore = settingsStore;
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
                "Checking for updates at {Url}. " +
                "Include beta: {IncludeBeta}",
                GetUpdateUrl(),
                _settings.Value.Update.IncludeBeta);

            var response = await _httpClient
                .GetAsync(GetUpdateUrl(), ct);

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

            if (!TryGetDownloadUri(release, out _))
            {
                _logger.LogDebug(
                    "Update payload for v{Version} had no usable setup URL.",
                    release.DisplayVersion);
                return null;
            }

            _settings.Value.Update.LastChecked =
                DateTimeOffset.Now;
            _settingsStore.Save();

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
                == release.DisplayVersion)
            {
                _logger.LogDebug(
                    "Already notified for v{Version} — skipping.",
                    release.DisplayVersion);
                return null;
            }

            _logger.LogInformation(
                "Update available — v{Version}.",
                release.DisplayVersion);

            // Cache the release for the toast action handler
            _cachedRelease = release;

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
                $"MTGB-v{release.DisplayVersion}-x64-Setup.exe");

            _logger.LogInformation(
                "Downloading MTGB v{Version} to {Path}.",
                release.DisplayVersion, tempPath);

            if (!TryGetDownloadUri(release, out var downloadUri))
                return null;

            using var response = await _httpClient
                .GetAsync(
                    downloadUri,
                    HttpCompletionOption.ResponseHeadersRead,
                    ct);

            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers
                .ContentLength ?? -1L;
            var contentType = response.Content.Headers
                .ContentType
                ?.MediaType;

            if (IsHtmlResponse(contentType) ||
                totalBytes is > 0 and < MinimumInstallerBytes)
            {
                _logger.LogWarning(
                    "Refusing update download for v{Version}. " +
                    "Response looked wrong: content type {ContentType}, " +
                    "length {Length}.",
                    release.DisplayVersion,
                    contentType ?? "unknown",
                    totalBytes);
                return null;
            }

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
                release.DisplayVersion);
            return null;
        }
    }

    // ── Install ───────────────────────────────────────────────

    public void InstallUpdate(string setupPath)
    {
        _logger.LogInformation(
            "Launching setup installer — {Path}. " +
            "MTGB is exiting. Goodbye.",
            setupPath);

        System.Diagnostics.Process.Start(
            new System.Diagnostics.ProcessStartInfo
            {
                FileName = setupPath,
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

    private static string GetDownloadUrl(ReleaseInfo release) =>
        !string.IsNullOrWhiteSpace(release.SetupUrl)
            ? release.SetupUrl
            : release.LegacyMsixUrl;

    private string GetUpdateUrl() =>
        $"{UpdateUrl}?include_beta=" +
        $"{(_settings.Value.Update.IncludeBeta ? "1" : "0")}";

    private static bool TryGetDownloadUri(
        ReleaseInfo release,
        out Uri downloadUri)
    {
        downloadUri = null!;

        var url = GetDownloadUrl(release);
        if (!Uri.TryCreate(
                url,
                UriKind.Absolute,
                out var candidate))
            return false;

        if (candidate.Scheme is not "https" and not "http")
            return false;

        if (!candidate.AbsolutePath.EndsWith(
                ".exe",
                StringComparison.OrdinalIgnoreCase))
            return false;

        downloadUri = candidate;
        return true;
    }

    private static bool IsHtmlResponse(string? contentType) =>
        contentType?.Contains(
            "html",
            StringComparison.OrdinalIgnoreCase) == true;

    // ── API envelope ──────────────────────────────────────────

    private class ApiEnvelope
    {
        [JsonPropertyName("status")]
        public bool Status { get; init; }

        [JsonPropertyName("data")]
        public object? Data { get; init; }
    }
}
