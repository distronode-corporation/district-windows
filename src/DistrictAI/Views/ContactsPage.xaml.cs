using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views;

/// <summary>The contacts, a page at a time. Adding, editing and blocking stay on the web and the phone apps.</summary>
public sealed partial class ContactsPage : UserControl
{
    /// <summary>A page with nothing shown yet.</summary>
    public ContactsPage()
    {
        InitializeComponent();
        Status.Attach(ViewModel.Load);
        Refresh.Attach(ViewModel.Load);
    }

    /// <summary>What the page shows, and its buttons.</summary>
    public ContactsViewModel ViewModel { get; } = new();

    internal void Attach(PageContext context) => ViewModel.Attach(context);

    internal void Show(ContactsView view) => ViewModel.Show(view);

    private void OnContactClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ContactRowItem row)
        {
            ViewModel.OpenContact(row);
        }
    }
}
