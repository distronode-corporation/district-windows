using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels.Calls;

namespace DistrictAI.ViewModels.Desk;

/// <summary>
/// One help desk ticket: who raised it, its status and the buttons that move
/// it, its conversation, and the reply. Copied from the core's
/// <see cref="DeskTicketView"/>. Each change is sent once, on its press, and
/// the ticket shows what the service answers with; a status change is not
/// asked first, because every status can be moved back.
/// </summary>
public sealed partial class DeskTicketViewModel : ObservableObject
{
    private TextEcho _replyEcho = new();
    private PageContext? _context;
    private string _ticketId = string.Empty;
    private bool _writing;

    /// <summary>Loading, a read that failed, and a refresh.</summary>
    public LoadStateViewModel Load { get; } = new();

    /// <summary>The status buttons, in their order.</summary>
    public ObservableCollection<DeskStatusItem> Statuses { get; } = [];

    /// <summary>The customer's details and where the ticket came in.</summary>
    public ObservableCollection<FactItem> Details { get; } = [];

    /// <summary>The conversation, oldest first.</summary>
    public ObservableCollection<DeskMessageItem> Messages { get; } = [];

    /// <summary>The core's view of the screen.</summary>
    [ObservableProperty]
    public partial DeskTicketView? View { get; set; }

    /// <summary>The heading: the subject once read.</summary>
    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;

    /// <summary>Its reference and status, or empty until read.</summary>
    [ObservableProperty]
    public partial string ReferenceLine { get; set; } = string.Empty;

    /// <summary>When it was raised and, while it is, resolved, in local time.</summary>
    [ObservableProperty]
    public partial string DatesLine { get; set; } = string.Empty;

    /// <summary>Whether a status change is on its way.</summary>
    [ObservableProperty]
    public partial bool StatusChanging { get; set; }

    /// <summary>The reply being written.</summary>
    [ObservableProperty]
    public partial string Reply { get; set; } = string.Empty;

    /// <summary>Whether "Send" works for the reply.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendReplyCommand))]
    public partial bool CanReply { get; set; }

    /// <summary>Whether the reply box can be written in.</summary>
    [ObservableProperty]
    public partial bool CanWriteReply { get; set; }

    /// <summary>Whether the reply is on its way.</summary>
    [ObservableProperty]
    public partial bool Sending { get; set; }

    /// <summary>Why the last reply or status change failed, or empty.</summary>
    [ObservableProperty]
    public partial string Failure { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="Failure"/>.</summary>
    [ObservableProperty]
    public partial bool HasFailure { get; set; }

    /// <summary>Whether the customer was emailed the last reply, in words, or empty.</summary>
    [ObservableProperty]
    public partial string NotifiedNote { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="NotifiedNote"/>.</summary>
    [ObservableProperty]
    public partial bool HasNotifiedNote { get; set; }

    internal void Attach(PageContext context)
    {
        _context = context;
        Load.Attach(context);
    }

    internal void Show(DeskTicketView view)
    {
        if (view.TicketId != _ticketId)
        {
            // Another ticket: its reply box starts from what the core holds.
            _ticketId = view.TicketId;
            _replyEcho = new TextEcho();
        }
        View = view;
        Load.Show(view.Status, true, null, false, view.RefreshFailure);
        Title = view.Title;
        ReferenceLine = view.ReferenceLine;
        var raised = Display.When(view.CreatedAt);
        var resolved = Display.When(view.ResolvedAt);
        DatesLine = string.Join(
            " · ",
            new[] { raised.Length > 0 ? "Raised " + raised : string.Empty, resolved.Length > 0 ? "Resolved " + resolved : string.Empty }
                .Where(part => part.Length > 0));
        Display.Sync(Statuses, [.. view.Statuses.Select(choice => new DeskStatusItem(choice.Status, choice.Label, choice.Selected, view.CanChangeStatus && !choice.Selected))]);
        StatusChanging = view.StatusChanging;
        Display.Sync(Details, [.. view.Details.Select(FactItem.From)]);
        Display.Sync(Messages, [.. view.Messages.Select(DeskMessageItem.From)]);
        if (_replyEcho.Write(view.Reply, Reply))
        {
            _writing = true;
            try
            {
                Reply = view.Reply;
            }
            finally
            {
                _writing = false;
            }
        }
        CanReply = view.CanReply;
        CanWriteReply = view.CanWriteReply;
        Sending = view.Sending;
        var failure = view.SendFailure ?? view.StatusFailure;
        Failure = Display.Failure(failure);
        HasFailure = failure is not null;
        NotifiedNote = view.NotifiedNote ?? string.Empty;
        HasNotifiedNote = view.NotifiedNote is not null;
    }

    partial void OnReplyChanged(string value)
    {
        if (_writing)
        {
            return;
        }
        _replyEcho.Typed(value);
        Send(new DeskAction.EditReply(value));
    }

    /// <summary>Sends the reply: turned off as it is pressed, so one press is one reply.</summary>
    [RelayCommand(CanExecute = nameof(CanReply))]
    private void SendReply()
    {
        if (!CanReply)
        {
            return;
        }
        CanReply = false;
        Send(new DeskAction.SendReply());
    }

    /// <summary>
    /// Moves the ticket to <paramref name="choice"/>'s status, when it is not
    /// the ticket's own and no change is on its way. Every button is turned
    /// off as one is pressed, so one press is one change.
    /// </summary>
    internal void SetStatus(DeskStatusItem choice)
    {
        if (!choice.Enabled)
        {
            return;
        }
        Display.Sync(Statuses, [.. Statuses.Select(item => item with { Enabled = false })]);
        Send(new DeskAction.SetStatus(choice.Status));
    }

    [RelayCommand]
    private void DismissFailures() => Send(new DeskAction.DismissTicketFailures());

    /// <summary>Forwards one of the screen's actions.</summary>
    internal void Send(DeskAction action) => _context?.Send(new UiEvent.Desk(action));
}

/// <summary>One of a ticket's status buttons.</summary>
/// <param name="Status">The status it moves the ticket to.</param>
/// <param name="Label">Its label ("Waiting on the customer").</param>
/// <param name="Selected">Whether it is the ticket's status now.</param>
/// <param name="Enabled">Whether pressing it sends a change.</param>
public sealed record DeskStatusItem(DeskStatus Status, string Label, bool Selected, bool Enabled)
{
    /// <summary>What a screen reader says for the button.</summary>
    public string AccessibleName => Selected ? Label + ", current status" : "Mark as " + Label;

    /// <inheritdoc/>
    public override string ToString() => AccessibleName;
}

/// <summary>One message of a ticket's conversation.</summary>
/// <param name="Id">The message.</param>
/// <param name="Author">Who wrote it, in the core's words.</param>
/// <param name="Body">The text.</param>
/// <param name="When">When it was written, in local time.</param>
/// <param name="FromTeam">Whether the workspace's team wrote it.</param>
public sealed record DeskMessageItem(string Id, string Author, string Body, string When, bool FromTeam)
{
    /// <summary>Whether the customer or the receptionist wrote it.</summary>
    public bool FromOthers => !FromTeam;

    /// <summary>The line under the text: who wrote it, and when.</summary>
    public string Meta => string.Join(" · ", new[] { Author, When }.Where(part => part.Length > 0));

    /// <summary>What a screen reader says for the message.</summary>
    public string AccessibleName => string.Join(", ", new[] { Author, Body, When }.Where(part => part.Length > 0));

    internal static DeskMessageItem From(DeskMessageView message) =>
        new(message.Id, message.Author, message.Body, Display.When(message.CreatedAt), message.FromTeam);

    /// <inheritdoc/>
    public override string ToString() => AccessibleName;
}
