using Microsoft.Extensions.Logging;
using MTGB.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MTGB.UI;

public partial class HistoryWindow : Window
{
    private readonly INotificationManager _notificationManager;
    private readonly ILogger<HistoryWindow> _logger;

    private string _activeFilter = "all";
    private int _activePrinterId = 0;
    private IReadOnlyList<NotificationHistoryEntry> _allEntries
        = new List<NotificationHistoryEntry>();

    public HistoryWindow(
        INotificationManager notificationManager,
        ILogger<HistoryWindow> logger)
    {
        _notificationManager = notificationManager;
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

        // Defer history load until after render
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

        // Printer filter
        if (_activePrinterId > 0)
            filtered = filtered.Where(
                e => e.PrinterId == _activePrinterId);

        // Event type filter
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
            "suppressed" => filtered.Where(
                e => e.WasSuppressed),
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

        // Group by date
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

        var border = new Border
        {
            Padding = new Thickness(4, 12, 4, 4)
        };

        border.Child = new TextBlock
        {
            Text = label.ToUpperInvariant(),
            FontFamily = new FontFamily("Courier New"),
            FontSize = 9,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(
                Color.FromRgb(0x8B, 0x65, 0x08))
        };

        return border;
    }

    private Border BuildHistoryRow(
        NotificationHistoryEntry entry)
    {
        var (accent, icon) = GetEventAccent(entry);

        var row = new Border
        {
            Background = new SolidColorBrush(
                entry.WasSuppressed
                    ? Color.FromRgb(0x14, 0x14, 0x17)
                    : Color.FromRgb(0x1A, 0x1A, 0x1F)),
            BorderBrush = new SolidColorBrush(
                Color.FromArgb(
                    entry.WasSuppressed ? (byte)0x10 : (byte)0x28,
                    accent.R, accent.G, accent.B)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(8, 9, 8, 9),
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
            Margin = new Thickness(0, 0, 10, 0),
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
            FontFamily = new FontFamily(
                "Segoe UI Variable, Segoe UI"),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(
                entry.WasSuppressed
                    ? Color.FromRgb(0x8A, 0x80, 0x78)
                    : Color.FromRgb(0xF0, 0xED, 0xE8)),
            Margin = new Thickness(0, 0, 8, 0)
        };

        var printerName = new TextBlock
        {
            Text = entry.PrinterName,
            FontFamily = new FontFamily("Courier New"),
            FontSize = 10,
            Foreground = new SolidColorBrush(accent),
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = entry.WasSuppressed ? 0.6 : 1.0
        };

        titleRow.Children.Add(eventName);
        titleRow.Children.Add(printerName);

        content.Children.Add(titleRow);

        // Job filename if present
        if (!string.IsNullOrWhiteSpace(entry.JobFilename))
        {
            var filename = new TextBlock
            {
                Text = entry.JobFilename
                    .Replace(".gcode", "")
                    .Replace(".GCODE", ""),
                FontFamily = new FontFamily("Courier New"),
                FontSize = 10,
                Foreground = new SolidColorBrush(
                    Color.FromRgb(0xC8, 0xC0, 0xB8)),
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 0, 0, 2)
            };
            content.Children.Add(filename);
        }

        // Suppressed reason
        if (entry.WasSuppressed &&
            !string.IsNullOrWhiteSpace(entry.SuppressReason))
        {
            var reason = new TextBlock
            {
                Text = $"Suppressed — {entry.SuppressReason}",
                FontFamily = new FontFamily("Courier New"),
                FontSize = 9,
                FontStyle = FontStyles.Italic,
                Foreground = new SolidColorBrush(
                    Color.FromRgb(0x6A, 0x60, 0x58))
            };
            content.Children.Add(reason);
        }

        Grid.SetColumn(content, 1);

        // Timestamp
        var timestamp = new TextBlock
        {
            Text = entry.Timestamp.LocalDateTime
                .ToString("HH:mm:ss"),
            FontFamily = new FontFamily("Courier New"),
            FontSize = 9,
            Foreground = new SolidColorBrush(
                Color.FromRgb(0x8A, 0x80, 0x78)),
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(8, 2, 0, 0)
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
            "job.finished" =>
                (Color.FromRgb(0x3B, 0xB2, 0x73), "✓"),
            "job.failed" =>
                (Color.FromRgb(0xE8, 0x48, 0x55), "✕"),
            "job.started" =>
                (Color.FromRgb(0x37, 0x8A, 0xDD), "▶"),
            "job.paused" =>
                (Color.FromRgb(0xF1, 0x8F, 0x01), "⏸"),
            "job.cancelled" =>
                (Color.FromRgb(0xE8, 0x48, 0x55), "✕"),
            "job.resumed" =>
                (Color.FromRgb(0x37, 0x8A, 0xDD), "▶"),
            "printer.offline" =>
                (Color.FromRgb(0xE8, 0x48, 0x55), "⚠"),
            "printer.online" =>
                (Color.FromRgb(0x3B, 0xB2, 0x73), "✓"),
            "filament.low" =>
                (Color.FromRgb(0xF1, 0x8F, 0x01), "⚠"),
            "progress.25" or
            "progress.50" or
            "progress.75" =>
                (Color.FromRgb(0x37, 0x8A, 0xDD), "◉"),
            "temp.nozzle.low" or
            "temp.bed.low" =>
                (Color.FromRgb(0xF1, 0x8F, 0x01), "⚠"),
            _ =>
                (Color.FromRgb(0x8B, 0x65, 0x08), "·")
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
            FontFamily = new FontFamily(
                "Segoe UI Variable, Segoe UI"),
            FontSize = 13,
            Foreground = new SolidColorBrush(
                Color.FromRgb(0xC8, 0xC0, 0xB8)),
            HorizontalAlignment = HorizontalAlignment.Center
        });

        panel.Children.Add(new TextBlock
        {
            Text = "The Ministry is at peace. " +
                   "This is suspicious.",
            FontFamily = new FontFamily("Courier New"),
            FontSize = 10,
            FontStyle = FontStyles.Italic,
            Foreground = new SolidColorBrush(
                Color.FromRgb(0x8A, 0x80, 0x78)),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 6, 0, 0)
        });

        return panel;
    }

    // ── Filter handlers ───────────────────────────────────────────

    private void SetActiveFilter(
        string filter, Button activeButton)
    {
        _activeFilter = filter;

        var filterButtons = new[]
        {
            FilterAll, FilterJobs, FilterPrinter,
            FilterAlerts, FilterSuppressed
        };

        foreach (var btn in filterButtons)
            btn.Style = (Style)FindResource("FilterButton");

        activeButton.Style =
            (Style)FindResource("FilterButtonActive");

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