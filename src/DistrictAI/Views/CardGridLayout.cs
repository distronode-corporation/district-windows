using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace DistrictAI.Views;

/// <summary>
/// Cards in equal columns, as many as fit at <see cref="MinItemWidth"/>, each
/// row as tall as its tallest card. UniformGridLayout sizes every item to the
/// first one, so at 200 percent text a two-line label under a one-line first
/// card lost its second line; here every card is measured.
/// </summary>
public sealed partial class CardGridLayout : NonVirtualizingLayout
{
    /// <summary>The narrowest a card is drawn: fewer columns before narrower cards.</summary>
    public double MinItemWidth { get; set; } = 160;

    /// <summary>The room between two cards on a row.</summary>
    public double ColumnSpacing { get; set; } = 8;

    /// <summary>The room between two rows.</summary>
    public double RowSpacing { get; set; } = 8;

    /// <inheritdoc/>
    protected override Size MeasureOverride(NonVirtualizingLayoutContext context, Size availableSize)
    {
        var children = context.Children;
        if (children.Count == 0)
        {
            return new Size(0, 0);
        }
        var columns = Columns(availableSize.Width, children.Count);
        var itemWidth = ItemWidth(availableSize.Width, columns);
        foreach (var child in children)
        {
            child.Measure(new Size(itemWidth, double.PositiveInfinity));
        }
        double height = 0;
        for (var start = 0; start < children.Count; start += columns)
        {
            height += RowHeight(children, start, columns) + (start > 0 ? RowSpacing : 0);
        }
        var width = double.IsInfinity(availableSize.Width)
            ? (columns * itemWidth) + ((columns - 1) * ColumnSpacing)
            : availableSize.Width;
        return new Size(width, height);
    }

    /// <inheritdoc/>
    protected override Size ArrangeOverride(NonVirtualizingLayoutContext context, Size finalSize)
    {
        var children = context.Children;
        if (children.Count == 0)
        {
            return finalSize;
        }
        var columns = Columns(finalSize.Width, children.Count);
        var itemWidth = ItemWidth(finalSize.Width, columns);
        double y = 0;
        for (var start = 0; start < children.Count; start += columns)
        {
            var rowHeight = RowHeight(children, start, columns);
            for (var index = start; index < Math.Min(start + columns, children.Count); index++)
            {
                var x = (index - start) * (itemWidth + ColumnSpacing);
                children[index].Arrange(new Rect(x, y, itemWidth, rowHeight));
            }
            y += rowHeight + RowSpacing;
        }
        return finalSize;
    }

    private int Columns(double width, int count) =>
        double.IsInfinity(width)
            ? count
            : Math.Clamp((int)Math.Floor((width + ColumnSpacing) / (MinItemWidth + ColumnSpacing)), 1, count);

    private double ItemWidth(double width, int columns) =>
        double.IsInfinity(width) ? MinItemWidth : Math.Max(0, (width - ((columns - 1) * ColumnSpacing)) / columns);

    private static double RowHeight(IReadOnlyList<UIElement> children, int start, int columns)
    {
        double height = 0;
        for (var index = start; index < Math.Min(start + columns, children.Count); index++)
        {
            height = Math.Max(height, children[index].DesiredSize.Height);
        }
        return height;
    }
}
