using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace DistrictAI.Views;

/// <summary>
/// A status line and its buttons: the first child (the line) and the second
/// (the buttons), side by side with the buttons at the right edge while the
/// line keeps at least <see cref="MinLineWidth"/> beside them, and the buttons
/// under the line, at the left, when it would not. A line squeezed beside
/// French buttons in a narrow window broke inside its words on Linux; here it
/// always has a line's room. A line with nothing to say (collapsed, or no
/// text) takes no room, and the buttons stay at the right.
/// </summary>
public sealed partial class StatusRow : Panel
{
    /// <summary>The <see cref="MinLineWidth"/> property.</summary>
    public static readonly DependencyProperty MinLineWidthProperty = DependencyProperty.Register(
        nameof(MinLineWidth), typeof(double), typeof(StatusRow), new PropertyMetadata(240d, OnLayoutChanged));

    /// <summary>The <see cref="Spacing"/> property.</summary>
    public static readonly DependencyProperty SpacingProperty = DependencyProperty.Register(
        nameof(Spacing), typeof(double), typeof(StatusRow), new PropertyMetadata(8d, OnLayoutChanged));

    /// <summary>The narrowest the line may be beside the buttons before they go under it.</summary>
    public double MinLineWidth
    {
        get => (double)GetValue(MinLineWidthProperty);
        set => SetValue(MinLineWidthProperty, value);
    }

    /// <summary>The room between the line and the buttons, either way.</summary>
    public double Spacing
    {
        get => (double)GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    // Whether the last measure put the buttons under the line.
    private bool _stacked;

    /// <inheritdoc/>
    protected override Size MeasureOverride(Size availableSize)
    {
        var (line, buttons) = Parts();
        buttons?.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var buttonSize = buttons?.DesiredSize ?? default;
        var width = availableSize.Width;
        if (line is null)
        {
            _stacked = false;
            return buttonSize;
        }
        var beside = buttonSize.Width > 0 ? width - buttonSize.Width - Spacing : width;
        // A line with no text takes no room: measured beside, it stays out of the way.
        line.Measure(new Size(Math.Max(0, beside), double.PositiveInfinity));
        _stacked = buttonSize.Width > 0 && line.DesiredSize.Width > 0 && beside < MinLineWidth;
        if (_stacked)
        {
            line.Measure(new Size(width, double.PositiveInfinity));
            var lineSize = line.DesiredSize;
            return new Size(
                Math.Max(lineSize.Width, buttonSize.Width),
                lineSize.Height + Spacing + buttonSize.Height);
        }
        var lineDesired = line.DesiredSize;
        var gap = buttonSize.Width > 0 && lineDesired.Width > 0 ? Spacing : 0;
        return new Size(
            double.IsInfinity(width) ? lineDesired.Width + gap + buttonSize.Width : width,
            Math.Max(lineDesired.Height, buttonSize.Height));
    }

    /// <inheritdoc/>
    protected override Size ArrangeOverride(Size finalSize)
    {
        var (line, buttons) = Parts();
        var buttonSize = buttons?.DesiredSize ?? default;
        if (_stacked && line is not null)
        {
            var lineHeight = line.DesiredSize.Height;
            line.Arrange(new Rect(0, 0, finalSize.Width, lineHeight));
            buttons?.Arrange(new Rect(0, lineHeight + Spacing, buttonSize.Width, buttonSize.Height));
            return finalSize;
        }
        var height = finalSize.Height;
        var left = Math.Max(0, finalSize.Width - buttonSize.Width);
        buttons?.Arrange(new Rect(left, (height - buttonSize.Height) / 2, buttonSize.Width, buttonSize.Height));
        var lineWidth = buttonSize.Width > 0 ? Math.Max(0, left - Spacing) : finalSize.Width;
        line?.Arrange(new Rect(0, 0, lineWidth, height));
        return finalSize;
    }

    // The line and the buttons, each null when it is missing or collapsed.
    private (UIElement? Line, UIElement? Buttons) Parts()
    {
        UIElement? Shown(int index) =>
            index < Children.Count && Children[index].Visibility == Visibility.Visible ? Children[index] : null;
        return (Shown(0), Shown(1));
    }

    private static void OnLayoutChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e) =>
        ((StatusRow)sender).InvalidateMeasure();
}
