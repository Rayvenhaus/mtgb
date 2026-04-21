using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Toolkit.Uwp.Notifications;
using MTGB.Config;
using MTGB.Core.Events;
using System.IO;
using System.Text.Json;

namespace MTGB.Services;

// ── Notification history entry ────────────────────────────────────

/// <summary>
/// A single entry in the MTGB notification history log.
/// Written regardless of whether the toast was suppressed.
/// </summary>
public record NotificationHistoryEntry
{
    public required string EventId { get; init; }
    public required string EventDisplayName { get; init; }
    public required int PrinterId { get; init; }
    public required string PrinterName { get; init; }
    public string? JobFilename { get; init; }
    public double? JobPercentage { get; init; }
    public bool WasSuppressed { get; init; }
    public string? SuppressReason { get; init; }
    public bool IsCritical { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;
}

// ── Suppression result ────────────────────────────────────────────

internal record SuppressionResult(bool Suppressed, string? Reason);

// ── Interface ─────────────────────────────────────────────────────

public interface INotificationManager
{
    /// <summary>
    /// Process a detected event through the rules engine
    /// and deliver a toast if not suppressed.
    /// </summary>
    Task ProcessEventAsync(
        DetectedEvent detectedEvent,
        CancellationToken ct = default);

    /// <summary>
    /// Process a batch of detected events, respecting grouping settings.
    /// </summary>
    Task ProcessEventsAsync(
        IReadOnlyList<DetectedEvent> events,
        CancellationToken ct = default);

    /// <summary>
    /// Get the full notification history.
    /// </summary>
    IReadOnlyList<NotificationHistoryEntry> GetHistory();

    /// <summary>
    /// Clear the notification history.
    /// </summary>
    void ClearHistory();
}

// ── Implementation ────────────────────────────────────────────────

/// <summary>
/// Processes detected events through the MTGB rules engine
/// and delivers native Windows 11 toast notifications.
///
/// Rules engine order:
///   1. Global mute
///   2. Quiet hours (critical events bypass if AllowCritical)
///   3. Per-printer enabled
///   4. Per-event type enabled
///   5. Deduplication
///   6. Grouping
///   7. Deliver toast + log to history
/// </summary>
public class NotificationManager : INotificationManager, IDisposable
{
    private readonly IOptions<AppSettings> _settings;
    private readonly ILogger<NotificationManager> _logger;
    private readonly ITelemetryService _telemetry;

    // In-memory history log — persisted to disk on change
    private readonly List<NotificationHistoryEntry> _history = new();
    private readonly object _historyLock = new();

    // Deduplication — tracks recently fired event keys
    // Key: "{printerId}:{eventId}", Value: when it last fired
    private readonly Dictionary<string, DateTimeOffset> _recentlyFired
        = new();

    private static readonly TimeSpan DeduplicationWindow =
        TimeSpan.FromSeconds(30);

    // Grouping buffer — collects events within the grouping window
    private readonly List<DetectedEvent> _groupBuffer = new();
    private DateTimeOffset? _groupWindowStart;
    private readonly object _groupLock = new();
    private readonly System.Threading.Timer _groupFlushTimer;

    // History file location
    private static readonly string HistoryFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MTGB", "history.json");

    // Notification sound
    private static readonly string SoundFilePath = Path.Combine(
        AppContext.BaseDirectory,
        "Assets", "mtgbNotification.wav");

    public NotificationManager(
        IOptions<AppSettings> settings,
        ILogger<NotificationManager> logger,
        ITelemetryService telemetry)
    {
        _settings = settings;
        _logger = logger;
        _telemetry = telemetry;

        // Register toast notification activator
        ToastNotificationManagerCompat.OnActivated +=
            OnToastActivated;

        // Independent flush timer — checks the group buffer every
        // second regardless of polling cycle timing.
        // This ensures grouped toasts always fire within
        // GroupingWindowSeconds of the first buffered event.
        _groupFlushTimer = new System.Threading.Timer(
            callback: _ => _ = FlushGroupBufferIfReadyAsync(
                CancellationToken.None),
            state: null,
            dueTime: TimeSpan.FromSeconds(1),
            period: TimeSpan.FromSeconds(1));

        // Load existing history from disk
        LoadHistory();

        // Ensure history directory exists
        Directory.CreateDirectory(Path.GetDirectoryName(
            HistoryFilePath)!);
    }

    // ── Public API ────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task ProcessEventAsync(
        DetectedEvent detectedEvent,
        CancellationToken ct = default)
    {
        await ProcessEventsAsync(
            new[] { detectedEvent }, ct);
    }

    /// <inheritdoc/>
    public async Task ProcessEventsAsync(
        IReadOnlyList<DetectedEvent> events,
        CancellationToken ct = default)
    {
        if (!events.Any()) return;

        var settings = _settings.Value;

        foreach (var evt in events)
        {
            _logger.LogDebug(
                "Processing event '{EventId}' for {Printer}",
                evt.EventId, evt.PrinterName);

            var suppression = EvaluateRules(evt, settings);

            _logger.LogDebug(
                "Event '{EventId}' suppressed: {Suppressed} — {Reason}",
                evt.EventId, suppression.Suppressed,
                suppression.Reason ?? "not suppressed");

            LogToHistory(evt, suppression);

            if (suppression.Suppressed)
                continue;

            if (settings.Notifications.GroupingEnabled)
                BufferForGrouping(evt);
            else
                await DeliverToastAsync(evt, ct);
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<NotificationHistoryEntry> GetHistory()
    {
        lock (_historyLock)
            return _history.AsReadOnly();
    }

    /// <inheritdoc/>
    public void ClearHistory()
    {
        lock (_historyLock)
        {
            _history.Clear();
            PersistHistory();
        }
    }

    public void Dispose()
    {
        _groupFlushTimer.Dispose();
        ToastNotificationManagerCompat.History.Clear();
    }

    // ── Rules engine ──────────────────────────────────────────────

    private SuppressionResult EvaluateRules(
        DetectedEvent evt,
        AppSettings settings)
    {
        var notifications = settings.Notifications;

        // 1. Global mute — overrides everything
        if (notifications.GlobalMuteEnabled)
            return Suppress("Global mute enabled");

        // 2. Quiet hours
        //    Critical events bypass quiet hours if AllowCritical is set
        if (settings.QuietHours.Enabled && IsWithinQuietHours(settings.QuietHours))
        {
            if (!evt.IsCritical || !settings.QuietHours.AllowCritical)
                return Suppress("Within quiet hours");
        }

        // 3. Per-printer enabled
        var printerSettings = settings.Printers
            .GetValueOrDefault(evt.PrinterId);

        if (printerSettings is { Enabled: false })
            return Suppress($"Printer {evt.PrinterId} is disabled");

        // 4. Per-event type enabled
        //    Check per-printer override first, fall back to global
        var enabledEventIds = printerSettings?.EnabledEventIds
            ?? notifications.EnabledEventIds;

        if (enabledEventIds.Any() &&
            !enabledEventIds.Contains(evt.EventId))
            return Suppress($"Event type '{evt.EventId}' not enabled");

        // 5. Deduplication
        var dedupeKey = $"{evt.PrinterId}:{evt.EventId}";
        if (_recentlyFired.TryGetValue(dedupeKey, out var lastFired))
        {
            if (DateTimeOffset.Now - lastFired < DeduplicationWindow)
                return Suppress("Duplicate within deduplication window");
        }

        _recentlyFired[dedupeKey] = DateTimeOffset.Now;
        return new SuppressionResult(false, null);
    }

    private static bool IsWithinQuietHours(QuietHoursSettings qh)
    {
        if (!TimeOnly.TryParse(qh.Start, out var start) ||
            !TimeOnly.TryParse(qh.End, out var end))
            return false;

        var now = TimeOnly.FromDateTime(DateTime.Now);

        // Handle overnight ranges e.g. 22:00 → 08:00
        return start <= end
            ? now >= start && now <= end
            : now >= start || now <= end;
    }

    // ── Toast delivery ────────────────────────────────────────────

    private async Task DeliverToastAsync(
        DetectedEvent evt,
        CancellationToken ct = default)
    {
        await Task.Run(() =>
        {
            try
            {
                var definition = EventRegistry.GetById(evt.EventId);
                var title = BuildTitle(evt, definition);
                var body = BuildBody(evt);

                var builder = new ToastContentBuilder()
                    .AddAppLogoOverride(
                        new Uri(
                            Path.Combine(
                                AppContext.BaseDirectory,
                                "Assets", "mtgb.ico")))
                    .AddText(title)
                    .AddText(body)
                    .AddAttributionText("MTGB — It goes Bing");

                // Action buttons for active print states
                if (_settings.Value.Notifications.ActionButtonsEnabled)
                    AddActionButtons(builder, evt);

                builder.Show(toast =>
                {
                    toast.Tag = $"{evt.PrinterId}:{evt.EventId}";
                    toast.Group = "MTGB";
                });

                _telemetry.RecordToastSuccess();
                PlayNotificationSound();

                _logger.LogInformation(
                    "Toast delivered: '{EventId}' for {Printer}.",
                    evt.EventId, evt.PrinterName);
            }
            catch (Exception ex)
            {
                _telemetry.RecordToastFailure();
                _logger.LogError(ex,
                    "Failed to deliver toast for event '{EventId}'.",
                    evt.EventId);
            }
        }, ct);
    }

    private static string BuildTitle(
        DetectedEvent evt,
        EventDefinition? definition)
    {
        var displayName = definition?.DisplayName ?? evt.EventId;

        return evt.EventId switch
        {
            "job.finished" => $"{evt.PrinterName} — Print finished",
            "job.failed" => $"{evt.PrinterName} — Print failed",
            "job.started" => $"{evt.PrinterName} — Print started",
            "job.paused" => $"{evt.PrinterName} — Print paused",
            "job.resumed" => $"{evt.PrinterName} — Print resumed",
            "job.cancelled" => $"{evt.PrinterName} — Print cancelled",
            "printer.offline" => $"{evt.PrinterName} — Offline",
            "printer.online" => $"{evt.PrinterName} — Back online",
            "filament.low" => $"{evt.PrinterName} — Low filament",
            "progress.25" => $"{evt.PrinterName} — 25% complete",
            "progress.50" => $"{evt.PrinterName} — 50% complete",
            "progress.75" => $"{evt.PrinterName} — 75% complete",
            "temp.nozzle.low" => $"{evt.PrinterName} — Nozzle temp alert",
            "temp.bed.low" => $"{evt.PrinterName} — Bed temp alert",
            "update.available" => $"MTGB update available",
            _ => $"{evt.PrinterName} — {displayName}"
        };
    }

    // ── Flavour message pools ─────────────────────────────────────────

    /// <summary>
    /// Rotating pool of failure messages.
    /// Helpful. Pythonesque. Occasionally accusatory.
    /// </summary>
    private static readonly string[] FailureMessages =
    {
    "Someone may have forgotten the bol for the Spag bol again.",
    "It's not dead, it's resting. (It's dead.)",
    "This print has ceased to be. It has expired and gone to meet its maker.",
    "We're no strangers to failure here. The Ministry has been notified.",
    "It is merely pining for the build plate.",
    "Nobody expects a print failure. And yet.",
    "Strange women lying in ponds distributing filament " +
        "is no basis for a print farm.",
    "The print is no more. It has run down the curtain " +
        "and joined the choir invisible.",
    "We apologise for the inconvenience.",
    "Have you tried not printing spaghetti?",
    "Your filament has achieved a state of pure abstract art. " +
        "We suggest a gallery.",
    "The Knights Who Say 'Nii' are displeased with this outcome.",
    "It's just a flesh wound. (It is not just a flesh wound.)",
    "On the bright side, spaghetti is delicious. " +
        "This is not edible. Probably.",
    "Run away! Run away!",
    "This was a triumph. We're making a note here: it was not.",
    "The Redundant Department of Redundancy has filed " +
        "a redundant failure report.",
    "What is the airspeed velocity of an unladen print head? " +
        "Irrelevant. It crashed.",
    "She turned me into a failed print. I got better.",
    "We didn't start the fire. Actually, please check the printer."
};

    private static readonly string[] OfflineMessages =
    {
    "It has gone to a better place. Possibly the shed.",
    "The printer appears to have made a break for it.",
    "Gone. Reduced to atoms. Or just unplugged.",
    "No one is home. We checked. Twice. In triplicate.",
    "The Ministry of Silly Printers reports: absent without leave.",
    "It's offline. Have you tried bribing it with fresh filament?",
    "Printer? What printer? We see no printer.",
    "It was here a moment ago. Honest.",
    "This is an ex-printer. (Temporarily. Hopefully.)"
};

    private static readonly string[] FilamentMessages =
    {
    "The spool giveth, and the spool taketh away.",
    "Running on fumes and sheer optimism.",
    "Filament levels: critical. Dignity levels: also critical.",
    "The Knights Who Say 'Nii' demand more filament immediately.",
    "We're going to need a bigger spool.",
    "Almost out. Much like our patience with this.",
    "Warning: filament poverty detected. Please consult your spool drawer."
};

    private static readonly string[] SuccessMessages =
    {
    "And there was much rejoicing.",
    "It is done. The Ministry approves. Barely.",
    "A perfect print. The Redundant Department has been notified. Twice.",
    "Success! We weren't entirely sure that would work.",
    "Finished. Pop the kettle on.",
    "Beautiful. Somebody's printer actually did its job.",
    "The print gods are pleased. For now."
};

    private static readonly Random _random = new();

    private static string PickRandom(string[] pool) =>
        pool[_random.Next(pool.Length)];

    private static string BuildBody(DetectedEvent evt)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(evt.JobFilename))
            parts.Add(evt.JobFilename);

        if (evt.JobPercentage.HasValue)
            parts.Add($"{evt.JobPercentage:F0}% complete");

        if (evt.JobTimeRemaining.HasValue && evt.JobTimeRemaining > 0)
        {
            var remaining = TimeSpan.FromSeconds(evt.JobTimeRemaining.Value);
            parts.Add(remaining.TotalHours >= 1
                ? $"{remaining.TotalHours:F0}h {remaining.Minutes}m remaining"
                : $"{remaining.Minutes}m remaining");
        }

        // ── Flavour line ──────────────────────────────────────────────
        var flavour = evt.EventId switch
        {
            "job.failed" => PickRandom(FailureMessages),
            "printer.offline" => PickRandom(OfflineMessages),
            "filament.low" => PickRandom(FilamentMessages),
            "job.finished" => PickRandom(SuccessMessages),
            "job.started" => "It has begun. Try not to watch it too closely.",
            "job.paused" => "Taking five. The print, not you. " +
                                 "Although, same.",
            "job.cancelled" => "Cancelled. The Redundant Department " +
                                 "of Redundancy has been notified.",
            "progress.25" => "One quarter done. Three quarters " +
                                 "of the anxiety remains.",
            "progress.50" => "Halfway. This is either going brilliantly " +
                                 "or you haven't looked yet.",
            "progress.75" => "Nearly there. Do not jinx it. " +
                                 "We mean it. Step away from the printer.",
            "temp.nozzle.low" => "The nozzle has gone cold. " +
                                 "Much like our confidence.",
            "temp.bed.low" => "Bed temperature dropping. " +
                                 "Tuck it in. Put the kettle on.",
            "update.available" => $"Version {evt.JobFilename} is ready to install. " +
                                    "Click Update to download and install now.",
            _ => null
        };

        if (flavour is not null)
            parts.Add(flavour);

        return parts.Any()
            ? string.Join("\n", parts)
            : string.Empty;
    }

    private static void AddActionButtons(
        ToastContentBuilder builder,
        DetectedEvent evt)
    {
        switch (evt.EventId)
        {
            case "job.started":
            case "job.resumed":
                builder
                    .AddButton(new ToastButton()
                        .SetContent("Pause")
                        .AddArgument("action", "pause")
                        .AddArgument("printerId",
                            evt.PrinterId.ToString()))
                    .AddButton(new ToastButton()
                        .SetContent("Cancel")
                        .AddArgument("action", "cancel")
                        .AddArgument("printerId",
                            evt.PrinterId.ToString()));
                break;

            case "job.paused":
                builder
                    .AddButton(new ToastButton()
                        .SetContent("Resume")
                        .AddArgument("action", "resume")
                        .AddArgument("printerId",
                            evt.PrinterId.ToString()))
                    .AddButton(new ToastButton()
                        .SetContent("Cancel")
                        .AddArgument("action", "cancel")
                        .AddArgument("printerId",
                            evt.PrinterId.ToString()));
                break;

            case "update.available":
                builder.AddButton(new ToastButton()
                    .SetContent("Update")
                    .AddArgument("action", "update")
                    .AddArgument("version", evt.JobFilename));
                break;
        }
    }

    private void PlayNotificationSound()
    {
        if (!_settings.Value.Notifications.SoundEnabled) return;
        if (_settings.Value.Notifications.GlobalMuteEnabled) return;

        try
        {
            if (!File.Exists(SoundFilePath))
            {
                _logger.LogDebug(
                    "Notification sound file not found at {Path}.",
                    SoundFilePath);
                return;
            }

            using var player = new System.Media.SoundPlayer(
                SoundFilePath);
            player.Play();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to play notification sound. " +
                "The Ministry mourns the silence.");
        }
    }

    // ── Grouping ──────────────────────────────────────────────────

    private void BufferForGrouping(DetectedEvent evt)
    {
        lock (_groupLock)
        {
            _groupWindowStart ??= DateTimeOffset.Now;
            _groupBuffer.Add(evt);
        }
    }

    private async Task FlushGroupBufferIfReadyAsync(
        CancellationToken ct)
    {
        List<DetectedEvent> toFlush;

        lock (_groupLock)
        {
            if (!_groupBuffer.Any()) return;

            var windowSeconds = _settings.Value.Notifications
                .GroupingWindowSeconds;
            var elapsed = DateTimeOffset.Now - _groupWindowStart;

            if (elapsed < TimeSpan.FromSeconds(windowSeconds)) return;

            toFlush = new List<DetectedEvent>(_groupBuffer);
            _groupBuffer.Clear();
            _groupWindowStart = null;
        }

        try
        {
            if (toFlush.Count == 1)
                await DeliverToastAsync(toFlush[0], ct);
            else
                await DeliverGroupedToastAsync(toFlush, ct);
        }
        catch (Exception ex)
        {
            // Must not throw — this runs on a timer thread.
            // The individual deliver methods log their own errors
            // but a rethrow here would kill the timer permanently.
            _logger.LogError(ex,
                "Unhandled error in group flush.");
        }
    }

    private async Task DeliverGroupedToastAsync(
    List<DetectedEvent> events,
    CancellationToken ct)
    {
        await Task.Run(() =>
        {
            try
            {
                var printerNames = events
                    .Select(e => e.PrinterName)
                    .Distinct()
                    .Take(3)
                    .ToList();

                var title = printerNames.Count == 1
                    ? $"{printerNames[0]} — {events.Count} events"
                    : $"{events.Count} events across " +
                      $"{printerNames.Count} printers";

                // Windows toast allows max 4 text elements total.
                // Title occupies slot 1 — leaving 3 slots for detail lines.
                // Build the summary lines, capping at 3.
                var allLines = events
                    .Select(e =>
                    {
                        var def = EventRegistry.GetById(e.EventId);
                        return $"{e.PrinterName}: " +
                               $"{def?.DisplayName ?? e.EventId}";
                    })
                    .ToList();

                List<string> toastLines;

                if (allLines.Count <= 3)
                {
                    toastLines = allLines;
                }
                else
                {
                    // Take 2 lines + overflow indicator
                    toastLines = allLines.Take(2).ToList();
                    toastLines.Add(
                        $"...and {allLines.Count - 2} more");
                }

                var builder = new ToastContentBuilder()
                    .AddText(title)
                    .AddAttributionText("MTGB — It goes Bing");

                foreach (var line in toastLines)
                    builder.AddText(line);

                builder.Show(toast =>
                {
                    toast.Tag = "mtgb-grouped";
                    toast.Group = "MTGB";
                });

                PlayNotificationSound();

                _logger.LogInformation(
                    "Grouped toast delivered for {Count} events.",
                    events.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to deliver grouped toast.");
            }
        }, ct);
    }

    // ── Toast activation (action buttons) ────────────────────────

    private void OnToastActivated(
        ToastNotificationActivatedEventArgsCompat args)
    {
        var arguments = ToastArguments.Parse(args.Argument);

        if (!arguments.TryGetValue("action", out var action) ||
            !arguments.TryGetValue("printerId", out var printerIdStr) ||
            !int.TryParse(printerIdStr, out var printerId))
            return;

        _logger.LogInformation(
            "Toast action '{Action}' triggered for printer {PrinterId}.",
            action, printerId);

        // Fire and forget — the API client handles errors internally
        _ = action switch
        {
            "pause" => HandleToastActionAsync(printerId, "pause"),
            "resume" => HandleToastActionAsync(printerId, "resume"),
            "cancel" => HandleToastActionAsync(printerId, "cancel"),
            _ => Task.CompletedTask
        };
    }

    private async Task HandleToastActionAsync(
        int printerId, string action)
    {
        // Resolved via service locator pattern here since toast
        // activation happens outside the normal DI scope
        _logger.LogInformation(
            "Handling toast action '{Action}' for printer {PrinterId}.",
            action, printerId);

        // Action execution is wired in TrayIcon.cs where the
        // API client is available — this event bubbles up there
        ToastActionRequested?.Invoke(this,
            new ToastActionEventArgs(printerId, action));
    }

    // ── Toast action event ────────────────────────────────────────

    public event EventHandler<ToastActionEventArgs>? ToastActionRequested;

    // ── History ───────────────────────────────────────────────────

    private void LogToHistory(
        DetectedEvent evt,
        SuppressionResult suppression)
    {
        var definition = EventRegistry.GetById(evt.EventId);

        var entry = new NotificationHistoryEntry
        {
            EventId = evt.EventId,
            EventDisplayName = definition?.DisplayName ?? evt.EventId,
            PrinterId = evt.PrinterId,
            PrinterName = evt.PrinterName,
            JobFilename = evt.JobFilename,
            JobPercentage = evt.JobPercentage,
            WasSuppressed = suppression.Suppressed,
            SuppressReason = suppression.Reason,
            IsCritical = evt.IsCritical
        };

        lock (_historyLock)
        {
            _history.Insert(0, entry);

            // Cap history at 1000 entries
            if (_history.Count > 1000)
                _history.RemoveRange(1000, _history.Count - 1000);

            PersistHistory();
        }
    }

    private void LoadHistory()
    {
        try
        {
            if (!File.Exists(HistoryFilePath)) return;

            var json = File.ReadAllText(HistoryFilePath);
            var entries = JsonSerializer
                .Deserialize<List<NotificationHistoryEntry>>(json);

            if (entries is null) return;

            lock (_historyLock)
                _history.AddRange(entries);

            _logger.LogInformation(
                "Loaded {Count} history entries from disk.",
                _history.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to load notification history.");
        }
    }

    private void PersistHistory()
    {
        try
        {
            var json = JsonSerializer.Serialize(_history,
                new JsonSerializerOptions { WriteIndented = false });
            File.WriteAllText(HistoryFilePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to persist notification history.");
        }
    }

    private static SuppressionResult Suppress(string reason) =>
        new(true, reason);
}

// ── Toast action event args ───────────────────────────────────────

public class ToastActionEventArgs : EventArgs
{
    public int PrinterId { get; }
    public string Action { get; }

    public ToastActionEventArgs(int printerId, string action)
    {
        PrinterId = printerId;
        Action = action;
    }
}