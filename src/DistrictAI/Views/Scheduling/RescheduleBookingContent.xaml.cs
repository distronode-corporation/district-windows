using DistrictAI.ViewModels.Scheduling;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views.Scheduling;

/// <summary>The move dialog's fields: a day, and its free times in the scheduler profile's zone.</summary>
public sealed partial class RescheduleBookingContent : UserControl
{
    /// <summary>The fields of <paramref name="model"/>'s dialog.</summary>
    public RescheduleBookingContent(RescheduleBookingViewModel model)
    {
        ViewModel = model;
        InitializeComponent();
    }

    /// <summary>The dialog's fields.</summary>
    public RescheduleBookingViewModel ViewModel { get; }

    private void OnDayChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args)
    {
        if (args.NewDate is { } date)
        {
            ViewModel.PickDay(date);
        }
    }

    private void OnSlotChanged(object sender, SelectionChangedEventArgs e) => ViewModel.Slots.Pick(SlotChooser.SelectedIndex);
}
