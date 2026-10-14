using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DistrictAI.Core.Ffi;

namespace DistrictAI.ViewModels.Scheduling;

/// <summary>
/// A page of the booking scheduler's administration (the core's
/// <see cref="SchedulingAdminView"/>): its heading, the section navigation
/// (every section this version has, the one showing selected), and the
/// section's own view model, of which exactly one shows.
/// </summary>
/// <remarks>
/// A section's pull request adds its view model here (a property, and a
/// <c>Shows</c> flag), shows it for its <see cref="SchedulingAdminPage"/>
/// variant in <see cref="Show"/>, and adds its control to the page, visible
/// on its flag.
/// </remarks>
public sealed partial class SchedulingAdminViewModel : ObservableObject
{
    private PageContext? _context;

    /// <summary>The section navigation's entries.</summary>
    public ObservableCollection<SchedulingSectionEntry> Sections { get; } = [];

    /// <summary>The overview.</summary>
    public SchedulingOverviewViewModel Overview { get; } = new();

    /// <summary>The event types.</summary>
    public SchedulingEventTypesViewModel EventTypes { get; } = new();

    /// <summary>One event type.</summary>
    public SchedulingEventTypeViewModel EventType { get; } = new();

    /// <summary>The working hours.</summary>
    public SchedulingHoursViewModel Hours { get; } = new();

    /// <summary>The bookings.</summary>
    public SchedulingBookingsViewModel Bookings { get; } = new();

    /// <summary>One booking.</summary>
    public SchedulingBookingViewModel Booking { get; } = new();

    /// <summary>The member's calendars.</summary>
    public SchedulingCalendarViewModel Calendar { get; } = new();

    /// <summary>The scheduler's users and teams.</summary>
    public SchedulingTeamViewModel Team { get; } = new();

    /// <summary>API keys, connected apps and webhooks.</summary>
    public SchedulingDeveloperViewModel Developer { get; } = new();

    /// <summary>The scheduler's settings.</summary>
    public SchedulingSettingsViewModel Settings { get; } = new();

    /// <summary>The page's heading.</summary>
    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;

    /// <summary>Which of <see cref="Sections"/> shows, or -1.</summary>
    [ObservableProperty]
    public partial int SelectedSection { get; set; } = -1;

    /// <summary>Whether the overview shows.</summary>
    [ObservableProperty]
    public partial bool ShowsOverview { get; set; }

    /// <summary>Whether the event types show.</summary>
    [ObservableProperty]
    public partial bool ShowsEventTypes { get; set; }

    /// <summary>Whether one event type shows.</summary>
    [ObservableProperty]
    public partial bool ShowsEventType { get; set; }

    /// <summary>Whether the working hours show.</summary>
    [ObservableProperty]
    public partial bool ShowsHours { get; set; }

    /// <summary>Whether the bookings show.</summary>
    [ObservableProperty]
    public partial bool ShowsBookings { get; set; }

    /// <summary>Whether one booking shows.</summary>
    [ObservableProperty]
    public partial bool ShowsBooking { get; set; }

    /// <summary>Whether the calendars show.</summary>
    [ObservableProperty]
    public partial bool ShowsCalendar { get; set; }

    /// <summary>Whether the team shows.</summary>
    [ObservableProperty]
    public partial bool ShowsTeam { get; set; }

    /// <summary>Whether Developer shows.</summary>
    [ObservableProperty]
    public partial bool ShowsDeveloper { get; set; }

    /// <summary>Whether the settings show.</summary>
    [ObservableProperty]
    public partial bool ShowsSettings { get; set; }

    internal void Attach(PageContext context)
    {
        _context = context;
        EventTypes.Attach(context);
        EventType.Attach(context);
        Hours.Attach(context);
        Bookings.Attach(context);
        Booking.Attach(context);
        Calendar.Attach(context);
        Team.Attach(context);
        Developer.Attach(context);
        Settings.Attach(context);
    }

    internal void Show(SchedulingAdminView view)
    {
        Title = view.Title;
        Display.Sync(Sections, [.. view.Sections.Select(SchedulingSectionEntry.From)]);
        SelectedSection = Array.FindIndex(view.Sections, section => section.Selected);
        ShowsOverview = view.Page is SchedulingAdminPage.Overview;
        ShowsEventTypes = view.Page is SchedulingAdminPage.EventTypes;
        ShowsEventType = view.Page is SchedulingAdminPage.EventType;
        ShowsHours = view.Page is SchedulingAdminPage.Hours;
        ShowsBookings = view.Page is SchedulingAdminPage.Bookings;
        ShowsBooking = view.Page is SchedulingAdminPage.Booking;
        ShowsCalendar = view.Page is SchedulingAdminPage.Calendar;
        ShowsTeam = view.Page is SchedulingAdminPage.Team;
        ShowsDeveloper = view.Page is SchedulingAdminPage.Developer;
        ShowsSettings = view.Page is SchedulingAdminPage.Settings;
        // A section left with a dialog open (a deleted event type goes back
        // to its list) closes it.
        if (!ShowsEventTypes)
        {
            EventTypes.Leave();
        }
        if (!ShowsEventType)
        {
            EventType.Leave();
        }
        if (!ShowsHours)
        {
            Hours.Leave();
        }
        if (!ShowsCalendar)
        {
            Calendar.Leave();
        }
        if (!ShowsTeam)
        {
            Team.Leave();
        }
        if (!ShowsDeveloper)
        {
            Developer.Leave();
        }
        if (!ShowsSettings)
        {
            Settings.Leave();
        }
        switch (view.Page)
        {
            case SchedulingAdminPage.Overview overview:
                Overview.Show(overview.View);
                break;
            case SchedulingAdminPage.EventTypes eventTypes:
                EventTypes.Show(eventTypes.View);
                break;
            case SchedulingAdminPage.EventType eventType:
                EventType.Show(eventType.View);
                break;
            case SchedulingAdminPage.Hours hours:
                Hours.Show(hours.View);
                break;
            case SchedulingAdminPage.Bookings bookings:
                Bookings.Show(bookings.View);
                break;
            case SchedulingAdminPage.Booking booking:
                Booking.Show(booking.View);
                break;
            case SchedulingAdminPage.Calendar calendar:
                Calendar.Show(calendar.View);
                break;
            case SchedulingAdminPage.Team team:
                Team.Show(team.View);
                break;
            case SchedulingAdminPage.Developer developer:
                Developer.Show(developer.View);
                break;
            case SchedulingAdminPage.Settings settings:
                Settings.Show(settings.View);
                break;
        }
    }

    /// <summary>Opens the section at <paramref name="index"/> of <see cref="Sections"/>; the one showing sends nothing.</summary>
    internal void OpenSection(int index)
    {
        if (index < 0 || index >= Sections.Count || index == SelectedSection)
        {
            return;
        }
        _context?.Send(SchedulingSectionEntry.Open(Sections[index].Section));
    }
}
