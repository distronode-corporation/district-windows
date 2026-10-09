using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels.Settings.CallHandling;
using Xunit;

namespace DistrictAI.Presentation.Tests.Settings.CallHandling;

public sealed class CallHandlingViewModelTests
{
    private static AvailabilityPanelView Availability(
        LoadStatus? status = null,
        bool available = true,
        bool showSwitch = true,
        bool canToggle = true,
        string? blocked = null,
        bool changing = false,
        SaveNoticeView? notice = null) =>
        new("Your availability", "Whether your own devices ring.", status ?? V.Ready, "Ring me for calls",
            available, showSwitch, canToggle, blocked, changing, notice);

    private static CallHandlingModeView Mode(CallHandlingChoice mode, bool selected) =>
        new(mode, mode.ToString(), "What it does.", selected);

    private static CallHandlingView View(
        LoadStatus? status = null,
        CallHandlingModeView[]? modes = null,
        string? unknown = null,
        long ring = 20,
        bool canChange = true,
        bool canEdit = true,
        bool canSave = false,
        bool saving = false,
        SaveNoticeView? notice = null,
        string? viewerNote = null,
        AvailabilityPanelView? availability = null) =>
        new(
            "Call handling",
            "Who answers",
            "For every member of this workspace.",
            status ?? V.Ready,
            modes ?? [Mode(CallHandlingChoice.AiFirst, true), Mode(CallHandlingChoice.AiThenApp, false), Mode(CallHandlingChoice.AppFirst, false)],
            unknown,
            "How long your devices ring",
            "Between 5 and 30 seconds.",
            ring,
            ring + " seconds",
            5,
            30,
            canChange,
            canEdit,
            canSave,
            saving,
            notice,
            viewerNote,
            availability ?? Availability());

    private static (CallHandlingViewModel Model, RecordingSink Sink) Attached(CallHandlingView view)
    {
        var (context, sink) = Pages.Context();
        var model = new CallHandlingViewModel();
        model.Attach(context);
        model.Show(view);
        return (model, sink);
    }

    [Fact]
    public void ItShowsTheCoreWordsAndSendsNothingByShowing()
    {
        var (model, sink) = Attached(View(unknown: "Stored as \"ai_overnight\"."));
        Assert.Equal("Call handling", model.Title);
        Assert.True(model.HandlingReady);
        Assert.False(model.HandlingLoading);
        Assert.Equal(3, model.Modes.Count);
        Assert.True(model.Modes[0].Selected);
        Assert.Equal("AiFirst", model.Modes[0].ToString());
        Assert.True(model.HasUnknownMode);
        Assert.Equal(20, model.Ring);
        Assert.Equal("20 seconds", model.RingWords);
        Assert.True(model.ShowSave);
        Assert.False(model.ShowRingWords);
        Assert.True(model.Available);
        Assert.True(model.ShowSwitch);
        Assert.Empty(sink.Sent);
    }

    [Fact]
    public void LoadingAndFailuresShowApart()
    {
        var (model, _) = Attached(View(
            status: V.Loading,
            availability: Availability(status: V.Failed(V.Failure("Offline.", retryable: true), "Could not read whether you are rung"))));
        Assert.True(model.HandlingLoading);
        Assert.False(model.HandlingReady);
        Assert.False(model.ShowSave);
        Assert.True(model.AvailabilityFailed);
        Assert.True(model.CanRetryAvailability);
        Assert.Equal("Could not read whether you are rung. Offline.", model.AvailabilityFailure);

        model.Show(View(status: V.Failed(V.Failure("Gone."), "Could not load this workspace's settings")));
        Assert.True(model.HandlingFailed);
        Assert.False(model.CanRetryHandling);
        Assert.False(model.AvailabilityFailed);
    }

    [Fact]
    public void ChoosingAModeAndTheRingSendsEach()
    {
        var (model, sink) = Attached(View());
        model.SelectMode(model.Modes[0]);
        model.SelectMode(model.Modes[2]);
        model.Ring = 12.4;
        Assert.Equal(
            [
                new UiEvent.CallHandling(new CallHandlingAction.SelectMode(CallHandlingChoice.AppFirst)),
                new UiEvent.CallHandling(new CallHandlingAction.SetRingSeconds(12)),
            ],
            sink.Sent);

        // The core echoes the ring it took; the slider is not moved back by it.
        model.Show(View(ring: 12, canSave: true));
        Assert.Equal(12.4, model.Ring);
        // A value the slider did not send (the save stored another) is written.
        model.Show(View(ring: 30));
        Assert.Equal(30, model.Ring);
        Assert.Equal(2, sink.Sent.Count);
    }

    [Fact]
    public void SaveIsOnePressAndTheNoticeIsTheCores()
    {
        var (model, sink) = Attached(View(canSave: true));
        Assert.True(model.SaveCommand.CanExecute(null));
        model.SaveCommand.Execute(null);
        model.SaveCommand.Execute(null);
        Assert.Equal([new UiEvent.CallHandling(new CallHandlingAction.Save())], sink.Sent);

        model.Show(View(saving: true, canEdit: false));
        Assert.True(model.Saving);
        model.Show(View(notice: new SaveNoticeView("Saved.", true)));
        Assert.True(model.HasNotice);
        Assert.True(model.NoticeSaved);
        Assert.Equal("Saved.", model.Notice);
        model.DismissNoticesCommand.Execute(null);
        Assert.Equal(new UiEvent.CallHandling(new CallHandlingAction.DismissNotices()), sink.Sent[^1]);
    }

    [Fact]
    public void TheSwitchSendsAtOnceOnceAndIsPutBackWhenRefused()
    {
        var (model, sink) = Attached(View());
        model.Available = false;
        Assert.False(model.CanToggle);
        model.Available = true;
        Assert.Equal([new UiEvent.CallHandling(new CallHandlingAction.SetAvailable(false))], sink.Sent);

        model.Show(View(availability: Availability(canToggle: false, changing: true)));
        Assert.True(model.Changing);
        model.Show(View(availability: Availability(available: true, notice: new SaveNoticeView("Refused.", false))));
        Assert.True(model.Available);
        Assert.True(model.HasAvailabilityNotice);
        Assert.False(model.AvailabilityNoticeSaved);
        Assert.Single(sink.Sent);
    }

    [Fact]
    public void AViewerReadsAndSendsNothing()
    {
        var (model, sink) = Attached(View(
            modes: [Mode(CallHandlingChoice.AiThenApp, true)],
            canChange: false,
            canEdit: false,
            viewerNote: "Only agency and client members can change how this workspace answers calls.",
            availability: Availability(available: false, showSwitch: false, canToggle: false, blocked: "Viewers are not rung for calls.")));
        Assert.True(model.HasViewerNote);
        Assert.True(model.ShowRingWords);
        Assert.False(model.ShowSave);
        Assert.True(model.HasBlocked);
        Assert.False(model.Modes[0].CanEdit);
        model.SelectMode(new CallHandlingModeItem(CallHandlingChoice.AiFirst, "x", "y", false, false));
        model.Ring = 9;
        model.Available = true;
        Assert.Empty(sink.Sent);
    }

    [Fact]
    public void RetryReadsAgain()
    {
        var (model, sink) = Attached(View());
        model.RetryCommand.Execute(null);
        Assert.Equal([new UiEvent.Refresh()], sink.Sent);

        var detached = new CallHandlingViewModel();
        detached.Send(new CallHandlingAction.Save());
        detached.RetryCommand.Execute(null);
    }
}
