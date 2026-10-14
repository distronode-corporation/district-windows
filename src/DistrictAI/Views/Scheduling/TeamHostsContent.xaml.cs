using DistrictAI.ViewModels.Scheduling;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views.Scheduling;

/// <summary>
/// A team's hosts: each host's routing priority with Save priority and
/// Remove (asked once more in place), and the people who could be added, one
/// picked for Add member.
/// </summary>
public sealed partial class TeamHostsContent : UserControl
{
    /// <summary>The fields of <paramref name="model"/>'s dialog.</summary>
    public TeamHostsContent(TeamHostsViewModel model)
    {
        ViewModel = model;
        InitializeComponent();
    }

    /// <summary>The dialog's fields.</summary>
    public TeamHostsViewModel ViewModel { get; }

    // An ItemsRepeater whose template uses x:Bind gives what it makes no
    // DataContext, so each row's control carries its host's id in Tag.
    private TeamHostEntry? HostOf(object sender) =>
        (sender as FrameworkElement)?.Tag is string id ? ViewModel.Hosts.FirstOrDefault(host => host.UserId == id) : null;

    private void OnPriorityChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox box && HostOf(sender) is { } host)
        {
            ViewModel.SetPriority(host, box.Text);
        }
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (HostOf(sender) is { } host)
        {
            ViewModel.SavePriority(host);
        }
    }

    private void OnRemoveClick(object sender, RoutedEventArgs e)
    {
        if (HostOf(sender) is { } host)
        {
            ViewModel.AskRemove(host);
        }
    }

    private void OnCandidateChanged(object sender, SelectionChangedEventArgs e) =>
        ViewModel.Pick(CandidateChooser.SelectedIndex);
}
