using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DistrictAI.Core.Ffi;

namespace DistrictAI.ViewModels;

/// <summary>The contacts, a page at a time. Read-only in this build.</summary>
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

    internal void Attach(PageContext context)
    {
        _context = context;
        Load.Attach(context);
        Paging.Attach(context);
    }

    internal void Show(ContactsView view)
    {
        Load.Show(view.Status, view.Rows.Length > 0, view.Empty, view.Paging.Refreshing, view.Paging.RefreshFailure);
        Paging.Show(view.Paging);
        TotalLabel = view.TotalLabel ?? string.Empty;
        HasTotalLabel = view.TotalLabel is not null;
        Display.Sync(Rows, [.. view.Rows.Select(ContactRowItem.From)]);
    }

    internal void OpenContact(ContactRowItem row) => _context?.Send(new UiEvent.OpenContact(row.ContactId));
}
