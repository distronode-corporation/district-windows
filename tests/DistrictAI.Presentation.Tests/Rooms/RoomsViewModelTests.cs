using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels.Rooms;
using Xunit;

namespace DistrictAI.Presentation.Tests.Rooms;

public sealed class RoomsViewModelTests
{
    private static RoomView Room(bool canSpeak = true, bool muted = false, bool canMute = true, string? link = "https://www.distronode.com/meet/x", string? notice = null) =>
        new("Room \"standup\"", "In the room.", ["Ada", "A guest"], "With Ada, A guest.", notice, canSpeak, muted, canMute, link,
            "Copy guest link", "Copy a link that lets someone without an account join this room", "Guest link copied.", "Leave the room");

    private static MeetingRowView Row(string id = "m1", bool running = false) =>
        new(id, "Weekly review", "2026-08-14T15:00:00Z", "42m 0s, 2 people", running ? "Minutes are written when the meeting ends." : null,
            running ? "In progress" : null, running, "Rejoin");

    private static MeetingRecordView Record(LoadStatus? status = null, ReportAvailability report = ReportAvailability.InApp, string[]? items = null) =>
        new("m1", "Weekly review", status ?? V.Ready,
            [new FactView("Status", "Completed"), new FactView("Length", "42m 0s")],
            "2026-08-14T15:00:00Z", "2026-08-14T15:42:00Z",
            "Minutes", "The team reviewed the week.", report,
            "Action items", items ?? ["Rewrite the greeting (Grace)"], report,
            "Transcript", "Ada: Hello.");

    private static RoomsView View(
        string name = "",
        bool canJoin = false,
        bool joining = false,
        string? busy = null,
        FailureView? failure = null,
        RoomView? room = null,
        LoadStatus? meetingsStatus = null,
        EmptyView? empty = null,
        MeetingRowView[]? meetings = null,
        MeetingRecordView? record = null) =>
        new("Meeting rooms", "Start or join a room", "Room name", name, "Name the room.", "The Companion joins every room and writes up the minutes.",
            busy, "Join", canJoin, joining, failure, room, "Meetings", "The most recent meetings.",
            meetingsStatus ?? V.Ready, empty, meetings ?? [Row(), Row("m2", running: true)], false, record);

    [Fact]
    public void TheLobbyShowsTheFormAndTheMeetings()
    {
        var model = new RoomsViewModel();
        model.Show(View(busy: "Finish the call you are on to join a room."));

        Assert.Equal("Meeting rooms", model.Title);
        Assert.Equal(("Start or join a room", "Room name", "Join"), (model.StartTitle, model.RoomNameLabel, model.JoinLabel));
        Assert.False(model.CanJoin);
        Assert.False(model.JoinCommand.CanExecute(null));
        Assert.True(model.HasBusyNote);
        Assert.False(model.InRoom);
        Assert.True(model.MeetingsShown);
        Assert.False(model.RecordOpen);
        Assert.True(model.Load.Ready);
        Assert.Equal(["m1", "m2"], model.Meetings.Select(row => row.MeetingId));
        Assert.False(model.Meetings[0].IsInProgress);
        Assert.True(model.Meetings[1].IsInProgress);
        Assert.True(model.Meetings[1].HasMinutes);
        Assert.StartsWith("Weekly review, In progress, ", model.Meetings[1].AccessibleName, StringComparison.Ordinal);
        Assert.Equal(model.Meetings[1].AccessibleName, model.Meetings[1].ToString());
    }

    [Fact]
    public void NoMeetingsYetIsSaid()
    {
        var model = new RoomsViewModel();
        model.Show(View(meetings: [], empty: new EmptyView("No meetings yet", "Meetings appear here.")));
        Assert.True(model.Load.ShowEmpty);
        Assert.Equal("No meetings yet", model.Load.EmptyTitle);
    }

    [Fact]
    public void TheNameIsSentAsTypedAndTheCoresOnlyWhenNew()
    {
        var model = new RoomsViewModel();
        var (context, sink) = Pages.Context();
        model.Attach(context);
        model.Show(View());

        model.RoomName = "Week";
        model.RoomName = "Weekly";
        // The core's answer to the first does not take back what was typed since.
        model.Show(View(name: "Week", canJoin: true));
        Assert.Equal("Weekly", model.RoomName);
        // A name the box did not send (cleared after a join) is written.
        model.Show(View(name: "Weekly"));
        model.Show(View(name: ""));
        Assert.Equal(string.Empty, model.RoomName);

        Assert.Equal(
            [
                new UiEvent.Rooms(new RoomsAction.EditRoomName("Week")),
                new UiEvent.Rooms(new RoomsAction.EditRoomName("Weekly")),
            ],
            sink.Sent);
    }

    [Fact]
    public void JoinMuteLeaveAndRejoinAreSent()
    {
        var model = new RoomsViewModel();
        var (context, sink) = Pages.Context();
        model.Attach(context);
        model.Show(View(canJoin: true));
        model.JoinCommand.Execute(null);

        model.Show(View(room: Room(muted: false)));
        Assert.True(model.InRoom);
        Assert.Equal("Mute", model.MuteLabel);
        model.ToggleMuteCommand.Execute(null);
        model.Show(View(room: Room(muted: true)));
        Assert.Equal("Unmute", model.MuteLabel);
        model.ToggleMuteCommand.Execute(null);
        model.LeaveCommand.Execute(null);
        model.Rejoin(model.Meetings[0]);
        model.Rejoin(model.Meetings[1]);
        model.OpenRecord(model.Meetings[0]);
        model.CloseRecordCommand.Execute(null);
        model.DismissFailureCommand.Execute(null);

        Assert.Equal(
            [
                new UiEvent.Rooms(new RoomsAction.Join()),
                new UiEvent.Rooms(new RoomsAction.Microphone(false)),
                new UiEvent.Rooms(new RoomsAction.Microphone(true)),
                new UiEvent.Rooms(new RoomsAction.Leave()),
                new UiEvent.Rooms(new RoomsAction.Rejoin("m2")),
                new UiEvent.Rooms(new RoomsAction.OpenRecord("m1")),
                new UiEvent.Rooms(new RoomsAction.CloseRecord()),
                new UiEvent.Rooms(new RoomsAction.DismissFailure()),
            ],
            sink.Sent);
    }

    [Fact]
    public void TheRoomShowsItsPeopleAndTheGuestLinkIsCopiedNotShown()
    {
        var model = new RoomsViewModel();
        model.Show(View(room: Room(notice: "Your microphone could not be used, so nobody can hear you.")));
        Assert.Equal("Room \"standup\"", model.RoomTitle);
        Assert.Equal("In the room.", model.RoomState);
        Assert.Equal(["Ada", "A guest"], model.People);
        Assert.True(model.HasRoomNotice);
        Assert.True(model.CanSpeak && model.CanMute);
        Assert.True(model.HasGuestLink);
        Assert.Equal("https://www.distronode.com/meet/x", model.GuestLink);
        Assert.False(model.HasCopiedNote);

        model.LinkCopied(false);
        Assert.False(model.HasCopiedNote);
        model.LinkCopied(true);
        Assert.Equal("Guest link copied.", model.CopiedNote);

        // Still in the room, the note stays; a new room starts without it.
        model.Show(View(room: Room()));
        Assert.True(model.HasCopiedNote);
        model.Show(View());
        Assert.False(model.HasCopiedNote);
        Assert.False(model.HasGuestLink);
        model.LinkCopied(true);
        Assert.False(model.HasCopiedNote);
    }

    [Fact]
    public void AViewerListensWithNoMuteAndNoLink()
    {
        var model = new RoomsViewModel();
        model.Show(View(room: Room(canSpeak: false, muted: true, canMute: false, link: null)));
        Assert.False(model.CanSpeak);
        Assert.False(model.ToggleMuteCommand.CanExecute(null));
        Assert.False(model.HasGuestLink);
        Assert.Null(model.GuestLink);
    }

    [Fact]
    public void AFailureShowsUntilDismissed()
    {
        var model = new RoomsViewModel();
        model.Show(View(failure: V.Failure("The room could not be joined. Try again.")));
        Assert.True(model.HasFailure);
        Assert.Equal("The room could not be joined. Try again.", model.Failure);
        model.Show(View(joining: true));
        Assert.False(model.HasFailure);
        Assert.True(model.Joining);
    }

    [Fact]
    public void TheRecordReplacesTheListWithReportOnMinutesAndActionItems()
    {
        var model = new RoomsViewModel();
        model.Show(View(record: Record()), reportSending: false);
        Assert.True(model.RecordOpen);
        Assert.False(model.MeetingsShown);
        var record = model.Record;
        Assert.True(record.Ready);
        Assert.Equal("Weekly review", record.Title);
        Assert.Equal(["Status", "Started", "Ended", "Length"], record.Facts.Select(fact => fact.Label));
        Assert.Equal("The team reviewed the week.", record.Minutes);
        Assert.Equal(("Report", true), (record.MinutesReportLabel, record.HasMinutesReport));
        Assert.Equal(["Rewrite the greeting (Grace)"], record.ActionItems);
        Assert.True(record.HasActionItems && record.HasActionItemsReport);
        Assert.True(record.ReportEnabled);
        Assert.True(record.HasTranscript);
        Assert.Equal(new ReportTarget.MeetingMinutes("m1"), record.MinutesTarget);
        Assert.Equal(new ReportTarget.MeetingActionItems("m1"), record.ActionItemsTarget);

        // While a report is on its way, Report waits.
        model.Show(View(record: Record()), reportSending: true);
        Assert.False(model.Record.ReportEnabled);

        // A viewer reports on the web; minutes nobody wrote offer nothing.
        model.Show(View(record: Record(report: ReportAvailability.OnWeb)), reportSending: false);
        Assert.Equal("Report on the web", model.Record.MinutesReportLabel);
        model.Show(View(record: Record(report: ReportAvailability.Hidden, items: [])), reportSending: false);
        Assert.False(model.Record.HasMinutesReport);
        Assert.False(model.Record.HasActionItems);

        // Closed, the list is back.
        model.Show(View(), reportSending: false);
        Assert.False(model.RecordOpen);
        Assert.True(model.MeetingsShown);
    }

    [Fact]
    public void ARecordLoadingOrFailedSaysSo()
    {
        var model = new RoomsViewModel();
        model.Show(View(record: Record(V.Loading) with { Title = "Meeting", StartedAt = null, EndedAt = null, Facts = [] }));
        Assert.True(model.Record.Loading);
        Assert.Empty(model.Record.Facts);
        model.Show(View(record: Record(V.Failed(V.Failure("Not found."), "Could not load this meeting"))));
        Assert.True(model.Record.Failed);
        Assert.Equal(("Could not load this meeting", "Not found."), (model.Record.FailureTitle, model.Record.FailureMessage));
    }
}
