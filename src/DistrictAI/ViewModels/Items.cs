using DistrictAI.Core.Ffi;
using Microsoft.UI.Xaml;

namespace DistrictAI.ViewModels;

// The rows the lists show. Each is a record of plain values copied from the
// core's view, so two snapshots that show the same row compare equal and the
// list leaves it alone (Display.Sync). ToString is the row's accessible name,
// which a list item falls back to when nothing else names it.

/// <summary>A label and its value: a metric, a fact about a call or a contact.</summary>
/// <param name="Label">What the value is.</param>
/// <param name="Value">The value, as the core formats it.</param>
public sealed record FactItem(string Label, string Value)
{
    /// <summary>What a screen reader says for it.</summary>
    public string AccessibleName => Label + ": " + Value;

    internal static FactItem From(FactView fact) => new(fact.Label, fact.Value);

    /// <inheritdoc/>
    public override string ToString() => AccessibleName;
}

/// <summary>A call in a list.</summary>
/// <param name="CallId">The call.</param>
/// <param name="Title">Who called or was called.</param>
/// <param name="Detail">Direction, outcome and duration.</param>
/// <param name="Summary">The short AI summary, or empty.</param>
/// <param name="When">When it started, in local time.</param>
public sealed record CallRowItem(string CallId, string Title, string Detail, string Summary, string When)
{
    /// <summary>Whether there is a <see cref="Summary"/>.</summary>
    public bool HasSummary => Summary.Length > 0;

    /// <summary>What a screen reader says for the row.</summary>
    public string AccessibleName => string.Join(", ", new[] { Title, Detail, When }.Where(part => part.Length > 0));

    internal static CallRowItem From(CallRowView row) =>
        new(row.CallId, row.Title, row.Detail, row.Summary ?? string.Empty, Display.When(row.StartedAt));

    /// <inheritdoc/>
    public override string ToString() => AccessibleName;
}

/// <summary>A conversation in the inbox.</summary>
/// <param name="ThreadKey">The conversation.</param>
/// <param name="Title">Who it is with.</param>
/// <param name="Preview">The latest message.</param>
/// <param name="ChannelLabel">Text message, email, and so on.</param>
/// <param name="Unread">Whether it has unread messages.</param>
/// <param name="When">When the latest message arrived, in local time.</param>
/// <param name="CanOpen">Whether this build can open it.</param>
public sealed record ThreadRowItem(string ThreadKey, string Title, string Preview, string ChannelLabel, bool Unread, string When, bool CanOpen)
{
    /// <summary>A conversation this build cannot open is shown faded.</summary>
    public double RowOpacity => CanOpen ? 1.0 : 0.6;

    /// <summary>What a screen reader says for the row.</summary>
    public string AccessibleName =>
        string.Join(", ", new[] { Title, Unread ? "unread" : string.Empty, ChannelLabel, Preview, When }.Where(part => part.Length > 0));

    internal static ThreadRowItem From(ThreadRowView row) =>
        new(row.ThreadKey, row.Title, row.Preview, row.ChannelLabel, row.Unread, Display.When(row.LastAt), row.CanOpen);

    /// <inheritdoc/>
    public override string ToString() => AccessibleName;
}

/// <summary>A message that matches the search.</summary>
/// <param name="ThreadKey">The conversation it is in.</param>
/// <param name="Title">Who the conversation is with.</param>
/// <param name="Snippet">The matching text.</param>
/// <param name="When">When it was sent, in local time.</param>
/// <param name="CanOpen">Whether this build can open its conversation.</param>
public sealed record SearchHitItem(string ThreadKey, string Title, string Snippet, string When, bool CanOpen)
{
    /// <summary>A match in a conversation this build cannot open is shown faded.</summary>
    public double RowOpacity => CanOpen ? 1.0 : 0.6;

    /// <summary>What a screen reader says for the row.</summary>
    public string AccessibleName => string.Join(", ", new[] { Title, Snippet, When }.Where(part => part.Length > 0));

    internal static SearchHitItem From(SearchHitView hit) =>
        new(hit.ThreadKey, hit.Title, hit.Snippet, Display.When(hit.At), hit.CanOpen);

    /// <inheritdoc/>
    public override string ToString() => AccessibleName;
}

/// <summary>One item of a conversation: a message, a call, or a note.</summary>
public sealed record TimelineItem
{
    /// <summary>The item, as the core names it.</summary>
    public required string Id { get; init; }

    /// <summary>A message this workspace sent.</summary>
    public bool IsOutboundMessage { get; init; }

    /// <summary>A message this workspace received.</summary>
    public bool IsInboundMessage { get; init; }

    /// <summary>Who wrote the message, or empty.</summary>
    public string Author { get; init; } = string.Empty;

    /// <summary>The message's text.</summary>
    public string Body { get; init; } = string.Empty;

    /// <summary>The email's subject, or empty.</summary>
    public string Subject { get; init; } = string.Empty;

    /// <summary>How the message went: text message, email, and its delivery ("Delivered", "Not delivered").</summary>
    public string Meta { get; init; } = string.Empty;

    /// <summary>The names of the message's attachments, one per line, or empty.</summary>
    public string Attachments { get; init; } = string.Empty;

    /// <summary>A call in the conversation.</summary>
    public bool IsCallEvent { get; init; }

    /// <summary>The call's line: direction, outcome and length.</summary>
    public string CallTitle { get; init; } = string.Empty;

    /// <summary>The label over the call's AI summary.</summary>
    public string SummaryLabel { get; init; } = string.Empty;

    /// <summary>The call's AI summary, or empty.</summary>
    public string SummaryText { get; init; } = string.Empty;

    /// <summary>Anything else, as one line.</summary>
    public string NoteText { get; init; } = string.Empty;

    /// <summary>When it happened, in local time.</summary>
    public string When { get; init; } = string.Empty;

    /// <summary>Whether, and how, the item can be reported.</summary>
    public ReportAvailability Report { get; init; }

    /// <summary>Whether the Report button can be pressed now (no report on its way).</summary>
    public bool ReportEnabled { get; init; }

    /// <summary>Whether there is a <see cref="Subject"/>.</summary>
    public bool HasSubject => Subject.Length > 0;

    /// <summary>Whether there is a <see cref="Meta"/>.</summary>
    public bool HasMeta => Meta.Length > 0;

    /// <summary>Whether there is an <see cref="Author"/>.</summary>
    public bool HasAuthor => Author.Length > 0;

    /// <summary>Whether there are <see cref="Attachments"/>.</summary>
    public bool HasAttachments => Attachments.Length > 0;

    /// <summary>Whether there is a <see cref="SummaryText"/>.</summary>
    public bool HasSummary => SummaryText.Length > 0;

    /// <summary>Whether this is a note.</summary>
    public bool IsNote => NoteText.Length > 0;

    /// <summary>Whether there is a time to show.</summary>
    public bool HasWhen => When.Length > 0;

    /// <summary>Sent messages sit on the right, everything else on the left.</summary>
    public HorizontalAlignment Alignment => IsOutboundMessage ? HorizontalAlignment.Right : HorizontalAlignment.Left;

    /// <summary>The Report button's words.</summary>
    public string ReportLabel => Display.ReportLabel(Report);

    /// <summary>Whether the item has a Report button.</summary>
    public bool ReportVisible => ReportLabel.Length > 0;

    /// <summary>What a screen reader says for the item.</summary>
    public string AccessibleName => string.Join(
        ", ",
        new[]
        {
            IsOutboundMessage ? "You" : Author,
            Subject,
            Body,
            Attachments,
            Meta,
            CallTitle,
            SummaryText.Length > 0 ? SummaryLabel + ": " + SummaryText : string.Empty,
            NoteText,
            When,
        }.Where(part => part.Length > 0));

    internal static TimelineItem From(TimelineItemView item, bool reportEnabled)
    {
        var when = Display.When(item.At);
        return item.Kind switch
        {
            TimelineKind.Message message => new TimelineItem
            {
                Id = item.Id,
                IsOutboundMessage = message.Outbound,
                IsInboundMessage = !message.Outbound,
                Author = message.Author ?? string.Empty,
                Subject = message.Subject ?? string.Empty,
                Body = message.Body,
                Attachments = message.Attachments.Length > 0
                    ? string.Join(Environment.NewLine, message.Attachments)
                    : message.AttachmentsLabel ?? string.Empty,
                Meta = string.Join(" \u00B7 ", new[] { message.ChannelLabel, message.Delivery ?? string.Empty }.Where(part => part.Length > 0)),
                When = when,
                Report = item.Report,
                ReportEnabled = reportEnabled,
            },
            TimelineKind.CallEvent call => new TimelineItem
            {
                Id = item.Id,
                IsCallEvent = true,
                CallTitle = call.Title,
                SummaryLabel = call.Summary?.Label ?? string.Empty,
                SummaryText = call.Summary?.Text ?? string.Empty,
                When = when,
                Report = item.Report,
                ReportEnabled = reportEnabled,
            },
            TimelineKind.Note note => new TimelineItem
            {
                Id = item.Id,
                NoteText = note.Text,
                When = when,
                Report = item.Report,
                ReportEnabled = reportEnabled,
            },
            // A kind a later core adds: its time alone, until this build knows it.
            _ => new TimelineItem { Id = item.Id, When = when },
        };
    }

    /// <inheritdoc/>
    public override string ToString() => AccessibleName;
}

/// <summary>A contact in the list.</summary>
/// <param name="ContactId">The contact.</param>
/// <param name="Name">Their name, or how they are known.</param>
/// <param name="Detail">The line under it.</param>
public sealed record ContactRowItem(string ContactId, string Name, string Detail)
{
    /// <summary>Whether there is a <see cref="Detail"/>.</summary>
    public bool HasDetail => Detail.Length > 0;

    /// <summary>What a screen reader says for the row.</summary>
    public string AccessibleName => Detail.Length > 0 ? Name + ", " + Detail : Name;

    internal static ContactRowItem From(ContactRowView row) => new(row.ContactId, row.Name, row.Detail);

    /// <inheritdoc/>
    public override string ToString() => AccessibleName;
}

/// <summary>An installation signed in to the account.</summary>
/// <param name="DeviceId">The installation.</param>
/// <param name="Name">Its name.</param>
/// <param name="Detail">Its platform and when it was last active.</param>
/// <param name="IsThisDevice">Whether it is this computer.</param>
/// <param name="CanSignOut">Whether its Sign out button can be pressed now.</param>
public sealed record DeviceRowItem(string DeviceId, string Name, string Detail, bool IsThisDevice, bool CanSignOut)
{
    /// <summary>The Sign out button's accessible name.</summary>
    public string SignOutName => "Sign out " + Name;

    /// <summary>What a screen reader says for the row.</summary>
    public string AccessibleName => IsThisDevice ? Name + ", this device, " + Detail : Name + ", " + Detail;

    internal static DeviceRowItem From(DeviceRowView row, bool busy)
    {
        // The time in this computer's zone when the core has one; otherwise
        // the service's words ("Signed in recently").
        var when = Display.When(row.LastActiveAt);
        var lastActive = when.Length > 0 ? "Last active " + when : row.LastActiveLabel;
        return new(row.DeviceId, row.Name, row.Platform + " \u00B7 " + lastActive, row.IsThisDevice, !busy);
    }

    /// <inheritdoc/>
    public override string ToString() => AccessibleName;
}

/// <summary>A workspace the user can switch to.</summary>
/// <param name="Id">The workspace.</param>
/// <param name="Name">Its name.</param>
/// <param name="RoleLabel">The user's role in it.</param>
public sealed record WorkspaceItem(string Id, string Name, string RoleLabel)
{
    internal static WorkspaceItem From(WorkspaceEntryView entry) => new(entry.Id, entry.Name, entry.RoleLabel);

    /// <inheritdoc/>
    public override string ToString() => RoleLabel.Length > 0 ? Name + ", " + RoleLabel : Name;
}
