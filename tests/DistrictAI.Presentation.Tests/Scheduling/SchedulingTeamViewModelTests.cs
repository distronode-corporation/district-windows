using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels.Scheduling;
using Xunit;

namespace DistrictAI.Presentation.Tests.Scheduling;

public sealed class SchedulingTeamViewModelTests
{
    private static readonly SchedulingSectionEntryView[] _sections =
    [
        new(SchedulingAdminSection.Overview, "Overview", "Your booking desk", false),
        new(SchedulingAdminSection.EventTypes, "Event types", "The pages a customer can book", false),
        new(SchedulingAdminSection.Hours, "Working hours", "The weekly hours you are bookable", false),
        new(SchedulingAdminSection.Bookings, "Bookings", "Every booking", false),
        new(SchedulingAdminSection.Calendar, "Calendar", "The accounts bookings are written to and checked against", false),
        new(SchedulingAdminSection.Team, "Team", "Who takes bookings, and the teams that route them", true),
    ];

    private static readonly LoadStatus _failed =
        new LoadStatus.Failed(new FailureView("Offline.", null, true), "Could not load this section");

    private static readonly FailureView _refused = new("The booking system did not answer. Try again in a minute.", null, true);

    private static DialogView Dialog(
        string title,
        string confirm,
        string close = "Cancel",
        bool canConfirm = true,
        bool working = false,
        bool destructive = false,
        FailureView? failure = null,
        string body = "") =>
        new(title, body, confirm, close, canConfirm, working, destructive, failure);

    private static TeamEditorView Editor(
        bool renaming = false,
        string name = "",
        string? nameError = null,
        bool asking = false,
        bool working = false,
        string? notice = null,
        FailureView? failure = null) =>
        new(
            Dialog: Dialog(
                renaming ? "Support" : "New team",
                renaming ? "Save name" : "Create team",
                close: renaming ? "Done" : "Cancel",
                canConfirm: !working,
                working: working,
                failure: failure),
            Hint: "A team rotates bookings through its hosts, in routing-priority order.",
            NameLabel: "Name",
            NamePlaceholder: "Front desk",
            Name: name,
            NameError: nameError,
            SlugHint: renaming ? "The booking-page address cannot be changed here." : null,
            ShowsDelete: renaming,
            DeleteLabel: "Delete team",
            CanDelete: renaming && !working,
            Editable: !working,
            Notice: notice,
            Delete: asking
                ? new SchedulingAskView("Delete Support?", "Event types that route to this team stop rotating through it.", "Delete team", "Keep team")
                : null);

    private static TeamHostsView Hosts(
        LoadStatus? users = null,
        int picked = -1,
        bool asking = false,
        bool working = false,
        string? priorityError = null,
        string? notice = null,
        string rotationPriority = "5",
        bool oneHost = false,
        bool noneToAdd = false) =>
        new(
            Dialog: Dialog("Sales", "Add member", close: "Done", canConfirm: picked >= 0 && !working, working: working),
            Hint: "A lower routing priority is offered a booking first.",
            TeamId: "team-1",
            Hosts: oneHost
                ? [new("u-1", "Contract Member", "contract@example.com", "0")]
                : [new("u-1", "Contract Member", "contract@example.com", "0"), new("u-2", "Rotation Host", "rotation@example.com", rotationPriority)],
            Empty: "No hosts yet. Nothing routes through this team until one is added.",
            PriorityLabel: "Routing priority",
            SavePriorityLabel: "Save priority",
            RemoveLabel: "Remove",
            PriorityError: priorityError,
            UsersStatus: users ?? new LoadStatus.Ready(),
            AddLabel: "Host",
            AddPlaceholder: "Choose a host",
            Candidates: users is null && !noneToAdd
                ? [new("u-3", "Sam", picked == 0), new("u-4", "dana@example.com", picked == 1)]
                : [],
            AddNone: noneToAdd ? "Every host is already in this team." : null,
            Editable: !working,
            Notice: notice,
            Remove: asking
                ? new SchedulingAskView("Remove this host?", "Bookings stop routing to them through this team. Their routing priority is not kept.", "Remove", "Keep them")
                : null);

    private static TeamArchiveView Archive(
        LoadStatus? status = null,
        bool blocked = false,
        bool asking = false,
        bool working = false,
        string? notice = null,
        FailureView? failure = null)
    {
        var read = status is null;
        var canArchive = read && !blocked && !asking && !working && notice is null;
        return new(
            Dialog: Dialog(
                "Archive Ann's account?",
                "Archive account",
                close: "Keep account",
                canConfirm: canArchive,
                working: working,
                destructive: true,
                failure: failure,
                body: "They can no longer sign in, they are skipped in routing, and their event types stop taking bookings. The account is kept."),
            Status: status ?? new LoadStatus.Ready(),
            Blocked: !read
                ? "Checking their upcoming bookings…"
                : blocked ? "Reassign or cancel their upcoming bookings first." : null,
            Bookings: blocked ? [new("bk-1", "Site visit", "Dana Booker", "2026-09-18T15:00:00Z")] : [],
            Notice: notice,
            Ask: asking
                ? new SchedulingAskView("Archive Ann's account?", "They can no longer sign in.", "Archive account", "Keep account")
                : null);
    }

    private static SchedulingTeamView Team(
        LoadStatus? status = null,
        bool viewer = false,
        bool stranded = false,
        bool rolesUnread = false,
        LoadStatus? teamsStatus = null,
        bool empty = false,
        TeamEditorView? editor = null,
        TeamHostsView? hosts = null,
        TeamArchiveView? archive = null)
    {
        var read = status is null;
        var teamsRead = teamsStatus is null;
        return new(
            Status: status ?? new LoadStatus.Ready(),
            StrandedHeading: "Someone who has left still has bookings",
            Stranded: stranded ? "Ann is no longer in this workspace but still holds upcoming bookings." : null,
            MembersHeading: "Members",
            MembersDegraded: rolesUnread ? "The workspace's members could not be read, so roles are not shown." : null,
            Members: read && !empty
                ?
                [
                    new("ann@example.com", "Ann", rolesUnread ? "ann@example.com" : "ann@example.com · Agency", "Host", RunTone.Success, viewer ? null : "u-1"),
                    new("gone@example.com", "Former Host", "gone@example.com · Removed from the workspace", "Archived", RunTone.Neutral, null),
                ]
                : [],
            MembersEmpty: "Nobody else is in this workspace yet.",
            ArchiveLabel: "Archive account",
            TeamsHeading: "Teams",
            TeamsSubtitle: "A team rotates bookings through its hosts, in routing-priority order.",
            TeamsStatus: teamsStatus ?? new LoadStatus.Ready(),
            Teams: teamsRead && !empty ? [new("team-1", "Sales", "2 members"), new("team-2", "Support", "0 members")] : [],
            TeamsEmpty: "No teams yet.",
            OffersWrites: !viewer,
            OffersCreate: !viewer && teamsRead,
            CreateLabel: "Create team",
            EditLabel: "Edit",
            HostsLabel: "Hosts",
            Editor: editor,
            Hosts: hosts,
            Archive: archive);
    }

    private static SchedulingAdminView Page(SchedulingTeamView view) =>
        new("Team", _sections, new SchedulingAdminPage.Team(view));

    private static (SchedulingAdminViewModel Model, RecordingSink Sink) Attached()
    {
        var model = new SchedulingAdminViewModel();
        var (context, sink) = Pages.Context();
        model.Attach(context);
        return (model, sink);
    }

    /// <summary>The team action <paramref name="sent"/> carries.</summary>
    private static TeamAction Action(UiEvent sent) =>
        Assert.IsType<SchedulingAdminAction.Team>(Assert.IsType<UiEvent.SchedulingAdmin>(sent).Action).Action;

    [Fact]
    public void TheMembersAndTheTeamsShowWithTheirWrites()
    {
        var (model, sink) = Attached();
        model.Show(Page(Team()));
        var vm = model.Team;
        Assert.True(model.ShowsTeam);
        Assert.False(model.ShowsCalendar || model.ShowsHours || model.ShowsBookings || model.ShowsOverview || model.ShowsEventTypes || model.ShowsEventType || model.ShowsBooking);
        Assert.Equal(5, model.SelectedSection);
        Assert.True(vm.Load.Ready);
        Assert.False(vm.HasStranded || vm.HasMembersDegraded || vm.ShowMembersEmpty || vm.ShowTeamsEmpty);
        Assert.Equal(("Members", "Teams"), (vm.MembersHeading, vm.TeamsHeading));
        Assert.StartsWith("A team rotates", vm.TeamsSubtitle);
        Assert.Equal(2, vm.Members.Count);
        var ann = vm.Members[0];
        Assert.Equal("Ann, ann@example.com · Agency, Host", ann.ToString());
        Assert.Equal("TeamMemberann@example.com", ann.AutomationId);
        Assert.Equal("Archive account: Ann", ann.ArchiveName);
        Assert.True(ann.IsSuccess && ann.OffersArchive);
        var gone = vm.Members[1];
        Assert.True(gone.IsNeutral);
        Assert.False(gone.OffersArchive);
        Assert.Equal(2, vm.Teams.Count);
        var sales = vm.Teams[0];
        Assert.Equal("Sales, 2 members", sales.ToString());
        Assert.Equal("TeamRowteam-1", sales.AutomationId);
        Assert.Equal(("Edit: Sales", "Hosts: Sales"), (sales.EditName, sales.HostsName));
        Assert.True(sales.OffersWrites);
        Assert.False(vm.TeamsLoading || vm.TeamsFailed);
        Assert.True(vm.OffersCreate);
        Assert.Equal("Create team", vm.CreateLabel);
        Assert.False(vm.Editor.Dialog.IsOpen || vm.Hosts.Dialog.IsOpen || vm.Archive.Dialog.IsOpen);

        vm.OpenCreateCommand.Execute(null);
        vm.OpenEditor(sales);
        vm.OpenHosts(sales);
        vm.OpenArchive(ann);
        vm.OpenArchive(gone);
        Assert.Equal(
            new TeamAction[]
            {
                new TeamAction.OpenCreate(),
                new TeamAction.OpenEditor("team-1"),
                new TeamAction.OpenMembers("team-1"),
                new TeamAction.OpenArchive("u-1"),
            },
            sink.Sent.Select(Action).ToArray());

        // A viewer is offered none of the writes.
        model.Show(Page(Team(viewer: true)));
        Assert.False(vm.OffersCreate);
        Assert.False(vm.OpenCreateCommand.CanExecute(null));
        Assert.False(vm.Teams[0].OffersWrites || vm.Members[0].OffersArchive);
        var before = sink.Sent.Count;
        vm.OpenEditor(vm.Teams[0]);
        vm.OpenHosts(vm.Teams[0]);
        vm.OpenArchive(vm.Members[0]);
        Assert.Equal(before, sink.Sent.Count);
    }

    [Fact]
    public void WhoLeftIsNamedAndUnreadRolesAreSaidOnce()
    {
        var (model, _) = Attached();
        var vm = model.Team;
        model.Show(Page(Team(stranded: true)));
        Assert.True(vm.HasStranded);
        Assert.StartsWith("Ann is no longer in this workspace", vm.Stranded);
        Assert.Equal("Someone who has left still has bookings", vm.StrandedHeading);

        model.Show(Page(Team(rolesUnread: true)));
        Assert.False(vm.HasStranded);
        Assert.True(vm.HasMembersDegraded);
        Assert.Equal("The workspace's members could not be read, so roles are not shown.", vm.MembersDegraded);
        Assert.Equal("ann@example.com", vm.Members[0].Subtitle);

        // A failed read of the members is a status page; of the teams, a line.
        model.Show(Page(Team(status: _failed, teamsStatus: new LoadStatus.Loading())));
        Assert.True(vm.Load.Failed);
        Assert.True(vm.Load.CanRetry);
        Assert.Empty(vm.Members);
        Assert.False(vm.ShowMembersEmpty);
        Assert.True(vm.TeamsLoading);
        Assert.False(vm.OffersCreate);
        model.Show(Page(Team(teamsStatus: _failed)));
        Assert.True(vm.TeamsFailed);
        Assert.Equal("Offline.", vm.TeamsFailure);
        Assert.Empty(vm.Teams);
        Assert.False(vm.ShowTeamsEmpty);

        model.Show(Page(Team(empty: true)));
        Assert.True(vm.ShowMembersEmpty && vm.ShowTeamsEmpty);
        Assert.Equal(("Nobody else is in this workspace yet.", "No teams yet."), (vm.MembersEmpty, vm.TeamsEmpty));
    }

    [Fact]
    public void ATeamIsCreatedRenamedAndItsDeleteIsAskedOnceMore()
    {
        var (model, sink) = Attached();
        model.Show(Page(Team(editor: Editor())));
        var vm = model.Team.Editor;
        Assert.True(vm.Dialog.IsOpen);
        Assert.Equal(("New team", "Create team", "Cancel"), (vm.Dialog.Title, vm.Dialog.ConfirmText, vm.Dialog.CloseText));
        Assert.Equal("Name", vm.Words?.NameLabel);
        Assert.Equal("Front desk", vm.Words?.NamePlaceholder);
        Assert.Equal(string.Empty, vm.Name);
        Assert.False(vm.ShowsDelete || vm.HasNameError || vm.HasNotice || vm.Asking);
        Assert.True(vm.Editable);
        Assert.Empty(sink.Sent);

        // Each change goes to the core; the next snapshot does not write over it.
        vm.Name = "Front desk";
        model.Show(Page(Team(editor: Editor(name: "Front"))));
        Assert.Equal("Front desk", vm.Name);
        vm.Dialog.ConfirmCommand.Execute(null);
        Assert.Equal(
            new TeamAction[] { new TeamAction.EditName("Front desk"), new TeamAction.SaveName() },
            sink.Sent.Select(Action).ToArray());
        model.Show(Page(Team(editor: Editor(name: "", nameError: "Give the team a name."))));
        Assert.True(vm.HasNameError);
        Assert.Equal("Give the team a name.", vm.NameError);
        model.Show(Page(Team(editor: Editor(name: "Front desk", working: true))));
        Assert.True(vm.Dialog.Working);
        Assert.False(vm.Editable);
        model.Show(Page(Team(editor: Editor(name: "Front desk", notice: "Team created"))));
        Assert.True(vm.HasNotice);
        Assert.Equal("Team created", vm.Notice);
        model.Show(Page(Team(editor: Editor(name: "Front desk", failure: _refused))));
        Assert.True(vm.Dialog.HasFailure);
        vm.Dialog.CloseCommand.Execute(null);
        Assert.Equal(new TeamAction.CloseDialog(), Action(sink.Sent[^1]));
        model.Show(Page(Team()));
        Assert.False(vm.Dialog.IsOpen);

        // A team's name: written as it opens, Done its way out, the delete apart.
        model.Show(Page(Team(editor: Editor(renaming: true, name: "Support"))));
        Assert.Equal("Support", vm.Name);
        Assert.Equal(("Support", "Save name", "Done"), (vm.Dialog.Title, vm.Dialog.ConfirmText, vm.Dialog.CloseText));
        Assert.Equal("The booking-page address cannot be changed here.", vm.SlugHint);
        Assert.True(vm.ShowsDelete && vm.CanDelete);
        Assert.Equal("Delete team", vm.DeleteLabel);
        var before = sink.Sent.Count;
        vm.ConfirmDeleteCommand.Execute(null);
        Assert.Equal(before, sink.Sent.Count);
        vm.AskDeleteCommand.Execute(null);
        model.Show(Page(Team(editor: Editor(renaming: true, name: "Support", asking: true))));
        Assert.True(vm.Asking);
        Assert.Equal(("Delete Support?", "Delete team", "Keep team"), (vm.AskTitle, vm.AskConfirm, vm.AskCancel));
        Assert.StartsWith("Event types that route", vm.AskBody);
        vm.CancelDeleteCommand.Execute(null);
        vm.ConfirmDeleteCommand.Execute(null);
        // One press, one delete.
        Assert.False(vm.ConfirmDeleteCommand.CanExecute(null));
        vm.ConfirmDeleteCommand.Execute(null);
        Assert.Equal(
            new TeamAction[] { new TeamAction.AskDelete(), new TeamAction.CancelDelete(), new TeamAction.ConfirmDelete() },
            sink.Sent.Skip(before).Select(Action).ToArray());
        model.Show(Page(Team(editor: Editor(renaming: true, name: "Support", working: true))));
        Assert.False(vm.CanDelete || vm.Asking);
        Assert.False(vm.AskDeleteCommand.CanExecute(null));
    }

    [Fact]
    public void AHostIsPickedAddedReorderedAndRemovedAskedOnceMore()
    {
        var (model, sink) = Attached();
        model.Show(Page(Team(hosts: Hosts(users: new LoadStatus.Loading()))));
        var vm = model.Team.Hosts;
        Assert.True(vm.Dialog.IsOpen);
        Assert.True(vm.UsersLoading);
        Assert.False(vm.HasCandidates || vm.HasAddNone || vm.UsersFailed);
        Assert.Equal(2, vm.Hosts.Count);
        Assert.False(vm.Dialog.ConfirmCommand.CanExecute(null));
        model.Show(Page(Team(hosts: Hosts(users: _failed))));
        Assert.True(vm.UsersFailed);
        Assert.Equal("Offline.", vm.UsersFailure);

        model.Show(Page(Team(hosts: Hosts())));
        Assert.Equal(("Sales", "Add member", "Done"), (vm.Dialog.Title, vm.Dialog.ConfirmText, vm.Dialog.CloseText));
        Assert.Equal("A lower routing priority is offered a booking first.", vm.Hint);
        Assert.False(vm.ShowEmpty);
        var rotation = vm.Hosts[1];
        Assert.Equal("Rotation Host, rotation@example.com", rotation.ToString());
        Assert.Equal("TeamHostu-2", rotation.AutomationId);
        Assert.Equal(
            ("Routing priority: Rotation Host", "Save priority: Rotation Host", "Remove: Rotation Host"),
            (rotation.PriorityName, rotation.SaveName, rotation.RemoveName));
        Assert.Equal("5", rotation.Priority);
        Assert.True(vm.HasCandidates);
        Assert.Equal(("Host", "Choose a host"), (vm.AddLabel, vm.AddPlaceholder));
        Assert.Equal(2, vm.Candidates.Count);
        Assert.Equal(-1, vm.PickedIndex);
        Assert.True(vm.Editable);

        // What a box already holds, or the one picked, sends nothing.
        vm.SetPriority(rotation, "5");
        vm.Pick(-1);
        vm.Pick(7);
        Assert.Empty(sink.Sent);
        vm.Pick(0);
        vm.Pick(0);
        model.Show(Page(Team(hosts: Hosts(picked: 0))));
        Assert.Equal(0, vm.PickedIndex);
        Assert.True(vm.Dialog.ConfirmCommand.CanExecute(null));
        vm.Dialog.ConfirmCommand.Execute(null);
        vm.SetPriority(rotation, "2");
        vm.SetPriority(rotation, "2");
        vm.SavePriority(rotation);
        // One press, one save.
        vm.SavePriority(rotation);
        Assert.Equal(
            new TeamAction[]
            {
                new TeamAction.Pick("u-3"),
                new TeamAction.AddMember(),
                new TeamAction.EditPriority("u-2", "2"),
                new TeamAction.SavePriority("u-2"),
            },
            sink.Sent.Select(Action).ToArray());

        // The next snapshot keeps the rows while the hosts are the same, the typed box with them.
        model.Show(Page(Team(hosts: Hosts(working: true, rotationPriority: "2"))));
        Assert.Same(rotation, vm.Hosts[1]);
        Assert.False(vm.Editable);
        var before = sink.Sent.Count;
        vm.Pick(1);
        vm.AskRemove(rotation);
        Assert.Equal(before, sink.Sent.Count);
        model.Show(Page(Team(hosts: Hosts(notice: "Routing priority saved"))));
        Assert.True(vm.HasNotice);
        Assert.Equal("Routing priority saved", vm.Notice);
        model.Show(Page(Team(hosts: Hosts(priorityError: "A routing priority is a whole number, zero or more."))));
        Assert.True(vm.HasPriorityError);
        Assert.Equal("A routing priority is a whole number, zero or more.", vm.PriorityError);

        // A removal is asked once more, then confirmed once.
        vm.AskRemove(rotation);
        model.Show(Page(Team(hosts: Hosts(asking: true))));
        Assert.True(vm.Asking);
        Assert.Equal(("Remove this host?", "Remove", "Keep them"), (vm.AskTitle, vm.AskConfirm, vm.AskCancel));
        Assert.StartsWith("Bookings stop routing", vm.AskBody);
        vm.CancelRemoveCommand.Execute(null);
        vm.ConfirmRemoveCommand.Execute(null);
        Assert.False(vm.ConfirmRemoveCommand.CanExecute(null));
        vm.ConfirmRemoveCommand.Execute(null);
        Assert.Equal(
            new TeamAction[] { new TeamAction.AskRemove("u-2"), new TeamAction.CancelRemove(), new TeamAction.ConfirmRemove() },
            sink.Sent.Skip(before).Select(Action).ToArray());

        // A host gone: the rows are built again; nobody left to add says so.
        model.Show(Page(Team(hosts: Hosts(oneHost: true, noneToAdd: true, notice: "Host removed from the team"))));
        Assert.Single(vm.Hosts);
        Assert.False(vm.HasCandidates);
        Assert.True(vm.HasAddNone);
        Assert.Equal("Every host is already in this team.", vm.AddNone);

        model.Show(Page(Team()));
        Assert.False(vm.Dialog.IsOpen);
        Assert.Empty(vm.Hosts);
        Assert.Empty(vm.Candidates);
        Assert.Equal(-1, vm.PickedIndex);
        model.Show(Page(Team(hosts: Hosts(oneHost: true))));
        Assert.Single(vm.Hosts);
        Assert.False(vm.ShowEmpty);
    }

    [Fact]
    public void AnAccountIsArchivedOnceNothingStandsInTheWay()
    {
        var (model, sink) = Attached();
        model.Show(Page(Team(archive: Archive(status: new LoadStatus.Loading()))));
        var vm = model.Team.Archive;
        Assert.True(vm.Dialog.IsOpen);
        Assert.True(vm.Dialog.Destructive);
        Assert.Equal(("Archive Ann's account?", "Archive account", "Keep account"), (vm.Dialog.Title, vm.Dialog.ConfirmText, vm.Dialog.CloseText));
        Assert.StartsWith("They can no longer sign in", vm.Dialog.Body);
        Assert.True(vm.Loading && vm.HasBlocked);
        Assert.Equal("Checking their upcoming bookings…", vm.Blocked);
        Assert.False(vm.Dialog.ConfirmCommand.CanExecute(null));

        model.Show(Page(Team(archive: Archive(blocked: true))));
        Assert.Equal("Reassign or cancel their upcoming bookings first.", vm.Blocked);
        var booking = Assert.Single(vm.Bookings);
        Assert.Equal(("Site visit", "Dana Booker"), (booking.EventType, booking.Attendee));
        Assert.False(string.IsNullOrEmpty(booking.When));
        Assert.Equal("Site visit, Dana Booker, " + booking.When, booking.ToString());
        Assert.False(vm.Dialog.ConfirmCommand.CanExecute(null));
        model.Show(Page(Team(archive: Archive(status: _failed))));
        Assert.True(vm.Failed);
        Assert.Equal("Offline.", vm.FailureMessage);

        // The dialog's confirm asks; the question's confirm archives, once.
        model.Show(Page(Team(archive: Archive())));
        Assert.False(vm.HasBlocked || vm.Asking || vm.Loading || vm.Failed);
        Assert.Empty(vm.Bookings);
        vm.ConfirmArchiveCommand.Execute(null);
        Assert.Empty(sink.Sent);
        vm.Dialog.ConfirmCommand.Execute(null);
        model.Show(Page(Team(archive: Archive(asking: true))));
        Assert.True(vm.Asking);
        Assert.Equal(("Archive Ann's account?", "Archive account", "Keep account"), (vm.AskTitle, vm.AskConfirm, vm.AskCancel));
        Assert.Equal("They can no longer sign in.", vm.AskBody);
        Assert.False(vm.Dialog.CanConfirm);
        vm.CancelArchiveCommand.Execute(null);
        vm.ConfirmArchiveCommand.Execute(null);
        Assert.False(vm.ConfirmArchiveCommand.CanExecute(null));
        vm.ConfirmArchiveCommand.Execute(null);
        Assert.Equal(
            new TeamAction[] { new TeamAction.AskArchive(), new TeamAction.CancelArchive(), new TeamAction.ConfirmArchive() },
            sink.Sent.Select(Action).ToArray());

        model.Show(Page(Team(archive: Archive(working: true))));
        Assert.True(vm.Dialog.Working);
        model.Show(Page(Team(archive: Archive(notice: "Ann's scheduler account is closed"))));
        Assert.True(vm.HasNotice);
        Assert.Equal("Ann's scheduler account is closed", vm.Notice);
        Assert.False(vm.Dialog.CanConfirm);
        model.Show(Page(Team(archive: Archive(failure: _refused))));
        Assert.True(vm.Dialog.HasFailure);
        Assert.False(vm.HasNotice);
    }

    [Fact]
    public void AnotherSectionClosesTheDialogs()
    {
        var (model, _) = Attached();
        model.Show(Page(Team(editor: Editor(), hosts: Hosts(), archive: Archive(asking: true))));
        Assert.True(model.Team.Editor.Dialog.IsOpen && model.Team.Hosts.Dialog.IsOpen && model.Team.Archive.Dialog.IsOpen);
        model.Show(new SchedulingAdminView("Overview", _sections, new SchedulingAdminPage.Overview(
            new SchedulingOverviewView("Times in UTC", "Your booking desk", [], "Coming up", new LoadStatus.Loading(), [], "Nothing coming up."))));
        Assert.False(model.ShowsTeam);
        Assert.False(model.Team.Editor.Dialog.IsOpen || model.Team.Hosts.Dialog.IsOpen || model.Team.Archive.Dialog.IsOpen);
        Assert.False(model.Team.Archive.Asking);
        Assert.Empty(model.Team.Hosts.Hosts);
    }

    [Fact]
    public void NothingIsSentBeforeAttaching()
    {
        var model = new SchedulingAdminViewModel();
        model.Show(Page(Team(editor: Editor(), hosts: Hosts(), archive: Archive(asking: true))));
        var team = model.Team;
        team.OpenCreateCommand.Execute(null);
        team.OpenEditor(team.Teams[0]);
        team.OpenArchive(team.Members[0]);
        team.Editor.Name = "Desk";
        team.Hosts.Pick(1);
        team.Hosts.SetPriority(team.Hosts.Hosts[0], "4");
        team.Hosts.SavePriority(team.Hosts.Hosts[0]);
        team.Archive.ConfirmArchiveCommand.Execute(null);
        var (context, sink) = Pages.Context();
        model.Attach(context);
        Assert.Empty(sink.Sent);
    }
}
