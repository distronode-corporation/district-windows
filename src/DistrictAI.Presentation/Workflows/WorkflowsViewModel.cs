using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DistrictAI.Core.Ffi;

namespace DistrictAI.ViewModels.Workflows;

/// <summary>
/// The workflows: the outbound campaign's card, and each workflow with its
/// switch and its runs. Pausing or resuming the campaign goes through the
/// core's question, which the page shows as a dialog for exactly as long as
/// the core asks it. A role that may change nothing sees the same page with
/// the switches off and no campaign buttons.
/// </summary>
public sealed partial class WorkflowsViewModel : ObservableObject
{
    private PageContext? _context;

    /// <summary>The core's view of the screen.</summary>
    [ObservableProperty]
    public partial WorkflowsView? View { get; set; }

    /// <summary>The page's heading.</summary>
    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;

    /// <summary>Loading, failure and empty, for the list of workflows.</summary>
    public LoadStateViewModel Load { get; } = new();

    /// <summary>Loading and failure, for the campaign's card.</summary>
    public LoadStateViewModel CampaignLoad { get; } = new();

    /// <summary>The workflows, newest first.</summary>
    public ObservableCollection<WorkflowItem> Rows { get; } = [];

    /// <summary>The campaign card's heading.</summary>
    [ObservableProperty]
    public partial string CampaignTitle { get; set; } = string.Empty;

    /// <summary>"Active" or "Paused", once read.</summary>
    [ObservableProperty]
    public partial string Badge { get; set; } = string.Empty;

    /// <summary>Whether the campaign is calling.</summary>
    [ObservableProperty]
    public partial bool CampaignActive { get; set; }

    /// <summary>The batch size row's label.</summary>
    [ObservableProperty]
    public partial string BatchLabel { get; set; } = string.Empty;

    /// <summary>The batch size, or that none is set.</summary>
    [ObservableProperty]
    public partial string Batch { get; set; } = string.Empty;

    /// <summary>The goal row's label.</summary>
    [ObservableProperty]
    public partial string GoalLabel { get; set; } = string.Empty;

    /// <summary>The goal, or that none is set.</summary>
    [ObservableProperty]
    public partial string Goal { get; set; } = string.Empty;

    /// <summary>Whether "Pause campaign" is shown.</summary>
    [ObservableProperty]
    public partial bool ShowPause { get; set; }

    /// <summary>Whether "Pause campaign" can be pressed now.</summary>
    [ObservableProperty]
    public partial bool CanPause { get; set; }

    /// <summary>Whether "Resume campaign" is shown.</summary>
    [ObservableProperty]
    public partial bool ShowResume { get; set; }

    /// <summary>Whether "Resume campaign" can be pressed now.</summary>
    [ObservableProperty]
    public partial bool CanResume { get; set; }

    /// <summary>"Pause campaign".</summary>
    [ObservableProperty]
    public partial string PauseLabel { get; set; } = string.Empty;

    /// <summary>"Resume campaign".</summary>
    [ObservableProperty]
    public partial string ResumeLabel { get; set; } = string.Empty;

    /// <summary>Whether a pause or resume is on its way.</summary>
    [ObservableProperty]
    public partial bool CampaignPending { get; set; }

    /// <summary>Why the last pause or resume failed, or empty.</summary>
    [ObservableProperty]
    public partial string CampaignFailure { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="CampaignFailure"/>.</summary>
    [ObservableProperty]
    public partial bool HasCampaignFailure { get; set; }

    /// <summary>What is still changed on the web, or why a viewer has no buttons.</summary>
    [ObservableProperty]
    public partial string CampaignNote { get; set; } = string.Empty;

    /// <summary>Whether the member may change the campaign and the workflows.</summary>
    [ObservableProperty]
    public partial bool CanChange { get; set; }

    /// <summary>Why the last change of a workflow failed, or empty.</summary>
    [ObservableProperty]
    public partial string ToggleFailure { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="ToggleFailure"/>.</summary>
    [ObservableProperty]
    public partial bool HasToggleFailure { get; set; }

    /// <summary>Whether the core is asking before a pause or resume.</summary>
    public bool Confirming { get; private set; }

    /// <summary>The question's heading.</summary>
    public string ConfirmTitle { get; private set; } = string.Empty;

    /// <summary>What answering yes would do.</summary>
    public string ConfirmBody { get; private set; } = string.Empty;

    /// <summary>The button that answers yes.</summary>
    public string ConfirmAction { get; private set; } = string.Empty;

    internal void Attach(PageContext context)
    {
        _context = context;
        Load.Attach(context);
        CampaignLoad.Attach(context);
    }

    internal void Show(WorkflowsView view)
    {
        View = view;
        Title = view.Title;
        Load.Show(view.Status, view.Rows.Length > 0, view.Empty, refreshing: false, refreshFailure: null);
        Display.Sync(Rows, [.. view.Rows.Select(WorkflowItem.From)]);
        ToggleFailure = Display.Failure(view.ToggleFailure);
        HasToggleFailure = ToggleFailure.Length > 0;

        var campaign = view.Campaign;
        CampaignLoad.Show(campaign.Status, hasRows: true, empty: null, refreshing: false, refreshFailure: null);
        CampaignTitle = campaign.Title;
        Badge = campaign.Badge ?? string.Empty;
        CampaignActive = campaign.Active;
        BatchLabel = campaign.BatchLabel;
        Batch = campaign.Batch;
        GoalLabel = campaign.GoalLabel;
        Goal = campaign.Goal;
        ShowPause = campaign.ShowPause;
        CanPause = campaign.CanPause;
        ShowResume = campaign.ShowResume;
        CanResume = campaign.CanResume;
        PauseLabel = campaign.PauseLabel;
        ResumeLabel = campaign.ResumeLabel;
        CampaignPending = campaign.Pending;
        CampaignFailure = Display.Failure(campaign.Failure);
        HasCampaignFailure = CampaignFailure.Length > 0;
        CampaignNote = campaign.Note;
        CanChange = campaign.CanChange;

        Confirming = view.Confirming is not null;
        ConfirmTitle = view.Confirming?.Title ?? string.Empty;
        ConfirmBody = view.Confirming?.Body ?? string.Empty;
        ConfirmAction = view.Confirming?.Action ?? string.Empty;
    }

    /// <summary>Forwards one of the screen's actions.</summary>
    internal void Send(WorkflowsAction action) => _context?.Send(new UiEvent.Workflows(action));

    /// <summary>Opens or closes <paramref name="row"/>'s runs.</summary>
    internal void ToggleRuns(WorkflowItem row) => Send(new WorkflowsAction.ToggleExpanded(row.WorkflowId));

    /// <summary>Reads the next page of <paramref name="row"/>'s runs, once per page.</summary>
    internal void LoadMoreRuns(WorkflowItem row)
    {
        if (!row.CanLoadMore || row.RunsLoading)
        {
            return;
        }
        Send(new WorkflowsAction.LoadMoreRuns(row.WorkflowId));
    }

    /// <summary>
    /// Turns <paramref name="row"/> on or off when the switch says other than
    /// the row: a switch set by a snapshot (the core's value, or a value put
    /// back) sends nothing, and one already being changed or locked by the
    /// role sends nothing either.
    /// </summary>
    internal void SetActive(WorkflowItem row, bool on)
    {
        if (!row.CanSwitch || row.Active == on)
        {
            return;
        }
        Send(new WorkflowsAction.SetActive(row.WorkflowId, on));
    }

    /// <summary>A first page of runs that failed is read again with the whole screen.</summary>
    internal void RetryRuns() => _context?.Send(new UiEvent.Refresh());

    /// <summary>The answer to the campaign's question: yes sends it, no cancels.</summary>
    internal void Answer(bool confirmed) =>
        Send(confirmed ? new WorkflowsAction.ConfirmCampaign() : new WorkflowsAction.CancelCampaign());

    [RelayCommand]
    private void Pause()
    {
        if (!CanPause)
        {
            return;
        }
        // Until the core's next snapshot: one press, one question.
        CanPause = false;
        Send(new WorkflowsAction.AskCampaign(false));
    }

    [RelayCommand]
    private void Resume()
    {
        if (!CanResume)
        {
            return;
        }
        CanResume = false;
        Send(new WorkflowsAction.AskCampaign(true));
    }

    [RelayCommand]
    private void DismissToggleFailure() => Send(new WorkflowsAction.DismissToggleFailure());
}
