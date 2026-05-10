using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MTGB.Config;
using MTGB.Core.Security;
using MTGB.UI;
using System.IO;
using System.Windows;

namespace MTGB;

public partial class App : Application
{
    private ILogger<App>? _logger;
    private IHost? _host;
    private IServiceProvider? _services;
    private IOptions<AppSettings>? _settings;
    private ICredentialManager? _credentials;
    private IHostApplicationLifetime? _lifetime;

    private TrayIcon? _trayIcon;

    // Called by Program.cs after the host is built
    public void SetHost(IHost host)
    {
        _host = host;
        var services = host.Services;

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

        _logger?.LogInformation(
            "MTGB {Version} starting up. It goes Bing.",
            GetType().Assembly.GetName().Version);

        SetupExceptionHandling();

        // Register AppUserModelID so Windows associates toasts
        // with the correct app icon and name
        RegisterAppUserModelId();

        if (IsFirstRun())
        {
            _logger?.LogInformation(
                "First run detected — launching Induction " +
                "(Form MwA 621d/7 22). The Ministry awaits.");
            var inductionCompleted = ShowFirstRunSetup();

            if (!inductionCompleted)
            {
                _logger?.LogInformation(
                    "Induction did not complete — shutting down " +
                    "without starting the tray icon.");
                Shutdown();
                return;
            }
        }

        _trayIcon = _services!.GetRequiredService<TrayIcon>();
        _trayIcon.Initialise();

        _ = Task.Run(StartHostAsync);

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
        !_settings!.Value.Inducted;

    private async Task StartHostAsync()
    {
        try
        {
            await _host!.StartAsync();
        }
        catch (Exception ex)
        {
            _logger?.LogCritical(ex,
                "Failed to start MTGB background services.");

            Dispatcher.Invoke(() =>
            {
                MessageBox.Show(
                    "MTGB started, but its background services did not.\n\n" +
                    ex.Message,
                    "MTGB — It does not go Bing",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            });
        }
    }

    private bool ShowFirstRunSetup()
    {
        bool? result = null;

        Dispatcher.Invoke(() =>
        {
            var induction = _services!
                .GetRequiredService<InductionWindow>();
            result = induction.ShowDialog();
        });

        return result == true && !IsFirstRun();
    }

    private static void RegisterAppUserModelId()
    {
        const string appId = "MTGB.TheMonitorThatGoesBing";

        // Set the AppUserModelID for the current process
        // This must be called before any toast notifications fire
        SetCurrentProcessExplicitAppUserModelID(appId);

        // Register the app in the Start Menu for toast delivery
        // Required for toasts to appear in Action Centre
        var regPath = $@"SOFTWARE\Classes\AppUserModelId\{appId}";

        using var key = Microsoft.Win32.Registry
            .CurrentUser
            .CreateSubKey(regPath);

        if (key is null) return;

        key.SetValue("DisplayName",
            "The Monitor That Goes Bing");
        key.SetValue("IconUri",
            Environment.ProcessPath
            ?? Path.Combine(AppContext.BaseDirectory, "MTGB.exe"));
    }

    [System.Runtime.InteropServices.DllImport(
        "shell32.dll",
        SetLastError = true)]

    private static extern void
        SetCurrentProcessExplicitAppUserModelID(
            [System.Runtime.InteropServices.MarshalAs(
            System.Runtime.InteropServices.UnmanagedType.LPWStr)]
        string appId);

    private void SetupExceptionHandling()
    {
        DispatcherUnhandledException += (sender, e) =>
        {
            _logger?.LogError(e.Exception,
                "Unhandled UI thread exception.");
            MessageBox.Show(
                $"Something went wrong.\n\n{e.Exception.Message}\n\n" +
                $"Check logs in {DataPaths.LogsPath} for details.",
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
}
