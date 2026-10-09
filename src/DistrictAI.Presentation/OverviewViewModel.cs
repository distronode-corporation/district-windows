using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels.Billing;

namespace DistrictAI.ViewModels;

/// <summary>
/// The overview: the workspace's numbers, its recent calls, and the finish-setup
/// card; and, for an account with no workspace yet that the core offers the
/// plans, the billing screen's chooser, since checkout makes the first workspace.
/// </summary>
public sealed partial class OverviewViewModel : ObservableObject
{
    private PageContext? _context;

    /// <summary>Loading, failure and refresh.</summary>
    public LoadStateViewModel Load { get; } = new();

    /// <summary>The overview's numbers.</summary>
    public ObservableCollection<FactItem> Metrics { get; } = [];

    /// <summary>The latest calls.</summary>
    public ObservableCollection<CallRowItem> RecentCalls { get; } = [];

    /// <summary>The plan chooser, for an account with no workspace.</summary>
    public PlanChooserViewModel Purchase { get; } = new();

    /// <summary>
    /// Whether the no-workspace page with the chooser shows, in place of the
    /// plain status page: no workspace, and the plans offered.
    /// </summary>
    [ObservableProperty]
    public partial bool OffersPlans { get; set; }

    /// <summary>Whether the plain status page (loading, or a page with no way forward) shows.</summary>
    [ObservableProperty]
    public partial bool StatusShown { get; set; } = true;

    /// <summary>The workspace's name.</summary>
    [ObservableProperty]
    public partial string WorkspaceName { get; set; } = string.Empty;

    /// <summary>The read-only badge, or empty.</summary>
    [ObservableProperty]
    public partial string ReadOnlyBadge { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="ReadOnlyBadge"/>.</summary>
    [ObservableProperty]
    public partial bool HasReadOnlyBadge { get; set; }

    /// <summary>Whether the finish-setup card shows.</summary>
    [ObservableProperty]
    public partial bool HasFinishSetup { get; set; }

    /// <summary>The finish-setup card's heading.</summary>
    [ObservableProperty]
    public partial string FinishSetupTitle { get; set; } = string.Empty;

    /// <summary>The finish-setup card's text.</summary>
    [ObservableProperty]
    public partial string FinishSetupBody { get; set; } = string.Empty;

    /// <summary>The finish-setup card's button.</summary>
    [ObservableProperty]
    public partial string FinishSetupAction { get; set; } = string.Empty;

    /// <summary>Whether the loaded workspace has no calls yet.</summary>
    [ObservableProperty]
    public partial bool NoRecentCalls { get; set; }

    /// <summary>What to say when there are no recent calls.</summary>
    [ObservableProperty]
    public partial string RecentCallsEmpty { get; set; } = string.Empty;

    internal void Attach(PageContext context)
    {
        _context = context;
        Load.Attach(context);
        Purchase.Attach(context);
    }

    internal void Show(OverviewView view)
    {
        Load.Show(view.Status, hasRows: true, empty: null, view.Refreshing, view.RefreshFailure);
        WorkspaceName = view.WorkspaceName;
        ReadOnlyBadge = view.ReadOnlyBadge ?? string.Empty;
        HasReadOnlyBadge = view.ReadOnlyBadge is not null;
        HasFinishSetup = view.FinishSetup is not null;
        FinishSetupTitle = view.FinishSetup?.Title ?? string.Empty;
        FinishSetupBody = view.FinishSetup?.Body ?? string.Empty;
        FinishSetupAction = view.FinishSetup?.Action ?? string.Empty;
        Display.Sync(Metrics, [.. view.Metrics.Select(FactItem.From)]);
        Display.Sync(RecentCalls, [.. view.RecentCalls.Select(CallRowItem.From)]);
        RecentCallsEmpty = view.RecentCallsEmpty ?? string.Empty;
        NoRecentCalls = Load.Ready && view.RecentCalls.Length == 0 && RecentCallsEmpty.Length > 0;
        Purchase.Show(view.Purchase);
        OffersPlans = view.Purchase is not null && Load.Failed;
        StatusShown = !OffersPlans;
    }

    internal void OpenCall(CallRowItem row) => _context?.Send(new UiEvent.OpenCall(row.CallId));

    [RelayCommand]
    private void FinishSetup() => _context?.Send(new UiEvent.OpenFinishSetup());
}
