using DistrictAI.ViewModels.Scheduling;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views.Scheduling;

/// <summary>
/// The event type's form: the name, the booking page it makes, the duration
/// and the location for a create, and every field for an edit.
/// </summary>
public sealed partial class EventTypeEditorContent : UserControl
{
    /// <summary>The fields of <paramref name="model"/>'s dialog.</summary>
    public EventTypeEditorContent(EventTypeEditorViewModel model)
    {
        ViewModel = model;
        InitializeComponent();
    }

    /// <summary>The dialog's fields.</summary>
    public EventTypeEditorViewModel ViewModel { get; }

    private void OnLocationChanged(object sender, SelectionChangedEventArgs e) => ViewModel.PickLocation(LocationChooser.SelectedIndex);
}
