using DistrictAI.ViewModels.Scheduling;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views.Scheduling;

/// <summary>
/// One account's calendars: a switch per calendar for conflicts, and the
/// calendars that can receive bookings, one chosen.
/// </summary>
public sealed partial class CalendarPickerContent : UserControl
{
    /// <summary>The fields of <paramref name="model"/>'s dialog.</summary>
    public CalendarPickerContent(CalendarPickerViewModel model)
    {
        ViewModel = model;
        InitializeComponent();
    }

    /// <summary>The dialog's fields.</summary>
    public CalendarPickerViewModel ViewModel { get; }

    private void OnConflictClick(object sender, RoutedEventArgs e)
    {
        // An ItemsRepeater whose template uses x:Bind gives what it makes no
        // DataContext, so each switch carries its calendar's id in Tag.
        if (sender is CheckBox { Tag: string id } box && ViewModel.Calendars.FirstOrDefault(row => row.Id == id) is { } row)
        {
            ViewModel.SetCheckConflicts(row, box.IsChecked == true);
        }
    }

    private void OnDestinationChanged(object sender, SelectionChangedEventArgs e) =>
        ViewModel.ChooseDestination(DestinationChooser.SelectedIndex);
}
