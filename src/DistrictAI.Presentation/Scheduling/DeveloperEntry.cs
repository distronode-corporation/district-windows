using DistrictAI.Core.Ffi;

namespace DistrictAI.ViewModels.Scheduling;

/// <summary>A tab of Developer.</summary>
/// <param name="Tab">Which.</param>
/// <param name="Label">"API keys".</param>
public sealed record DeveloperTabEntry(DeveloperTabView Tab, string Label)
{
    /// <summary>The UI Automation id of its button ("DeveloperTab" and the tab).</summary>
    public string AutomationId => "DeveloperTab" + Tab;

    /// <inheritdoc/>
    public override string ToString() => Label;
}

/// <summary>An API key, with Revoke for a member who may change things; never the key itself.</summary>
/// <param name="Id">The key, as its Revoke names it.</param>
/// <param name="Name">Its name.</param>
/// <param name="Line">When it was made and last used.</param>
/// <param name="OffersRevoke">Whether Revoke shows.</param>
/// <param name="CanRevoke">Whether Revoke can be pressed: no removal is on its way.</param>
/// <param name="RevokeLabel">"Revoke key".</param>
public sealed record ApiKeyEntry(string Id, string Name, string Line, bool OffersRevoke, bool CanRevoke, string RevokeLabel)
{
    /// <summary>What a screen reader says for the row.</summary>
    public string AccessibleName => Name + ", " + Line;

    /// <summary>What a screen reader says for Revoke: "Revoke key: Zapier".</summary>
    public string RevokeName => RevokeLabel + ": " + Name;

    /// <summary>The UI Automation id of the row ("DeveloperKey" and the key's id).</summary>
    public string AutomationId => "DeveloperKey" + Id;

    /// <inheritdoc/>
    public override string ToString() => AccessibleName;
}

/// <summary>An app connected with OAuth, with Revoke for a member who may change things.</summary>
/// <param name="Id">The connection, as its Revoke names it.</param>
/// <param name="Name">The app's name.</param>
/// <param name="LastUsed">When it last reached the bookings, or "Never".</param>
/// <param name="OffersRevoke">Whether Revoke shows.</param>
/// <param name="CanRevoke">Whether Revoke can be pressed: no removal is on its way.</param>
/// <param name="RevokeLabel">"Revoke access".</param>
public sealed record ConnectedAppEntry(string Id, string Name, string LastUsed, bool OffersRevoke, bool CanRevoke, string RevokeLabel)
{
    /// <summary>What a screen reader says for the row.</summary>
    public string AccessibleName => Name + ", " + LastUsed;

    /// <summary>What a screen reader says for Revoke: "Revoke access: Raycast".</summary>
    public string RevokeName => RevokeLabel + ": " + Name;

    /// <summary>The UI Automation id of the row ("DeveloperApp" and the connection's id).</summary>
    public string AutomationId => "DeveloperApp" + Id;

    /// <inheritdoc/>
    public override string ToString() => AccessibleName;
}

/// <summary>A webhook: where it posts, what it sends, whether it is on, with Deliveries, and Edit and Delete for a member who may change things.</summary>
/// <param name="Id">The webhook, as its actions name it.</param>
/// <param name="Url">Where its deliveries go.</param>
/// <param name="Events">The events it sends.</param>
/// <param name="StateLabel">"On" or "Off".</param>
/// <param name="Tone">The state's colour.</param>
/// <param name="Open">Whether its deliveries are open.</param>
/// <param name="OffersWrites">Whether Edit and Delete show.</param>
/// <param name="CanDelete">Whether Delete can be pressed: no removal is on its way.</param>
/// <param name="DeliveriesLabel">"Deliveries".</param>
/// <param name="EditLabel">"Edit".</param>
/// <param name="DeleteLabel">"Delete webhook".</param>
public sealed record WebhookEntry(
    string Id,
    string Url,
    string Events,
    string StateLabel,
    RunTone Tone,
    bool Open,
    bool OffersWrites,
    bool CanDelete,
    string DeliveriesLabel,
    string EditLabel,
    string DeleteLabel)
{
    /// <summary>Whether it is on (the success colour).</summary>
    public bool IsSuccess => Tone == RunTone.Success;

    /// <summary>Off (the quiet colour).</summary>
    public bool IsNeutral => !IsSuccess;

    /// <summary>What a screen reader says for the row.</summary>
    public string AccessibleName => Url + ", " + StateLabel + ", " + Events;

    /// <summary>What a screen reader says for Deliveries.</summary>
    public string DeliveriesName => DeliveriesLabel + ": " + Url;

    /// <summary>What a screen reader says for Edit.</summary>
    public string EditName => EditLabel + ": " + Url;

    /// <summary>What a screen reader says for Delete.</summary>
    public string DeleteName => DeleteLabel + ": " + Url;

    /// <summary>The UI Automation id of the row ("DeveloperWebhook" and the webhook's id).</summary>
    public string AutomationId => "DeveloperWebhook" + Id;

    /// <inheritdoc/>
    public override string ToString() => AccessibleName;
}

/// <summary>A delivery of a webhook: when it was tried, what it carried and what it came to.</summary>
/// <param name="Id">The delivery.</param>
/// <param name="When">When it was last tried, or "Not tried yet".</param>
/// <param name="Event">The event it carried.</param>
/// <param name="Outcome">What it came to: "Delivered", "No answer".</param>
/// <param name="Tone">The outcome's colour.</param>
public sealed record DeliveryEntry(string Id, string When, string Event, string Outcome, RunTone Tone)
{
    /// <summary>Whether it went through (the success colour).</summary>
    public bool IsSuccess => Tone == RunTone.Success;

    /// <summary>Whether it failed (the critical colour).</summary>
    public bool IsDanger => Tone == RunTone.Danger;

    /// <summary>Anything else (the quiet colour).</summary>
    public bool IsNeutral => !IsSuccess && !IsDanger;

    /// <summary>What a screen reader says for the row.</summary>
    public string AccessibleName => When + ", " + Event + ", " + Outcome;

    /// <summary>The UI Automation id of the row ("DeveloperDelivery" and the delivery's id).</summary>
    public string AutomationId => "DeveloperDelivery" + Id;

    internal static DeliveryEntry From(DeliveryRowView row) => new(row.Id, row.When, row.Event, row.Outcome, row.Tone);

    /// <inheritdoc/>
    public override string ToString() => AccessibleName;
}

/// <summary>An event or a field of the webhook form, with its switch.</summary>
/// <param name="Value">Its wire name, which the switch shows and sends.</param>
/// <param name="On">Whether it is chosen.</param>
/// <param name="Mark">"Personal data" for a person's own data, or empty.</param>
/// <param name="Enabled">Whether it can be changed: no save is on its way.</param>
public sealed record ToggleEntry(string Value, bool On, string Mark, bool Enabled)
{
    /// <summary>Whether it carries a <see cref="Mark"/>.</summary>
    public bool HasMark => Mark.Length > 0;

    /// <summary>What a screen reader says for its switch: the value, and its mark.</summary>
    public string AccessibleName => HasMark ? Value + ", " + Mark : Value;

    /// <summary>The UI Automation id of an event's switch ("DeveloperWebhookEvent" and the event).</summary>
    public string EventAutomationId => "DeveloperWebhookEvent" + Value;

    /// <summary>The UI Automation id of a field's switch ("DeveloperWebhookField" and the field).</summary>
    public string FieldAutomationId => "DeveloperWebhookField" + Value;

    internal static ToggleEntry From(ToggleRowView row, bool enabled) => new(row.Value, row.On, row.Mark ?? string.Empty, enabled);

    /// <inheritdoc/>
    public override string ToString() => AccessibleName;
}
