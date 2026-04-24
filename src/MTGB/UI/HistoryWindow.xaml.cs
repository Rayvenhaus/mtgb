using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MTGB.Config;
using MTGB.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MTGB.UI;

public partial class HistoryWindow : Window
{
    private readonly INotificationManager _notificationManager;
    private readonly IOptions<AppSettings> _settings;
    private readonly ILogger<HistoryWindow> _logger;

    private string _activeFilter = "all";
    private int _activePrinterId = 0;
    private IReadOnlyList<NotificationHistoryEntry> _allEntries
        = new List<NotificationHistoryEntry>();

    // ── Navy & Gold palette ───────────────────────────────────────
    private static readonly Color BgDeepest = Color.FromRgb(0x0f, 0x1f, 0x4a);
    private static readonly Color BgPrimary = Color.FromRgb(0x1e, 0x3b, 0x8a);
    private static readonly Color BgRaised = Color.FromRgb(0x2c, 0x4d, 0xba);
    private static readonly Color GoldPrimary = Color.FromRgb(0xfb, 0xbd, 0x23);
    private static readonly Color AccentBlue = Color.FromRgb(0x3c, 0x83, 0xf6);
    private static readonly Color TextPrimary = Color.FromRgb(0xf5, 0xf5, 0xf5);
    private static readonly Color TextMuted = Color.FromRgb(0xd1, 0xd5, 0xdb);
    private static readonly Color TextDim = Color.FromRgb(0xa0, 0xae, 0xc0);
    private static readonly Color Green = Color.FromRgb(0x3B, 0xB2, 0x73);
    private static readonly Color Red = Color.FromRgb(0xE8, 0x48, 0x55);
    private static readonly Color Amber = Color.FromRgb(0xF1, 0x8F, 0x01);

    public HistoryWindow(
        INotificationManager notificationManager,
        IOptions<AppSettings> settings,
        ILogger<HistoryWindow> logger)
    {
        _notificationManager = notificationManager;
        _settings = settings;
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

        Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Loaded,
            new Action(LoadHistory));
    }

    private void LoadHistory()
    {
        _allEntries = _notificationManager.GetHistory();
        PopulatePrinterFilter();
        ApplyFilter();
    }

    // ── Printer filter combobox ───────────────────────────────────

    private void PopulatePrinterFilter()
    {
        if (PrinterFilter is null) return;

        PrinterFilter.Items.Clear();
        PrinterFilter.Items.Add(
            new ComboBoxItem
            {
                Content = "All printers",
                Tag = 0,
                IsSelected = true
            });

        var printers = _allEntries
            .Select(e => new { e.PrinterId, e.PrinterName })
            .DistinctBy(p => p.PrinterId)
            .OrderBy(p => p.PrinterName);

        foreach (var printer in printers)
        {
            PrinterFilter.Items.Add(
                new ComboBoxItem
                {
                    Content = printer.PrinterName,
                    Tag = printer.PrinterId
                });
        }
    }

    // ── Filtering ─────────────────────────────────────────────────

    private void ApplyFilter()
    {
        if (HistoryPanel is null || FooterCountText is null) return;

        var filtered = _allEntries.AsEnumerable();

        if (_activePrinterId > 0)
            filtered = filtered.Where(
                e => e.PrinterId == _activePrinterId);

        filtered = _activeFilter switch
        {
            "jobs" => filtered.Where(e =>
                                e.EventId.StartsWith("job.")),
            "printer" => filtered.Where(e =>
                                e.EventId.StartsWith("printer.")),
            "alerts" => filtered.Where(e =>
                                e.EventId is "job.failed" or
                                "printer.offline" or
                                "filament.low" or
                                "temp.nozzle.low" or
                                "temp.bed.low"),
            "suppressed" => filtered.Where(e => e.WasSuppressed),
            _ => filtered
        };

        var results = filtered.ToList();
        RenderHistory(results);

        FooterCountText.Text = results.Count == 0
            ? "No entries matching current filter."
            : $"{results.Count} " +
              $"{(results.Count == 1 ? "entry" : "entries")} — " +
              $"history capped at 1000. " +
              $"The Redundant Department has been notified.";
    }

    // ── Rendering ─────────────────────────────────────────────────

    private void RenderHistory(
        List<NotificationHistoryEntry> entries)
    {
        if (HistoryPanel is null) return;

        HistoryPanel.Children.Clear();

        if (!entries.Any())
        {
            HistoryPanel.Children.Add(BuildEmptyState());
            return;
        }

        var grouped = entries
            .GroupBy(e => e.Timestamp.LocalDateTime.Date)
            .OrderByDescending(g => g.Key);

        foreach (var group in grouped)
        {
            HistoryPanel.Children.Add(
                BuildDateHeader(group.Key));

            foreach (var entry in group
                .OrderByDescending(e => e.Timestamp))
            {
                HistoryPanel.Children.Add(
                    BuildHistoryRow(entry));
            }
        }
    }

    private Border BuildDateHeader(DateTime date)
    {
        var label = date.Date == DateTime.Today
            ? "Today"
            : date.Date == DateTime.Today.AddDays(-1)
                ? "Yesterday"
                : date.ToString("dddd d MMMM yyyy");

        return new Border
        {
            Padding = new Thickness(4, 12, 4, 4),
            Child = new TextBlock
            {
                Text = label.ToUpperInvariant(),
                FontFamily = new FontFamily("Segoe UI Variable, Segoe UI"),
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(AccentBlue)
            }
        };
    }

    private Border BuildHistoryRow(
        NotificationHistoryEntry entry)
    {
        var (accent, _) = GetEventAccent(entry);

        var row = new Border
        {
            Background = new SolidColorBrush(
                entry.WasSuppressed ? BgDeepest : BgPrimary),
            BorderBrush = new SolidColorBrush(
                Color.FromArgb(
                    entry.WasSuppressed ? (byte)0x10 : (byte)0x28,
                    accent.R, accent.G, accent.B)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(10, 10, 10, 10),
            Opacity = entry.WasSuppressed ? 0.55 : 1.0
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(
            new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });
        grid.ColumnDefinitions.Add(
            new ColumnDefinition { Width = GridLength.Auto });

        // Accent bar
        var accentBar = new Border
        {
            Width = 3,
            CornerRadius = new CornerRadius(2),
            Background = new SolidColorBrush(accent),
            Margin = new Thickness(0, 0, 12, 0),
            Opacity = entry.WasSuppressed ? 0.4 : 1.0
        };
        Grid.SetColumn(accentBar, 0);

        // Content
        var content = new StackPanel();

        var titleRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 3)
        };

        var eventName = new TextBlock
        {
            Text = entry.EventDisplayName,
            FontFamily = new FontFamily("Segoe UI Variable, Segoe UI"),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(
                entry.WasSuppressed ? TextDim : TextPrimary),
            Margin = new Thickness(0, 0, 10, 0)
        };

        var printerName = new TextBlock
        {
            Text = entry.PrinterName,
            FontFamily = new FontFamily("Segoe UI Variable, Segoe UI"),
            FontSize = 11,
            Foreground = new SolidColorBrush(accent),
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = entry.WasSuppressed ? 0.6 : 1.0
        };

        titleRow.Children.Add(eventName);
        titleRow.Children.Add(printerName);
        content.Children.Add(titleRow);

        if (!string.IsNullOrWhiteSpace(entry.JobFilename))
        {
            content.Children.Add(new TextBlock
            {
                Text = entry.JobFilename
                                   .Replace(".gcode", "")
                                   .Replace(".GCODE", ""),
                FontFamily = new FontFamily("Segoe UI Variable, Segoe UI"),
                FontSize = 12,
                Foreground = new SolidColorBrush(TextMuted),
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 0, 0, 2)
            });
        }

        if (entry.WasSuppressed &&
            !string.IsNullOrWhiteSpace(entry.SuppressReason))
        {
            content.Children.Add(new TextBlock
            {
                Text = $"Suppressed — {entry.SuppressReason}",
                FontFamily = new FontFamily("Segoe UI Variable, Segoe UI"),
                FontSize = 11,
                FontStyle = FontStyles.Italic,
                Foreground = new SolidColorBrush(TextDim)
            });
        }

        Grid.SetColumn(content, 1);

        var timestamp = new TextBlock
        {
            Text = entry.Timestamp.LocalDateTime.ToString("HH:mm:ss"),
            FontFamily = new FontFamily("Segoe UI Variable, Segoe UI"),
            FontSize = 11,
            Foreground = new SolidColorBrush(TextDim),
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(10, 2, 0, 0)
        };
        Grid.SetColumn(timestamp, 2);

        grid.Children.Add(accentBar);
        grid.Children.Add(content);
        grid.Children.Add(timestamp);
        row.Child = grid;

        return row;
    }

    private static (Color accent, string icon)
        GetEventAccent(NotificationHistoryEntry entry)
    {
        return entry.EventId switch
        {
            "job.finished" => (Color.FromRgb(0x3B, 0xB2, 0x73), "✓"),
            "job.failed" => (Color.FromRgb(0xE8, 0x48, 0x55), "✕"),
            "job.started" => (Color.FromRgb(0x3c, 0x83, 0xf6), "▶"),
            "job.paused" => (Color.FromRgb(0xF1, 0x8F, 0x01), "⏸"),
            "job.cancelled" => (Color.FromRgb(0xE8, 0x48, 0x55), "✕"),
            "job.resumed" => (Color.FromRgb(0x3c, 0x83, 0xf6), "▶"),
            "printer.offline" => (Color.FromRgb(0xE8, 0x48, 0x55), "⚠"),
            "printer.online" => (Color.FromRgb(0x3B, 0xB2, 0x73), "✓"),
            "filament.low" => (Color.FromRgb(0xF1, 0x8F, 0x01), "⚠"),
            "progress.25" or
            "progress.50" or
            "progress.75" => (Color.FromRgb(0x3c, 0x83, 0xf6), "◉"),
            "temp.nozzle.low" or
            "temp.bed.low" => (Color.FromRgb(0xF1, 0x8F, 0x01), "⚠"),
            _ => (Color.FromRgb(0xfb, 0xbd, 0x23), "·")
        };
    }

    private UIElement BuildEmptyState()
    {
        var panel = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 40, 0, 0)
        };

        panel.Children.Add(new TextBlock
        {
            Text = "No notifications recorded.",
            FontFamily = new FontFamily("Segoe UI Variable, Segoe UI"),
            FontSize = 14,
            Foreground = new SolidColorBrush(TextMuted),
            HorizontalAlignment = HorizontalAlignment.Center
        });

        panel.Children.Add(new TextBlock
        {
            Text = "The Ministry is at peace. This is suspicious.",
            FontFamily = new FontFamily("Segoe UI Variable, Segoe UI"),
            FontSize = 12,
            FontStyle = FontStyles.Italic,
            Foreground = new SolidColorBrush(TextDim),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 6, 0, 0)
        });

        return panel;
    }

    // ── View toggle ───────────────────────────────────────────────

    private void OnViewHistoryClick(
        object sender, RoutedEventArgs e)
    {
        HistoryView.Visibility = Visibility.Visible;
        StatsView.Visibility = Visibility.Collapsed;
        ViewHistoryButton.Style = (Style)FindResource("FilterButtonActive");
        ViewStatsButton.Style = (Style)FindResource("FilterButton");
    }

    private void OnViewStatsClick(
        object sender, RoutedEventArgs e)
    {
        HistoryView.Visibility = Visibility.Collapsed;
        StatsView.Visibility = Visibility.Visible;
        ViewHistoryButton.Style = (Style)FindResource("FilterButton");
        ViewStatsButton.Style = (Style)FindResource("FilterButtonActive");
        BuildStatsView();
    }

    // ── Stats view ────────────────────────────────────────────────

    private void BuildStatsView()
    {
        StatsPanel.Children.Clear();

        var settings = _settings.Value;
        var entries = _allEntries;

        // Telemetry
        StatsPanel.Children.Add(BuildStatsSectionHeader("TELEMETRY"));
        StatsPanel.Children.Add(BuildStatsRow(
            "Anonymous telemetry",
            settings.Telemetry.Enabled ? "Enabled" : "Disabled",
            settings.Telemetry.Enabled ? Green : TextDim));
        StatsPanel.Children.Add(BuildStatsRow(
            "Install ID",
            string.IsNullOrWhiteSpace(settings.InstallId)
                ? "Not yet generated"
                : $"{settings.InstallId[..8]}••••••••••••••••••••",
            TextDim));

        // Community map
        StatsPanel.Children.Add(BuildStatsSectionHeader("COMMUNITY MAP"));
        StatsPanel.Children.Add(BuildStatsRow(
            "Registration status",
            settings.CommunityMap.Registered
                ? $"Registered — {settings.CommunityMap.DisplayName}"
                : "Not registered",
            settings.CommunityMap.Registered ? Green : TextDim));

        // Notification history
        StatsPanel.Children.Add(BuildStatsSectionHeader("NOTIFICATION HISTORY"));

        var totalEntries = entries.Count;
        var suppressed = entries.Count(e => e.WasSuppressed);
        var delivered = totalEntries - suppressed;
        var critical = entries.Count(e => e.IsCritical);
        var failures = entries.Count(e => e.EventId == "job.failed");
        var successes = entries.Count(e => e.EventId == "job.finished");

        StatsPanel.Children.Add(BuildStatsRow(
            "Total events recorded", totalEntries.ToString(), GoldPrimary));
        StatsPanel.Children.Add(BuildStatsRow(
            "Notifications delivered", delivered.ToString(), Green));
        StatsPanel.Children.Add(BuildStatsRow(
            "Notifications suppressed", suppressed.ToString(), TextDim));
        StatsPanel.Children.Add(BuildStatsRow(
            "Critical alerts", critical.ToString(), Red));
        StatsPanel.Children.Add(BuildStatsRow(
            "Prints finished", successes.ToString(), Green));
        StatsPanel.Children.Add(BuildStatsRow(
            "Prints failed", failures.ToString(),
            failures > 0 ? Red : TextDim));

        // Event breakdown
        StatsPanel.Children.Add(BuildStatsSectionHeader("EVENT BREAKDOWN"));

        var eventCounts = entries
            .Where(e => !e.WasSuppressed)
            .GroupBy(e => e.EventDisplayName)
            .OrderByDescending(g => g.Count())
            .Take(10);

        foreach (var group in eventCounts)
        {
            StatsPanel.Children.Add(BuildStatsRow(
                group.Key, group.Count().ToString(), AccentBlue));
        }

        if (!eventCounts.Any())
        {
            StatsPanel.Children.Add(new TextBlock
            {
                Text = "No events delivered yet. The Ministry is at peace.",
                FontFamily = new FontFamily("Segoe UI Variable, Segoe UI"),
                FontSize = 12,
                FontStyle = FontStyles.Italic,
                Foreground = new SolidColorBrush(TextDim),
                Margin = new Thickness(4, 8, 4, 4)
            });
        }

        // Footer note
        StatsPanel.Children.Add(new TextBlock
        {
            Text = "Statistics are derived from local notification history only.\n" +
                           "History is capped at 1000 entries.",
            FontFamily = new FontFamily("Segoe UI Variable, Segoe UI"),
            FontSize = 11,
            FontStyle = FontStyles.Italic,
            Foreground = new SolidColorBrush(TextDim),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(4, 20, 4, 4)
        });
    }

    private Border BuildStatsSectionHeader(string title)
    {
        return new Border
        {
            Padding = new Thickness(4, 16, 4, 6),
            Child = new TextBlock
            {
                Text = title,
                FontFamily = new FontFamily("Segoe UI Variable, Segoe UI"),
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(AccentBlue)
            }
        };
    }

    private Border BuildStatsRow(
        string label,
        string value,
        Color valueColour)
    {
        var row = new Border
        {
            Background = new SolidColorBrush(BgPrimary),
            BorderBrush = new SolidColorBrush(
                Color.FromArgb(0x28, 0x3c, 0x83, 0xf6)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(10, 11, 10, 11)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });
        grid.ColumnDefinitions.Add(
            new ColumnDefinition { Width = GridLength.Auto });

        var labelText = new TextBlock
        {
            Text = label,
            FontFamily = new FontFamily("Segoe UI Variable, Segoe UI"),
            FontSize = 13,
            Foreground = new SolidColorBrush(TextMuted),
            VerticalAlignment = VerticalAlignment.Center
        };

        var valueText = new TextBlock
        {
            Text = value,
            FontFamily = new FontFamily("Segoe UI Variable, Segoe UI"),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(valueColour),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16, 0, 0, 0)
        };

        Grid.SetColumn(labelText, 0);
        Grid.SetColumn(valueText, 1);

        grid.Children.Add(labelText);
        grid.Children.Add(valueText);
        row.Child = grid;

        return row;
    }

    // ── Filter handlers ───────────────────────────────────────────

    private void SetActiveFilter(string filter, Button activeButton)
    {
        _activeFilter = filter;

        var filterButtons = new[]
        {
            FilterAll, FilterJobs, FilterPrinter,
            FilterAlerts, FilterSuppressed
        };

        foreach (var btn in filterButtons)
            btn.Style = (Style)FindResource("FilterButton");

        activeButton.Style = (Style)FindResource("FilterButtonActive");
        ApplyFilter();
    }

    private void OnFilterAllClick(
        object sender, RoutedEventArgs e) =>
        SetActiveFilter("all", FilterAll);

    private void OnFilterJobsClick(
        object sender, RoutedEventArgs e) =>
        SetActiveFilter("jobs", FilterJobs);

    private void OnFilterPrinterClick(
        object sender, RoutedEventArgs e) =>
        SetActiveFilter("printer", FilterPrinter);

    private void OnFilterAlertsClick(
        object sender, RoutedEventArgs e) =>
        SetActiveFilter("alerts", FilterAlerts);

    private void OnFilterSuppressedClick(
        object sender, RoutedEventArgs e) =>
        SetActiveFilter("suppressed", FilterSuppressed);

    private void OnPrinterFilterChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (PrinterFilter.SelectedItem is ComboBoxItem item)
            _activePrinterId = (int)(item.Tag ?? 0);
        ApplyFilter();
    }

    private void OnClearHistoryClick(
        object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "Permanently delete all notification history?\n\n" +
            "The Ministry will not file a recovery form.",
            "MTGB — Clear history",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        _notificationManager.ClearHistory();
        LoadHistory();
    }

    private void OnCloseClick(
        object sender, RoutedEventArgs e) => Hide();

    // ── Dark title bar ────────────────────────────────────────────

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd, int attr,
        ref int attrValue, int attrSize);
}