using DistrictAI.ViewModels.Scheduling;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views.Scheduling;

/// <summary>
/// The member's email notifications, as an attendee and as a host, a switch
/// each, saved together.
/// </summary>
public sealed partial class SettingsNotificationsContent : UserControl
{
    /// <summary>The switches of <paramref name="model"/>'s dialog.</summary>
    public SettingsNotificationsContent(SettingsNotificationsViewModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        ViewModel = model;
        InitializeComponent();
    }

    /// <summary>The dialog's switches.</summary>
    public SettingsNotificationsViewModel ViewModel { get; }

    // An ItemsRepeater whose template uses x:Bind gives what it makes no
    // DataContext, so each switch carries its notification's field in Tag.
    // A switch set to what the core holds (as it is made, or as the core's
    // answer comes back) sends nothing: the view model sends only a change.
    private void OnToggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch { Tag: string field } toggle)
        {
            ViewModel.Set(field, toggle.IsOn);
        }
    }
}
