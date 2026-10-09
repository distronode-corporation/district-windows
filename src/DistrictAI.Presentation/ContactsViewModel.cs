using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels.Contacts;

namespace DistrictAI.ViewModels;

/// <summary>The contacts, a page at a time, adding one, and the way to the blocked callers.</summary>
public sealed partial class ContactsViewModel : ObservableObject
{
    private PageContext? _context;

    /// <summary>Loading, failure, the empty list and refresh.</summary>
    public LoadStateViewModel Load { get; } = new();

    /// <summary>"Load more" at the end of the list.</summary>
    public PagingViewModel Paging { get; } = new(() => new UiEvent.LoadMoreContacts());

    /// <summary>The contacts.</summary>
    public ObservableCollection<ContactRowItem> Rows { get; } = [];

    /// <summary>How many contacts there are, or empty.</summary>
    [ObservableProperty]
    public partial string TotalLabel { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="TotalLabel"/>.</summary>
    [ObservableProperty]
    public partial bool HasTotalLabel { get; set; }

    /// <summary>Whether "Add contact" shows: the member's role may change contacts.</summary>
    [ObservableProperty]
    public partial bool CanCreate { get; set; }

    /// <summary>The form adding a contact.</summary>
    public ContactFormViewModel Create { get; } = new(ContactFormKind.Create);

    internal void Attach(PageContext context)
    {
        _context = context;
        Load.Attach(context);
        Paging.Attach(context);
        Create.Attach(context);
    }

    internal void Show(ContactsView view)
    {
        Load.Show(view.Status, view.Rows.Length > 0, view.Empty, view.Paging.Refreshing, view.Paging.RefreshFailure);
        Paging.Show(view.Paging);
        TotalLabel = view.TotalLabel ?? string.Empty;
        HasTotalLabel = view.TotalLabel is not null;
        Display.Sync(Rows, [.. view.Rows.Select(ContactRowItem.From)]);
        CanCreate = view.CanCreate;
        Create.Show(view.Create);
    }

    internal void OpenContact(ContactRowItem row) => _context?.Send(new UiEvent.OpenContact(row.ContactId));

    /// <summary>"Add contact": the core opens the form, unless one is open already.</summary>
    [RelayCommand]
    private void AddContact()
    {
        if (CanCreate && !Create.IsOpen)
        {
            _context?.Send(new UiEvent.Contacts(new ContactsAction.StartCreate()));
        }
    }

    /// <summary>"Blocked callers".</summary>
    [RelayCommand]
    private void OpenBlocked() => _context?.Send(new UiEvent.Blocked(new BlockedAction.Open()));
}
