using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DistrictAI.Core.Ffi;

namespace DistrictAI.ViewModels;

/// <summary>
/// "Load more" at the end of a long list: offered while the core has more,
/// disabled while a page is on its way, and why the last one failed.
/// </summary>
public sealed partial class PagingViewModel : ObservableObject
{
    private readonly Func<UiEvent> _loadMore;
    private PageContext? _context;

    internal PagingViewModel(Func<UiEvent> loadMore) => _loadMore = loadMore;

    /// <summary>Whether "Load more" is offered.</summary>
    [ObservableProperty]
    public partial bool CanLoadMore { get; set; }

    /// <summary>Whether the next page is on its way.</summary>
    [ObservableProperty]
    public partial bool LoadingMore { get; set; }

    /// <summary>Whether "Load more" can be pressed now.</summary>
    [ObservableProperty]
    public partial bool LoadMoreEnabled { get; set; }

    /// <summary>Whether the last page failed to load.</summary>
    [ObservableProperty]
    public partial bool HasMoreFailure { get; set; }

    /// <summary>Why the last page failed to load.</summary>
    [ObservableProperty]
    public partial string MoreFailureMessage { get; set; } = string.Empty;

    internal void Attach(PageContext context) => _context = context;

    internal void Show(PagingView paging)
    {
        CanLoadMore = paging.CanLoadMore;
        LoadingMore = paging.LoadingMore;
        LoadMoreEnabled = paging.CanLoadMore && !paging.LoadingMore;
        HasMoreFailure = paging.MoreFailure is not null;
        MoreFailureMessage = Display.Failure(paging.MoreFailure);
    }

    [RelayCommand]
    private void LoadMore()
    {
        if (!LoadMoreEnabled)
        {
            return;
        }
        // Disabled at once, so a second click before the core's next snapshot
        // is not a second request.
        LoadMoreEnabled = false;
        _context?.Send(_loadMore());
    }
}
