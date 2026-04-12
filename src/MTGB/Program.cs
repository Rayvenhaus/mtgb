using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MTGB.Config;
using MTGB.Core.Security;
using MTGB.Services;
using System.Windows;

namespace MTGB;

/// <summary>
/// MTGB — The Monitor That Goes Bing.
/// Never leave a print behind.
/// 
/// Entry point and dependency injection wiring.
/// All services are registered here and composed via 
/// the .NET 8 Generic Host.
/// </summary>
internal class Program
{
    [STAThread]
    public static async Task Main(string[] args)
    {
        // WPF requires STA thread — [STAThread] above handles this.
        // The Generic Host runs async services on background threads.

        var host = CreateHost(args);

        try
        {
            await host.StartAsync();

            // Boot the WPF application on the STA thread.
            // This blocks until the app exits.
            var app = host.Services.GetRequiredService<App>();
            app.InitializeComponent();
            app.Run();
        }
        catch (Exception ex)
        {
            // Last-resort catch — something went catastrophically wrong
            // before the UI was even up.
            MessageBox.Show(
                $"MTGB failed to start.\n\n{ex.Message}\n\n" +
                $"Please check the logs in %APPDATA%\\MTGB\\logs\\",
                "MTGB — It does not go Bing",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    private static IHost CreateHost(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((context, config) =>
            {
                config
                    .SetBasePath(AppContext.BaseDirectory)
                    .AddJsonFile("appsettings.json",
                        optional: false,
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
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "MTGB", "logs", "mtgb-.log"));
            })
            .ConfigureServices((context, services) =>
            {
                // ── Configuration ─────────────────────────────────
                services.Configure<AppSettings>(
                    context.Configuration);

                // ── Security ──────────────────────────────────────
                services.AddSingleton<ICredentialManager,
                    WindowsCredentialManager>();
                services.AddSingleton<WebhookSecretManager>();

                // ── HTTP client ───────────────────────────────────
                services.AddHttpClient<ISimplyPrintApiClient,
                    SimplyPrintApiClient>((provider, client) =>
                    {
                        client.BaseAddress = new Uri(
                            "https://api.simplyprint.io/");
                        client.Timeout = TimeSpan.FromSeconds(30);
                        client.DefaultRequestHeaders.Add(
                            "User-Agent", "MTGB/0.1.0");
                    });

                // ── Auth ──────────────────────────────────────────
                services.AddSingleton<IAuthService, AuthService>();

                // ── Core services ─────────────────────────────────
                services.AddSingleton<IStateDiffEngine,
                    StateDiffEngine>();
                services.AddSingleton<INotificationManager,
                    NotificationManager>();

                // ── Background workers ────────────────────────────
                services.AddHostedService<PollingWorker>();
                services.AddHostedService<WebhookWorker>();

                // ── WPF App ───────────────────────────────────────
                services.AddSingleton<App>();
            })
            .Build();
}