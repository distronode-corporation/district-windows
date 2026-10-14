using DistrictAI.ViewModels.Scheduling;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views.Scheduling;

/// <summary>
/// The CalDAV form: where the calendar lives, its server's address where one
/// is needed, the user name and an app password, each with why it was
/// refused.
/// </summary>
public sealed partial class CaldavContent : UserControl
{
    /// <summary>The fields of <paramref name="model"/>'s dialog.</summary>
    public CaldavContent(CaldavViewModel model)
    {
        ViewModel = model;
        InitializeComponent();
    }

    /// <summary>The dialog's fields.</summary>
    public CaldavViewModel ViewModel { get; }

    private void OnPresetChanged(object sender, SelectionChangedEventArgs e) => ViewModel.PickPreset(PresetChooser.SelectedIndex);
}
