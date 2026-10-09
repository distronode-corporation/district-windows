using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels.Workflows;
using Xunit;

namespace DistrictAI.Presentation.Tests.Workflows;

public sealed class WorkflowsViewModelTests
{
    private const string Viewer = "You are a viewer in this workspace. Ask an agency or client member to change the campaign.";
    private const string WebOnly = "The campaign's goal and batch size are changed on the District AI website.";

    private static CampaignView Campaign(
        LoadStatus? status = null,
        bool active = true,
        bool canChange = true,
        bool pending = false,
        FailureView? failure = null) =>
        new(
            "Outbound campaign",
            status ?? V.Ready,
            active,
            status is null or LoadStatus.Ready ? (active ? "Active" : "Paused") : null,
            "Batch size",
            "25",
            "Goal",
            "Book demos",
            ShowPause: active && canChange,
            CanPause: active && canChange && !pending,
            ShowResume: !active && canChange,
            CanResume: !active && canChange && !pending,
            "Pause campaign",
            "Resume campaign",
            pending,
            failure,
            canChange ? WebOnly : Viewer,
            canChange);

    private static RunsView Runs(
        RunView[]? runs = null,
        bool loading = false,
        string? note = null,
        FailureView? failure = null,
        bool canLoadMore = false,
        bool canRetry = false) =>
        new(runs ?? [], loading, note, failure, canLoadMore, "More runs", canRetry);

    private static WorkflowRowView Row(
        string id = "wf-1",
        bool active = true,
        string? lastRunAt = null,
        bool switching = false,
        bool canSwitch = true,
        RunsView? runs = null) =>
        new(id, "Follow up", active, "After a missed call · Not run yet", lastRunAt, switching, canSwitch, "Turn on Follow up", runs is not null, runs);

    private static WorkflowsView View(
        LoadStatus? status = null,
        WorkflowRowView[]? rows = null,
        EmptyView? empty = null,
        CampaignView? campaign = null,
        FailureView? toggleFailure = null,
        CampaignConfirmView? confirming = null) =>
        new("Workflows", campaign ?? Campaign(), status ?? V.Ready, empty, rows ?? [Row()], toggleFailure, confirming);

    private static (WorkflowsViewModel Model, RecordingSink Sink) Attached()
    {
        var model = new WorkflowsViewModel();
        var (context, sink) = Pages.Context();
        model.Attach(context);
        return (model, sink);
    }

    [Fact]
    public void ItShowsTheCampaignAndTheWorkflows()
    {
        var (model, _) = Attached();
        var view = View();
        model.Show(view);

        Assert.Same(view, model.View);
        Assert.Equal("Workflows", model.Title);
        Assert.True(model.Load.Ready);
        Assert.False(model.Load.ShowEmpty);
        Assert.True(model.CampaignLoad.Ready);
        Assert.Equal("Outbound campaign", model.CampaignTitle);
        Assert.Equal("Active", model.Badge);
        Assert.True(model.CampaignActive);
        Assert.Equal("Batch size", model.BatchLabel);
        Assert.Equal("25", model.Batch);
        Assert.Equal("Goal", model.GoalLabel);
        Assert.Equal("Book demos", model.Goal);
        Assert.True(model.ShowPause);
        Assert.True(model.CanPause);
        Assert.False(model.ShowResume);
        Assert.Equal("Pause campaign", model.PauseLabel);
        Assert.Equal("Resume campaign", model.ResumeLabel);
        Assert.Equal(WebOnly, model.CampaignNote);
        Assert.True(model.CanChange);
        Assert.False(model.HasCampaignFailure);
        Assert.False(model.HasToggleFailure);
        Assert.False(model.Confirming);
        var row = Assert.Single(model.Entries);
        Assert.Equal("wf-1", row.WorkflowId);
        Assert.Equal("Follow up", row.Name);
        Assert.Equal("Turn on Follow up", row.SwitchName);
        Assert.Equal("Show runs", row.RunsButtonLabel);
        Assert.Equal("Show runs of Follow up", row.RunsButtonName);
        Assert.True(row.IsWorkflow);
        Assert.False(row.IsRun || row.IsFooter || row.IsNeutral);
        Assert.Equal("Follow up, on, After a missed call · Not run yet", row.ToString());
    }

    [Fact]
    public void LoadingEmptyAndFailedAreTheLoadStates()
    {
        var model = new WorkflowsViewModel();
        model.Show(View(status: V.Loading, rows: [], campaign: Campaign(status: V.Loading)));
        Assert.True(model.Load.Loading);
        Assert.True(model.CampaignLoad.Loading);
        Assert.Equal(string.Empty, model.Badge);

        model.Show(View(rows: [], empty: new EmptyView("No workflows yet", "Built on the web.")));
        Assert.True(model.Load.ShowEmpty);
        Assert.Equal("No workflows yet", model.Load.EmptyTitle);

        model.Show(View(
            status: V.Failed(V.Failure("Down.", retryable: true), "Could not load workflows"),
            rows: [],
            campaign: Campaign(status: V.Failed(V.Failure("Down."), "Could not load the campaign"))));
        Assert.True(model.Load.Failed);
        Assert.Equal("Could not load workflows", model.Load.FailureTitle);
        Assert.True(model.Load.CanRetry);
        Assert.True(model.CampaignLoad.Failed);
        Assert.Equal("Could not load the campaign", model.CampaignLoad.FailureTitle);
        Assert.False(model.CampaignLoad.CanRetry);
    }

    [Fact]
    public void AViewerHasNoCampaignButtonsAndNoWorkingSwitches()
    {
        var (model, sink) = Attached();
        model.Show(View(rows: [Row(canSwitch: false)], campaign: Campaign(canChange: false)));

        Assert.False(model.CanChange);
        Assert.False(model.ShowPause);
        Assert.False(model.ShowResume);
        Assert.Equal(Viewer, model.CampaignNote);
        model.PauseCommand.Execute(null);
        model.ResumeCommand.Execute(null);
        model.SetActive(model.Entries[0], on: false);
        Assert.Empty(sink.Sent);

        // Runs are for every member.
        model.ToggleRuns(model.Entries[0]);
        Assert.Equal([new UiEvent.Workflows(new WorkflowsAction.ToggleExpanded("wf-1"))], sink.Sent);
    }

    [Fact]
    public void PauseAsksTheCoreOncePerPress()
    {
        var (model, sink) = Attached();
        model.Show(View());

        model.PauseCommand.Execute(null);
        model.PauseCommand.Execute(null);

        Assert.False(model.CanPause);
        Assert.Equal([new UiEvent.Workflows(new WorkflowsAction.AskCampaign(false))], sink.Sent);
    }

    [Fact]
    public void ResumeAsksTheCoreOncePerPress()
    {
        var (model, sink) = Attached();
        model.Show(View(campaign: Campaign(active: false)));
        Assert.Equal("Paused", model.Badge);
        Assert.True(model.ShowResume);

        model.ResumeCommand.Execute(null);
        model.ResumeCommand.Execute(null);

        Assert.False(model.CanResume);
        Assert.Equal([new UiEvent.Workflows(new WorkflowsAction.AskCampaign(true))], sink.Sent);
    }

    [Fact]
    public void TheCoreSQuestionIsShownAndAnswered()
    {
        var (model, sink) = Attached();
        model.Show(View(confirming: new CampaignConfirmView("Pause the outbound campaign?", "It stops.", "Pause campaign", false)));

        Assert.True(model.Confirming);
        Assert.Equal("Pause the outbound campaign?", model.ConfirmTitle);
        Assert.Equal("It stops.", model.ConfirmBody);
        Assert.Equal("Pause campaign", model.ConfirmAction);

        model.Answer(true);
        model.Answer(false);
        Assert.Equal(
            [
                new UiEvent.Workflows(new WorkflowsAction.ConfirmCampaign()),
                new UiEvent.Workflows(new WorkflowsAction.CancelCampaign()),
            ],
            sink.Sent);

        model.Show(View());
        Assert.False(model.Confirming);
        Assert.Equal(string.Empty, model.ConfirmTitle);
        Assert.Equal(string.Empty, model.ConfirmBody);
        Assert.Equal(string.Empty, model.ConfirmAction);
    }

    [Fact]
    public void APendingOrFailedChangeShowsOnTheCard()
    {
        var model = new WorkflowsViewModel();
        model.Show(View(campaign: Campaign(pending: true)));
        Assert.True(model.CampaignPending);
        Assert.False(model.CanPause);

        model.Show(View(campaign: Campaign(failure: V.Failure("Refused.", "Affected regions: EU"))));
        Assert.True(model.HasCampaignFailure);
        Assert.Equal("Refused." + Environment.NewLine + "Affected regions: EU", model.CampaignFailure);
    }

    [Fact]
    public void ASwitchSendsOnlyWhatTheMemberChanged()
    {
        var (model, sink) = Attached();
        model.Show(View(rows: [Row(active: false)]));
        var row = model.Entries[0];

        // A switch set to the row's own value, as a snapshot sets it: nothing.
        model.SetActive(row, on: false);
        Assert.Empty(sink.Sent);

        model.SetActive(row, on: true);
        Assert.Equal([new UiEvent.Workflows(new WorkflowsAction.SetActive("wf-1", true))], sink.Sent);

        // While it is being changed, the switch does nothing.
        model.Show(View(rows: [Row(active: true, switching: true, canSwitch: false)]));
        Assert.True(model.Entries[0].Switching);
        model.SetActive(model.Entries[0], on: false);
        Assert.Single(sink.Sent);
    }

    [Fact]
    public void ARefusedSwitchSaysWhyUntilDismissed()
    {
        var (model, sink) = Attached();
        model.Show(View(toggleFailure: V.Failure("Refused.")));
        Assert.True(model.HasToggleFailure);
        Assert.Equal("Refused.", model.ToggleFailure);

        model.DismissToggleFailureCommand.Execute(null);
        Assert.Equal([new UiEvent.Workflows(new WorkflowsAction.DismissToggleFailure())], sink.Sent);

        model.Show(View());
        Assert.False(model.HasToggleFailure);
    }

    [Fact]
    public void OpenRunsShowEachRunAndThePagingControls()
    {
        var (model, sink) = Attached();
        var runs = Runs(
            runs:
            [
                new RunView("r1", "Success", RunTone.Success, "2020-01-02T03:04:05Z", "Send sms: ok"),
                new RunView("r2", "Partial", RunTone.Warning, "2020-01-02T03:04:05Z", ""),
                new RunView("r3", "Failed", RunTone.Danger, "2020-01-02T03:04:05Z", "Boom"),
                new RunView("r4", "Odd", RunTone.Neutral, "2020-01-02T03:04:05Z", ""),
            ],
            canLoadMore: true);
        model.Show(View(rows: [Row(lastRunAt: "2020-01-02T03:04:05Z", runs: runs)]));

        Assert.Equal(6, model.Entries.Count);
        var row = model.Entries[0];
        var footer = model.Entries[5];
        Assert.True(row.IsWorkflow);
        Assert.True(row.Expanded);
        Assert.Equal("Hide runs", row.RunsButtonLabel);
        Assert.StartsWith("After a missed call \u00B7 Not run yet, ", row.Detail, StringComparison.Ordinal);
        var runsShown = model.Entries.Skip(1).Take(4).ToList();
        Assert.All(runsShown, run => Assert.True(run.IsRun));
        Assert.True(runsShown[0].IsSuccess);
        Assert.True(runsShown[1].IsWarning);
        Assert.True(runsShown[2].IsDanger);
        Assert.True(runsShown[3].IsNeutral);
        Assert.False(runsShown[0].IsNeutral);
        Assert.Equal("Send sms: ok", runsShown[0].Detail);
        Assert.StartsWith("Success, ", runsShown[0].ToString(), StringComparison.Ordinal);
        Assert.EndsWith(", Send sms: ok", runsShown[0].AccessibleName, StringComparison.Ordinal);
        Assert.DoesNotContain(", ,", runsShown[1].AccessibleName, StringComparison.Ordinal);
        Assert.True(footer.IsFooter);
        Assert.True(footer.CanLoadMore);
        Assert.Equal("More runs", footer.MoreLabel);
        Assert.False(footer.HasRunsNote);
        Assert.False(footer.HasRunsFailure);
        Assert.Equal(string.Empty, footer.AccessibleName);

        // A run's line is not a switch.
        model.SetActive(runsShown[0], on: false);
        model.LoadMoreRuns(footer);
        model.ToggleRuns(row);
        Assert.Equal(
            [
                new UiEvent.Workflows(new WorkflowsAction.LoadMoreRuns("wf-1")),
                new UiEvent.Workflows(new WorkflowsAction.ToggleExpanded("wf-1")),
            ],
            sink.Sent);
    }

    [Fact]
    public void RunsLoadingEmptyAndFailed()
    {
        var (model, sink) = Attached();
        model.Show(View(rows: [Row(runs: Runs(loading: true, note: "Reading runs."))]));
        var row = model.Entries[1];
        Assert.True(row.RunsLoading);
        Assert.True(row.HasRunsNote);
        Assert.Equal("Reading runs.", row.RunsNote);
        model.LoadMoreRuns(row);
        Assert.Empty(sink.Sent);

        model.Show(View(rows: [Row(runs: Runs(note: "This workflow has not run yet."))]));
        Assert.Equal("This workflow has not run yet.", model.Entries[1].RunsNote);
        model.LoadMoreRuns(model.Entries[1]);
        Assert.Empty(sink.Sent);

        model.Show(View(rows: [Row(runs: Runs(failure: V.Failure("Down."), canRetry: true))]));
        Assert.True(model.Entries[1].HasRunsFailure);
        Assert.Equal("Down.", model.Entries[1].RunsFailure);
        Assert.True(model.Entries[1].CanRetryRuns);
        model.RetryRuns();
        Assert.Equal([new UiEvent.Refresh()], sink.Sent);
    }

    [Fact]
    public void AnUnchangedRowIsKeptAndAChangedOneReplaced()
    {
        var model = new WorkflowsViewModel();
        var runs = Runs(runs: [new RunView("r1", "Success", RunTone.Success, "2020-01-02T03:04:05Z", "ok")]);
        model.Show(View(rows: [Row(runs: runs)]));
        var first = model.Entries[1];

        model.Show(View(rows: [Row(runs: runs with { Runs = [.. runs.Runs] })]));
        Assert.Same(first, model.Entries[1]);
        Assert.Equal(first.GetHashCode(), model.Entries[1].GetHashCode());

        model.Show(View(rows: [Row(runs: runs with { Runs = [] })]));
        Assert.NotSame(first, model.Entries[1]);
        Assert.True(first.IsRun);
        Assert.NotEqual(first, model.Entries[1]);
    }

    [Fact]
    public void ItSendsTheAreaSActions()
    {
        var model = new WorkflowsViewModel();
        model.Send(new WorkflowsAction.Open());

        var (context, sink) = Pages.Context();
        model.Attach(context);
        model.Send(new WorkflowsAction.Open());
        model.RetryRuns();

        Assert.Equal([new UiEvent.Workflows(new WorkflowsAction.Open()), new UiEvent.Refresh()], sink.Sent);
    }
}
