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
/// Right click → Context menu from TrayContextMenu.xaml.
///
/// Owns the NotifyIcon lifetime, routes toast actions to the API,
/// and keeps the tray icon state in sync with the printer farm.
/// Navy and Gold. It goes BING.
/// </summary>
public class TrayIcon : IDisposable
{
    private readonly IServiceProvider _services;
    private readonly ISimplyPrintApiClient _apiClient;
    private readonly INotificationManager _notificationManager;
    private readonly IStateDiffEngine _diffEngine;
    private readonly IUpdateService _updateService;
    private readonly IOptions<AppSettings> _settings;
    private readonly ILogger<TrayIcon> _logger;

    private TaskbarIcon? _taskbarIcon;
    private FlyoutWindow? _flyout;
    private bool _disposed;
    private string _currentTooltipFlavour = string.Empty;
    private string _lastTooltipState = string.Empty;

    private static readonly string IconIdle = "Assets/mtgb.ico";
    private static readonly string IconPrinting = "Assets/mtgb.ico";
    private static readonly string IconAlert = "Assets/mtgb.ico";

    private readonly Dictionary<int, (string state, string text)>
        _flavourCache = new();

    // ── Flavour text pools ────────────────────────────────────────

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
        IUpdateService updateService,
        IOptions<AppSettings> settings,
        ILogger<TrayIcon> logger)
    {
        _services = services;
        _apiClient = apiClient;
        _notificationManager = notificationManager;
        _diffEngine = diffEngine;
        _updateService = updateService;
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

        _taskbarIcon.TrayLeftMouseDown += OnLeftClick;

        // Context menu from TrayContextMenu.xaml resource dictionary
        if (Application.Current.Resources["TrayMenu"]
            is ContextMenu menu)
        {
            WireContextMenu(menu);
            _taskbarIcon.ContextMenu = menu;
        }

        if (_notificationManager is NotificationManager nm)
            nm.ToastActionRequested += OnToastActionRequested;

        _logger.LogInformation(
            "Tray icon initialised. " +
            "MTGB is watching. Always watching.");

        _ = StartIconStateLoopAsync();
    }

    private void WireContextMenu(ContextMenu menu)
    {
        foreach (var item in menu.Items.OfType<MenuItem>())
        {
            switch (item.Name)
            {
                case "MenuOpenDashboard":
                    item.Click += (_, _) => OpenSimplyPrintDashboard();
                    break;
                case "MenuHistory":
                    item.Click += (_, _) => OpenHistory();
                    break;
                case "MenuSettings":
                    item.Click += (_, _) => OpenSettings();
                    break;
                case "MenuMute":
                    item.Click += (_, _) => ToggleMute();
                    break;
                case "MenuExit":
                    item.Click += (_, _) => ExitApplication();
                    break;
            }
        }
    }

    // ── Icon state loop ───────────────────────────────────────────

    private async Task StartIconStateLoopAsync()
    {
        while (!_disposed)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5));
                Application.Current?.Dispatcher.Invoke(UpdateIconState);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Icon state loop error.");
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

        var offlineCount = snapshots.Values.Count(s => !s.Online);

        if (hasAlert)
        {
            var tooltip = offlineCount > 0
                ? $"MTGB — {offlineCount} printer(s) offline. " +
                  GetTooltipFlavour("offline")
                : $"MTGB — Attention required. " +
                  GetTooltipFlavour("alert");
            SetIcon(IconAlert, tooltip);
        }
        else if (hasPrinting)
        {
            var tooltip = printingCount == 1
                ? $"MTGB — 1 printer active. " +
                  GetTooltipFlavour("printing")
                : $"MTGB — {printingCount} printers active. " +
                  GetTooltipFlavour("printing");
            SetIcon(IconPrinting, tooltip);
        }
        else
        {
            SetIcon(IconIdle,
                $"MTGB — All printers ready. " +
                GetTooltipFlavour("idle"));
        }

        _flyout?.RefreshPrinterCards(snapshots);
    }

    private string GetTooltipFlavour(string stateKey)
    {
        if (_lastTooltipState == stateKey &&
            !string.IsNullOrEmpty(_currentTooltipFlavour))
            return _currentTooltipFlavour;

        _lastTooltipState = stateKey;
        _currentTooltipFlavour = stateKey switch
        {
            "alert" => Pick(FailedFlavour),
            "offline" => Pick(OfflineFlavour),
            "printing" => Pick(PrintingFlavour),
            _ => Pick(IdleFlavour)
        };
        return _currentTooltipFlavour;
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

        if (_flyout is null)
        {
            _flyout = _services.GetRequiredService<FlyoutWindow>();
            _flyout.SetCallbacks(
                onHistory: () => OpenHistory(),
                onSettings: () => OpenSettings(),
                onDashboard: () => OpenSimplyPrintDashboard(),
                onExit: () => ExitApplication());
        }

        _flyout.RefreshPrinterCards(_diffEngine.GetAllSnapshots());
        _flyout.SlideUp();
    }

    // ── Menu actions ──────────────────────────────────────────────

    private void OpenSimplyPrintDashboard()
    {
        var orgId = _settings.Value.OrganisationId;
        var url = orgId > 0
            ? "https://simplyprint.io/panel/printers"
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
        var history = _services.GetRequiredService<HistoryWindow>();
        history.Show();
        history.Activate();
    }

    private void OpenSettings()
    {
        var settings = _services.GetRequiredService<SettingsWindow>();
        settings.Show();
        settings.Activate();
    }

    private void ToggleMute()
    {
        var current = _settings.Value.Notifications.GlobalMuteEnabled;
        _settings.Value.Notifications.GlobalMuteEnabled = !current;

        var state = !current ? "enabled" : "disabled";
        _taskbarIcon!.ToolTipText =
            $"MTGB — Mute {state}. " +
            (!current ? "Blessed silence." : "And so it goes Bing again.");

        _logger.LogInformation(
            "Global mute {State} via tray menu.", state);

        SaveSettings();
    }

    private void SaveSettings()
    {
        try
        {
            DataPaths.EnsureDirectoriesExist();

            var json = System.Text.Json.JsonSerializer.Serialize(
                _settings.Value,
                new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true
                });

            System.IO.File.WriteAllText(DataPaths.SettingsFile, json);
            _logger.LogDebug("Settings persisted after mute toggle.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to persist settings after mute toggle. " +
                "The Ministry is mildly inconvenienced.");
        }
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
            "pause" => await _apiClient.PausePrintAsync(e.PrinterId),
            "resume" => await _apiClient.ResumePrintAsync(e.PrinterId),
            "cancel" => await _apiClient.CancelPrintAsync(e.PrinterId),
            "update" => await HandleUpdateActionAsync(),
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

    // ── Update action ─────────────────────────────────────────────

    private async Task<bool> HandleUpdateActionAsync()
    {
        var release = _updateService.GetCachedRelease();

        if (release is null)
        {
            _logger.LogWarning(
                "Update toast action fired but no " +
                "cached release found. " +
                "The Ministry is confused.");
            return false;
        }

        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var window = new UpdateWindow(
                _updateService,
                release,
                _services.GetRequiredService<ILogger<UpdateWindow>>());

            window.ShowDialog();
        });

        return true;
    }

    // ── Flavour text for printer cards ────────────────────────────

    public string GetFlavourText(PrinterSnapshot snapshot)
    {
        if (_flavourCache.TryGetValue(
            snapshot.PrinterId, out var cached) &&
            cached.state == snapshot.State &&
            snapshot.Online == (cached.state != "offline"))
        {
            return cached.text;
        }

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
                    >= 75 => "Nearly there. Do not jinx it. " +
                             "Step away from the printer.",
                    >= 50 => "Halfway. Either going brilliantly " +
                             "or you haven't looked yet.",
                    >= 25 => "One quarter done. " +
                             "Three quarters of the anxiety remains.",
                    _ => Pick(PrintingFlavour)
                },
            "paused" or "pausing" => Pick(PausedFlavour),
            "error" or "printer_error" => Pick(FailedFlavour),
            "idle" or "operational" => Pick(IdleFlavour),
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

        _logger.LogInformation("Tray icon disposed. Goodbye.");
    }
}