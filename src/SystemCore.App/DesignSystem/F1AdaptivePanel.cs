using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace SystemCore.App.DesignSystem;

// Presentation-only responsive grid. Children keep native control semantics.
public sealed class F1AdaptivePanel : Panel
{
    public double MinimumItemWidth { get; set; } = 280;
    public int MaximumColumns { get; set; } = 3;
    private int columns = 1;
    private double[] heights = [];
    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : MinimumItemWidth;
        columns = Math.Clamp((int)((width + 16) / (MinimumItemWidth + 16)), 1, MaximumColumns);
        var count = (Children.Count + columns - 1) / columns;
        if (heights.Length != count) heights = new double[count]; else Array.Clear(heights);
        var itemWidth = Math.Max(0, (width - 16 * (columns - 1)) / columns);
        for (var i = 0; i < Children.Count; i++)
        {
            Children[i].Measure(new Size(itemWidth, double.PositiveInfinity));
            heights[i / columns] = Math.Max(heights[i / columns], Children[i].DesiredSize.Height);
        }
        return new Size(width, heights.Sum() + 16 * Math.Max(0, count - 1));
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        var itemWidth = Math.Max(0, (finalSize.Width - 16 * (columns - 1)) / columns);
        double y = 0;
        for (var i = 0; i < Children.Count; i++)
        {
            var row = i / columns;
            Children[i].Arrange(new Rect((i % columns) * (itemWidth + 16), y, itemWidth, heights[row]));
            if (i % columns == columns - 1) y += heights[row] + 16;
        }
        return finalSize;
    }
}
