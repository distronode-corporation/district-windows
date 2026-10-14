using DistrictAI.ViewModels.Scheduling;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views.Scheduling;

/// <summary>The cancel dialog's fields: an optional reason for the attendee's email.</summary>
public sealed partial class CancelBookingContent : UserControl
{
    /// <summary>The fields of <paramref name="model"/>'s dialog.</summary>
    public CancelBookingContent(CancelBookingViewModel model)
    {
        ViewModel = model;
        InitializeComponent();
    }

    /// <summary>The dialog's fields.</summary>
    public CancelBookingViewModel ViewModel { get; }

    private void OnReasonChanged(object sender, TextChangedEventArgs e) => ViewModel.EditReason(ReasonBox.Text);
}
