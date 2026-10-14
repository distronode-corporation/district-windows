using DistrictAI.ViewModels.Scheduling;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views.Scheduling;

/// <summary>
/// The overrides' editor: the days still to come, each with Delete (asked
/// once more), Add override, and the add form in place of the list.
/// </summary>
public sealed partial class HoursOverridesContent : UserControl
{
    /// <summary>The fields of <paramref name="model"/>'s dialog.</summary>
    public HoursOverridesContent(HoursOverridesViewModel model)
    {
        ViewModel = model;
        InitializeComponent();
    }

    /// <summary>The dialog's fields.</summary>
    public HoursOverridesViewModel ViewModel { get; }

    private void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is OverrideEntry row)
        {
            ViewModel.AskDelete(row);
        }
    }

    private void OnKindChanged(object sender, SelectionChangedEventArgs e) => ViewModel.PickKind(KindChooser.SelectedIndex);
}
