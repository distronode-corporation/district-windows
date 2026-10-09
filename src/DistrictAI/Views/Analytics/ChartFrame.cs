using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views.Analytics;

/// <summary>
/// A chart, as one element: Narrator reads it as an image named by its
/// sentence (<see cref="AutomationProperties.NameProperty"/>), and its bars,
/// scale and legend inside are drawing, not elements of their own. It takes
/// keyboard focus, so the sentence can be reached without a mouse.
/// </summary>
public sealed partial class ChartFrame : ContentControl
{
    /// <summary>A frame with nothing in it yet.</summary>
    public ChartFrame()
    {
        IsTabStop = true;
        UseSystemFocusVisuals = true;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
    }

    /// <inheritdoc/>
    protected override AutomationPeer OnCreateAutomationPeer() => new ChartPeer(this);

    private sealed partial class ChartPeer(ChartFrame owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Image;

        protected override string GetClassNameCore() => nameof(ChartFrame);

        protected override string GetLocalizedControlTypeCore() => "chart";

        // The bars are drawing: nothing under the chart is read on its own.
        protected override IList<AutomationPeer> GetChildrenCore() => [];
    }
}

/// <summary>
/// The page's side of a bar's size: the view model's parts of the track
/// (<see cref="DistrictAI.ViewModels.Analytics.BarLength"/>) as star-sized
/// grid lengths, for x:Bind.
/// </summary>
public static class ChartLength
{
    /// <summary><paramref name="parts"/> parts of the track.</summary>
    public static GridLength Star(double parts) => new(parts, GridUnitType.Star);
}
