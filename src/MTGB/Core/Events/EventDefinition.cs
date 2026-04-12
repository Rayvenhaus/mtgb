namespace MTGB.Core.Events;

/// <summary>
/// Defines a single monitorable event type in MTGB.
/// Stored as data, not code — new events can be added 
/// without recompiling.
/// </summary>
public class EventDefinition
{
    /// <summary>
    /// Unique identifier. Matches SimplyPrint webhook event 
    /// name where applicable. e.g. "job.finished"
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Human readable display name for the settings UI.
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// Longer description shown in settings.
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// Where this event comes from.
    /// </summary>
    public required EventSource Source { get; init; }

    /// <summary>
    /// Whether this is a critical event that bypasses quiet hours
    /// when AllowCritical is true.
    /// </summary>
    public bool IsCritical { get; init; } = false;

    /// <summary>
    /// Whether this event type is enabled by default.
    /// </summary>
    public bool EnabledByDefault { get; init; } = true;

    /// <summary>
    /// Optional category for grouping in the settings UI.
    /// </summary>
    public string Category { get; init; } = "General";
}

public enum EventSource
{
    /// <summary>
    /// Event received via SimplyPrint webhook POST.
    /// </summary>
    Webhook,

    /// <summary>
    /// Event detected locally by comparing polled state snapshots.
    /// </summary>
    Polling
}

/// <summary>
/// The master registry of all known MTGB event definitions.
/// This is the single source of truth for the settings pick list.
/// </summary>
public static class EventRegistry
{
    public static readonly IReadOnlyList<EventDefinition> All = new List<EventDefinition>
    {
        // ── Print job events (webhook) ────────────────────────────
        new()
        {
            Id = "job.started",
            DisplayName = "Print started",
            Description = "Fires when a print job begins.",
            Source = EventSource.Webhook,
            Category = "Print jobs",
            EnabledByDefault = true
        },
        new()
        {
            Id = "job.finished",
            DisplayName = "Print finished",
            Description = "Fires when a print job completes successfully.",
            Source = EventSource.Webhook,
            Category = "Print jobs",
            EnabledByDefault = true
        },
        new()
        {
            Id = "job.failed",
            DisplayName = "Print failed",
            Description = "Fires when a print job fails.",
            Source = EventSource.Webhook,
            Category = "Print jobs",
            IsCritical = true,
            EnabledByDefault = true
        },
        new()
        {
            Id = "job.cancelled",
            DisplayName = "Print cancelled",
            Description = "Fires when a print job is cancelled.",
            Source = EventSource.Webhook,
            Category = "Print jobs",
            EnabledByDefault = true
        },
        new()
        {
            Id = "job.paused",
            DisplayName = "Print paused",
            Description = "Fires when a print job is paused.",
            Source = EventSource.Webhook,
            Category = "Print jobs",
            EnabledByDefault = true
        },
        new()
        {
            Id = "job.resumed",
            DisplayName = "Print resumed",
            Description = "Fires when a paused print job resumes.",
            Source = EventSource.Webhook,
            Category = "Print jobs",
            EnabledByDefault = false
        },

        // ── Queue events (webhook) ────────────────────────────────
        new()
        {
            Id = "queue.added",
            DisplayName = "Item added to queue",
            Description = "Fires when a new item is added to the print queue.",
            Source = EventSource.Webhook,
            Category = "Queue",
            EnabledByDefault = false
        },
        new()
        {
            Id = "queue.emptied",
            DisplayName = "Queue emptied",
            Description = "Fires when the print queue is cleared.",
            Source = EventSource.Webhook,
            Category = "Queue",
            EnabledByDefault = false
        },

        // ── Printer state events (polling) ────────────────────────
        new()
        {
            Id = "printer.offline",
            DisplayName = "Printer went offline",
            Description = "Fires when a printer loses connection.",
            Source = EventSource.Polling,
            Category = "Printer",
            IsCritical = true,
            EnabledByDefault = true
        },
        new()
        {
            Id = "printer.online",
            DisplayName = "Printer came online",
            Description = "Fires when a printer reconnects.",
            Source = EventSource.Polling,
            Category = "Printer",
            EnabledByDefault = false
        },

        // ── Progress milestones (polling) ─────────────────────────
        new()
        {
            Id = "progress.25",
            DisplayName = "Print at 25%",
            Description = "Fires when a print reaches 25% completion.",
            Source = EventSource.Polling,
            Category = "Progress",
            EnabledByDefault = false
        },
        new()
        {
            Id = "progress.50",
            DisplayName = "Print at 50%",
            Description = "Fires when a print reaches 50% completion.",
            Source = EventSource.Polling,
            Category = "Progress",
            EnabledByDefault = false
        },
        new()
        {
            Id = "progress.75",
            DisplayName = "Print at 75%",
            Description = "Fires when a print reaches 75% completion.",
            Source = EventSource.Polling,
            Category = "Progress",
            EnabledByDefault = true
        },

        // ── Temperature alerts (polling) ──────────────────────────
        new()
        {
            Id = "temp.nozzle.low",
            DisplayName = "Nozzle temp dropped",
            Description = "Fires when nozzle temperature drops below threshold.",
            Source = EventSource.Polling,
            Category = "Temperature",
            IsCritical = true,
            EnabledByDefault = false
        },
        new()
        {
            Id = "temp.bed.low",
            DisplayName = "Bed temp dropped",
            Description = "Fires when bed temperature drops below threshold.",
            Source = EventSource.Polling,
            Category = "Temperature",
            IsCritical = true,
            EnabledByDefault = false
        },

        // ── Filament (webhook) ────────────────────────────────────
        new()
        {
            Id = "filament.low",
            DisplayName = "Low filament warning",
            Description = "Fires when the filament sensor detects low filament.",
            Source = EventSource.Polling,
            Category = "Filament",
            IsCritical = true,
            EnabledByDefault = true
        }
    };

    /// <summary>
    /// Look up a single event definition by ID.
    /// </summary>
    public static EventDefinition? GetById(string id) =>
        All.FirstOrDefault(e => e.Id == id);

    /// <summary>
    /// Get all events in a given category.
    /// </summary>
    public static IEnumerable<EventDefinition> GetByCategory(string category) =>
        All.Where(e => e.Category == category);

    /// <summary>
    /// Get all distinct categories in display order.
    /// </summary>
    public static IEnumerable<string> Categories =>
        All.Select(e => e.Category).Distinct();
}