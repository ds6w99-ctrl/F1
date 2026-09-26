using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace SystemCore.App.DesignSystem;

public sealed class F1WrapPanel : Panel
{
    protected override Size MeasureOverride(Size availableSize)
    {
        double x = 0, y = 0, rowHeight = 0;
        foreach (var child in Children)
        {
            child.Measure(availableSize); var size = child.DesiredSize;
            if (x > 0 && x + size.Width > availableSize.Width) { y += rowHeight + 8; x = rowHeight = 0; }
            x += size.Width + 8; rowHeight = Math.Max(rowHeight, size.Height);
        }
        return new Size(double.IsFinite(availableSize.Width) ? availableSize.Width : Math.Max(0, x - 8), y + rowHeight);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        double x = 0, y = 0, rowHeight = 0;
        foreach (var child in Children)
        {
            var size = child.DesiredSize;
            if (x > 0 && x + size.Width > finalSize.Width) { y += rowHeight + 8; x = rowHeight = 0; }
            child.Arrange(new Rect(x, y, size.Width, size.Height));
            x += size.Width + 8; rowHeight = Math.Max(rowHeight, size.Height);
        }
        return finalSize;
    }
}
