using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MTGB.Config;
using MTGB.Core.Security;
using MTGB.Services;
using MTGB.UI;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;

namespace MTGB;

internal class Program
{
    private const string SingleInstanceMutexName =
        @"Local\MTGB.TheMonitorThatGoesBing";

    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Any(a => string.Equals(
                a,
                "--cleanup-uninstall",
                StringComparison.OrdinalIgnoreCase)))
        {
            RunUninstallCleanup();
            return;
        }

        using var singleInstanceMutex = new Mutex(
            initiallyOwned: true,
            name: SingleInstanceMutexName,
            createdNew: out var isFirstInstance);

        if (!isFirstInstance)
        {
            MessageBox.Show(
                "MTGB is already running.\n\n" +
                "The Ministry refuses to stamp the same form twice.",
                "MTGB — Already Running",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var host = CreateHost(args);

        // Build the WPF app first on the STA thread
        var app = new App();
        app.SetHost(host);

        try
        {
            app.InitializeComponent();
            app.Run();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"MTGB failed to start.\n\n{ex.Message}\n\n" +
                $"Please check the logs in the 'Data\\logs' folder " +
                "next to the application.",
                "MTGB — It does not go Bing",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            host.StopAsync().GetAwaiter().GetResult();
            host.Dispose();
            singleInstanceMutex.ReleaseMutex();
        }
    }

    private static IHost CreateHost(string[] args) =>
    Host.CreateDefaultBuilder(args)
        .ConfigureAppConfiguration((context, config) =>
        {
            // Ensure Data directory exists
            DataPaths.EnsureDirectoriesExist();

            config
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile(DataPaths.SettingsFile,
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
            WriteLogHeader();
            logging.AddFile(
                DataPaths.LogFilePattern,
                outputTemplate:
                "{Timestamp:yyyy-MM-ddTHH:mm:ss.fffffffzzz}  " +
                "[{Level:u3}] {Message:lj}{NewLine}{Exception}");
        })
        .ConfigureServices((context, services) =>
        {
            // ── Configuration ─────────────────────────────────────────────
            services.Configure<AppSettings>(
                context.Configuration);
            services.AddSingleton<ISettingsStore,
                SettingsStore>();

            // ── Security ────────────────────────────────────────────────────────
            services.AddSingleton<ICredentialManager,
                WindowsCredentialManager>();
            services.AddSingleton<WebhookSecretManager>();

            // ── HTTP client ─────────────────────────────────────────────────────
            services.AddHttpClient<ISimplyPrintApiClient,
                SimplyPrintApiClient>((provider, client) =>
                {
                    client.BaseAddress = new Uri(
                        "https://api.simplyprint.io/");
                    client.Timeout = TimeSpan.FromSeconds(30);
                    client.DefaultRequestHeaders.Add(
                        "User-Agent", "MTGB/0.1.0");
                });

            // ── Auth ─────────────────────────────────────────────────────────────────────
            services.AddSingleton<IAuthService, AuthService>();

            // ── Core services ────────────────────────────────────────────────────────
            services.AddSingleton<IStateDiffEngine,
                StateDiffEngine>();
            services.AddSingleton<INotificationManager,
                NotificationManager>();

            // ── Background workers ─────────────────────────────────────────────────
            services.AddHostedService<PollingWorker>();
            services.AddHostedService<WebhookWorker>();

            // ── UI ─────────────────────────────────────────────────────────────────────
            services.AddTransient<InductionWindow>();
            services.AddTransient<FlyoutWindow>();
            services.AddTransient<SettingsWindow>();
            services.AddTransient<HistoryWindow>();
            services.AddTransient<UpdateWindow>();
            services.AddSingleton<TrayIcon>();

            // ── Community Map ──────────────────────────────────────────────────────
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

            // ── Telemetry ────────────────────────────────────────────────────────────
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

            // ── Update Service ──────────────────────────────────────────────────────
            services.AddHttpClient<IUpdateService, UpdateService>(
                (provider, client) =>
                {
                    client.BaseAddress = new Uri(
                        "https://community.myndworx.com/");
                    client.Timeout = TimeSpan.FromSeconds(30);
                    client.DefaultRequestHeaders.Add(
                        "User-Agent",
                        $"MTGB/{typeof(App).Assembly.GetName()
                            .Version?.ToString(3)} " +
                        "(https://github.com/Rayvenhaus/mtgb)");
                });

            services.AddHostedService<UpdateWorker>();
        })
        .Build();

    private static void WriteLogHeader()
    {
        try
        {
            DataPaths.EnsureDirectoriesExist();

            var version = typeof(Program).Assembly
                .GetName()
                .Version
                ?.ToString() ?? "unknown";

            var header = string.Join(
                Environment.NewLine,
                string.Empty,
                "============================================================",
                "MTGB - The Monitor That Goes Bing           MRFWVP Form 3201/4d",
                "Log session started",
                "------------------------------------------------------------",
                "Ministry of Reduction of Filament Waste and Void Prevention",
                "hereby opens this log in accordance with all applicable",
                "forms, sub-forms, side-forms, and forms denying the existence",
                "of the previous forms.",
                string.Empty,
                "The machine has been observed. It may go Bing.",
                "Should it fail to go Bing, the following dry entries will",
                "explain precisely how disappointed everyone should be.",
                "------------------------------------------------------------",
                $"Started:      {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}",
                $"Version:      {version}",
                $"Process ID:   {Environment.ProcessId}",
                $"Install path: {AppContext.BaseDirectory}",
                $"Log file:     {DataPaths.CurrentLogFile}",
                "============================================================",
                string.Empty);

            File.AppendAllText(DataPaths.CurrentLogFile, header);
        }
        catch
        {
            // Logging must never prevent the app from starting.
        }
    }

    private static void RunUninstallCleanup()
    {
        try
        {
            DataPaths.EnsureDirectoriesExist();

            var settings = LoadSettings();
            DeleteStartupEntry();
            DeleteToastRegistration();
            DeleteCredentials();
            DeregisterInstallationAsync(settings)
                .GetAwaiter()
                .GetResult();
            DeleteRuntimeData();
        }
        catch
        {
            // Uninstall cleanup must never block MSI removal.
            // Best-effort cleanup is better than a stuck uninstall.
        }
    }

    private static AppSettings? LoadSettings()
    {
        try
        {
            if (!File.Exists(DataPaths.SettingsFile))
                return null;

            var json = File.ReadAllText(DataPaths.SettingsFile);
            return JsonSerializer.Deserialize<AppSettings>(
                json,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
        }
        catch
        {
            return null;
        }
    }

    private static void DeleteStartupEntry()
    {
        const string runKey =
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

        using var key = Microsoft.Win32.Registry
            .CurrentUser
            .OpenSubKey(runKey, writable: true);

        key?.DeleteValue("MTGB", throwOnMissingValue: false);
    }

    private static void DeleteToastRegistration()
    {
        const string appId = "MTGB.TheMonitorThatGoesBing";
        var regPath = $@"SOFTWARE\Classes\AppUserModelId\{appId}";

        Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(
            regPath,
            throwOnMissingSubKey: false);
    }

    private static void DeleteCredentials()
    {
        var credentials = new WindowsCredentialManager();

        foreach (CredentialKey key in
                 Enum.GetValues<CredentialKey>())
        {
            credentials.Delete(key);
        }
    }

    private static void DeleteRuntimeData()
    {
        TryDeleteDirectory(DataPaths.LogsPath);

        if (!Directory.Exists(DataPaths.DataPath))
            return;

        foreach (var file in Directory.EnumerateFiles(
                     DataPaths.DataPath))
        {
            TryDeleteFile(file);
        }

        foreach (var directory in Directory.EnumerateDirectories(
                     DataPaths.DataPath))
        {
            if (string.Equals(
                    Path.GetFileName(directory),
                    "assets",
                    StringComparison.OrdinalIgnoreCase))
                continue;

            TryDeleteDirectory(directory);
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Best-effort uninstall cleanup.
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best-effort uninstall cleanup.
        }
    }

    private static async Task DeregisterInstallationAsync(
        AppSettings? settings)
    {
        if (string.IsNullOrWhiteSpace(settings?.InstallId))
            return;

        using var client = new HttpClient
        {
            BaseAddress = new Uri("https://community.myndworx.com/"),
            Timeout = TimeSpan.FromSeconds(10)
        };

        client.DefaultRequestHeaders.Add(
            "User-Agent",
            $"MTGB/{typeof(Program).Assembly.GetName()
                .Version?.ToString(3)} uninstall");

        var body = JsonSerializer.Serialize(new
        {
            install_id = settings.InstallId
        });

        var endpoints = new[]
        {
            "mtgb/v1/installations",
            "mtgb/v1/installations.php"
        };

        foreach (var endpoint in endpoints)
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Delete,
                endpoint)
            {
                Content = new StringContent(
                    body,
                    Encoding.UTF8,
                    "application/json")
            };

            var response = await client.SendAsync(request);
            if (response.IsSuccessStatusCode)
                return;
        }
    }
}
