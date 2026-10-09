using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels.Support;
using Xunit;

namespace DistrictAI.Presentation.Tests.Support;

public sealed class SupportViewModelTests
{
    private static readonly SupportKindView[] _kinds =
    [
        new(SupportKind.Problem, "Something is broken"),
        new(SupportKind.Question, "A question"),
        new(SupportKind.Suggestion, "A suggestion"),
    ];

    private static SupportRowView Row(string key, string reference, string status) =>
        new(key, "Subject " + key, reference, status, string.Empty);

    private static SupportComposeView Compose(
        string subject = "",
        string message = "",
        SupportKind kind = SupportKind.Problem,
        bool canSubmit = false,
        bool submitting = false,
        FailureView? failure = null) =>
        new(
            "New support request",
            _kinds,
            kind,
            subject,
            message,
            200,
            10000,
            canSubmit ? null : "A subject of 3 to 200 characters, and a message.",
            canSubmit,
            submitting,
            failure);

    private static SupportView View(
        LoadStatus? status = null,
        SupportRowView[]? open = null,
        SupportRowView[]? resolved = null,
        EmptyView? empty = null,
        string? submitted = null,
        string? capped = null,
        SupportComposeView? compose = null) =>
        new(
            "Support",
            status ?? new LoadStatus.Ready(),
            empty,
            open ?? [],
            resolved ?? [],
            capped,
            false,
            null,
            submitted,
            compose is null,
            compose);

    private static (SupportViewModel Model, RecordingSink Sink) Attached()
    {
        var model = new SupportViewModel();
        var (context, sink) = Pages.Context();
        model.Attach(context);
        return (model, sink);
    }

    [Fact]
    public void TheListIsSplitIntoOpenAndResolved()
    {
        var (model, sink) = Attached();
        model.Show(View(
            open: [Row("DA-42", "DA-42", "In Progress"), Row("support_pending", "Not filed yet", "Opening")],
            resolved: [Row("DA-40", "DA-40", "Done")],
            submitted: "Request DA-43 is open with our team.",
            capped: "Showing the 100 most recent requests."));

        Assert.True(model.Load.Ready);
        Assert.Equal(2, model.Open.Count);
        Assert.Single(model.Resolved);
        Assert.True(model.HasOpen);
        Assert.True(model.HasResolved);
        Assert.Equal("Not filed yet · Opening", model.Open[1].Line);
        Assert.Equal("Subject DA-42, DA-42 · In Progress", model.Open[0].AccessibleName);
        Assert.Equal(model.Open[0].AccessibleName, model.Open[0].ToString());
        Assert.True(model.HasSubmitted);
        Assert.True(model.HasCappedNote);
        Assert.True(model.CanStart);
        Assert.False(model.Composing);

        model.OpenRequest(model.Open[1]);
        model.DismissSubmittedCommand.Execute(null);
        model.StartRequestCommand.Execute(null);
        Assert.Equal(
            [
                new UiEvent.Support(new SupportAction.OpenRequest("support_pending")),
                new UiEvent.Support(new SupportAction.DismissSubmitted()),
                new UiEvent.Support(new SupportAction.StartRequest()),
            ],
            sink.Sent);
    }

    [Fact]
    public void AnEmptyOrFailedListSaysSo()
    {
        var (model, _) = Attached();
        model.Show(View(empty: new EmptyView("No requests", "Requests your team raises appear here.")));
        Assert.True(model.Load.ShowEmpty);
        Assert.False(model.HasOpen);

        model.Show(View(status: new LoadStatus.Failed(new FailureView("Offline.", null, true), "Could not load your support requests")));
        Assert.True(model.Load.Failed);
        Assert.True(model.Load.CanRetry);
        Assert.Equal("Could not load your support requests", model.Load.FailureTitle);
    }

    [Fact]
    public void TypingSendsTheWholeFormAndTheCoresEchoLeavesTheBoxAlone()
    {
        var (model, sink) = Attached();
        model.Show(View(compose: Compose()));
        Assert.True(model.Composing);
        Assert.False(model.CanStart);
        Assert.Equal(["Something is broken", "A question", "A suggestion"], model.KindLabels);
        Assert.Equal(0, model.KindIndex);
        Assert.Equal(200, model.SubjectMax);
        Assert.Equal(10000, model.MessageMax);
        Assert.True(model.HasNeeds);
        Assert.False(model.SubmitRequestCommand.CanExecute(null));
        Assert.Empty(sink.Sent);

        model.KindIndex = 1;
        model.Subject = "Hours";
        model.Message = "When";
        Assert.Equal(
            [
                new UiEvent.Support(new SupportAction.EditRequest(SupportKind.Question, string.Empty, string.Empty)),
                new UiEvent.Support(new SupportAction.EditRequest(SupportKind.Question, "Hours", string.Empty)),
                new UiEvent.Support(new SupportAction.EditRequest(SupportKind.Question, "Hours", "When")),
            ],
            sink.Sent);

        // The core gives back an older value while the person has typed on: the box keeps theirs.
        model.Show(View(compose: Compose("Hours", string.Empty, SupportKind.Question)));
        Assert.Equal("When", model.Message);
        model.Show(View(compose: Compose("Hours", "When", SupportKind.Question, canSubmit: true)));
        Assert.Equal(3, sink.Sent.Count);
        Assert.False(model.HasNeeds);
        Assert.True(model.SubmitRequestCommand.CanExecute(null));
        model.SubmitRequestCommand.Execute(null);
        Assert.Equal(new UiEvent.Support(new SupportAction.SubmitRequest()), sink.Sent[^1]);
    }

    [Fact]
    public void WhileSendingTheFormIsLockedAndAFailureIsShown()
    {
        var (model, sink) = Attached();
        model.Show(View(compose: Compose("Hours", "When", canSubmit: true, submitting: true)));
        Assert.True(model.Submitting);
        Assert.False(model.CanEdit);
        Assert.False(model.SubmitRequestCommand.CanExecute(null));
        Assert.False(model.CancelRequestCommand.CanExecute(null));

        model.Show(View(compose: Compose("Hours", "When", canSubmit: true, failure: new FailureView("The service is busy.", null, true))));
        Assert.True(model.HasComposeFailure);
        Assert.Equal("The service is busy.", model.ComposeFailure);
        Assert.True(model.CanEdit);
        model.CancelRequestCommand.Execute(null);
        Assert.Equal([new UiEvent.Support(new SupportAction.CancelRequest())], sink.Sent);
    }

    [Fact]
    public void AFormTheCoreFilledOrClosedIsWrittenIntoTheBoxes()
    {
        var (model, sink) = Attached();
        model.Show(View(compose: Compose("Report: AI-generated content", "Body", SupportKind.Suggestion)));
        Assert.Equal("Report: AI-generated content", model.Subject);
        Assert.Equal("Body", model.Message);
        Assert.Equal(2, model.KindIndex);

        model.Show(View());
        Assert.False(model.Composing);
        Assert.Equal(string.Empty, model.Subject);
        Assert.Equal(-1, model.KindIndex);
        Assert.Empty(model.KindLabels);
        Assert.Empty(sink.Sent);
    }

    [Fact]
    public void NothingIsSentBeforeTheFormIsOpenOrBeforeAttaching()
    {
        var model = new SupportViewModel();
        model.Subject = "typed";
        model.Send(new SupportAction.Open());
        model.Show(View(compose: Compose()));
        model.KindIndex = 7;

        var (context, sink) = Pages.Context();
        model.Attach(context);
        model.KindIndex = -1;
        Assert.Empty(sink.Sent);
    }
}
