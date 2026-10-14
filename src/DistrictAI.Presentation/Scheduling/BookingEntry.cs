using DistrictAI.Core.Ffi;

namespace DistrictAI.ViewModels.Scheduling;

/// <summary>A booking in the list, with the actions the core offers on it.</summary>
/// <param name="Id">The booking, which a click opens.</param>
/// <param name="When">When it starts, in the scheduler profile's zone.</param>
/// <param name="Subtitle">Who, what and with whom.</param>
/// <param name="Email">The first attendee's address, or empty.</param>
/// <param name="StatusLabel">The status badge's words.</param>
/// <param name="Tone">The badge's colour.</param>
/// <param name="CanCancel">Whether "Cancel booking" is offered.</param>
/// <param name="CanReschedule">Whether "Move booking" is offered.</param>
/// <param name="CanReassign">Whether "Change host" is offered.</param>
/// <param name="CancelLabel">"Cancel booking".</param>
/// <param name="RescheduleLabel">"Move booking".</param>
/// <param name="ReassignLabel">"Change host".</param>
public sealed record BookingEntry(
    string Id,
    string When,
    string Subtitle,
    string Email,
    string StatusLabel,
    RunTone Tone,
    bool CanCancel,
    bool CanReschedule,
    bool CanReassign,
    string CancelLabel,
    string RescheduleLabel,
    string ReassignLabel)
{
    /// <summary>Whether there is an <see cref="Email"/>.</summary>
    public bool HasEmail => Email.Length > 0;

    /// <summary>Whether any action is offered on it.</summary>
    public bool HasActions => CanCancel || CanReschedule || CanReassign;

    /// <summary>Whether the status is a good one (the success colour).</summary>
    public bool IsSuccess => Tone == RunTone.Success;

    /// <summary>Whether the status is a warning (the caution colour).</summary>
    public bool IsWarning => Tone == RunTone.Warning;

    /// <summary>Whether the status is a bad one (the critical colour).</summary>
    public bool IsDanger => Tone == RunTone.Danger;

    /// <summary>Any other status (the quiet colour).</summary>
    public bool IsNeutral => !IsSuccess && !IsWarning && !IsDanger;

    /// <summary>What a screen reader says for the row.</summary>
    public string AccessibleName => When + ", " + Subtitle + ", " + StatusLabel;

    /// <summary>The UI Automation id of the row ("BookingRow" and the booking's id).</summary>
    public string AutomationId => "BookingRow" + Id;

    internal static BookingEntry From(BookingRowView row, SchedulingBookingsView list) =>
        new(
            row.Id,
            row.When,
            row.Subtitle,
            row.Email,
            row.StatusLabel,
            row.Tone,
            row.CanCancel,
            row.CanReschedule,
            row.CanReassign,
            list.CancelLabel,
            list.RescheduleLabel,
            list.ReassignLabel);

    /// <inheritdoc/>
    public override string ToString() => AccessibleName;
}

/// <summary>One choice in a dialog's list: a free time, or a host.</summary>
/// <param name="Value">What is sent when it is chosen.</param>
/// <param name="Label">What it reads.</param>
/// <param name="Selected">Whether it is chosen.</param>
public sealed record ChoiceEntry(string Value, string Label, bool Selected)
{
    internal static ChoiceEntry From(ChoiceRowView choice) => new(choice.Value, choice.Label, choice.Selected);

    /// <inheritdoc/>
    public override string ToString() => Label;
}
