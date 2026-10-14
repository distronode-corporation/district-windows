using DistrictAI.ViewModels.Scheduling;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views.Scheduling;

/// <summary>The host dialog's fields: the scheduler's users who can take the booking.</summary>
public sealed partial class ReassignBookingContent : UserControl
{
    /// <summary>The fields of <paramref name="model"/>'s dialog.</summary>
    public ReassignBookingContent(ReassignBookingViewModel model)
    {
        ViewModel = model;
        InitializeComponent();
    }

    /// <summary>The dialog's fields.</summary>
    public ReassignBookingViewModel ViewModel { get; }

    private void OnHostChanged(object sender, SelectionChangedEventArgs e) => ViewModel.Hosts.Pick(HostChooser.SelectedIndex);
}
