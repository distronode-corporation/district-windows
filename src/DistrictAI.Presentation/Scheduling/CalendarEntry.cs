using DistrictAI.Core.Ffi;

namespace DistrictAI.ViewModels.Scheduling;

/// <summary>
/// A connected calendar account's card: its provider, its address, which of
/// its calendars are checked for conflicts and which receives new bookings,
/// with Choose calendars and Disconnect.
/// </summary>
/// <param name="Id">The account, as its actions name it.</param>
/// <param name="Provider">Its provider's name, the card's heading: "Google Calendar".</param>
/// <param name="Account">Its address.</param>
/// <param name="Conflicts">Which of its calendars are checked for conflicts.</param>
/// <param name="Destination">Which receives new bookings.</param>
/// <param name="AccountLabel">"Account".</param>
/// <param name="ConflictsLabel">"Checked for conflicts".</param>
/// <param name="DestinationLabel">"Receives bookings".</param>
/// <param name="ChooseLabel">"Choose calendars".</param>
/// <param name="DisconnectLabel">"Disconnect".</param>
/// <param name="CanDisconnect">Whether its Disconnect can be pressed: none is on its way.</param>
public sealed record CalendarConnectionEntry(
    string Id,
    string Provider,
    string Account,
    string Conflicts,
    string Destination,
    string AccountLabel,
    string ConflictsLabel,
    string DestinationLabel,
    string ChooseLabel,
    string DisconnectLabel,
    bool CanDisconnect)
{
    /// <summary>What a screen reader says for the card: its provider and its address.</summary>
    public string AccessibleName => Provider + ": " + Account;

    /// <summary>What a screen reader says for Choose calendars: "Choose calendars: ada@example.com".</summary>
    public string ChooseName => ChooseLabel + ": " + Account;

    /// <summary>What a screen reader says for Disconnect: "Disconnect: ada@example.com".</summary>
    public string DisconnectName => DisconnectLabel + ": " + Account;

    /// <summary>The UI Automation id of the card ("CalendarConnection" and the account's id).</summary>
    public string AutomationId => "CalendarConnection" + Id;

    internal static CalendarConnectionEntry From(CalendarConnectionView card, SchedulingCalendarView view) =>
        new(
            card.Id,
            card.Provider,
            card.Account,
            card.Conflicts,
            card.Destination,
            view.AccountLabel,
            view.ConflictsLabel,
            view.DestinationLabel,
            view.ChooseLabel,
            view.DisconnectLabel,
            view.CanDisconnect);

    /// <inheritdoc/>
    public override string ToString() => AccessibleName;
}

/// <summary>A Connect button for a provider the scheduler can connect.</summary>
/// <param name="Provider">The provider, as the core names it: "google".</param>
/// <param name="Label">"Connect Google Calendar".</param>
/// <param name="Enabled">Whether it can be pressed: no sign-in is on its way.</param>
public sealed record CalendarProviderEntry(string Provider, string Label, bool Enabled)
{
    /// <summary>The UI Automation id of the button ("CalendarConnect" and the provider).</summary>
    public string AutomationId => "CalendarConnect" + Provider;

    /// <inheritdoc/>
    public override string ToString() => Label;
}

/// <summary>A calendar of an account, with its switch for conflicts.</summary>
/// <param name="Id">The calendar, as its switch names it.</param>
/// <param name="Name">Its name.</param>
/// <param name="ChecksConflicts">Whether it is checked for conflicts.</param>
/// <param name="Enabled">Whether its switch can be changed.</param>
/// <param name="ConflictsLabel">"Checked for conflicts".</param>
public sealed record CalendarRowEntry(string Id, string Name, bool ChecksConflicts, bool Enabled, string ConflictsLabel)
{
    /// <summary>What a screen reader says for its switch: "Checked for conflicts: Holidays".</summary>
    public string ConflictsName => ConflictsLabel + ": " + Name;

    /// <inheritdoc/>
    public override string ToString() => Name;
}
