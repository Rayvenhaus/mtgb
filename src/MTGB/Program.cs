using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MTGB.Config;
using MTGB.Core.Security;
using MTGB.Services;
using MTGB.UI;
using System.IO;
using System.Windows;

namespace MTGB;

internal class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var host = CreateHost(args);

        // Build the WPF app first on the STA thread
        var app = new App();
        app.SetServiceProvider(host.Services);

        // Start background services without blocking the STA thread
        Task.Run(async () => await host.StartAsync());

        try
        {
            app.InitializeComponent();
            app.Run();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"MTGB failed to start.\n\n{ex.Message}\n\n" +
                $"Please check the logs in %APPDATA%\\MTGB\\logs\\",
                "MTGB — It does not go Bing",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            host.StopAsync().GetAwaiter().GetResult();
            host.Dispose();
        }
    }

    private static IHost CreateHost(string[] args) =>
    Host.CreateDefaultBuilder(args)
        .ConfigureAppConfiguration((context, config) =>
        {
            var appDataDir = Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.ApplicationData),
                "MTGB");

            Directory.CreateDirectory(appDataDir);

            var appDataSettings = Path.Combine(
                appDataDir, "appsettings.json");
            var builtInSettings = Path.Combine(
                AppContext.BaseDirectory, "appsettings.json");

            if (!File.Exists(appDataSettings) &&
                File.Exists(builtInSettings))
            {
                File.Copy(builtInSettings, appDataSettings);
            }

            config
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json",
                    optional: false,
                    reloadOnChange: false)
                .AddJsonFile(appDataSettings,
                    optional: true,
                    reloadOnChange: true)
                .AddJsonFile("appsettings.local.json",
                    optional: true,
                    reloadOnChange: true)
                .AddEnvironmentVariables("MTGB_")
                .AddCommandLine(args);
        })
        .ConfigureLogging((context, logging) =>
        {
            logging.ClearProviders();
            logging.AddConsole();
            logging.AddDebug();
            logging.AddFile(Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.ApplicationData),
                "MTGB", "logs", "mtgb-.log"));
        })
        .ConfigureServices((context, services) =>
        {
            // ── Configuration ─────────────────────────────
            services.Configure<AppSettings>(
                context.Configuration);

            // ── Security ──────────────────────────────────
            services.AddSingleton<ICredentialManager,
                WindowsCredentialManager>();
            services.AddSingleton<WebhookSecretManager>();

            // ── HTTP client ───────────────────────────────
            services.AddHttpClient<ISimplyPrintApiClient,
                SimplyPrintApiClient>((provider, client) =>
                {
                    client.BaseAddress = new Uri(
                        "https://api.simplyprint.io/");
                    client.Timeout = TimeSpan.FromSeconds(30);
                    client.DefaultRequestHeaders.Add(
                        "User-Agent", "MTGB/0.1.0");
                });

            // ── Auth ──────────────────────────────────────
            services.AddSingleton<IAuthService, AuthService>();

            // ── Core services ─────────────────────────────
            services.AddSingleton<IStateDiffEngine,
                StateDiffEngine>();
            services.AddSingleton<INotificationManager,
                NotificationManager>();

            // ── Background workers ────────────────────────
            services.AddHostedService<PollingWorker>();
            services.AddHostedService<WebhookWorker>();

            // ── UI ────────────────────────────────────────
            services.AddTransient<InductionWindow>();
            services.AddTransient<FlyoutWindow>();
            services.AddTransient<SettingsWindow>();
            services.AddTransient<HistoryWindow>();
            services.AddSingleton<TrayIcon>();

            // ── Community Map ─────────────────────────────
            services.AddHttpClient<ICommunityMapService,
                CommunityMapService>((provider, client) =>
                {
                    client.BaseAddress = new Uri(
                        "https://community.myndworx.com/");
                    client.Timeout = TimeSpan.FromSeconds(15);
                    client.DefaultRequestHeaders.Add(
                        "User-Agent",
                        $"MTGB/{typeof(App).Assembly.GetName().Version?.ToString(3)}");
                });

            // ── Telemetry ─────────────────────────────────
            services.AddHttpClient<ITelemetryService, TelemetryService>(
                (provider, client) =>
                {
                    client.BaseAddress = new Uri(
                        "https://community.myndworx.com/");
                    client.Timeout = TimeSpan.FromSeconds(15);
                    client.DefaultRequestHeaders.Add(
                        "User-Agent",
                        $"MTGB/{typeof(App).Assembly.GetName()
                            .Version?.ToString(3)}");
                });

            services.AddHostedService<TelemetryWorker>();
        })
        .Build();
}