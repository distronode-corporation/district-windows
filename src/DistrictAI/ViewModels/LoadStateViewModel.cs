using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DistrictAI.Core.Ffi;

namespace DistrictAI.ViewModels;

/// <summary>
/// The part every list and detail page shares: loading, a load that failed
/// (with "Try again" when the core says it may work), an empty list, and a
/// refresh under way or failed over content already shown.
/// </summary>
public sealed partial class LoadStateViewModel : ObservableObject
{
    private PageContext? _context;

    /// <summary>Whether the first load is under way.</summary>
    [ObservableProperty]
    public partial bool Loading { get; set; }

    /// <summary>Whether the first load failed.</summary>
    [ObservableProperty]
    public partial bool Failed { get; set; }

    /// <summary>Why it failed.</summary>
    [ObservableProperty]
    public partial string FailureMessage { get; set; } = string.Empty;

    /// <summary>Which regions the failure touches, or empty.</summary>
    [ObservableProperty]
    public partial string RegionsLine { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="RegionsLine"/>.</summary>
    [ObservableProperty]
    public partial bool HasRegionsLine { get; set; }

    /// <summary>Whether "Try again" is offered for the failed load.</summary>
    [ObservableProperty]
    public partial bool CanRetry { get; set; }

    /// <summary>Whether the content is loaded.</summary>
    [ObservableProperty]
    public partial bool Ready { get; set; }

    /// <summary>Whether the content is loaded and there is nothing in it.</summary>
    [ObservableProperty]
    public partial bool ShowEmpty { get; set; }

    /// <summary>The empty state's heading.</summary>
    [ObservableProperty]
    public partial string EmptyTitle { get; set; } = string.Empty;

    /// <summary>The empty state's text.</summary>
    [ObservableProperty]
    public partial string EmptyBody { get; set; } = string.Empty;

    /// <summary>Whether a refresh is under way over loaded content.</summary>
    [ObservableProperty]
    public partial bool Refreshing { get; set; }

    /// <summary>Whether the last refresh failed, the loaded content still showing.</summary>
    [ObservableProperty]
    public partial bool HasRefreshFailure { get; set; }

    /// <summary>Why the last refresh failed.</summary>
    [ObservableProperty]
    public partial string RefreshFailureMessage { get; set; } = string.Empty;

    /// <summary>Whether "Try again" is offered for the failed refresh.</summary>
    [ObservableProperty]
    public partial bool CanRetryRefresh { get; set; }

    internal void Attach(PageContext context) => _context = context;

    /// <summary>
    /// Shows <paramref name="status"/>. <paramref name="hasRows"/> says whether
    /// there is anything to list, and <paramref name="empty"/> what to say when
    /// there is not.
    /// </summary>
    internal void Show(LoadStatus status, bool hasRows, EmptyView? empty, bool refreshing, FailureView? refreshFailure)
    {
        var failure = status is LoadStatus.Failed failed ? failed.Failure : null;
        Loading = status is LoadStatus.Loading;
        Failed = failure is not null;
        FailureMessage = failure?.Message ?? string.Empty;
        RegionsLine = failure?.RegionsLine ?? string.Empty;
        HasRegionsLine = RegionsLine.Length > 0;
        CanRetry = failure?.Retryable ?? false;
        Ready = status is LoadStatus.Ready;
        ShowEmpty = Ready && !hasRows && empty is not null;
        EmptyTitle = empty?.Title ?? string.Empty;
        EmptyBody = empty?.Body ?? string.Empty;
        Refreshing = Ready && refreshing;
        HasRefreshFailure = refreshFailure is not null;
        RefreshFailureMessage = Display.Failure(refreshFailure);
        CanRetryRefresh = refreshFailure?.Retryable ?? false;
    }

    [RelayCommand]
    private void Retry() => _context?.Send(new UiEvent.Refresh());
}
