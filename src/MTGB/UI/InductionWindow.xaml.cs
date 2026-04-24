using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Win32;
using MTGB.Config;
using MTGB.Core.Security;
using MTGB.Services;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace MTGB.UI;

/// <summary>
/// MTGB Induction — Form MwA 621d/7 22.
/// The Ministry of Printer Observation &amp; Void Containment welcomes you.
/// We are here to help.
/// We have forms. In triplicate.
/// </summary>
public partial class InductionWindow : Window
{
    private readonly IOptions<AppSettings> _settings;
    private readonly ICredentialManager _credentials;
    private readonly ISimplyPrintApiClient _apiClient;
    private readonly IAuthService _authService;
    private readonly ILogger<InductionWindow> _logger;
    private readonly ICommunityMapService _communityMap;

    private int _currentScreen = 1;
    private const int TotalScreens = 7;
    private bool _connectionVerified = false;

    private List<CountryData> _countries = new();
    private CountryData? _selectedCountry;

    // ── Navy & Gold palette ───────────────────────────────────────
    private static readonly Color GoldPrimary = Color.FromRgb(0xfb, 0xbd, 0x23);
    private static readonly Color AccentBlue = Color.FromRgb(0x3c, 0x83, 0xf6);
    private static readonly Color TextDim = Color.FromRgb(0xa0, 0xae, 0xc0);
    private static readonly Color Green = Color.FromRgb(0x3B, 0xB2, 0x73);
    private static readonly Color Red = Color.FromRgb(0xE8, 0x48, 0x55);
    private static readonly Color Amber = Color.FromRgb(0xF1, 0x8F, 0x01);
    private static readonly Color BgDeepest = Color.FromRgb(0x0f, 0x1f, 0x4a);

    public InductionWindow(
        IOptions<AppSettings> settings,
        ICredentialManager credentials,
        ISimplyPrintApiClient apiClient,
        IAuthService authService,
        ICommunityMapService communityMap,
        ILogger<InductionWindow> logger)
    {
        _settings = settings;
        _credentials = credentials;
        _apiClient = apiClient;
        _authService = authService;
        _communityMap = communityMap;
        _logger = logger;

        InitializeComponent();
        Loaded += OnLoaded;
    }

    // ── Startup ───────────────────────────────────────────────────

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var hwnd = new System.Windows.Interop
            .WindowInteropHelper(this).Handle;
        int darkMode = 1;
        DwmSetWindowAttribute(hwnd, 20,
            ref darkMode, sizeof(int));

        OrgIdInput.TextChanged += (_, _) => UpdateTestConnectionButton();
        ApiKeyInput.PasswordChanged += (_, _) => UpdateTestConnectionButton();

        LoadCountries();
        UpdateScreen();
        UpdateTestConnectionButton();
    }

    private void LoadCountries()
    {
        var countryList = _communityMap.LoadCountries();
        if (countryList is null) return;

        _countries = countryList.Countries;

        CountrySelector.ItemsSource = _countries
            .Select(c => c.Name)
            .ToList();
    }

    private void UpdateTestConnectionButton()
    {
        var orgIdFilled = !string.IsNullOrWhiteSpace(OrgIdInput.Text);
        var apiKeyFilled = ApiKeyInput.SecurePassword.Length > 0;
        var bothFilled = orgIdFilled && apiKeyFilled;

        TestConnectionButton.IsEnabled = bothFilled;

        TestConnectionButton.Foreground = new SolidColorBrush(
            bothFilled ? AccentBlue : TextDim);

        TestConnectionButton.BorderBrush = new SolidColorBrush(
            bothFilled ? AccentBlue : Color.FromRgb(0x2c, 0x4d, 0xba));
    }

    // ── Navigation ────────────────────────────────────────────────

    private async void OnContinueClick(
        object sender, RoutedEventArgs e)
    {
        if (_currentScreen == TotalScreens)
        {
            CompleteInduction();
            return;
        }

        if (_currentScreen == 2 && !_connectionVerified)
        {
            SetConnectionStatus(
                "Please test your connection before continuing.",
                isError: true);
            return;
        }

        if (_currentScreen == 3)
        {
            var startWithWindows = StartupToggle.IsChecked == true;
            _settings.Value.Ui.StartWithWindows = startWithWindows;
            SetWindowsStartup(startWithWindows);
        }

        if (_currentScreen == 4)
        {
            _settings.Value.Telemetry.Enabled =
                TelemetryToggle.IsChecked == true;
        }

        if (_currentScreen == 5)
        {
            await HandleRegistryScreenAsync();
            _settings.Value.GetOrCreateInstallId();
            PopulateSummaryScreen();
        }

        _currentScreen++;
        UpdateScreen();
    }

    private void OnBackClick(
        object sender, RoutedEventArgs e)
    {
        _currentScreen--;
        UpdateScreen();
    }

    private void OnExitClick(
        object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "The Ministry notes your departure.\n\n" +
            "The Ministry also notes that your printers will " +
            "continue printing, largely unobserved, into the void.\n\n" +
            "MTGB will run the Induction again on next launch.",
            "MTGB — Leaving So Soon?",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.OK) return;

        _logger.LogInformation(
            "User exited Induction — cleaning up and shutting down.");

        SetWindowsStartup(false);
        _settings.Value.OrganisationId = 0;
        _settings.Value.Telemetry.Enabled = false;
        _settings.Value.InstallId = string.Empty;

        if (_credentials.Exists(CredentialKey.ApiKey))
            _credentials.Delete(CredentialKey.ApiKey);
        if (_credentials.Exists(CredentialKey.WebhookSecret))
            _credentials.Delete(CredentialKey.WebhookSecret);
        if (_credentials.Exists(CredentialKey.OAuthAccessToken))
            _credentials.Delete(CredentialKey.OAuthAccessToken);
        if (_credentials.Exists(CredentialKey.OAuthRefreshToken))
            _credentials.Delete(CredentialKey.OAuthRefreshToken);

        _settings.Value.CommunityMap.Registered = false;
        _settings.Value.CommunityMap.CountryCode = null;
        _settings.Value.CommunityMap.CountryName = null;
        _settings.Value.CommunityMap.StateName = null;
        _settings.Value.CommunityMap.DisplayName = null;

        _logger.LogInformation(
            "Induction cleanup complete. " +
            "The Ministry has filed the abandonment form. " +
            "In triplicate.");

        Application.Current.Shutdown();
    }

    // ── Screen management ─────────────────────────────────────────

    private void UpdateScreen()
    {
        Screen1.Visibility = _currentScreen == 1
            ? Visibility.Visible : Visibility.Collapsed;
        Screen2.Visibility = _currentScreen == 2
            ? Visibility.Visible : Visibility.Collapsed;
        Screen3.Visibility = _currentScreen == 3
            ? Visibility.Visible : Visibility.Collapsed;
        Screen4.Visibility = _currentScreen == 4
            ? Visibility.Visible : Visibility.Collapsed;
        Screen5.Visibility = _currentScreen == 5
            ? Visibility.Visible : Visibility.Collapsed;
        Screen6.Visibility = _currentScreen == 6
            ? Visibility.Visible : Visibility.Collapsed;
        Screen7.Visibility = _currentScreen == 7
            ? Visibility.Visible : Visibility.Collapsed;

        UpdateDots();

        BackButton.Visibility = _currentScreen > 1
            ? Visibility.Visible : Visibility.Collapsed;

        ContinueButton.Content = _currentScreen == TotalScreens
            ? "Done ✓" : "Continue →";

        ContinueButton.IsEnabled =
            _currentScreen != 2 || _connectionVerified;

        ScreenIndicatorText.Text =
            $"{_currentScreen} of {TotalScreens}";
    }

    private void UpdateDots()
    {
        var dots = new[]
        {
            Dot1, Dot2, Dot3, Dot4, Dot5, Dot6, Dot7
        };

        for (int i = 0; i < dots.Length; i++)
        {
            dots[i].Fill = new SolidColorBrush(
                i + 1 == _currentScreen
                    ? GoldPrimary
                    : i + 1 < _currentScreen
                        ? AccentBlue
                        : Color.FromRgb(0x2c, 0x4d, 0xba));
        }
    }

    // ── Summary screen ────────────────────────────────────────────

    private void PopulateSummaryScreen()
    {
        var s = _settings.Value;

        SummaryOrgId.Text = s.OrganisationId.ToString();
        SummaryAuthMode.Text = s.AuthMode.ToString();

        SummaryStartWithWindows.Text = s.Ui.StartWithWindows
            ? "Enabled" : "Disabled";
        SummaryStartWithWindows.Foreground = new SolidColorBrush(
            s.Ui.StartWithWindows ? Green : TextDim);

        SummaryTelemetry.Text = s.Telemetry.Enabled
            ? "Enabled" : "Disabled";
        SummaryTelemetry.Foreground = new SolidColorBrush(
            s.Telemetry.Enabled ? Green : TextDim);

        SummaryCommunityMap.Text = s.CommunityMap.Registered
            ? $"Registered — {s.CommunityMap.DisplayName}"
            : "Not registered";
        SummaryCommunityMap.Foreground = new SolidColorBrush(
            s.CommunityMap.Registered ? Green : TextDim);

        SummaryInstallId.Text = s.InstallId;
    }

    // ── Connection test ───────────────────────────────────────────

    private async void OnTestConnectionClick(
        object sender, RoutedEventArgs e)
    {
        _connectionVerified = false;
        ContinueButton.IsEnabled = false;
        TestConnectionButton.IsEnabled = false;

        SetConnectionStatus("Contacting the Ministry...",
            isPending: true);

        try
        {
            if (!int.TryParse(
                OrgIdInput.Text.Trim(), out var orgId)
                || orgId <= 0)
            {
                SetConnectionStatus(
                    "Invalid organisation ID — " +
                    "check your SimplyPrint URL.",
                    isError: true);
                return;
            }

            var apiKey = ApiKeyInput.Password;
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                SetConnectionStatus(
                    "API key is required.",
                    isError: true);
                return;
            }

            var result = await _authService
                .LoginWithApiKeyAsync(apiKey, orgId);

            if (result.Success)
            {
                _settings.Value.OrganisationId = orgId;
                _connectionVerified = true;
                ContinueButton.IsEnabled = true;

                SetConnectionStatus(
                    "Connected. The Ministry approves.",
                    isSuccess: true);

                _logger.LogInformation(
                    "Induction connection verified " +
                    "for org {OrgId}.", orgId);
            }
            else
            {
                SetConnectionStatus(
                    result.ErrorMessage ??
                    "Connection failed. " +
                    "Check your credentials and try again.",
                    isError: true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Induction connection test failed.");
            SetConnectionStatus(
                "Connection failed — check logs for details.",
                isError: true);
        }
        finally
        {
            TestConnectionButton.IsEnabled = true;
        }
    }

    private void SetConnectionStatus(
        string message,
        bool isError = false,
        bool isSuccess = false,
        bool isPending = false)
    {
        ConnectionStatusPanel.Visibility = Visibility.Visible;

        var colour = isError ? Red
                   : isSuccess ? Green
                   : isPending ? Amber
                   : TextDim;

        ConnectionDot.Fill = new SolidColorBrush(colour);
        ConnectionStatusText.Foreground = new SolidColorBrush(colour);
        ConnectionStatusText.Text = message;

        TestConnectionButton.Foreground = new SolidColorBrush(colour);
        TestConnectionButton.BorderBrush = new SolidColorBrush(
            isSuccess ? Green
            : isError ? Red
            : AccentBlue);
    }

    // ── Completion ────────────────────────────────────────────────

    private void CompleteInduction()
    {
        try
        {
            _settings.Value.Inducted = true;
            SaveSettings();

            _logger.LogInformation(
                "Induction complete. " +
                "Form MwA 621d/7 22 filed. " +
                "The scribe has made four copies.");

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to complete induction.");
            MessageBox.Show(
                "Something went wrong saving your settings.\n\n" +
                ex.Message,
                "MTGB — Induction Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void SaveSettings()
    {
        var appDataDir = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.ApplicationData),
            "MTGB");

        Directory.CreateDirectory(appDataDir);

        var path = Path.Combine(appDataDir, "appsettings.json");

        var json = JsonSerializer.Serialize(
            _settings.Value,
            new JsonSerializerOptions { WriteIndented = true });

        File.WriteAllText(path, json);
    }

    // ── Startup registry ──────────────────────────────────────────

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
            key.DeleteValue(appName, throwOnMissingValue: false);
        }
    }

    // ── Registry screen ───────────────────────────────────────────

    private async Task HandleRegistryScreenAsync()
    {
        if (MapToggle.IsChecked != true) return;
        if (_selectedCountry is null) return;

        var stateName = _selectedCountry.HasStates
            ? StateSelector.SelectedItem?.ToString()
            : null;

        await _communityMap.RegisterAsync(
            _selectedCountry.Code,
            _selectedCountry.Name,
            stateName);
    }

    private void OnCountryChanged(
        object sender,
        System.Windows.Controls.SelectionChangedEventArgs e)
    {
        var selectedName = CountrySelector.SelectedItem?.ToString();

        _selectedCountry = _countries
            .FirstOrDefault(c => c.Name == selectedName);

        if (_selectedCountry is null) return;

        if (_selectedCountry.HasStates)
        {
            StateSelectorPanel.Visibility = Visibility.Visible;
            StateSelector.ItemsSource = _selectedCountry.States;
            StateSelector.SelectedIndex = -1;
        }
        else
        {
            StateSelectorPanel.Visibility = Visibility.Collapsed;
            StateSelector.ItemsSource = null;
        }

        UpdateConfirmationText();
    }

    private void OnStateChanged(
        object sender,
        System.Windows.Controls.SelectionChangedEventArgs e)
    {
        UpdateConfirmationText();
    }

    private void OnMapToggleChanged(
        object sender, RoutedEventArgs e)
    {
        MapSelectionPanel.Visibility =
            MapToggle.IsChecked == true
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

    private void UpdateConfirmationText()
    {
        if (_selectedCountry is null)
        {
            RegistrationConfirmText.Text = string.Empty;
            return;
        }

        var stateName = _selectedCountry.HasStates
            ? StateSelector.SelectedItem?.ToString()
            : null;

        RegistrationConfirmText.Text = stateName is not null
            ? $"You'll be added to the count of installations " +
              $"in {_selectedCountry.Name}, in {stateName}."
            : $"You'll be added to the count of installations " +
              $"in {_selectedCountry.Name}.";
    }

    // ── Window drag ───────────────────────────────────────────────

    private void OnTitleBarDrag(
        object sender,
        System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.LeftButton ==
            System.Windows.Input.MouseButtonState.Pressed)
            DragMove();
    }

    // ── DWM dark title bar ────────────────────────────────────────

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd, int attr,
        ref int attrValue, int attrSize);
}