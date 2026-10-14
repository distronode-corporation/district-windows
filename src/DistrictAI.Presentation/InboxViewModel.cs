using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DistrictAI.Core.Ffi;
using DistrictAI.L10n;
using DistrictAI.ViewModels.Inbox;

namespace DistrictAI.ViewModels;

/// <summary>The inbox: the conversations, and the search over every message.</summary>
public sealed partial class InboxViewModel : ObservableObject
{
    private PageContext? _context;

    /// <summary>Loading, failure, the empty inbox and refresh.</summary>
    public LoadStateViewModel Load { get; } = new();

    /// <summary>The conversations.</summary>
    public ObservableCollection<ThreadRowItem> Threads { get; } = [];

    /// <summary>The messages matching the search.</summary>
    public ObservableCollection<SearchHitItem> Hits { get; } = [];

    /// <summary>The new message form, a dialog while the core holds it open.</summary>
    public ComposeViewModel Compose { get; } = new();

    /// <summary>Whether "New message" is offered: not to a viewer, who cannot send.</summary>
    [ObservableProperty]
    public partial bool CanCompose { get; set; }

    /// <summary>The "New message" button's words.</summary>
    [ObservableProperty]
    public partial string ComposeLabel { get; set; } = string.Empty;

    /// <summary>That replies are sent elsewhere.</summary>
    [ObservableProperty]
    public partial string ReadOnlyNote { get; set; } = string.Empty;

    /// <summary>That some conversations could not be loaded, or empty.</summary>
    [ObservableProperty]
    public partial string PartialNote { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="PartialNote"/>.</summary>
    [ObservableProperty]
    public partial bool HasPartialNote { get; set; }

    /// <summary>The query the core is searching for.</summary>
    [ObservableProperty]
    public partial string Query { get; set; } = string.Empty;

    /// <summary>Whether the search results show instead of the conversations.</summary>
    [ObservableProperty]
    public partial bool SearchActive { get; set; }

    /// <summary>Whether the conversations show.</summary>
    [ObservableProperty]
    public partial bool ShowThreads { get; set; }

    /// <summary>Whether the loading, failed or empty state of the conversations shows (not while searching).</summary>
    [ObservableProperty]
    public partial bool ShowStatus { get; set; } = true;

    /// <summary>Whether a search is under way.</summary>
    [ObservableProperty]
    public partial bool SearchRunning { get; set; }

    /// <summary>A line under the search box: too short a query, no matches, or only the first matches shown.</summary>
    [ObservableProperty]
    public partial string SearchNote { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="SearchNote"/>.</summary>
    [ObservableProperty]
    public partial bool HasSearchNote { get; set; }

    /// <summary>Why the search failed.</summary>
    [ObservableProperty]
    public partial string SearchFailure { get; set; } = string.Empty;

    /// <summary>Whether the search failed.</summary>
    [ObservableProperty]
    public partial bool HasSearchFailure { get; set; }

    internal void Attach(PageContext context)
    {
        _context = context;
        Load.Attach(context);
        Compose.Attach(context);
    }

    internal void Show(InboxView view)
    {
        Load.Show(view.Status, view.Threads.Length > 0, view.Empty, view.Refreshing, view.RefreshFailure);
        ReadOnlyNote = view.ReadOnlyNote;
        CanCompose = view.NewMessage.Offered;
        ComposeLabel = view.NewMessage.Label;
        Compose.Show(view.Compose);
        PartialNote = view.PartialNote ?? string.Empty;
        HasPartialNote = view.PartialNote is not null;
        Display.Sync(Threads, [.. view.Threads.Select(ThreadRowItem.From)]);

        var search = view.Search;
        Query = search.Query;
        SearchActive = search.Active;
        ShowThreads = Load.Ready && !search.Active;
        ShowStatus = !search.Active;
        SearchRunning = search.Running;
        HasSearchFailure = search.Failure is not null;
        SearchFailure = Display.Failure(search.Failure);
        Display.Sync(Hits, [.. search.Hits.Select(SearchHitItem.From)]);
        SearchNote = SearchNoteFor(search);
        HasSearchNote = SearchActive && SearchNote.Length > 0;
    }

    private static string SearchNoteFor(SearchView search)
    {
        if (!search.Active || search.Running || search.Failure is not null)
        {
            return string.Empty;
        }
        if (search.Query.Trim().Length < search.MinQueryLength)
        {
            return S.InboxMinQuery(search.MinQueryLength);
        }
        if (search.Hits.Length == 0)
        {
            return search.Empty is { } empty ? JoinLines(empty.Title, empty.Body) : string.Empty;
        }
        return search.Truncated ? search.TruncatedNote ?? string.Empty : string.Empty;
    }

    private static string JoinLines(string first, string second)
    {
        if (second.Length == 0)
        {
            return first;
        }
        return first.Length == 0 ? second : first + Environment.NewLine + second;
    }

    /// <summary>The search box changed: a new query, or cleared. The core waits for typing to pause.</summary>
    internal void Search(string text)
    {
        if (string.Equals(text, Query, StringComparison.Ordinal))
        {
            return;
        }
        Query = text;
        _context?.Send(text.Length == 0 ? new UiEvent.ClearSearch() : new UiEvent.Search(text));
    }

    /// <summary>"New message": the core opens the form.</summary>
    [RelayCommand]
    private void NewMessage()
    {
        if (CanCompose && !Compose.IsOpen)
        {
            _context?.Send(new UiEvent.Compose(new ComposeAction.Open()));
        }
    }

    internal void OpenThread(ThreadRowItem row)
    {
        if (row.CanOpen)
        {
            _context?.Send(new UiEvent.OpenThread(row.ThreadKey));
        }
    }

    internal void OpenHit(SearchHitItem hit)
    {
        if (hit.CanOpen)
        {
            _context?.Send(new UiEvent.OpenThread(hit.ThreadKey));
        }
    }
}
