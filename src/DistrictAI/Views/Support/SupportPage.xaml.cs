using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels;
using DistrictAI.ViewModels.Support;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views.Support;

/// <summary>
/// The workspace's support requests to Distronode, open and resolved, and the
/// form that raises one. Closed to a viewer, so it is never shown for one.
/// </summary>
public sealed partial class SupportPage : UserControl
{
    /// <summary>A page with nothing shown yet.</summary>
    public SupportPage()
    {
        InitializeComponent();
        Status.Attach(ViewModel.Load);
        Refresh.Attach(ViewModel.Load);
    }

    /// <summary>What the page shows, and its actions.</summary>
    public SupportViewModel ViewModel { get; } = new();

    internal void Attach(PageContext context) => ViewModel.Attach(context);

    internal void Show(SupportView view) => ViewModel.Show(view);

    private void OnRequestClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is SupportRowItem row)
        {
            ViewModel.OpenRequest(row);
        }
    }
}
