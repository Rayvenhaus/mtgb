namespace MTGB.Config;

/// <summary>
/// Root application settings for MTGB.
/// Loaded from appsettings.json — never store secrets here.
/// Secrets live in Windows Credential Manager.
/// </summary>

public class AppSettings
{
    /// <summary>
    /// Whether the user has completed the MTGB Induction
    /// (Form MwA 621d/7 22). False means show the wizard on next launch.
    /// </summary>
    public bool Inducted { get; set; } = false;

    /// <summary>
    /// Anonymous install ID — generated once on first run.
    /// A random GUID. Never tied to a person or organisation.
    /// The Ministry knows only this. Nothing more.
    /// </summary>
    public string InstallId { get; set; } = string.Empty;

    /// <summary>
    /// Community map registration settings.
    /// </summary>
    public CommunityMapSettings CommunityMap { get; set; } = new();

    /// <summary>
    /// SimplyPrint organisation ID — visible in the API base URL.
    /// </summary>
    public int OrganisationId { get; set; }

    /// <summary>
    /// Authentication mode — ApiKey or OAuth2.
    /// </summary>
    [System.Text.Json.Serialization.JsonConverter(
        typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
    public AuthMode AuthMode { get; set; } = AuthMode.ApiKey;

    /// <summary>
    /// Polling configuration.
    /// </summary>
    public PollingSettings Polling { get; set; } = new();

    /// <summary>
    /// Webhook configuration.
    /// </summary>
    public WebhookSettings Webhook { get; set; } = new();

    /// <summary>
    /// Notification behaviour configuration.
    /// </summary>
    public NotificationSettings Notifications { get; set; } = new();

    /// <summary>
    /// Quiet hours configuration.
    /// </summary>
    public QuietHoursSettings QuietHours { get; set; } = new();

    /// <summary>
    /// Per-printer overrides keyed by printer ID.
    /// </summary>
    public Dictionary<int, PrinterSettings> Printers { get; set; } = new();

    /// <summary>
    /// UI and display preferences.
    /// </summary>
    public UiSettings Ui { get; set; } = new();

    /// <summary>
    /// Anonymous telemetry configuration.
    /// Default off — always opt-in, never opt-out.
    /// The scribes are patient.
    /// </summary>
    public TelemetrySettings Telemetry { get; set; } = new();

    /// <summary>
    /// Update check settings.
    /// </summary>
    public UpdateSettings Update { get; set; } = new();
}

public enum AuthMode
{
    ApiKey,
    OAuth2
}

public class PollingSettings
{
    /// <summary>
    /// Whether polling is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Polling interval in seconds. Minimum 10.
    /// </summary>
    public int IntervalSeconds { get; set; } = 30;
}

public class WebhookSettings
{
    /// <summary>
    /// Whether the local webhook receiver is enabled.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Local port to listen on for incoming webhook POSTs.
    /// </summary>
    public int Port { get; set; } = 7878;

    /// <summary>
    /// SimplyPrint webhook ID — stored after auto-registration.
    /// </summary>
    public int? RegisteredWebhookId { get; set; }
}

public class NotificationSettings
{
    /// <summary>
    /// Master kill switch — overrides everything.
    /// </summary>
    public bool GlobalMuteEnabled { get; set; } = false;

    /// <summary>
    /// Whether to group multiple events into a single toast.
    /// </summary>
    public bool GroupingEnabled { get; set; } = true;

    /// <summary>
    /// Time window in seconds to batch grouped notifications.
    /// </summary>
    public int GroupingWindowSeconds { get; set; } = 5;

    /// <summary>
    /// Enabled event type IDs — drawn from the EventDefinition registry.
    /// Empty means all events enabled.
    /// </summary>
    public List<string> EnabledEventIds { get; set; } = new();

    /// <summary>
    /// Whether to play a sound with notifications.
    /// </summary>
    public bool SoundEnabled { get; set; } = true;

    /// <summary>
    /// Whether to show action buttons on toasts (Pause, Cancel etc).
    /// </summary>
    public bool ActionButtonsEnabled { get; set; } = true;
}

public class QuietHoursSettings
{
    /// <summary>
    /// Whether quiet hours are enabled.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Start time in HH:mm format. e.g. "22:00"
    /// </summary>
    public string Start { get; set; } = "22:00";

    /// <summary>
    /// End time in HH:mm format. e.g. "08:00"
    /// </summary>
    public string End { get; set; } = "08:00";

    /// <summary>
    /// Whether critical alerts (printer offline, errors)
    /// still fire during quiet hours.
    /// </summary>
    public bool AllowCritical { get; set; } = true;
}

public class PrinterSettings
{
    /// <summary>
    /// Whether this printer is monitored at all.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Per-printer event overrides.
    /// Null means inherit global setting.
    /// </summary>
    public List<string>? EnabledEventIds { get; set; }

    /// <summary>
    /// Display name override — defaults to SimplyPrint printer name.
    /// </summary>
    public string? DisplayName { get; set; }
}

public class UiSettings
{
    /// <summary>
    /// Whether the tray icon is visible.
    /// </summary>
    public bool TrayIconEnabled { get; set; } = true;

    /// <summary>
    /// UI language code. e.g. "en-AU", "en-GB", "de-DE"
    /// </summary>
    public string Language { get; set; } = "en-AU";

    /// <summary>
    /// Whether to start MTGB with Windows.
    /// </summary>
    public bool StartWithWindows { get; set; } = true;
}

public class TelemetrySettings
{
    /// <summary>
    /// Whether anonymous telemetry is enabled.
    /// Default off — always opt-in, never opt-out.
    /// The scribes are patient.
    /// </summary>
    public bool Enabled { get; set; } = false;
}

public class CommunityMapSettings
{
    /// <summary>
    /// Whether registered on the community map.
    /// </summary>
    public bool Registered { get; set; } = false;

    /// <summary>
    /// ISO 3166-1 alpha-2 country code. e.g. "AU"
    /// </summary>
    public string? CountryCode { get; set; }

    /// <summary>
    /// Country name. e.g. "Australia"
    /// </summary>
    public string? CountryName { get; set; }

    /// <summary>
    /// State or territory name if applicable.
    /// Null for countries with no state system.
    /// </summary>
    public string? StateName { get; set; }

    /// <summary>
    /// Human readable display name.
    /// e.g. "Victoria, Australia" or "Fiji"
    /// </summary>
    public string? DisplayName { get; set; }
}

public class UpdateSettings
{
    /// <summary>
    /// When the last update check was performed.
    /// Null if never checked.
    /// </summary>
    public DateTimeOffset? LastChecked { get; set; }

    /// <summary>
    /// The last version we notified the user about.
    /// Prevents repeated toasts for the same version.
    /// </summary>
    public string? LastNotifiedVersion { get; set; }
}