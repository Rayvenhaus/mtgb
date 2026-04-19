using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Win32;
using MTGB.Config;
using MTGB.Core.Security;
using MTGB.Services;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MTGB.UI;

/// <summary>
/// MTGB Settings Window.
/// Tabbed Art Deco dark brass settings UI.
/// Handles credentials, printer config, notification events,
/// quiet hours, advanced options and the origin story.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly IOptions<AppSettings> _settings;
    private readonly ICredentialManager _credentials;
    private readonly ISimplyPrintApiClient _apiClient;
    private readonly IAuthService _authService;
    private readonly IStateDiffEngine _diffEngine;
    private readonly INotificationManager _notificationManager;
    private readonly ICommunityMapService _communityMap;
    private readonly ILogger<SettingsWindow> _logger;

    private bool _isDirty = false;
    #pragma warning disable CS0414
    private bool _isLoading = true;
    #pragma warning restore CS0414
    private List<CountryData> _privacyCountries = new();
    private CountryData? _privacySelectedCountry;

    public SettingsWindow(
        IOptions<AppSettings> settings,
        ICredentialManager credentials,
        ISimplyPrintApiClient apiClient,
        IAuthService authService,
        IStateDiffEngine diffEngine,
        INotificationManager notificationManager,
        ICommunityMapService communityMap,
        ILogger<SettingsWindow> logger)
    {
        _settings = settings;
        _credentials = credentials;
        _apiClient = apiClient;
        _authService = authService;
        _diffEngine = diffEngine;
        _notificationManager = notificationManager;
        _communityMap = communityMap;
        _logger = logger;

        InitializeComponent();
        Loaded += OnLoaded;
        Closing += OnWindowClosing;
    }

    // ── Startup ───────────────────────────────────────────────────

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Force dark title bar
        var hwnd = new System.Windows.Interop
            .WindowInteropHelper(this).Handle;
        int darkMode = 1;
        DwmSetWindowAttribute(hwnd, 20,
            ref darkMode, sizeof(int));

        try
        {
            LoadSettingsIntoUi();
            UpdateConnectionStatus();
            UpdateVersionText();
            RefreshPrinterList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Settings window failed to load: {Message}",
                ex.Message);

            MessageBox.Show(
                $"Settings window error:\n\n{ex.Message}\n\n" +
                $"{ex.StackTrace}",
                "MTGB — Settings Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void LoadSettingsIntoUi()
    {
        _isLoading = true;
        var s = _settings.Value;

        // Account
        RadioApiKey.IsChecked = s.AuthMode == AuthMode.ApiKey;
        RadioOAuth.IsChecked = s.AuthMode == AuthMode.OAuth2;
        OrgIdInput.Text = s.OrganisationId > 0
            ? s.OrganisationId.ToString()
            : string.Empty;

        // Load masked API key indicator if key exists
        if (_credentials.Exists(CredentialKey.ApiKey))
            ApiKeyInput.Password = "••••••••••••••••••••••";

        // Show correct auth panel
        UpdateAuthPanels();

        // Show diagnostic panel if DiagnosticMode is enabled
        #if DEBUG
            DiagnosticPanel.Visibility = Visibility.Visible;
        #else
            DiagnosticPanel.Visibility = Visibility.Collapsed;
        #endif

        // Notifications
        GlobalMuteToggle.IsChecked =
            s.Notifications.GlobalMuteEnabled;
        ActionButtonsToggle.IsChecked =
            s.Notifications.ActionButtonsEnabled;
        GroupingToggle.IsChecked =
            s.Notifications.GroupingEnabled;
        SoundToggle.IsChecked =
            s.Notifications.SoundEnabled;

        // Event toggles
        SyncEventToggle(EventJobStarted, "job.started");
        SyncEventToggle(EventJobFinished, "job.finished");
        SyncEventToggle(EventJobFailed, "job.failed");
        SyncEventToggle(EventJobPaused, "job.paused");
        SyncEventToggle(EventJobCancelled, "job.cancelled");
        SyncEventToggle(EventJobResumed, "job.resumed");
        SyncEventToggle(EventPrinterOffline, "printer.offline");
        SyncEventToggle(EventPrinterOnline, "printer.online");
        SyncEventToggle(EventProgress25, "progress.25");
        SyncEventToggle(EventProgress50, "progress.50");
        SyncEventToggle(EventProgress75, "progress.75");
        SyncEventToggle(EventFilamentLow, "filament.low");
        SyncEventToggle(EventNozzleTemp, "temp.nozzle.low");
        SyncEventToggle(EventBedTemp, "temp.bed.low");
        SyncEventToggle(EventQueueAdded, "queue.added");
        SyncEventToggle(EventQueueEmptied, "queue.emptied");

        // Quiet hours
        QuietHoursToggle.IsChecked = s.QuietHours.Enabled;
        QuietStartInput.Text = s.QuietHours.Start;
        QuietEndInput.Text = s.QuietHours.End;
        AllowCriticalToggle.IsChecked = s.QuietHours.AllowCritical;
        UpdateQuietHoursPanels();

        // Advanced
        PollingIntervalInput.Text =
            s.Polling.IntervalSeconds.ToString();
        WebhookToggle.IsChecked = s.Webhook.Enabled;
        StartupToggle.IsChecked = s.Ui.StartWithWindows;
        UpdateWebhookPortPanel();

        // Language
        foreach (var item in LanguageSelector.Items
            .OfType<System.Windows.Controls.ComboBoxItem>())
        {
            if (item.Tag?.ToString() == s.Ui.Language)
            {
                item.IsSelected = true;
                break;
            }
        }

        // Privacy tab
        TelemetryEnabledToggle.IsChecked =
            s.Telemetry.Enabled;
        LoadPrivacyCountries();

        _isLoading = false;
        _isDirty = false;
        if (CancelButton is not null)
            CancelButton.IsEnabled = false;
        UpdateFooterStatus();
    }

    private void SyncEventToggle(
        System.Windows.Controls.Primitives.ToggleButton toggle,
        string eventId)
    {
        var enabled = _settings.Value.Notifications
            .EnabledEventIds;

        // If the list is empty, all events are enabled
        toggle.IsChecked = !enabled.Any() ||
                           enabled.Contains(eventId);
    }

    private void UpdateVersionText()
    {
        var version = GetType().Assembly
            .GetName().Version;
        VersionText.Text =
            $"The Monitor That Goes Bing · " +
            $"v{version?.Major}.{version?.Minor}." +
            $"{version?.Build}";
    }

    // ── Auth panel switching ──────────────────────────────────────

    private void UpdateAuthPanels()
    {
        if (RadioApiKey is null ||
            ApiKeyPanel is null ||
            OAuthPanel is null) return;

        var isApiKey = RadioApiKey.IsChecked == true;
        ApiKeyPanel.Visibility = isApiKey
            ? Visibility.Visible
            : Visibility.Collapsed;
        OAuthPanel.Visibility = isApiKey
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void OnAuthModeChanged(
        object sender, RoutedEventArgs e)
    {
        UpdateAuthPanels();
        MarkDirty();
    }

    // ── Connection ────────────────────────────────────────────────

    private async void OnConnectClick(
        object sender, RoutedEventArgs e)
    {
        ConnectButton.IsEnabled = false;
        SetFooterStatus("Connecting...", isPending: true);

        try
        {
            if (RadioApiKey.IsChecked == true)
            {
                if (!int.TryParse(OrgIdInput.Text.Trim(),
                    out var orgId) || orgId <= 0)
                {
                    SetFooterStatus(
                        "Invalid organisation ID.",
                        isError: true);
                    return;
                }

                var apiKey = ApiKeyInput.Password;
                if (string.IsNullOrWhiteSpace(apiKey) ||
                    apiKey.StartsWith("••"))
                {
                    // Key already stored — just test it
                    var valid = await _apiClient
                        .TestConnectionAsync();
                    if (valid)
                    {
                        _settings.Value.OrganisationId = orgId;
                        ShowConnected(
                            "Connected to SimplyPrint.");
                        await RefreshPrinterListAsync();
                        return;
                    }

                    SetFooterStatus(
                        "Connection failed. Check your credentials.",
                        isError: true);
                    return;
                }

                var result = await _authService
                    .LoginWithApiKeyAsync(apiKey, orgId);

                if (result.Success)
                {
                    _settings.Value.OrganisationId = orgId;
                    _settings.Value.AuthMode = AuthMode.ApiKey;
                    ApiKeyInput.Password = "••••••••••••••••••••••";
                    ShowConnected("Connected to SimplyPrint.");
                    await RefreshPrinterListAsync();
                }
                else
                {
                    SetFooterStatus(
                        result.ErrorMessage ??
                        "Connection failed.",
                        isError: true);
                }
            }
            else
            {
                var result = await _authService
                    .LoginWithOAuthAsync();

                if (result.Success)
                {
                    _settings.Value.AuthMode = AuthMode.OAuth2;
                    ShowConnected(
                        $"Connected as {result.UserName}.");
                    await RefreshPrinterListAsync();
                }
                else
                {
                    SetFooterStatus(
                        result.ErrorMessage ??
                        "OAuth2 login failed.",
                        isError: true);
                }
            }
        }
        finally
        {
            ConnectButton.IsEnabled = true;
        }
    }

    private void OnDisconnectClick(
        object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(
            "Disconnect from SimplyPrint and clear stored credentials?\n\n" +
            "The Ministry will be notified.",
            "MTGB — Disconnect",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes) return;

        _authService.Logout();
        ApiKeyInput.Password = string.Empty;

        ConnectionStatusPanel.Visibility = Visibility.Collapsed;
        ConnectButton.Visibility = Visibility.Visible;
        DisconnectButton.Visibility = Visibility.Collapsed;

        SetFooterStatus(
            "Disconnected. Credentials cleared.",
            isError: false);

        PrinterListPanel.Children.Clear();
        NoPrintersText.Visibility = Visibility.Visible;

        _logger.LogInformation(
            "User disconnected via Settings window.");
    }

    private async void OnOAuthLoginClick(
        object sender, RoutedEventArgs e) =>
        await OnConnectClickAsync();

    private async Task OnConnectClickAsync() =>
        OnConnectClick(this, new RoutedEventArgs());

    private void ShowConnected(string message)
    {
        ConnectionStatusPanel.Visibility = Visibility.Visible;
        ConnectionDot.Fill = new SolidColorBrush(
            Color.FromRgb(0x3B, 0xB2, 0x73));
        ConnectionStatusText.Text = message;
        ConnectionStatusText.Foreground = new SolidColorBrush(
            Color.FromRgb(0x3B, 0xB2, 0x73));

        ConnectButton.Visibility = Visibility.Collapsed;
        DisconnectButton.Visibility = Visibility.Visible;

        SetFooterStatus(message);
    }

    private void UpdateConnectionStatus()
    {
        if (_authService.IsAuthenticated)
        {
            ShowConnected("Connected to SimplyPrint.");
        }
        else
        {
            ConnectionStatusPanel.Visibility =
                Visibility.Collapsed;
            ConnectButton.Visibility = Visibility.Visible;
            DisconnectButton.Visibility = Visibility.Collapsed;
        }
    }

    // ── Printer list ──────────────────────────────────────────────

    private void RefreshPrinterList() =>
        _ = RefreshPrinterListAsync();

    private async Task RefreshPrinterListAsync()
    {
        if (!_authService.IsAuthenticated) return;

        // Show cached snapshots immediately
        var snapshots = _diffEngine.GetAllSnapshots();

        if (snapshots.Any())
        {
            RenderPrinterList(snapshots);
            return;
        }

        // No cached data — fetch fresh
        var printers = await _apiClient.GetPrintersAsync();
        var snaps = _diffEngine.GetAllSnapshots();

        if (snaps.Any())
            RenderPrinterList(snaps);
        else if (printers.Any())
            RenderPrinterListFromApi(printers);
    }

    private void RenderPrinterList(
        IReadOnlyDictionary<int, PrinterSnapshot> snapshots)
    {
        Dispatcher.Invoke(() =>
        {
            PrinterListPanel.Children.Clear();
            NoPrintersText.Visibility = Visibility.Collapsed;

            foreach (var snapshot in snapshots.Values
                .OrderBy(s => s.PrinterName))
            {
                var row = BuildPrinterRow(
                    snapshot.PrinterId,
                    snapshot.PrinterName,
                    snapshot.Online,
                    snapshot.State);

                PrinterListPanel.Children.Add(row);
            }
        });
    }

    private void RenderPrinterListFromApi(
        List<PrinterData> printers)
    {
        Dispatcher.Invoke(() =>
        {
            PrinterListPanel.Children.Clear();
            NoPrintersText.Visibility = Visibility.Collapsed;

            foreach (var printer in printers
                .OrderBy(p => p.Printer?.Name))
            {
                if (printer.Printer is null) continue;

                var row = BuildPrinterRow(
                    printer.Id,
                    printer.Printer.Name,
                    printer.Printer.Online,
                    printer.Printer.State);

                PrinterListPanel.Children.Add(row);
            }
        });
    }

    private System.Windows.Controls.Border BuildPrinterRow(
        int printerId,
        string printerName,
        bool online,
        string state)
    {
        var settings = _settings.Value;
        var printerSettings = settings.Printers
            .GetValueOrDefault(printerId);
        var isEnabled = printerSettings?.Enabled ?? true;

        var statusColor = online
            ? state.ToLowerInvariant() switch
            {
                "printing" or "printing_completing" =>
                    Color.FromRgb(0xF1, 0x8F, 0x01),
                "error" or "printer_error" =>
                    Color.FromRgb(0xE8, 0x48, 0x55),
                _ => Color.FromRgb(0x3B, 0xB2, 0x73)
            }
            : Color.FromRgb(0x5A, 0x52, 0x48);

        var row = new System.Windows.Controls.Border
        {
            Padding = new Thickness(14, 11, 14, 11),
            BorderBrush = new SolidColorBrush(
                Color.FromArgb(0x20, 0xC9, 0x93, 0x0E)),
            BorderThickness = new Thickness(0, 0, 0, 1)
        };

        var grid = new System.Windows.Controls.Grid();
        grid.ColumnDefinitions.Add(
            new System.Windows.Controls.ColumnDefinition
            {
                Width = System.Windows.GridLength.Auto
            });
        grid.ColumnDefinitions.Add(
            new System.Windows.Controls.ColumnDefinition
            {
                Width = new System.Windows.GridLength(
                    1, System.Windows.GridUnitType.Star)
            });
        grid.ColumnDefinitions.Add(
            new System.Windows.Controls.ColumnDefinition
            {
                Width = System.Windows.GridLength.Auto
            });

        // Avatar
        var avatar = new System.Windows.Controls.Border
        {
            Width = 34,
            Height = 34,
            Background = new SolidColorBrush(
                Color.FromRgb(0x22, 0x22, 0x2A)),
            BorderBrush = new SolidColorBrush(
                Color.FromArgb(0x59, 0xC9, 0x93, 0x0E)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(0, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center
        };

        var initials = new System.Windows.Controls.TextBlock
        {
            Text = GetInitials(printerName),
            FontFamily = new FontFamily("Courier New"),
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(
                Color.FromRgb(0x8B, 0x65, 0x08)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        avatar.Child = initials;
        System.Windows.Controls.Grid.SetColumn(avatar, 0);

        // Info
        var info = new System.Windows.Controls.StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center
        };

        var nameText = new System.Windows.Controls.TextBlock
        {
            Text = printerName,
            FontFamily = new FontFamily(
                "Segoe UI Variable, Segoe UI"),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(
                Color.FromRgb(0xF0, 0xED, 0xE8))
        };

        var statusRow = new System.Windows.Controls.StackPanel
        {
            Orientation =
                System.Windows.Controls.Orientation.Horizontal,
            Margin = new Thickness(0, 3, 0, 0)
        };

        var dot = new System.Windows.Shapes.Ellipse
        {
            Width = 6,
            Height = 6,
            Fill = new SolidColorBrush(statusColor),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 5, 0)
        };

        var stateText = new System.Windows.Controls.TextBlock
        {
            Text = online
                ? char.ToUpper(state[0]) + state[1..]
                : "Offline",
            FontFamily = new FontFamily("Courier New"),
            FontSize = 9,
            Foreground = new SolidColorBrush(
                Color.FromRgb(0x9A, 0x90, 0x80))
        };

        statusRow.Children.Add(dot);
        statusRow.Children.Add(stateText);
        info.Children.Add(nameText);
        info.Children.Add(statusRow);

        System.Windows.Controls.Grid.SetColumn(info, 1);

        // Toggle
        var toggle =
            new System.Windows.Controls.Primitives.ToggleButton
            {
                Style = (Style)FindResource("MtgbToggle"),
                IsChecked = isEnabled,
                VerticalAlignment = VerticalAlignment.Center,
                Tag = printerId
            };

        toggle.Click += (_, _) =>
        {
            var pid = (int)toggle.Tag;
            var enabled = toggle.IsChecked == true;

            if (!_settings.Value.Printers.ContainsKey(pid))
                _settings.Value.Printers[pid] =
                    new PrinterSettings();

            _settings.Value.Printers[pid].Enabled = enabled;
            MarkDirty();
        };

        System.Windows.Controls.Grid.SetColumn(toggle, 2);

        grid.Children.Add(avatar);
        grid.Children.Add(info);
        grid.Children.Add(toggle);
        row.Child = grid;

        return row;
    }

    private static string GetInitials(string name)
    {
        var words = name.Split(' ',
            StringSplitOptions.RemoveEmptyEntries);
        return words.Length >= 2
            ? $"{words[0][0]}{words[1][0]}"
                .ToUpperInvariant()
            : name.Length >= 2
                ? name[..2].ToUpperInvariant()
                : name.ToUpperInvariant();
    }

    private async void OnRefreshPrintersClick(
        object sender, RoutedEventArgs e) =>
        await RefreshPrinterListAsync();

    // ── Notification toggles ──────────────────────────────────────

    private void OnGlobalMuteChanged(
        object sender, RoutedEventArgs e)
    {
        _settings.Value.Notifications.GlobalMuteEnabled =
            GlobalMuteToggle.IsChecked == true;
        MarkDirty();
    }

    private void OnActionButtonsChanged(
        object sender, RoutedEventArgs e)
    {
        _settings.Value.Notifications.ActionButtonsEnabled =
            ActionButtonsToggle.IsChecked == true;
        MarkDirty();
    }

    private void OnGroupingChanged(
        object sender, RoutedEventArgs e)
    {
        _settings.Value.Notifications.GroupingEnabled =
            GroupingToggle.IsChecked == true;
        MarkDirty();
    }

    private void OnSoundChanged(
        object sender, RoutedEventArgs e)
    {
        _settings.Value.Notifications.SoundEnabled =
            SoundToggle.IsChecked == true;
        MarkDirty();
    }

    private void OnEventToggleChanged(
        object sender, RoutedEventArgs e)
    {
        if (sender is not
            System.Windows.Controls.Primitives.ToggleButton
            toggle) return;

        var eventId = toggle.Tag?.ToString();
        if (string.IsNullOrWhiteSpace(eventId)) return;

        var ids = _settings.Value.Notifications.EnabledEventIds;

        if (toggle.IsChecked == true)
        {
            if (!ids.Contains(eventId))
                ids.Add(eventId);
        }
        else
        {
            ids.Remove(eventId);
        }

        MarkDirty();
    }

    // ── Quiet hours ───────────────────────────────────────────────

    private void OnQuietHoursToggleChanged(
        object sender, RoutedEventArgs e)
    {
        _settings.Value.QuietHours.Enabled =
            QuietHoursToggle.IsChecked == true;
        UpdateQuietHoursPanels();
        MarkDirty();
    }

    private void UpdateQuietHoursPanels()
    {
        if (QuietHoursTimePanel is null ||
            QuietHoursEndPanel is null) return;

        var enabled = QuietHoursToggle.IsChecked == true;
        QuietHoursTimePanel.IsEnabled = enabled;
        QuietHoursTimePanel.Opacity = enabled ? 1.0 : 0.5;
        QuietHoursEndPanel.IsEnabled = enabled;
        QuietHoursEndPanel.Opacity = enabled ? 1.0 : 0.5;
    }

    private void OnQuietHoursChanged(
        object sender,
        System.Windows.Controls.TextChangedEventArgs e)
    {
        if (QuietStartInput is null || QuietEndInput is null)
            return;

        _settings.Value.QuietHours.Start =
            QuietStartInput.Text;
        _settings.Value.QuietHours.End =
            QuietEndInput.Text;
        MarkDirty();
    }

    private void OnAllowCriticalChanged(
        object sender, RoutedEventArgs e)
    {
        _settings.Value.QuietHours.AllowCritical =
            AllowCriticalToggle.IsChecked == true;
        MarkDirty();
    }

    // ── Advanced ──────────────────────────────────────────────────

    private void OnPollingIntervalChanged(
        object sender,
        System.Windows.Controls.TextChangedEventArgs e)
    {
        if (int.TryParse(PollingIntervalInput.Text,
            out var interval) && interval >= 10)
        {
            _settings.Value.Polling.IntervalSeconds = interval;
            MarkDirty();
        }
    }

    private void OnWebhookToggleChanged(
        object sender, RoutedEventArgs e)
    {
        _settings.Value.Webhook.Enabled =
            WebhookToggle.IsChecked == true;
        UpdateWebhookPortPanel();
        MarkDirty();
    }

    private void UpdateWebhookPortPanel()
    {
        if (WebhookPortPanel is null) return;

        var enabled = WebhookToggle.IsChecked == true;
        WebhookPortPanel.IsEnabled = enabled;
        WebhookPortPanel.Opacity = enabled ? 1.0 : 0.5;
    }

    private void OnWebhookPortChanged(
        object sender,
        System.Windows.Controls.TextChangedEventArgs e)
    {
        if (int.TryParse(WebhookPortInput.Text,
            out var port) && port is > 1024 and < 65535)
        {
            _settings.Value.Webhook.Port = port;
            MarkDirty();
        }
    }

    private void OnStartupToggleChanged(
        object sender, RoutedEventArgs e)
    {
        var enabled = StartupToggle.IsChecked == true;
        _settings.Value.Ui.StartWithWindows = enabled;
        SetWindowsStartup(enabled);
        MarkDirty();
    }

    private static void SetWindowsStartup(bool enable)
    {
        const string runKey =
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        const string appName = "MTGB";

        using var key = Registry.CurrentUser
            .OpenSubKey(runKey, writable: true);

        if (key is null) return;

        if (enable)
        {
            var exePath = Environment.ProcessPath ??
                System.Reflection.Assembly
                    .GetExecutingAssembly().Location;
            key.SetValue(appName, $"\"{exePath}\"");
        }
        else
        {
            key.DeleteValue(appName,
                throwOnMissingValue: false);
        }
    }

    private void OnLanguageChanged(
        object sender,
        System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (LanguageSelector.SelectedItem is
            System.Windows.Controls.ComboBoxItem item)
        {
            _settings.Value.Ui.Language =
                item.Tag?.ToString() ?? "en-AU";
            MarkDirty();
        }
    }

    private void OnClearHistoryClick(
        object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(
            "Permanently delete all notification history?\n\n" +
            "This cannot be undone. " +
            "The Ministry will not file a recovery form.",
            "MTGB — Clear history",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        _notificationManager.ClearHistory();
        SetFooterStatus("History cleared.");

        _logger.LogInformation(
            "Notification history cleared via Settings.");
    }

    // ── Privacy tab ───────────────────────────────────────────────

    private void LoadPrivacyCountries()
    {
        var countryList = _communityMap.LoadCountries();
        if (countryList is null) return;

        _privacyCountries = countryList.Countries;
        PrivacyCountrySelector.ItemsSource =
            _privacyCountries.Select(c => c.Name).ToList();
    }

    private void OnTelemetryToggleChanged(
        object sender, RoutedEventArgs e)
    {
        _settings.Value.Telemetry.Enabled =
            TelemetryEnabledToggle.IsChecked == true;
        MarkDirty();
    }

    private void OnTelemetryPolicyClick(
        object sender, RoutedEventArgs e) =>
        OpenUrl(
            "https://github.com/Rayvenhaus/mtgb/blob/main/TELEMETRY.md");

    private async void OnMapStatusRefreshClick(
        object sender, RoutedEventArgs e)
    {
        await RefreshMapStatusAsync();
    }

    private async Task RefreshMapStatusAsync()
    {
        // Amber — checking
        MapStatusDot.Fill = new SolidColorBrush(
            Color.FromRgb(0xF1, 0x8F, 0x01));
        MapStatusText.Text = "Checking...";
        MapStatusText.Foreground = new SolidColorBrush(
            Color.FromRgb(0xF1, 0x8F, 0x01));
        MapRefreshButton.IsEnabled = false;
        MapOptOutPanel.Visibility = Visibility.Collapsed;
        MapOptInPanel.Visibility = Visibility.Collapsed;

        try
        {
            var displayName = await _communityMap
                .GetStatusAsync();

            if (displayName is not null)
            {
                // Green — registered
                MapStatusDot.Fill = new SolidColorBrush(
                    Color.FromRgb(0x3B, 0xB2, 0x73));
                MapStatusText.Text =
                    $"Registered — {displayName}";
                MapStatusText.Foreground =
                    new SolidColorBrush(
                        Color.FromRgb(0x3B, 0xB2, 0x73));
                MapOptOutPanel.Visibility =
                    Visibility.Visible;
            }
            else
            {
                // Grey — not registered
                MapStatusDot.Fill = new SolidColorBrush(
                    Color.FromRgb(0x5A, 0x52, 0x48));
                MapStatusText.Text = "Not registered";
                MapStatusText.Foreground =
                    new SolidColorBrush(
                        Color.FromRgb(0x5A, 0x52, 0x48));
                MapOptInPanel.Visibility =
                    Visibility.Visible;
            }
        }
        catch
        {
            MapStatusDot.Fill = new SolidColorBrush(
                Color.FromRgb(0xE8, 0x48, 0x55));
            MapStatusText.Text =
                "Could not reach the Ministry.";
            MapStatusText.Foreground =
                new SolidColorBrush(
                    Color.FromRgb(0xE8, 0x48, 0x55));
        }
        finally
        {
            MapRefreshButton.IsEnabled = true;
        }
    }

    private async void OnMapOptOutClick(
        object sender, RoutedEventArgs e)
    {
        MapOptOutButton.IsEnabled = false;
        MapOptOutButton.Content = "Processing...";

        var success = await _communityMap.UnregisterAsync();

        if (success)
        {
            MapOptOutPanel.Visibility = Visibility.Collapsed;
            MapStatusDot.Fill = new SolidColorBrush(
                Color.FromRgb(0x5A, 0x52, 0x48));
            MapStatusText.Text =
                "Removed. The Ministry has filed the departure form.";
            MapStatusText.Foreground =
                new SolidColorBrush(
                    Color.FromRgb(0x5A, 0x52, 0x48));
            MapOptInPanel.Visibility = Visibility.Visible;
            MarkDirty();
        }
        else
        {
            MapOptOutButton.Content = "Opt out";
            MapOptOutButton.IsEnabled = true;
            SetFooterStatus(
                "Removal failed — check your connection.",
                isError: true);
        }
    }

    private void OnPrivacyCountryChanged(
        object sender,
        System.Windows.Controls.SelectionChangedEventArgs e)
    {
        var selectedName = PrivacyCountrySelector
            .SelectedItem?.ToString();

        _privacySelectedCountry = _privacyCountries
            .FirstOrDefault(c => c.Name == selectedName);

        if (_privacySelectedCountry is null) return;

        if (_privacySelectedCountry.HasStates)
        {
            PrivacyStateSelectorPanel.Visibility =
                Visibility.Visible;
            PrivacyStateSelector.ItemsSource =
                _privacySelectedCountry.States;
            PrivacyStateSelector.SelectedIndex = -1;
        }
        else
        {
            PrivacyStateSelectorPanel.Visibility =
                Visibility.Collapsed;
            PrivacyStateSelector.ItemsSource = null;
        }

        UpdatePrivacyConfirmText();
    }

    private void OnPrivacyStateChanged(
        object sender,
        System.Windows.Controls.SelectionChangedEventArgs e)
    {
        UpdatePrivacyConfirmText();
    }

    private void UpdatePrivacyConfirmText()
    {
        if (_privacySelectedCountry is null)
        {
            PrivacyConfirmText.Text = string.Empty;
            return;
        }

        var stateName = _privacySelectedCountry.HasStates
            ? PrivacyStateSelector.SelectedItem?.ToString()
            : null;

        PrivacyConfirmText.Text = stateName is not null
            ? $"You'll be added to the count of installations " +
              $"in {_privacySelectedCountry.Name}, " +
              $"in {stateName}."
            : $"You'll be added to the count of installations " +
              $"in {_privacySelectedCountry.Name}.";
    }

    private async void OnMapOptInClick(
        object sender, RoutedEventArgs e)
    {
        if (_privacySelectedCountry is null)
        {
            SetFooterStatus(
                "Please select a country first.",
                isError: true);
            return;
        }

        var stateName = _privacySelectedCountry.HasStates
            ? PrivacyStateSelector.SelectedItem?.ToString()
            : null;

        if (_privacySelectedCountry.HasStates &&
            string.IsNullOrWhiteSpace(stateName))
        {
            SetFooterStatus(
                "Please select a state or territory.",
                isError: true);
            return;
        }

        MapOptInButton.IsEnabled = false;
        MapOptInButton.Content = "Processing...";

        var success = await _communityMap.RegisterAsync(
            _privacySelectedCountry.Code,
            _privacySelectedCountry.Name,
            stateName);

        if (success)
        {
            MapOptInPanel.Visibility = Visibility.Collapsed;
            MapStatusDot.Fill = new SolidColorBrush(
                Color.FromRgb(0x3B, 0xB2, 0x73));
            MapStatusText.Text =
                $"Registered — " +
                $"{_settings.Value.CommunityMap.DisplayName}";
            MapStatusText.Foreground =
                new SolidColorBrush(
                    Color.FromRgb(0x3B, 0xB2, 0x73));
            MapOptOutPanel.Visibility = Visibility.Visible;
            MarkDirty();
        }
        else
        {
            MapOptInButton.Content = "Add me to the map";
            MapOptInButton.IsEnabled = true;
            SetFooterStatus(
                "Registration failed — check your connection.",
                isError: true);
        }
    }

    // ── About ─────────────────────────────────────────────────────

    private void OnGitHubClick(
        object sender, RoutedEventArgs e) =>
        OpenUrl("https://github.com/YOUR_USERNAME/mtgb");

    private void OnOriginStoryClick(
        object sender, RoutedEventArgs e) =>
        OpenUrl(
            "https://github.com/YOUR_USERNAME/mtgb/blob/main/THE_TRUTH.md");

    private static void OpenUrl(string url)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }

    // ── Save / Close ──────────────────────────────────────────────

    private void OnSaveClick(
        object sender, RoutedEventArgs e)
    {
        SaveSettings();
    }

    private void OnCloseClick(
        object sender, RoutedEventArgs e)
    {
        if (_isDirty)
        {
            var result = MessageBox.Show(
                "You have unsaved changes.\n\n" +
                "Save your changes before closing, " +
                "or Cancel to discard them.\n\n" +
                "The Ministry strongly recommends saving.",
                "MTGB — Unsaved Changes",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Cancel) return;
            if (result == MessageBoxResult.Yes)
                SaveSettings();
        }

        Hide();
    }

    private void SaveSettings()
    {
        try
        {
            var json = System.Text.Json.JsonSerializer
                .Serialize(_settings.Value,
                new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true
                });

            // Save to %APPDATA%\MTGB\ — not the build output directory.
            // AppContext.BaseDirectory breaks on installed builds.
            var appDataDir = System.IO.Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.ApplicationData),
                "MTGB");

            Directory.CreateDirectory(appDataDir);

            var path = System.IO.Path.Combine(
                appDataDir, "appsettings.json");

            System.IO.File.WriteAllText(path, json);

            _isDirty = false;
            if (CancelButton is not null)
                CancelButton.IsEnabled = false;

            SetFooterStatus("Settings saved.");

            _logger.LogInformation(
                "Settings saved via Settings window.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save settings.");
            SetFooterStatus(
                "Failed to save settings.",
                isError: true);
        }
    }

    private async void OnTabChanged(
        object sender,
        System.Windows.Controls.SelectionChangedEventArgs e)
    {
        // Only fire when Privacy tab (index 5) is selected
        if (MainTabs.SelectedIndex == 5)
            await RefreshMapStatusAsync();
    }

    // ── Footer status ─────────────────────────────────────────────

    private void SetFooterStatus(
        string message,
        bool isError = false,
        bool isPending = false)
    {
        if (FooterStatusText is null) return;

        FooterStatusText.Text = message;
        FooterStatusText.Foreground = isError
            ? new SolidColorBrush(
                Color.FromRgb(0xE8, 0x48, 0x55))
            : isPending
                ? new SolidColorBrush(
                    Color.FromRgb(0xF1, 0x8F, 0x01))
                : new SolidColorBrush(
                    Color.FromRgb(0x5A, 0x52, 0x48));
    }

    private void UpdateFooterStatus()
    {
        if (_authService.IsAuthenticated)
        {
            SetFooterStatus("Connected to SimplyPrint.");
        }
        else
        {
            SetFooterStatus(
                "Not connected — enter credentials above.");
        }
    }

    private void MarkDirty()
    {
        if (FooterStatusText is null) return;

        _isDirty = true;
        FooterStatusText.Text =
            "Unsaved changes — click Save to apply.";
        FooterStatusText.Foreground = new SolidColorBrush(
            Color.FromRgb(0xF1, 0x8F, 0x01));

        if (CancelButton is not null)
            CancelButton.IsEnabled = true;
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
    IntPtr hwnd, int attr,
    ref int attrValue, int attrSize);

    private void OnWindowClosing(
    object? sender,
    System.ComponentModel.CancelEventArgs e)
    {
        if (!_isDirty) return;

        var result = MessageBox.Show(
            "You have unsaved changes.\n\n" +
            "Save your changes before closing, " +
            "or Cancel to discard them.\n\n" +
            "The Ministry strongly recommends saving.",
            "MTGB — Unsaved Changes",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.Cancel)
        {
            // Abort the close
            e.Cancel = true;
            return;
        }

        if (result == MessageBoxResult.Yes)
            SaveSettings();

        // Hide instead of close so the window
        // can be reopened without recreating it
        e.Cancel = true;
        Hide();
    }

    private async void OnDumpApiClick(
    object sender, RoutedEventArgs e)
    {
        var btn = (Button)sender;
        btn.IsEnabled = false;
        btn.Content = "Dumping...";

        try
        {
            var dumpDir = System.IO.Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.ApplicationData),
                "MTGB");

            Directory.CreateDirectory(dumpDir);

            var orgId = _settings.Value.OrganisationId;
            var baseUrl = $"https://api.simplyprint.io/{orgId}";

            using var http = new System.Net.Http.HttpClient();
            var apiKey = _credentials.Load(CredentialKey.ApiKey);
            if (!string.IsNullOrWhiteSpace(apiKey))
                http.DefaultRequestHeaders.Add(
                    "X-API-KEY", apiKey);

            // ── 1. Printers/Get ───────────────────────────────────
            var printersResponse = await http.PostAsync(
                $"{baseUrl}/printers/Get",
                new System.Net.Http.StringContent(
                    "{\"page\":1,\"page_size\":100}",
                    System.Text.Encoding.UTF8,
                    "application/json"));

            var printersJson = await printersResponse.Content
                .ReadAsStringAsync();

            // Pretty print
            var printersPretty = System.Text.Json.JsonSerializer
                .Serialize(
                    System.Text.Json.JsonSerializer
                        .Deserialize<object>(printersJson),
                    new System.Text.Json.JsonSerializerOptions
                    {
                        WriteIndented = true
                    });

            var printersPath = System.IO.Path.Combine(
                dumpDir, "api_dump_printers.json");
            await System.IO.File.WriteAllTextAsync(
                printersPath, printersPretty);

            _logger.LogInformation(
                "Printers dump written to {Path}", printersPath);

            // ── 2. Jobs/GetPaginatedPrintJobs ─────────────────────
            var jobsResponse = await http.PostAsync(
                $"{baseUrl}/jobs/GetPaginatedPrintJobs",
                new System.Net.Http.StringContent(
                    "{\"page\":1,\"page_size\":10}",
                    System.Text.Encoding.UTF8,
                    "application/json"));

            var jobsJson = await jobsResponse.Content
                .ReadAsStringAsync();

            var jobsPretty = System.Text.Json.JsonSerializer
                .Serialize(
                    System.Text.Json.JsonSerializer
                        .Deserialize<object>(jobsJson),
                    new System.Text.Json.JsonSerializerOptions
                    {
                        WriteIndented = true
                    });

            var jobsPath = System.IO.Path.Combine(
                dumpDir, "api_dump_jobs.json");
            await System.IO.File.WriteAllTextAsync(
                jobsPath, jobsPretty);

            _logger.LogInformation(
                "Jobs dump written to {Path}", jobsPath);

            // ── 3. Jobs/GetDetails — most recent job ──────────────
            // Extract first job UID from the jobs response
            try
            {
                var jobsDoc = System.Text.Json.JsonDocument
                    .Parse(jobsJson);

                var firstUid = jobsDoc.RootElement
                    .GetProperty("data")[0]
                    .GetProperty("uid")
                    .GetString();

                if (!string.IsNullOrWhiteSpace(firstUid))
                {
                    var detailsResponse = await http.GetAsync(
                        $"{baseUrl}/jobs/GetDetails" +
                        $"?id={firstUid}");

                    var detailsJson = await detailsResponse
                        .Content.ReadAsStringAsync();

                    var detailsPretty = System.Text.Json
                        .JsonSerializer.Serialize(
                            System.Text.Json.JsonSerializer
                                .Deserialize<object>(detailsJson),
                            new System.Text.Json.JsonSerializerOptions
                            {
                                WriteIndented = true
                            });

                    var detailsPath = System.IO.Path.Combine(
                        dumpDir, "api_dump_job_details.json");
                    await System.IO.File.WriteAllTextAsync(
                        detailsPath, detailsPretty);

                    _logger.LogInformation(
                        "Job details dump written to {Path} " +
                        "(job UID: {Uid})",
                        detailsPath, firstUid);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Could not dump job details — " +
                    "no recent jobs found.");
            }

            // ── 4. Account/Test ───────────────────────────────────
            var accountResponse = await http.GetAsync(
                $"{baseUrl}/account/Test");

            var accountJson = await accountResponse.Content
                .ReadAsStringAsync();

            var accountPretty = System.Text.Json.JsonSerializer
                .Serialize(
                    System.Text.Json.JsonSerializer
                        .Deserialize<object>(accountJson),
                    new System.Text.Json.JsonSerializerOptions
                    {
                        WriteIndented = true
                    });

            var accountPath = System.IO.Path.Combine(
                dumpDir, "api_dump_account.json");
            await System.IO.File.WriteAllTextAsync(
                accountPath, accountPretty);

            _logger.LogInformation(
                "Account dump written to {Path}", accountPath);

            // ── 5. Account/GetStatistics — last 30 days ───────────────
            try
            {
                var endTs = DateTimeOffset.UtcNow
                    .ToUnixTimeSeconds().ToString();
                var startTs = DateTimeOffset.UtcNow
                    .AddDays(-30).ToUnixTimeSeconds().ToString();

                var statsBody =
                    "{\"start_date\":\"" + startTs + "\"," +
                    "\"end_date\":\"" + endTs + "\"}";

                _logger.LogInformation(
                    "Stats body: {Body}", statsBody);

                var statsResp = await http.PostAsync(
                    $"{baseUrl}/account/GetStatistics",
                    new System.Net.Http.StringContent(
                        statsBody,
                        System.Text.Encoding.UTF8,
                        "application/json"));

                var statsJson = await statsResp.Content
                    .ReadAsStringAsync();

                _logger.LogInformation(
                    "Stats raw response ({Status}): {Json}",
                    (int)statsResp.StatusCode,
                    statsJson);

                var statsPath = System.IO.Path.Combine(
                    dumpDir, "api_dump_statistics.json");

                await System.IO.File.WriteAllTextAsync(
                    statsPath, statsJson);

                _logger.LogInformation(
                    "Statistics dump written to {Path}", statsPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Statistics dump failed — continuing.");
            }

            SetFooterStatus(
                $"API dump complete — files written to {dumpDir}");

            btn.Content = "Done ✓";
            await Task.Delay(3000);
            btn.Content = "Dump API";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "API dump failed.");
            SetFooterStatus("API dump failed — check logs.",
                isError: true);
            btn.Content = "Dump API";
        }
        finally
        {
            btn.IsEnabled = true;
        }
    }
}