using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DistrictAI.Core.Ffi;

namespace DistrictAI.ViewModels.Scheduling;

/// <summary>
/// The working hours (the core's <see cref="SchedulingHoursView"/>): the
/// week, every day Monday first and an empty one too, with its summary and
/// the zone the times are in; the dated overrides still to come, read on
/// their own and failing alone; and, for a member who may change things, the
/// two editors, each a dialog the core holds open.
/// </summary>
public sealed partial class SchedulingHoursViewModel : ObservableObject
{
    private PageContext? _context;

    /// <summary>Loading and failure, for the week's read.</summary>
    public LoadStateViewModel Load { get; } = new();

    /// <summary>The week's editor.</summary>
    public HoursWeekViewModel WeekEditor { get; } = new();

    /// <summary>The overrides' editor.</summary>
    public HoursOverridesViewModel OverridesEditor { get; } = new();

    /// <summary>The week, Monday first: each day and its hours.</summary>
    public ObservableCollection<FactItem> Week { get; } = [];

    /// <summary>The overrides still to come: their days and their hours.</summary>
    public ObservableCollection<FactItem> Overrides { get; } = [];

    /// <summary>"Times in America/Toronto".</summary>
    [ObservableProperty]
    public partial string TimesIn { get; set; } = string.Empty;

    /// <summary>Whether the two editors are offered.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenWeekCommand), nameof(OpenOverridesCommand))]
    public partial bool OffersWrites { get; set; }

    /// <summary>"Your working hours", the button.</summary>
    [ObservableProperty]
    public partial string WeekLabel { get; set; } = string.Empty;

    /// <summary>"Date overrides", the button.</summary>
    [ObservableProperty]
    public partial string OverridesLabel { get; set; } = string.Empty;

    /// <summary>The week's heading.</summary>
    [ObservableProperty]
    public partial string WeekHeading { get; set; } = string.Empty;

    /// <summary>"Mon to Fri, 9:00 a.m. to 5:00 p.m.", or that no day is bookable yet.</summary>
    [ObservableProperty]
    public partial string Summary { get; set; } = string.Empty;

    /// <summary>The overrides' heading.</summary>
    [ObservableProperty]
    public partial string OverridesHeading { get; set; } = string.Empty;

    /// <summary>What the overrides are.</summary>
    [ObservableProperty]
    public partial string OverridesSubtitle { get; set; } = string.Empty;

    /// <summary>Whether the overrides are being read.</summary>
    [ObservableProperty]
    public partial bool OverridesLoading { get; set; }

    /// <summary>Why the overrides could not be read, or empty.</summary>
    [ObservableProperty]
    public partial string OverridesFailure { get; set; } = string.Empty;

    /// <summary>Whether there is an <see cref="OverridesFailure"/>.</summary>
    [ObservableProperty]
    public partial bool HasOverridesFailure { get; set; }

    /// <summary>Whether the overrides are read and none is to come.</summary>
    [ObservableProperty]
    public partial bool ShowNoOverrides { get; set; }

    /// <summary>What to say then.</summary>
    [ObservableProperty]
    public partial string NoOverrides { get; set; } = string.Empty;

    internal void Attach(PageContext context)
    {
        _context = context;
        Load.Attach(context);
        WeekEditor.Attach(context);
        OverridesEditor.Attach(context);
    }

    internal void Show(SchedulingHoursView view)
    {
        Load.Show(view.Status, true, null, refreshing: false, refreshFailure: null);
        TimesIn = view.TimesIn;
        OffersWrites = view.OffersWrites;
        WeekLabel = view.WeekLabel;
        OverridesLabel = view.OverridesLabel;
        WeekHeading = view.WeekHeading;
        Summary = view.Summary;
        Display.Sync(Week, [.. view.Week.Select(FactItem.From)]);

        OverridesHeading = view.OverridesHeading;
        OverridesSubtitle = view.OverridesSubtitle;
        OverridesLoading = view.OverridesStatus is LoadStatus.Loading;
        var failure = (view.OverridesStatus as LoadStatus.Failed)?.Failure;
        OverridesFailure = Display.Failure(failure);
        HasOverridesFailure = failure is not null;
        Display.Sync(Overrides, [.. view.Overrides.Select(FactItem.From)]);
        ShowNoOverrides = view.OverridesStatus is LoadStatus.Ready && view.Overrides.Length == 0;
        NoOverrides = view.OverridesEmpty;

        WeekEditor.Show(view.WeekEditor);
        OverridesEditor.Show(view.OverridesEditor);
    }

    /// <summary>Closes its dialogs: the working hours are no longer showing.</summary>
    internal void Leave()
    {
        WeekEditor.Show(null);
        OverridesEditor.Show(null);
    }

    [RelayCommand(CanExecute = nameof(OffersWrites))]
    private void OpenWeek() => _context?.Send(Event(new HoursAction.OpenWeek()));

    [RelayCommand(CanExecute = nameof(OffersWrites))]
    private void OpenOverrides() => _context?.Send(Event(new HoursAction.OpenOverrides()));

    /// <summary>The event for <paramref name="action"/>.</summary>
    internal static UiEvent Event(HoursAction action) =>
        new UiEvent.SchedulingAdmin(new SchedulingAdminAction.Hours(action));
}
