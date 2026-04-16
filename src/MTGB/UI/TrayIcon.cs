using Hardcodet.Wpf.TaskbarNotification;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MTGB.Config;
using MTGB.Services;
using MTGB.UI;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;

namespace MTGB.UI;

/// <summary>
/// MTGB System Tray Icon.
/// 
/// Left click  → Flyout panel slides up from taskbar.
/// Right click → Minimal context menu (Mute, Settings, Exit).
/// 
/// Owns the NotifyIcon lifetime, routes toast actions to the API,
/// and keeps the tray icon state in sync with the printer farm.
/// </summary>
public class TrayIcon : IDisposable
{
    private readonly IServiceProvider _services;
    private readonly ISimplyPrintApiClient _apiClient;
    private readonly INotificationManager _notificationManager;
    private readonly IStateDiffEngine _diffEngine;
    private readonly IOptions<AppSettings> _settings;
    private readonly ILogger<TrayIcon> _logger;

    private TaskbarIcon? _taskbarIcon;
    private FlyoutWindow? _flyout;
    private bool _disposed;

    // Icon states — each maps to a different .ico file
    // reflecting overall farm status at a glance
    private static readonly string IconIdle =
        "Assets/mtgb.ico";
    private static readonly string IconPrinting =
        "Assets/mtgb.ico";
    private static readonly string IconAlert =
        "Assets/mtgb.ico";

    // Cache of flavour text per printer — keyed by printerId
    // Value is (state when picked, flavour text)
    private readonly Dictionary<int, (string state, string text)>
        _flavourCache = new();

    // ── Flavour text pools ────────────────────────────────────────
    // These mirror the NotificationManager pools —
    // same voice, same energy, in the tray hover tooltips

    private static readonly string[] IdleFlavour =
    {
        "All quiet on the western front. Suspiciously quiet.",
        "Nothing happening. The Ministry is at ease.",
        "Idle. Like a civil servant on a Friday afternoon.",
        "Ready and waiting. Unlike the bus.",
        "Standing by. Stoically. Without complaint.",
        "All printers present and accounted for. Mostly.",
        "Peaceful. Don't jinx it."
    };

    private static readonly string[] PrintingFlavour =
    {
        "Something is being made. We're choosing to be optimistic.",
        "Layers are happening. This is fine.",
        "It prints. Therefore it is.",
        "The machines are doing their thing. Stand back.",
        "Progress is being made. The Ministry is cautiously hopeful.",
        "Printing. Try not to watch. It makes it nervous.",
        "Layer by layer. Like bureaucracy, but faster."
    };

    private static readonly string[] PausedFlavour =
    {
        "Taking five. The print, not you. Although, same.",
        "Paused. Someone blinked at it.",
        "On hold. Like your tax return.",
        "Resting. It has earned this.",
        "Temporarily suspended. By order of the Ministry.",
        "Paused mid-layer. The suspense is considerable."
    };

    private static readonly string[] FailedFlavour =
    {
        "It is not dead, it's resting. (It's dead.)",
        "Someone may have forgotten the bol for the Spag bol.",
        "This is fine. Everything is fine. It is not fine.",
        "The Ministry has filed a strongly worded report.",
        "Run away! Run away!",
        "We apologise for the inconvenience.",
        "Strange things have occurred. Investigation ongoing."
    };

    private static readonly string[] OfflineFlavour =
    {
        "It has gone to a better place. Possibly the shed.",
        "Gone. Reduced to atoms. Or just unplugged.",
        "The printer appears to have made a break for it.",
        "No one is home. We checked. Twice. In triplicate.",
        "Absent without leave. The Ministry is displeased."
    };

    private static readonly Random _random = new();

    private static string Pick(string[] pool) =>
        pool[_random.Next(pool.Length)];

    public TrayIcon(
        IServiceProvider services,
        ISimplyPrintApiClient apiClient,
        INotificationManager notificationManager,
        IStateDiffEngine diffEngine,
        IOptions<AppSettings> settings,
        ILogger<TrayIcon> logger)
    {
        _services = services;
        _apiClient = apiClient;
        _notificationManager = notificationManager;
        _diffEngine = diffEngine;
        _settings = settings;
        _logger = logger;
    }

    // ── Initialisation ────────────────────────────────────────────

    public void Initialise()
    {
        _taskbarIcon = new TaskbarIcon
        {
            IconSource = LoadIcon(IconIdle),
            ToolTipText = "MTGB — It goes Bing",
            Visibility = Visibility.Visible
        };

        // Left click — open flyout
        _taskbarIcon.TrayLeftMouseDown += OnLeftClick;

        // Right click — minimal context menu
        _taskbarIcon.ContextMenu = BuildContextMenu();

        // Subscribe to toast action buttons
        if (_notificationManager is NotificationManager nm)
            nm.ToastActionRequested += OnToastActionRequested;

        _logger.LogInformation(
            "Tray icon initialised. " +
            "MTGB is watching. Always watching.");

        // Start updating the icon state
        _ = StartIconStateLoopAsync();
    }

    // ── Icon state loop ───────────────────────────────────────────

    /// <summary>
    /// Periodically updates the tray icon and tooltip
    /// to reflect the current farm state.
    /// Runs on a background thread, updates UI on dispatcher.
    /// </summary>
    private async Task StartIconStateLoopAsync()
    {
        while (!_disposed)
        {
            try
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(5));

                Application.Current?.Dispatcher.Invoke(
                    UpdateIconState);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Icon state loop error.");
            }
        }
    }

    private void UpdateIconState()
    {
        if (_taskbarIcon is null) return;

        var snapshots = _diffEngine.GetAllSnapshots();

        if (!snapshots.Any())
        {
            SetIcon(IconIdle, "MTGB — No printers found");
            return;
        }

        var hasAlert = snapshots.Values.Any(s =>
            s.State.Equals("error",
                StringComparison.OrdinalIgnoreCase) ||
            s.State.Equals("printer_error",
                StringComparison.OrdinalIgnoreCase) ||
            !s.Online);

        var hasPrinting = snapshots.Values.Any(s =>
            s.State.Equals("printing",
                StringComparison.OrdinalIgnoreCase));

        var printingCount = snapshots.Values.Count(s =>
            s.State.Equals("printing",
                StringComparison.OrdinalIgnoreCase));

        var offlineCount = snapshots.Values.Count(s =>
            !s.Online);

        if (hasAlert)
        {
            var tooltip = offlineCount > 0
                ? $"MTGB — {offlineCount} printer(s) offline. " +
                  Pick(OfflineFlavour)
                : $"MTGB — Attention required. " +
                  Pick(FailedFlavour);

            SetIcon(IconAlert, tooltip);
        }
        else if (hasPrinting)
        {
            var tooltip = printingCount == 1
                ? $"MTGB — 1 printer active. " +
                  Pick(PrintingFlavour)
                : $"MTGB — {printingCount} printers active. " +
                  Pick(PrintingFlavour);

            SetIcon(IconPrinting, tooltip);
        }
        else
        {
            SetIcon(IconIdle,
                $"MTGB — All printers ready. " +
                Pick(IdleFlavour));
        }

        // Update flyout if it's open
        _flyout?.RefreshPrinterCards(snapshots);
    }

    private void SetIcon(string iconPath, string tooltip)
    {
        if (_taskbarIcon is null) return;
        _taskbarIcon.IconSource = LoadIcon(iconPath);
        _taskbarIcon.ToolTipText = tooltip;
    }

    private static BitmapImage LoadIcon(string relativePath)
    {
        var fullPath = Path.Combine(
            AppContext.BaseDirectory, relativePath);

        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = new Uri(fullPath, UriKind.Absolute);
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.EndInit();
        return image;
    }

    // ── Left click — flyout ───────────────────────────────────────

    private void OnLeftClick(object sender, RoutedEventArgs e)
    {
        if (_flyout is { IsVisible: true })
        {
            _flyout.SlideDown();
            return;
        }

        _flyout ??= _services.GetRequiredService<FlyoutWindow>();
        _flyout.RefreshPrinterCards(_diffEngine.GetAllSnapshots());
        _flyout.SlideUp();
    }

    // ── Right click — minimal context menu ───────────────────────

    private ContextMenu BuildContextMenu()
    {
        var menu = Application.Current.Resources["TrayMenu"]
            as ContextMenu
            ?? new ContextMenu();

        // Wire up click handlers by name
        WireMenuItemClick(menu, "MenuOpenDashboard",
            () => OpenSimplyPrintDashboard());

        WireMenuItemClick(menu, "MenuHistory",
            () => OpenHistory());

        WireMenuItemClick(menu, "MenuSettings",
            () => OpenSettings());

        WireMenuItemClick(menu, "MenuMute",
            () => ToggleMute());

        WireMenuItemClick(menu, "MenuExit",
            () => ExitApplication());

        return menu;
    }

    private static void WireMenuItemClick(
        ContextMenu menu,
        string itemName,
        Action action)
    {
        var item = menu.Items
            .OfType<MenuItem>()
            .FirstOrDefault(i => i.Name == itemName);

        if (item is not null)
            item.Click += (_, _) => action();
    }

    private MenuItem BuildMenuItem(
        string header,
        Action? action,
        bool isHeader = false,
        bool isDanger = false)
    {
        var item = new MenuItem
        {
            Header = header,
            IsEnabled = !isHeader,
            Foreground = isHeader
                ? new SolidColorBrush(
                    Color.FromRgb(0xF0, 0xC8, 0x40))
                : isDanger
                    ? new SolidColorBrush(
                        Color.FromRgb(0xE8, 0x48, 0x55))
                    : new SolidColorBrush(
                        Color.FromRgb(0xF0, 0xED, 0xE8)),
            Background = new SolidColorBrush(
                Color.FromRgb(0x14, 0x14, 0x17)),
            FontFamily = new FontFamily(
                "Segoe UI Variable, Segoe UI"),
            FontSize = 12
        };

        if (action is not null)
            item.Click += (_, _) => action();

        return item;
    }

    // ── Menu actions ──────────────────────────────────────────────

    private void OpenSimplyPrintDashboard()
    {
        var orgId = _settings.Value.OrganisationId;
        var url = orgId > 0
            ? $"https://simplyprint.io/panel/printers"
            : "https://simplyprint.io/panel";

        System.Diagnostics.Process.Start(
            new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
    }

    private void OpenHistory()
    {
        var history = _services
            .GetRequiredService<HistoryWindow>();
        history.Show();
        history.Activate();
    }

    private void OpenSettings()
    {
        var settings = _services
            .GetRequiredService<SettingsWindow>();
        settings.Show();
        settings.Activate();
    }

    private void ToggleMute()
    {
        var current = _settings.Value
            .Notifications.GlobalMuteEnabled;

        _settings.Value.Notifications.GlobalMuteEnabled = !current;

        var state = !current ? "enabled" : "disabled";
        _taskbarIcon!.ToolTipText =
            $"MTGB — Mute {state}. " +
            (!current
                ? "Blessed silence."
                : "And so it goes Bing again.");

        _logger.LogInformation(
            "Global mute {State} via tray menu.", state);
    }

    private void ExitApplication()
    {
        _logger.LogInformation(
            "Exit requested via tray menu. " +
            "The Ministry has been informed.");

        _flyout?.Close();
        _taskbarIcon?.Dispose();

        Application.Current.Shutdown();
    }

    // ── Toast action handler ──────────────────────────────────────

    private void OnToastActionRequested(
        object? sender,
        ToastActionEventArgs e)
    {
        _logger.LogInformation(
            "Toast action '{Action}' for printer {PrinterId}.",
            e.Action, e.PrinterId);

        _ = ExecuteToastActionAsync(e);
    }

    private async Task ExecuteToastActionAsync(
        ToastActionEventArgs e)
    {
        var success = e.Action switch
        {
            "pause" => await _apiClient
                .PausePrintAsync(e.PrinterId),
            "resume" => await _apiClient
                .ResumePrintAsync(e.PrinterId),
            "cancel" => await _apiClient
                .CancelPrintAsync(e.PrinterId),
            _ => false
        };

        if (!success)
        {
            _logger.LogWarning(
                "Toast action '{Action}' failed for " +
                "printer {PrinterId}. " +
                "It may have already changed state. " +
                "The Ministry is unsurprised.",
                e.Action, e.PrinterId);
        }
    }

    // ── Flavour text for printer cards ────────────────────────────

    /// <summary>
    /// Returns contextual flavour text for a printer's current state.
    /// Used by FlyoutWindow for hover tooltips on printer cards.
    /// </summary>
    public string GetFlavourText(PrinterSnapshot snapshot)
    {
        // Check cache — if state hasn't changed, return cached text
        if (_flavourCache.TryGetValue(
            snapshot.PrinterId, out var cached) &&
            cached.state == snapshot.State &&
            snapshot.Online == (cached.state != "offline"))
        {
            return cached.text;
        }

        // State changed or not cached — pick a new one
        var text = PickFlavourText(snapshot);
        _flavourCache[snapshot.PrinterId] =
            (snapshot.Online ? snapshot.State : "offline", text);
        return text;
    }

    private static string PickFlavourText(PrinterSnapshot snapshot)
    {
        if (!snapshot.Online)
            return Pick(OfflineFlavour);

        return snapshot.State.ToLowerInvariant() switch
        {
            "printing" or "printing_completing" =>
                snapshot.JobPercentage switch
                {
                    >= 75 =>
                        "Nearly there. Do not jinx it. " +
                        "Step away from the printer.",
                    >= 50 =>
                        "Halfway. Either going brilliantly " +
                        "or you haven't looked yet.",
                    >= 25 =>
                        "One quarter done. " +
                        "Three quarters of the anxiety remains.",
                    _ => Pick(PrintingFlavour)
                },
            "paused" or "pausing" =>
                Pick(PausedFlavour),
            "error" or "printer_error" =>
                Pick(FailedFlavour),
            "idle" or "operational" =>
                Pick(IdleFlavour),
            _ => Pick(IdleFlavour)
        };
    }

    // ── Disposal ──────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _taskbarIcon?.Dispose();
        _flyout?.Close();

        _logger.LogInformation(
            "Tray icon disposed. Goodbye.");
    }
}