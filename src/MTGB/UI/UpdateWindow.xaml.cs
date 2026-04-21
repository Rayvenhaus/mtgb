using Microsoft.Extensions.Logging;
using MTGB.Services;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MTGB.UI;

/// <summary>
/// MTGB Update Window.
/// Shows release notes and download progress.
/// OK TO INSTALL button enables when download completes.
/// The Ministry keeps its software current.
/// </summary>
public partial class UpdateWindow : Window
{
    private readonly IUpdateService _updateService;
    private readonly ILogger<UpdateWindow> _logger;
    private readonly ReleaseInfo _release;

    private string? _downloadedMsixPath;
    private double _progressBarMaxWidth;

    public UpdateWindow(
        IUpdateService updateService,
        ReleaseInfo release,
        ILogger<UpdateWindow> logger)
    {
        _updateService = updateService;
        _release = release;
        _logger = logger;

        InitializeComponent();
        Loaded += OnLoaded;
    }

    // ── Startup ───────────────────────────────────────────────────

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Dark title bar
        var hwnd = new System.Windows.Interop
            .WindowInteropHelper(this).Handle;
        int darkMode = 1;
        DwmSetWindowAttribute(hwnd, 20,
            ref darkMode, sizeof(int));

        // Populate content
        var current = typeof(UpdateWindow).Assembly
            .GetName().Version?.ToString(3) ?? "0.0.0";

        VersionHeader.Text =
            $"Version {_release.Version} is available";
        CurrentVersionText.Text =
            $"You are running v{current}";
        ReleaseNotesText.Text = _release.ReleaseNotes;

        // Cache progress bar max width after render
        Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Loaded,
            new Action(() =>
            {
                _progressBarMaxWidth =
                    ((Border)ProgressFill.Parent).ActualWidth - 2;
            }));

        // Start download immediately
        _ = StartDownloadAsync();
    }

    // ── Download ──────────────────────────────────────────────────

    private async Task StartDownloadAsync()
    {
        ProgressPanel.Visibility = Visibility.Visible;
        StatusText.Text = "Downloading update...";
        CancelButton.IsEnabled = false;

        var progress = new Progress<int>(percent =>
        {
            Dispatcher.Invoke(() =>
            {
                ProgressText.Text =
                    $"Downloading... {percent}%";

                ProgressFill.Width = Math.Max(0,
                    _progressBarMaxWidth * percent / 100.0);

                if (percent >= 100)
                {
                    ProgressText.Text = "Download complete.";
                    ProgressFill.Background =
                        new SolidColorBrush(
                            Color.FromRgb(0x3B, 0xB2, 0x73));
                }
            });
        });

        using var cts = new CancellationTokenSource(
            TimeSpan.FromMinutes(10));

        _downloadedMsixPath = await _updateService
            .DownloadUpdateAsync(_release, progress, cts.Token);

        if (_downloadedMsixPath is null ||
            !File.Exists(_downloadedMsixPath))
        {
            Dispatcher.Invoke(() =>
            {
                StatusText.Text =
                    "Download failed. Please try again later.";
                StatusText.Foreground = new SolidColorBrush(
                    Color.FromRgb(0xE8, 0x48, 0x55));
                ProgressText.Text = "Download failed.";
                CancelButton.IsEnabled = true;
                CancelButton.Content = "CLOSE";
            });
            return;
        }

        Dispatcher.Invoke(() =>
        {
            StatusText.Text =
                "Ready to install. " +
                "MTGB will close and the installer will launch.";
            InstallButton.IsEnabled = true;
            CancelButton.IsEnabled = true;
        });
    }

    // ── Handlers ──────────────────────────────────────────────────

    private void OnInstallClick(
        object sender, RoutedEventArgs e)
    {
        if (_downloadedMsixPath is null) return;

        _logger.LogInformation(
            "User confirmed update to v{Version}. " +
            "The Ministry is applying the update.",
            _release.Version);

        _updateService.InstallUpdate(_downloadedMsixPath);
    }

    private void OnCancelClick(
        object sender, RoutedEventArgs e)
    {
        _logger.LogInformation(
            "User deferred update to v{Version}. " +
            "The Ministry notes this without judgement.",
            _release.Version);

        Close();
    }

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