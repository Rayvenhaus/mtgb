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
/// </summary>
public class StateDiffEngine : IStateDiffEngine
{
    private readonly IOptions<AppSettings> _settings;
    private readonly ILogger<StateDiffEngine> _logger;

    // Keyed by printer ID
    private readonly Dictionary<int, PrinterSnapshot> _snapshots = new();

    // Tracks which progress milestones have already fired per job ID
    // Key: "{printerId}:{jobId}:{milestoneEventId}"
    private readonly HashSet<string> _firedMilestones = new();

    // Temperature alert cooldown — prevents repeated alerts
    // Key: "{printerId}:{eventId}", Value: when it last fired
    private readonly Dictionary<string, DateTimeOffset> _tempAlertCooldowns
        = new();

    private static readonly TimeSpan TempAlertCooldown =
        TimeSpan.FromMinutes(10);

    // Printer states that indicate active printing
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

    // Progress milestones we track — maps event ID to threshold
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
        // (could indicate they went offline at the API level)
        var freshIds = freshData.Select(p => p.Id).ToHashSet();
        foreach (var missingId in _snapshots.Keys.Except(freshIds).ToList())
        {
            var snapshot = _snapshots[missingId];
            if (snapshot.Online)
            {
                _logger.LogWarning(
                    "Printer {PrinterId} ({Name}) disappeared from API response.",
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
                "First snapshot for printer {Id} ({Name}) — state: {State}.",
                fresh.PrinterId, fresh.PrinterName, fresh.State);
            return events;
        }

        // ── Online / offline ──────────────────────────────────────
        if (previous.Online && !fresh.Online)
            events.Add(MakeEvent("printer.offline", fresh,
                isCritical: true));

        if (!previous.Online && fresh.Online)
            events.Add(MakeEvent("printer.online", fresh));

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

    private void CheckTempAlert(
        string eventId,
        PrinterSnapshot fresh,
        double? currentTemp,
        List<DetectedEvent> events)
    {
        if (!currentTemp.HasValue) return;

        var settings = _settings.Value;
        var printerSettings = settings.Printers
            .GetValueOrDefault(fresh.PrinterId);

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