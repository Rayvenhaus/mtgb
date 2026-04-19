using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MTGB.Config;

namespace MTGB.Services;

/// <summary>
/// MTGB Telemetry Worker.
/// Fires the daily anonymous telemetry ping once per day.
/// Runs as a .NET 8 BackgroundService — starts with the app,
/// stops cleanly when the app exits.
///
/// Only sends if the user has opted in.
/// Silently does nothing if telemetry is disabled.
/// The scribes are patient. They can wait.
/// </summary>
public class TelemetryWorker : BackgroundService
{
    private readonly ITelemetryService _telemetry;
    private readonly IOptions<AppSettings> _settings;
    private readonly ILogger<TelemetryWorker> _logger;

    // How often to check if a ping is due
    // Checks every hour — actual ping fires once per 23 hours
    // on the server side
    private static readonly TimeSpan CheckInterval =
        TimeSpan.FromHours(1);

    // Startup delay — let the app fully initialise
    // and the first poll cycle complete before pinging
    private static readonly TimeSpan StartupDelay =
        TimeSpan.FromMinutes(5);

    public TelemetryWorker(
        ITelemetryService telemetry,
        IOptions<AppSettings> settings,
        ILogger<TelemetryWorker> logger)
    {
        _telemetry = telemetry;
        _settings = settings;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        // Skip entirely if telemetry is disabled
        if (!_settings.Value.Telemetry.Enabled)
        {
            _logger.LogDebug(
                "Telemetry worker standing by. " +
                "The scribes respect your privacy.");
            return;
        }

        _logger.LogInformation(
            "Telemetry worker starting. " +
            "The scribes are sharpening their quills.");

        // Brief startup delay
        await Task.Delay(StartupDelay, stoppingToken);

        // Send an initial ping on startup — catches installs
        // that run for less than 24 hours before the first
        // scheduled ping would fire
        await _telemetry.SendPingAsync(stoppingToken);

        // Then check every hour
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(CheckInterval, stoppingToken);

                // Re-check opt-in status on every cycle —
                // user may have changed it in Settings
                if (!_settings.Value.Telemetry.Enabled)
                {
                    _logger.LogDebug(
                        "Telemetry disabled — " +
                        "skipping scheduled ping.");
                    continue;
                }

                await _telemetry.SendPingAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown — not an error
                break;
            }
            catch (Exception ex)
            {
                // Never crash MTGB over telemetry
                _logger.LogDebug(ex,
                    "Telemetry worker encountered an error. " +
                    "The scribes will try again next hour.");
            }
        }

        _logger.LogInformation(
            "Telemetry worker stopped. " +
            "The scribes have put down their quills.");
    }
}