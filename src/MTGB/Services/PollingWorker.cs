using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MTGB.Config;
using System.Net.Http;

namespace MTGB.Services;

/// <summary>
/// MTGB Polling Worker.
/// The heartbeat of the application.
/// Wakes up on a configurable interval, asks SimplyPrint how all
/// printers are doing, hands the result to the state diff engine,
/// and passes any detected events to the notification manager.
///
/// Runs as a .NET 8 BackgroundService — starts with the app,
/// stops cleanly when the app exits.
/// </summary>
public class PollingWorker : BackgroundService
{
    private readonly ISimplyPrintApiClient _apiClient;
    private readonly IStateDiffEngine _diffEngine;
    private readonly INotificationManager _notificationManager;
    private readonly IAuthService _authService;
    private readonly ITelemetryService _telemetry;  
    private readonly IOptions<AppSettings> _settings;
    private readonly ILogger<PollingWorker> _logger;

    // Tracks consecutive failures for backoff logic
    private int _consecutiveFailures = 0;
    private const int MaxConsecutiveFailures = 5;

    // Backoff intervals — progressive delay on repeated failures
    private static readonly TimeSpan[] BackoffIntervals =
    {
        TimeSpan.FromSeconds(30),
        TimeSpan.FromSeconds(60),
        TimeSpan.FromSeconds(120),
        TimeSpan.FromSeconds(300),
        TimeSpan.FromSeconds(600)
    };

    public PollingWorker(
        ISimplyPrintApiClient apiClient,
        IStateDiffEngine diffEngine,
        INotificationManager notificationManager,
        IAuthService authService,
        ITelemetryService telemetry,
        IOptions<AppSettings> settings,
        ILogger<PollingWorker> logger)
    {
        _apiClient = apiClient;
        _diffEngine = diffEngine;
        _notificationManager = notificationManager;
        _authService = authService;
        _telemetry = telemetry;
        _settings = settings;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Polling worker starting. " +
            "Interval: {Interval}s. " +
            "It goes Bing.",
            _settings.Value.Polling.IntervalSeconds);

        // Brief startup delay — let the app finish initialising
        // before hammering the API
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            // Skip polling if not authenticated
            if (!_authService.IsAuthenticated)
            {
                _logger.LogDebug(
                    "Not authenticated — skipping poll cycle.");
                await WaitForNextCycle(stoppingToken);
                continue;
            }

            // Skip polling if disabled in settings
            if (!_settings.Value.Polling.Enabled)
            {
                _logger.LogDebug(
                    "Polling disabled in settings — " +
                    "standing by.");
                await Task.Delay(
                    TimeSpan.FromSeconds(10), stoppingToken);
                continue;
            }

            await PollAsync(stoppingToken);
            await WaitForNextCycle(stoppingToken);
        }

        _logger.LogInformation(
            "Polling worker stopped. " +
            "The Ministry has been informed.");
    }

    private async Task PollAsync(CancellationToken ct)
    {
        try
        {
            _logger.LogDebug("Poll cycle starting.");

            var printers = await _apiClient.GetPrintersAsync(ct);

            if (!printers.Any())
            {
                _logger.LogDebug(
                    "No printers returned — nothing to diff.");
                ResetFailureCount();
                return;
            }

            _logger.LogDebug(
                "Poll returned {Count} printer(s).",
                printers.Count);

            // Hand to the diff engine
            var events = _diffEngine.ProcessUpdate(printers);

            if (events.Any())
            {
                _logger.LogInformation(
                    "Poll detected {Count} event(s).",
                    events.Count);

                await _notificationManager
                    .ProcessEventsAsync(events, ct);
            }

            _telemetry.RecordPollSuccess();
            ResetFailureCount();
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown — not an error
        }
        catch (HttpRequestException ex)
        {
            HandleFailure(
                ex,
                "Network error during poll cycle. " +
                "SimplyPrint appears to be unavailable.");
        }
        catch (Exception ex)
        {
            HandleFailure(
                ex,
                "Unexpected error during poll cycle.");
        }
    }

    private async Task WaitForNextCycle(CancellationToken ct)
    {
        TimeSpan delay;

        if (_consecutiveFailures > 0)
        {
            // Progressive backoff — the more failures, 
            // the longer we wait
            var backoffIndex = Math.Min(
                _consecutiveFailures - 1,
                BackoffIntervals.Length - 1);

            delay = BackoffIntervals[backoffIndex];

            _logger.LogWarning(
                "Backing off for {Seconds}s after " +
                "{Failures} consecutive failure(s). " +
                "The Ministry is displeased.",
                delay.TotalSeconds,
                _consecutiveFailures);
        }
        else
        {
            delay = TimeSpan.FromSeconds(
                Math.Max(
                    _settings.Value.Polling.IntervalSeconds,
                    10)); // Minimum 10 seconds — be a good API citizen
        }

        try
        {
            await Task.Delay(delay, ct);
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
    }

    private void HandleFailure(Exception ex, string message)
    {
        _consecutiveFailures++;

        if (_consecutiveFailures >= MaxConsecutiveFailures)
        {
            _logger.LogError(ex,
                "{Message} " +
                "({Failures} consecutive failures). " +
                "We apologise for the inconvenience.",
                message,
                _consecutiveFailures);
        }
        else
        {
            _logger.LogWarning(ex,
                "{Message} (failure {Count} of {Max} " +
                "before escalation).",
                message,
                _consecutiveFailures,
                MaxConsecutiveFailures);
        }

        _telemetry.RecordPollFailure();
    }

    private void ResetFailureCount()
    {
        if (_consecutiveFailures > 0)
        {
            _logger.LogInformation(
                "Poll cycle succeeded after {Failures} failure(s). " +
                "The Ministry is cautiously optimistic.",
                _consecutiveFailures);
        }

        _consecutiveFailures = 0;
    }
}