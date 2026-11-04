using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DistrictAI.Core;
using DistrictAI.Core.Ffi;
using DistrictAI.L10n;
using DistrictAI.ViewModels.Calls;

namespace DistrictAI.ViewModels.Composer;

/// <summary>
/// The reply box under a conversation, copied from the core's
/// <see cref="ComposerView"/>: the text, the images, Send, Attach, the AI
/// button, the busy line and the failure line. Every edit is sent as it
/// happens and the core's text is written back only when the box did not send
/// it (a saved draft restored, a reply the model wrote, the box cleared after
/// a send). Each button works only when the core says so, and is turned off as
/// it is pressed, so one press is one message, one upload, one billed draft.
/// </summary>
public sealed partial class ComposerViewModel : ObservableObject
{
    private TextEcho _echo = new();
    private PageContext? _context;
    private bool _writing;
    private DraftPhase _phase;

    /// <summary>Where a reply asked of the model stands, as this box saw it.</summary>
    private enum DraftPhase
    {
        /// <summary>None asked for, or the box no longer holds what one wrote.</summary>
        None,

        /// <summary>Asked for, not written yet.</summary>
        Asked,

        /// <summary>Written into the box: Report is offered on it.</summary>
        Written,
    }

    /// <summary>The conversation the box is under, which a report on a draft names.</summary>
    public string ThreadKey { get; private set; } = string.Empty;

    /// <summary>Whether the box shows at all: the member may reply here.</summary>
    [ObservableProperty]
    public partial bool Visible { get; set; }

    /// <summary>What the box holds.</summary>
    [ObservableProperty]
    public partial string Text { get; set; } = string.Empty;

    /// <summary>Whether the box is read-only: while a message is on its way.</summary>
    [ObservableProperty]
    public partial bool IsReadOnly { get; set; }

    /// <summary>The images waiting to go with the message.</summary>
    public ObservableCollection<AttachmentChip> Attachments { get; } = [];

    /// <summary>Whether any image is waiting.</summary>
    [ObservableProperty]
    public partial bool HasAttachments { get; set; }

    /// <summary>Whether Attach is offered: a text message conversation.</summary>
    [ObservableProperty]
    public partial bool ShowAttach { get; set; }

    /// <summary>Whether Attach works.</summary>
    [ObservableProperty]
    public partial bool CanAttach { get; set; }

    /// <summary>Whether Send works.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    public partial bool CanSend { get; set; }

    /// <summary>Whether the AI button works.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DraftReplyCommand))]
    public partial bool CanDraftReply { get; set; }

    /// <summary>What the box is waiting for ("Sending"), or empty.</summary>
    [ObservableProperty]
    public partial string Busy { get; set; } = string.Empty;

    /// <summary>Whether the box is waiting for anything.</summary>
    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>Why the last send, upload or draft failed, or why a file was refused, or empty.</summary>
    [ObservableProperty]
    public partial string Failure { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="Failure"/>.</summary>
    [ObservableProperty]
    public partial bool HasFailure { get; set; }

    /// <summary>The AI button: "Draft a reply with AI".</summary>
    [ObservableProperty]
    public partial string DraftReplyLabel { get; set; } = string.Empty;

    /// <summary>What the AI button does, and that each press is billed.</summary>
    [ObservableProperty]
    public partial string DraftReplyNote { get; set; } = string.Empty;

    /// <summary>The heading over the box while it holds what the model wrote.</summary>
    [ObservableProperty]
    public partial string DraftLabel { get; set; } = string.Empty;

    /// <summary>Whether the box holds a reply the model wrote: its heading and Report show.</summary>
    [ObservableProperty]
    public partial bool HasAiDraft { get; set; }

    /// <summary>How Report is offered on the model's reply.</summary>
    [ObservableProperty]
    public partial ReportAvailability DraftReport { get; set; } = ReportAvailability.Hidden;

    /// <summary>Report's label: "Report", or "Report on the web" for a member support refuses.</summary>
    [ObservableProperty]
    public partial string DraftReportLabel { get; set; } = string.Empty;

    /// <summary>Whether Report on the draft can be pressed: not while a report is on its way.</summary>
    [ObservableProperty]
    public partial bool DraftReportEnabled { get; set; }

    /// <summary>The channel chooser's label: "Reply channel".</summary>
    [ObservableProperty]
    public partial string ChannelLabel { get; set; } = string.Empty;

    /// <summary>The ways to reply the service lists for the conversation.</summary>
    public ObservableCollection<ChannelChoice> Channels { get; } = [];

    /// <summary>Whether the chooser shows: there is more than one way to reply.</summary>
    [ObservableProperty]
    public partial bool ShowChannels { get; set; }

    /// <summary>Whether the chooser works now: not while anything is on its way.</summary>
    [ObservableProperty]
    public partial bool CanChooseChannel { get; set; }

    /// <summary>The channel the reply goes on.</summary>
    [ObservableProperty]
    public partial ChannelChoice? SelectedChannel { get; set; }

    /// <summary>Said beside the images when the channel cannot carry them, or empty.</summary>
    [ObservableProperty]
    public partial string AttachmentsNote { get; set; } = string.Empty;

    /// <summary>Whether there is an <see cref="AttachmentsNote"/>.</summary>
    [ObservableProperty]
    public partial bool HasAttachmentsNote { get; set; }

    /// <summary>Whether the free-text box and Send show: not while WhatsApp allows only a template.</summary>
    [ObservableProperty]
    public partial bool ShowText { get; set; } = true;

    /// <summary>The WhatsApp window and its templates.</summary>
    public TemplatePickerViewModel WhatsApp { get; } = new();

    /// <summary>What a report on the reply in the box is about.</summary>
    internal ReportTarget DraftTarget => new ReportTarget.AiDraft(ThreadKey);

    internal void Attach(PageContext context)
    {
        _context = context;
        WhatsApp.Attach(context);
    }

    /// <summary>
    /// Shows <paramref name="view"/>, the box under <paramref name="threadKey"/>,
    /// or hides it when there is none. Another conversation starts the box afresh.
    /// </summary>
    internal void Show(string threadKey, ComposerView? view, bool reportSending)
    {
        if (threadKey != ThreadKey)
        {
            ThreadKey = threadKey;
            _echo = new TextEcho();
            _phase = DraftPhase.None;
        }
        Visible = view is not null;
        if (view is null)
        {
            _phase = DraftPhase.None;
            HasAiDraft = false;
            CanAttach = CanSend = CanDraftReply = CanChooseChannel = false;
            WhatsApp.Show(null);
            return;
        }
        ShowChannelsOf(view);
        WhatsApp.Show(view.Whatsapp);
        ShowText = view.Whatsapp?.WindowClosed is null;
        var written = _echo.Write(view.Text, Text);
        if (written)
        {
            _writing = true;
            try
            {
                Text = view.Text;
            }
            finally
            {
                _writing = false;
            }
        }
        TrackDraft(view, written);
        IsReadOnly = view.Sending;
        Display.Sync(Attachments, [.. view.Attachments.Select(a => new AttachmentChip(a.Url, a.Name))]);
        HasAttachments = view.Attachments.Length > 0;
        ShowAttach = view.ShowAttach;
        CanAttach = view.CanAttach;
        CanSend = view.CanSend;
        CanDraftReply = view.CanDraftReply;
        Busy = view.Busy ?? string.Empty;
        IsBusy = view.Busy is not null;
        Failure = Display.Failure(view.Failure);
        HasFailure = view.Failure is not null;
        DraftReplyLabel = view.AiDraft.Label;
        DraftReplyNote = view.AiDraft.BillingNote;
        DraftLabel = view.AiDraft.DraftLabel;
        DraftReport = view.AiDraft.Report;
        DraftReportLabel = Display.ReportLabel(view.AiDraft.Report);
        DraftReportEnabled = !reportSending;
    }

    /// <summary>
    /// Whether the box holds a reply the model wrote. The core does not say;
    /// it writes the reply into the box as if it had been typed. So it is the
    /// text the core wrote into the box, unasked by the box, after the AI
    /// button was pressed and once the model is done. It stays one while it is
    /// edited, and stops being one once the box is empty (sent or cleared).
    /// </summary>
    private void TrackDraft(ComposerView view, bool written)
    {
        if (_phase == DraftPhase.Asked && !view.Generating)
        {
            if (written && view.Text.Length > 0)
            {
                _phase = DraftPhase.Written;
            }
            else if (view.Failure is not null)
            {
                _phase = DraftPhase.None;
            }
        }
        if (_phase == DraftPhase.Written && view.Text.Length == 0)
        {
            _phase = DraftPhase.None;
        }
        HasAiDraft = _phase == DraftPhase.Written;
    }

    /// <summary>The channel chooser, from <paramref name="view"/>.</summary>
    private void ShowChannelsOf(ComposerView view)
    {
        _writing = true;
        try
        {
            ChannelLabel = view.ChannelLabel;
            Display.Sync(Channels, [.. view.Channels.Select(channel => new ChannelChoice(channel.Channel, channel.Label, channel.To))]);
            var selected = Array.FindIndex(view.Channels, channel => channel.Selected);
            SelectedChannel = selected >= 0 ? Channels[selected] : null;
            ShowChannels = view.Channels.Length > 1;
            CanChooseChannel = view.CanChooseChannel;
            AttachmentsNote = view.AttachmentsNote ?? string.Empty;
            HasAttachmentsNote = view.AttachmentsNote is not null;
        }
        finally
        {
            _writing = false;
        }
    }

    partial void OnSelectedChannelChanged(ChannelChoice? value)
    {
        if (_writing || value is null || _context is null || !CanChooseChannel)
        {
            return;
        }
        CanChooseChannel = false;
        _context.Send(new UiEvent.Composer(new ComposerAction.ChooseChannel(value.Channel)));
    }

    partial void OnTextChanged(string value)
    {
        if (_writing)
        {
            return;
        }
        _echo.Typed(value);
        _context?.Send(new UiEvent.Composer(new ComposerAction.Compose(value)));
    }

    /// <summary>Sends the message: the button, and Ctrl+Enter in the box.</summary>
    [RelayCommand(CanExecute = nameof(CanSend))]
    private void Send()
    {
        if (_context is null || !CanSend)
        {
            return;
        }
        CanSend = false;
        _context.Send(new UiEvent.Composer(new ComposerAction.Send()));
    }

    /// <summary>
    /// The AI button. Billed, so it is turned off as it is pressed and stays
    /// off until the core says another can be asked for.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanDraftReply))]
    private void DraftReply()
    {
        if (_context is null || !CanDraftReply)
        {
            return;
        }
        CanDraftReply = false;
        _phase = DraftPhase.Asked;
        HasAiDraft = false;
        _context.Send(new UiEvent.Composer(new ComposerAction.DraftReply()));
    }

    /// <summary>
    /// Whether Attach may open the file chooser now, for the conversation it
    /// is to attach to (<see cref="ThreadKey"/>, handed back to
    /// <see cref="Attached"/>).
    /// </summary>
    internal bool MayAttach => _context is not null && Visible && CanAttach;

    /// <summary>
    /// What the chooser opened under <paramref name="threadKey"/> returned:
    /// nothing when cancelled, else a file the core is to check and upload, or
    /// one that could not be read. Dropped when the member has moved to
    /// another conversation meanwhile.
    /// </summary>
    internal void Attached(string threadKey, IReadOnlyList<PickedFile> picked)
    {
        if (_context is null || !MayAttach || threadKey != ThreadKey)
        {
            return;
        }
        foreach (var file in picked)
        {
            _context.Send(new UiEvent.Composer(file.Data is { } data
                ? new ComposerAction.Attach(data)
                : new ComposerAction.AttachFailed()));
        }
    }

    /// <summary>Takes an image off the message.</summary>
    [RelayCommand]
    private void RemoveAttachment(AttachmentChip? chip)
    {
        if (chip is null || _context is null)
        {
            return;
        }
        _context.Send(new UiEvent.Composer(new ComposerAction.RemoveAttachment(chip.Url)));
    }

    /// <summary>Puts the failure line away.</summary>
    [RelayCommand]
    private void DismissFailure()
    {
        HasFailure = false;
        _context?.Send(new UiEvent.Composer(new ComposerAction.DismissFailure()));
    }
}

/// <summary>One image on the message, as its chip shows it.</summary>
/// <param name="Url">Where the service holds it, to remove it by.</param>
/// <param name="Name">"Image 1".</param>
public sealed record AttachmentChip(string Url, string Name)
{
    /// <summary>The remove button's accessible name: "Remove Image 1".</summary>
    public string RemoveLabel => S.ComposerRemoveAttachment(Name);
}

/// <summary>One way to reply, in the channel chooser.</summary>
/// <param name="Channel">The channel as the service names it, to choose it by.</param>
/// <param name="Label">Its name: "Text message", "WhatsApp".</param>
/// <param name="To">Where the reply goes on it.</param>
public sealed record ChannelChoice(string Channel, string Label, string To)
{
    /// <inheritdoc/>
    public override string ToString() => Label;
}
