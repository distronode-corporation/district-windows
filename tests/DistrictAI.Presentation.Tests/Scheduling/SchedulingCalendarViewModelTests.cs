using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels.Scheduling;
using Xunit;

namespace DistrictAI.Presentation.Tests.Scheduling;

public sealed class SchedulingCalendarViewModelTests
{
    private static readonly SchedulingSectionEntryView[] _sections =
    [
        new(SchedulingAdminSection.Overview, "Overview", "Your booking desk", false),
        new(SchedulingAdminSection.EventTypes, "Event types", "The pages a customer can book", false),
        new(SchedulingAdminSection.Hours, "Working hours", "The weekly hours you are bookable", false),
        new(SchedulingAdminSection.Bookings, "Bookings", "Every booking", false),
        new(SchedulingAdminSection.Calendar, "Calendar", "The accounts bookings are written to and checked against", true),
    ];

    private static readonly LoadStatus _failed =
        new LoadStatus.Failed(new FailureView("Offline.", null, true), "Could not load this section");

    private static readonly FailureView _refused = new("That did not save. Try again.", null, true);

    private const string Secret = "app-password-never-shown";

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

    private static CalendarPickerView Picker(
        LoadStatus? status = null,
        bool editable = true,
        DialogView? dialog = null,
        string? notice = null,
        bool holidaysChecked = false,
        bool holidaysReceives = false) =>
        new(
            Dialog: dialog ?? Dialog("Calendars in contract@example.com", "Save changes", canConfirm: editable),
            ConnectionId: "cal-1",
            Status: status ?? new LoadStatus.Ready(),
            LoadingLabel: "Loading calendars…",
            ConflictsLabel: "Checked for conflicts",
            ConflictsHint: "A booking is refused when it clashes with an event on a checked calendar.",
            DestinationLabel: "Receives bookings",
            DestinationHint: "New bookings are written to this calendar.",
            Calendars: status is null
                ? [new("contract@example.com", "Contract Member", true), new("holidays@example.com", "Statutory holidays", holidaysChecked)]
                : [],
            Destinations: status is null
                ? [new("contract@example.com", "Contract Member", !holidaysReceives), new("holidays@example.com", "Statutory holidays", holidaysReceives)]
                : [],
            Editable: editable,
            Notice: notice);

    private static CaldavView Caldav(
        int preset = 0,
        string serverUrl = "",
        string username = "",
        string? usernameError = null,
        bool editable = true,
        string? notice = null,
        DialogView? dialog = null) =>
        new(
            Dialog: dialog ?? Dialog("Connect a CalDAV calendar", "Connect calendar", canConfirm: editable),
            PresetLabel: "Where the calendar lives",
            Presets:
            [
                new("icloud", "iCloud", preset == 0),
                new("fastmail", "Fastmail", preset == 1),
                new("nextcloud", "Nextcloud", preset == 2),
                new("custom", "Other server URL", preset == 3),
            ],
            PresetNote: preset == 2 ? "Your own Nextcloud server, at its DAV address." : "Apple's calendar.",
            ShowsServerUrl: preset >= 2,
            ServerUrlLabel: "Server URL",
            ServerUrlHint: "The base address of the CalDAV server.",
            UsernameLabel: "Username",
            PasswordLabel: "App password",
            PasswordHint: "An app-specific password from the provider, not the account password.",
            ServerUrl: serverUrl,
            Username: username,
            ServerUrlError: preset >= 2 && serverUrl.Length == 0 ? "Enter the server URL." : null,
            UsernameError: usernameError,
            PasswordError: null,
            Editable: editable,
            Notice: notice);

    private static SchedulingCalendarView Calendar(
        LoadStatus? status = null,
        bool connecting = false,
        bool awaitingBrowser = false,
        FailureView? connectFailure = null,
        bool disconnecting = false,
        FailureView? disconnectFailure = null,
        string? disconnectNotice = null,
        bool asking = false,
        CalendarPickerView? picker = null,
        CaldavView? caldav = null) =>
        new(
            Status: status ?? new LoadStatus.Ready(),
            Connections: status is null
                ?
                [
                    new("cal-1", "Google Calendar", "contract@example.com", "Contract Member", "Contract Member"),
                    new("cal-2", "CalDAV", "caldav@example.com", "Not checked", "No calendar in this account"),
                ]
                : [],
            AccountLabel: "Account",
            ConflictsLabel: "Checked for conflicts",
            DestinationLabel: "Receives bookings",
            ChooseLabel: "Choose calendars",
            DisconnectLabel: "Disconnect",
            CanDisconnect: !disconnecting,
            ConnectHeading: "Connecting a calendar",
            Sentence: status is null ? "Another account can be connected to check it for conflicts as well." : string.Empty,
            Zoom: status is null ? new FactView("Zoom", "Not connected") : null,
            Providers: status is null ? [new("google", "Connect Google Calendar", false)] : [],
            ConnectNote: "Finish in the browser, then come back and refresh this page.",
            ShowsConnectNote: true,
            Connecting: connecting,
            AwaitingBrowser: awaitingBrowser,
            ConnectFailure: connectFailure,
            CaldavLabel: "Connect a CalDAV calendar",
            Disconnecting: disconnecting,
            DisconnectFailure: disconnectFailure,
            DisconnectNotice: disconnectNotice,
            Disconnect: asking
                ? Dialog(
                    "Disconnect contract@example.com?",
                    "Disconnect",
                    close: "Keep it connected",
                    destructive: true,
                    body: "Bookings stop syncing to it. Existing events stay.")
                : null,
            Picker: picker,
            Caldav: caldav);

    private static SchedulingAdminView Page(SchedulingCalendarView view) =>
        new("Calendar", _sections, new SchedulingAdminPage.Calendar(view));

    private static (SchedulingAdminViewModel Model, RecordingSink Sink) Attached()
    {
        var model = new SchedulingAdminViewModel();
        var (context, sink) = Pages.Context();
        model.Attach(context);
        return (model, sink);
    }

    /// <summary>The calendar action <paramref name="sent"/> carries.</summary>
    private static CalendarAction Action(UiEvent sent) =>
        Assert.IsType<SchedulingAdminAction.Calendar>(Assert.IsType<UiEvent.SchedulingAdmin>(sent).Action).Action;

    [Fact]
    public void TheAccountsShowAndConnectHandsOffToTheBrowser()
    {
        var (model, sink) = Attached();
        model.Show(Page(Calendar()));
        var vm = model.Calendar;
        Assert.True(model.ShowsCalendar);
        Assert.False(model.ShowsHours || model.ShowsBookings || model.ShowsOverview || model.ShowsEventTypes || model.ShowsEventType || model.ShowsBooking);
        Assert.Equal(4, model.SelectedSection);
        Assert.True(vm.Load.Ready);
        Assert.Equal(2, vm.Connections.Count);
        var google = vm.Connections[0];
        Assert.Equal("Google Calendar: contract@example.com", google.ToString());
        Assert.Equal("CalendarConnectioncal-1", google.AutomationId);
        Assert.Equal("Choose calendars: contract@example.com", google.ChooseName);
        Assert.Equal("Disconnect: contract@example.com", google.DisconnectName);
        Assert.Equal(("Account", "Checked for conflicts", "Receives bookings"), (google.AccountLabel, google.ConflictsLabel, google.DestinationLabel));
        Assert.Equal(("Contract Member", "Contract Member"), (google.Conflicts, google.Destination));
        Assert.True(google.CanDisconnect);
        Assert.Equal("Connecting a calendar", vm.ConnectHeading);
        Assert.Equal("Another account can be connected to check it for conflicts as well.", vm.Sentence);
        Assert.True(vm.HasZoom);
        Assert.Equal(("Zoom", "Not connected"), (vm.ZoomLabel, vm.ZoomValue));
        var provider = Assert.Single(vm.Providers);
        Assert.Equal("CalendarConnectgoogle", provider.AutomationId);
        Assert.Equal("Connect Google Calendar", provider.ToString());
        Assert.True(provider.Enabled);
        Assert.True(vm.ShowsConnectNote);
        Assert.Equal("Connect a CalDAV calendar", vm.CaldavLabel);
        Assert.False(vm.Connecting || vm.AwaitingBrowser || vm.HasConnectFailure);
        Assert.False(vm.Disconnecting || vm.HasDisconnectFailure || vm.HasDisconnectNotice);
        Assert.False(vm.Disconnect.IsOpen || vm.Picker.Dialog.IsOpen || vm.Caldav.Dialog.IsOpen);

        // One press, one sign-in.
        vm.Connect(provider);
        vm.Connect(provider);
        vm.OpenCalendars(google);
        vm.AskDisconnect(google);
        vm.OpenCaldavCommand.Execute(null);
        vm.DismissConnectFailureCommand.Execute(null);
        vm.DismissDisconnectCommand.Execute(null);
        Assert.Equal(
            new CalendarAction[]
            {
                new CalendarAction.Connect("google"),
                new CalendarAction.OpenCalendars("cal-1"),
                new CalendarAction.AskDisconnect("cal-1"),
                new CalendarAction.OpenCaldav(),
                new CalendarAction.DismissConnectFailure(),
                new CalendarAction.DismissDisconnect(),
            },
            sink.Sent.Select(Action).ToArray());

        // The sign-in on its way: the buttons wait. Then the page is open in
        // the browser, and Refresh reads what is connected.
        model.Show(Page(Calendar(connecting: true)));
        Assert.True(vm.Connecting);
        Assert.False(vm.Providers[0].Enabled);
        model.Show(Page(Calendar(awaitingBrowser: true)));
        Assert.True(vm.AwaitingBrowser);
        Assert.True(vm.Providers[0].Enabled);
        vm.RefreshCommand.Execute(null);
        Assert.IsType<UiEvent.Refresh>(sink.Sent[^1]);
        model.Show(Page(Calendar(connectFailure: new FailureView("Scheduling is still being set up for this workspace. Try again shortly.", null, false))));
        Assert.True(vm.HasConnectFailure);
        Assert.Equal("Scheduling is still being set up for this workspace. Try again shortly.", vm.ConnectFailure);

        // A failed read is a status page, with no card.
        model.Show(Page(Calendar(status: _failed)));
        Assert.True(vm.Load.Failed);
        Assert.True(vm.Load.CanRetry);
        Assert.Empty(vm.Connections);
        Assert.Empty(vm.Providers);
        Assert.False(vm.HasZoom);
    }

    [Fact]
    public void ADisconnectIsAskedOnceMoreAndSaysWhatItCameTo()
    {
        var (model, sink) = Attached();
        model.Show(Page(Calendar(asking: true)));
        var vm = model.Calendar;
        Assert.True(vm.Disconnect.IsOpen);
        Assert.Equal("Disconnect contract@example.com?", vm.Disconnect.Title);
        Assert.Equal("Bookings stop syncing to it. Existing events stay.", vm.Disconnect.Body);
        Assert.Equal(("Disconnect", "Keep it connected"), (vm.Disconnect.ConfirmText, vm.Disconnect.CloseText));
        Assert.True(vm.Disconnect.Destructive);
        vm.Disconnect.CloseCommand.Execute(null);
        vm.Disconnect.ConfirmCommand.Execute(null);
        Assert.False(vm.Disconnect.CanConfirm);
        Assert.Equal(
            new CalendarAction[] { new CalendarAction.CancelDisconnect(), new CalendarAction.ConfirmDisconnect() },
            sink.Sent.Select(Action).ToArray());

        // While one is on its way, no other is asked.
        model.Show(Page(Calendar(disconnecting: true)));
        Assert.False(vm.Disconnect.IsOpen);
        Assert.True(vm.Disconnecting);
        Assert.False(vm.Connections[0].CanDisconnect);
        vm.AskDisconnect(vm.Connections[0]);
        Assert.Equal(2, sink.Sent.Count);

        model.Show(Page(Calendar(disconnectNotice: "No calendar receives new bookings now. Connect one, or choose a calendar in another account.")));
        Assert.True(vm.HasDisconnectNotice);
        Assert.StartsWith("No calendar receives new bookings now.", vm.DisconnectNotice);
        model.Show(Page(Calendar(disconnectFailure: _refused)));
        Assert.True(vm.HasDisconnectFailure);
        Assert.Equal("That did not save. Try again.", vm.DisconnectFailure);
        Assert.False(vm.HasDisconnectNotice);
    }

    [Fact]
    public void AnAccountsCalendarsAreChosenAndSavedOnce()
    {
        var (model, sink) = Attached();
        model.Show(Page(Calendar(picker: Picker(status: new LoadStatus.Loading(), editable: false))));
        var vm = model.Calendar.Picker;
        Assert.True(vm.Dialog.IsOpen);
        Assert.True(vm.Loading);
        Assert.Equal("Loading calendars…", vm.LoadingLabel);
        Assert.False(vm.Ready || vm.Failed);
        Assert.Empty(vm.Calendars);
        Assert.False(vm.Dialog.ConfirmCommand.CanExecute(null));

        // A read that failed is asked for again.
        model.Show(Page(Calendar(picker: Picker(status: _failed, editable: false))));
        Assert.True(vm.Failed);
        Assert.Equal("Offline.", vm.FailureMessage);
        Assert.True(vm.RetryCommand.CanExecute(null));
        vm.RetryCommand.Execute(null);
        Assert.Equal(new CalendarAction.OpenCalendars("cal-1"), Action(sink.Sent[^1]));

        model.Show(Page(Calendar(picker: Picker())));
        Assert.True(vm.Ready);
        Assert.False(vm.Loading || vm.Failed || vm.HasNotice);
        Assert.Equal("Calendars in contract@example.com", vm.Dialog.Title);
        Assert.Equal("Save changes", vm.Dialog.ConfirmText);
        Assert.Equal(("Checked for conflicts", "Receives bookings"), (vm.ConflictsLabel, vm.DestinationLabel));
        Assert.Equal("New bookings are written to this calendar.", vm.DestinationHint);
        Assert.StartsWith("A booking is refused", vm.ConflictsHint);
        Assert.Equal(2, vm.Calendars.Count);
        var holidays = vm.Calendars[1];
        Assert.Equal("Statutory holidays", holidays.ToString());
        Assert.Equal("Checked for conflicts: Statutory holidays", holidays.ConflictsName);
        Assert.False(holidays.ChecksConflicts);
        Assert.True(holidays.Enabled);
        Assert.Equal(2, vm.Destinations.Count);
        Assert.Equal(0, vm.DestinationIndex);

        // What a box already says sends nothing.
        var before = sink.Sent.Count;
        vm.SetCheckConflicts(vm.Calendars[0], true);
        vm.ChooseDestination(0);
        vm.ChooseDestination(5);
        Assert.Equal(before, sink.Sent.Count);
        vm.SetCheckConflicts(holidays, true);
        vm.ChooseDestination(1);
        Assert.Equal(1, vm.DestinationIndex);
        vm.Dialog.ConfirmCommand.Execute(null);
        Assert.Equal(
            new CalendarAction[]
            {
                new CalendarAction.SetCheckConflicts("holidays@example.com", true),
                new CalendarAction.ChooseDestination("holidays@example.com"),
                new CalendarAction.SaveCalendars(),
            },
            sink.Sent.Skip(before).Select(Action).ToArray());

        // Saving: nothing changes; then what it came to; a refusal keeps the choices.
        model.Show(Page(Calendar(picker: Picker(
            editable: false,
            dialog: Dialog("Calendars in contract@example.com", "Saving…", canConfirm: false, working: true),
            holidaysChecked: true,
            holidaysReceives: true))));
        Assert.True(vm.Dialog.Working);
        Assert.False(vm.Calendars[1].Enabled);
        before = sink.Sent.Count;
        vm.SetCheckConflicts(vm.Calendars[1], false);
        vm.ChooseDestination(0);
        Assert.Equal(before, sink.Sent.Count);
        model.Show(Page(Calendar(picker: Picker(notice: "Calendars saved", holidaysChecked: true, holidaysReceives: true))));
        Assert.True(vm.HasNotice);
        Assert.Equal("Calendars saved", vm.Notice);
        model.Show(Page(Calendar(picker: Picker(
            dialog: Dialog("Calendars in contract@example.com", "Save changes", failure: _refused),
            holidaysChecked: true))));
        Assert.True(vm.Dialog.HasFailure);
        Assert.True(vm.Calendars[1].ChecksConflicts);
        vm.Dialog.CloseCommand.Execute(null);
        Assert.Equal(new CalendarAction.CloseDialog(), Action(sink.Sent[^1]));

        model.Show(Page(Calendar()));
        Assert.False(vm.Dialog.IsOpen);
        Assert.Empty(vm.Calendars);
        Assert.Empty(vm.Destinations);
        Assert.Equal(-1, vm.DestinationIndex);
    }

    [Fact]
    public void TheCaldavFormSendsEachChangeAndForgetsThePassword()
    {
        var (model, sink) = Attached();
        model.Show(Page(Calendar(caldav: Caldav())));
        var vm = model.Calendar.Caldav;
        Assert.True(vm.Dialog.IsOpen);
        Assert.Equal("Connect a CalDAV calendar", vm.Dialog.Title);
        Assert.Equal("Connect calendar", vm.Dialog.ConfirmText);
        Assert.Equal("Where the calendar lives", vm.Words?.PresetLabel);
        Assert.Equal(4, vm.Presets.Count);
        Assert.Equal(0, vm.PresetIndex);
        Assert.Equal("Apple's calendar.", vm.PresetNote);
        Assert.False(vm.ShowsServerUrl);
        Assert.True(vm.Editable);
        Assert.False(vm.HasServerUrlError || vm.HasUsernameError || vm.HasPasswordError || vm.HasNotice);
        Assert.Equal(new CaldavFormView("icloud", string.Empty, string.Empty, string.Empty), vm.Form());
        Assert.Empty(sink.Sent);

        // Each change sends the whole form; the place chosen sends nothing.
        vm.PickPreset(0);
        vm.PickPreset(9);
        Assert.Empty(sink.Sent);
        vm.PickPreset(2);
        vm.ServerUrl = "https://dav.example.com";
        vm.Username = "ada";
        vm.Password = Secret;
        var sent = sink.Sent.Select(Action).ToArray();
        Assert.Equal(4, sent.Length);
        Assert.Equal(
            new CaldavFormView("nextcloud", "https://dav.example.com", "ada", Secret),
            Assert.IsType<CalendarAction.EditCaldav>(sent[^1]).Form);
        Assert.Equal("nextcloud", Assert.IsType<CalendarAction.EditCaldav>(sent[0]).Form.Preset);

        // The core's next snapshot does not write over what is typed.
        model.Show(Page(Calendar(caldav: Caldav(preset: 2, serverUrl: "https://dav.example.com", username: "ada", usernameError: "Enter the username."))));
        Assert.Equal("ada", vm.Username);
        Assert.Equal(Secret, vm.Password);
        Assert.True(vm.ShowsServerUrl);
        Assert.Equal("Your own Nextcloud server, at its DAV address.", vm.PresetNote);
        Assert.True(vm.HasUsernameError);
        Assert.Equal("Enter the username.", vm.UsernameError);
        vm.Dialog.ConfirmCommand.Execute(null);
        Assert.Equal(new CalendarAction.ConnectCaldav(), Action(sink.Sent[^1]));

        // Connecting: nothing can change; then connected, and the fields go,
        // the password with them, sending nothing.
        model.Show(Page(Calendar(caldav: Caldav(
            preset: 2,
            serverUrl: "https://dav.example.com",
            username: "ada",
            editable: false,
            dialog: Dialog("Connect a CalDAV calendar", "Connecting…", canConfirm: false, working: true)))));
        Assert.False(vm.Editable);
        var before = sink.Sent.Count;
        model.Show(Page(Calendar(caldav: Caldav(preset: 2, notice: "Calendar connected: caldav@example.com"))));
        Assert.Equal(before, sink.Sent.Count);
        Assert.True(vm.HasNotice);
        Assert.Equal("Calendar connected: caldav@example.com", vm.Notice);
        Assert.Equal((string.Empty, string.Empty, string.Empty), (vm.ServerUrl, vm.Username, vm.Password));
        Assert.Equal(2, vm.PresetIndex);

        // Another account typed after it is not written over while the notice stays.
        vm.Username = "grace";
        model.Show(Page(Calendar(caldav: Caldav(preset: 2, username: "grace", notice: "Calendar connected: caldav@example.com"))));
        Assert.Equal("grace", vm.Username);
        model.Show(Page(Calendar(caldav: Caldav(preset: 2, dialog: Dialog("Connect a CalDAV calendar", "Connect calendar", failure: _refused)))));
        Assert.True(vm.Dialog.HasFailure);

        // Closed: the password goes, and nothing typed then is sent.
        vm.Password = Secret;
        model.Show(Page(Calendar()));
        Assert.False(vm.Dialog.IsOpen);
        Assert.Equal(string.Empty, vm.Password);
        Assert.Null(vm.Words);
        before = sink.Sent.Count;
        vm.Username = "closed";
        Assert.Equal(before, sink.Sent.Count);
    }

    [Fact]
    public void AnotherSectionClosesTheDialogs()
    {
        var (model, _) = Attached();
        model.Show(Page(Calendar(asking: true, picker: Picker())));
        Assert.True(model.Calendar.Disconnect.IsOpen && model.Calendar.Picker.Dialog.IsOpen);
        model.Show(new SchedulingAdminView("Overview", _sections, new SchedulingAdminPage.Overview(
            new SchedulingOverviewView("Times in UTC", "Your booking desk", [], "Coming up", new LoadStatus.Loading(), [], "Nothing coming up."))));
        Assert.False(model.ShowsCalendar);
        Assert.False(model.Calendar.Disconnect.IsOpen || model.Calendar.Picker.Dialog.IsOpen || model.Calendar.Caldav.Dialog.IsOpen);
    }

    [Fact]
    public void NothingIsSentBeforeAttaching()
    {
        var model = new SchedulingAdminViewModel();
        model.Show(Page(Calendar(picker: Picker())));
        model.Calendar.Connect(model.Calendar.Providers[0]);
        model.Calendar.RefreshCommand.Execute(null);
        model.Calendar.Picker.ChooseDestination(1);
        model.Show(Page(Calendar(caldav: Caldav())));
        model.Calendar.Caldav.Username = "ada";
        var (context, sink) = Pages.Context();
        model.Attach(context);
        Assert.Empty(sink.Sent);
    }
}
