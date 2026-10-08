using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views;

/// <summary>The call log, newest first, a page at a time.</summary>
public sealed partial class CallsPage : UserControl
{
    /// <summary>A page with nothing shown yet.</summary>
    public CallsPage()
    {
        InitializeComponent();
        Status.Attach(ViewModel.Load);
        Refresh.Attach(ViewModel.Load);
    }

    /// <summary>What the page shows, and its buttons.</summary>
    public CallsViewModel ViewModel { get; } = new();

    internal void Attach(PageContext context) => ViewModel.Attach(context);

    internal void Show(CallsView view) => ViewModel.Show(view);

    private void OnCallClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is CallRowItem row)
        {
            ViewModel.OpenCall(row);
        }
    }
}
