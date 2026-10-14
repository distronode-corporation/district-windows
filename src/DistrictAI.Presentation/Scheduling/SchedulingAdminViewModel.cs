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

    /// <summary>The bookings.</summary>
    public SchedulingBookingsViewModel Bookings { get; } = new();

    /// <summary>One booking.</summary>
    public SchedulingBookingViewModel Booking { get; } = new();

    /// <summary>The page's heading.</summary>
    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;

    /// <summary>Which of <see cref="Sections"/> shows, or -1.</summary>
    [ObservableProperty]
    public partial int SelectedSection { get; set; } = -1;

    /// <summary>Whether the overview shows.</summary>
    [ObservableProperty]
    public partial bool ShowsOverview { get; set; }

    /// <summary>Whether the bookings show.</summary>
    [ObservableProperty]
    public partial bool ShowsBookings { get; set; }

    /// <summary>Whether one booking shows.</summary>
    [ObservableProperty]
    public partial bool ShowsBooking { get; set; }

    internal void Attach(PageContext context)
    {
        _context = context;
        Bookings.Attach(context);
        Booking.Attach(context);
    }

    internal void Show(SchedulingAdminView view)
    {
        Title = view.Title;
        Display.Sync(Sections, [.. view.Sections.Select(SchedulingSectionEntry.From)]);
        SelectedSection = Array.FindIndex(view.Sections, section => section.Selected);
        ShowsOverview = view.Page is SchedulingAdminPage.Overview;
        ShowsBookings = view.Page is SchedulingAdminPage.Bookings;
        ShowsBooking = view.Page is SchedulingAdminPage.Booking;
        switch (view.Page)
        {
            case SchedulingAdminPage.Overview overview:
                Overview.Show(overview.View);
                break;
            case SchedulingAdminPage.Bookings bookings:
                Bookings.Show(bookings.View);
                break;
            case SchedulingAdminPage.Booking booking:
                Booking.Show(booking.View);
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
