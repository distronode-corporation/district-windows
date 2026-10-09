using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels;
using DistrictAI.ViewModels.Desk;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views.Desk;

/// <summary>
/// The help desk's queue: the tickets the workspace's customers raised, the
/// filter by status, the form that raises a ticket for a customer, and, while
/// the desk is switched off, the offer to turn it on. Closed to a viewer, so
/// it is never shown for one.
/// </summary>
public sealed partial class DeskPage : UserControl
{
    /// <summary>A page with nothing shown yet.</summary>
    public DeskPage()
    {
        InitializeComponent();
        Status.Attach(ViewModel.Load);
        Refresh.Attach(ViewModel.Load);
    }

    /// <summary>What the page shows, and its actions.</summary>
    public DeskViewModel ViewModel { get; } = new();

    internal void Attach(PageContext context) => ViewModel.Attach(context);

    internal void Show(DeskView view) => ViewModel.Show(view);

    private void OnTicketClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is DeskRowItem row)
        {
            ViewModel.OpenTicket(row);
        }
    }
}
