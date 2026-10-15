using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace DistrictAI.Views;

/// <summary>
/// The page frame (App.xaml, ReadingWidth): its child as wide as the page
/// allows up to the child's own MaxWidth, anchored to the left edge. A
/// stretched element with a MaxWidth narrower than its slot is centred, which
/// put some pages' headings in the middle of the window and others at the
/// left; and a left-aligned one shrinks to its content, so cards changed width
/// from page to page. Here the column is always the same width, at the left.
/// </summary>
public sealed partial class PageColumn : Panel
{
    /// <inheritdoc/>
    protected override Size MeasureOverride(Size availableSize)
    {
        double width = 0;
        double height = 0;
        foreach (var child in Children)
        {
            var column = ColumnWidth(child, availableSize.Width);
            child.Measure(new Size(column, availableSize.Height));
            width = Math.Max(width, double.IsInfinity(column) ? child.DesiredSize.Width : column);
            height = Math.Max(height, child.DesiredSize.Height);
        }
        return new Size(width, height);
    }

    /// <inheritdoc/>
    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var child in Children)
        {
            var column = ColumnWidth(child, finalSize.Width);
            child.Arrange(new Rect(0, 0, double.IsInfinity(column) ? child.DesiredSize.Width : column, finalSize.Height));
        }
        return finalSize;
    }

    // The width the child is given: the room there is, held to its MaxWidth.
    private static double ColumnWidth(UIElement child, double room) =>
        child is FrameworkElement { MaxWidth: var most } && most < room ? most : room;
}
