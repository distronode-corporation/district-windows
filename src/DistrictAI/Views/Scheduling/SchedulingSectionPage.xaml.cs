using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels;
using DistrictAI.ViewModels.Scheduling;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views.Scheduling;

/// <summary>
/// A page of the booking scheduler's administration: its heading, the section
/// navigation, and the section showing, each section a control of its own
/// (a section's pull request adds its control and shows it here).
/// </summary>
public sealed partial class SchedulingSectionPage : UserControl
{
    /// <summary>A page with nothing shown yet.</summary>
    public SchedulingSectionPage()
    {
        InitializeComponent();
        OverviewPanel.Attach(ViewModel.Overview);
        // The dialogs never outlive the page: leaving it, or signing out,
        // takes them away. The core drops them when a section is entered again.
        Unloaded += (_, _) =>
        {
            EventTypesPanel.CloseDialogs();
            EventTypePanel.CloseDialogs();
            HoursPanel.CloseDialogs();
            BookingsPanel.CloseDialogs();
            BookingPanel.CloseDialogs();
            CalendarPanel.CloseDialogs();
            TeamPanel.CloseDialogs();
            DeveloperPanel.CloseDialogs();
            SettingsPanel.CloseDialogs();
        };
    }

    /// <summary>What the page shows, and its actions.</summary>
    public SchedulingAdminViewModel ViewModel { get; } = new();

    internal void Attach(PageContext context)
    {
        ViewModel.Attach(context);
        EventTypesPanel.Attach(ViewModel.EventTypes, context);
        EventTypePanel.Attach(ViewModel.EventType, context);
        HoursPanel.Attach(ViewModel.Hours, context);
        BookingsPanel.Attach(ViewModel.Bookings, context);
        BookingPanel.Attach(ViewModel.Booking, context);
        CalendarPanel.Attach(ViewModel.Calendar, context);
        TeamPanel.Attach(ViewModel.Team, context);
        DeveloperPanel.Attach(ViewModel.Developer, context);
        SettingsPanel.Attach(ViewModel.Settings, context);
    }

    internal void Show(SchedulingAdminView view)
    {
        ViewModel.Show(view);
        EventTypesPanel.SyncDialogs();
        EventTypePanel.SyncDialogs();
        HoursPanel.SyncDialogs();
        BookingsPanel.SyncDialogs();
        BookingPanel.SyncDialogs();
        CalendarPanel.SyncDialogs();
        TeamPanel.SyncDialogs();
        DeveloperPanel.SyncDialogs();
        SettingsPanel.SyncDialogs();
    }

    private void OnSectionChanged(object sender, SelectionChangedEventArgs e) =>
        ViewModel.OpenSection(SectionChooser.SelectedIndex);
}
