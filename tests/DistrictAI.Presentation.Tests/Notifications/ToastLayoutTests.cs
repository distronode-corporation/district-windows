using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels.Notifications;
using Xunit;

namespace DistrictAI.Presentation.Tests.Notifications;

public sealed class ToastLayoutTests
{
    private static readonly NotificationActionView[] _answerDecline =
        [new("Answer", "answer"), new("Decline", "decline")];

    private static NotificationView Message(string id = "message:m-1") =>
        new(id, "New message", "Open District AI to read it.", false, [], NotificationKind.Message, "1|message|ws-1|m-1");

    [Fact]
    public void AMessageIsAPlainToastWithNoButtonsThatCarriesOnlyItsId()
    {
        var asked = false;
        var layout = ToastLayout.For(Message(), () => asked = true);
        Assert.Equal(ToastStyle.Message, layout.Style);
        Assert.Empty(layout.Buttons);
        Assert.Equal("message:m-1", layout.Id);
        Assert.Equal(ToastLayout.TagFor("message:m-1"), layout.Tag);
        Assert.Equal("New message", layout.Title);
        Assert.Equal("Open District AI to read it.", layout.Body);
        Assert.Equal("1|message|ws-1|m-1", layout.OpenTarget);
        Assert.False(asked);
    }

    [Fact]
    public void AMessageNeverTakesButtonsOrUrgency()
    {
        var odd = new NotificationView("message:m-2", "New message", "Body", true, _answerDecline, NotificationKind.Message, string.Empty);
        var layout = ToastLayout.For(odd, () => true);
        Assert.Equal(ToastStyle.Message, layout.Style);
        Assert.Empty(layout.Buttons);
    }

    [Fact]
    public void ACallKeepsItsStyles()
    {
        var ringing = new NotificationView("call:c-1", "Incoming call", "Body", true, _answerDecline, NotificationKind.IncomingCall, "1|incoming-call|ws-1|c-1");
        var layout = ToastLayout.For(ringing, () => throw new InvalidOperationException("not asked"));
        Assert.Equal(ToastStyle.IncomingCall, layout.Style);
        Assert.Equal([new ToastButton("Answer", "answer"), new ToastButton("Decline", "decline")], layout.Buttons);

        var waiting = ringing with { Actions = [] };
        Assert.Equal(ToastStyle.Urgent, ToastLayout.For(waiting, () => true).Style);
        Assert.Equal(ToastStyle.Plain, ToastLayout.For(waiting, () => false).Style);

        var missed = new NotificationView("call:c-1", "Missed call", "Body", false, [], NotificationKind.Call, "1|call|ws-1|c-1");
        Assert.Equal(ToastStyle.Plain, ToastLayout.For(missed, () => true).Style);
        Assert.Equal("1|call|ws-1|c-1", ToastLayout.For(missed, () => true).OpenTarget);
    }

    [Fact]
    public void TheBodysPressReadsBackAsTheIdAlone()
    {
        var arguments = new Dictionary<string, string> { [ToastLayout.IdArgument] = "message:m-1" };
        Assert.True(ToastLayout.TryReadActivation(arguments, out var id, out var action, out var target));
        Assert.Equal("message:m-1", id);
        Assert.Null(action);
        Assert.Null(target);

        arguments[ToastLayout.ActionArgument] = "answer";
        Assert.True(ToastLayout.TryReadActivation(arguments, out _, out action, out _));
        Assert.Equal("answer", action);

        arguments[ToastLayout.ActionArgument] = string.Empty;
        Assert.True(ToastLayout.TryReadActivation(arguments, out _, out action, out _));
        Assert.Null(action);
    }

    [Fact]
    public void ArgumentsThisAppDidNotWriteAreRefused()
    {
        Assert.False(ToastLayout.TryReadActivation(new Dictionary<string, string>(), out var id, out var action, out var target));
        Assert.Equal(string.Empty, id);
        Assert.Null(action);
        Assert.Null(target);
        var empty = new Dictionary<string, string> { [ToastLayout.IdArgument] = string.Empty, [ToastLayout.ActionArgument] = "answer" };
        Assert.False(ToastLayout.TryReadActivation(empty, out _, out action, out target));
        Assert.Null(action);
        Assert.Null(target);
        var noId = new Dictionary<string, string> { [ToastLayout.OpenTargetArgument] = "1|message|ws-1|m-1" };
        Assert.False(ToastLayout.TryReadActivation(noId, out _, out _, out target));
        Assert.Null(target);
    }

    [Fact]
    public void TheBodysPressHandsBackWhatItOpensAndAButtonsDoesNot()
    {
        var arguments = new Dictionary<string, string>
        {
            [ToastLayout.IdArgument] = "message:m-1",
            [ToastLayout.OpenTargetArgument] = "1|message|ws-1|m-1",
        };
        Assert.True(ToastLayout.TryReadActivation(arguments, out var id, out var action, out var target));
        Assert.Equal("message:m-1", id);
        Assert.Null(action);
        // Handed on as it is: the core alone decides whether it reads back.
        Assert.Equal("1|message|ws-1|m-1", target);

        arguments[ToastLayout.OpenTargetArgument] = "garbled";
        Assert.True(ToastLayout.TryReadActivation(arguments, out _, out _, out target));
        Assert.Equal("garbled", target);

        arguments[ToastLayout.OpenTargetArgument] = string.Empty;
        Assert.True(ToastLayout.TryReadActivation(arguments, out _, out _, out target));
        Assert.Null(target);

        // A stale Answer never becomes a click on the body.
        arguments[ToastLayout.OpenTargetArgument] = "1|incoming-call|ws-1|c-1";
        arguments[ToastLayout.ActionArgument] = "answer";
        Assert.True(ToastLayout.TryReadActivation(arguments, out _, out action, out target));
        Assert.Equal("answer", action);
        Assert.Null(target);
    }

    [Fact]
    public void ATagIsAStableShortHashOfTheId()
    {
        var tag = ToastLayout.TagFor("message:m-1");
        Assert.Equal(16, tag.Length);
        Assert.Equal(tag, ToastLayout.TagFor("message:m-1"));
        Assert.NotEqual(tag, ToastLayout.TagFor("message:m-2"));
        Assert.Equal(16, ToastLayout.TagFor(new string('x', 10_000)).Length);
    }

    [Fact]
    public void NothingNullIsTaken()
    {
        Assert.Throws<ArgumentNullException>(() => ToastLayout.For(null!, () => true));
        Assert.Throws<ArgumentNullException>(() => ToastLayout.For(Message(), null!));
        Assert.Throws<ArgumentNullException>(() => ToastLayout.TryReadActivation(null!, out _, out _, out _));
    }
}
