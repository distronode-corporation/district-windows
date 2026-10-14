using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DistrictAI.Core.Ffi;

namespace DistrictAI.ViewModels.Scheduling;

/// <summary>A row of the booking desk, labelled in every state.</summary>
/// <param name="Label">What the row is ("Booking page").</param>
/// <param name="State">Loading, failed, a value, or a hint.</param>
/// <param name="Value">The value's first line, the hint, or why the read failed.</param>
/// <param name="More">The value's other lines, one under another, or empty.</param>
public sealed record DeskEntry(string Label, BookingDeskRowState State, string Value, string More)
{
    /// <summary>Whether its read is on its way.</summary>
    public bool IsLoading => State == BookingDeskRowState.Loading;

    /// <summary>Whether its read failed (the critical colour).</summary>
    public bool IsFailed => State == BookingDeskRowState.Failed;

    /// <summary>Whether it shows a value.</summary>
    public bool IsValue => State == BookingDeskRowState.Lines;

    /// <summary>Whether it says nothing is set up yet (the quiet colour).</summary>
    public bool IsHint => State == BookingDeskRowState.Hint;

    /// <summary>Whether there is <see cref="More"/>.</summary>
    public bool HasMore => More.Length > 0;

    /// <summary>What a screen reader says for the row.</summary>
    public string AccessibleName => HasMore ? $"{Label}: {Value}. {More}" : $"{Label}: {Value}";

    internal static DeskEntry From(BookingDeskRowView row) => new(
        row.Label,
        row.State,
        row.State switch
        {
            BookingDeskRowState.Failed => Display.Failure(row.Failure),
            _ => row.Lines.FirstOrDefault() ?? string.Empty,
        },
        string.Join(Environment.NewLine, row.Lines.Skip(1)));

    /// <inheritdoc/>
    public override string ToString() => AccessibleName;
}

/// <summary>A booking under "Coming up".</summary>
/// <param name="Id">The booking.</param>
/// <param name="When">"Today 2:30 p.m.", or the date and time.</param>
/// <param name="Subtitle">Who booked, and what.</param>
/// <param name="StatusLabel">The status badge's words.</param>
/// <param name="Tone">The badge's colour.</param>
public sealed record ComingUpEntry(string Id, string When, string Subtitle, string StatusLabel, RunTone Tone)
{
    /// <summary>Whether the status is a good one.</summary>
    public bool IsSuccess => Tone == RunTone.Success;

    /// <summary>Whether the status is a bad one.</summary>
    public bool IsDanger => Tone == RunTone.Danger;

    /// <summary>Any other status.</summary>
    public bool IsNeutral => !IsSuccess && !IsDanger;

    /// <summary>What a screen reader says for the row.</summary>
    public string AccessibleName => When + ", " + Subtitle + ", " + StatusLabel;

    internal static ComingUpEntry From(ComingUpRowView row) => new(row.Id, row.When, row.Subtitle, row.StatusLabel, row.Tone);

    /// <inheritdoc/>
    public override string ToString() => AccessibleName;
}

/// <summary>
/// The overview (the core's <see cref="SchedulingOverviewView"/>): the
/// booking desk's five rows, each standing or falling alone, and what is
/// coming up. Nothing here writes, and the page never fails as a whole.
/// </summary>
public sealed partial class SchedulingOverviewViewModel : ObservableObject
{
    /// <summary>The desk's rows.</summary>
    public ObservableCollection<DeskEntry> Desk { get; } = [];

    /// <summary>The next bookings.</summary>
    public ObservableCollection<ComingUpEntry> ComingUp { get; } = [];

    /// <summary>"Times in America/Toronto".</summary>
    [ObservableProperty]
    public partial string TimesIn { get; set; } = string.Empty;

    /// <summary>"Your booking desk".</summary>
    [ObservableProperty]
    public partial string DeskHeading { get; set; } = string.Empty;

    /// <summary>"Coming up".</summary>
    [ObservableProperty]
    public partial string ComingUpHeading { get; set; } = string.Empty;

    /// <summary>Whether the next bookings are being read.</summary>
    [ObservableProperty]
    public partial bool ComingUpLoading { get; set; }

    /// <summary>Why the next bookings could not be read, or empty.</summary>
    [ObservableProperty]
    public partial string ComingUpFailure { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="ComingUpFailure"/>.</summary>
    [ObservableProperty]
    public partial bool HasComingUpFailure { get; set; }

    /// <summary>Whether the next bookings are read and there are none.</summary>
    [ObservableProperty]
    public partial bool ShowComingUpEmpty { get; set; }

    /// <summary>What to say then.</summary>
    [ObservableProperty]
    public partial string ComingUpEmpty { get; set; } = string.Empty;

    internal void Show(SchedulingOverviewView view)
    {
        TimesIn = view.TimesIn;
        DeskHeading = view.DeskHeading;
        Display.Sync(Desk, [.. view.Desk.Select(DeskEntry.From)]);
        ComingUpHeading = view.ComingUpHeading;
        ComingUpLoading = view.ComingUpStatus is LoadStatus.Loading;
        var failure = (view.ComingUpStatus as LoadStatus.Failed)?.Failure;
        ComingUpFailure = Display.Failure(failure);
        HasComingUpFailure = failure is not null;
        Display.Sync(ComingUp, [.. view.ComingUp.Select(ComingUpEntry.From)]);
        ShowComingUpEmpty = view.ComingUpStatus is LoadStatus.Ready && view.ComingUp.Length == 0;
        ComingUpEmpty = view.ComingUpEmpty;
    }
}
