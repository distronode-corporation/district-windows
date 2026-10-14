using DistrictAI.Platform;
using DistrictAI.ViewModels.Scheduling;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views.Scheduling;

/// <summary>
/// Making an API key: its name, then the key, shown once, with Copy. The key
/// goes to the clipboard only when Copy is pressed, kept out of the
/// clipboard's history.
/// </summary>
public sealed partial class DeveloperKeyContent : UserControl
{
    /// <summary>The fields of <paramref name="model"/>'s dialog.</summary>
    public DeveloperKeyContent(DeveloperKeyViewModel model)
    {
        ViewModel = model;
        InitializeComponent();
    }

    /// <summary>The dialog's fields.</summary>
    public DeveloperKeyViewModel ViewModel { get; }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Shown.IsShown)
        {
            ViewModel.Copied(ClipboardService.CopySecret(ViewModel.Shown.Value));
        }
    }
}
