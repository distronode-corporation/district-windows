using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels.Hq;
using Xunit;

namespace DistrictAI.Presentation.Tests.Hq;

public sealed class HqViewModelTests
{
    private static RichRun Run(string text, bool bold = false, bool italic = false, bool code = false, string? link = null) =>
        new(text, bold, italic, code, link);

    private static HqView View(
        HqMessageView[]? messages = null,
        EmptyView? empty = null,
        string? thinking = null,
        FailureView? failure = null,
        bool canRetry = false,
        HqCardView? card = null,
        bool canAsk = true) =>
        new(
            "District HQ",
            empty,
            messages ?? [],
            thinking,
            failure,
            canRetry,
            card,
            canAsk,
            "Ask District HQ",
            "Ask",
            "Try again",
            "Each question is answered by an AI model.");

    private static HqCardView Card(
        string? note = "Nothing has been changed yet.",
        string? applying = null,
        FailureView? failure = null,
        bool canConfirm = true,
        bool canDismiss = true) =>
        new("Confirm this change", "Update the greeting.", note, applying, failure, "Confirm", "Dismiss", canConfirm, canDismiss);

    private static HqMessageView.Answer Answer(uint index, ReportAvailability report = ReportAvailability.InApp, params RichBlock[] blocks) =>
        new(index, new RichTextView(blocks.Length > 0 ? blocks : [new RichBlock.Paragraph([Run("You had "), Run("19 calls", bold: true)])]), report);

    [Fact]
    public void AnEmptyConversationSaysSo()
    {
        var model = new HqViewModel();
        model.Show(View(empty: new EmptyView("Ask District HQ", "Ask about your calls.")), reportSending: false);

        Assert.True(model.IsEmpty);
        Assert.False(model.HasMessages);
        Assert.Equal("Ask District HQ", model.EmptyTitle);
        Assert.Equal("Ask about your calls.", model.EmptyBody);
        Assert.Equal("District HQ", model.Title);
        Assert.Equal("Ask District HQ", model.PromptPlaceholder);
        Assert.Equal("Ask", model.AskLabel);
        Assert.Equal("Try again", model.RetryLabel);
        Assert.StartsWith("Each question", model.Footnote, StringComparison.Ordinal);
        Assert.False(model.HasCard);
        Assert.False(model.IsThinking);
        Assert.False(model.HasFailure);
        Assert.NotNull(model.View);
    }

    [Fact]
    public void EachLineOfTheConversationIsShown()
    {
        var model = new HqViewModel();
        model.Show(
            View(messages:
            [
                new HqMessageView.Question(0, "How many calls?"),
                Answer(1),
                new HqMessageView.Note(2, "Done. The change is in place.", Applied: true),
                new HqMessageView.Note(3, "That change was not made.", Applied: false),
            ]),
            reportSending: false);

        Assert.True(model.HasMessages);
        Assert.False(model.IsEmpty);
        Assert.Equal(4, model.Messages.Count);
        var question = model.Messages[0];
        Assert.True(question.IsQuestion);
        Assert.Equal("You asked: How many calls?", question.AccessibleName);
        Assert.False(question.ReportVisible);
        var answer = model.Messages[1];
        Assert.True(answer.IsAnswer);
        Assert.Equal("You had 19 calls", answer.Text);
        Assert.Equal("District HQ answered: You had 19 calls", answer.AccessibleName);
        Assert.Equal("Report", answer.ReportLabel);
        Assert.True(answer.ReportVisible);
        Assert.True(answer.ReportEnabled);
        Assert.True(model.Messages[2].IsAppliedNote);
        Assert.False(model.Messages[2].IsWarningNote);
        Assert.True(model.Messages[3].IsWarningNote);
        Assert.Equal("That change was not made.", model.Messages[3].AccessibleName);
    }

    [Fact]
    public void AViewerReportsOnTheWebAndNoReportWhileOneIsSending()
    {
        var model = new HqViewModel();
        model.Show(View(messages: [Answer(0, ReportAvailability.OnWeb)]), reportSending: true);

        Assert.Equal("Report on the web", model.Messages[0].ReportLabel);
        Assert.False(model.Messages[0].ReportEnabled);
        Assert.IsType<ReportTarget.HqAnswer>(HqViewModel.AnswerTarget);
    }

    [Fact]
    public void ALineDrawnOnceIsKeptAcrossSnapshots()
    {
        var model = new HqViewModel();
        model.Show(View(messages: [new HqMessageView.Question(0, "Hi"), Answer(1)]), reportSending: false);
        var first = model.Messages[1];

        model.Show(View(messages: [new HqMessageView.Question(0, "Hi"), Answer(1), new HqMessageView.Question(2, "More")]), reportSending: false);

        Assert.Same(first, model.Messages[1]);
        Assert.Equal(3, model.Messages.Count);

        // A report starting changes the button, so the line is drawn again.
        model.Show(View(messages: [new HqMessageView.Question(0, "Hi"), Answer(1)]), reportSending: true);
        Assert.NotSame(first, model.Messages[1]);
        Assert.Equal(2, model.Messages.Count);
        Assert.NotEqual(first.GetHashCode(), model.Messages[1].GetHashCode());
        Assert.False(first.Equals(null));
    }

    [Fact]
    public void AskSendsTheTrimmedPromptAndEmptiesTheBox()
    {
        var model = new HqViewModel();
        var (context, sink) = Pages.Context();
        model.Attach(context);
        model.Show(View());

        Assert.False(model.AskCommand.CanExecute(null));
        model.Draft = "   ";
        Assert.False(model.AskCommand.CanExecute(null));
        model.AskCommand.Execute(null);
        Assert.Empty(sink.Sent);

        model.Draft = "  How many calls?\n ";
        Assert.True(model.AskCommand.CanExecute(null));
        model.AskCommand.Execute(null);

        Assert.Equal([new UiEvent.Hq(new HqAction.Ask("How many calls?"))], sink.Sent);
        Assert.Equal(string.Empty, model.Draft);
    }

    [Fact]
    public void NothingIsAskedWhileAnAnswerIsOnItsWay()
    {
        var model = new HqViewModel();
        var (context, sink) = Pages.Context();
        model.Attach(context);
        model.Show(View(thinking: "Thinking.", canAsk: false));
        model.Draft = "Again?";

        Assert.True(model.IsThinking);
        Assert.Equal("Thinking.", model.ThinkingText);
        Assert.False(model.AskCommand.CanExecute(null));
        model.AskCommand.Execute(null);
        Assert.Empty(sink.Sent);
        Assert.Equal("Again?", model.Draft);
    }

    [Fact]
    public void WithNoCoreAttachedTheDraftIsKept()
    {
        var model = new HqViewModel();
        model.Show(View());
        model.Draft = "Hello";
        model.AskCommand.Execute(null);
        Assert.Equal("Hello", model.Draft);
    }

    [Fact]
    public void AFailureOffersTryAgainWhenItCouldHelp()
    {
        var model = new HqViewModel();
        var (context, sink) = Pages.Context();
        model.Attach(context);
        model.Show(View(failure: V.Failure("Offline.", "Affected regions: EU", retryable: true), canRetry: true));

        Assert.True(model.HasFailure);
        Assert.Equal("Offline." + Environment.NewLine + "Affected regions: EU", model.FailureText);
        Assert.True(model.RetryCommand.CanExecute(null));
        model.RetryCommand.Execute(null);
        Assert.Equal([new UiEvent.Hq(new HqAction.Retry())], sink.Sent);

        model.Show(View(failure: V.Failure("Refused."), canRetry: false));
        Assert.False(model.CanRetry);
        Assert.False(model.RetryCommand.CanExecute(null));
    }

    [Fact]
    public void TheCardConfirmsAndDismisses()
    {
        var model = new HqViewModel();
        var (context, sink) = Pages.Context();
        model.Attach(context);
        model.Show(View(card: Card()));

        Assert.True(model.HasCard);
        Assert.Equal("Confirm this change", model.CardTitle);
        Assert.Equal("Update the greeting.", model.CardSummary);
        Assert.True(model.HasCardNote);
        Assert.Equal("Confirm", model.ConfirmLabel);
        Assert.Equal("Dismiss", model.DismissLabel);
        Assert.False(model.IsApplying);
        Assert.False(model.HasCardFailure);
        model.ConfirmCommand.Execute(null);
        model.DismissCommand.Execute(null);
        Assert.Equal(
            [new UiEvent.Hq(new HqAction.Confirm()), new UiEvent.Hq(new HqAction.Dismiss())],
            sink.Sent);
    }

    [Fact]
    public void TheCardShowsApplyingAndAFailedConfirmation()
    {
        var model = new HqViewModel();
        model.Show(View(card: Card(note: null, applying: "Applying the change.", canConfirm: false, canDismiss: false)), reportSending: false);

        Assert.True(model.IsApplying);
        Assert.Equal("Applying the change.", model.ApplyingText);
        Assert.False(model.HasCardNote);
        Assert.False(model.ConfirmCommand.CanExecute(null));
        Assert.False(model.DismissCommand.CanExecute(null));

        model.Show(View(card: Card(note: "Check before confirming again.", failure: V.Failure("Offline.", retryable: true))), reportSending: false);
        Assert.True(model.HasCardFailure);
        Assert.Equal("Offline.", model.CardFailure);
        Assert.True(model.ConfirmCommand.CanExecute(null));

        model.Show(View(), reportSending: false);
        Assert.False(model.HasCard);
        Assert.Equal(string.Empty, model.CardSummary);
    }

    [Fact]
    public void ALinkGoesBackThroughTheCore()
    {
        var model = new HqViewModel();
        var (context, sink) = Pages.Context();
        model.Attach(context);
        model.OpenLink("https://example.com");
        Assert.Equal([new UiEvent.Hq(new HqAction.OpenLink("https://example.com"))], sink.Sent);
    }

    [Fact]
    public void RunsBecomeParagraphsOfInlines()
    {
        var paragraphs = RichText.Layout(new RichTextView(
        [
            new RichBlock.Heading([Run("This week")]),
            new RichBlock.Paragraph([Run("One\nTwo "), Run("bold", bold: true), Run("it", italic: true), Run("x", code: true), Run("site", link: "https://example.com")]),
            new RichBlock.ListItem("\u2022", 0, [Run("first")]),
            new RichBlock.ListItem("2.", 7, [Run("deep")]),
            new RichBlock.Code("a\n\nb"),
        ]));

        Assert.Equal(
            [RichParagraphKind.Heading, RichParagraphKind.Body, RichParagraphKind.ListItem, RichParagraphKind.ListItem, RichParagraphKind.Code],
            paragraphs.Select(p => p.Kind));
        Assert.Equal([false, true, true, false, true], paragraphs.Select(p => p.SpaceBefore));
        Assert.Equal([0, 0, 0, 3, 0], paragraphs.Select(p => p.Indent));
        Assert.Equal(
            [
                new RichInline("One", false, false, false, null, false),
                RichInline.LineBreak,
                new RichInline("Two ", false, false, false, null, false),
                new RichInline("bold", true, false, false, null, false),
                new RichInline("it", false, true, false, null, false),
                new RichInline("x", false, false, true, null, false),
                new RichInline("site", false, false, false, "https://example.com", false),
            ],
            paragraphs[1].Inlines);
        Assert.Equal(new RichInline("\u2022 ", false, false, false, null, false), paragraphs[2].Inlines[0]);
        Assert.Equal(
            [
                new RichInline("a", false, false, true, null, false),
                RichInline.LineBreak,
                RichInline.LineBreak,
                new RichInline("b", false, false, true, null, false),
            ],
            paragraphs[4].Inlines);
        Assert.Equal("This week\nOne\nTwo bolditxsite\n\u2022 first\n2. deep\na\n\nb", RichText.Plain(paragraphs));
    }

    [Fact]
    public void MarkupInTheRunsIsNeverRead()
    {
        var paragraphs = RichText.Layout(new RichTextView([new RichBlock.Paragraph([Run("<b>not bold</b> **nor this**")])]));
        Assert.Equal([new RichInline("<b>not bold</b> **nor this**", false, false, false, null, false)], paragraphs[0].Inlines);
        Assert.Empty(RichText.Layout(new RichTextView([])));
    }

    [Fact]
    public void SendForwardsAnyAction()
    {
        var model = new HqViewModel();
        model.Send(new HqAction.Open());
        var (context, sink) = Pages.Context();
        model.Attach(context);
        model.Send(new HqAction.Open());
        Assert.Equal([new UiEvent.Hq(new HqAction.Open())], sink.Sent);
    }
}
