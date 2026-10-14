using DistrictAI.ViewModels.Scheduling;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views.Scheduling;

/// <summary>
/// The week's editor: every day, Monday first, with its windows (each a time
/// it opens and a time it closes, and Remove), Add range, and Copy to all
/// weekdays on a weekday with a window to copy.
/// </summary>
public sealed partial class HoursWeekContent : UserControl
{
    /// <summary>The fields of <paramref name="model"/>'s dialog.</summary>
    public HoursWeekContent(HoursWeekViewModel model)
    {
        ViewModel = model;
        InitializeComponent();
    }

    /// <summary>The dialog's fields.</summary>
    public HoursWeekViewModel ViewModel { get; }

    private static HoursDayEntry? DayOf(object sender) => (sender as FrameworkElement)?.DataContext as HoursDayEntry;

    private static HoursRangeEntry? RangeOf(object sender) => (sender as FrameworkElement)?.DataContext as HoursRangeEntry;

    private void OnAddRangeClick(object sender, RoutedEventArgs e)
    {
        if (DayOf(sender) is { } day)
        {
            ViewModel.AddRange(day);
        }
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        if (DayOf(sender) is { } day)
        {
            ViewModel.CopyToWeekdays(day);
        }
    }

    private void OnRemoveClick(object sender, RoutedEventArgs e)
    {
        if (RangeOf(sender) is { } range)
        {
            ViewModel.Remove(range);
        }
    }

    private void OnStartChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox box && RangeOf(sender) is { } range)
        {
            ViewModel.SetStart(range, box.Text);
        }
    }

    private void OnEndChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox box && RangeOf(sender) is { } range)
        {
            ViewModel.SetEnd(range, box.Text);
        }
    }
}
