using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace DistrictAI.Views;

/// <summary>
/// Its children left to right, a child that does not fit starting the next
/// line: buttons that sit side by side at 100 percent text size and stack at
/// 200 percent, in either language, instead of running off the edge. A
/// collapsed child takes no room and no spacing.
/// </summary>
public sealed partial class WrapPanel : Panel
{
    /// <summary>The <see cref="HorizontalSpacing"/> property.</summary>
    public static readonly DependencyProperty HorizontalSpacingProperty = DependencyProperty.Register(
        nameof(HorizontalSpacing), typeof(double), typeof(WrapPanel), new PropertyMetadata(8d, OnSpacingChanged));

    /// <summary>The <see cref="VerticalSpacing"/> property.</summary>
    public static readonly DependencyProperty VerticalSpacingProperty = DependencyProperty.Register(
        nameof(VerticalSpacing), typeof(double), typeof(WrapPanel), new PropertyMetadata(8d, OnSpacingChanged));

    /// <summary>The room between two children on a line.</summary>
    public double HorizontalSpacing
    {
        get => (double)GetValue(HorizontalSpacingProperty);
        set => SetValue(HorizontalSpacingProperty, value);
    }

    /// <summary>The room between two lines.</summary>
    public double VerticalSpacing
    {
        get => (double)GetValue(VerticalSpacingProperty);
        set => SetValue(VerticalSpacingProperty, value);
    }

    /// <inheritdoc/>
    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children)
        {
            child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
        }
        return Place(availableSize.Width, arrange: false);
    }

    /// <inheritdoc/>
    protected override Size ArrangeOverride(Size finalSize)
    {
        _ = Place(finalSize.Width, arrange: true);
        return finalSize;
    }

    // One walk for both passes: where each visible child goes, line by line,
    // and the size the lines take. A line is arranged once it is complete, each
    // child given the line's height, so its VerticalAlignment (a button's
    // Center) lines it up with the tallest child on the line.
    private Size Place(double width, bool arrange)
    {
        var line = new List<UIElement>();
        double x = 0;
        double y = 0;
        double lineHeight = 0;
        double widest = 0;

        void EndLine()
        {
            if (arrange)
            {
                double at = 0;
                foreach (var child in line)
                {
                    child.Arrange(new Rect(at, y, child.DesiredSize.Width, lineHeight));
                    at += child.DesiredSize.Width + HorizontalSpacing;
                }
            }
            line.Clear();
        }

        foreach (var child in Children)
        {
            if (child.Visibility == Visibility.Collapsed)
            {
                continue;
            }
            var size = child.DesiredSize;
            if (line.Count > 0 && x + HorizontalSpacing + size.Width > width)
            {
                EndLine();
                y += lineHeight + VerticalSpacing;
                x = 0;
                lineHeight = 0;
            }
            if (line.Count > 0)
            {
                x += HorizontalSpacing;
            }
            line.Add(child);
            x += size.Width;
            widest = Math.Max(widest, x);
            lineHeight = Math.Max(lineHeight, size.Height);
        }
        EndLine();
        return new Size(widest, y + lineHeight);
    }

    private static void OnSpacingChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e) =>
        ((WrapPanel)sender).InvalidateMeasure();
}
