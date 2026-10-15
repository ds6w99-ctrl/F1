using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SystemCore.App.ViewModels;
using SystemCore.Core.Performance;
using Windows.Foundation;

namespace SystemCore.App.DesignSystem;

public sealed partial class F1MetricCard : UserControl
{
    private IReadOnlyList<HistoryPoint> points = [];
    private int metric;
    private readonly LineSegment[] arcSegments = new LineSegment[64];
    private readonly LineSegment[] glowSegments = new LineSegment[64];
    public F1MetricCard()
    {
        InitializeComponent(); ActualThemeChanged += (_, _) => ApplyAccent();
        // Bounded reusable geometry; each Path owns its native geometry instance.
        var figure = new PathFigure { StartPoint = new Point(98, 10), IsFilled = false };
        var glow = new PathFigure { StartPoint = new Point(98, 10), IsFilled = false };
        for (var i = 0; i < arcSegments.Length; i++)
        {
            arcSegments[i] = new LineSegment(); figure.Segments.Add(arcSegments[i]);
            glowSegments[i] = new LineSegment(); glow.Segments.Add(glowSegments[i]);
        }
        var geometry = new PathGeometry(); geometry.Figures.Add(figure); Arc.Data = geometry;
        var glowGeometry = new PathGeometry(); glowGeometry.Figures.Add(glow); Glow.Data = glowGeometry;
    }
    private string accent = "F1BlueBrush";
    private void ApplyAccent()
    {
        var brush = (Brush)Application.Current.Resources[accent];
        Arc.Stroke = Glow.Stroke = Line.Stroke = Icon.Foreground = brush;
    }
    public void Present(MetricCardViewModel card, string gauge, string metadata, string glyph, string color,
        IReadOnlyList<HistoryPoint> history, int metric, string historyDescription, bool rtl)
    {
        accent = color; ApplyAccent(); this.points = history; this.metric = metric;
        Icon.Glyph = glyph; Title.Text = card.Title; Value.Text = gauge; Detail.Text = card.Id == "MetricCpu" ? card.Title : card.Value; Metadata.Text = metadata;
        Title.FlowDirection = Metadata.FlowDirection = rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        AutomationProperties.SetAutomationId(card.Id == "MetricCpu" ? Value : Detail, card.Id);
        AutomationProperties.SetName(this, card.Title + ": " + gauge + "; " + card.Value + "; " + historyDescription);
        var known = card.ProgressVisibility == Visibility.Visible;
        Value.FontSize = known ? 34 : 18;
        var percent = known ? Math.Clamp(card.Progress, 0, 100) : 0;
        Arc.Visibility = Glow.Visibility = percent > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (percent > 0)
        {
            for (var i = 0; i < arcSegments.Length; i++)
            {
                var angle = percent * Math.PI * 2 / 100 * (i + 1) / arcSegments.Length;
                arcSegments[i].Point = new Point(98 + 88 * Math.Sin(angle), 98 - 88 * Math.Cos(angle));
                glowSegments[i].Point = arcSegments[i].Point;
            }
        }
        DrawHistory();
    }
    private void OnPlotSizeChanged(object sender, SizeChangedEventArgs args) => DrawHistory();
    private void DrawHistory()
    {
        var geometry = new PathGeometry(); PathFigure? segment = null; DateTimeOffset? before = null;
        if (points.Count > 0 && Plot.ActualWidth > 0)
        {
            var end = points[^1].Timestamp;
            foreach (var sample in points)
            {
                var value = metric switch { 0 => sample.Cpu, 1 => sample.Memory, _ => sample.Disk };
                if (value is null || before is { } previous && (sample.Timestamp - previous).TotalSeconds > 2.5) segment = null;
                before = sample.Timestamp;
                if (value is not { } number) continue;
                var point = new Point(Math.Clamp(1 - (end - sample.Timestamp).TotalSeconds / 60, 0, 1) * Plot.ActualWidth, 42 - Math.Clamp(number, 0, 100) * .4);
                if (segment is null) { segment = new PathFigure { StartPoint = point, IsFilled = false }; geometry.Figures.Add(segment); }
                else segment.Segments.Add(new LineSegment { Point = point });
            }
        }
        Line.Data = geometry;
    }
}
