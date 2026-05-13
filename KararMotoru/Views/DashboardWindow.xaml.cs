using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using FokusKararMotoru.Services;

namespace FokusKararMotoru;

public partial class DashboardWindow : Window
{
    private readonly FokusDb _database;
    private readonly Func<int> _focusThresholdProvider;
    private readonly DispatcherTimer _refreshTimer;
    private DashboardSnapshot? _snapshot;
    private bool _refreshInProgress;
    private bool _closed;

    public DashboardWindow(FokusDb database, Func<int> focusThresholdProvider)
    {
        InitializeComponent();

        _database = database;
        _focusThresholdProvider = focusThresholdProvider;
        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _refreshTimer.Tick += (_, _) => _ = RefreshDashboardAsync();
        Loaded += (_, _) =>
        {
            _ = RefreshDashboardAsync();
            _refreshTimer.Start();
        };
        Closed += (_, _) =>
        {
            _closed = true;
            _refreshTimer.Stop();
        };
        TrendCanvas.SizeChanged += (_, _) => DrawTrend();
        DailyCanvas.SizeChanged += (_, _) => DrawDailyBars();
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        _ = RefreshDashboardAsync(force: true);
    }

    private async Task RefreshDashboardAsync(bool force = false)
    {
        if (_refreshInProgress && !force)
        {
            return;
        }

        _refreshInProgress = true;
        try
        {
            int threshold = _focusThresholdProvider();
            DashboardSnapshot snapshot = await Task.Run(() => _database.GetDashboardSnapshot(20, threshold));
            if (_closed)
            {
                return;
            }

            _snapshot = snapshot;
            ApplySnapshot(_snapshot);
            DashboardStatusText.Text = $"Son güncelleme: {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.IO.IOException or Microsoft.Data.Sqlite.SqliteException)
        {
            DashboardStatusText.Text = "İstatistikler okunamadı: " + ex.Message;
        }
        finally
        {
            _refreshInProgress = false;
        }
    }

    private void ApplySnapshot(DashboardSnapshot snapshot)
    {
        DashboardOverview overview = snapshot.Overview;
        SessionCountText.Text = overview.SessionCount.ToString(CultureInfo.CurrentCulture);
        TotalDurationText.Text = FormatDuration(overview.TotalDuration);
        AverageFocusText.Text = overview.SampleCount == 0
            ? "-"
            : overview.AverageFocus.ToString("0.0", CultureInfo.CurrentCulture);
        FocusRangeText.Text = overview.SampleCount == 0
            ? "Min - / Maks -"
            : $"Min {overview.MinimumFocus} / Maks {overview.MaximumFocus}";
        LowFocusRateText.Text = overview.LowFocusRate.ToString("P0", CultureInfo.CurrentCulture);
        RiskSamplesText.Text = $"{overview.LowFocusSamples} düşük, {overview.InterventionSamples} müdahale";
        LastSampleText.Text = overview.LastSampleTime is null
            ? "-"
            : overview.LastSampleTime.Value.ToString("dd.MM HH:mm:ss", CultureInfo.CurrentCulture);
        InputAverageText.Text = $"Tuş {overview.AverageKeysPerMinute:0.0}/dk, fare {overview.AverageMousePixelsPerMinute:0} px/dk";

        PenaltyList.ItemsSource = snapshot.Penalties.Count == 0
            ? new[] { "Ceza verisi yok" }
            : snapshot.Penalties.Select(FormatPenalty).ToArray();
        BlacklistList.ItemsSource = snapshot.Blacklist.Count == 0
            ? new[] { "Kara liste yakalaması yok" }
            : snapshot.Blacklist.Select(item => $"{item.ProcessName}  {item.Hits} örnek").ToArray();
        DailySummaryList.ItemsSource = snapshot.DailySummaries.Count == 0
            ? new[] { "Günlük özet için veri yok" }
            : snapshot.DailySummaries.Select(FormatDailySummary).ToArray();

        DrawTrend();
        DrawDailyBars();
    }

    private void DrawTrend()
    {
        TrendCanvas.Children.Clear();
        if (_snapshot is null)
        {
            return;
        }

        IReadOnlyList<FocusTrendPoint> points = _snapshot.FocusTrend;
        if (points.Count < 2 || TrendCanvas.ActualWidth <= 0 || TrendCanvas.ActualHeight <= 0)
        {
            AddCanvasMessage(TrendCanvas, "Trend için yeterli veri yok");
            return;
        }

        double width = TrendCanvas.ActualWidth;
        double height = TrendCanvas.ActualHeight;
        DrawHorizontalGuide(TrendCanvas, 50, "#D7DEE8", 0.8);
        DrawHorizontalGuide(TrendCanvas, _focusThresholdProvider(), "#B45309", 0.9);

        var focusLine = new Polyline
        {
            Stroke = (Brush)FindResource("AccentBrush"),
            StrokeThickness = 2.4
        };
        var rawLine = new Polyline
        {
            Stroke = (Brush)FindResource("MutedTextBrush"),
            StrokeThickness = 1.3,
            Opacity = 0.55
        };

        double step = width / (points.Count - 1);
        for (int i = 0; i < points.Count; i++)
        {
            double x = i * step;
            focusLine.Points.Add(new Point(x, height - points[i].FocusScore / 100.0 * height));
            rawLine.Points.Add(new Point(x, height - points[i].RawScore / 100.0 * height));
        }

        TrendCanvas.Children.Add(rawLine);
        TrendCanvas.Children.Add(focusLine);
    }

    private void DrawDailyBars()
    {
        DailyCanvas.Children.Clear();
        if (_snapshot is null)
        {
            return;
        }

        IReadOnlyList<DailyFocusSummary> days = _snapshot.DailySummaries;
        if (days.Count == 0 || DailyCanvas.ActualWidth <= 0 || DailyCanvas.ActualHeight <= 0)
        {
            AddCanvasMessage(DailyCanvas, "Günlük veri yok");
            return;
        }

        double width = DailyCanvas.ActualWidth;
        double height = DailyCanvas.ActualHeight;
        double gap = 12;
        double barWidth = Math.Max(22, (width - gap * (days.Count + 1)) / days.Count);
        DrawHorizontalGuide(DailyCanvas, _focusThresholdProvider(), "#B45309", 0.9);

        for (int i = 0; i < days.Count; i++)
        {
            DailyFocusSummary day = days[i];
            double barHeight = Math.Max(3, day.AverageFocus / 100.0 * (height - 28));
            double x = gap + i * (barWidth + gap);
            double y = height - 24 - barHeight;

            var bar = new Rectangle
            {
                Width = barWidth,
                Height = barHeight,
                RadiusX = 3,
                RadiusY = 3,
                Fill = day.AverageFocus < _focusThresholdProvider()
                    ? (Brush)FindResource("DangerBrush")
                    : (Brush)FindResource("AccentBrush"),
                Opacity = 0.88
            };
            Canvas.SetLeft(bar, x);
            Canvas.SetTop(bar, y);
            DailyCanvas.Children.Add(bar);

            var label = new TextBlock
            {
                Text = day.Day.ToString("dd.MM", CultureInfo.CurrentCulture),
                FontSize = 11,
                Foreground = (Brush)FindResource("MutedTextBrush")
            };
            Canvas.SetLeft(label, x);
            Canvas.SetTop(label, height - 20);
            DailyCanvas.Children.Add(label);
        }
    }

    private void DrawHorizontalGuide(Canvas canvas, int value, string color, double opacity)
    {
        if (canvas.ActualWidth <= 0 || canvas.ActualHeight <= 0)
        {
            return;
        }

        double y = canvas.ActualHeight - Math.Clamp(value, 0, 100) / 100.0 * canvas.ActualHeight;
        canvas.Children.Add(new Line
        {
            X1 = 0,
            X2 = canvas.ActualWidth,
            Y1 = y,
            Y2 = y,
            Stroke = (Brush)new BrushConverter().ConvertFromString(color)!,
            StrokeThickness = 1,
            Opacity = opacity
        });
    }

    private void AddCanvasMessage(Canvas canvas, string message)
    {
        var text = new TextBlock
        {
            Text = message,
            Foreground = (Brush)FindResource("MutedTextBrush")
        };
        Canvas.SetLeft(text, 12);
        Canvas.SetTop(text, 12);
        canvas.Children.Add(text);
    }

    private static string FormatPenalty(PenaltySummary item) =>
        $"{item.Source}  toplam -{item.TotalPenalty:0.#} | {item.Hits} kez | ort -{item.AveragePenalty:0.#}";

    private static string FormatDailySummary(DailyFocusSummary item) =>
        $"{item.Day:dd.MM.yyyy}  Ort {item.AverageFocus:0.0}  Min {item.MinimumFocus}  Düşük {item.LowFocusRate:P0}  Kara liste {item.BlacklistSamples}";

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
        {
            return $"{(int)duration.TotalHours:0}:{duration.Minutes:00}:{duration.Seconds:00}";
        }

        return duration.ToString(@"mm\:ss", CultureInfo.CurrentCulture);
    }
}
