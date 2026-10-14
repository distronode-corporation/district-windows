using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels;
using DistrictAI.ViewModels.Scheduling;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views.Scheduling;

/// <summary>
/// The booking pages: where they stand, turning them on, the sections of their
/// administration built natively (each a row that opens its page), and
/// managing the rest on the web, signed in, through the core's hand-off.
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

    private void OnSectionClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is SchedulingSectionEntry section)
        {
            ViewModel.OpenSection(section);
        }
    }
}
