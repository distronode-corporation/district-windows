using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels.Calls;
using Xunit;

namespace DistrictAI.Presentation.Tests.Calls;

public sealed class DialerViewModelTests
{
    private static DialerView View(string number = "", string formatted = "", bool canDial = false, FailureView? failure = null, string? note = null) =>
        new(number, canDial, Dialing: false, failure, note, formatted, "Type a number.", "Placing a call turns on your microphone.");

    [Fact]
    public void ItCopiesTheDialler()
    {
        var dialer = new DialerViewModel();
        Assert.Equal("\u00A0", dialer.Formatted);

        dialer.Show(View("5550100", "555 0100", canDial: true, failure: V.Failure("Could not place it."), note: "Finish the call first."));
        Assert.Equal("5550100", dialer.Number);
        Assert.Equal("555 0100", dialer.Formatted);
        Assert.Equal("Type a number.", dialer.Hint);
        Assert.Equal("Placing a call turns on your microphone.", dialer.MicrophoneNote);
        Assert.True(dialer.CanDial);
        Assert.True(dialer.DialCommand.CanExecute(null));
        Assert.False(dialer.Dialing);
        Assert.Equal("Could not place it.", dialer.Failure);
        Assert.True(dialer.HasFailure);
        Assert.Equal("Finish the call first.", dialer.Note);
        Assert.True(dialer.HasNote);

        dialer.Show(View(note: string.Empty));
        Assert.Equal("\u00A0", dialer.Formatted);
        Assert.False(dialer.DialCommand.CanExecute(null));
        Assert.False(dialer.HasFailure);
        Assert.Equal(string.Empty, dialer.Note);
        Assert.False(dialer.HasNote);

        dialer.Show(View());
        Assert.Equal(string.Empty, dialer.Note);
        Assert.False(dialer.HasNote);
    }

    [Fact]
    public void TypingIsSentAndTheCoresEchoDoesNotRewriteTheBox()
    {
        var (context, sink) = Pages.Context();
        var dialer = new DialerViewModel();
        dialer.Attach(context);

        dialer.Number = "5";
        dialer.Number = "55";
        // The core's answer to the first edit, after the second was typed.
        dialer.Show(View("5"));
        Assert.Equal("55", dialer.Number);

        // A number the box did not send (a call back) is written, and not sent back.
        dialer.Show(View("+12125550100"));
        Assert.Equal("+12125550100", dialer.Number);

        Assert.Equal([new UiEvent.DialerEdit("5"), new UiEvent.DialerEdit("55")], sink.Sent);
    }

    [Fact]
    public void CallSendsTheDial()
    {
        var dialer = new DialerViewModel();
        dialer.Number = "5";
        dialer.DialCommand.Execute(null);

        var (context, sink) = Pages.Context();
        dialer.Attach(context);
        dialer.DialCommand.Execute(null);
        Assert.Equal([new UiEvent.Dial()], sink.Sent);
    }
}

public sealed class IncomingCallViewModelTests
{
    private static IncomingRingView Ring(string id = "call-1", string? detail = "From +1 212 555 0100", string? note = "Answering puts your microphone on this call.", bool live = true) =>
        new(id, "Incoming call", detail, note, ShowAnswer: live, CanAnswer: live, CanDecline: live, Answering: false, live, Sounding: live, WorkspaceName: null);

    [Fact]
    public void ARingShowsAndIsAnnouncedOnce()
    {
        var banner = new IncomingCallViewModel();
        Assert.True(banner.Show(Ring()));

        Assert.True(banner.IsShown);
        Assert.Equal("call-1", banner.CallId);
        Assert.Equal("Incoming call", banner.Heading);
        Assert.Equal("From +1 212 555 0100", banner.Detail);
        Assert.True(banner.HasDetail);
        Assert.Equal("Answering puts your microphone on this call.", banner.Note);
        Assert.True(banner.HasNote);
        Assert.True(banner.ShowAnswer);
        Assert.True(banner.CanAnswer);
        Assert.True(banner.AnswerCommand.CanExecute(null));
        Assert.True(banner.Live);
        Assert.True(banner.CanDecline);
        Assert.True(banner.DeclineCommand.CanExecute(null));
        Assert.False(banner.Answering);
        Assert.False(banner.CanDismiss);
        Assert.True(banner.Sounding);
        Assert.Equal("Incoming call. From +1 212 555 0100", banner.Announcement);

        // The same ring again is not a new one; another call is.
        Assert.False(banner.Show(Ring()));
        Assert.True(banner.Show(Ring("call-2")));
    }

    [Fact]
    public void AnEndedRingWithNothingUnderIt()
    {
        var banner = new IncomingCallViewModel();
        banner.Show(Ring(detail: null, note: null, live: false));
        Assert.Equal(string.Empty, banner.Detail);
        Assert.False(banner.HasDetail);
        Assert.Equal(string.Empty, banner.Note);
        Assert.False(banner.HasNote);
        Assert.True(banner.CanDismiss);
        Assert.Equal("Incoming call", banner.Announcement);
    }

    [Fact]
    public void NoRingHidesTheBannerAndTheNextRingIsNew()
    {
        var banner = new IncomingCallViewModel();
        banner.Show(Ring());
        Assert.False(banner.Show(null));
        Assert.False(banner.IsShown);
        Assert.Equal(string.Empty, banner.CallId);
        Assert.False(banner.AnswerCommand.CanExecute(null));
        Assert.False(banner.DeclineCommand.CanExecute(null));

        Assert.True(banner.Show(Ring()));
    }

    [Fact]
    public void AnswerAndDeclineNameTheCall()
    {
        var banner = new IncomingCallViewModel();
        banner.Show(Ring());
        banner.AnswerCommand.Execute(null);
        banner.DeclineCommand.Execute(null);
        banner.DismissCommand.Execute(null);

        var (context, sink) = Pages.Context();
        banner.Attach(context);
        banner.AnswerCommand.Execute(null);
        banner.DeclineCommand.Execute(null);
        banner.DismissCommand.Execute(null);

        Assert.Equal([new UiEvent.Answer("call-1"), new UiEvent.Decline("call-1"), new UiEvent.DismissRing()], sink.Sent);
    }
}

public sealed class TextEchoTests
{
    [Fact]
    public void ValuesTheBoxSentAreOnlyAcknowledged()
    {
        var echo = new TextEcho();
        echo.Typed("5");
        echo.Typed("55");
        Assert.False(echo.Write("5", "55"));
        Assert.False(echo.Write("55", "55"));
    }

    [Fact]
    public void TheSameCoreValueAgainWhileTypingIsNothingNew()
    {
        var echo = new TextEcho();
        echo.Typed("5");
        Assert.False(echo.Write("5", "5"));
        echo.Typed("55");
        Assert.False(echo.Write("5", "55"));
    }

    [Fact]
    public void AValueTheBoxDidNotSendIsWrittenUnlessTheBoxHoldsIt()
    {
        var echo = new TextEcho();
        Assert.True(echo.Write("+12125550100", string.Empty));
        Assert.False(echo.Write("+12125550100", "+12125550100"));

        // The number cleared after a call, with typing pending.
        echo.Typed("+12125550101");
        Assert.True(echo.Write(string.Empty, "+12125550101"));
    }
}
