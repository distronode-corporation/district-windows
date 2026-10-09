using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels.Calls;

namespace DistrictAI.ViewModels.Support;

/// <summary>
/// One support request: where it stands, the conversation with Distronode,
/// the reply, and marking it resolved, which asks first and is offered only
/// when the core says it can be done. Copied from the core's
/// <see cref="SupportRequestView"/>.
/// </summary>
public sealed partial class SupportRequestViewModel : ObservableObject
{
    private TextEcho _replyEcho = new();
    private PageContext? _context;
    private string _key = string.Empty;
    private bool _writing;

    /// <summary>Loading, a read that failed, and a refresh.</summary>
    public LoadStateViewModel Load { get; } = new();

    /// <summary>The conversation, oldest first.</summary>
    public ObservableCollection<SupportMessageItem> Messages { get; } = [];

    /// <summary>The heading: the subject once read.</summary>
    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;

    /// <summary>Its reference and where it stands, or empty until read.</summary>
    [ObservableProperty]
    public partial string ReferenceLine { get; set; } = string.Empty;

    /// <summary>The reply being written.</summary>
    [ObservableProperty]
    public partial string Reply { get; set; } = string.Empty;

    /// <summary>Whether "Send" works for the reply.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendReplyCommand))]
    public partial bool CanReply { get; set; }

    /// <summary>Whether the reply is on its way.</summary>
    [ObservableProperty]
    public partial bool Sending { get; set; }

    /// <summary>Whether the reply box can be written in (read, and no reply on its way).</summary>
    [ObservableProperty]
    public partial bool CanWriteReply { get; set; }

    /// <summary>Why the last reply or close failed, or empty.</summary>
    [ObservableProperty]
    public partial string Failure { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="Failure"/>.</summary>
    [ObservableProperty]
    public partial bool HasFailure { get; set; }

    /// <summary>The confirmation of a close ("Closed as Done."), or empty.</summary>
    [ObservableProperty]
    public partial string Closed { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="Closed"/> confirmation.</summary>
    [ObservableProperty]
    public partial bool HasClosed { get; set; }

    /// <summary>Whether "Mark as resolved" shows.</summary>
    [ObservableProperty]
    public partial bool CloseOffered { get; set; }

    /// <summary>Whether "Mark as resolved" works.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AskCloseCommand))]
    public partial bool CanClose { get; set; }

    /// <summary>Whether the close is on its way.</summary>
    [ObservableProperty]
    public partial bool Closing { get; set; }

    /// <summary>Whether the question before closing is showing.</summary>
    [ObservableProperty]
    public partial bool ConfirmingClose { get; set; }

    /// <summary>The question before closing.</summary>
    [ObservableProperty]
    public partial string CloseQuestion { get; set; } = string.Empty;

    /// <summary>The closing button's label.</summary>
    [ObservableProperty]
    public partial string CloseAction { get; set; } = string.Empty;

    internal void Attach(PageContext context)
    {
        _context = context;
        Load.Attach(context);
    }

    internal void Show(SupportRequestView view)
    {
        if (view.Key != _key)
        {
            // Another request: its reply box starts from what the core holds.
            _key = view.Key;
            _replyEcho = new TextEcho();
        }
        Load.Show(view.Status, true, null, false, view.RefreshFailure);
        Title = view.Title;
        ReferenceLine = string.Join(
            " · ",
            new[] { view.Reference, view.StatusName }.Where(part => part.Length > 0));
        Display.Sync(Messages, [.. view.Messages.Select(SupportMessageItem.From)]);
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
        Sending = view.Sending;
        CanWriteReply = view.Status is LoadStatus.Ready && !view.Sending;
        var failure = view.SendFailure ?? view.CloseFailure;
        Failure = Display.Failure(failure);
        HasFailure = failure is not null;
        Closed = view.ClosedMessage ?? string.Empty;
        HasClosed = view.ClosedMessage is not null;
        CloseOffered = view.CloseOffered;
        CanClose = view.CanClose;
        Closing = view.Closing;
        ConfirmingClose = view.ConfirmingClose;
        CloseQuestion = view.CloseQuestion;
        CloseAction = view.CloseAction;
    }

    partial void OnReplyChanged(string value)
    {
        if (_writing)
        {
            return;
        }
        _replyEcho.Typed(value);
        Send(new SupportAction.EditReply(value));
    }

    [RelayCommand(CanExecute = nameof(CanReply))]
    private void SendReply() => Send(new SupportAction.SendReply());

    [RelayCommand(CanExecute = nameof(CanClose))]
    private void AskClose() => Send(new SupportAction.AskClose());

    [RelayCommand]
    private void ConfirmClose() => Send(new SupportAction.ConfirmClose());

    [RelayCommand]
    private void CancelClose() => Send(new SupportAction.CancelClose());

    [RelayCommand]
    private void DismissNotices() => Send(new SupportAction.DismissFailures());

    /// <summary>Forwards one of the screen's actions.</summary>
    internal void Send(SupportAction action) => _context?.Send(new UiEvent.Support(action));
}

/// <summary>One message of a request's conversation.</summary>
/// <param name="Id">The message.</param>
/// <param name="Author">Who wrote it, as the service labels them.</param>
/// <param name="Body">The text.</param>
/// <param name="When">When it was written, in local time.</param>
/// <param name="FromWorkspace">Whether the workspace wrote it.</param>
public sealed record SupportMessageItem(string Id, string Author, string Body, string When, bool FromWorkspace)
{
    /// <summary>Whether Distronode wrote it.</summary>
    public bool FromDistronode => !FromWorkspace;

    /// <summary>The line under the text: who wrote it, and when.</summary>
    public string Meta => string.Join(" · ", new[] { Author, When }.Where(part => part.Length > 0));

    /// <summary>What a screen reader says for the message.</summary>
    public string AccessibleName => string.Join(", ", new[] { Author, Body, When }.Where(part => part.Length > 0));

    internal static SupportMessageItem From(SupportMessageView message) =>
        new(message.Id, message.Author, message.Body, Display.When(message.CreatedAt), message.FromWorkspace);

    /// <inheritdoc/>
    public override string ToString() => AccessibleName;
}
