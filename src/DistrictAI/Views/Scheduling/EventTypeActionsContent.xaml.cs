using DistrictAI.ViewModels.Scheduling;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views.Scheduling;

/// <summary>
/// An event type's state actions: on or off, archive or restore, a test email
/// of each kind, and delete, asked once more in place.
/// </summary>
public sealed partial class EventTypeActionsContent : UserControl
{
    /// <summary>The actions of <paramref name="model"/>'s dialog.</summary>
    public EventTypeActionsContent(EventTypeActionsViewModel model)
    {
        ViewModel = model;
        InitializeComponent();
    }

    /// <summary>The dialog's actions.</summary>
    public EventTypeActionsViewModel ViewModel { get; }

    private void OnTestEmailClick(object sender, RoutedEventArgs e)
    {
        // An ItemsRepeater whose template uses x:Bind gives what it makes no
        // DataContext, so each link carries its email's kind in Tag.
        if ((sender as FrameworkElement)?.Tag is string kind && ViewModel.TestEmails.FirstOrDefault(entry => entry.Value == kind) is { } email)
        {
            ViewModel.SendTestEmail(email);
        }
    }
}
