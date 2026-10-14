using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels.Calls;

namespace DistrictAI.ViewModels.Inbox;

/// <summary>A contact the new message form offers.</summary>
/// <param name="ContactId">The contact, to choose it by.</param>
/// <param name="Name">The name, or else the number or address.</param>
/// <param name="Detail">The number and address, on one line.</param>
public sealed record ComposeSuggestionItem(string ContactId, string Name, string Detail)
{
    internal static ComposeSuggestionItem From(ContactRowView row) => new(row.ContactId, row.Name, row.Detail);
}

/// <summary>One way to reach the recipient.</summary>
/// <param name="Name">The channel's name: "Text message" or "Email".</param>
/// <param name="Address">The number or address.</param>
public sealed record ComposeChannelItem(string Name, string Address);

/// <summary>
/// The new message form, the core's: a recipient (a recent contact, or a
/// number or address typed in), the channel, a subject for an email, and the
/// message. What is typed goes to the core as it changes, and the core says
/// when it can be sent; it sends once, then closes the form and opens the
/// message's thread.
/// </summary>
public sealed partial class ComposeViewModel : ObservableObject
{
    private readonly TextEcho _queryEcho = new();
    private readonly TextEcho _subjectEcho = new();
    private readonly TextEcho _bodyEcho = new();
    private PageContext? _context;

    /// <summary>Whether the fields are being filled from the core, not typed in.</summary>
    private bool _filling;

    /// <summary>Whether the core holds the form open.</summary>
    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    /// <summary>The dialog's title.</summary>
    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;

    /// <summary>The recipient field's label.</summary>
    [ObservableProperty]
    public partial string ToLabel { get; set; } = string.Empty;

    /// <summary>What is typed in the recipient field.</summary>
    [ObservableProperty]
    public partial string Query { get; set; } = string.Empty;

    /// <summary>Whether no recipient is chosen yet: the recipient field and the contacts show.</summary>
    [ObservableProperty]
    public partial bool Choosing { get; set; } = true;

    /// <summary>Whether a recipient is chosen: the channel, subject and message show.</summary>
    [ObservableProperty]
    public partial bool Chosen { get; set; }

    /// <summary>Whether the contacts are being read.</summary>
    [ObservableProperty]
    public partial bool ContactsLoading { get; set; }

    /// <summary>The contacts matching what is typed.</summary>
    public ObservableCollection<ComposeSuggestionItem> Suggestions { get; } = [];

    /// <summary>That only the most recent contacts are offered.</summary>
    [ObservableProperty]
    public partial string RecentNote { get; set; } = string.Empty;

    /// <summary>Why the contacts could not be read, or empty.</summary>
    [ObservableProperty]
    public partial string SuggestionsFailed { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="SuggestionsFailed"/>.</summary>
    [ObservableProperty]
    public partial bool HasSuggestionsFailed { get; set; }

    /// <summary>The button that sends to what is typed, or empty.</summary>
    [ObservableProperty]
    public partial string TypedAction { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="TypedAction"/>.</summary>
    [ObservableProperty]
    public partial bool HasTypedAction { get; set; }

    /// <summary>Who the message goes to, once chosen.</summary>
    [ObservableProperty]
    public partial string Recipient { get; set; } = string.Empty;

    /// <summary>The button that goes back to choosing.</summary>
    [ObservableProperty]
    public partial string ChangeLabel { get; set; } = string.Empty;

    /// <summary>Why the chosen contact cannot be sent to, or empty.</summary>
    [ObservableProperty]
    public partial string NoAddressNote { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="NoAddressNote"/>.</summary>
    [ObservableProperty]
    public partial bool HasNoAddressNote { get; set; }

    /// <summary>The label above the channels.</summary>
    [ObservableProperty]
    public partial string SendOnLabel { get; set; } = string.Empty;

    /// <summary>Each way the recipient can be reached.</summary>
    public ObservableCollection<ComposeChannelItem> Channels { get; } = [];

    /// <summary>Whether there are channels to choose from.</summary>
    [ObservableProperty]
    public partial bool HasChannels { get; set; }

    /// <summary>Which of <see cref="Channels"/> is chosen.</summary>
    [ObservableProperty]
    public partial int Channel { get; set; }

    /// <summary>Whether the subject field shows: an email needs one.</summary>
    [ObservableProperty]
    public partial bool RequiresSubject { get; set; }

    /// <summary>The subject field's label.</summary>
    [ObservableProperty]
    public partial string SubjectLabel { get; set; } = string.Empty;

    /// <summary>The subject, as typed.</summary>
    [ObservableProperty]
    public partial string Subject { get; set; } = string.Empty;

    /// <summary>The message field's label.</summary>
    [ObservableProperty]
    public partial string MessageLabel { get; set; } = string.Empty;

    /// <summary>The message, as typed.</summary>
    [ObservableProperty]
    public partial string Body { get; set; } = string.Empty;

    /// <summary>The send button's words.</summary>
    [ObservableProperty]
    public partial string SendLabel { get; set; } = string.Empty;

    /// <summary>The cancel button's words.</summary>
    [ObservableProperty]
    public partial string CancelLabel { get; set; } = string.Empty;

    /// <summary>Whether Send works now.</summary>
    [ObservableProperty]
    public partial bool CanSend { get; set; }

    /// <summary>Whether the message is on its way.</summary>
    [ObservableProperty]
    public partial bool Sending { get; set; }

    /// <summary>Whether the fields take input: not while sending.</summary>
    [ObservableProperty]
    public partial bool Editable { get; set; } = true;

    /// <summary>Why the last send failed, or empty.</summary>
    [ObservableProperty]
    public partial string Failure { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="Failure"/>.</summary>
    [ObservableProperty]
    public partial bool HasFailure { get; set; }

    internal void Attach(PageContext context) => _context = context;

    /// <summary>
    /// Shows the core's form, or closes it. A field takes the core's value only
    /// when it is newer than what was typed (<see cref="TextEcho"/>): writing
    /// back what was just typed could only move the caret.
    /// </summary>
    internal void Show(ComposeView? view)
    {
        if (view is null)
        {
            IsOpen = false;
            return;
        }
        _filling = true;
        if (_queryEcho.Write(view.Query, Query))
        {
            Query = view.Query;
        }
        if (_subjectEcho.Write(view.Subject, Subject))
        {
            Subject = view.Subject;
        }
        if (_bodyEcho.Write(view.Body, Body))
        {
            Body = view.Body;
        }
        Title = view.Title;
        ToLabel = view.ToLabel;
        Choosing = view.Recipient is null;
        Chosen = !Choosing;
        ContactsLoading = view.ContactsLoading;
        Display.Sync(Suggestions, [.. view.Suggestions.Select(ComposeSuggestionItem.From)]);
        RecentNote = view.RecentNote;
        SuggestionsFailed = view.SuggestionsFailed ?? string.Empty;
        HasSuggestionsFailed = SuggestionsFailed.Length > 0;
        TypedAction = view.TypedAction ?? string.Empty;
        HasTypedAction = TypedAction.Length > 0;
        Recipient = view.Recipient ?? string.Empty;
        ChangeLabel = view.ChangeLabel;
        NoAddressNote = view.NoAddressNote ?? string.Empty;
        HasNoAddressNote = NoAddressNote.Length > 0;
        SendOnLabel = view.SendOnLabel;
        Display.Sync(Channels, [.. view.Channels.Select(channel => new ComposeChannelItem(channel.Name, channel.Address))]);
        HasChannels = Channels.Count > 0;
        Channel = (int)view.Channel;
        RequiresSubject = view.RequiresSubject;
        SubjectLabel = view.SubjectLabel;
        MessageLabel = view.MessageLabel;
        SendLabel = view.SendLabel;
        CancelLabel = view.CancelLabel;
        CanSend = view.CanSend;
        Sending = view.Sending;
        Editable = !view.Sending;
        Failure = Display.Failure(view.Failure);
        HasFailure = Failure.Length > 0;
        _filling = false;
        IsOpen = true;
    }

    partial void OnQueryChanged(string value) => Typed(_queryEcho, value, new ComposeAction.Query(value));

    partial void OnSubjectChanged(string value) => Typed(_subjectEcho, value, new ComposeAction.EditSubject(value));

    partial void OnBodyChanged(string value) => Typed(_bodyEcho, value, new ComposeAction.EditBody(value));

    partial void OnChannelChanged(int value)
    {
        if (value >= 0)
        {
            Typed(null, string.Empty, new ComposeAction.SelectChannel((uint)value));
        }
    }

    private void Typed(TextEcho? echo, string value, ComposeAction action)
    {
        if (_filling || !IsOpen)
        {
            return;
        }
        echo?.Typed(value);
        Send(action);
    }

    /// <summary>An offered contact, chosen.</summary>
    internal void Choose(ComposeSuggestionItem contact) => Send(new ComposeAction.ChooseContact(contact.ContactId));

    /// <summary>Send to what is typed.</summary>
    [RelayCommand]
    private void UseTyped() => Send(new ComposeAction.UseTyped());

    /// <summary>"Change": back to choosing a recipient.</summary>
    [RelayCommand]
    private void ClearRecipient() => Send(new ComposeAction.ClearRecipient());

    /// <summary>"Send". Once per press: the button turns off until the core answers.</summary>
    [RelayCommand]
    private void Submit()
    {
        if (!CanSend)
        {
            return;
        }
        CanSend = false;
        Send(new ComposeAction.Send());
    }

    /// <summary>Cancel, Escape, or the dialog closed. The core refuses while the message is on its way.</summary>
    [RelayCommand]
    private void Cancel() => Send(new ComposeAction.Cancel());

    private void Send(ComposeAction action) => _context?.Send(new UiEvent.Compose(action));
}
