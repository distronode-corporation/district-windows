using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views;

/// <summary>The open workspace's summary: its numbers, its recent calls, and finishing setup.</summary>
public sealed partial class OverviewPage : UserControl
{
    /// <summary>A page with nothing shown yet.</summary>
    public OverviewPage()
    {
        InitializeComponent();
        Status.Attach(ViewModel.Load);
        Refresh.Attach(ViewModel.Load);
    }

    /// <summary>What the page shows, and its buttons.</summary>
    public OverviewViewModel ViewModel { get; } = new();

    internal void Attach(PageContext context) => ViewModel.Attach(context);

    internal void Show(OverviewView view) => ViewModel.Show(view);

    private void OnCallClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is CallRowItem row)
        {
            ViewModel.OpenCall(row);
        }
    }
}
