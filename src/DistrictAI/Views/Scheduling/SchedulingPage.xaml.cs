using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels;
using DistrictAI.ViewModels.Scheduling;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views.Scheduling;

/// <summary>
/// The booking pages: where they stand, turning them on, and managing them on
/// the web, signed in, through the core's hand-off.
/// </summary>
public sealed partial class SchedulingPage : UserControl
{
    /// <summary>A page with nothing shown yet.</summary>
    public SchedulingPage()
    {
        InitializeComponent();
        Status.Attach(ViewModel.Load);
        Refresh.Attach(ViewModel.Load);
    }

    /// <summary>What the page shows, and its actions.</summary>
    public SchedulingViewModel ViewModel { get; } = new();

    internal void Attach(PageContext context) => ViewModel.Attach(context);

    internal void Show(SchedulingView view) => ViewModel.Show(view);
}
