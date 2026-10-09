using DistrictAI.Core;
using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels;
using DistrictAI.ViewModels.Composer;
using Xunit;

namespace DistrictAI.Presentation.Tests.Composer;

public sealed class ComposerViewModelTests
{
    private const string Note = "Writes a suggested reply into the box for you to read before sending. Each suggestion is billed.";
    private const string Written = "Thanks for waiting - Thursday at 2pm is confirmed.";

    private static ComposerView View(
        string text = "",
        AttachmentView[]? attachments = null,
        bool showAttach = true,
        bool canAttach = true,
        bool canSend = false,
        bool canDraftReply = true,
        bool sending = false,
        bool generating = false,
        string? busy = null,
        FailureView? failure = null,
        ReportAvailability report = ReportAvailability.InApp) =>
        new(
            text,
            attachments ?? [],
            showAttach,
            canAttach,
            canSend,
            canDraftReply,
            sending,
            Attaching: false,
            generating,
            busy,
            failure,
            new AiDraftOfferView("Draft a reply with AI", Note, "AI draft", report));

    private static UiEvent.Composer Action(ComposerAction action) => new(action);

    private static (ComposerViewModel Box, RecordingSink Sink) Attached(ComposerView? view = null)
    {
        var (context, sink) = Pages.Context();
        var box = new ComposerViewModel();
        box.Attach(context);
        box.Show("contact:c-1", view ?? View(), reportSending: false);
        return (box, sink);
    }

    [Fact]
    public void ItCopiesTheBox()
    {
        var box = new ComposerViewModel();
        box.Show("contact:c-1", View(
            "Roof:",
            [new AttachmentView("https://example.com/m/1", "Image 1")],
            canSend: true,
            sending: true,
            busy: "Sending",
            failure: V.Failure("Only JPEG, PNG, GIF or WebP images can be attached.")), reportSending: false);

        Assert.True(box.Visible);
        Assert.Equal("Roof:", box.Text);
        Assert.True(box.IsReadOnly);
        Assert.Equal([new AttachmentChip("https://example.com/m/1", "Image 1")], box.Attachments);
        Assert.Equal("Remove Image 1", box.Attachments[0].RemoveLabel);
        Assert.True(box.HasAttachments);
        Assert.True(box.ShowAttach);
        Assert.True(box.CanAttach);
        Assert.True(box.SendCommand.CanExecute(null));
        Assert.True(box.DraftReplyCommand.CanExecute(null));
        Assert.Equal("Sending", box.Busy);
        Assert.True(box.IsBusy);
        Assert.Equal("Only JPEG, PNG, GIF or WebP images can be attached.", box.Failure);
        Assert.True(box.HasFailure);
        Assert.Equal("Draft a reply with AI", box.DraftReplyLabel);
        Assert.Equal(Note, box.DraftReplyNote);
        Assert.Equal("AI draft", box.DraftLabel);
        Assert.Equal(ReportAvailability.InApp, box.DraftReport);
        Assert.Equal("Report", box.DraftReportLabel);
        Assert.True(box.DraftReportEnabled);
        Assert.False(box.HasAiDraft);
        Assert.Equal(new ReportTarget.AiDraft("contact:c-1"), box.DraftTarget);

        box.Show("contact:c-1", View(showAttach: false, canAttach: false, canDraftReply: false, report: ReportAvailability.OnWeb), reportSending: true);
        Assert.Equal(string.Empty, box.Text);
        Assert.False(box.IsReadOnly);
        Assert.Empty(box.Attachments);
        Assert.False(box.HasAttachments);
        Assert.False(box.ShowAttach);
        Assert.False(box.SendCommand.CanExecute(null));
        Assert.False(box.DraftReplyCommand.CanExecute(null));
        Assert.Equal(string.Empty, box.Busy);
        Assert.False(box.IsBusy);
        Assert.False(box.HasFailure);
        Assert.Equal("Report on the web", box.DraftReportLabel);
        Assert.False(box.DraftReportEnabled);
    }

    /// <summary>A member who may not reply gets no box, and nothing in it works.</summary>
    [Fact]
    public void NoBoxWhereTheMemberCannotReply()
    {
        var (box, sink) = Attached(View(canSend: true, text: "Hi"));
        box.Show("contact:c-1", null, reportSending: false);

        Assert.False(box.Visible);
        Assert.False(box.SendCommand.CanExecute(null));
        Assert.False(box.DraftReplyCommand.CanExecute(null));
        Assert.False(box.MayAttach);
        box.Attached("contact:c-1", [new PickedFile("roof.png", null)]);
        Assert.Empty(sink.Sent);
    }

    [Fact]
    public void TypingIsSentAndTheCoresEchoDoesNotRewriteTheBox()
    {
        var (box, sink) = Attached();
        box.Text = "O";
        box.Text = "On";
        // The core's answer to the first edit, after the second was typed.
        box.Show("contact:c-1", View("O"), reportSending: false);
        Assert.Equal("On", box.Text);

        // A saved draft the box did not type is written, and not sent back.
        box.Show("contact:c-1", View("Saved earlier."), reportSending: false);
        Assert.Equal("Saved earlier.", box.Text);

        Assert.Equal(
            [Action(new ComposerAction.Compose("O")), Action(new ComposerAction.Compose("On"))],
            sink.Sent);
    }

    /// <summary>One press is one message: Send turns itself off until the core says otherwise.</summary>
    [Fact]
    public void OnePressIsOneMessage()
    {
        var unattached = new ComposerViewModel();
        unattached.Show("contact:c-1", View("Hi", canSend: true), reportSending: false);
        unattached.SendCommand.Execute(null);
        Assert.True(unattached.CanSend);

        var (box, sink) = Attached(View("Hi", canSend: true));
        box.SendCommand.Execute(null);
        Assert.False(box.SendCommand.CanExecute(null));
        box.SendCommand.Execute(null);
        Assert.Equal([Action(new ComposerAction.Send())], sink.Sent);
    }

    [Fact]
    public void PickedFilesGoToTheCoreForTheThreadTheyWerePickedFor()
    {
        var (box, sink) = Attached();
        Assert.True(box.MayAttach);
        var roof = new PickedFileView("roof.png", 4, [0x89, 0x50, 0x4E, 0x47]);

        box.Attached("contact:c-1", [new PickedFile("roof.png", roof), new PickedFile("gone.png", null)]);
        box.Attached("contact:c-1", []);
        // Picked while the member moved to another conversation: dropped.
        box.Attached("contact:c-2", [new PickedFile("roof.png", roof)]);

        Assert.Equal(
            [Action(new ComposerAction.Attach(roof)), Action(new ComposerAction.AttachFailed())],
            sink.Sent);

        box.Show("contact:c-1", View(canAttach: false), reportSending: false);
        Assert.False(box.MayAttach);
        Assert.False(new ComposerViewModel().MayAttach);
    }

    [Fact]
    public void AnImageIsRemovedAndTheFailureDismissed()
    {
        var unattached = new ComposerViewModel();
        unattached.RemoveAttachmentCommand.Execute(new AttachmentChip("u", "Image 1"));
        unattached.DismissFailureCommand.Execute(null);

        var (box, sink) = Attached(View(attachments: [new AttachmentView("https://example.com/m/1", "Image 1")], failure: V.Failure()));
        box.RemoveAttachmentCommand.Execute(null);
        box.RemoveAttachmentCommand.Execute(box.Attachments[0]);
        box.DismissFailureCommand.Execute(null);

        Assert.False(box.HasFailure);
        Assert.Equal(
            [Action(new ComposerAction.RemoveAttachment("https://example.com/m/1")), Action(new ComposerAction.DismissFailure())],
            sink.Sent);
    }

    /// <summary>
    /// The AI button asks once, and what the model writes into the box shows
    /// as an AI draft, with Report, while it is edited, until the box is empty.
    /// </summary>
    [Fact]
    public void AnAiDraftIsMarkedUntilTheBoxIsEmpty()
    {
        var unattached = new ComposerViewModel();
        unattached.Show("contact:c-1", View(), reportSending: false);
        unattached.DraftReplyCommand.Execute(null);
        Assert.True(unattached.CanDraftReply);

        var (box, sink) = Attached();
        box.DraftReplyCommand.Execute(null);
        Assert.False(box.DraftReplyCommand.CanExecute(null));
        box.DraftReplyCommand.Execute(null);
        Assert.Equal([Action(new ComposerAction.DraftReply())], sink.Sent);

        // A view from before the core took the press, then the model at work.
        box.Show("contact:c-1", View(), reportSending: false);
        Assert.False(box.HasAiDraft);
        box.Show("contact:c-1", View(generating: true, canDraftReply: false, busy: "Writing a reply"), reportSending: false);
        Assert.False(box.HasAiDraft);

        box.Show("contact:c-1", View(Written, canSend: true), reportSending: false);
        Assert.Equal(Written, box.Text);
        Assert.True(box.HasAiDraft);

        // Edited, it is still what the model wrote, for Report.
        box.Text = Written + " Thanks!";
        box.Show("contact:c-1", View(Written + " Thanks!", canSend: true), reportSending: false);
        Assert.True(box.HasAiDraft);

        // Sent: the box is empty, and the mark goes.
        box.Show("contact:c-1", View(), reportSending: false);
        Assert.False(box.HasAiDraft);
    }

    [Fact]
    public void AFailedDraftIsNoDraft()
    {
        var (box, _) = Attached(View("Typed myself.", canSend: true));
        box.DraftReplyCommand.Execute(null);
        box.Show("contact:c-1", View("Typed myself.", canSend: true, failure: V.Failure("Could not write a reply.")), reportSending: false);
        Assert.False(box.HasAiDraft);
        // What the box later gets from elsewhere is not the model's.
        box.Show("contact:c-1", View("Restored."), reportSending: false);
        Assert.False(box.HasAiDraft);
    }

    [Fact]
    public void AnotherConversationStartsTheBoxAfresh()
    {
        var (box, _) = Attached();
        box.DraftReplyCommand.Execute(null);
        box.Show("contact:c-1", View(Written, canSend: true), reportSending: false);
        Assert.True(box.HasAiDraft);

        box.Show("contact:c-2", View("Saved for c-2."), reportSending: false);
        Assert.Equal("contact:c-2", box.ThreadKey);
        Assert.Equal("Saved for c-2.", box.Text);
        Assert.False(box.HasAiDraft);
        Assert.Equal(new ReportTarget.AiDraft("contact:c-2"), box.DraftTarget);

        box.DraftReplyCommand.Execute(null);
        box.Show("contact:c-2", null, reportSending: false);
        box.Show("contact:c-2", View(Written), reportSending: false);
        Assert.False(box.HasAiDraft);
    }
}

public sealed class ThreadComposerTests
{
    private static ThreadView Thread(string note, ComposerView? composer) =>
        new("contact:c-1", "Alex", V.Ready, [], false, false, null, false, null, note, composer);

    /// <summary>The thread shows the box for a member who may reply, and the core's note for one who may not.</summary>
    [Fact]
    public void TheThreadCarriesTheBoxOrSaysWhyNot()
    {
        var (context, sink) = Pages.Context();
        var thread = new ThreadViewModel();
        thread.Attach(context);

        thread.Show(Thread("You have read-only access to this workspace, so you cannot reply here.", null), reportSending: false);
        Assert.True(thread.HasReadOnlyNote);
        Assert.False(thread.Composer.Visible);

        var box = new ComposerView("", [], true, true, false, true, false, false, false, null, null,
            new AiDraftOfferView("Draft a reply with AI", "Billed.", "AI draft", ReportAvailability.InApp));
        thread.Show(Thread(string.Empty, box), reportSending: false);
        Assert.False(thread.HasReadOnlyNote);
        Assert.True(thread.Composer.Visible);
        Assert.Equal("contact:c-1", thread.Composer.ThreadKey);

        thread.Composer.Text = "Hi";
        Assert.Equal([new UiEvent.Composer(new ComposerAction.Compose("Hi"))], sink.Sent);
    }
}
