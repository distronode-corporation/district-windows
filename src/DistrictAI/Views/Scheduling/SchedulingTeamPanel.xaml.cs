using DistrictAI.ViewModels;
using DistrictAI.ViewModels.Scheduling;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views.Scheduling;

/// <summary>
/// The team: who left while still a host, the members with Archive account,
/// and the teams with Edit, Hosts and Create team; and their three dialogs (a
/// team's name, its hosts, an account's archive), each one the core holds
/// open (<see cref="SchedulingDialogHost"/>), each asking its delete, removal
/// or archive once more in place.
/// </summary>
public sealed partial class SchedulingTeamPanel : UserControl
{
    private readonly SchedulingDialogHost _editor = new("TeamEditorDialog");
    private readonly SchedulingDialogHost _hosts = new("TeamHostsDialog");
    private readonly SchedulingDialogHost _archive = new("TeamArchiveDialog");
    private PageContext? _context;

    /// <summary>A panel showing nothing until <see cref="Attach"/>.</summary>
    public SchedulingTeamPanel()
    {
        InitializeComponent();
        Status.Attach(ViewModel.Load);
        Loaded += (_, _) => SyncDialogs();
    }

    /// <summary>What the panel shows, and its actions.</summary>
    public SchedulingTeamViewModel ViewModel { get; private set; } = new();

    /// <summary>Shows <paramref name="model"/>, the page's own, from now on.</summary>
    internal void Attach(SchedulingTeamViewModel model, PageContext context)
    {
        ViewModel = model;
        _context = context;
        Status.Attach(model.Load);
        Bindings.Update();
    }

    /// <summary>Opens the dialog the core holds open, and closes the ones it does not.</summary>
    internal void SyncDialogs()
    {
        _editor.Sync(this, _context, ViewModel.Editor.Dialog, () => new TeamEditorContent(ViewModel.Editor));
        _hosts.Sync(this, _context, ViewModel.Hosts.Dialog, () => new TeamHostsContent(ViewModel.Hosts));
        _archive.Sync(this, _context, ViewModel.Archive.Dialog, () => new TeamArchiveContent(ViewModel.Archive));
    }

    /// <summary>Closes every dialog without asking the core: the page left the screen.</summary>
    internal void CloseDialogs()
    {
        _editor.Close();
        _hosts.Close();
        _archive.Close();
    }

    // An ItemsRepeater whose template uses x:Bind gives what it makes no
    // DataContext, so each row's button carries its row's key in Tag.
    private static string? KeyOf(object sender) => (sender as FrameworkElement)?.Tag as string;

    private TeamEntry? TeamOf(object sender) =>
        KeyOf(sender) is { } id ? ViewModel.Teams.FirstOrDefault(team => team.Id == id) : null;

    private void OnEditClick(object sender, RoutedEventArgs e)
    {
        if (TeamOf(sender) is { } team)
        {
            ViewModel.OpenEditor(team);
        }
    }

    private void OnHostsClick(object sender, RoutedEventArgs e)
    {
        if (TeamOf(sender) is { } team)
        {
            ViewModel.OpenHosts(team);
        }
    }

    private void OnArchiveClick(object sender, RoutedEventArgs e)
    {
        if (KeyOf(sender) is { } email && ViewModel.Members.FirstOrDefault(member => member.Email == email) is { } member)
        {
            ViewModel.OpenArchive(member);
        }
    }
}
