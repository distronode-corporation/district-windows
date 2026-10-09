using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels.Support;
using Xunit;

namespace DistrictAI.Presentation.Tests.Support;

public sealed class SupportRequestViewModelTests
{
    private const string Question = "Mark this request as resolved?";

    private static SupportRequestView View(
        string key = "DA-42",
        LoadStatus? status = null,
        string reply = "",
        bool canReply = false,
        bool sending = false,
        FailureView? sendFailure = null,
        bool canClose = true,
        bool closing = false,
        bool confirming = false,
        FailureView? closeFailure = null,
        string? closed = null) =>
        new(
            "Outbound calls failing",
            key,
            status ?? new LoadStatus.Ready(),
            "DA-42",
            "In Progress",
            false,
            "2026-09-05T08:00:00.000Z",
            [
                new SupportMessageView("m-1", "You", "Every call fails.", string.Empty, true),
                new SupportMessageView("m-2", "Distronode Support", "Looking now.", string.Empty, false),
            ],
            null,
            reply,
            canReply,
            sending,
            sendFailure,
            canClose || closing,
            canClose,
            closing,
            confirming,
            Question,
            "Mark as resolved",
            closeFailure,
            closed);

    private static (SupportRequestViewModel Model, RecordingSink Sink) Attached()
    {
        var model = new SupportRequestViewModel();
        var (context, sink) = Pages.Context();
        model.Attach(context);
        return (model, sink);
    }

    [Fact]
    public void ItShowsTheRequestAndItsConversation()
    {
        var (model, _) = Attached();
        model.Show(View());
        Assert.Equal("Outbound calls failing", model.Title);
        Assert.Equal("DA-42 · In Progress", model.ReferenceLine);
        Assert.Equal(2, model.Messages.Count);
        Assert.True(model.Messages[0].FromWorkspace);
        Assert.True(model.Messages[1].FromDistronode);
        Assert.Equal("Distronode Support", model.Messages[1].Meta);
        Assert.Equal("You, Every call fails.", model.Messages[0].AccessibleName);
        Assert.Equal(model.Messages[0].AccessibleName, model.Messages[0].ToString());
        Assert.True(model.CloseOffered);
        Assert.True(model.AskCloseCommand.CanExecute(null));
        Assert.True(model.CanWriteReply);
        Assert.False(model.HasFailure);
        Assert.False(model.HasClosed);
    }

    [Fact]
    public void AFailedReadIsAStatusPage()
    {
        var (model, _) = Attached();
        model.Show(View(status: new LoadStatus.Failed(new FailureView("Not found.", null, false), "Could not load this request")));
        Assert.True(model.Load.Failed);
        Assert.False(model.Load.CanRetry);
        Assert.False(model.CanWriteReply);
    }

    [Fact]
    public void TheReplyIsSentAsTypedAndKeptWhenItFails()
    {
        var (model, sink) = Attached();
        model.Show(View());
        model.Reply = "Still failing.";
        model.Show(View(reply: "Still failing.", canReply: true));
        Assert.Equal("Still failing.", model.Reply);
        Assert.True(model.SendReplyCommand.CanExecute(null));
        model.SendReplyCommand.Execute(null);

        model.Show(View(reply: "Still failing.", sending: true));
        Assert.True(model.Sending);
        Assert.False(model.CanWriteReply);
        model.Show(View(reply: "Still failing.", canReply: true, sendFailure: new FailureView("Busy.", "Affected regions: US", true)));
        Assert.True(model.HasFailure);
        Assert.Equal("Busy." + Environment.NewLine + "Affected regions: US", model.Failure);
        Assert.Equal("Still failing.", model.Reply);
        model.DismissNoticesCommand.Execute(null);

        // Sent: the core clears the box.
        model.Show(View());
        Assert.Equal(string.Empty, model.Reply);
        Assert.Equal(
            [
                new UiEvent.Support(new SupportAction.EditReply("Still failing.")),
                new UiEvent.Support(new SupportAction.SendReply()),
                new UiEvent.Support(new SupportAction.DismissFailures()),
            ],
            sink.Sent);
    }

    [Fact]
    public void ClosingAsksFirst()
    {
        var (model, sink) = Attached();
        model.Show(View());
        model.AskCloseCommand.Execute(null);
        model.Show(View(confirming: true));
        Assert.True(model.ConfirmingClose);
        Assert.Equal(Question, model.CloseQuestion);
        Assert.Equal("Mark as resolved", model.CloseAction);
        model.CancelCloseCommand.Execute(null);
        model.ConfirmCloseCommand.Execute(null);

        model.Show(View(canClose: false, closing: true));
        Assert.True(model.Closing);
        Assert.True(model.CloseOffered);
        Assert.False(model.AskCloseCommand.CanExecute(null));

        model.Show(View(canClose: false, closed: "Closed as Done."));
        Assert.True(model.HasClosed);
        Assert.Equal("Closed as Done.", model.Closed);
        Assert.False(model.CloseOffered);

        model.Show(View(closeFailure: new FailureView("It cannot be closed from here.", null, false)));
        Assert.True(model.HasFailure);
        Assert.Equal(
            [
                new UiEvent.Support(new SupportAction.AskClose()),
                new UiEvent.Support(new SupportAction.CancelClose()),
                new UiEvent.Support(new SupportAction.ConfirmClose()),
            ],
            sink.Sent);
    }

    [Fact]
    public void AnotherRequestStartsItsReplyFromTheCore()
    {
        var (model, sink) = Attached();
        model.Show(View());
        model.Reply = "Draft for 42";
        model.Show(View(key: "DA-40", reply: string.Empty));
        Assert.Equal(string.Empty, model.Reply);
        Assert.Single(sink.Sent);
    }

    [Fact]
    public void NothingIsSentBeforeAttaching()
    {
        var model = new SupportRequestViewModel();
        model.Reply = "typed";
        model.Send(new SupportAction.Open());
        var (context, sink) = Pages.Context();
        model.Attach(context);
        Assert.Empty(sink.Sent);
    }
}
