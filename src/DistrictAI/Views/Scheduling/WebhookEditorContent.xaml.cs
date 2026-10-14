using DistrictAI.Platform;
using DistrictAI.ViewModels.Scheduling;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views.Scheduling;

/// <summary>
/// Adding a webhook, or changing what one sends: its address, a switch per
/// event and per part of a booking; then, for one just added, its signing
/// secret, shown once, with Copy (kept out of the clipboard's history).
/// </summary>
public sealed partial class WebhookEditorContent : UserControl
{
    /// <summary>The fields of <paramref name="model"/>'s dialog.</summary>
    public WebhookEditorContent(WebhookEditorViewModel model)
    {
        ViewModel = model;
        InitializeComponent();
    }

    /// <summary>The dialog's fields.</summary>
    public WebhookEditorViewModel ViewModel { get; }

    // An ItemsRepeater whose template uses x:Bind gives what it makes no
    // DataContext, so each switch carries its event's or field's name in Tag.
    private void OnEventClick(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { Tag: string value } box)
        {
            ViewModel.SetEvent(value, box.IsChecked == true);
        }
    }

    private void OnFieldClick(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { Tag: string value } box)
        {
            ViewModel.SetField(value, box.IsChecked == true);
        }
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Shown.IsShown)
        {
            ViewModel.Copied(ClipboardService.CopySecret(ViewModel.Shown.Value));
        }
    }
}
