using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DistrictAI.Core.Ffi;

namespace DistrictAI.ViewModels.Scheduling;

/// <summary>
/// The team (the core's <see cref="SchedulingTeamView"/>): who left the
/// workspace while still holding bookings, named at the top; the members,
/// each with their role and where they stand with the scheduler, and Archive
/// account for a live scheduler account; and the teams, each with Edit and
/// Hosts, and Create team. Every write is offered only to a member who may
/// change things, and each is a dialog the core holds open.
/// </summary>
/// <remarks>
/// When the workspace's members could not be read, no role is shown and the
/// section says so, rather than calling every host removed.
/// </remarks>
public sealed partial class SchedulingTeamViewModel : ObservableObject
{
    private PageContext? _context;

    /// <summary>Loading and failure, for the read of the members.</summary>
    public LoadStateViewModel Load { get; } = new();

    /// <summary>A team's name: create, rename, delete.</summary>
    public TeamEditorViewModel Editor { get; } = new();

    /// <summary>A team's hosts.</summary>
    public TeamHostsViewModel Hosts { get; } = new();

    /// <summary>An account's archive.</summary>
    public TeamArchiveViewModel Archive { get; } = new();

    /// <summary>The people.</summary>
    public ObservableCollection<TeamMemberEntry> Members { get; } = [];

    /// <summary>The teams.</summary>
    public ObservableCollection<TeamEntry> Teams { get; } = [];

    /// <summary>The stranded notice's heading.</summary>
    [ObservableProperty]
    public partial string StrandedHeading { get; set; } = string.Empty;

    /// <summary>Who left while still a host, and what to do, or empty.</summary>
    [ObservableProperty]
    public partial string Stranded { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="Stranded"/>.</summary>
    [ObservableProperty]
    public partial bool HasStranded { get; set; }

    /// <summary>The members' heading.</summary>
    [ObservableProperty]
    public partial string MembersHeading { get; set; } = string.Empty;

    /// <summary>That the workspace's members could not be read, or empty.</summary>
    [ObservableProperty]
    public partial string MembersDegraded { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="MembersDegraded"/>.</summary>
    [ObservableProperty]
    public partial bool HasMembersDegraded { get; set; }

    /// <summary>What the members' card says when there is nobody.</summary>
    [ObservableProperty]
    public partial string MembersEmpty { get; set; } = string.Empty;

    /// <summary>Whether the members are read and there is nobody.</summary>
    [ObservableProperty]
    public partial bool ShowMembersEmpty { get; set; }

    /// <summary>The teams' heading.</summary>
    [ObservableProperty]
    public partial string TeamsHeading { get; set; } = string.Empty;

    /// <summary>What a team is.</summary>
    [ObservableProperty]
    public partial string TeamsSubtitle { get; set; } = string.Empty;

    /// <summary>Whether the teams are being read.</summary>
    [ObservableProperty]
    public partial bool TeamsLoading { get; set; }

    /// <summary>Whether their read failed.</summary>
    [ObservableProperty]
    public partial bool TeamsFailed { get; set; }

    /// <summary>Why their read failed.</summary>
    [ObservableProperty]
    public partial string TeamsFailure { get; set; } = string.Empty;

    /// <summary>What the teams' card says when there are none.</summary>
    [ObservableProperty]
    public partial string TeamsEmpty { get; set; } = string.Empty;

    /// <summary>Whether the teams are read and there are none.</summary>
    [ObservableProperty]
    public partial bool ShowTeamsEmpty { get; set; }

    /// <summary>Whether "Create team" is offered.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenCreateCommand))]
    public partial bool OffersCreate { get; set; }

    /// <summary>"Create team".</summary>
    [ObservableProperty]
    public partial string CreateLabel { get; set; } = string.Empty;

    internal void Attach(PageContext context)
    {
        _context = context;
        Load.Attach(context);
        Editor.Attach(context);
        Hosts.Attach(context);
        Archive.Attach(context);
    }

    internal void Show(SchedulingTeamView view)
    {
        Load.Show(view.Status, view.Members.Length > 0, null, refreshing: false, refreshFailure: null);
        StrandedHeading = view.StrandedHeading;
        Stranded = view.Stranded ?? string.Empty;
        HasStranded = view.Stranded is not null;
        MembersHeading = view.MembersHeading;
        MembersDegraded = view.MembersDegraded ?? string.Empty;
        HasMembersDegraded = view.MembersDegraded is not null;
        MembersEmpty = view.MembersEmpty;
        ShowMembersEmpty = view.Status is LoadStatus.Ready && view.Members.Length == 0;
        Display.Sync(Members, [.. view.Members.Select(row => TeamMemberEntry.From(row, view.ArchiveLabel))]);
        TeamsHeading = view.TeamsHeading;
        TeamsSubtitle = view.TeamsSubtitle;
        var failure = (view.TeamsStatus as LoadStatus.Failed)?.Failure;
        TeamsLoading = view.TeamsStatus is LoadStatus.Loading;
        TeamsFailed = failure is not null;
        TeamsFailure = Display.Failure(failure);
        TeamsEmpty = view.TeamsEmpty;
        ShowTeamsEmpty = view.TeamsStatus is LoadStatus.Ready && view.Teams.Length == 0;
        Display.Sync(Teams, [.. view.Teams.Select(team => new TeamEntry(
            team.Id, team.Name, team.MemberCount, view.OffersWrites, view.EditLabel, view.HostsLabel))]);
        OffersCreate = view.OffersCreate;
        CreateLabel = view.CreateLabel;
        Editor.Show(view.Editor);
        Hosts.Show(view.Hosts);
        Archive.Show(view.Archive);
    }

    /// <summary>Closes its dialogs: the team is no longer showing.</summary>
    internal void Leave()
    {
        Editor.Show(null);
        Hosts.Show(null);
        Archive.Show(null);
    }

    /// <summary>Opens <paramref name="team"/>'s name; nothing when it is not offered.</summary>
    internal void OpenEditor(TeamEntry team)
    {
        if (team.OffersWrites)
        {
            Send(new TeamAction.OpenEditor(team.Id));
        }
    }

    /// <summary>Opens <paramref name="team"/>'s hosts; nothing when they are not offered.</summary>
    internal void OpenHosts(TeamEntry team)
    {
        if (team.OffersWrites)
        {
            Send(new TeamAction.OpenMembers(team.Id));
        }
    }

    /// <summary>Opens the archive of <paramref name="member"/>'s account; nothing when it is not offered.</summary>
    internal void OpenArchive(TeamMemberEntry member)
    {
        if (member.ArchiveUserId is { } userId)
        {
            Send(new TeamAction.OpenArchive(userId));
        }
    }

    [RelayCommand(CanExecute = nameof(OffersCreate))]
    private void OpenCreate() => Send(new TeamAction.OpenCreate());

    private void Send(TeamAction action) => _context?.Send(Event(action));

    /// <summary>The event for <paramref name="action"/>.</summary>
    internal static UiEvent Event(TeamAction action) =>
        new UiEvent.SchedulingAdmin(new SchedulingAdminAction.Team(action));
}
