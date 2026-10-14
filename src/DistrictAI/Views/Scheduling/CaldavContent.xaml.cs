using DistrictAI.ViewModels.Scheduling;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views.Scheduling;

/// <summary>
/// The CalDAV form: where the calendar lives, its server's address where one
/// is needed, the user name and an app password, each with why it was
/// refused. The app password box owns what is typed in it: each change is
/// handed to the view model, and the box empties when the view model says
/// the password went.
/// </summary>
public sealed partial class CaldavContent : UserControl
{
    /// <summary>The fields of <paramref name="model"/>'s dialog.</summary>
    public CaldavContent(CaldavViewModel model)
    {
        ViewModel = model;
        InitializeComponent();
        ViewModel.PasswordCleared += OnPasswordCleared;
        Unloaded += (_, _) => ViewModel.PasswordCleared -= OnPasswordCleared;
    }

    /// <summary>The dialog's fields.</summary>
    public CaldavViewModel ViewModel { get; }

    private void OnPresetChanged(object sender, SelectionChangedEventArgs e) => ViewModel.PickPreset(PresetChooser.SelectedIndex);

    private void OnPasswordChanged(object sender, RoutedEventArgs e) => ViewModel.SetPassword(PasswordInput.Password);

    private void OnPasswordCleared(object? sender, EventArgs e) => PasswordInput.Password = string.Empty;
}
