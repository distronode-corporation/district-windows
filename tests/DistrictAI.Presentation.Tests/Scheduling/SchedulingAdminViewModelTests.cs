using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels.Scheduling;
using Xunit;

namespace DistrictAI.Presentation.Tests.Scheduling;

public sealed class SchedulingAdminViewModelTests
{
    private static readonly SchedulingSectionEntryView[] _sections =
    [
        new(SchedulingAdminSection.Overview, "Overview", "Your booking desk", false),
        new(SchedulingAdminSection.Bookings, "Bookings", "Every booking", true),
    ];

    private static DialogView Dialog(bool canConfirm = true, bool working = false, FailureView? failure = null) =>
        new("Cancel this booking?", "The attendee gets an email.", "Cancel booking", "Keep it", canConfirm, working, true, failure);

    private static BookingRowView Row(string id = "bk-1", bool actionable = true) =>
        new(id, "Sep 14, 2099, 9:00 a.m.", "Dana · Phone · Sam", "dana@example.com", "Confirmed", RunTone.Success, actionable, actionable, actionable);

    private static SchedulingBookingsView Bookings(
        LoadStatus? status = null,
        BookingRowView[]? rows = null,
        string? notice = null,
        CancelDialogView? cancel = null,
        RescheduleDialogView? reschedule = null,
        ReassignDialogView? reassign = null,
        PagingView? paging = null) =>
        new(
            TimesIn: "Times in America/Toronto",
            Tabs:
            [
                new(BookingsTab.Upcoming, "Upcoming", true),
                new(BookingsTab.Past, "Past", false),
                new(BookingsTab.Cancelled, "Cancelled", false),
                new(BookingsTab.All, "All", false),
            ],
            Status: status ?? new LoadStatus.Ready(),
            Rows: rows ?? [Row(), Row("bk-2", false)],
            Empty: new EmptyView("No bookings", "Nothing is booked yet."),
            Paging: paging ?? new PagingView(true, false, null, false, null),
            LoadMore: "Load more",
            CancelLabel: "Cancel booking",
            RescheduleLabel: "Move booking",
            ReassignLabel: "Change host",
            Notice: notice,
            Cancel: cancel,
            Reschedule: reschedule,
            Reassign: reassign);

    private static SchedulingAdminView Page(SchedulingAdminPage page, string title = "Bookings") =>
        new(title, _sections, page);

    private static (SchedulingAdminViewModel Model, RecordingSink Sink) Attached()
    {
        var model = new SchedulingAdminViewModel();
        var (context, sink) = Pages.Context();
        model.Attach(context);
        return (model, sink);
    }

    private static UiEvent ListEvent(BookingsAction action) =>
        new UiEvent.SchedulingAdmin(new SchedulingAdminAction.Bookings(action));

    private static UiEvent DetailEvent(BookingAction action) =>
        new UiEvent.SchedulingAdmin(new SchedulingAdminAction.Booking(action));

    [Fact]
    public void ThePageShowsItsSectionAndTheNavigationOpensAnother()
    {
        var (model, sink) = Attached();
        model.Show(Page(new SchedulingAdminPage.Bookings(Bookings())));
        Assert.Equal("Bookings", model.Title);
        Assert.True(model.ShowsBookings);
        Assert.False(model.ShowsOverview || model.ShowsBooking);
        Assert.Equal(1, model.SelectedSection);
        Assert.Equal("SchedulingTabOverview", model.Sections[0].TabAutomationId);
        model.OpenSection(1);
        model.OpenSection(5);
        model.OpenSection(-1);
        Assert.Empty(sink.Sent);
        model.OpenSection(0);
        Assert.Equal([new UiEvent.SchedulingAdmin(new SchedulingAdminAction.Open(SchedulingAdminSection.Overview))], sink.Sent);
    }

    [Fact]
    public void TheOverviewShowsEachRowInItsOwnState()
    {
        var (model, _) = Attached();
        var overview = new SchedulingOverviewView(
            TimesIn: "Times in UTC",
            DeskHeading: "Your booking desk",
            Desk:
            [
                new("Booking page", BookingDeskRowState.Lines, ["https://book.example.com/book/a", "https://book.example.com/book/b"], null),
                new("Calendar", BookingDeskRowState.Failed, [], new FailureView("The booking system did not answer.", null, true)),
                new("Working hours", BookingDeskRowState.Hint, ["Not bookable on any day yet."], null),
                new("Event types", BookingDeskRowState.Loading, [], null),
            ],
            ComingUpHeading: "Coming up",
            ComingUpStatus: new LoadStatus.Ready(),
            ComingUp: [new("bk-1", "Today 2:30 p.m.", "Dana · Phone", "Cancelled", RunTone.Danger)],
            ComingUpEmpty: "No bookings yet.");
        model.Show(Page(new SchedulingAdminPage.Overview(overview), "Overview"));
        var vm = model.Overview;
        Assert.True(model.ShowsOverview);
        Assert.Equal("Times in UTC", vm.TimesIn);
        Assert.Equal("Your booking desk", vm.DeskHeading);
        Assert.Equal(4, vm.Desk.Count);
        Assert.True(vm.Desk[0].IsValue);
        Assert.True(vm.Desk[0].HasMore);
        Assert.Equal("https://book.example.com/book/b", vm.Desk[0].More);
        Assert.Equal("Booking page: https://book.example.com/book/a. https://book.example.com/book/b", vm.Desk[0].ToString());
        Assert.True(vm.Desk[1].IsFailed);
        Assert.Equal("The booking system did not answer.", vm.Desk[1].Value);
        Assert.Equal("Calendar: The booking system did not answer.", vm.Desk[1].AccessibleName);
        Assert.True(vm.Desk[2].IsHint);
        Assert.True(vm.Desk[3].IsLoading);
        Assert.Equal(string.Empty, vm.Desk[3].Value);
        Assert.Equal("Coming up", vm.ComingUpHeading);
        Assert.False(vm.ComingUpLoading || vm.HasComingUpFailure || vm.ShowComingUpEmpty);
        var row = Assert.Single(vm.ComingUp);
        Assert.True(row.IsDanger);
        Assert.False(row.IsSuccess || row.IsNeutral);
        Assert.Equal("Today 2:30 p.m., Dana · Phone, Cancelled", row.ToString());

        model.Show(Page(new SchedulingAdminPage.Overview(overview with
        {
            ComingUpStatus = new LoadStatus.Failed(new FailureView("Offline.", null, true), "Could not load this section"),
            ComingUp = [],
        }), "Overview"));
        Assert.True(vm.HasComingUpFailure);
        Assert.Equal("Offline.", vm.ComingUpFailure);
        Assert.False(vm.ShowComingUpEmpty);

        model.Show(Page(new SchedulingAdminPage.Overview(overview with { ComingUp = [] }), "Overview"));
        Assert.True(vm.ShowComingUpEmpty);
        Assert.Equal("No bookings yet.", vm.ComingUpEmpty);

        model.Show(Page(new SchedulingAdminPage.Overview(overview with { ComingUpStatus = new LoadStatus.Loading() }), "Overview"));
        Assert.True(vm.ComingUpLoading);
        Assert.True(new ComingUpEntry("b", "w", "s", "Rescheduled", RunTone.Neutral).IsNeutral);
    }

    [Fact]
    public void TheBookingsListTheirRowsWithTheActionsTheCoreOffers()
    {
        var (model, sink) = Attached();
        model.Show(Page(new SchedulingAdminPage.Bookings(Bookings())));
        var vm = model.Bookings;
        Assert.Equal(["Upcoming", "Past", "Cancelled", "All"], vm.Tabs);
        Assert.Equal(0, vm.SelectedTab);
        Assert.Equal("Times in America/Toronto", vm.TimesIn);
        Assert.Equal("Load more", vm.LoadMoreLabel);
        Assert.True(vm.Load.Ready);
        Assert.True(vm.Paging.CanLoadMore);
        Assert.Equal(2, vm.Rows.Count);
        var row = vm.Rows[0];
        Assert.True(row.HasActions && row.HasEmail && row.IsSuccess);
        Assert.False(row.IsWarning || row.IsDanger || row.IsNeutral);
        Assert.Equal("BookingRowbk-1", row.AutomationId);
        Assert.Equal("Cancel booking", row.CancelLabel);
        Assert.Equal("Sep 14, 2099, 9:00 a.m., Dana · Phone · Sam, Confirmed", row.ToString());
        Assert.False(vm.Rows[1].HasActions);

        vm.SelectTab(0);
        vm.SelectTab(9);
        vm.OpenCancel(vm.Rows[1]);
        vm.OpenReschedule(vm.Rows[1]);
        vm.OpenReassign(vm.Rows[1]);
        Assert.Empty(sink.Sent);

        vm.SelectTab(2);
        vm.Open(row);
        vm.OpenCancel(row);
        vm.OpenReschedule(row);
        vm.OpenReassign(row);
        vm.Paging.LoadMoreCommand.Execute(null);
        Assert.Equal(
            [
                ListEvent(new BookingsAction.SelectTab(BookingsTab.Cancelled)),
                new UiEvent.SchedulingAdmin(new SchedulingAdminAction.OpenBooking("bk-1")),
                ListEvent(new BookingsAction.OpenCancel("bk-1")),
                ListEvent(new BookingsAction.OpenReschedule("bk-1")),
                ListEvent(new BookingsAction.OpenReassign("bk-1")),
                ListEvent(new BookingsAction.LoadMore()),
            ],
            sink.Sent);
    }

    [Fact]
    public void AFailedFirstPageIsAStatusPageAndAnEmptyViewSaysSo()
    {
        var (model, _) = Attached();
        model.Show(Page(new SchedulingAdminPage.Bookings(Bookings(
            status: new LoadStatus.Failed(new FailureView("Offline.", null, true), "Could not load this section"),
            rows: []))));
        Assert.True(model.Bookings.Load.Failed);
        Assert.True(model.Bookings.Load.CanRetry);
        model.Show(Page(new SchedulingAdminPage.Bookings(Bookings(rows: []))));
        Assert.True(model.Bookings.Load.ShowEmpty);
        Assert.Equal("No bookings", model.Bookings.Load.EmptyTitle);
    }

    [Fact]
    public void TheCancelIsConfirmedOnceAndALandedOneIsSaidOverTheList()
    {
        var (model, sink) = Attached();
        var cancel = new CancelDialogView(Dialog(), "Reason", "Optional.", string.Empty, null);
        model.Show(Page(new SchedulingAdminPage.Bookings(Bookings(cancel: cancel))));
        var vm = model.Bookings.Cancel;
        Assert.True(vm.Dialog.IsOpen);
        Assert.True(vm.Dialog.Destructive);
        Assert.Equal("Cancel this booking?", vm.Dialog.Title);
        Assert.Equal("Keep it", vm.Dialog.CloseText);
        Assert.Equal("Reason", vm.ReasonLabel);
        Assert.Equal("Optional.", vm.ReasonHint);
        vm.EditReason(string.Empty);
        vm.EditReason("Moved away");
        vm.Dialog.ConfirmCommand.Execute(null);
        Assert.False(vm.Dialog.CanConfirm);
        Assert.False(vm.Dialog.ConfirmCommand.CanExecute(null));

        model.Show(Page(new SchedulingAdminPage.Bookings(Bookings(cancel: cancel with
        {
            Dialog = Dialog(failure: new FailureView("That did not save. Try again.", null, true)),
            Reason = "Moved away",
            ReasonRejected = "Too long.",
        }))));
        Assert.True(vm.Dialog.HasFailure);
        Assert.Equal("That did not save. Try again.", vm.Dialog.Failure);
        Assert.True(vm.HasReasonRejected);
        Assert.Equal("Too long.", vm.ReasonRejected);
        vm.Dialog.CloseCommand.Execute(null);

        model.Show(Page(new SchedulingAdminPage.Bookings(Bookings(notice: "Booking cancelled"))));
        Assert.False(vm.Dialog.IsOpen);
        Assert.True(model.Bookings.HasNotice);
        Assert.Equal("Booking cancelled", model.Bookings.Notice);
        model.Bookings.DismissNoticeCommand.Execute(null);
        Assert.Equal(
            [
                ListEvent(new BookingsAction.EditCancelReason("Moved away")),
                ListEvent(new BookingsAction.Confirm()),
                ListEvent(new BookingsAction.CloseDialog()),
                ListEvent(new BookingsAction.CloseDialog()),
            ],
            sink.Sent);
    }

    [Fact]
    public void AMoveIsADayAndATime()
    {
        var (model, sink) = Attached();
        var reschedule = new RescheduleDialogView(
            Dialog: Dialog(canConfirm: false),
            TimesIn: "Times in America/Toronto",
            DayLabel: "Day",
            Day: "2099-09-14",
            DayText: "Sep 14, 2099",
            TimeLabel: "Time",
            SlotsStatus: new LoadStatus.Ready(),
            Slots: [new("2099-09-14T13:00:00Z", "9:00 a.m.", false), new("2099-09-14T14:00:00Z", "10:00 a.m.", true)],
            NoSlots: "Nothing free that day.",
            SelectionRejected: "Pick a time.");
        model.Show(Page(new SchedulingAdminPage.Bookings(Bookings(reschedule: reschedule))));
        var vm = model.Bookings.Reschedule;
        Assert.True(vm.Dialog.IsOpen);
        Assert.False(vm.Dialog.ConfirmCommand.CanExecute(null));
        Assert.Equal("Times in America/Toronto", vm.TimesIn);
        Assert.Equal("Day", vm.DayLabel);
        Assert.Equal("Time", vm.TimeLabel);
        Assert.Equal(new DateTime(2099, 9, 14), vm.DayDate?.DateTime);
        Assert.True(vm.HasSelectionRejected);
        Assert.Equal("Pick a time.", vm.SelectionRejected);
        Assert.True(vm.Slots.HasChoices);
        Assert.Equal(1, vm.Slots.SelectedIndex);
        Assert.Equal("9:00 a.m.", vm.Slots.Choices[0].ToString());
        vm.PickDay(vm.DayDate!.Value);
        vm.Slots.Pick(1);
        vm.Slots.Pick(7);
        Assert.Empty(sink.Sent);
        vm.PickDay(new DateTimeOffset(2099, 9, 15, 0, 0, 0, TimeSpan.FromHours(-4)));
        vm.Slots.Pick(0);
        Assert.Equal(
            [
                ListEvent(new BookingsAction.PickDay("2099-09-15")),
                ListEvent(new BookingsAction.PickSlot("2099-09-14T13:00:00Z")),
            ],
            sink.Sent);

        model.Show(Page(new SchedulingAdminPage.Bookings(Bookings(reschedule: reschedule with { Slots = [], SelectionRejected = null }))));
        Assert.True(vm.Slots.ShowEmpty);
        Assert.Equal("Nothing free that day.", vm.Slots.EmptyText);
        Assert.False(vm.HasSelectionRejected);
        Assert.Null(RescheduleBookingViewModel.ParseDay("tomorrow"));
    }

    [Fact]
    public void AHandOverListsTheHostsAndAFailedReadIsOfferedAgain()
    {
        var (model, sink) = Attached();
        var reassign = new ReassignDialogView(
            Dialog(canConfirm: false),
            "New host",
            new LoadStatus.Failed(new FailureView("Offline.", null, true), "Could not load this section"),
            [],
            "No other host can take it.");
        model.Show(Page(new SchedulingAdminPage.Bookings(Bookings(reassign: reassign))));
        var vm = model.Bookings.Reassign;
        Assert.Equal("New host", vm.HostLabel);
        Assert.True(vm.Hosts.Failed);
        Assert.Equal("Offline.", vm.Hosts.FailureMessage);
        Assert.True(vm.Hosts.RetryCommand.CanExecute(null));
        vm.Hosts.RetryCommand.Execute(null);
        Assert.False(vm.Hosts.CanRetry);

        model.Show(Page(new SchedulingAdminPage.Bookings(Bookings(reassign: reassign with
        {
            HostsStatus = new LoadStatus.Loading(),
        }))));
        Assert.True(vm.Hosts.Loading);
        model.Show(Page(new SchedulingAdminPage.Bookings(Bookings(reassign: reassign with
        {
            HostsStatus = new LoadStatus.Ready(),
            Hosts = [new("sched-user-2", "Sam Host", false)],
        }))));
        vm.Hosts.Pick(0);
        Assert.Equal(
            [ListEvent(new BookingsAction.RetryDialogRead()), ListEvent(new BookingsAction.PickHost("sched-user-2"))],
            sink.Sent);
    }

    [Fact]
    public void OneBookingShowsItsAnswersAndOffersTheCancel()
    {
        var (model, sink) = Attached();
        var booking = new SchedulingBookingView(
            Id: "bk-1",
            Summary: Row(),
            Status: new LoadStatus.Ready(),
            AnswersHeading: "Answers",
            Answers: [new FactView("Which product?", "District"), new FactView("Anything else?", "No answer")],
            NoQuestions: "This event type asks no questions.",
            ManageHeading: "Manage",
            OffersCancel: true,
            CancelLabel: "Cancel booking",
            Notice: null,
            Cancel: null);
        model.Show(Page(new SchedulingAdminPage.Booking(booking), "Booking"));
        var vm = model.Booking;
        Assert.True(model.ShowsBooking);
        Assert.True(vm.HasSummary);
        Assert.Equal("Sep 14, 2099, 9:00 a.m.", vm.When);
        Assert.Equal("Dana · Phone · Sam", vm.Subtitle);
        Assert.Equal("dana@example.com", vm.Email);
        Assert.Equal("Confirmed", vm.StatusLabel);
        Assert.True(vm.IsSuccess);
        Assert.False(vm.IsDanger || vm.IsNeutral);
        Assert.Equal("Answers", vm.AnswersHeading);
        Assert.Equal(2, vm.Answers.Count);
        Assert.False(vm.ShowNoQuestions);
        Assert.Equal("Manage", vm.ManageHeading);
        Assert.Equal("Cancel booking", vm.CancelLabel);
        Assert.True(vm.OpenCancelCommand.CanExecute(null));
        vm.OpenCancelCommand.Execute(null);

        model.Show(Page(new SchedulingAdminPage.Booking(booking with
        {
            Cancel = new CancelDialogView(Dialog(), "Reason", "Optional.", string.Empty, null),
        }), "Booking"));
        Assert.True(vm.Cancel.Dialog.IsOpen);
        vm.Cancel.Dialog.ConfirmCommand.Execute(null);

        model.Show(Page(new SchedulingAdminPage.Booking(booking with
        {
            Summary = null,
            Answers = [],
            OffersCancel = false,
            Notice = "Booking cancelled",
        }), "Booking"));
        Assert.False(vm.HasSummary);
        Assert.False(vm.IsNeutral);
        Assert.True(vm.ShowNoQuestions);
        Assert.Equal("This event type asks no questions.", vm.NoQuestions);
        Assert.False(vm.OpenCancelCommand.CanExecute(null));
        Assert.True(vm.HasNotice);
        vm.DismissNoticeCommand.Execute(null);
        Assert.Equal(
            [DetailEvent(new BookingAction.OpenCancel()), DetailEvent(new BookingAction.Confirm()), DetailEvent(new BookingAction.CloseDialog())],
            sink.Sent);
    }

    [Fact]
    public void NothingIsSentBeforeAttaching()
    {
        var model = new SchedulingAdminViewModel();
        model.Show(Page(new SchedulingAdminPage.Bookings(Bookings())));
        model.OpenSection(0);
        model.Bookings.OpenCancel(model.Bookings.Rows[0]);
        model.Bookings.Cancel.EditReason("x");
        model.Bookings.Reschedule.PickDay(DateTimeOffset.UnixEpoch);
        model.Bookings.Reschedule.Slots.Pick(0);
        model.Booking.Send(new BookingAction.Confirm());
        var (context, sink) = Pages.Context();
        model.Attach(context);
        Assert.Empty(sink.Sent);
    }
}
