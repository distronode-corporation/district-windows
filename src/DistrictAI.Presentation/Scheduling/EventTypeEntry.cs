using DistrictAI.Core.Ffi;
using DistrictAI.L10n;

namespace DistrictAI.ViewModels.Scheduling;

/// <summary>An event type in the list, which a click opens.</summary>
/// <param name="Slug">The event type, by its booking page's slug.</param>
/// <param name="Name">Its name.</param>
/// <param name="Subtitle">"30 min · Phone".</param>
/// <param name="StateLabel">The state badge's words: active, inactive or archived.</param>
/// <param name="Tone">The badge's colour.</param>
/// <param name="BookingPage">Its public booking page's address, or "Not public".</param>
public sealed record EventTypeEntry(string Slug, string Name, string Subtitle, string StateLabel, RunTone Tone, string BookingPage)
{
    /// <summary>Whether the state is a good one (the success colour).</summary>
    public bool IsSuccess => Tone == RunTone.Success;

    /// <summary>Any other state (the quiet colour).</summary>
    public bool IsNeutral => !IsSuccess;

    /// <summary>What a screen reader says for the row.</summary>
    public string AccessibleName => Name + ", " + Subtitle + ", " + StateLabel + ", " + BookingPage;

    /// <summary>The UI Automation id of the row ("EventTypeRow" and the slug).</summary>
    public string AutomationId => "EventTypeRow" + Slug;

    internal static EventTypeEntry From(EventTypeRowView row) =>
        new(row.Slug, row.Name, row.Subtitle, row.StateLabel, row.Tone, row.BookingPage);

    /// <inheritdoc/>
    public override string ToString() => AccessibleName;
}

/// <summary>A host in the hosts dialog: who, their role and their priority, as edited.</summary>
/// <param name="UserId">Their scheduler user id.</param>
/// <param name="Name">Their name, or their address for a blank one.</param>
/// <param name="Email">Their address.</param>
/// <param name="Roles">The roles' labels, in the core's order.</param>
/// <param name="RoleIndex">Which of <paramref name="Roles"/> is theirs, or -1 for one this build does not know.</param>
/// <param name="Priority">Their place in the rotation, as typed.</param>
/// <param name="PriorityLabel">"Priority".</param>
/// <param name="RemoveLabel">"Remove".</param>
public sealed record HostEntry(
    string UserId,
    string Name,
    string Email,
    IReadOnlyList<string> Roles,
    int RoleIndex,
    string Priority,
    string PriorityLabel,
    string RemoveLabel)
{
    /// <summary>What a screen reader says for the row.</summary>
    public string AccessibleName => Name + ", " + Email;

    /// <summary>What a screen reader says for the role's choice: "Role: Ann".</summary>
    public string RoleName => S.SchedulingHostRole + ": " + Name;

    /// <summary>What a screen reader says for the priority's box: "Priority: Ann".</summary>
    public string PriorityName => PriorityLabel + ": " + Name;

    /// <summary>What a screen reader says for the Remove button: "Remove: Ann".</summary>
    public string RemoveName => RemoveLabel + ": " + Name;

    /// <summary>The UI Automation id of the row ("EventTypeHost" and the user id).</summary>
    public string AutomationId => "EventTypeHost" + UserId;

    /// <inheritdoc/>
    public override string ToString() => AccessibleName;
}

/// <summary>Someone who could be added as a host, with Add.</summary>
/// <param name="UserId">Their scheduler user id.</param>
/// <param name="Name">Their name, or their address for a blank one.</param>
/// <param name="AddLabel">"Add".</param>
public sealed record CandidateEntry(string UserId, string Name, string AddLabel)
{
    /// <summary>What a screen reader says for the Add button: "Add: Sam".</summary>
    public string AddName => AddLabel + ": " + Name;

    /// <inheritdoc/>
    public override string ToString() => Name;
}

/// <summary>A booking question in the questions dialog, with Edit and Delete.</summary>
/// <param name="Id">The question.</param>
/// <param name="Label">What it asks.</param>
/// <param name="Value">Its type, and required when it is.</param>
/// <param name="EditLabel">"Edit".</param>
/// <param name="DeleteLabel">"Delete".</param>
public sealed record QuestionEntry(string Id, string Label, string Value, string EditLabel, string DeleteLabel)
{
    /// <summary>What a screen reader says for the row.</summary>
    public string AccessibleName => Label + ": " + Value;

    /// <summary>What a screen reader says for its Edit button.</summary>
    public string EditName => EditLabel + ": " + Label;

    /// <summary>What a screen reader says for its Delete button.</summary>
    public string DeleteName => DeleteLabel + ": " + Label;

    /// <summary>The UI Automation id of the row ("EventTypeQuestion" and the id).</summary>
    public string AutomationId => "EventTypeQuestion" + Id;

    /// <inheritdoc/>
    public override string ToString() => AccessibleName;
}

/// <summary>A select question's choice, with Remove.</summary>
/// <param name="Option">The choice.</param>
/// <param name="RemoveLabel">"Remove".</param>
public sealed record OptionEntry(string Option, string RemoveLabel)
{
    /// <summary>What a screen reader says for the Remove button: "Remove: Toronto".</summary>
    public string RemoveName => RemoveLabel + ": " + Option;

    /// <inheritdoc/>
    public override string ToString() => Option;
}
