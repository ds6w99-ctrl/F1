using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SystemCore.Core.Performance;
using Windows.Foundation;

namespace SystemCore.App.Performance;

public sealed partial class HistoryChart : UserControl
{
    public static readonly DependencyProperty PointsProperty = DependencyProperty.Register(nameof(Points), typeof(IReadOnlyList<HistoryPoint>), typeof(HistoryChart), new PropertyMetadata(null, Changed));
    public IReadOnlyList<HistoryPoint>? Points { get => (IReadOnlyList<HistoryPoint>?)GetValue(PointsProperty); set => SetValue(PointsProperty, value); }
    public int Metric { get; set; }
    public static readonly DependencyProperty CultureProperty = DependencyProperty.Register(nameof(Culture), typeof(System.Globalization.CultureInfo), typeof(HistoryChart), new PropertyMetadata(System.Globalization.CultureInfo.InvariantCulture, Changed));
    public System.Globalization.CultureInfo Culture { get => (System.Globalization.CultureInfo)GetValue(CultureProperty); set => SetValue(CultureProperty, value); }
    private double scale = 1;
    public HistoryChart() { InitializeComponent(); }
    private static void Changed(DependencyObject sender, DependencyPropertyChangedEventArgs e) => ((HistoryChart)sender).Draw();
    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => Draw();
    private double? Value(HistoryPoint p) => Metric switch { 0 => p.Cpu, 1 => p.Memory, 2 => p.Disk, 4 => p.AvailableBytes, _ => p.Network };
    private void Draw()
    {
        if (Plot is null || Points is not { Count: > 0 } data || Plot.ActualWidth <= 0) { if (Line is not null) Line.Data = null; return; }
        var maximum = data.Max(p => Value(p) ?? 0);
        scale = Metric < 2 ? 100 : Math.Max(1, Math.Max(maximum, scale * 0.98));
        ScaleLabel.Text = Metric < 2 ? "0–100%" : "0–" + (Metric == 4 ? SystemCore.Core.SystemInformation.InfoFormatting.Size((ulong)scale, Culture) : PerformanceMath.RateText(scale, Culture));
        var geometry = new PathGeometry();
        PathFigure? figure = null;
        var end = data[^1].Timestamp;
        DateTimeOffset? previous = null;
        foreach (var sample in data)
        {
            var value = Value(sample);
            if (!value.HasValue || end - sample.Timestamp >= TimeSpan.FromSeconds(60)) { figure = null; continue; }
            if (previous.HasValue && sample.Timestamp - previous.Value > TimeSpan.FromSeconds(2.5)) figure = null;
            previous = sample.Timestamp;
            var point = new Point(Math.Clamp(1 - (end - sample.Timestamp).TotalSeconds / 60, 0, 1) * Plot.ActualWidth,
                (1 - Math.Clamp(value.Value / scale, 0, 1)) * Plot.ActualHeight);
            if (figure is null) { figure = new PathFigure { StartPoint = point, IsClosed = false, IsFilled = false }; geometry.Figures.Add(figure); }
            else figure.Segments.Add(new LineSegment { Point = point });
        }
        Line.Data = geometry;
    }
}
