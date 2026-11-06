using DistrictAI.Core;
using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels;
using DistrictAI.ViewModels.Composer;
using Xunit;

namespace DistrictAI.Presentation.Tests.Composer;

/// <summary>
/// App parity: the reply channel, the conversation's owner, the WhatsApp
/// templates and the Messenger and Instagram reply window.
/// </summary>
public sealed class ParityViewModelTests
{
    private static readonly ReplyChannelView[] _channels =
    [
        new("email", "Email", "ada@example.com", true),
        new("sms", "Text message", "14165550142", false),
        new("whatsapp", "WhatsApp", "+14165550142", false),
    ];

    private static ComposerView Box(
        ReplyChannelView[]? channels = null,
        bool canChooseChannel = true,
        string? attachmentsNote = null,
        WhatsAppView? whatsapp = null,
        MetaView? meta = null,
        string text = "",
        bool canSend = false,
        bool canDraftReply = true) =>
        new(text, [], false, false, canSend, canDraftReply, false, false, false, null, null,
            new AiDraftOfferView("Draft a reply with AI", "Billed.", "AI draft", ReportAvailability.InApp),
            "Reply channel", channels ?? _channels, canChooseChannel, attachmentsNote, whatsapp, meta);

    private static TemplateVariableView Variable(uint index, string value) =>
        new(index, $"Variable {{{{{index + 1}}}}}", value, value.Trim().Length == 0 ? "Fill in this variable" : null);

    private static TemplatePickerView Picker(
        string? chosen = null,
        TemplateVariableView[]? variables = null,
        string? preview = null,
        bool canSend = false,
        string? message = null,
        TemplateOptionView[]? options = null) =>
        new(message, "Template", "Choose an approved template",
            options ?? [new TemplateOptionView("wa_tpl_reminder", "appointment_reminder (en_US)")],
            chosen, variables ?? [], "Preview", preview, "Send template", canSend, Sending: false);

    private static WhatsAppView Closed(TemplatePickerView picker) =>
        new("Window closed: WhatsApp only allows a template now", picker);

    private static AssignmentView Owner(uint selected = 0, bool showPicker = true, bool canAssign = true, FailureView? failure = null) =>
        new("Assigned to", selected == 0 ? "Unassigned" : "Assigned to founder@example.com", showPicker,
            [new AssigneeOptionView(null, "Unassigned"), new AssigneeOptionView("founder@example.com", "founder@example.com")],
            selected, canAssign, Saving: false, failure);

    private static (ComposerViewModel Box, RecordingSink Sink) Shown(ComposerView view)
    {
        var (context, sink) = Pages.Context();
        var box = new ComposerViewModel();
        box.Attach(context);
        box.Show("contact:c-1", view, reportSending: false);
        return (box, sink);
    }

    private static UiEvent.Composer Action(ComposerAction action) => new(action);

    [Fact]
    public void TheChooserShowsTheServicesChannelsAndSendsAChoiceOnce()
    {
        var (box, sink) = Shown(Box());
        Assert.True(box.ShowChannels);
        Assert.True(box.CanChooseChannel);
        Assert.Equal("Reply channel", box.ChannelLabel);
        Assert.Equal(["Email", "Text message", "WhatsApp"], box.Channels.Select(c => c.Label));
        Assert.Equal("email", box.SelectedChannel?.Channel);
        Assert.Empty(sink.Sent);

        box.SelectedChannel = box.Channels[2];
        Assert.Equal([Action(new ComposerAction.ChooseChannel("whatsapp"))], sink.Sent);
        Assert.False(box.CanChooseChannel);

        // Turned off until the core answers: a second choice is not sent.
        box.SelectedChannel = box.Channels[1];
        Assert.Single(sink.Sent);
    }

    [Fact]
    public void OneChannelHasNoChooser()
    {
        var (box, _) = Shown(Box([new("messenger", "Messenger", "fb:5550101", true)], canChooseChannel: false));
        Assert.False(box.ShowChannels);
        Assert.Equal("messenger", box.SelectedChannel?.Channel);
    }

    [Fact]
    public void AttachmentsOnAnotherChannelSayWhySendWaits()
    {
        var (box, _) = Shown(Box(attachmentsNote: "Attachments can only go by text message. Remove them to send on this channel."));
        Assert.True(box.HasAttachmentsNote);
        Assert.StartsWith("Attachments can only go", box.AttachmentsNote);
    }

    [Fact]
    public void AClosedWindowOffersTheTemplatesInPlaceOfText()
    {
        var (box, sink) = Shown(Box(whatsapp: Closed(Picker())));
        Assert.False(box.ShowText);
        var whatsApp = box.WhatsApp;
        Assert.True(whatsApp.Visible);
        Assert.True(whatsApp.HasWindowClosed);
        Assert.True(whatsApp.ShowPicker);
        Assert.Equal(["appointment_reminder (en_US)"], whatsApp.Options.Select(o => o.Label));
        Assert.Null(whatsApp.SelectedTemplate);
        Assert.False(whatsApp.SendCommand.CanExecute(null));

        whatsApp.SelectedTemplate = whatsApp.Options[0];
        Assert.Equal([Action(new ComposerAction.ChooseTemplate("wa_tpl_reminder"))], sink.Sent);
    }

    [Fact]
    public void TheAiNoteGoesWithTheAiButton()
    {
        var (box, _) = Shown(Box());
        Assert.True(box.ShowText);
        Assert.True(box.ShowDraftReplyNote);

        // A closed window hides the box and its AI button, and the note that goes with them.
        box.Show("contact:c-1", Box(whatsapp: Closed(Picker())), reportSending: false);
        Assert.False(box.ShowText);
        Assert.False(box.ShowDraftReplyNote);

        // An open window brings them back.
        box.Show("contact:c-1", Box(whatsapp: new WhatsAppView(null, null)), reportSending: false);
        Assert.True(box.ShowDraftReplyNote);
    }

    [Fact]
    public void EachVariableIsSentAsTypedAndThePreviewIsTheCores()
    {
        var (box, sink) = Shown(Box(whatsapp: Closed(Picker("wa_tpl_reminder", [Variable(0, ""), Variable(1, "")],
            "Hi {{1}}, this is a reminder of your appointment on {{2}}."))));
        var fields = box.WhatsApp.Variables;
        Assert.Equal(["Variable {{1}}", "Variable {{2}}"], fields.Select(f => f.Label));
        Assert.True(fields[0].HasRequired);
        var first = fields[0];

        first.Value = "Ada";
        Assert.Equal([Action(new ComposerAction.TemplateVariable(0, "Ada"))], sink.Sent);

        // The core's older value does not take back what was typed; the same fields stay.
        box.Show("contact:c-1", Box(whatsapp: Closed(Picker("wa_tpl_reminder", [Variable(0, ""), Variable(1, "")],
            "Hi {{1}}, this is a reminder of your appointment on {{2}}."))), reportSending: false);
        Assert.Same(first, box.WhatsApp.Variables[0]);
        Assert.Equal("Ada", first.Value);

        box.Show("contact:c-1", Box(whatsapp: Closed(Picker("wa_tpl_reminder", [Variable(0, "Ada"), Variable(1, "Thursday")],
            "Hi Ada, this is a reminder of your appointment on Thursday.", canSend: true))), reportSending: false);
        Assert.Equal("Hi Ada, this is a reminder of your appointment on Thursday.", box.WhatsApp.Preview);
        Assert.True(box.WhatsApp.HasPreview);
        Assert.Equal("Thursday", box.WhatsApp.Variables[1].Value);
        Assert.False(box.WhatsApp.Variables[1].HasRequired);
        Assert.Single(sink.Sent);

        // One press is one message.
        box.WhatsApp.SendCommand.Execute(null);
        box.WhatsApp.SendCommand.Execute(null);
        Assert.Equal(Action(new ComposerAction.SendTemplate()), sink.Sent[^1]);
        Assert.Equal(2, sink.Sent.Count);
    }

    [Fact]
    public void LoadingOrNoTemplateIsSaidInPlaceOfThePicker()
    {
        var (box, _) = Shown(Box(whatsapp: Closed(Picker(message: "Loading templates", options: []))));
        Assert.True(box.WhatsApp.HasMessage);
        Assert.Equal("Loading templates", box.WhatsApp.Message);
        Assert.False(box.WhatsApp.ShowPicker);

        box.Show("contact:c-1", Box(whatsapp: new WhatsAppView(null, null)), reportSending: false);
        Assert.True(box.ShowText);
        Assert.False(box.WhatsApp.HasWindowClosed);
        Assert.False(box.WhatsApp.HasMessage);

        box.Show("contact:c-1", null, reportSending: false);
        Assert.False(box.WhatsApp.Visible);
    }

    [Fact]
    public void TheOwnerPickerSendsAChoiceOnceAndWaits()
    {
        var (context, sink) = Pages.Context();
        var owner = new AssignmentViewModel();
        owner.Attach(context);
        owner.Show(Owner());
        Assert.True(owner.Visible);
        Assert.True(owner.ShowPicker);
        Assert.False(owner.ShowLine);
        Assert.Equal("Assigned to", owner.Label);
        Assert.Equal(["Unassigned", "founder@example.com"], owner.Options.Select(o => o.Label));
        Assert.Null(owner.SelectedOption?.Email);
        Assert.Empty(sink.Sent);

        owner.SelectedOption = owner.Options[1];
        Assert.Equal([Action(new ComposerAction.Assign("founder@example.com"))], sink.Sent);
        Assert.False(owner.CanAssign);
        owner.SelectedOption = owner.Options[0];
        Assert.Single(sink.Sent);

        owner.Show(Owner(selected: 1));
        owner.SelectedOption = owner.Options[0];
        Assert.Equal(Action(new ComposerAction.Assign(null)), sink.Sent[^1]);
    }

    [Fact]
    public void AViewerSeesTheOwnersLineAndARefusalCanBeDismissed()
    {
        var (context, sink) = Pages.Context();
        var owner = new AssignmentViewModel();
        owner.Attach(context);
        owner.Show(Owner(selected: 1, showPicker: false, canAssign: false));
        Assert.True(owner.ShowLine);
        Assert.Equal("Assigned to founder@example.com", owner.Line);

        owner.Show(Owner(failure: V.Failure("A Viewer cannot be assigned a conversation.")));
        Assert.True(owner.HasFailure);
        owner.DismissFailureCommand.Execute(null);
        Assert.False(owner.HasFailure);
        Assert.Equal([Action(new ComposerAction.DismissAssignFailure())], sink.Sent);

        owner.Show(null);
        Assert.False(owner.Visible);
    }

    [Fact]
    public void TheThreadCarriesItsOwner()
    {
        var (context, _) = Pages.Context();
        var thread = new ThreadViewModel();
        thread.Attach(context);
        thread.Show(new ThreadView("contact:c-1", "Alex", V.Ready, [], false, false, null, false, null, "", Box(), Owner(selected: 1)), reportSending: false);
        Assert.True(thread.Assignment.Visible);
        Assert.Equal("founder@example.com", thread.Assignment.SelectedOption?.Email);
    }

    [Fact]
    public void AnOwnedRowSaysWho()
    {
        var row = ThreadRowItem.From(V.ThreadRow(assignee: "Assigned to ada@example.com"));
        Assert.True(row.HasAssignee);
        Assert.Contains("Assigned to ada@example.com", row.AccessibleName);
        Assert.False(ThreadRowItem.From(V.ThreadRow()).HasAssignee);
    }

    private const string HumanAgent = "Human agent reply (7-day window)";

    private const string MetaClosed =
        "Reply window closed: Messenger and Instagram allow a reply only within 24 hours of the customer's last message.";

    private static readonly ReplyChannelView[] _messenger = [new("messenger", "Messenger", "fb:5550101", true)];

    [Fact]
    public void TheHumanAgentWindowShowsItsBadgeAndOnlyAPersonReplies()
    {
        var (box, sink) = Shown(Box(_messenger, canChooseChannel: false, meta: new MetaView(HumanAgent, null),
            text: "Thanks", canSend: true, canDraftReply: false));
        Assert.True(box.HasMetaHumanAgent);
        Assert.Equal(HumanAgent, box.MetaHumanAgent);
        Assert.False(box.HasMetaWindowClosed);
        Assert.True(box.ShowText);
        Assert.True(box.SendCommand.CanExecute(null));
        Assert.False(box.DraftReplyCommand.CanExecute(null));

        // The AI button stays off: pressing it sends nothing.
        box.DraftReplyCommand.Execute(null);
        Assert.Empty(sink.Sent);
    }

    [Fact]
    public void AClosedMetaWindowWarnsAndKeepsTheBoxWithSendOff()
    {
        var (box, sink) = Shown(Box(_messenger, canChooseChannel: false, meta: new MetaView(null, MetaClosed),
            text: "Thanks", canSend: false, canDraftReply: false));
        Assert.True(box.HasMetaWindowClosed);
        Assert.Equal(MetaClosed, box.MetaWindowClosed);
        Assert.False(box.HasMetaHumanAgent);
        // Web parity: the box stays, with what was typed, and Send is off.
        Assert.True(box.ShowText);
        Assert.Equal("Thanks", box.Text);
        Assert.False(box.SendCommand.CanExecute(null));
        box.SendCommand.Execute(null);
        Assert.Empty(sink.Sent);
        // Not WhatsApp's: no template picker.
        Assert.False(box.WhatsApp.Visible);
    }

    [Fact]
    public void AnOpenLoadingOrUnknownMetaWindowSaysNothing()
    {
        var (box, _) = Shown(Box(_messenger, canChooseChannel: false, meta: new MetaView(null, null),
            text: "Thanks", canSend: true));
        Assert.False(box.HasMetaHumanAgent);
        Assert.False(box.HasMetaWindowClosed);
        Assert.Equal(string.Empty, box.MetaHumanAgent);
        Assert.Equal(string.Empty, box.MetaWindowClosed);
        Assert.True(box.SendCommand.CanExecute(null));
        Assert.True(box.DraftReplyCommand.CanExecute(null));
    }

    [Fact]
    public void TheMetaLinesFollowTheCoreAndGoWithTheBox()
    {
        var (box, _) = Shown(Box(_messenger, canChooseChannel: false, meta: new MetaView(null, MetaClosed)));
        Assert.True(box.HasMetaWindowClosed);

        // The customer wrote again: the core reads the window open.
        box.Show("contact:c-1", Box(_messenger, canChooseChannel: false, meta: new MetaView(null, null)), reportSending: false);
        Assert.False(box.HasMetaWindowClosed);
        Assert.Equal(string.Empty, box.MetaWindowClosed);

        box.Show("contact:c-1", Box(_messenger, canChooseChannel: false, meta: new MetaView(HumanAgent, null)), reportSending: false);
        Assert.True(box.HasMetaHumanAgent);

        // Another channel (no Meta side), and then another conversation with no box: both lines go.
        box.Show("contact:c-1", Box(), reportSending: false);
        Assert.False(box.HasMetaHumanAgent);
        Assert.False(box.HasMetaWindowClosed);
        box.Show("addr:ig:5550102", Box(_messenger, canChooseChannel: false, meta: new MetaView(null, MetaClosed)), reportSending: false);
        Assert.True(box.HasMetaWindowClosed);
        box.Show("addr:ig:5550102", null, reportSending: false);
        Assert.False(box.HasMetaWindowClosed);
        Assert.False(box.HasMetaHumanAgent);
    }
}
