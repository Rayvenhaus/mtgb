using Microsoft.Extensions.Options;
using MTGB.Config;
using MTGB.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace MTGB.UI;

/// <summary>
/// MTGB Flyout Panel.
/// Slides up from the taskbar on left-click of the tray icon.
/// Shows live printer status cards with hover flavour text.
/// Navy and Gold. It goes BING.
/// </summary>
public partial class FlyoutWindow : Window
{
    private readonly IOptions<AppSettings> _settings;
    private readonly TrayIcon _trayIcon;

    private Action? _onHistoryClick;
    private Action? _onSettingsClick;
    private Action? _onDashboardClick;
    private Action? _onExitClick;

    // ── Navy & Gold palette ───────────────────────────────────────
    private static readonly Color BgDeepest = Color.FromRgb(0x0f, 0x1f, 0x4a);
    private static readonly Color BgPrimary = Color.FromRgb(0x1e, 0x3b, 0x8a);
    private static readonly Color BgRaised = Color.FromRgb(0x2c, 0x4d, 0xba);
    private static readonly Color GoldPrimary = Color.FromRgb(0xfb, 0xbd, 0x23);
    private static readonly Color AccentBlue = Color.FromRgb(0x3c, 0x83, 0xf6);
    private static readonly Color TextPrimary = Color.FromRgb(0xf5, 0xf5, 0xf5);
    private static readonly Color TextMuted = Color.FromRgb(0xd1, 0xd5, 0xdb);
    private static readonly Color TextDim = Color.FromRgb(0xa0, 0xae, 0xc0);

    public FlyoutWindow(
        IOptions<AppSettings> settings,
        TrayIcon trayIcon)
    {
        _settings = settings;
        _trayIcon = trayIcon;
        InitializeComponent();

        SourceInitialized += (_, _) =>
        {
            var hwnd = new System.Windows.Interop
                .WindowInteropHelper(this).Handle;
            var extendedStyle = NativeMethods.GetWindowLong(
                hwnd, NativeMethods.GWL_EXSTYLE);
            NativeMethods.SetWindowLong(hwnd,
                NativeMethods.GWL_EXSTYLE,
                extendedStyle | NativeMethods.WS_EX_TOOLWINDOW);
        };

        Deactivated += (_, _) => SlideDown();
        Loaded += (_, _) => SyncMuteToggle();
    }

    // ── Wiring ────────────────────────────────────────────────────

    public void SetCallbacks(
        Action onHistory,
        Action onSettings,
        Action onDashboard,
        Action onExit)
    {
        _onHistoryClick = onHistory;
        _onSettingsClick = onSettings;
        _onDashboardClick = onDashboard;
        _onExitClick = onExit;
    }

    // ── Printer cards ─────────────────────────────────────────────

    public void RefreshPrinterCards(
        IReadOnlyDictionary<int, PrinterSnapshot> snapshots)
    {
        Dispatcher.Invoke(() =>
        {
            PrinterCardsPanel.Children.Clear();

            if (!snapshots.Any())
            {
                NoPrintersPanel.Visibility = Visibility.Visible;
                return;
            }

            NoPrintersPanel.Visibility = Visibility.Collapsed;

            var printingCount = snapshots.Values
                .Count(s => s.State.Equals("printing",
                    StringComparison.OrdinalIgnoreCase));

            var alertCount = snapshots.Values
                .Count(s => !s.Online ||
                    s.State.Equals("error",
                        StringComparison.OrdinalIgnoreCase));

            UpdateBingDot(printingCount, alertCount);

            foreach (var snapshot in snapshots.Values
                .OrderBy(s => s.PrinterName))
            {
                var card = BuildPrinterCard(snapshot);
                PrinterCardsPanel.Children.Add(card);
            }

            UpdateLastPolled();
            UpdateWebhookStatus();
        });
    }

    private void UpdateBingDot(int printing, int alerts)
    {
        BingDot.Fill = alerts > 0
            ? new SolidColorBrush(Color.FromRgb(0xE8, 0x48, 0x55))
            : printing > 0
                ? new SolidColorBrush(GoldPrimary)
                : new SolidColorBrush(Color.FromRgb(0x3B, 0xB2, 0x73));
    }

    private Border BuildPrinterCard(PrinterSnapshot snapshot)
    {
        var (statusColor, stateLabel) = GetStatusInfo(snapshot);
        var flavourText = _trayIcon.GetFlavourText(snapshot);

        var card = new Border
        {
            Style = (Style)FindResource("PrinterCard"),
            ToolTip = BuildFlavourTooltip(flavourText),
            Tag = snapshot.PrinterId
        };

        card.MouseEnter += (_, _) =>
            card.Background = new SolidColorBrush(BgDeepest);
        card.MouseLeave += (_, _) =>
            card.Background = new SolidColorBrush(BgRaised);

        var stack = new StackPanel();

        // ── Top row — status dot + name + state ──────────────────
        var topRow = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        topRow.ColumnDefinitions.Add(
            new ColumnDefinition { Width = GridLength.Auto });
        topRow.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });
        topRow.ColumnDefinitions.Add(
            new ColumnDefinition { Width = GridLength.Auto });

        var dot = new Ellipse
        {
            Width = 9,
            Height = 9,
            Fill = new SolidColorBrush(statusColor),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        Grid.SetColumn(dot, 0);

        var nameText = new TextBlock
        {
            Text = snapshot.PrinterName,
            FontFamily = new FontFamily("Segoe UI Variable, Segoe UI"),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(TextPrimary),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(nameText, 1);

        var stateText = new TextBlock
        {
            Text = stateLabel.ToUpperInvariant(),
            FontFamily = new FontFamily("Segoe UI Variable, Segoe UI"),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(statusColor),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(stateText, 2);

        topRow.Children.Add(dot);
        topRow.Children.Add(nameText);
        topRow.Children.Add(stateText);
        stack.Children.Add(topRow);

        // ── Progress bar ──────────────────────────────────────────
        if (snapshot.JobPercentage.HasValue &&
            snapshot.JobPercentage > 0)
        {
            var progressBg = new Border
            {
                Height = 5,
                Background = new SolidColorBrush(
                    Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
                CornerRadius = new CornerRadius(3),
                Margin = new Thickness(0, 0, 0, 5)
            };

            var progressFill = new Border
            {
                Height = 5,
                Background = new SolidColorBrush(statusColor),
                CornerRadius = new CornerRadius(3),
                HorizontalAlignment = HorizontalAlignment.Left,
                Width = (snapshot.JobPercentage.Value / 100.0)
                                      * (300 - 24)
            };

            var progressGrid = new Grid
            {
                Margin = new Thickness(0, 0, 0, 5)
            };
            progressGrid.Children.Add(progressBg);
            progressGrid.Children.Add(progressFill);
            stack.Children.Add(progressGrid);
        }

        // ── Meta row — filename + time ────────────────────────────
        var metaRow = new Grid();
        metaRow.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });
        metaRow.ColumnDefinitions.Add(
            new ColumnDefinition { Width = GridLength.Auto });

        var filenameText = new TextBlock
        {
            Text = TruncateFilename(
                snapshot.ActiveJobFilename ?? GetIdleText(snapshot)),
            FontFamily = new FontFamily("Segoe UI Variable, Segoe UI"),
            FontSize = 12,
            Foreground = new SolidColorBrush(TextMuted),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(filenameText, 0);

        var timeText = new TextBlock
        {
            Text = FormatTimeRemaining(snapshot.JobTimeRemaining),
            FontFamily = new FontFamily("Segoe UI Variable, Segoe UI"),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(GoldPrimary)
        };
        Grid.SetColumn(timeText, 1);

        metaRow.Children.Add(filenameText);
        metaRow.Children.Add(timeText);
        stack.Children.Add(metaRow);

        card.Child = stack;
        return card;
    }

    private ToolTip BuildFlavourTooltip(string flavourText)
    {
        return new ToolTip
        {
            Content = flavourText,
            Background = new SolidColorBrush(BgDeepest),
            Foreground = new SolidColorBrush(GoldPrimary),
            BorderBrush = new SolidColorBrush(
                Color.FromArgb(0x59, 0x3c, 0x83, 0xf6)),
            BorderThickness = new Thickness(1),
            FontFamily = new FontFamily("Segoe UI Variable, Segoe UI"),
            FontSize = 12,
            FontStyle = FontStyles.Italic,
            Padding = new Thickness(10, 6, 10, 6)
        };
    }

    // ── Helpers ───────────────────────────────────────────────────

    private static (Color color, string label)
        GetStatusInfo(PrinterSnapshot snapshot)
    {
        if (!snapshot.Online)
            return (Color.FromRgb(0xa0, 0xae, 0xc0), "Offline");

        return snapshot.State.ToLowerInvariant() switch
        {
            "printing" or "printing_completing" =>
                (Color.FromRgb(0x3c, 0x83, 0xf6), "Printing"),
            "paused" or "pausing" =>
                (Color.FromRgb(0xF1, 0x8F, 0x01), "Paused"),
            "error" or "printer_error" =>
                (Color.FromRgb(0xE8, 0x48, 0x55), "Error"),
            "idle" or "operational" =>
                (Color.FromRgb(0x3B, 0xB2, 0x73), "Ready"),
            _ =>
                (Color.FromRgb(0xa0, 0xae, 0xc0), snapshot.State)
        };
    }

    private static string GetIdleText(PrinterSnapshot snapshot)
    {
        if (!snapshot.Online) return "Offline";
        return snapshot.State.ToLowerInvariant() switch
        {
            "idle" or "operational" => "Bed clear · Ready",
            "error" or "printer_error" => "Attention required",
            _ => snapshot.State
        };
    }

    private static string TruncateFilename(string? filename)
    {
        if (string.IsNullOrWhiteSpace(filename))
            return string.Empty;

        filename = filename.Replace(".gcode", "")
                           .Replace(".GCODE", "");

        return filename.Length > 28
            ? filename[..25] + "..."
            : filename;
    }

    private static string FormatTimeRemaining(int? seconds)
    {
        if (!seconds.HasValue || seconds <= 0)
            return string.Empty;

        var ts = TimeSpan.FromSeconds(seconds.Value);
        return ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours}h {ts.Minutes}m"
            : $"{ts.Minutes}m";
    }

    private void UpdateLastPolled()
    {
        LastPolledText.Text = $"Updated {DateTime.Now:HH:mm:ss}";
    }

    private void UpdateWebhookStatus()
    {
        var webhookEnabled = _settings.Value.Webhook.Enabled;
        WebhookStatusText.Text = webhookEnabled
            ? "Webhook ● live"
            : "Polling ● active";
    }

    private void SyncMuteToggle()
    {
        MuteToggle.IsChecked =
            _settings.Value.Notifications.GlobalMuteEnabled;

        MutedBanner.Visibility =
            _settings.Value.Notifications.GlobalMuteEnabled
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

    // ── Slide animation ───────────────────────────────────────────

    public void SlideUp()
    {
        Left = -9999;
        Top = -9999;

        if (!IsLoaded)
        {
            ContentRendered += OnContentRenderedPosition;
            Show();
        }
        else
        {
            Show();
            PositionAboveTaskbar();
            ReplaySlideAnimation();
        }

        Activate();
    }

    private void ReplaySlideAnimation()
    {
        SlideTransform.Y = 20;
        MainBorder.Opacity = 0;

        var slideIn = new DoubleAnimation
        {
            From = 20,
            To = 0,
            Duration = TimeSpan.FromMilliseconds(180),
            EasingFunction = new CubicEase
            {
                EasingMode = EasingMode.EaseOut
            }
        };

        var fadeIn = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(180)
        };

        SlideTransform.BeginAnimation(
            TranslateTransform.YProperty, slideIn);
        MainBorder.BeginAnimation(
            UIElement.OpacityProperty, fadeIn);
    }

    private void OnContentRenderedPosition(
        object? sender, EventArgs e)
    {
        ContentRendered -= OnContentRenderedPosition;
        PositionAboveTaskbar();
        ReplaySlideAnimation();
    }

    private void PositionAboveTaskbar()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - 12;
        Top = workArea.Bottom - ActualHeight - 12;
    }

    public void SlideDown()
    {
        var slideOut = new DoubleAnimation
        {
            From = 0,
            To = 20,
            Duration = TimeSpan.FromMilliseconds(150),
            EasingFunction = new CubicEase
            {
                EasingMode = EasingMode.EaseIn
            }
        };

        var fadeOut = new DoubleAnimation
        {
            From = 1,
            To = 0,
            Duration = TimeSpan.FromMilliseconds(150)
        };

        slideOut.Completed += (_, _) => Hide();

        SlideTransform.BeginAnimation(
            TranslateTransform.YProperty, slideOut);
        MainBorder.BeginAnimation(
            UIElement.OpacityProperty, fadeOut);
    }

    // ── Button handlers ───────────────────────────────────────────

    private void OnMuteToggleClick(
        object sender, RoutedEventArgs e)
    {
        var muted = MuteToggle.IsChecked == true;
        _settings.Value.Notifications.GlobalMuteEnabled = muted;
        MutedBanner.Visibility = muted
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void OnHistoryClick(
        object sender, RoutedEventArgs e)
    {
        SlideDown();
        _onHistoryClick?.Invoke();
    }

    private void OnSettingsClick(
        object sender, RoutedEventArgs e)
    {
        SlideDown();
        _onSettingsClick?.Invoke();
    }

    private void OnDashboardClick(
        object sender, RoutedEventArgs e) =>
        _onDashboardClick?.Invoke();

    private void OnExitClick(
        object sender, RoutedEventArgs e) =>
        _onExitClick?.Invoke();

    // ── Native methods ────────────────────────────────────────────

    internal static class NativeMethods
    {
        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TOOLWINDOW = 0x00000080;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern int GetWindowLong(
            IntPtr hwnd, int index);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern int SetWindowLong(
            IntPtr hwnd, int index, int newStyle);
    }
}