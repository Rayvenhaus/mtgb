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
    /// SimplyPrint organisation ID — visible in the API base URL.
    /// </summary>
    public int OrganisationId { get; set; }

    /// <summary>
    /// Authentication mode — ApiKey or OAuth2.
    /// </summary>
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