using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels;
using DistrictAI.Views.Contacts;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views;

/// <summary>
/// The contacts, a page at a time, "Add contact" (its form a dialog open while
/// the core holds it open) and the way to the blocked callers.
/// </summary>
public sealed partial class ContactsPage : UserControl
{
    private readonly ContactFormDialog.Host _form = new();
    private PageContext? _context;

    /// <summary>A page with nothing shown yet.</summary>
    public ContactsPage()
    {
        InitializeComponent();
        Status.Attach(ViewModel.Load);
        Refresh.Attach(ViewModel.Load);
        Loaded += (_, _) => SyncForm();
        // A dialog never outlives its page; the core still holds the form, so
        // it opens again when the page is back.
        Unloaded += (_, _) => _form.Close();
    }

    /// <summary>What the page shows, and its buttons.</summary>
    public ContactsViewModel ViewModel { get; } = new();

    internal void Attach(PageContext context)
    {
        _context = context;
        ViewModel.Attach(context);
    }

    internal void Show(ContactsView view)
    {
        ViewModel.Show(view);
        SyncForm();
    }

    private void SyncForm() => _form.Sync(this, _context, ViewModel.Create);

    private void OnContactClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ContactRowItem row)
        {
            ViewModel.OpenContact(row);
        }
    }
}
