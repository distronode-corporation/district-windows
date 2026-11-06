using DistrictAI.Core.Ffi;
using DistrictAI.L10n;
using DistrictAI.ViewModels;
using DistrictAI.ViewModels.Calls;
using Xunit;

namespace DistrictAI.Presentation.Tests.Calls;

public sealed class SuperviseViewModelTests
{
    private static WhisperView Whisper(
        string draft = "",
        bool canEdit = true,
        bool canSend = false,
        bool sending = false,
        FailureView? failure = null,
        string? outcome = null,
        string? outcomeWords = null) =>
        new(
            Title: "Note to your receptionist",
            Draft: draft,
            Placeholder: "For example: Offer a 10% discount to book today.",
            SendLabel: "Send note",
            CanEdit: canEdit,
            CanSend: canSend,
            Sending: sending,
            Failure: failure,
            Outcome: outcome,
            OutcomeWords: outcomeWords);

    private static SuperviseView View(
        SuperviseState state = SuperviseState.Idle,
        string? status = null,
        bool canListen = true,
        bool canStopListening = false,
        bool canSupervise = true,
        bool canTakeOver = true,
        bool canHandBack = false,
        bool confirming = false,
        string? onCallBody = null,
        FailureView? failure = null,
        WhisperView? whisper = null,
        SuperviseLineView[]? lines = null,
        string? linesWaiting = null,
        MicrophoneView microphone = MicrophoneView.None,
        string? microphoneLine = null,
        bool canMute = false,
        string? mediaNotice = null) =>
        new(
            State: state,
            Status: status,
            ListenTitle: "Listen in",
            ListenBody: "Hear the call as it happens. The caller does not hear you.",
            ListenLabel: "Listen",
            CanListen: canListen,
            StopListeningLabel: "Stop listening",
            CanStopListening: canStopListening,
            CanSupervise: canSupervise,
            TakeOverLabel: "Take over the call",
            CanTakeOver: canTakeOver,
            HandBackLabel: "Hand back to your receptionist",
            CanHandBack: canHandBack,
            Confirm: confirming ? new TakeOverConfirmView("Take over this call?", "Your microphone is live.", "Take over", "Cancel") : null,
            OnCallBody: onCallBody,
            Failure: failure,
            Whisper: whisper ?? Whisper(),
            LinesHeading: "Live transcript",
            Lines: lines ?? [],
            LinesWaiting: linesWaiting,
            Microphone: microphone,
            MicrophoneLine: microphoneLine,
            CanMute: canMute,
            MediaNotice: mediaNotice);

    /// <summary>On the call, with the microphone the engine reports.</summary>
    private static SuperviseView OnCall(MicrophoneView microphone = MicrophoneView.On, bool canMute = true) =>
        View(
            state: SuperviseState.OnCall,
            status: "You are on the call",
            canListen: false,
            canTakeOver: false,
            canHandBack: true,
            onCallBody: "Your receptionist has stepped back.",
            whisper: Whisper(canEdit: false),
            microphone: microphone,
            microphoneLine: microphone switch
            {
                MicrophoneView.On => "Your microphone is on. The caller hears you.",
                MicrophoneView.Off => "Your microphone is off. The caller does not hear you.",
                _ => null,
            },
            canMute: canMute);

    private static (SuperviseViewModel Card, RecordingSink Sink, List<string> Said) Card()
    {
        var (context, sink) = Pages.Context(callsAvailable: true);
        var card = new SuperviseViewModel();
        card.Attach(context);
        var said = new List<string>();
        card.Announced += (_, line) => said.Add(line);
        return (card, sink, said);
    }

    private static UiEvent Action(SuperviseAction action) => new UiEvent.Supervise(action);

    [Fact]
    public void NoCardHidesIt()
    {
        var (card, _, _) = Card();
        card.Show(View(lines: [new SuperviseLineView("Caller", "Hello", true)]));
        Assert.True(card.IsShown);
        Assert.True(card.HasLines);

        card.Show(null);
        Assert.False(card.IsShown);
        Assert.Empty(card.Lines);
        Assert.False(card.HasLines);
    }

    [Fact]
    public void IdleOffersListenAndTakeOverAndSaysNothing()
    {
        var (card, sink, _) = Card();
        card.Show(View());

        Assert.Equal(SuperviseState.Idle, card.State);
        Assert.False(card.HasStatus);
        Assert.False(card.IsWorking);
        Assert.Equal("Listen in", card.ListenTitle);
        Assert.Equal("Listen", card.ListenLabel);
        Assert.True(card.ListenCommand.CanExecute(null));
        Assert.False(card.StopListeningCommand.CanExecute(null));
        Assert.True(card.TakeOverCommand.CanExecute(null));
        Assert.False(card.HandBackCommand.CanExecute(null));
        Assert.False(card.IsConfirming);
        Assert.False(card.ConfirmTakeOverCommand.CanExecute(null));
        Assert.False(card.ShowsMute);
        Assert.False(card.ToggleMuteCommand.CanExecute(null));
        Assert.False(card.HasMicrophoneLine);
        Assert.False(card.CanDismiss);

        card.ListenCommand.Execute(null);
        card.TakeOverCommand.Execute(null);
        Assert.Equal([Action(new SuperviseAction.Listen()), Action(new SuperviseAction.TakeOver())], sink.Sent);
    }

    [Fact]
    public void ConnectingSaysSoAndOffersOnlyStop()
    {
        var (card, sink, _) = Card();
        card.Show(View(state: SuperviseState.Connecting, status: "Connecting you to the call.", canListen: false, canStopListening: true));

        Assert.True(card.IsWorking);
        Assert.Equal("Connecting you to the call.", card.Status);
        Assert.False(card.ListenCommand.CanExecute(null));
        card.StopListeningCommand.Execute(null);
        Assert.Equal([Action(new SuperviseAction.StopListening())], sink.Sent);
    }

    [Fact]
    public void TheConfirmationAsksBeforeAnythingIsRequested()
    {
        var (card, sink, _) = Card();
        card.Show(View(state: SuperviseState.Listening, status: "You are listening", confirming: true));

        Assert.True(card.IsConfirming);
        Assert.Equal("Take over this call?", card.ConfirmTitle);
        Assert.Equal("Your microphone is live.", card.ConfirmBody);
        Assert.Equal("Take over", card.ConfirmLabel);
        Assert.Equal("Cancel", card.CancelLabel);
        card.CancelTakeOverCommand.Execute(null);
        card.ConfirmTakeOverCommand.Execute(null);
        Assert.Equal([Action(new SuperviseAction.CancelTakeOver()), Action(new SuperviseAction.ConfirmTakeOver())], sink.Sent);

        card.Show(View(state: SuperviseState.TakingOver, status: "Taking over the call.", canTakeOver: false));
        Assert.False(card.IsConfirming);
        Assert.Equal(string.Empty, card.ConfirmTitle);
        Assert.True(card.IsWorking);
    }

    [Fact]
    public void OnTheCallTheMuteButtonFollowsOnlyTheEngine()
    {
        var (card, sink, _) = Card();
        card.Show(OnCall(MicrophoneView.On));

        Assert.True(card.ShowsMute);
        Assert.True(card.CanMute);
        Assert.Equal(S.CallBarMute, card.MuteLabel);
        Assert.Equal("Your microphone is on. The caller hears you.", card.MicrophoneLine);
        Assert.True(card.HasOnCallBody);
        card.ToggleMuteCommand.Execute(null);
        Assert.Equal([new UiEvent.Microphone(false)], sink.Sent);
        // Asked for, not yet reported: the button still says Mute.
        Assert.Equal(S.CallBarMute, card.MuteLabel);

        card.Show(OnCall(MicrophoneView.Off));
        Assert.Equal(S.RoomsUnmute, card.MuteLabel);
        Assert.Equal("Your microphone is off. The caller does not hear you.", card.MicrophoneLine);
        card.ToggleMuteCommand.Execute(null);
        Assert.Equal(new UiEvent.Microphone(true), sink.Sent[^1]);

        card.HandBackCommand.Execute(null);
        Assert.Equal(Action(new SuperviseAction.HandBack()), sink.Sent[^1]);
    }

    [Fact]
    public void ABlockedMicrophoneSaysSoAndCannotBeMuted()
    {
        var (card, _, _) = Card();
        card.Show(OnCall(MicrophoneView.Blocked, canMute: false));

        Assert.True(card.MicrophoneBlocked);
        Assert.False(card.HasMicrophoneLine);
        Assert.False(card.ToggleMuteCommand.CanExecute(null));
        Assert.Equal(S.RoomsUnmute, card.MuteLabel);
    }

    [Fact]
    public void ReconnectingKeepsTheMuteButtonOnlyWithAMicrophone()
    {
        var (card, _, _) = Card();
        card.Show(View(state: SuperviseState.Reconnecting, status: "Reconnecting. The call is still here.", microphone: MicrophoneView.On));
        Assert.True(card.ShowsMute);
        Assert.False(card.CanMute);

        card.Show(View(state: SuperviseState.Reconnecting, status: "Reconnecting. The call is still here."));
        Assert.False(card.ShowsMute);
    }

    [Fact]
    public void ListeningShowsTheLinesOrWhatIsAwaited()
    {
        var (card, _, _) = Card();
        card.Show(View(state: SuperviseState.Listening, status: "You are listening", linesWaiting: "What is said shows up here while you listen."));
        Assert.True(card.HasLinesWaiting);
        Assert.False(card.HasLines);
        Assert.Equal("Live transcript", card.LinesHeading);

        card.Show(View(state: SuperviseState.Listening, lines: [new SuperviseLineView("Caller", "Do you have Thursday?", true), new SuperviseLineView("Ava", "Let me check.", false)]));
        Assert.False(card.HasLinesWaiting);
        Assert.Equal([new SuperviseLineItem("Caller", "Do you have Thursday?", true), new SuperviseLineItem("Ava", "Let me check.", false)], card.Lines);
        Assert.Equal("Caller: Do you have Thursday?", card.Lines[0].ToString());
    }

    [Fact]
    public void AFailureOrAnEndCanBeDismissed()
    {
        var (card, sink, _) = Card();
        card.Show(View(failure: V.Failure("Could not connect you to the call. Try again.", retryable: true)));
        Assert.True(card.HasFailure);
        Assert.Equal("Could not connect you to the call. Try again.", card.Failure);
        Assert.True(card.DismissCommand.CanExecute(null));
        card.DismissCommand.Execute(null);
        Assert.Equal([Action(new SuperviseAction.Dismiss())], sink.Sent);

        card.Show(View(whisper: Whisper(failure: V.Failure("The note was not delivered. Try again."))));
        Assert.True(card.CanDismiss);
        Assert.True(card.HasWhisperFailure);

        card.Show(View(state: SuperviseState.Ended, status: "The call has ended.", canListen: false, canTakeOver: false));
        Assert.True(card.CanDismiss);

        card.Show(View());
        Assert.False(card.CanDismiss);
    }

    [Fact]
    public void TheNoteSendsEachEditAndKeepsWhatIsTyped()
    {
        var (card, sink, _) = Card();
        card.Show(View());

        card.WhisperDraft = "Offer";
        card.WhisperDraft = "Offer Thursday";
        Assert.Equal([Action(new SuperviseAction.EditWhisper("Offer")), Action(new SuperviseAction.EditWhisper("Offer Thursday"))], sink.Sent);

        // The core's answer to the first edit lands after the second: the box keeps what was typed.
        card.Show(View(whisper: Whisper(draft: "Offer", canSend: true)));
        Assert.Equal("Offer Thursday", card.WhisperDraft);
        card.Show(View(whisper: Whisper(draft: "Offer Thursday", canSend: true)));
        Assert.Equal("Offer Thursday", card.WhisperDraft);
        Assert.True(card.SendWhisperCommand.CanExecute(null));
        card.SendWhisperCommand.Execute(null);
        Assert.Equal(Action(new SuperviseAction.SendWhisper()), sink.Sent[^1]);

        // Sending: one at a time.
        card.Show(View(whisper: Whisper(draft: "Offer Thursday", sending: true)));
        Assert.True(card.WhisperSending);
        Assert.False(card.SendWhisperCommand.CanExecute(null));
        Assert.True(card.CanEditWhisper);

        // Delivered: the core cleared the draft, which the box did not send.
        var sent = sink.Sent.Count;
        card.Show(View(whisper: Whisper(draft: string.Empty, outcome: "Your receptionist used your note.", outcomeWords: "Good afternoon.")));
        Assert.Equal(string.Empty, card.WhisperDraft);
        Assert.Equal(sent, sink.Sent.Count);
        Assert.Equal("Your receptionist used your note.", card.WhisperOutcome);
        Assert.Equal("Good afternoon.", card.WhisperOutcomeWords);
        Assert.True(card.HasWhisperOutcome);
        Assert.True(card.HasWhisperOutcomeWords);
    }

    [Fact]
    public void AViewerSeesNoNoteBox()
    {
        var (card, _, _) = Card();
        card.Show(View(canSupervise: false, canTakeOver: false, whisper: Whisper(canEdit: false)));
        Assert.False(card.CanSupervise);
        Assert.False(card.CanEditWhisper);
        Assert.True(card.ListenCommand.CanExecute(null));
    }

    [Fact]
    public void NarratorHearsTheMicrophoneGoLiveTheEndAndANotesAnswer()
    {
        var (card, _, said) = Card();
        // Nothing is said for the card as first drawn, even on the call.
        card.Show(OnCall(MicrophoneView.On));
        Assert.Empty(said);

        card.Show(OnCall(MicrophoneView.Off));
        card.Show(OnCall(MicrophoneView.On));
        card.Show(OnCall(MicrophoneView.On));
        Assert.Equal(["Your microphone is on. The caller hears you."], said);

        card.Show(View(whisper: Whisper(outcome: "Your receptionist used your note.", outcomeWords: "Good afternoon.")));
        card.Show(View(whisper: Whisper(outcome: "Your receptionist used your note.", outcomeWords: "Good afternoon.")));
        Assert.Equal("Your receptionist used your note. Good afternoon.", said[^1]);
        Assert.Equal(2, said.Count);

        card.Show(View(state: SuperviseState.Ended, status: "The call has ended."));
        card.Show(View(state: SuperviseState.Ended, status: "The call has ended."));
        Assert.Equal("The call has ended.", said[^1]);
        Assert.Equal(3, said.Count);

        // Hidden, then shown again: drawn afresh, nothing said.
        card.Show(null);
        card.Show(OnCall(MicrophoneView.On));
        Assert.Equal(3, said.Count);
    }

    [Fact]
    public void TheCallPageShowsItsCard()
    {
        var (context, _) = Pages.Context(callsAvailable: true);
        var detail = new CallDetailViewModel();
        detail.Attach(context);
        detail.Show(
            new CallDetailView("CA1", V.Ready, "Alex", null, null, [], null, [], [], new TranscriptState.Loading(), null, ReportAvailability.Hidden, null, View()),
            reportSending: false);
        Assert.True(detail.Supervise.IsShown);

        detail.Show(
            new CallDetailView("CA1", V.Ready, "Alex", null, null, [], null, [], [], new TranscriptState.Loading(), null, ReportAvailability.Hidden, null, null),
            reportSending: false);
        Assert.False(detail.Supervise.IsShown);
    }

    [Fact]
    public void TheCallsPageListsTheLiveCallsOverTheLog()
    {
        var (context, sink) = Pages.Context(callsAvailable: true);
        var calls = new CallsViewModel();
        calls.Attach(context);
        calls.Show(new CallsView(V.Ready, [V.CallRow()], null, V.Paging(), new LiveCallsView("Live calls", "Calls happening right now.", [V.CallRow(id: "CA1", title: "Sam")])));

        Assert.True(calls.HasLive);
        Assert.Equal("Live calls", calls.LiveTitle);
        Assert.Equal("Calls happening right now.", calls.LiveBody);
        var live = Assert.Single(calls.Live);
        Assert.Equal("CA1", live.CallId);
        calls.OpenCall(live);
        Assert.Equal([new UiEvent.OpenCall("CA1")], sink.Sent);

        calls.Show(new CallsView(V.Ready, [V.CallRow()], null, V.Paging(), null));
        Assert.False(calls.HasLive);
        Assert.Empty(calls.Live);
        Assert.Equal(string.Empty, calls.LiveTitle);
    }
}
