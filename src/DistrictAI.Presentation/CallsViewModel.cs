using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DistrictAI.Core.Ffi;

namespace DistrictAI.ViewModels;

/// <summary>The call log, a page at a time.</summary>
public sealed partial class CallsViewModel : ObservableObject
{
    private PageContext? _context;

    /// <summary>Loading, failure, the empty log and refresh.</summary>
    public LoadStateViewModel Load { get; } = new();

    /// <summary>"Load more" at the end of the list.</summary>
    public PagingViewModel Paging { get; } = new(() => new UiEvent.LoadMoreCalls());

    /// <summary>The calls.</summary>
    public ObservableCollection<CallRowItem> Rows { get; } = [];

    /// <summary>The calls under way, over the log: each opens the page where it is listened to.</summary>
    public ObservableCollection<CallRowItem> Live { get; } = [];

    /// <summary>Whether any call is under way, so the Live calls list shows.</summary>
    [ObservableProperty]
    public partial bool HasLive { get; set; }

    /// <summary>"Live calls".</summary>
    [ObservableProperty]
    public partial string LiveTitle { get; set; } = string.Empty;

    /// <summary>What can be done from a live call.</summary>
    [ObservableProperty]
    public partial string LiveBody { get; set; } = string.Empty;

    /// <summary>Whether "Place a call" is offered: in a build that can carry calls.</summary>
    [ObservableProperty]
    public partial bool CanPlaceCall { get; set; }

    internal void Attach(PageContext context)
    {
        _context = context;
        Load.Attach(context);
        Paging.Attach(context);
    }

    internal void Show(CallsView view)
    {
        Load.Show(view.Status, view.Rows.Length > 0, view.Empty, view.Paging.Refreshing, view.Paging.RefreshFailure);
        Paging.Show(view.Paging);
        Display.Sync(Rows, [.. view.Rows.Select(CallRowItem.From)]);
        Display.Sync(Live, [.. (view.Live?.Rows ?? []).Select(CallRowItem.From)]);
        HasLive = Live.Count > 0;
        LiveTitle = view.Live?.Title ?? string.Empty;
        LiveBody = view.Live?.Body ?? string.Empty;
        CanPlaceCall = _context?.CallsAvailable ?? false;
    }

    internal void OpenCall(CallRowItem row) => _context?.Send(new UiEvent.OpenCall(row.CallId));

    [RelayCommand]
    private void PlaceCall() => _context?.Send(new UiEvent.OpenDialer());
}
