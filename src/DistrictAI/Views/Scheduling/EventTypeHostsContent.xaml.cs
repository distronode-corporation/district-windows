using DistrictAI.ViewModels.Scheduling;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views.Scheduling;

/// <summary>
/// An event type's hosts and their routing: the routing mode, the rotation
/// for round robin, each host's role and priority with Remove, and the people
/// who could be added, each with Add.
/// </summary>
public sealed partial class EventTypeHostsContent : UserControl
{
    /// <summary>The fields of <paramref name="model"/>'s dialog.</summary>
    public EventTypeHostsContent(EventTypeHostsViewModel model)
    {
        ViewModel = model;
        InitializeComponent();
    }

    /// <summary>The dialog's fields.</summary>
    public EventTypeHostsViewModel ViewModel { get; }

    // An ItemsRepeater whose template uses x:Bind gives what it makes no
    // DataContext, so each row's control carries its person's id in Tag.
    private static string? IdOf(object sender) => (sender as FrameworkElement)?.Tag as string;

    private HostEntry? HostOf(object sender) =>
        IdOf(sender) is { } id ? ViewModel.Hosts.FirstOrDefault(host => host.UserId == id) : null;

    private void OnRoutingChanged(object sender, SelectionChangedEventArgs e) => ViewModel.PickRouting(RoutingChooser.SelectedIndex);

    private void OnRotationChanged(object sender, SelectionChangedEventArgs e) => ViewModel.PickRotation(RotationChooser.SelectedIndex);

    private void OnRoleChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox box && HostOf(sender) is { } host)
        {
            ViewModel.SetRole(host, box.SelectedIndex);
        }
    }

    private void OnPriorityChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox box && HostOf(sender) is { } host)
        {
            ViewModel.SetPriority(host, box.Text);
        }
    }

    private void OnRemoveClick(object sender, RoutedEventArgs e)
    {
        if (HostOf(sender) is { } host)
        {
            ViewModel.Remove(host);
        }
    }

    private void OnAddClick(object sender, RoutedEventArgs e)
    {
        if (IdOf(sender) is { } id && ViewModel.Candidates.FirstOrDefault(entry => entry.UserId == id) is { } candidate)
        {
            ViewModel.Add(candidate);
        }
    }
}
