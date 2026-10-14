using DistrictAI.Core.Ffi;

namespace DistrictAI.ViewModels.Scheduling;

/// <summary>
/// A person on the team: a workspace member, a scheduler user, or both, with
/// where they stand with the scheduler, and Archive account when it is
/// offered.
/// </summary>
/// <param name="Email">Their address, which keys the row.</param>
/// <param name="Name">Their name.</param>
/// <param name="Subtitle">Their address, and their workspace role when it was read.</param>
/// <param name="StateLabel">Where they stand: "Host", "Not set up yet", "Archived".</param>
/// <param name="Tone">The state's colour.</param>
/// <param name="ArchiveUserId">Their scheduler account, when Archive account is offered for it.</param>
/// <param name="ArchiveLabel">"Archive account".</param>
public sealed record TeamMemberEntry(
    string Email,
    string Name,
    string Subtitle,
    string StateLabel,
    RunTone Tone,
    string? ArchiveUserId,
    string ArchiveLabel)
{
    /// <summary>Whether the state is a good one (the success colour).</summary>
    public bool IsSuccess => Tone == RunTone.Success;

    /// <summary>Any other state (the quiet colour).</summary>
    public bool IsNeutral => !IsSuccess;

    /// <summary>Whether Archive account shows.</summary>
    public bool OffersArchive => ArchiveUserId is not null;

    /// <summary>What a screen reader says for the row.</summary>
    public string AccessibleName => Name + ", " + Subtitle + ", " + StateLabel;

    /// <summary>What a screen reader says for Archive account: "Archive account: Ann".</summary>
    public string ArchiveName => ArchiveLabel + ": " + Name;

    /// <summary>The UI Automation id of the row ("TeamMember" and the address).</summary>
    public string AutomationId => "TeamMember" + Email;

    internal static TeamMemberEntry From(TeamMemberRowView row, string archiveLabel) =>
        new(row.Email, row.Name, row.Subtitle, row.StateLabel, row.Tone, row.ArchiveUserId, archiveLabel);

    /// <inheritdoc/>
    public override string ToString() => AccessibleName;
}

/// <summary>A team, with Edit and Hosts for a member who may change things.</summary>
/// <param name="Id">The team, as its actions name it.</param>
/// <param name="Name">Its name.</param>
/// <param name="MemberCount">How many hosts it has: "2 members".</param>
/// <param name="OffersWrites">Whether Edit and Hosts show.</param>
/// <param name="EditLabel">"Edit".</param>
/// <param name="HostsLabel">"Hosts".</param>
public sealed record TeamEntry(string Id, string Name, string MemberCount, bool OffersWrites, string EditLabel, string HostsLabel)
{
    /// <summary>What a screen reader says for the row.</summary>
    public string AccessibleName => Name + ", " + MemberCount;

    /// <summary>What a screen reader says for Edit: "Edit: Sales".</summary>
    public string EditName => EditLabel + ": " + Name;

    /// <summary>What a screen reader says for Hosts: "Hosts: Sales".</summary>
    public string HostsName => HostsLabel + ": " + Name;

    /// <summary>The UI Automation id of the row ("TeamRow" and the team's id).</summary>
    public string AutomationId => "TeamRow" + Id;

    /// <inheritdoc/>
    public override string ToString() => AccessibleName;
}

/// <summary>A host of a team, with their routing priority, Save priority and Remove.</summary>
/// <param name="UserId">Their scheduler user id.</param>
/// <param name="Name">Their name, or their address for a blank one.</param>
/// <param name="Email">Their address.</param>
/// <param name="Priority">Their routing priority, as typed.</param>
/// <param name="PriorityLabel">"Routing priority".</param>
/// <param name="SaveLabel">"Save priority".</param>
/// <param name="RemoveLabel">"Remove".</param>
public sealed record TeamHostEntry(
    string UserId,
    string Name,
    string Email,
    string Priority,
    string PriorityLabel,
    string SaveLabel,
    string RemoveLabel)
{
    /// <summary>What a screen reader says for the row.</summary>
    public string AccessibleName => Name + ", " + Email;

    /// <summary>What a screen reader says for the priority's box: "Routing priority: Ann".</summary>
    public string PriorityName => PriorityLabel + ": " + Name;

    /// <summary>What a screen reader says for Save priority: "Save priority: Ann".</summary>
    public string SaveName => SaveLabel + ": " + Name;

    /// <summary>What a screen reader says for Remove: "Remove: Ann".</summary>
    public string RemoveName => RemoveLabel + ": " + Name;

    /// <summary>The UI Automation id of the row ("TeamHost" and the user id).</summary>
    public string AutomationId => "TeamHost" + UserId;

    /// <inheritdoc/>
    public override string ToString() => AccessibleName;
}

/// <summary>An upcoming booking that stands in the way of an archive.</summary>
/// <param name="Id">The booking.</param>
/// <param name="EventType">Its event type's name.</param>
/// <param name="Attendee">Who booked it.</param>
/// <param name="When">When it starts, in this computer's time zone.</param>
public sealed record UpcomingBookingEntry(string Id, string EventType, string Attendee, string When)
{
    /// <summary>What a screen reader says for the row.</summary>
    public string AccessibleName => EventType + ", " + Attendee + ", " + When;

    internal static UpcomingBookingEntry From(UpcomingBookingView booking) =>
        new(booking.Id, booking.EventType, booking.Attendee, Display.When(booking.StartAt));

    /// <inheritdoc/>
    public override string ToString() => AccessibleName;
}
