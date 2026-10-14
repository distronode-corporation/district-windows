using DistrictAI.ViewModels.Scheduling;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views.Scheduling;

/// <summary>
/// The overview: the booking desk's five rows, each standing or falling alone,
/// and what is coming up. The booking links are text to select, not links.
/// </summary>
public sealed partial class SchedulingOverviewPanel : UserControl
{
    /// <summary>A panel showing nothing until <see cref="Attach"/>.</summary>
    public SchedulingOverviewPanel()
    {
        InitializeComponent();
    }

    /// <summary>What the panel shows.</summary>
    public SchedulingOverviewViewModel ViewModel { get; private set; } = new();

    /// <summary>Shows <paramref name="model"/>, the page's own, from now on.</summary>
    internal void Attach(SchedulingOverviewViewModel model)
    {
        ViewModel = model;
        Bindings.Update();
    }
}
