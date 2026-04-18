using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MTGB.Config;
using MTGB.Core.Events;

namespace MTGB.Services;

// ── Printer snapshot ──────────────────────────────────────────────

/// <summary>
/// Point-in-time snapshot of a printer's state.
/// The diff engine compares successive snapshots to detect changes.
/// </summary>
public record PrinterSnapshot
{
    public int PrinterId { get; init; }
    public string PrinterName { get; init; } = string.Empty;
    public bool Online { get; init; }
    public string State { get; init; } = string.Empty;
    public int? ActiveJobId { get; init; }
    public string? ActiveJobFilename { get; init; }
    public double? JobPercentage { get; init; }
    public int? JobTimeRemaining { get; init; }
    public double? NozzleTemp { get; init; }
    public double? BedTemp { get; init; }
    public bool FilamentSensorTriggered { get; init; }
    public DateTimeOffset CapturedAt { get; init; }
}

// ── Fired event ───────────────────────────────────────────────────

/// <summary>
/// A single event detected by the diff engine, ready for the 
/// notification manager to process.
/// </summary>
public record DetectedEvent
{
    public required string EventId { get; init; }
    public required int PrinterId { get; init; }
    public required string PrinterName { get; init; }
    public string? JobFilename { get; init; }
    public double? JobPercentage { get; init; }
    public int? JobTimeRemaining { get; init; }
    public bool IsCritical { get; init; }
    public DateTimeOffset DetectedAt { get; init; } = DateTimeOffset.Now;
}

// ── Offline tracking ──────────────────────────────────────────────

/// <summary>
/// Tracks a printer that has gone offline but not yet been confirmed.
/// Counts poll cycles to implement the 3-poll grace period.
/// </summary>
internal record OfflinePendingEntry
{
    public required DateTimeOffset FirstSeenOffline { get; init; }
    public required PrinterSnapshot SnapshotBeforeOffline { get; init; }
    public int PollCount { get; set; } = 0;
    public int BounceCount { get; set; } = 0;
}

// ── Online tracking ───────────────────────────────────────────────

/// <summary>
/// Tracks a printer that has come back online but not yet been confirmed.
/// Counts poll cycles to implement the 3-poll confirmation period.
/// </summary>
internal record OnlinePendingEntry
{
    public required DateTimeOffset FirstSeenOnline { get; init; }
    public required PrinterSnapshot SnapshotBeforeOnline { get; init; }
    public int PollCount { get; set; } = 0;
}

// ── Interface ─────────────────────────────────────────────────────

public interface IStateDiffEngine
{
    /// <summary>
    /// Process a fresh batch of printer data from the API.
    /// Returns any events detected by comparing to the last snapshot.
    /// </summary>
    IReadOnlyList<DetectedEvent> ProcessUpdate(
        IReadOnlyList<PrinterData> freshData);

    /// <summary>
    /// Process a single webhook event received from SimplyPrint.
    /// Returns the detected event if it maps to a known event type,
    /// null if it should be ignored.
    /// </summary>
    DetectedEvent? ProcessWebhookEvent(
        string eventName,
        int printerId,
        string printerName,
        string? jobFilename = null);

    /// <summary>
    /// Get the last known snapshot for a printer.
    /// </summary>
    PrinterSnapshot? GetSnapshot(int printerId);

    /// <summary>
    /// Get all current snapshots.
    /// </summary>
    IReadOnlyDictionary<int, PrinterSnapshot> GetAllSnapshots();
}

// ── Implementation ────────────────────────────────────────────────

/// <summary>
/// The brain of MTGB.
/// Holds in-memory snapshots of every printer's last known state.
/// Compares incoming data against snapshots and fires events on changes.
/// Handles milestone deduplication per job so 75% only fires once.
///
/// Offline/online behaviour:
///   - A printer must be offline for 3 consecutive polls before
///     printer.offline fires. This prevents bounce floods.
///   - A printer must be online for 3 consecutive polls before
///     printer.online fires.
///   - On confirmed reconnect, only state changes vs the
///     pre-offline snapshot are reported — no duplicate alerts
///     for conditions that existed before the outage.
///   - Bounce count is tracked. If a printer bounces repeatedly
///     a separate instability warning is fired.
/// </summary>
public class StateDiffEngine : IStateDiffEngine
{
    private readonly IOptions<AppSettings> _settings;
    private readonly ILogger<StateDiffEngine> _logger;

    // ── Snapshot store ────────────────────────────────────────────
    // Keyed by printer ID
    private readonly Dictionary<int, PrinterSnapshot> _snapshots = new();

    // ── Offline grace period tracking ─────────────────────────────
    // Printers that have gone offline but not yet confirmed
    // Key: printerId
    private readonly Dictionary<int, OfflinePendingEntry> _pendingOffline
        = new();

    // ── Online confirmation tracking ──────────────────────────────
    // Printers that have come back online but not yet confirmed
    // Key: printerId
    private readonly Dictionary<int, OnlinePendingEntry> _pendingOnline
        = new();

    // ── Bounce instability warning cooldown ───────────────────────
    // Key: printerId, Value: when the last instability warning fired
    private readonly Dictionary<int, DateTimeOffset> _instabilityWarnings
        = new();

    // How many poll cycles before offline/online is confirmed
    private const int OfflineConfirmationPolls = 3;
    private const int OnlineConfirmationPolls = 3;

    // How many bounces before we warn about instability
    private const int BounceWarningThreshold = 3;

    // Cooldown between instability warnings per printer
    private static readonly TimeSpan InstabilityWarningCooldown =
        TimeSpan.FromMinutes(30);

    // ── Milestone tracking ────────────────────────────────────────
    // Tracks which progress milestones have already fired per job ID
    // Key: "{printerId}:{jobId}:{milestoneEventId}"
    private readonly HashSet<string> _firedMilestones = new();

    // ── Temperature alert cooldown ────────────────────────────────
    // Key: "{printerId}:{eventId}", Value: when it last fired
    private readonly Dictionary<string, DateTimeOffset> _tempAlertCooldowns
        = new();

    private static readonly TimeSpan TempAlertCooldown =
        TimeSpan.FromMinutes(10);

    // ── State sets ────────────────────────────────────────────────

    private static readonly HashSet<string> PrintingStates =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "printing", "printing_completing"
        };

    private static readonly HashSet<string> PausedStates =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "paused", "pausing"
        };

    private static readonly HashSet<string> ErrorStates =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "error", "printer_error", "offline"
        };

    // ── Progress milestones ───────────────────────────────────────
    private static readonly Dictionary<string, double> Milestones =
        new()
        {
            { "progress.25", 25.0 },
            { "progress.50", 50.0 },
            { "progress.75", 75.0 }
        };

    public StateDiffEngine(
        IOptions<AppSettings> settings,
        ILogger<StateDiffEngine> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    // ── Public API ────────────────────────────────────────────────

    /// <inheritdoc/>
    public IReadOnlyList<DetectedEvent> ProcessUpdate(
        IReadOnlyList<PrinterData> freshData)
    {
        var events = new List<DetectedEvent>();

        foreach (var printerData in freshData)
        {
            if (printerData.Printer is null) continue;

            var fresh = BuildSnapshot(printerData);
            var previous = _snapshots.GetValueOrDefault(printerData.Id);

            var detected = CompareSnapshots(previous, fresh);
            events.AddRange(detected);

            // Always update the snapshot regardless of events
            _snapshots[printerData.Id] = fresh;
        }

        // Check for printers that disappeared from the response
        var freshIds = freshData.Select(p => p.Id).ToHashSet();
        foreach (var missingId in _snapshots.Keys.Except(freshIds).ToList())
        {
            var snapshot = _snapshots[missingId];
            if (snapshot.Online)
            {
                _logger.LogWarning(
                    "Printer {PrinterId} ({Name}) disappeared " +
                    "from API response.",
                    missingId, snapshot.PrinterName);
            }
        }

        return events;
    }

    /// <inheritdoc/>
    public DetectedEvent? ProcessWebhookEvent(
        string eventName,
        int printerId,
        string printerName,
        string? jobFilename = null)
    {
        var definition = EventRegistry.GetById(eventName);
        if (definition is null)
        {
            _logger.LogDebug(
                "Received unknown webhook event '{EventName}' — ignoring.",
                eventName);
            return null;
        }

        _logger.LogInformation(
            "Webhook event '{EventName}' for printer {PrinterId} ({Name}).",
            eventName, printerId, printerName);

        return new DetectedEvent
        {
            EventId = eventName,
            PrinterId = printerId,
            PrinterName = printerName,
            JobFilename = jobFilename,
            IsCritical = definition.IsCritical
        };
    }

    /// <inheritdoc/>
    public PrinterSnapshot? GetSnapshot(int printerId) =>
        _snapshots.GetValueOrDefault(printerId);

    /// <inheritdoc/>
    public IReadOnlyDictionary<int, PrinterSnapshot> GetAllSnapshots() =>
        _snapshots;

    // ── Diff logic ────────────────────────────────────────────────

    private IEnumerable<DetectedEvent> CompareSnapshots(
        PrinterSnapshot? previous,
        PrinterSnapshot fresh)
    {
        var events = new List<DetectedEvent>();

        // ── First seen ────────────────────────────────────────────
        if (previous is null)
        {
            _logger.LogInformation(
                "First snapshot for printer {Id} ({Name}) — " +
                "state: {State}.",
                fresh.PrinterId, fresh.PrinterName, fresh.State);
            return events;
        }

        // ── Online / offline with grace period ────────────────────
        events.AddRange(
            ProcessOnlineState(previous, fresh));

        // If printer is in pending offline or pending online state,
        // skip all other event detection — we don't know the true
        // state yet.
        if (_pendingOffline.ContainsKey(fresh.PrinterId) ||
            _pendingOnline.ContainsKey(fresh.PrinterId))
            return events;

        // If printer is currently offline, skip job/temp detection
        if (!fresh.Online)
            return events;

        // ── Print job state changes ───────────────────────────────
        var wasIdle = !IsActiveState(previous.State);
        var nowActive = IsActiveState(fresh.State);

        // Job started (idle/offline → printing)
        if (wasIdle && nowActive && fresh.ActiveJobId.HasValue)
            events.Add(MakeEvent("job.started", fresh));

        // Job finished (printing → not printing, no active job)
        if (PrintingStates.Contains(previous.State) &&
            !IsActiveState(fresh.State) &&
            previous.ActiveJobId.HasValue &&
            !fresh.ActiveJobId.HasValue)
            events.Add(MakeEvent("job.finished", fresh,
                jobFilename: previous.ActiveJobFilename));

        // Job paused
        if (PrintingStates.Contains(previous.State) &&
            PausedStates.Contains(fresh.State))
            events.Add(MakeEvent("job.paused", fresh));

        // Job resumed
        if (PausedStates.Contains(previous.State) &&
            PrintingStates.Contains(fresh.State))
            events.Add(MakeEvent("job.resumed", fresh));

        // Job failed / error
        if (!ErrorStates.Contains(previous.State) &&
            ErrorStates.Contains(fresh.State) &&
            previous.ActiveJobId.HasValue)
            events.Add(MakeEvent("job.failed", fresh,
                isCritical: true,
                jobFilename: fresh.ActiveJobFilename));

        // ── Progress milestones ───────────────────────────────────
        if (fresh.ActiveJobId.HasValue &&
            fresh.JobPercentage.HasValue &&
            PrintingStates.Contains(fresh.State))
        {
            foreach (var (eventId, threshold) in Milestones)
            {
                var milestoneKey =
                    $"{fresh.PrinterId}:{fresh.ActiveJobId}:{eventId}";

                var crossedThreshold =
                    fresh.JobPercentage >= threshold &&
                    (previous.JobPercentage ?? 0) < threshold;

                if (crossedThreshold &&
                    !_firedMilestones.Contains(milestoneKey))
                {
                    _firedMilestones.Add(milestoneKey);
                    events.Add(MakeEvent(eventId, fresh,
                        jobFilename: fresh.ActiveJobFilename));
                }
            }
        }

        // ── Filament sensor ───────────────────────────────────────
        if (!previous.FilamentSensorTriggered &&
            fresh.FilamentSensorTriggered)
            events.Add(MakeEvent("filament.low", fresh,
                isCritical: true));

        // ── Temperature alerts ────────────────────────────────────
        if (PrintingStates.Contains(fresh.State))
        {
            CheckTempAlert("temp.nozzle.low",
                fresh, fresh.NozzleTemp, events);
            CheckTempAlert("temp.bed.low",
                fresh, fresh.BedTemp, events);
        }

        return events;
    }

    // ── Online / offline grace period logic ───────────────────────

    private IEnumerable<DetectedEvent> ProcessOnlineState(
        PrinterSnapshot previous,
        PrinterSnapshot fresh)
    {
        var events = new List<DetectedEvent>();
        var printerId = fresh.PrinterId;

        // ── Printer has gone offline ──────────────────────────────
        if (previous.Online && !fresh.Online)
        {
            if (!_pendingOffline.ContainsKey(printerId))
            {
                // First poll offline — start the grace period
                _pendingOffline[printerId] = new OfflinePendingEntry
                {
                    FirstSeenOffline = DateTimeOffset.Now,
                    SnapshotBeforeOffline = previous
                };

                _logger.LogDebug(
                    "Printer {Name} went offline — " +
                    "starting {Polls}-poll grace period.",
                    fresh.PrinterName, OfflineConfirmationPolls);
            }

            return events;
        }

        // ── Printer is still offline ──────────────────────────────
        if (!previous.Online && !fresh.Online)
        {
            if (_pendingOffline.TryGetValue(
                printerId, out var offlineEntry))
            {
                offlineEntry.PollCount++;

                _logger.LogDebug(
                    "Printer {Name} still offline — " +
                    "poll {Poll}/{Required}.",
                    fresh.PrinterName,
                    offlineEntry.PollCount,
                    OfflineConfirmationPolls);

                if (offlineEntry.PollCount >= OfflineConfirmationPolls)
                {
                    // Confirmed offline — fire the event
                    _pendingOffline.Remove(printerId);
                    _logger.LogInformation(
                        "Printer {Name} confirmed offline " +
                        "after {Polls} polls.",
                        fresh.PrinterName, OfflineConfirmationPolls);
                    events.Add(MakeEvent("printer.offline", fresh,
                        isCritical: true));
                }
            }

            return events;
        }

        // ── Printer has come back online ──────────────────────────
        if (!previous.Online && fresh.Online)
        {
            // If it was in pending offline (bounced back quickly)
            // increment bounce count but don't fire offline event
            if (_pendingOffline.TryGetValue(
                printerId, out var offlineEntry))
            {
                offlineEntry.BounceCount++;
                _pendingOffline.Remove(printerId);

                _logger.LogDebug(
                    "Printer {Name} bounced back online " +
                    "(bounce #{Count}) — suppressing offline event.",
                    fresh.PrinterName, offlineEntry.BounceCount);

                // Check if we should warn about instability
                events.AddRange(
                    CheckInstability(fresh, offlineEntry.BounceCount));

                return events;
            }

            // Came back from a confirmed offline — start online
            // confirmation grace period
            if (!_pendingOnline.ContainsKey(printerId))
            {
                _pendingOnline[printerId] = new OnlinePendingEntry
                {
                    FirstSeenOnline = DateTimeOffset.Now,
                    SnapshotBeforeOnline = previous
                };

                _logger.LogDebug(
                    "Printer {Name} back online — " +
                    "starting {Polls}-poll confirmation period.",
                    fresh.PrinterName, OnlineConfirmationPolls);
            }

            return events;
        }

        // ── Printer is still online ───────────────────────────────
        if (previous.Online && fresh.Online)
        {
            // Clear any pending offline entry (shouldn't happen
            // but defensive cleanup)
            _pendingOffline.Remove(printerId);

            if (_pendingOnline.TryGetValue(
                printerId, out var onlineEntry))
            {
                onlineEntry.PollCount++;

                _logger.LogDebug(
                    "Printer {Name} still online — " +
                    "poll {Poll}/{Required}.",
                    fresh.PrinterName,
                    onlineEntry.PollCount,
                    OnlineConfirmationPolls);

                if (onlineEntry.PollCount >= OnlineConfirmationPolls)
                {
                    // Confirmed back online
                    _pendingOnline.Remove(printerId);

                    _logger.LogInformation(
                        "Printer {Name} confirmed back online " +
                        "after {Polls} polls.",
                        fresh.PrinterName, OnlineConfirmationPolls);

                    // Fire printer.online event
                    events.Add(MakeEvent("printer.online", fresh));

                    // Now diff against the pre-offline snapshot to
                    // detect only genuine state changes — don't
                    // re-fire conditions that existed before the outage
                    var preOfflineSnapshot =
                        onlineEntry.SnapshotBeforeOnline;

                    events.AddRange(
                        DiffPostReconnect(preOfflineSnapshot, fresh));
                }
            }
        }

        return events;
    }

    /// <summary>
    /// Diffs the current snapshot against the pre-offline snapshot
    /// to detect only genuine changes that occurred during the outage.
    /// Conditions that existed before going offline are not re-fired.
    /// </summary>
    private IEnumerable<DetectedEvent> DiffPostReconnect(
        PrinterSnapshot preOffline,
        PrinterSnapshot fresh)
    {
        var events = new List<DetectedEvent>();

        // Filament sensor — only fire if it wasn't already triggered
        // before the outage
        if (!preOffline.FilamentSensorTriggered &&
            fresh.FilamentSensorTriggered)
            events.Add(MakeEvent("filament.low", fresh,
                isCritical: true));

        // Job state — if a job was running before and is now gone,
        // we don't know if it finished or failed — fire job.finished
        // as it's the safer assumption (user can check history)
        if (preOffline.ActiveJobId.HasValue &&
            !fresh.ActiveJobId.HasValue &&
            PrintingStates.Contains(preOffline.State))
            events.Add(MakeEvent("job.finished", fresh,
                jobFilename: preOffline.ActiveJobFilename));

        // If printer came back in an error state when it wasn't
        // before — something went wrong during the outage
        if (!ErrorStates.Contains(preOffline.State) &&
            ErrorStates.Contains(fresh.State))
            events.Add(MakeEvent("job.failed", fresh,
                isCritical: true));

        return events;
    }

    /// <summary>
    /// Checks if a printer has bounced enough times to warrant
    /// an instability warning. Respects a cooldown to prevent
    /// warning floods.
    /// </summary>
    private IEnumerable<DetectedEvent> CheckInstability(
        PrinterSnapshot fresh,
        int bounceCount)
    {
        if (bounceCount < BounceWarningThreshold)
            yield break;

        var lastWarned = _instabilityWarnings
            .GetValueOrDefault(fresh.PrinterId);

        if (DateTimeOffset.Now - lastWarned < InstabilityWarningCooldown)
            yield break;

        _instabilityWarnings[fresh.PrinterId] = DateTimeOffset.Now;

        _logger.LogWarning(
            "Printer {Name} has bounced {Count} times — " +
            "firing instability warning.",
            fresh.PrinterName, bounceCount);

        yield return MakeEvent("printer.offline", fresh,
            isCritical: true);
    }

    // ── Temperature alerts ────────────────────────────────────────

    private void CheckTempAlert(
        string eventId,
        PrinterSnapshot fresh,
        double? currentTemp,
        List<DetectedEvent> events)
    {
        if (!currentTemp.HasValue) return;

        var settings = _settings.Value;

        // Only fire if the event type is enabled
        var enabled = settings.Notifications.EnabledEventIds
            .Contains(eventId);
        if (!enabled) return;

        // Temp dropped below 50°C during a print — something is wrong
        if (currentTemp < 50.0)
        {
            var cooldownKey = $"{fresh.PrinterId}:{eventId}";
            var lastFired = _tempAlertCooldowns
                .GetValueOrDefault(cooldownKey);

            if (DateTimeOffset.Now - lastFired > TempAlertCooldown)
            {
                _tempAlertCooldowns[cooldownKey] = DateTimeOffset.Now;
                events.Add(MakeEvent(eventId, fresh, isCritical: true));
            }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────

    private static bool IsActiveState(string state) =>
        PrintingStates.Contains(state) || PausedStates.Contains(state);

    private static PrinterSnapshot BuildSnapshot(PrinterData data)
    {
        var printer = data.Printer!;
        var job = printer.Job;
        var nozzleTemp = printer.Temps?.Current?.Tool?.FirstOrDefault();
        var bedTemp = printer.Temps?.Current?.Bed;

        return new PrinterSnapshot
        {
            PrinterId = data.Id,
            PrinterName = printer.Name,
            Online = printer.Online,
            State = printer.State,
            ActiveJobId = job?.Id,
            ActiveJobFilename = job?.Filename,
            JobPercentage = job?.Percentage,
            JobTimeRemaining = job?.Time?.Remaining,
            NozzleTemp = nozzleTemp,
            BedTemp = bedTemp,
            FilamentSensorTriggered = printer.FilamentSensorTriggered,
            CapturedAt = DateTimeOffset.Now
        };
    }

    private static DetectedEvent MakeEvent(
        string eventId,
        PrinterSnapshot snapshot,
        bool isCritical = false,
        string? jobFilename = null)
    {
        var definition = EventRegistry.GetById(eventId);

        return new DetectedEvent
        {
            EventId = eventId,
            PrinterId = snapshot.PrinterId,
            PrinterName = snapshot.PrinterName,
            JobFilename = jobFilename ?? snapshot.ActiveJobFilename,
            JobPercentage = snapshot.JobPercentage,
            JobTimeRemaining = snapshot.JobTimeRemaining,
            IsCritical = isCritical || (definition?.IsCritical ?? false)
        };
    }

    /// <summary>
    /// Clean up milestone tracking for completed jobs.
    /// Called when a job finishes to prevent the HashSet growing forever.
    /// </summary>
    public void CleanupJobMilestones(int printerId, int jobId)
    {
        var prefix = $"{printerId}:{jobId}:";
        _firedMilestones.RemoveWhere(k => k.StartsWith(prefix));
    }
}