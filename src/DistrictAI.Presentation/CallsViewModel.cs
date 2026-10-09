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
        CanPlaceCall = _context?.CallsAvailable ?? false;
    }

    internal void OpenCall(CallRowItem row) => _context?.Send(new UiEvent.OpenCall(row.CallId));

    [RelayCommand]
    private void PlaceCall() => _context?.Send(new UiEvent.OpenDialer());
}
