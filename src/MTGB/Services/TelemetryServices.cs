using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MTGB.Config;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MTGB.Services;

// ── Telemetry payload ─────────────────────────────────────────

public record TelemetryPayload
{
    [JsonPropertyName("install_id")]
    public string InstallId { get; init; } = string.Empty;

    [JsonPropertyName("mtgb_version")]
    public string MtgbVersion { get; init; } = string.Empty;

    [JsonPropertyName("windows_version")]
    public string WindowsVersion { get; init; } = string.Empty;

    [JsonPropertyName("windows_build")]
    public string WindowsBuild { get; init; } = string.Empty;

    [JsonPropertyName("printer_count")]
    public int PrinterCount { get; init; }

    [JsonPropertyName("grouping_enabled")]
    public bool GroupingEnabled { get; init; }

    [JsonPropertyName("webhook_enabled")]
    public bool WebhookEnabled { get; init; }

    [JsonPropertyName("quiet_hours_enabled")]
    public bool QuietHoursEnabled { get; init; }

    [JsonPropertyName("sound_enabled")]
    public bool SoundEnabled { get; init; }

    [JsonPropertyName("poll_success_count")]
    public int PollSuccessCount { get; init; }

    [JsonPropertyName("poll_failure_count")]
    public int PollFailureCount { get; init; }

    [JsonPropertyName("toast_success_count")]
    public int ToastSuccessCount { get; init; }

    [JsonPropertyName("toast_failure_count")]
    public int ToastFailureCount { get; init; }

    [JsonPropertyName("printers")]
    public List<TelemetryPrinter> Printers { get; init; } = new();

    [JsonPropertyName("enabled_events")]
    public List<string> EnabledEvents { get; init; } = new();
}

public record TelemetryPrinter
{
    [JsonPropertyName("integration")]
    public string Integration { get; init; } = string.Empty;

    [JsonPropertyName("model_brand")]
    public string? ModelBrand { get; init; }

    [JsonPropertyName("model_name")]
    public string? ModelName { get; init; }
}

// ── Interface ─────────────────────────────────────────────────

public interface ITelemetryService
{
    /// <summary>
    /// Collect and send the daily telemetry ping.
    /// Silently fails if the endpoint is unreachable.
    /// Never crashes MTGB.
    /// </summary>
    Task SendPingAsync(CancellationToken ct = default);

    /// <summary>
    /// Record a successful poll cycle.
    /// </summary>
    void RecordPollSuccess();

    /// <summary>
    /// Record a failed poll cycle.
    /// </summary>
    void RecordPollFailure();

    /// <summary>
    /// Record a successful toast delivery.
    /// </summary>
    void RecordToastSuccess();

    /// <summary>
    /// Record a failed toast delivery.
    /// </summary>
    void RecordToastFailure();
}

// ── Implementation ────────────────────────────────────────────

/// <summary>
/// Collects and transmits anonymous telemetry to
/// community.myndworx.com once per day.
///
/// Only fires if the user has opted in.
/// Silently fails on network errors — never crashes MTGB.
/// The scribes are patient. They will try again tomorrow.
/// </summary>
public class TelemetryService : ITelemetryService
{
    private readonly IOptions<AppSettings> _settings;
    private readonly IStateDiffEngine _diffEngine;
    private readonly ILogger<TelemetryService> _logger;
    private readonly HttpClient _httpClient;

    private const string TelemetryUrl =
        "https://community.myndworx.com/mtgb/v1/telemetry";

    private static readonly JsonSerializerOptions JsonOptions =
        new() { PropertyNameCaseInsensitive = true };

    // ── Counters ──────────────────────────────────────────────
    // Thread-safe counters — incremented by PollingWorker
    // and NotificationManager, read once per daily ping
    private int _pollSuccessCount;
    private int _pollFailureCount;
    private int _toastSuccessCount;
    private int _toastFailureCount;

    public TelemetryService(
        IOptions<AppSettings> settings,
        IStateDiffEngine diffEngine,
        ILogger<TelemetryService> logger,
        HttpClient httpClient)
    {
        _settings = settings;
        _diffEngine = diffEngine;
        _logger = logger;
        _httpClient = httpClient;
    }

    // ── Counter recording ─────────────────────────────────────

    /// <inheritdoc/>
    public void RecordPollSuccess() =>
        Interlocked.Increment(ref _pollSuccessCount);

    /// <inheritdoc/>
    public void RecordPollFailure() =>
        Interlocked.Increment(ref _pollFailureCount);

    /// <inheritdoc/>
    public void RecordToastSuccess() =>
        Interlocked.Increment(ref _toastSuccessCount);

    /// <inheritdoc/>
    public void RecordToastFailure() =>
        Interlocked.Increment(ref _toastFailureCount);

    // ── Ping ──────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task SendPingAsync(
        CancellationToken ct = default)
    {
        var settings = _settings.Value;

        // Only send if user has opted in
        if (!settings.Telemetry.Enabled)
        {
            _logger.LogDebug(
                "Telemetry disabled — skipping ping. " +
                "The scribes respect your privacy.");
            return;
        }

        try
        {
            var installId = GetOrCreateInstallId();
            var snapshots = _diffEngine.GetAllSnapshots();

            // Build printer list — no names, no IDs
            var printers = snapshots.Values
                .Select(s => new TelemetryPrinter
                {
                    Integration = s.Integration ?? string.Empty,
                    ModelBrand = s.ModelBrand,
                    ModelName = s.ModelName
                })
                .ToList();

            // Snapshot and reset counters atomically
            var pollSuccess = Interlocked.Exchange(
                ref _pollSuccessCount, 0);
            var pollFailure = Interlocked.Exchange(
                ref _pollFailureCount, 0);
            var toastSuccess = Interlocked.Exchange(
                ref _toastSuccessCount, 0);
            var toastFailure = Interlocked.Exchange(
                ref _toastFailureCount, 0);

            var payload = new TelemetryPayload
            {
                InstallId = installId,
                MtgbVersion = GetVersion(),
                WindowsVersion = GetWindowsVersion(),
                WindowsBuild = GetWindowsBuild(),
                PrinterCount = snapshots.Count,
                GroupingEnabled = settings.Notifications
                                        .GroupingEnabled,
                WebhookEnabled = settings.Webhook.Enabled,
                QuietHoursEnabled = settings.QuietHours.Enabled,
                SoundEnabled = settings.Notifications
                                        .SoundEnabled,
                PollSuccessCount = pollSuccess,
                PollFailureCount = pollFailure,
                ToastSuccessCount = toastSuccess,
                ToastFailureCount = toastFailure,
                Printers = printers,
                EnabledEvents = settings.Notifications
                                        .EnabledEventIds
                                        .ToList()
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(
                json,
                Encoding.UTF8,
                "application/json");

            var response = await _httpClient.PostAsync(
                TelemetryUrl, content, ct);

            var responseJson = await response.Content
                .ReadAsStringAsync(ct);

            var result = JsonSerializer.Deserialize<ApiResponse>(
                responseJson, JsonOptions);

            if (result?.Status == true)
            {
                _logger.LogInformation(
                    "Telemetry ping sent. " +
                    "The scribes are grateful.");
            }
            else
            {
                _logger.LogWarning(
                    "Telemetry ping rejected — {Message}.",
                    result?.Message);
            }
        }
        catch (Exception ex)
        {
            // Never crash MTGB over telemetry
            _logger.LogDebug(ex,
                "Telemetry ping failed silently. " +
                "The scribes will try again tomorrow.");
        }
    }

    // ── Helpers ───────────────────────────────────────────────

    private string GetOrCreateInstallId()
    {
        if (!string.IsNullOrWhiteSpace(
            _settings.Value.InstallId))
            return _settings.Value.InstallId;

        var installId = Guid.NewGuid().ToString();
        _settings.Value.InstallId = installId;

        _logger.LogInformation(
            "Generated new anonymous install ID.");

        return installId;
    }

    private static string GetVersion() =>
        typeof(TelemetryService).Assembly
            .GetName().Version
            ?.ToString(3) ?? "0.0.0";

    private static string GetWindowsVersion()
    {
        try
        {
            return Environment.OSVersion.Version.ToString();
        }
        catch
        {
            return "Unknown";
        }
    }

    private static string GetWindowsBuild()
    {
        try
        {
            return Environment.OSVersion
                .Version.Build.ToString();
        }
        catch
        {
            return "Unknown";
        }
    }

    // ── API response model ────────────────────────────────────

    private class ApiResponse
    {
        [JsonPropertyName("status")]
        public bool Status { get; init; }

        [JsonPropertyName("message")]
        public string Message { get; init; } = string.Empty;
    }
}