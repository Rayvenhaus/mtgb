using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MTGB.Config;
using MTGB.Core.Security;
using MTGB.UI;
using System.Windows;

namespace MTGB;

/// <summary>
/// MTGB WPF Application.
/// Manages application lifetime, first-run experience,
/// and tray icon initialisation.
/// </summary>
public partial class App : Application
{
    private readonly ILogger<App> _logger;
    private readonly IServiceProvider _services;
    private readonly IOptions<AppSettings> _settings;
    private readonly ICredentialManager _credentials;
    private readonly IHostApplicationLifetime _lifetime;

    private TrayIcon? _trayIcon;

    public App(
        ILogger<App> logger,
        IServiceProvider services,
        IOptions<AppSettings> settings,
        ICredentialManager credentials,
        IHostApplicationLifetime lifetime)
    {
        _logger = logger;
        _services = services;
        _settings = settings;
        _credentials = credentials;
        _lifetime = lifetime;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _logger.LogInformation(
            "MTGB {Version} starting up. It goes Bing.",
            GetType().Assembly.GetName().Version);

        // Wire up global exception handlers so crashes
        // are logged rather than silently swallowed
        SetupExceptionHandling();

        // First run check — if no credentials exist,
        // show the settings window before anything else
        if (IsFirstRun())
        {
            _logger.LogInformation(
                "First run detected — launching setup.");
            ShowFirstRunSetup();
        }

        // Boot the tray icon
        _trayIcon = _services.GetRequiredService<TrayIcon>();
        _trayIcon.Initialise();

        _logger.LogInformation("MTGB ready. Watching your prints.");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _logger.LogInformation("MTGB shutting down. Goodbye.");

        _trayIcon?.Dispose();

        // Signal the Generic Host to stop background services
        _lifetime.StopApplication();

        base.OnExit(e);
    }

    /// <summary>
    /// First run is defined as no Organisation ID configured
    /// and no credentials stored.
    /// </summary>
    private bool IsFirstRun() =>
        _settings.Value.OrganisationId == 0 &&
        !_credentials.Exists(CredentialKey.ApiKey) &&
        !_credentials.Exists(CredentialKey.OAuthAccessToken);

    private void ShowFirstRunSetup()
    {
        var settingsWindow = _services
            .GetRequiredService<SettingsWindow>();
        settingsWindow.ShowDialog();
    }

    private void SetupExceptionHandling()
    {
        // Unhandled exceptions on the UI thread
        DispatcherUnhandledException += (sender, e) =>
        {
            _logger.LogError(e.Exception,
                "Unhandled UI thread exception.");

            MessageBox.Show(
                $"Something went wrong.\n\n{e.Exception.Message}\n\n" +
                $"Check logs in %APPDATA%\\MTGB\\logs\\ for details.",
                "MTGB — Unexpected Bing",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            // Mark as handled so the app doesn't crash
            e.Handled = true;
        };

        // Unhandled exceptions on background threads
        AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
        {
            _logger.LogCritical(
                e.ExceptionObject as Exception,
                "Unhandled background thread exception. " +
                "IsTerminating: {IsTerminating}",
                e.IsTerminating);
        };

        // Unhandled exceptions in async Tasks
        TaskScheduler.UnobservedTaskException += (sender, e) =>
        {
            _logger.LogError(e.Exception,
                "Unobserved Task exception.");
            e.SetObserved();
        };
    }
}