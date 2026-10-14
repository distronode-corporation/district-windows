using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels.Inbox;
using Xunit;

namespace DistrictAI.Presentation.Tests.Inbox;

public sealed class ComposeViewModelTests
{
    private static ComposeView Form(
        string query = "",
        ContactRowView[]? suggestions = null,
        string? typedAction = null,
        string? recipient = null,
        ComposeChannelView[]? channels = null,
        uint channel = 0,
        bool requiresSubject = false,
        string subject = "",
        string body = "",
        bool canSend = false,
        bool sending = false,
        FailureView? failure = null) =>
        new(
            "New message",
            "To",
            query,
            false,
            suggestions ?? [],
            "Showing your most recent contacts.",
            null,
            typedAction,
            recipient,
            "Change",
            null,
            "Send on",
            channels ?? [],
            channel,
            requiresSubject,
            "Subject",
            subject,
            "Message",
            body,
            sending ? "Sending" : "Send",
            "Cancel",
            canSend,
            sending,
            failure);

    [Fact]
    public void ItOpensAsTheCoreSaysAndSendsTheTypingToTheCore()
    {
        var (context, sink) = Pages.Context();
        var form = new ComposeViewModel();
        form.Attach(context);
        form.Show(null);
        Assert.False(form.IsOpen);

        form.Show(Form(suggestions: [new ContactRowView("c-1", "Ada", "+1 416 555 0142")]));
        Assert.True(form.IsOpen);
        Assert.True(form.Choosing);
        Assert.False(form.Chosen);
        Assert.Equal("Ada", Assert.Single(form.Suggestions).Name);
        Assert.Empty(sink.Sent);

        form.Query = "ada@";
        Assert.Equal(new UiEvent.Compose(new ComposeAction.Query("ada@")), Assert.Single(sink.Sent));
        form.Choose(form.Suggestions[0]);
        Assert.Equal(new UiEvent.Compose(new ComposeAction.ChooseContact("c-1")), sink.Sent[^1]);
        form.UseTypedCommand.Execute(null);
        Assert.Equal(new UiEvent.Compose(new ComposeAction.UseTyped()), sink.Sent[^1]);
    }

    [Fact]
    public void ARecipientShowsItsChannelsAndAnEmailItsSubject()
    {
        var (context, sink) = Pages.Context();
        var form = new ComposeViewModel();
        form.Attach(context);
        form.Show(Form(
            recipient: "Ada",
            channels: [new ComposeChannelView("Text message", "+1 416 555 0142"), new ComposeChannelView("Email", "ada@example.com")],
            channel: 1,
            requiresSubject: true));
        Assert.True(form.Chosen);
        Assert.Equal(2, form.Channels.Count);
        Assert.Equal(1, form.Channel);
        Assert.True(form.RequiresSubject);
        Assert.Empty(sink.Sent);

        form.Channel = 0;
        Assert.Equal(new UiEvent.Compose(new ComposeAction.SelectChannel(0)), sink.Sent[^1]);
        form.Subject = "Thursday";
        Assert.Equal(new UiEvent.Compose(new ComposeAction.EditSubject("Thursday")), sink.Sent[^1]);
        form.Body = "Hello.";
        Assert.Equal(new UiEvent.Compose(new ComposeAction.EditBody("Hello.")), sink.Sent[^1]);
        form.ClearRecipientCommand.Execute(null);
        Assert.Equal(new UiEvent.Compose(new ComposeAction.ClearRecipient()), sink.Sent[^1]);
        form.CancelCommand.Execute(null);
        Assert.Equal(new UiEvent.Compose(new ComposeAction.Cancel()), sink.Sent[^1]);
    }

    [Fact]
    public void SendGoesOnceAndAFailureShows()
    {
        var (context, sink) = Pages.Context();
        var form = new ComposeViewModel();
        form.Attach(context);
        form.Show(Form(recipient: "Ada", body: "Hello.", canSend: true));
        form.SubmitCommand.Execute(null);
        form.SubmitCommand.Execute(null);
        Assert.Single(sink.Sent, e => e is UiEvent.Compose { Action: ComposeAction.Send });

        form.Show(Form(recipient: "Ada", body: "Hello.", sending: true));
        Assert.False(form.Editable);
        Assert.Equal("Sending", form.SendLabel);

        form.Show(Form(recipient: "Ada", body: "Hello.", canSend: true, failure: new FailureView("Not sent.", null, false)));
        Assert.True(form.HasFailure);
        Assert.True(form.Editable);
        Assert.Equal("Hello.", form.Body);
    }
}
