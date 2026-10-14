using DistrictAI.ViewModels.Scheduling;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views.Scheduling;

/// <summary>
/// The member's profile: their name, time zone, time and date formats, the
/// day their week starts on, and their picture, which saves at once.
/// </summary>
public sealed partial class SettingsProfileContent : UserControl
{
    /// <summary>The fields of <paramref name="model"/>'s dialog.</summary>
    public SettingsProfileContent(SettingsProfileViewModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        ViewModel = model;
        InitializeComponent();
        AvatarHost.Child = new SettingsImageControl(model.Avatar);
    }

    /// <summary>The dialog's fields.</summary>
    public SettingsProfileViewModel ViewModel { get; }

    private void OnTimezoneChanged(object sender, SelectionChangedEventArgs e) => ViewModel.PickTimezone(TimezoneChooser.SelectedIndex);

    private void OnTimeFormatChanged(object sender, SelectionChangedEventArgs e) => ViewModel.PickTimeFormat(TimeFormatChooser.SelectedIndex);

    private void OnWeekStartChanged(object sender, SelectionChangedEventArgs e) => ViewModel.PickWeekStart(WeekStartChooser.SelectedIndex);

    private void OnDateFormatChanged(object sender, SelectionChangedEventArgs e) => ViewModel.PickDateFormat(DateFormatChooser.SelectedIndex);
}
