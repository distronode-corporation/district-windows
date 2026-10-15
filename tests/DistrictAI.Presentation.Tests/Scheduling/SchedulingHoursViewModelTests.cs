using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels.Scheduling;
using Xunit;

namespace DistrictAI.Presentation.Tests.Scheduling;

public sealed class SchedulingHoursViewModelTests
{
    private static readonly SchedulingSectionEntryView[] _sections =
    [
        new(SchedulingAdminSection.Overview, "Overview", "Your booking desk", false),
        new(SchedulingAdminSection.EventTypes, "Event types", "The pages a customer can book", false),
        new(SchedulingAdminSection.Hours, "Working hours", "The weekly hours you are bookable, and the dates you are not", true),
        new(SchedulingAdminSection.Bookings, "Bookings", "Every booking", false),
    ];

    private static readonly LoadStatus _failed =
        new LoadStatus.Failed(new FailureView("Offline.", null, true), "Could not load this section");

    private static readonly LoadStatus _unretryable =
        new LoadStatus.Failed(new FailureView("You no longer have access.", null, false), "Could not load this section");

    private static DialogView Dialog(
        string title = "Your working hours",
        string confirm = "Save",
        bool canConfirm = true,
        bool working = false,
        FailureView? failure = null) =>
        new(title, string.Empty, confirm, "Cancel", canConfirm, working, false, failure);

    private static HoursDayView Day(uint day, string name, HoursRangeView[]? ranges = null, bool offersCopy = false) =>
        new(day, name, ranges ?? [], offersCopy);

    private static HoursDayView[] Days(HoursRangeView[]? monday = null, HoursRangeView[]? wednesday = null) =>
    [
        Day(0, "Monday", monday, monday is { Length: > 0 }),
        Day(1, "Tuesday"),
        Day(2, "Wednesday", wednesday ?? [new HoursRangeView(1, "10:30", "15:00", null)], true),
        Day(3, "Thursday"),
        Day(4, "Friday"),
        Day(5, "Saturday"),
        Day(6, "Sunday"),
    ];

    private static HoursWeekView Week(
        LoadStatus? status = null,
        HoursDayView[]? days = null,
        DialogView? dialog = null,
        bool editable = true,
        string? notice = null) =>
        new(
            Dialog: dialog ?? Dialog(),
            Status: status ?? new LoadStatus.Ready(),
            Editable: editable,
            Days: days ?? Days(),
            NotBookable: "Not bookable",
            AddRangeLabel: "Add range",
            CopyLabel: "Copy to all weekdays",
            ToLabel: "to",
            RemoveLabel: "Remove",
            StartPlaceholder: "09:00",
            EndPlaceholder: "17:00",
            Notice: notice);

    private static OverrideEditorView AddForm(string reason = "day_off", string date = "", string? validation = null, bool canSave = true) =>
        new(
            Title: "Add an override",
            Form: new OverrideFormView(reason, date, string.Empty, "09:00", "17:00"),
            Kinds: [new("day_off", "Unavailable", reason == "day_off"), new("custom_hours", "Custom hours", reason == "custom_hours")],
            KindLabel: "What happens that day",
            DateLabel: reason == "custom_hours" ? "Date" : "First day",
            DatePlaceholder: "2099-09-14",
            ShowsLastDay: reason != "custom_hours",
            LastDayLabel: "Last day",
            LastDayHint: "Leave blank for a single day.",
            ShowsHours: reason == "custom_hours",
            HoursLabel: "Hours that day",
            ToLabel: "to",
            StartPlaceholder: "09:00",
            EndPlaceholder: "17:00",
            SaveLabel: "Add override",
            CancelLabel: "Cancel",
            CanSave: canSave,
            Validation: validation);

    private static HoursOverridesView Overrides(
        LoadStatus? status = null,
        OverrideRowView[]? rows = null,
        OverrideEditorView? form = null,
        bool asking = false,
        string? notice = null,
        bool canChange = true,
        FailureView? failure = null) =>
        new(
            Dialog: Dialog("Date overrides", string.Empty, false, false, failure),
            Hint: "Days you are away, or working different hours from usual.",
            Status: status ?? new LoadStatus.Ready(),
            Rows: rows ??
            [
                new("group:grp-contract-1", "Sep 24, 2099", "12:00 to 16:00"),
                new("id:ovr-contract-2", "Sep 25, 2099", "Unavailable"),
            ],
            Empty: "No overrides ahead. Add one for a day off or different hours.",
            AddLabel: "Add override",
            CanChange: canChange,
            DeleteLabel: "Delete",
            Notice: notice,
            Form: form,
            Delete: asking ? new SchedulingAskView("Delete this override?", "All 3 days go back to your weekly hours.", "Delete override", "Cancel") : null);

    private static SchedulingHoursView Hours(
        LoadStatus? status = null,
        LoadStatus? overridesStatus = null,
        bool offersWrites = true,
        HoursWeekView? week = null,
        HoursOverridesView? overrides = null) =>
        new(
            TimesIn: "Times in America/Toronto",
            OffersWrites: offersWrites,
            WeekLabel: "Your working hours",
            OverridesLabel: "Date overrides",
            WeekHeading: "Working hours",
            Status: status ?? new LoadStatus.Ready(),
            Summary: "Wed, 10:30 to 15:00",
            Week: status is null
                ? [new FactView("Monday", "Not bookable"), new FactView("Wednesday", "10:30 to 15:00")]
                : [],
            OverridesHeading: "Date overrides",
            OverridesSubtitle: "Days you are away, or working different hours from usual.",
            OverridesStatus: overridesStatus ?? new LoadStatus.Ready(),
            Overrides: overridesStatus is null ? [new FactView("Sep 25, 2099", "Unavailable")] : [],
            OverridesEmpty: "No overrides ahead.",
            WeekEditor: week,
            OverridesEditor: overrides);

    private static SchedulingAdminView Page(SchedulingHoursView view) =>
        new("Working hours", _sections, new SchedulingAdminPage.Hours(view));

    private static (SchedulingAdminViewModel Model, RecordingSink Sink) Attached()
    {
        var model = new SchedulingAdminViewModel();
        var (context, sink) = Pages.Context();
        model.Attach(context);
        return (model, sink);
    }

    private static UiEvent.SchedulingAdmin Event(HoursAction action) =>
        new(new SchedulingAdminAction.Hours(action));

    /// <summary>The hours action <paramref name="sent"/> carries.</summary>
    private static HoursAction Action(UiEvent sent) =>
        Assert.IsType<SchedulingAdminAction.Hours>(Assert.IsType<UiEvent.SchedulingAdmin>(sent).Action).Action;

    [Fact]
    public void TheWeekAndTheOverridesShowAndTheEditorsOpen()
    {
        var (model, sink) = Attached();
        model.Show(Page(Hours()));
        var vm = model.Hours;
        Assert.True(model.ShowsHours);
        Assert.False(model.ShowsBookings || model.ShowsOverview || model.ShowsEventTypes || model.ShowsEventType || model.ShowsBooking);
        Assert.Equal(2, model.SelectedSection);
        Assert.True(vm.Load.Ready);
        Assert.Equal("Times in America/Toronto", vm.TimesIn);
        Assert.Equal("Working hours", vm.WeekHeading);
        Assert.Equal("Wed, 10:30 to 15:00", vm.Summary);
        Assert.Equal(2, vm.Week.Count);
        Assert.Equal("Monday: Not bookable", vm.Week[0].ToString());
        Assert.Equal("Date overrides", vm.OverridesHeading);
        Assert.Equal("Days you are away, or working different hours from usual.", vm.OverridesSubtitle);
        Assert.Equal("Sep 25, 2099: Unavailable", Assert.Single(vm.Overrides).ToString());
        Assert.False(vm.OverridesLoading || vm.HasOverridesFailure || vm.ShowNoOverrides);
        Assert.Equal("No overrides ahead.", vm.NoOverrides);
        Assert.Equal("Your working hours", vm.WeekLabel);
        Assert.Equal("Date overrides", vm.OverridesLabel);
        Assert.True(vm.OpenWeekCommand.CanExecute(null));
        vm.OpenWeekCommand.Execute(null);
        vm.OpenOverridesCommand.Execute(null);
        Assert.Equal(
            new UiEvent[] { Event(new HoursAction.OpenWeek()), Event(new HoursAction.OpenOverrides()) },
            sink.Sent);

        // A viewer reads the same; the week fails as a status page, the overrides alone.
        model.Show(Page(Hours(status: _failed, overridesStatus: _failed, offersWrites: false)));
        Assert.False(vm.OpenWeekCommand.CanExecute(null));
        Assert.True(vm.Load.Failed);
        Assert.True(vm.Load.CanRetry);
        Assert.Empty(vm.Week);
        Assert.True(vm.HasOverridesFailure);
        Assert.Equal("Offline.", vm.OverridesFailure);
        Assert.Empty(vm.Overrides);
        model.Show(Page(Hours(overridesStatus: new LoadStatus.Loading())));
        Assert.True(vm.OverridesLoading);
        Assert.False(vm.ShowNoOverrides);
        model.Show(Page(Hours() with { Overrides = [] }));
        Assert.True(vm.ShowNoOverrides);
    }

    [Fact]
    public void TheWeekIsEditedWindowByWindowAndSavedOnce()
    {
        var (model, sink) = Attached();
        model.Show(Page(Hours(week: Week(status: new LoadStatus.Loading(), days: [], dialog: Dialog(canConfirm: false), editable: false))));
        var vm = model.Hours.WeekEditor;
        Assert.True(vm.Dialog.IsOpen);
        Assert.True(vm.Loading);
        Assert.Empty(vm.Days);
        Assert.False(vm.Editable);
        Assert.False(vm.Dialog.ConfirmCommand.CanExecute(null));

        model.Show(Page(Hours(week: Week())));
        Assert.False(vm.Loading || vm.Failed || vm.HasNotice);
        Assert.True(vm.Editable);
        Assert.Equal("Your working hours", vm.Dialog.Title);
        Assert.Equal(7, vm.Days.Count);
        var monday = vm.Days[0];
        Assert.Equal("Monday", monday.ToString());
        Assert.Equal("HoursDay0", monday.AutomationId);
        Assert.True(monday.ShowsNone);
        Assert.False(monday.OffersCopy);
        Assert.Equal("Not bookable", monday.NotBookable);
        Assert.Equal("Add range: Monday", monday.AddRangeName);
        Assert.Equal("Copy to all weekdays: Monday", monday.CopyName);
        var wednesday = vm.Days[2];
        Assert.True(wednesday.OffersCopy);
        var window = Assert.Single(wednesday.Ranges);
        Assert.Equal(("10:30", "15:00"), (window.Start, window.End));
        Assert.Equal("Wednesday, range 1", window.ToString());
        Assert.Equal("Opens: Wednesday, range 1", window.StartName);
        Assert.Equal("Closes: Wednesday, range 1", window.EndName);
        Assert.Equal("Remove: Wednesday, range 1", window.RemoveName);
        Assert.Equal(("to", "09:00", "17:00"), (window.ToLabel, window.StartPlaceholder, window.EndPlaceholder));
        Assert.False(window.HasError);

        // What a box already holds sends nothing.
        vm.SetStart(window, "10:30");
        vm.SetEnd(window, "15:00");
        Assert.Empty(sink.Sent);
        vm.AddRange(monday);
        vm.CopyToWeekdays(wednesday);
        vm.SetStart(window, "9");
        vm.SetEnd(window, "16:00");
        vm.Remove(window);
        vm.Dialog.ConfirmCommand.Execute(null);
        Assert.False(vm.Dialog.CanConfirm);
        Assert.Equal(
            new HoursAction[]
            {
                new HoursAction.AddRange(0),
                new HoursAction.CopyToWeekdays(2),
                new HoursAction.SetRangeStart(2, 1, "9"),
                new HoursAction.SetRangeEnd(2, 1, "16:00"),
                new HoursAction.RemoveRange(2, 1),
                new HoursAction.SaveWeek(),
            },
            sink.Sent.Select(Action).ToArray());

        // The windows stay while none comes or goes; a fault changes in place.
        model.Show(Page(Hours(week: Week(
            days: Days(wednesday: [new HoursRangeView(1, "9", "16:00", "Enter a time as HH:MM.")]),
            dialog: Dialog(canConfirm: false)))));
        Assert.Same(window, vm.Days[2].Ranges[0]);
        Assert.True(window.HasError);
        Assert.Equal("Enter a time as HH:MM.", window.Error);
        Assert.Equal("9", window.Start);

        // A window added builds the day's windows again.
        model.Show(Page(Hours(week: Week(days: Days(monday: [new HoursRangeView(2, "09:00", "17:00", null)])))));
        Assert.False(vm.Days[0].ShowsNone);
        Assert.True(vm.Days[0].OffersCopy);
        Assert.Equal("Monday, range 1", Assert.Single(vm.Days[0].Ranges).Name);

        // Saving: nothing can change; then what it came to, until put away.
        model.Show(Page(Hours(week: Week(dialog: Dialog(confirm: "Saving…", canConfirm: false, working: true), editable: false))));
        Assert.True(vm.Dialog.Working);
        Assert.Equal("Saving…", vm.Dialog.ConfirmText);
        Assert.False(vm.Editable);
        model.Show(Page(Hours(week: Week(notice: "Working hours saved"))));
        Assert.True(vm.HasNotice);
        Assert.Equal("Working hours saved", vm.Notice);
        vm.DismissNoticeCommand.Execute(null);
        Assert.Equal(new HoursAction.DismissNotice(), Action(sink.Sent[^1]));
        model.Show(Page(Hours(week: Week(dialog: Dialog(failure: new FailureView("That did not save. Try again.", null, true))))));
        Assert.True(vm.Dialog.HasFailure);
        vm.Dialog.CloseCommand.Execute(null);
        Assert.Equal(new HoursAction.CloseDialog(), Action(sink.Sent[^1]));

        model.Show(Page(Hours(week: Week(status: _failed, days: [], dialog: Dialog(canConfirm: false), editable: false))));
        Assert.True(vm.Failed);
        Assert.Equal("Offline.", vm.FailureMessage);
        Assert.Empty(vm.Days);

        // Try again reads the week again, once: the core's Loading follows.
        Assert.True(vm.CanRetry);
        vm.RetryCommand.Execute(null);
        Assert.Equal(new HoursAction.RetryRead(), Action(sink.Sent[^1]));
        Assert.False(vm.RetryCommand.CanExecute(null));
        model.Show(Page(Hours(week: Week(status: new LoadStatus.Loading(), days: [], dialog: Dialog(canConfirm: false), editable: false))));
        Assert.False(vm.CanRetry);
        Assert.True(vm.Loading);

        // A failure trying again cannot mend offers no Try again.
        model.Show(Page(Hours(week: Week(status: _unretryable, days: [], dialog: Dialog(canConfirm: false), editable: false))));
        Assert.True(vm.Failed);
        Assert.False(vm.CanRetry);
        model.Show(Page(Hours()));
        Assert.False(vm.Dialog.IsOpen);
        Assert.Empty(vm.Days);
    }

    [Fact]
    public void AnOverrideIsAddedWithTheFieldsItsKindNeeds()
    {
        var (model, sink) = Attached();
        model.Show(Page(Hours(overrides: Overrides(status: new LoadStatus.Loading(), rows: [], canChange: false))));
        var vm = model.Hours.OverridesEditor;
        Assert.True(vm.Dialog.IsOpen);
        Assert.True(vm.Loading);
        Assert.False(vm.ShowEmpty);
        Assert.False(vm.AddOverrideCommand.CanExecute(null));
        Assert.False(vm.Dialog.ConfirmCommand.CanExecute(null));

        model.Show(Page(Hours(overrides: Overrides())));
        Assert.True(vm.ShowsList);
        Assert.False(vm.Adding || vm.Asking || vm.HasNotice || vm.ShowEmpty);
        Assert.Equal("Days you are away, or working different hours from usual.", vm.Hint);
        Assert.Equal("Add override", vm.AddLabel);
        Assert.Equal(2, vm.Rows.Count);
        var row = vm.Rows[0];
        Assert.Equal("HoursOverridegroup:grp-contract-1", row.AutomationId);
        Assert.Equal("Sep 24, 2099: 12:00 to 16:00", row.ToString());
        Assert.Equal("Delete: Sep 24, 2099", row.DeleteName);
        vm.AddOverrideCommand.Execute(null);
        vm.AskDelete(row);
        Assert.Equal(
            new HoursAction[] { new HoursAction.AddOverride(), new HoursAction.AskDeleteOverride("group:grp-contract-1") },
            sink.Sent.Select(Action).ToArray());

        model.Show(Page(Hours(overrides: Overrides(form: AddForm()))));
        Assert.True(vm.Adding);
        Assert.False(vm.ShowsList);
        Assert.Equal("Add an override", vm.Words?.Title);
        Assert.Equal("First day", vm.DateLabel);
        Assert.True(vm.ShowsLastDay);
        Assert.False(vm.ShowsHours);
        Assert.Equal(0, vm.KindIndex);
        Assert.Equal(2, vm.Kinds.Count);
        Assert.Equal("Opens: Hours that day", vm.StartName);
        Assert.Equal("Closes: Hours that day", vm.EndName);
        Assert.Equal(new OverrideFormView("day_off", string.Empty, string.Empty, "09:00", "17:00"), vm.Form());
        var before = sink.Sent.Count;
        vm.PickKind(0);
        vm.PickKind(5);
        Assert.Equal(before, sink.Sent.Count);
        vm.Date = "2099-12-24";
        vm.EndDate = "2099-12-26";
        vm.PickKind(1);
        vm.Start = "10:00";
        vm.End = "12:00";
        var sent = sink.Sent.Skip(before).Select(Action).ToArray();
        Assert.Equal(5, sent.Length);
        Assert.Equal(
            new OverrideFormView("custom_hours", "2099-12-24", "2099-12-26", "10:00", "12:00"),
            Assert.IsType<HoursAction.EditOverride>(sent[^1]).Form);
        Assert.Equal("day_off", Assert.IsType<HoursAction.EditOverride>(sent[1]).Form.Reason);

        // The core's next snapshot does not write over what is typed.
        model.Show(Page(Hours(overrides: Overrides(form: AddForm(reason: "custom_hours", validation: "Pick a date.")))));
        Assert.Equal("2099-12-24", vm.Date);
        Assert.Equal("Date", vm.DateLabel);
        Assert.True(vm.ShowsHours);
        Assert.False(vm.ShowsLastDay);
        Assert.True(vm.HasValidation);
        Assert.Equal("Pick a date.", vm.Validation);
        vm.SaveOverrideCommand.Execute(null);
        Assert.False(vm.CanSave);
        Assert.False(vm.SaveOverrideCommand.CanExecute(null));
        vm.CancelOverrideCommand.Execute(null);
        Assert.Equal(
            new HoursAction[] { new HoursAction.SaveOverride(), new HoursAction.CancelOverride() },
            sink.Sent.TakeLast(2).Select(Action).ToArray());

        // The form closed: what it held sends nothing, and it opens afresh.
        model.Show(Page(Hours(overrides: Overrides(notice: "Override added"))));
        Assert.True(vm.ShowsList);
        Assert.True(vm.HasNotice);
        Assert.Equal("Override added", vm.Notice);
        before = sink.Sent.Count;
        vm.Date = "closed";
        Assert.Equal(before, sink.Sent.Count);
        model.Show(Page(Hours(overrides: Overrides(form: AddForm()))));
        Assert.Equal(string.Empty, vm.Date);
        Assert.Equal(0, vm.KindIndex);
        vm.DismissNoticeCommand.Execute(null);
        Assert.Equal(new HoursAction.DismissNotice(), Action(sink.Sent[^1]));
    }

    [Fact]
    public void ADeleteIsAskedInPlaceAndPressedOnce()
    {
        var (model, sink) = Attached();
        model.Show(Page(Hours(overrides: Overrides(asking: true))));
        var vm = model.Hours.OverridesEditor;
        Assert.True(vm.Asking);
        Assert.Equal("Delete this override?", vm.AskTitle);
        Assert.Equal("All 3 days go back to your weekly hours.", vm.AskBody);
        Assert.Equal("Delete override", vm.AskConfirm);
        Assert.Equal("Cancel", vm.AskCancel);
        vm.CancelDeleteCommand.Execute(null);
        vm.ConfirmDeleteCommand.Execute(null);
        vm.ConfirmDeleteCommand.Execute(null);
        Assert.Equal(
            new HoursAction[] { new HoursAction.CancelDeleteOverride(), new HoursAction.ConfirmDeleteOverride() },
            sink.Sent.Select(Action).ToArray());

        // While a write is on its way, no other delete is asked.
        model.Show(Page(Hours(overrides: Overrides(canChange: false))));
        vm.AskDelete(vm.Rows[1]);
        Assert.Equal(2, sink.Sent.Count);
        model.Show(Page(Hours(overrides: Overrides(failure: new FailureView("That did not save. Try again.", null, true)))));
        Assert.True(vm.Dialog.HasFailure);
        model.Show(Page(Hours(overrides: Overrides(status: _failed, rows: [], canChange: false))));
        Assert.True(vm.Failed);
        Assert.Equal("Offline.", vm.FailureMessage);
        Assert.False(vm.ShowEmpty);

        // Try again reads the overrides again, once.
        Assert.True(vm.CanRetry);
        vm.RetryCommand.Execute(null);
        Assert.Equal(new HoursAction.RetryRead(), Action(sink.Sent[^1]));
        Assert.False(vm.RetryCommand.CanExecute(null));
        model.Show(Page(Hours(overrides: Overrides(status: _unretryable, rows: [], canChange: false))));
        Assert.False(vm.CanRetry);
        model.Show(Page(Hours(overrides: Overrides(rows: []))));
        Assert.True(vm.ShowEmpty);
        Assert.Equal("No overrides ahead. Add one for a day off or different hours.", vm.EmptyText);

        // Another section closes the editor.
        model.Show(new SchedulingAdminView("Overview", _sections, new SchedulingAdminPage.Overview(
            new SchedulingOverviewView("Times in UTC", "Your booking desk", [], "Coming up", new LoadStatus.Loading(), [], "Nothing coming up."))));
        Assert.False(vm.Dialog.IsOpen);
        Assert.False(model.ShowsHours);
    }

    [Fact]
    public void NothingIsSentBeforeAttaching()
    {
        var model = new SchedulingAdminViewModel();
        model.Show(Page(Hours(week: Week())));
        model.Hours.OpenWeekCommand.Execute(null);
        model.Hours.WeekEditor.AddRange(model.Hours.WeekEditor.Days[0]);
        model.Show(Page(Hours(overrides: Overrides(form: AddForm()))));
        model.Hours.OverridesEditor.Date = "2099-01-01";
        var (context, sink) = Pages.Context();
        model.Attach(context);
        Assert.Empty(sink.Sent);
    }
}
