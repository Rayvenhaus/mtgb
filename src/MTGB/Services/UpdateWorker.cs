using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MTGB.Config;

namespace MTGB.Services;

/// <summary>
/// MTGB Update Worker.
/// Checks for updates on startup and every 72 hours
/// during non-quiet hours.
/// Fires a toast if a newer version is available.
/// The Ministry keeps its software current.
/// </summary>
public class UpdateWorker : BackgroundService
{
    private readonly IUpdateService _updateService;
    private readonly INotificationManager _notifications;
    private readonly IOptions<AppSettings> _settings;
    private readonly ILogger<UpdateWorker> _logger;

    // 72 hours between checks
    private static readonly TimeSpan CheckInterval =
        TimeSpan.FromHours(72);

    // 2 minute startup delay — let everything initialise
    private static readonly TimeSpan StartupDelay =
        TimeSpan.FromMinutes(2);

    public UpdateWorker(
        IUpdateService updateService,
        INotificationManager notifications,
        IOptions<AppSettings> settings,
        ILogger<UpdateWorker> logger)
    {
        _updateService = updateService;
        _notifications = notifications;
        _settings = settings;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        _logger.LogDebug(
            "Update worker starting. " +
            "The Ministry checks for improvements.");

        // Brief startup delay
        await Task.Delay(StartupDelay, stoppingToken);

        // Check immediately on startup
        await CheckAndNotifyAsync(stoppingToken);

        // Then check every 72 hours
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(CheckInterval, stoppingToken);
                await CheckAndNotifyAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex,
                    "Update worker error — " +
                    "will retry in 72 hours.");
            }
        }

        _logger.LogDebug("Update worker stopped.");
    }

    private async Task CheckAndNotifyAsync(
        CancellationToken ct)
    {
        // Skip during quiet hours
        if (IsQuietHours())
        {
            _logger.LogDebug(
                "Skipping update check — quiet hours.");
            return;
        }

        // Skip if checked within 72 hours
        var lastChecked = _settings.Value.Update.LastChecked;
        if (lastChecked.HasValue &&
            DateTimeOffset.Now - lastChecked.Value
                < CheckInterval)
        {
            _logger.LogDebug(
                "Skipping update check — " +
                "checked {Hours:F0}h ago.",
                (DateTimeOffset.Now - lastChecked.Value)
                    .TotalHours);
            return;
        }

        var release = await _updateService
            .CheckForUpdateAsync(ct);

        if (release is null) return;

        // Deliver update toast
        await _notifications.ProcessEventAsync(
            new DetectedEvent
            {
                EventId = "update.available",
                PrinterId = 0,
                PrinterName = "MTGB",
                IsCritical = false,
                JobFilename = release.Version,
                DetectedAt = DateTimeOffset.Now
            }, ct);

        // Record that we notified for this version
        _settings.Value.Update.LastNotifiedVersion =
            release.Version;
    }

    private bool IsQuietHours()
    {
        var qh = _settings.Value.QuietHours;
        if (!qh.Enabled) return false;

        if (!TimeOnly.TryParse(qh.Start, out var start) ||
            !TimeOnly.TryParse(qh.End, out var end))
            return false;

        var now = TimeOnly.FromDateTime(DateTime.Now);

        return start <= end
            ? now >= start && now <= end
            : now >= start || now <= end;
    }
}