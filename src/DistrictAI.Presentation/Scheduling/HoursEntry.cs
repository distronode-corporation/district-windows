using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using DistrictAI.Core.Ffi;
using DistrictAI.L10n;

namespace DistrictAI.ViewModels.Scheduling;

/// <summary>
/// A day of the week's editor: its windows, Add range, and Copy to all
/// weekdays where it is offered. Its windows are built again only when one
/// comes or goes (or the week is read again), so a time being typed keeps
/// its box; their faults change in place.
/// </summary>
public sealed partial class HoursDayEntry : ObservableObject
{
    private ulong[] _keys = [];

    /// <summary>A day, 0 for Monday, named <paramref name="name"/>.</summary>
    internal HoursDayEntry(uint day, string name, string addRangeLabel, string copyLabel, string notBookable)
    {
        Day = day;
        Name = name;
        AddRangeLabel = addRangeLabel;
        CopyLabel = copyLabel;
        NotBookable = notBookable;
    }

    /// <summary>Which day, 0 for Monday.</summary>
    public uint Day { get; }

    /// <summary>"Monday".</summary>
    public string Name { get; }

    /// <summary>"Add range".</summary>
    public string AddRangeLabel { get; }

    /// <summary>"Copy to all weekdays".</summary>
    public string CopyLabel { get; }

    /// <summary>"Not bookable".</summary>
    public string NotBookable { get; }

    /// <summary>What a screen reader says for Add range: "Add range: Monday".</summary>
    public string AddRangeName => AddRangeLabel + ": " + Name;

    /// <summary>What a screen reader says for Copy to all weekdays: "Copy to all weekdays: Monday".</summary>
    public string CopyName => CopyLabel + ": " + Name;

    /// <summary>The UI Automation id of the day ("HoursDay" and the day, 0 for Monday).</summary>
    public string AutomationId => "HoursDay" + Day.ToString(CultureInfo.InvariantCulture);

    /// <summary>The day's windows, as edited.</summary>
    public ObservableCollection<HoursRangeEntry> Ranges { get; } = [];

    /// <summary>Whether it has no window: not bookable.</summary>
    [ObservableProperty]
    public partial bool ShowsNone { get; set; }

    /// <summary>Whether it offers Copy to all weekdays.</summary>
    [ObservableProperty]
    public partial bool OffersCopy { get; set; }

    /// <summary>Brings the day in line with <paramref name="day"/>: its windows built again only when one came or went.</summary>
    internal void Show(HoursDayView day, string toLabel, string removeLabel, string startPlaceholder, string endPlaceholder)
    {
        ulong[] keys = [.. day.Ranges.Select(range => range.Key)];
        if (!keys.SequenceEqual(_keys))
        {
            _keys = keys;
            Ranges.Clear();
            var position = 0;
            foreach (var range in day.Ranges)
            {
                Ranges.Add(new HoursRangeEntry(Day, range.Key, range.Start, range.End, S.SchedulingHoursWindow(Name, ++position), toLabel, removeLabel, startPlaceholder, endPlaceholder));
            }
        }
        for (var i = 0; i < day.Ranges.Length; i++)
        {
            Ranges[i].Error = day.Ranges[i].Error ?? string.Empty;
        }
        ShowsNone = day.Ranges.Length == 0;
        OffersCopy = day.OffersCopy;
    }

    /// <inheritdoc/>
    public override string ToString() => Name;
}

/// <summary>
/// One window of a day, as typed: when it opens, when it closes, Remove, and
/// its fault. The two times are written once, as the window is built; from
/// then on what is typed is the window.
/// </summary>
public sealed partial class HoursRangeEntry : ObservableObject
{
    /// <summary>The window <paramref name="key"/> of the day <paramref name="day"/>, named <paramref name="name"/> ("Monday, range 1").</summary>
    internal HoursRangeEntry(
        uint day,
        ulong key,
        string start,
        string end,
        string name,
        string toLabel,
        string removeLabel,
        string startPlaceholder,
        string endPlaceholder)
    {
        Day = day;
        Key = key;
        Start = start;
        End = end;
        Name = name;
        ToLabel = toLabel;
        RemoveLabel = removeLabel;
        StartPlaceholder = startPlaceholder;
        EndPlaceholder = endPlaceholder;
    }

    /// <summary>Its day, 0 for Monday.</summary>
    public uint Day { get; }

    /// <summary>Which window, as the core names it.</summary>
    public ulong Key { get; }

    /// <summary>When it opens, as last sent.</summary>
    public string Start { get; internal set; }

    /// <summary>When it closes, as last sent.</summary>
    public string End { get; internal set; }

    /// <summary>"Monday, range 1".</summary>
    public string Name { get; }

    /// <summary>"to", between the two times.</summary>
    public string ToLabel { get; }

    /// <summary>"Remove".</summary>
    public string RemoveLabel { get; }

    /// <summary>The opening's placeholder.</summary>
    public string StartPlaceholder { get; }

    /// <summary>The closing's placeholder.</summary>
    public string EndPlaceholder { get; }

    /// <summary>What a screen reader says for the opening's box: "Opens: Monday, range 1".</summary>
    public string StartName => S.SchedulingHoursOpens + ": " + Name;

    /// <summary>What a screen reader says for the closing's box: "Closes: Monday, range 1".</summary>
    public string EndName => S.SchedulingHoursCloses + ": " + Name;

    /// <summary>What a screen reader says for Remove: "Remove: Monday, range 1".</summary>
    public string RemoveName => RemoveLabel + ": " + Name;

    /// <summary>Its fault, or empty.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string Error { get; set; } = string.Empty;

    /// <summary>Whether there is an <see cref="Error"/>.</summary>
    public bool HasError => Error.Length > 0;

    /// <inheritdoc/>
    public override string ToString() => Name;
}

/// <summary>A day, or a run of days, still to come in the overrides' editor, with Delete.</summary>
/// <param name="Key">The row, as its delete names it.</param>
/// <param name="Dates">"Sep 24, 2026", or "Dec 24, 2026 to Dec 31, 2026".</param>
/// <param name="Hours">Its hours, or why it has none.</param>
/// <param name="DeleteLabel">"Delete".</param>
public sealed record OverrideEntry(string Key, string Dates, string Hours, string DeleteLabel)
{
    /// <summary>What a screen reader says for the row.</summary>
    public string AccessibleName => Dates + ": " + Hours;

    /// <summary>What a screen reader says for its Delete button.</summary>
    public string DeleteName => DeleteLabel + ": " + Dates;

    /// <summary>The UI Automation id of the row ("HoursOverride" and its key).</summary>
    public string AutomationId => "HoursOverride" + Key;

    internal static OverrideEntry From(OverrideRowView row, string deleteLabel) => new(row.Key, row.Dates, row.Hours, deleteLabel);

    /// <inheritdoc/>
    public override string ToString() => AccessibleName;
}
