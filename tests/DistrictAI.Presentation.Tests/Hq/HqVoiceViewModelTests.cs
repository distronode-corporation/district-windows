using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels.Hq;
using Xunit;

namespace DistrictAI.Presentation.Tests.Hq;

/// <summary>District HQ by voice: the controls, the statement, the cards, spoken lines and the dock.</summary>
public sealed class HqVoiceViewModelTests
{
    internal static HqVoiceView Voice(
        HqVoiceStage stage = HqVoiceStage.Off,
        string? line = null,
        bool problem = false,
        bool busy = false,
        bool canStop = false,
        bool showMicrophone = false,
        bool muted = true,
        bool canDismiss = false,
        FailureView? notice = null,
        HqVoiceCardView[]? cards = null) =>
        new(stage, line, problem, busy, canStop, "Stop voice", showMicrophone, muted, canDismiss, notice, cards ?? []);

    internal static HqVoiceCardView Card(
        string id = "p-1",
        string? note = "Nothing has changed yet. Say \"confirm\" or click Confirm to apply it, or cancel it.",
        FailureView? failure = null,
        bool busy = false,
        bool canCancel = true,
        bool canConfirm = true,
        bool suggested = true) =>
        new(id, "Confirm this change", "Update the greeting.", note, failure, busy, "Cancel", "Confirm", canCancel, canConfirm, suggested);

    private static HqVoiceDisclosureView Disclosure(string? regionNotice = null) =>
        new("Where your voice is processed", regionNotice, "Your voice is processed in the United States.", "Start voice", "Not now");

    [Fact]
    public void StartVoiceIsOfferedAndSent()
    {
        var model = new HqViewModel();
        var (context, sink) = Pages.Context(callsAvailable: true);
        model.Attach(context);
        model.Show(HqViewModelTests.View(showVoiceStart: true, canStartVoice: true));

        Assert.True(model.ShowVoiceStart);
        Assert.Equal("Start voice", model.VoiceStartLabel);
        Assert.False(model.HasDisclosure);
        Assert.False(model.AcceptVoiceCommand.CanExecute(null));
        Assert.True(model.StartVoiceCommand.CanExecute(null));
        model.StartVoiceCommand.Execute(null);
        Assert.Equal([new UiEvent.Hq(new HqAction.StartVoice())], sink.Sent);

        model.Show(HqViewModelTests.View(showVoiceStart: true, canStartVoice: false));
        Assert.False(model.StartVoiceCommand.CanExecute(null));
    }

    [Fact]
    public void TheStatementComesFirstAndAnswersBothWays()
    {
        var model = new HqViewModel();
        var (context, sink) = Pages.Context(callsAvailable: true);
        model.Attach(context);
        model.Show(HqViewModelTests.View(voice: Voice(HqVoiceStage.Disclosing), disclosure: Disclosure()));

        Assert.True(model.HasDisclosure);
        Assert.False(model.ShowVoiceStart);
        Assert.Equal("Where your voice is processed", model.DisclosureTitle);
        Assert.StartsWith("Your voice is processed", model.DisclosureBody, StringComparison.Ordinal);
        Assert.False(model.HasRegionNotice);
        Assert.Equal("Start voice", model.AcceptLabel);
        Assert.Equal("Not now", model.DeclineLabel);
        Assert.Equal(HqVoiceStage.Disclosing, model.Voice.Stage);
        model.DeclineVoiceCommand.Execute(null);
        model.AcceptVoiceCommand.Execute(null);
        Assert.Equal(
            [new UiEvent.Hq(new HqAction.DeclineVoiceDisclosure()), new UiEvent.Hq(new HqAction.AcceptVoiceDisclosure())],
            sink.Sent);

        model.Show(HqViewModelTests.View(disclosure: Disclosure("This workspace's data lives in Canada.")));
        Assert.True(model.HasRegionNotice);
        Assert.Equal("This workspace's data lives in Canada.", model.RegionNotice);

        model.Show(HqViewModelTests.View());
        Assert.False(model.HasDisclosure);
        Assert.Equal(string.Empty, model.DisclosureBody);
        Assert.Equal(string.Empty, model.RegionNotice);
    }

    [Fact]
    public void TheLineSaysNewsQuietlyAndProblemsAsErrors()
    {
        var model = new HqVoiceViewModel();
        model.Show(Voice(HqVoiceStage.Starting, "Connecting to District HQ.", busy: true, canStop: true));

        Assert.Equal(HqVoiceStage.Starting, model.Stage);
        Assert.True(model.ShowsNews);
        Assert.False(model.ShowsProblem);
        Assert.True(model.IsBusy);
        Assert.Equal("Stop voice", model.StopLabel);
        Assert.True(model.StopCommand.CanExecute(null));
        Assert.False(model.DismissCommand.CanExecute(null));

        model.Show(Voice(HqVoiceStage.Ended, "Voice session ended because District HQ left the session.", problem: true, canDismiss: true));
        Assert.True(model.ShowsProblem);
        Assert.False(model.ShowsNews);
        Assert.False(model.StopCommand.CanExecute(null));
        Assert.True(model.DismissCommand.CanExecute(null));

        model.Show(Voice());
        Assert.False(model.ShowsNews);
        Assert.False(model.ShowsProblem);
        Assert.Equal(string.Empty, model.Line);
    }

    [Fact]
    public void StopDismissAndTheNoticeAreSent()
    {
        var model = new HqVoiceViewModel();
        var (context, sink) = Pages.Context(callsAvailable: true);
        model.Attach(context);
        model.Show(Voice(
            HqVoiceStage.Live,
            "Listening. Speak to District HQ.",
            canStop: true,
            canDismiss: true,
            notice: V.Failure("This change is no longer available.")));

        Assert.True(model.HasNotice);
        Assert.Equal("This change is no longer available.", model.Notice);
        model.StopCommand.Execute(null);
        model.DismissCommand.Execute(null);
        model.DismissNoticeCommand.Execute(null);
        Assert.Equal(
            [
                new UiEvent.Hq(new HqAction.StopVoice()),
                new UiEvent.Hq(new HqAction.DismissVoiceNotice()),
                new UiEvent.Hq(new HqAction.DismissVoiceNotice()),
            ],
            sink.Sent);
    }

    [Fact]
    public void TheMicrophoneButtonAsksForTheOtherState()
    {
        var model = new HqVoiceViewModel();
        var (context, sink) = Pages.Context(callsAvailable: true);
        model.Attach(context);
        model.Show(Voice(HqVoiceStage.Live, showMicrophone: true, muted: false));

        Assert.True(model.ShowMicrophone);
        Assert.False(model.Muted);
        Assert.Empty(sink.Sent);

        model.Muted = true;
        Assert.Equal([new UiEvent.Microphone(false)], sink.Sent);

        // The core's answer writes it back without sending anything.
        model.Show(Voice(HqVoiceStage.Live, showMicrophone: true, muted: true));
        model.Show(Voice(HqVoiceStage.Live, showMicrophone: true, muted: false));
        Assert.Single(sink.Sent);

        model.Muted = true;
        model.Muted = false;
        Assert.Equal(new UiEvent.Microphone(true), sink.Sent[^1]);
    }

    [Fact]
    public void ACardConfirmsAndCancelsOnlyWhenItWorks()
    {
        var model = new HqVoiceViewModel();
        var (context, sink) = Pages.Context(callsAvailable: true);
        model.Attach(context);
        model.Show(Voice(HqVoiceStage.Live, cards: [Card(), Card("p-2", suggested: false)]));

        Assert.True(model.HasCards);
        Assert.Equal(2, model.Cards.Count);
        var card = model.Cards[0];
        Assert.Equal("p-1", card.ProposalId);
        Assert.Equal("Confirm this change", card.Title);
        Assert.Equal("Update the greeting.", card.Summary);
        Assert.True(card.HasNote);
        Assert.False(card.HasFailure);
        Assert.True(card.IsSuggested);
        Assert.False(model.Cards[1].IsSuggested);
        Assert.Equal("Confirm", card.ConfirmLabel);
        Assert.Equal("Cancel", card.CancelLabel);
        model.Confirm(card);
        model.Cancel(model.Cards[1]);
        Assert.Equal(
            [
                new UiEvent.Hq(new HqAction.ConfirmVoiceCard("p-1")),
                new UiEvent.Hq(new HqAction.CancelVoiceCard("p-2")),
            ],
            sink.Sent);

        // One click in flight: nothing is sent while it is.
        model.Show(Voice(HqVoiceStage.Live, cards: [Card(note: "Applying the change.", busy: true, canCancel: false, canConfirm: false)]));
        Assert.True(model.Cards[0].IsBusy);
        model.Confirm(model.Cards[0]);
        model.Cancel(model.Cards[0]);
        Assert.Equal(2, sink.Sent.Count);
    }

    [Fact]
    public void AFailedConfirmationKeepsItsCardAndSaysWhy()
    {
        var model = new HqVoiceViewModel();
        model.Show(Voice(HqVoiceStage.Live, cards: [Card(note: "The change may have been made.", failure: V.Failure("Offline.", "Affected regions: EU"))]));
        var first = model.Cards[0];

        Assert.True(first.HasFailure);
        Assert.Equal("Offline." + Environment.NewLine + "Affected regions: EU", first.Failure);
        Assert.Equal("The change may have been made.", first.Note);

        // The same card in the next snapshot is the same row.
        model.Show(Voice(HqVoiceStage.Live, cards: [Card(note: "The change may have been made.", failure: V.Failure("Offline.", "Affected regions: EU"))]));
        Assert.Same(first, model.Cards[0]);

        model.Show(Voice(HqVoiceStage.Live, cards: [Card(note: null, failure: V.Failure("Offline."))]));
        Assert.False(model.Cards[0].HasNote);

        model.Show(Voice());
        Assert.Empty(model.Cards);
        Assert.False(model.HasCards);
    }

    [Fact]
    public void WithNoCoreAttachedNothingIsSent()
    {
        var model = new HqVoiceViewModel();
        model.Show(Voice(HqVoiceStage.Live, canStop: true, showMicrophone: true, muted: false, cards: [Card()]));
        model.StopCommand.Execute(null);
        model.Confirm(model.Cards[0]);
        model.Muted = true;
        Assert.True(model.Muted);
    }

    [Fact]
    public void SpokenLinesJoinTheConversation()
    {
        var model = new HqViewModel();
        model.Show(HqViewModelTests.View(messages:
        [
            new HqMessageView.Spoken(0, "Change the greeting", Member: true, IsFinal: true),
            new HqMessageView.Spoken(1, "I've proposed it.", Member: false, IsFinal: false),
            new HqMessageView.Note(2, "Cancelled. Nothing was changed.", Applied: true),
        ]));

        var member = model.Messages[0];
        Assert.True(member.IsQuestion && member.IsSpoken);
        Assert.False(member.IsSpokenAnswer || member.IsAnswer || member.IsInterim);
        Assert.Equal(1.0, member.TextOpacity);
        Assert.Equal("You said: Change the greeting", member.AccessibleName);
        var assistant = model.Messages[1];
        Assert.True(assistant.IsSpokenAnswer && assistant.IsSpoken && assistant.IsInterim);
        Assert.False(assistant.IsQuestion || assistant.IsAnswer || assistant.ReportVisible);
        Assert.Equal(0.6, assistant.TextOpacity);
        Assert.Equal("District HQ said: I've proposed it.", assistant.AccessibleName);
        Assert.True(model.Messages[2].IsAppliedNote);

        // Words that grew are the line drawn again.
        model.Show(HqViewModelTests.View(messages:
        [
            new HqMessageView.Spoken(0, "Change the greeting", Member: true, IsFinal: true),
            new HqMessageView.Spoken(1, "I've proposed it. Say confirm.", Member: false, IsFinal: true),
        ]));
        Assert.Same(member, model.Messages[0]);
        Assert.NotSame(assistant, model.Messages[1]);
        Assert.False(model.Messages[1].IsInterim);
        Assert.Equal(2, model.Messages.Count);
    }

    [Fact]
    public void TheDockShowsOffDistrictHqAndGoesBack()
    {
        var model = new VoiceDockViewModel();
        var (context, sink) = Pages.Context(callsAvailable: true);
        model.Attach(context);
        model.Show(null);
        Assert.False(model.IsShown);
        Assert.False(model.ReviewCommand.CanExecute(null));

        model.Show(new HqVoiceDockView(
            "District HQ voice",
            "Review in District HQ",
            CanReview: true,
            Voice(HqVoiceStage.Live, "Listening. Speak to District HQ.", canStop: true, showMicrophone: true, muted: false, cards: [Card()])));
        Assert.True(model.IsShown);
        Assert.Equal("District HQ voice", model.Title);
        Assert.Equal("Review in District HQ", model.ReviewLabel);
        Assert.True(model.Voice.ShowsNews);
        Assert.Single(model.Voice.Cards);
        Assert.True(model.ReviewCommand.CanExecute(null));

        model.ReviewCommand.Execute(null);
        model.Voice.Muted = true;
        model.Voice.Confirm(model.Voice.Cards[0]);
        Assert.Equal(
            [
                new UiEvent.Hq(new HqAction.Open()),
                new UiEvent.Microphone(false),
                new UiEvent.Hq(new HqAction.ConfirmVoiceCard("p-1")),
            ],
            sink.Sent);

        model.Show(null);
        Assert.False(model.IsShown);
        Assert.False(model.CanReview);
    }

    [Fact]
    public void WithNoCoreAttachedTheDockSendsNothing()
    {
        var model = new VoiceDockViewModel();
        model.Show(new HqVoiceDockView("District HQ voice", "Review in District HQ", CanReview: true, Voice()));
        model.ReviewCommand.Execute(null);
        Assert.True(model.IsShown);
    }
}
