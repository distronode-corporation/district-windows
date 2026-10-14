using DistrictAI.Core.Ffi;

namespace DistrictAI.ViewModels.Scheduling;

/// <summary>
/// A section of the booking scheduler's administration: a row of the booking
/// pages screen, and an entry of the section navigation over each section.
/// </summary>
/// <param name="Section">The section it opens.</param>
/// <param name="Title">Its title, the web dashboard's name for it.</param>
/// <param name="Subtitle">What the section holds.</param>
/// <param name="Selected">Whether it is the section showing.</param>
public sealed record SchedulingSectionEntry(SchedulingAdminSection Section, string Title, string Subtitle, bool Selected)
{
    /// <summary>What a screen reader says for the row: its title, then what it holds.</summary>
    public string AccessibleName => Title + ". " + Subtitle;

    /// <summary>
    /// The UI Automation id of the booking pages screen's row title
    /// ("SchedulingSectionBookings"): the same in every language, for the UI tests.
    /// </summary>
    public string AutomationId => $"SchedulingSection{Section}";

    /// <summary>The UI Automation id of the section navigation's entry ("SchedulingTabBookings").</summary>
    public string TabAutomationId => $"SchedulingTab{Section}";

    internal static SchedulingSectionEntry From(SchedulingSectionEntryView view) =>
        new(view.Section, view.Title, view.Subtitle, view.Selected);

    /// <summary>The event that opens <paramref name="section"/>.</summary>
    internal static UiEvent Open(SchedulingAdminSection section) =>
        new UiEvent.SchedulingAdmin(new SchedulingAdminAction.Open(section));

    /// <inheritdoc/>
    public override string ToString() => AccessibleName;
}
