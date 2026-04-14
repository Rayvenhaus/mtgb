using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MTGB.Config;
using MTGB.Core.Security;
using MTGB.UI;
using System.Windows;

namespace MTGB;

public partial class App : Application
{
    private ILogger<App>? _logger;
    private IServiceProvider? _services;
    private IOptions<AppSettings>? _settings;
    private ICredentialManager? _credentials;
    private IHostApplicationLifetime? _lifetime;

    private TrayIcon? _trayIcon;

    // Called by Program.cs after the host is built
    public void SetServiceProvider(IServiceProvider services)
    {
        _services = services;
        _logger = services.GetRequiredService<ILogger<App>>();
        _settings = services.GetRequiredService<IOptions<AppSettings>>();
        _credentials = services
            .GetRequiredService<ICredentialManager>();
        _lifetime = services
            .GetRequiredService<IHostApplicationLifetime>();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Force dark title bar on all MTGB windows
        // regardless of Windows theme setting
        foreach (Window window in Windows)
            ForceDarkTitleBar(window);

        _logger?.LogInformation(
            "MTGB {Version} starting up. It goes Bing.",
            GetType().Assembly.GetName().Version);

        SetupExceptionHandling();

        // TODO: Re-enable once SettingsWindow is built
        // if (IsFirstRun())
        // {
        //     _logger?.LogInformation(
        //         "First run detected — launching setup.");
        //     ShowFirstRunSetup();
        // }

        _trayIcon = _services!.GetRequiredService<TrayIcon>();
        _trayIcon.Initialise();

        _logger?.LogInformation(
            "MTGB ready. Watching your prints.");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _logger?.LogInformation("MTGB shutting down. Goodbye.");
        _trayIcon?.Dispose();
        _lifetime?.StopApplication();
        base.OnExit(e);
    }

    private bool IsFirstRun() =>
        _settings?.Value.OrganisationId == 0 &&
        _credentials?.Exists(CredentialKey.ApiKey) == false &&
        _credentials?.Exists(
            CredentialKey.OAuthAccessToken) == false;

    private void ShowFirstRunSetup()
    {
        Dispatcher.Invoke(() =>
        {
            var settingsWindow = _services!
                .GetRequiredService<SettingsWindow>();
            settingsWindow.ShowDialog();
        });
    }

    private void SetupExceptionHandling()
    {
        DispatcherUnhandledException += (sender, e) =>
        {
            _logger?.LogError(e.Exception,
                "Unhandled UI thread exception.");
            MessageBox.Show(
                $"Something went wrong.\n\n{e.Exception.Message}\n\n" +
                $"Check logs in %APPDATA%\\MTGB\\logs\\ for details.",
                "MTGB — Unexpected Bing",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            e.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
        {
            _logger?.LogCritical(
                e.ExceptionObject as Exception,
                "Unhandled background thread exception. " +
                "IsTerminating: {IsTerminating}",
                e.IsTerminating);
        };

        TaskScheduler.UnobservedTaskException += (sender, e) =>
        {
            _logger?.LogError(e.Exception,
                "Unobserved Task exception.");
            e.SetObserved();
        };
    }

    private static void ForceDarkTitleBar(Window window)
    {
        var hwnd = new System.Windows.Interop
            .WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;

        int darkMode = 1;
        DwmSetWindowAttribute(hwnd, 20,
            ref darkMode, sizeof(int));
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int attr,
        ref int attrValue,
        int attrSize);
}