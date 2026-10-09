using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels.Calls;
using Xunit;

namespace DistrictAI.Presentation.Tests.Calls;

public sealed class CallBarViewModelTests
{
    private static readonly DateTimeOffset _answered = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
    private const string AnsweredIso = "2026-10-08T12:00:00Z";

    [Fact]
    public void NoCallHidesTheStrip()
    {
        var bar = new CallBarViewModel();
        bar.Show(V.Call(microphoneDenied: true, mediaNotice: "Reconnecting."));
        bar.Show(null);
        Assert.False(bar.IsShown);
        Assert.False(bar.MicrophoneDenied);
        Assert.False(bar.HasMediaNotice);
    }

    [Fact]
    public void ACallNotYetAnsweredShowsWhereItStands()
    {
        var time = new ManualTime(_answered);
        var bar = new CallBarViewModel(time);
        bar.Show(V.Call(peer: "+1 212 555 0100", state: "Calling.", muted: true));

        Assert.True(bar.IsShown);
        Assert.Equal("+1 212 555 0100", bar.Peer);
        Assert.Equal("Calling.", bar.Status);
        Assert.True(bar.Muted);
        Assert.True(bar.CanMute);
        Assert.True(bar.CanHangUp);
        Assert.True(bar.HangUpCommand.CanExecute(null));
        Assert.False(bar.CanDismiss);
        Assert.False(bar.HasEnded);
        Assert.False(bar.HasEndedNote);
        Assert.False(bar.HasFailure);
        Assert.Equal(0, time.TimersCreated);
    }

    [Fact]
    public void AnAnsweredCallCountsEachSecondFromWhenItWasAnswered()
    {
        var time = new ManualTime(_answered.AddSeconds(5));
        var bar = new CallBarViewModel(time);
        WithNoContext(() => bar.Show(V.Call(state: "00:04", connectedAt: AnsweredIso)));
        Assert.Equal("00:05", bar.Status);

        time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal("00:06", bar.Status);
        time.Advance(TimeSpan.FromMinutes(61));
        Assert.Equal("61:06", bar.Status);
    }

    [Fact]
    public void TicksGoBackToTheThreadThatStartedTheTimer()
    {
        var time = new ManualTime(_answered);
        var bar = new CallBarViewModel(time);
        var ui = new QueueingContext();
        ui.Within(() => bar.Show(V.Call(connectedAt: AnsweredIso)));
        Assert.Equal("00:00", bar.Status);

        time.Advance(TimeSpan.FromSeconds(2));
        // Nothing changes off the UI thread.
        Assert.Equal("00:00", bar.Status);
        Assert.Equal(2, ui.Pending);

        ui.RunAll();
        Assert.Equal("00:02", bar.Status);
    }

    [Fact]
    public void ATickThatArrivesAfterTheCallEndedChangesNothing()
    {
        var time = new ManualTime(_answered);
        var bar = new CallBarViewModel(time);
        var ui = new QueueingContext();
        ui.Within(() => bar.Show(V.Call(connectedAt: AnsweredIso)));
        time.Advance(TimeSpan.FromSeconds(1));

        bar.Show(V.Call(state: "Call ended", canHangUp: false));
        ui.RunAll();
        Assert.Equal("Call ended", bar.Status);
    }

    [Fact]
    public void TheTimerStopsWhenTheCallIsOverAndIsReusedForTheNext()
    {
        var time = new ManualTime(_answered);
        var bar = new CallBarViewModel(time);
        WithNoContext(() =>
        {
            bar.Show(V.Call(connectedAt: AnsweredIso));
            // The same call again: the timer keeps running, once.
            bar.Show(V.Call(connectedAt: AnsweredIso));
        });
        time.Advance(TimeSpan.FromSeconds(3));
        Assert.Equal("00:03", bar.Status);

        bar.Show(V.Call(state: "Call ended", canHangUp: false));
        Assert.Equal("Call ended", bar.Status);
        time.Advance(TimeSpan.FromSeconds(3));
        Assert.Equal("Call ended", bar.Status);

        // Stopped twice is still stopped.
        bar.Show(null);
        bar.Show(null);

        WithNoContext(() => bar.Show(V.Call(connectedAt: "2026-10-08T12:00:06Z")));
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal("00:01", bar.Status);
        Assert.Equal(1, time.TimersCreated);
    }

    [Fact]
    public void AnInstantThatDoesNotParseIsNoInstant()
    {
        var time = new ManualTime(_answered);
        var bar = new CallBarViewModel(time);
        bar.Show(V.Call(state: "Connecting.", connectedAt: "soon"));
        Assert.Equal("Connecting.", bar.Status);
        Assert.Equal(0, time.TimersCreated);
    }

    [Fact]
    public void AClockBehindTheServicesReadsAsZero()
    {
        var bar = new CallBarViewModel(new ManualTime(_answered.AddSeconds(-3)));
        WithNoContext(() => bar.Show(V.Call(connectedAt: AnsweredIso)));
        Assert.Equal("00:00", bar.Status);
    }

    [Fact]
    public void FormatDurationIsMinutesAndSeconds()
    {
        Assert.Equal("00:00", CallBarViewModel.FormatDuration(TimeSpan.FromSeconds(-1)));
        Assert.Equal("01:05", CallBarViewModel.FormatDuration(TimeSpan.FromSeconds(65)));
        Assert.Equal("125:00", CallBarViewModel.FormatDuration(TimeSpan.FromMinutes(125)));
    }

    [Fact]
    public void AnEndedCall()
    {
        var bar = new CallBarViewModel(new ManualTime(_answered));
        bar.Show(V.Call(state: "02:10", canHangUp: false, ended: "Call ended", endedNote: "The call log is the record.", failure: V.Failure("Could not connect.")));

        Assert.Equal("Call ended", bar.Ended);
        Assert.True(bar.HasEnded);
        Assert.Equal("The call log is the record.", bar.EndedNote);
        Assert.True(bar.HasEndedNote);
        Assert.Equal("Could not connect.", bar.Failure);
        Assert.True(bar.HasFailure);
        Assert.True(bar.CanDismiss);
        Assert.False(bar.HangUpCommand.CanExecute(null));

        // How it ended, when that is already the status, is not said twice.
        bar.Show(V.Call(state: "Call ended", canHangUp: false, ended: "Call ended"));
        Assert.Equal(string.Empty, bar.Ended);
        Assert.False(bar.HasEnded);
    }

    [Fact]
    public void TheMicrophonesOwnBarHidesOtherNotices()
    {
        var bar = new CallBarViewModel(new ManualTime(_answered));
        bar.Show(V.Call(mediaNotice: "Reconnecting."));
        Assert.Equal("Reconnecting.", bar.MediaNotice);
        Assert.True(bar.HasMediaNotice);

        bar.Show(V.Call(mediaNotice: "No microphone.", microphoneDenied: true));
        Assert.True(bar.MicrophoneDenied);
        Assert.False(bar.HasMediaNotice);

        bar.Show(V.Call());
        Assert.Equal(string.Empty, bar.MediaNotice);
        Assert.False(bar.HasMediaNotice);
    }

    [Fact]
    public void MuteHangUpAndDismissSendTheirEvents()
    {
        var bar = new CallBarViewModel(new ManualTime(_answered));
        bar.Muted = true;
        bar.HangUpCommand.Execute(null);
        bar.DismissCommand.Execute(null);

        var (context, sink) = Pages.Context();
        bar.Attach(context);
        // The core's value, written back: not a change to send.
        bar.Show(V.Call(muted: false));
        bar.Muted = true;
        bar.Muted = false;
        bar.HangUpCommand.Execute(null);
        bar.DismissCommand.Execute(null);

        Assert.Equal(
            [new UiEvent.Microphone(false), new UiEvent.Microphone(true), new UiEvent.HangUp(), new UiEvent.DismissCall()],
            sink.Sent);
    }

    private static void WithNoContext(Action body)
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        try
        {
            body();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }
}
