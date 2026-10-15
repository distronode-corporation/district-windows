using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using Xunit;
using static DistrictAI.UiTests.Walk;

namespace DistrictAI.UiTests;

/// <summary>
/// The scripted test package (district-ffi's <c>scripted</c> feature), started
/// in its signed-in scene: every entry the navigation pane offers is opened,
/// and each page must render, finish loading and show no failure. With
/// DISTRICTAI_SCREENSHOTS set to a folder, each page's client area is also
/// saved there at 1920x1080, for the Store listing, and the whole window
/// (title bar included) once, as 00-window.png.
/// </summary>
/// <remarks>
/// It runs only against a scripted package (DISTRICTAI_UI_SCRIPTED=1, which
/// scripts/run-ui-tests.ps1 -Scripted sets); the Store build ignores the scene
/// argument and would sit at the sign-in page.
/// </remarks>
public sealed class SceneWalkTests
{
    /// <summary>The scene argument (crates/district-ffi/src/scripted.rs, SCENE_ARG and Scene::SignedIn).</summary>
    public const string SceneArgument = "--district-scripted-scene=signed-in";

    /// <summary>The scripted workspace's name (scripted.rs, WORKSPACE_NAME), the overview's heading.</summary>
    public const string WorkspaceName = "Example Dental";

    private static readonly TimeSpan _startTimeout = TimeSpan.FromSeconds(90);

    /// <summary>
    /// The pages whose heading is not their pane entry's own name, by the
    /// entry's AutomationId (MainWindow.Nav.cs): the heading's text, or the
    /// heading's own AutomationId. Every other entry's page must show its
    /// entry's name as its level-one heading, so building an area adds nothing
    /// here. Nothing here is English: the walk reads each name from the app.
    /// </summary>
    private static readonly (string NavId, string? Heading, string? HeadingId)[] _headings =
    [
        ("NavOverview", WorkspaceName, null),
        ("NavInbox", null, "InboxHeading"),
    ];

    /// <summary>
    /// Pages a button on another page opens, not a pane entry: from the page
    /// of the entry <c>NavId</c>, the button whose AutomationId is
    /// <c>ButtonId</c>; the page it shows, by its heading's AutomationId, or
    /// null for a panel on the same page (whose heading stays the entry's
    /// name); and the text box that shows such a panel open (or null). Walked
    /// when the button is there (its area built).
    /// </summary>
    private static readonly (string NavId, string ButtonId, string? HeadingId, string? EditId)[] _subPages =
    [
        ("NavContacts", "BlockedCallersButton", "BlockedHeading", null),
        // The plan chooser opens on the billing page itself; nothing is bought.
        ("NavBilling", "ChoosePlanButton", null, "PromoCodeBox"),
    ];

    /// <summary>
    /// The settings sections built so far, by their hub row's AutomationId
    /// ("SettingsRow" and the core's SettingsSection): each is opened from
    /// the Workspace settings hub, and its page's level-one heading must be
    /// the row's title. A section's packet adds its row here.
    /// </summary>
    private static readonly string[] _settingsSections =
    [
        "SettingsRowPersona",
        "SettingsRowVoiceStudio",
        "SettingsRowMessaging",
        "SettingsRowMembers",
        "SettingsRowCallHandling",
        "SettingsRowRouting",
        "SettingsRowDirectory",
        "SettingsRowTools",
        "SettingsRowKnowledge",
    ];

    /// <summary>
    /// The booking scheduler's sections built so far, by their row's
    /// AutomationId on the Booking pages page ("SchedulingSection" and the
    /// core's section): each is opened from that page (the scene's booking
    /// pages are live), and its page's level-one heading must be the row's
    /// title. A section's pull request adds its row here.
    /// </summary>
    private static readonly string[] _schedulingSections =
    [
        "SchedulingSectionOverview",
        "SchedulingSectionEventTypes",
        "SchedulingSectionHours",
        "SchedulingSectionBookings",
        "SchedulingSectionCalendar",
        "SchedulingSectionTeam",
        "SchedulingSectionSettings",
        "SchedulingSectionDeveloper",
    ];

    /// <summary>
    /// The conversation the Inbox opens first: the scene answers the inbox with
    /// the core's district-conversations.json, whose first thread is this
    /// contact's (the thread page's heading), and its timeline and draft from
    /// fixtures too, so the reply box shows (scripted.rs,
    /// the_first_conversation_opens_with_its_reply_box).
    /// </summary>
    public const string ConversationTitle = "Contract Test Caller";

    /// <summary>The reply box's AutomationId (Views/Composer/ComposerBox.xaml).</summary>
    public const string ReplyBoxId = "ReplyBox";

    [Fact]
    public void EveryOfferedAreaOpensAndFillsIn()
    {
        RequireScriptedPackage();
        var shots = Environment.GetEnvironmentVariable("DISTRICTAI_SCREENSHOTS");
        using var app = InstalledApp.Launch(SceneArgument);
        var window = app.MainWindow(_startTimeout);
        var handle = window.Properties.NativeWindowHandle.Value;

        // Signed in by the scene, on the overview.
        _ = Heading(app, WorkspaceName, _startTimeout);
        var entries = Wait.For(
            () => NavItems(window) is { Length: >= 5 } found ? found : null,
            PageTimeout,
            "the navigation pane's entries",
            app.Describe);
        InstalledApp.Log($"the pane offers {entries.Length}: [{string.Join(", ", entries.Select(entry => $"{entry.Id} \"{entry.Name}\""))}]");

        if (shots is { Length: > 0 })
        {
            // The whole window once, title bar included, then the pages.
            Save(handle, Path.Combine(shots, "00-window.png"), clientOnly: false);
            SizeClient(handle, 1920, 1080);
        }

        var problems = new List<string>();
        var index = 0;
        foreach (var (navId, entry) in entries)
        {
            var known = _headings.FirstOrDefault(known => known.NavId == navId);
            string? OpenEntry()
            {
                var name = OpenById(app, navId);
                return known.NavId is null ? name : known.Heading;
            }
            if (!VisitPage(app, handle, entry, OpenEntry, known.HeadingId, shots, ++index, problems))
            {
                break;
            }
            // The reply box lives in a conversation, not behind a pane entry:
            // from the Inbox, the first conversation is opened and its box
            // waited for by id.
            if (navId == "NavInbox"
                && !VisitPage(app, handle, "Conversation", () => { OpenFirstConversation(app); return ConversationTitle; }, null, shots, ++index, problems, ReplyBoxId))
            {
                break;
            }
            // The built settings sections, each from the hub's row, and back to
            // the hub for the next.
            if (navId == "NavSettings")
            {
                foreach (var section in _settingsSections)
                {
                    // The row's title names the page (and its screenshot), as
                    // the page's heading must.
                    var title = app.TryFindById(section) is { } row ? NameOf(row) : section;
                    if (!VisitPage(app, handle, title, () => OpenSettingsRowById(app, section), null, shots, ++index, problems)
                        || !VisitPage(app, handle, entry, () => OpenById(app, navId), null, null, index, problems))
                    {
                        break;
                    }
                }
            }
            // The built scheduling sections, each from the booking pages'
            // row, and back to the booking pages for the next.
            if (navId == "NavScheduling")
            {
                foreach (var section in _schedulingSections)
                {
                    var title = app.TryFindById(section) is { } row ? NameOf(row) : section;
                    if (!VisitPage(app, handle, title, () => OpenSettingsRowById(app, section), null, shots, ++index, problems)
                        || !VisitPage(app, handle, entry, () => OpenById(app, navId), null, null, index, problems))
                    {
                        break;
                    }
                }
            }
            foreach (var sub in _subPages.Where(sub => sub.NavId == navId))
            {
                if (app.TryFindById(sub.ButtonId) is not { } button)
                {
                    InstalledApp.Log($"\"{entry}\" has no {sub.ButtonId}: not walked");
                    continue;
                }
                string? OpenSub()
                {
                    app.FindById(sub.ButtonId, PageTimeout).AsButton().Invoke();
                    return sub.HeadingId is null ? entry : null;
                }
                if (!VisitPage(app, handle, NameOf(button), OpenSub, sub.HeadingId, shots, ++index, problems, sub.EditId))
                {
                    break;
                }
            }
            if (!app.IsRunning)
            {
                break;
            }
        }
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine + Environment.NewLine, problems));
        Assert.True(app.IsRunning, "the app ended during the walk");
    }

    /// <summary>
    /// A call placed from the dialler shows its live transcript on the call
    /// strip: the scene accepts the call (its media never connects) and the
    /// call's socket answers with the recorded snapshot (scripted.rs,
    /// transcript_snapshot). The strip is saved as 90-live-transcript.png when
    /// screenshots are on; then the call is hung up.
    /// </summary>
    [Fact]
    public void APlacedCallShowsItsLiveTranscript()
    {
        RequireScriptedPackage();
        var shots = Environment.GetEnvironmentVariable("DISTRICTAI_SCREENSHOTS");
        using var app = InstalledApp.Launch(SceneArgument);
        var window = app.MainWindow(_startTimeout);
        var handle = window.Properties.NativeWindowHandle.Value;
        _ = Heading(app, WorkspaceName, _startTimeout);
        if (shots is { Length: > 0 })
        {
            SizeClient(handle, 1920, 1080);
        }

        OpenUntilById(app, "NavCalls");
        app.FindById("PlaceCallButton", PageTimeout).AsButton().Invoke();
        app.FindById("DialNumberBox", PageTimeout).AsTextBox().Text = "+12125550142";
        Wait.For(
            () => app.TryFindById("DialCallButton") is { IsEnabled: true } call ? call : null,
            PageTimeout,
            "Call to work",
            app.Describe).AsButton().Invoke();

        _ = app.FindById("TranscriptHeading", PageTimeout);
        _ = app.Find(ControlType.Text, "I'd like to book a cleaning on Thursday.", PageTimeout);
        InstalledApp.Log("the call strip shows the live transcript");
        if (shots is { Length: > 0 })
        {
            Save(handle, Path.Combine(shots, "90-live-transcript.png"), clientOnly: true);
        }
        app.FindById("HangUpButton", PageTimeout).AsButton().Invoke();
        // Ended: the call bar offers Dismiss in place of Hang up.
        _ = app.FindById("DismissCallButton", PageTimeout);
        Assert.True(app.IsRunning, "the app ended during the call");
    }

    /// <summary>
    /// The call shortcuts (MainWindow.xaml's accelerators) during a placed
    /// call: Ctrl+D turns the microphone the other way (the call bar's Mute
    /// toggled) and back again, and Ctrl+Shift+H ends the call.
    /// </summary>
    /// <remarks>
    /// The reply box's draft surviving a quit is not walked here: the scene
    /// answers no draft save and reads every draft back empty (scripted.rs),
    /// so it cannot keep one. The live walk's VM has the service for that.
    /// </remarks>
    [Fact]
    public void TheCallShortcutsMuteAndHangUp()
    {
        RequireScriptedPackage();
        using var app = InstalledApp.Launch(SceneArgument);
        var window = app.MainWindow(_startTimeout);
        var handle = window.Properties.NativeWindowHandle.Value;
        _ = Heading(app, WorkspaceName, _startTimeout);

        OpenUntilById(app, "NavCalls");
        app.FindById("PlaceCallButton", PageTimeout).AsButton().Invoke();
        app.FindById("DialNumberBox", PageTimeout).AsTextBox().Text = "+12125550142";
        Wait.For(
            () => app.TryFindById("DialCallButton") is { IsEnabled: true } call ? call : null,
            PageTimeout,
            "Call to work",
            app.Describe).AsButton().Invoke();
        _ = app.FindById("TranscriptHeading", PageTimeout);
        var mute = Wait.For(
            () => app.TryFindById("MuteButton") is { IsEnabled: true } found && found.Patterns.Toggle.IsSupported ? found : null,
            PageTimeout,
            "the call bar's Mute, enabled",
            app.Describe);
        // Whichever way the call starts (the scene's may start muted), Ctrl+D
        // turns it the other way, and Ctrl+D again back.
        var start = mute.Patterns.Toggle.Pattern.ToggleState.Value;
        var other = start == ToggleState.On ? ToggleState.Off : ToggleState.On;
        InstalledApp.Log($"the call starts with Mute {start}");

        // The shortcuts go to the window, as a person's keys do.
        Shortcut(handle, VirtualKeyShort.KEY_D, VirtualKeyShort.CONTROL);
        Assert.True(
            Wait.Until(() => MuteState(app) == other, PageTimeout),
            $"Ctrl+D did not turn Mute {other}.{Environment.NewLine}{app.Describe()}");
        InstalledApp.Log($"Ctrl+D: Mute {other}");
        Shortcut(handle, VirtualKeyShort.KEY_D, VirtualKeyShort.CONTROL);
        Assert.True(
            Wait.Until(() => MuteState(app) == start, PageTimeout),
            $"Ctrl+D again did not turn Mute back {start}.{Environment.NewLine}{app.Describe()}");
        InstalledApp.Log($"Ctrl+D: Mute {start}");

        Shortcut(handle, VirtualKeyShort.KEY_H, VirtualKeyShort.CONTROL, VirtualKeyShort.SHIFT);
        _ = app.FindById("DismissCallButton", PageTimeout);
        InstalledApp.Log("Ctrl+Shift+H: the call ended");
        Assert.True(app.IsRunning, "the app ended during the call");
    }

    private static ToggleState? MuteState(InstalledApp app) =>
        app.TryFindById("MuteButton") is { } mute && mute.Patterns.Toggle.IsSupported
            ? mute.Patterns.Toggle.Pattern.ToggleState.Value
            : null;

    /// <summary>Presses <paramref name="key"/> with <paramref name="modifiers"/> held, the window in front.</summary>
    private static void Shortcut(nint handle, VirtualKeyShort key, params VirtualKeyShort[] modifiers)
    {
        _ = Native.SetForegroundWindow(handle);
        Thread.Sleep(200);
        Keyboard.TypeSimultaneously([.. modifiers, key]);
        Thread.Sleep(200);
    }

    /// <summary>
    /// The rooms lobby lists the recorded meetings, and a finished one opens
    /// its record in place of the list: its minutes and action items, each
    /// with Report. Saved as 91-meeting-record.png when screenshots are on.
    /// No room is joined: the scene has no media.
    /// </summary>
    [Fact]
    public void AMeetingRecordOpensWithReportOnItsMinutes()
    {
        RequireScriptedPackage();
        var shots = Environment.GetEnvironmentVariable("DISTRICTAI_SCREENSHOTS");
        using var app = InstalledApp.Launch(SceneArgument);
        var window = app.MainWindow(_startTimeout);
        var handle = window.Properties.NativeWindowHandle.Value;
        _ = Heading(app, WorkspaceName, _startTimeout);
        if (shots is { Length: > 0 })
        {
            SizeClient(handle, 1920, 1080);
        }

        OpenUntilById(app, "NavRooms");
        var row = Wait.For(
            () => window.FindAllDescendants(cf => cf.ByControlType(ControlType.ListItem))
                .FirstOrDefault(item => item.Name.StartsWith("Weekly review", StringComparison.Ordinal)),
            PageTimeout,
            "the meeting \"Weekly review\"",
            app.Describe);
        Activate(row);
        _ = app.FindById("MinutesHeading", PageTimeout);
        _ = app.FindById("ActionItemsHeading", PageTimeout);
        _ = app.FindById("MinutesReportButton", PageTimeout);
        InstalledApp.Log("the meeting record shows its minutes and action items, with Report");
        if (shots is { Length: > 0 })
        {
            Save(handle, Path.Combine(shots, "91-meeting-record.png"), clientOnly: true);
        }
        app.FindById("CloseMeetingButton", PageTimeout).AsButton().Invoke();
        Assert.True(app.IsRunning, "the app ended in the meeting rooms");
    }

    /// <summary>
    /// The Inbox's "New message" opens the core's form: the scene's contacts
    /// are offered, choosing one shows its channels and the message field, and
    /// Cancel closes it. Nothing is sent: the scene answers no send.
    /// </summary>
    [Fact]
    public void NewMessageOffersContactsAndClosesOnCancel()
    {
        RequireScriptedPackage();
        using var app = InstalledApp.Launch(SceneArgument);
        _ = app.MainWindow(_startTimeout);
        _ = Heading(app, WorkspaceName, _startTimeout);

        // The Inbox's entry is named with its unread count, so the page is
        // waited for by its heading's id, and the entry chosen again when the
        // choice was lost.
        OpenUntilFoundById(app, "NavInbox", "InboxHeading");
        app.FindById("NewMessageButton", PageTimeout).AsButton().Invoke();
        _ = app.FindById("ComposeTo", PageTimeout);
        var suggestions = app.FindById("ComposeSuggestions", PageTimeout);
        var first = Wait.For(
            () => suggestions.FindFirstDescendant(cf => cf.ByControlType(ControlType.ListItem)),
            PageTimeout,
            "a contact offered in the new message form",
            app.Describe);
        Activate(first);
        _ = app.FindById("ComposeRecipient", PageTimeout);
        _ = app.FindById("ComposeChannel", PageTimeout);
        _ = app.FindById("ComposeBody", PageTimeout);
        InstalledApp.Log("the new message form offers the contacts and the channels of the one chosen");

        Keyboard.Type(VirtualKeyShort.ESCAPE);
        Assert.True(
            Wait.Until(() => app.TryFindById("ComposeBody") is null, PageTimeout),
            "the new message form stayed open after Escape");
        Assert.True(app.IsRunning, "the app ended in the new message form");
    }

    /// <summary>
    /// A held number's "Release this number" opens the core's confirmation,
    /// and Escape closes it without releasing anything: the scene answers no
    /// release, and the dialog's own Release is never pressed.
    /// </summary>
    [Fact]
    public void ReleasingANumberAsksFirst()
    {
        RequireScriptedPackage();
        using var app = InstalledApp.Launch(SceneArgument);
        var window = app.MainWindow(_startTimeout);
        _ = Heading(app, WorkspaceName, _startTimeout);

        _ = OpenById(app, "NavMarketplace");
        var owned = app.FindById("OwnedNumbers", PageTimeout);
        var release = Wait.For(
            () => owned.FindFirstDescendant(cf => cf.ByAutomationId("NumberRelease")),
            PageTimeout,
            "a held number's Release button",
            app.Describe);
        release.AsButton().Invoke();
        _ = app.FindById("NumberConfirmationDialog", PageTimeout);
        InstalledApp.Log("Release asks first");

        Keyboard.Type(VirtualKeyShort.ESCAPE);
        Assert.True(
            Wait.Until(() => app.TryFindById("NumberConfirmationDialog") is null, PageTimeout),
            "the release confirmation stayed open after Escape");
        Assert.NotNull(window);
        Assert.True(app.IsRunning, "the app ended on the phone numbers");
    }

    /// <summary>
    /// The bookings' main flow: the list opens from the Booking pages page, a
    /// booking still to come offers "Cancel booking", which asks first, and
    /// Escape closes the question without cancelling anything (the dialog's
    /// own confirm is never pressed); then the booking opens, with what its
    /// booker answered. The scene moves its bookings to the days after it
    /// runs (scripted.rs, scheduling_admin), so they can be acted on.
    /// </summary>
    [Fact]
    public void ABookingOpensAndCancellingItAsksFirst()
    {
        RequireScriptedPackage();
        using var app = InstalledApp.Launch(SceneArgument);
        _ = app.MainWindow(_startTimeout);
        _ = Heading(app, WorkspaceName, _startTimeout);

        OpenUntilById(app, "NavScheduling");
        var bookings = OpenSettingsRowById(app, "SchedulingSectionBookings");
        _ = Heading(app, bookings, PageTimeout, "SchedulingAdminHeading");
        var list = app.FindById("SchedulingBookingsList", PageTimeout);
        app.FindById("BookingCancelButton", PageTimeout).AsButton().Invoke();
        _ = app.FindById("CancelBookingDialog", PageTimeout);
        _ = app.FindById("CancelBookingReason", PageTimeout);
        InstalledApp.Log("Cancel booking asks first");
        Keyboard.Type(VirtualKeyShort.ESCAPE);
        Assert.True(
            Wait.Until(() => app.TryFindById("CancelBookingDialog") is null, PageTimeout),
            "the cancel question stayed open after Escape");

        var first = Wait.For(
            () => list.FindFirstDescendant(cf => cf.ByControlType(ControlType.ListItem)),
            PageTimeout,
            "a booking in the list",
            app.Describe);
        Activate(first);
        // The booking's own page (an ItemsRepeater, which holds the answers,
        // is not in the UI Automation tree; its rows are).
        _ = Heading(app, null, PageTimeout, "SchedulingAdminHeading");
        _ = app.FindById("SchedulingBookingWhen", PageTimeout);
        _ = app.FindById("SchedulingBookingCancelButton", PageTimeout);
        InstalledApp.Log("the booking shows its answers and offers the cancel");
        Assert.Null(Failure(app));
        Assert.True(app.IsRunning, "the app ended in the bookings");
    }

    /// <summary>
    /// The event types' main flow: the list opens from the Booking pages
    /// page, an event type opens with its details, hosts and questions, and
    /// its editor opens on the stored form and saves it: the scene answers the
    /// save, so the core closes the dialog. The state actions open too, and
    /// Escape closes them without changing anything.
    /// </summary>
    [Fact]
    public void AnEventTypeOpensAndItsEditorSaves()
    {
        RequireScriptedPackage();
        using var app = InstalledApp.Launch(SceneArgument);
        _ = app.MainWindow(_startTimeout);
        _ = Heading(app, WorkspaceName, _startTimeout);

        OpenUntilById(app, "NavScheduling");
        var eventTypes = OpenSettingsRowById(app, "SchedulingSectionEventTypes");
        _ = Heading(app, eventTypes, PageTimeout, "SchedulingAdminHeading");
        _ = app.FindById("EventTypeCreateButton", PageTimeout);
        var list = app.FindById("SchedulingEventTypesList", PageTimeout);
        var first = Wait.For(
            () => list.FindFirstDescendant(cf => cf.ByControlType(ControlType.ListItem)),
            PageTimeout,
            "an event type in the list",
            app.Describe);
        Activate(first);
        _ = Heading(app, null, PageTimeout, "SchedulingAdminHeading");
        _ = app.FindById("SchedulingEventTypeName", PageTimeout);
        InstalledApp.Log("the event type shows its details");

        app.FindById("EventTypeEditButton", PageTimeout).AsButton().Invoke();
        var editor = app.FindById("EventTypeEditorDialog", PageTimeout);
        _ = app.FindById("EventTypeEditorName", PageTimeout);
        var save = Wait.For(
            () => editor.FindFirstDescendant(cf => cf.ByAutomationId("PrimaryButton")),
            PageTimeout,
            "the editor's Save",
            app.Describe);
        Press(save);
        Assert.True(
            Wait.Until(() => app.TryFindById("EventTypeEditorDialog") is null, PageTimeout),
            "the editor stayed open after its save");
        InstalledApp.Log("the editor saved and closed");

        app.FindById("EventTypeManageButton", PageTimeout).AsButton().Invoke();
        _ = app.FindById("EventTypeActionsDialog", PageTimeout);
        _ = app.FindById("EventTypeActionsDelete", PageTimeout);
        Keyboard.Type(VirtualKeyShort.ESCAPE);
        Assert.True(
            Wait.Until(() => app.TryFindById("EventTypeActionsDialog") is null, PageTimeout),
            "the state actions stayed open after Escape");
        Assert.Null(Failure(app));
        Assert.True(app.IsRunning, "the app ended in the event types");
    }

    /// <summary>
    /// The working hours' main flow: the section opens from the Booking pages
    /// page with the week, and the overrides read. In the week's editor each
    /// row's controls reach the core: Monday has no window in the scene, so
    /// its Add range gives it one; a time typed there that is not a time
    /// shows its fault (and the save stops working), and a sound one clears
    /// it; Remove takes the window off again; the weekday that has a window
    /// copies it to Monday with Copy to all weekdays; and Save lands (the
    /// scene answers each step, so the editor says so). Then the overrides'
    /// editor: a row's Delete asks first and the delete lands, and a day off
    /// is added on a typed date. Escape closes each editor.
    /// </summary>
    [Fact]
    public void TheWeekRowsReachTheCoreAndAnOverrideIsDeletedAndAdded()
    {
        RequireScriptedPackage();
        using var app = InstalledApp.Launch(SceneArgument);
        _ = app.MainWindow(_startTimeout);
        _ = Heading(app, WorkspaceName, _startTimeout);

        OpenUntilById(app, "NavScheduling");
        var hours = OpenSettingsRowById(app, "SchedulingSectionHours");
        _ = Heading(app, hours, PageTimeout, "SchedulingAdminHeading");
        _ = app.FindById("SchedulingHoursSummary", PageTimeout);
        Assert.Null(app.TryFindById("SchedulingHoursOverridesFailure"));
        InstalledApp.Log("the week shows");

        app.FindById("HoursWeekButton", PageTimeout).AsButton().Invoke();
        var week = app.FindById("HoursWeekDialog", PageTimeout);
        // Monday comes first: its Add range is the first, named for the day
        // ("Add range: Monday"), and its windows' boxes and buttons name it too.
        var addRanges = Wait.For(
            () => AllById(app, "HoursAddRange") is { Length: 7 } found ? found : null,
            PageTimeout,
            "the seven days' Add range",
            app.Describe);
        var monday = AfterColon(NameOf(addRanges[0]));
        AutomationElement[] Monday(string id) => [.. AllById(app, id).Where(found => NameOf(found).Contains(monday, StringComparison.Ordinal))];
        Assert.Empty(Monday("HoursRangeStart"));
        AutomationElement WeekSave() => Wait.For(
            () => week.FindFirstDescendant(cf => cf.ByAutomationId("PrimaryButton")),
            PageTimeout,
            "the week's Save",
            app.Describe);

        Press(addRanges[0]);
        Expect(app, () => Monday("HoursRangeStart").Length == 1, $"a window on {monday} after its Add range");
        InstalledApp.Log("Add range gave Monday a window");

        Monday("HoursRangeStart")[0].AsTextBox().Text = "nine";
        Expect(app, () => AllById(app, "HoursRangeError").Length == 1, "the fault of an opening time that is not a time");
        Expect(app, () => !WeekSave().IsEnabled, "the week's Save to stop working while a time is not a time");
        Monday("HoursRangeStart")[0].AsTextBox().Text = "08:30";
        Expect(app, () => AllById(app, "HoursRangeError").Length == 0, "the fault to go once the opening time is a time");
        Monday("HoursRangeEnd")[0].AsTextBox().Text = "8";
        Expect(app, () => AllById(app, "HoursRangeError").Length == 1, "the fault of a closing time that is not a time");
        Monday("HoursRangeEnd")[0].AsTextBox().Text = "17:00";
        Expect(app, () => AllById(app, "HoursRangeError").Length == 0, "the fault to go once the closing time is a time");
        InstalledApp.Log("the typed times reached the core");

        Press(Monday("HoursRangeRemove")[0]);
        Expect(app, () => Monday("HoursRangeStart").Length == 0, $"{monday}'s window to go after its Remove");
        InstalledApp.Log("Remove took Monday's window off");

        // The one weekday with a window offers to copy it.
        Press(Wait.For(
            () => AllById(app, "HoursCopyToWeekdays").FirstOrDefault(),
            PageTimeout,
            "a day's Copy to all weekdays",
            app.Describe));
        Expect(app, () => Monday("HoursRangeStart").Length > 0, $"a window copied to {monday}");
        InstalledApp.Log("Copy to all weekdays gave Monday the window");

        Expect(app, () => WeekSave().IsEnabled, "the week's Save to work");
        Press(WeekSave());
        _ = app.FindById("HoursWeekNotice", PageTimeout);
        InstalledApp.Log("the week saved");
        Keyboard.Type(VirtualKeyShort.ESCAPE);
        Assert.True(
            Wait.Until(() => app.TryFindById("HoursWeekDialog") is null, PageTimeout),
            "the week's editor stayed open after Escape");

        app.FindById("HoursOverridesButton", PageTimeout).AsButton().Invoke();
        _ = app.FindById("HoursOverridesDialog", PageTimeout);
        Press(Wait.For(
            () => AllById(app, "HoursOverrideDelete").FirstOrDefault(),
            PageTimeout,
            "an override's Delete",
            app.Describe));
        _ = app.FindById("HoursOverrideDeleteTitle", PageTimeout, onScreen: false);
        Press(app.FindById("HoursOverrideDeleteConfirm", PageTimeout, onScreen: false));
        _ = app.FindById("HoursOverridesNotice", PageTimeout);
        Assert.Null(app.TryFindById("HoursOverrideDeleteTitle", onScreen: false));
        InstalledApp.Log("the override's Delete asked first, and the delete landed");
        PutAway(app, "HoursOverridesNotice");

        Press(Wait.For(
            () => app.TryFindById("HoursOverrideAdd") is { IsEnabled: true } add ? add : null,
            PageTimeout,
            "Add override to work",
            app.Describe));
        app.FindById("HoursOverrideDate", PageTimeout).AsTextBox().Text = "2099-12-24";
        Press(app.FindById("HoursOverrideSave", PageTimeout));
        _ = app.FindById("HoursOverridesNotice", PageTimeout);
        InstalledApp.Log("the override was added");
        Keyboard.Type(VirtualKeyShort.ESCAPE);
        Assert.True(
            Wait.Until(() => app.TryFindById("HoursOverridesDialog") is null, PageTimeout),
            "the overrides' editor stayed open after Escape");
        Assert.Null(Failure(app));
        Assert.True(app.IsRunning, "the app ended in the working hours");
    }

    /// <summary>
    /// The calendars' main flow: the section opens from the Booking pages
    /// page with the accounts read; Connect Google hands off to the browser
    /// (the scene answers the sign-in and never opens it), so Refresh is
    /// offered and reads the section again; the first account's calendars
    /// open, a switch is changed and Save lands (the dialog says so); the
    /// CalDAV form connects an iCloud account, typed in, and says which; and
    /// Disconnect asks first, and Escape keeps the account. Escape closes
    /// each dialog.
    /// </summary>
    [Fact]
    public void TheCalendarsSaveAndACaldavAccountConnects()
    {
        RequireScriptedPackage();
        using var app = InstalledApp.Launch(SceneArgument);
        _ = app.MainWindow(_startTimeout);
        _ = Heading(app, WorkspaceName, _startTimeout);

        OpenUntilById(app, "NavScheduling");
        var calendar = OpenSettingsRowById(app, "SchedulingSectionCalendar");
        _ = Heading(app, calendar, PageTimeout, "SchedulingAdminHeading");
        _ = app.FindById("SchedulingCalendarSentence", PageTimeout, onScreen: false);
        InstalledApp.Log("the accounts show");

        // The connect card is below the two accounts: scrolled to, as a member does.
        ScrollTo(app, "SchedulingCalendar", 100);

        Press(app.FindById("CalendarConnectgoogle", PageTimeout, onScreen: false));
        Press(app.FindById("CalendarRefresh", PageTimeout, onScreen: false));
        Assert.True(
            Wait.Until(() => app.TryFindById("CalendarRefresh", onScreen: false) is null, PageTimeout),
            "Refresh stayed after the section was read again");
        InstalledApp.Log("the hand-off waited for the browser, and Refresh read again");

        ScrollTo(app, "SchedulingCalendar", 0);
        Press(app.FindById("CalendarChoose", PageTimeout));
        var picker = app.FindById("CalendarPickerDialog", PageTimeout);
        Press(Wait.For(
            () => app.TryFindById("CalendarConflict") is { IsEnabled: true } box ? box : null,
            PageTimeout,
            "a calendar's switch to work",
            app.Describe));
        var save = Wait.For(
            () => picker.FindFirstDescendant(cf => cf.ByAutomationId("PrimaryButton")) is { IsEnabled: true } enabled ? enabled : null,
            PageTimeout,
            "the calendars' Save to work",
            app.Describe);
        Press(save);
        _ = app.FindById("CalendarPickerNotice", PageTimeout);
        InstalledApp.Log("the calendars saved");
        Keyboard.Type(VirtualKeyShort.ESCAPE);
        Assert.True(
            Wait.Until(() => app.TryFindById("CalendarPickerDialog") is null, PageTimeout),
            "the calendars stayed open after Escape");

        ScrollTo(app, "SchedulingCalendar", 100);
        app.FindById("CalendarCaldavButton", PageTimeout, onScreen: false).AsButton().Invoke();
        var caldav = app.FindById("CaldavDialog", PageTimeout);
        app.FindById("CaldavUsername", PageTimeout).AsTextBox().Text = "ada@example.com";
        app.FindById("CaldavPassword", PageTimeout).Focus();
        Keyboard.Type("scripted-app-password");
        // The keys go through the input queue and Connect through UI
        // Automation, which can overtake them: a connect that arrived before
        // the password is refused for want of one, and the last key typed
        // then puts the refusal away. So the walk waits for the queue, and a
        // connect that found the box still empty is pressed once more.
        FlaUI.Core.Input.Wait.UntilInputIsProcessed(TimeSpan.FromSeconds(1));
        AutomationElement ConnectButton() => Wait.For(
            () => caldav.FindFirstDescendant(cf => cf.ByAutomationId("PrimaryButton")) is { IsEnabled: true } enabled ? enabled : null,
            PageTimeout,
            "Connect calendar to work",
            app.Describe);
        Press(ConnectButton());
        if (!Wait.Until(() => app.TryFindById("CaldavNotice") is not null, TimeSpan.FromSeconds(5)))
        {
            InstalledApp.Log("the connect overtook the password; pressed again");
            Press(ConnectButton());
        }
        _ = app.FindById("CaldavNotice", PageTimeout);
        Assert.Null(app.TryFindById("CaldavPasswordError"));
        InstalledApp.Log("the CalDAV account connected");
        Keyboard.Type(VirtualKeyShort.ESCAPE);
        Assert.True(
            Wait.Until(() => app.TryFindById("CaldavDialog") is null, PageTimeout),
            "the CalDAV form stayed open after Escape");

        ScrollTo(app, "SchedulingCalendar", 0);
        Press(app.FindById("CalendarDisconnect", PageTimeout));
        _ = app.FindById("CalendarDisconnectDialog", PageTimeout);
        Keyboard.Type(VirtualKeyShort.ESCAPE);
        Assert.True(
            Wait.Until(() => app.TryFindById("CalendarDisconnectDialog") is null, PageTimeout),
            "the disconnect stayed asked after Escape");
        Assert.Null(Failure(app));
        Assert.True(app.IsRunning, "the app ended in the calendars");
    }

    /// <summary>
    /// The team's main flow, each row's button pressed and its effect seen:
    /// the section opens from the Booking pages page with the members and the
    /// teams read, and names the scene's host, who is not a workspace member;
    /// that host's Archive account opens their archive, whose confirm asks
    /// once more in the dialog (its own confirm off meanwhile) before the
    /// archive lands. Below the members, Sales's Edit opens Sales's name and a
    /// rename lands; Create team saves a typed name; Sales's Hosts open its
    /// hosts, where a routing priority that is not a whole number comes back
    /// refused and a whole one is saved, and Remove asks first, in the
    /// dialog, before the removal lands. Escape closes each dialog.
    /// </summary>
    [Fact]
    public void TheTeamsRowsActAndRemovingAHostAsksFirst()
    {
        RequireScriptedPackage();
        using var app = InstalledApp.Launch(SceneArgument);
        _ = app.MainWindow(_startTimeout);
        _ = Heading(app, WorkspaceName, _startTimeout);

        OpenUntilById(app, "NavScheduling");
        var team = OpenSettingsRowById(app, "SchedulingSectionTeam");
        _ = Heading(app, team, PageTimeout, "SchedulingAdminHeading");
        _ = app.FindById("SchedulingTeamStranded", PageTimeout, onScreen: false);
        InstalledApp.Log("the members show, the host who left named at the top");

        // The host's row: its archive is asked once, in the dialog, then lands.
        ScrollTo(app, "SchedulingTeam", 0);
        Press(app.FindById("TeamArchive", PageTimeout, onScreen: false));
        var archive = app.FindById("TeamArchiveDialog", PageTimeout);
        AutomationElement ArchiveConfirm() => Wait.For(
            () => archive.FindFirstDescendant(cf => cf.ByAutomationId("PrimaryButton")),
            PageTimeout,
            "the archive's Archive account",
            app.Describe);
        Assert.True(
            Wait.Until(() => ArchiveConfirm().IsEnabled, PageTimeout),
            "Archive account stayed off with nothing in the way");
        Press(ArchiveConfirm());
        _ = app.FindById("TeamArchiveAskTitle", PageTimeout, onScreen: false);
        Assert.False(ArchiveConfirm().IsEnabled, "the archive could be asked twice");
        InstalledApp.Log("Archive account asks once more");
        Press(app.FindById("TeamArchiveConfirm", PageTimeout, onScreen: false));
        _ = app.FindById("TeamArchiveNotice", PageTimeout, onScreen: false);
        Assert.Null(app.TryFindById("TeamArchiveAskTitle", onScreen: false));
        Assert.Null(app.TryFindById("TeamArchiveFailure", onScreen: false));
        InstalledApp.Log("the account was archived");
        Keyboard.Type(VirtualKeyShort.ESCAPE);
        Assert.True(
            Wait.Until(() => app.TryFindById("TeamArchiveDialog", onScreen: false) is null, PageTimeout),
            "the archive stayed open after Escape");

        // Sales's Edit opens Sales's own name, and a rename lands.
        ScrollTo(app, "SchedulingTeam", 100);
        Press(app.FindById("TeamEdit", PageTimeout, onScreen: false));
        var editor = app.FindById("TeamEditorDialog", PageTimeout);
        Assert.Equal("Sales", editor.Name);
        app.FindById("TeamEditorName", PageTimeout).AsTextBox().Text = "Sales desk";
        Press(Wait.For(
            () => editor.FindFirstDescendant(cf => cf.ByAutomationId("PrimaryButton")) is { IsEnabled: true } enabled ? enabled : null,
            PageTimeout,
            "Save name to work",
            app.Describe));
        _ = app.FindById("TeamEditorNotice", PageTimeout, onScreen: false);
        Assert.Null(app.TryFindById("TeamEditorFailure", onScreen: false));
        InstalledApp.Log("the team was renamed");
        Keyboard.Type(VirtualKeyShort.ESCAPE);
        Assert.True(
            Wait.Until(() => app.TryFindById("TeamEditorDialog", onScreen: false) is null, PageTimeout),
            "the team's name stayed open after Escape");

        ScrollTo(app, "SchedulingTeam", 100);
        Press(app.FindById("TeamCreateButton", PageTimeout, onScreen: false));
        var create = app.FindById("TeamEditorDialog", PageTimeout);
        app.FindById("TeamEditorName", PageTimeout).AsTextBox().Text = "Front desk";
        Press(Wait.For(
            () => create.FindFirstDescendant(cf => cf.ByAutomationId("PrimaryButton")) is { IsEnabled: true } enabled ? enabled : null,
            PageTimeout,
            "Create team to work",
            app.Describe));
        _ = app.FindById("TeamEditorNotice", PageTimeout, onScreen: false);
        InstalledApp.Log("the team was created");
        Keyboard.Type(VirtualKeyShort.ESCAPE);
        Assert.True(
            Wait.Until(() => app.TryFindById("TeamEditorDialog", onScreen: false) is null, PageTimeout),
            "the new team's name stayed open after Escape");

        // Sales's Hosts: each host's box and buttons reach the core.
        ScrollTo(app, "SchedulingTeam", 100);
        Press(app.FindById("TeamHosts", PageTimeout, onScreen: false));
        var hosts = app.FindById("TeamHostsDialog", PageTimeout);
        Assert.Equal("Sales", hosts.Name);
        var priority = app.FindById("TeamHostPriority", PageTimeout, onScreen: false).AsTextBox();
        priority.Text = "first";
        Press(app.FindById("TeamHostSave", PageTimeout, onScreen: false));
        _ = app.FindById("TeamPriorityError", PageTimeout, onScreen: false);
        priority.Text = "2";
        Assert.True(
            Wait.Until(() => app.TryFindById("TeamPriorityError", onScreen: false) is null, PageTimeout),
            "the priority's refusal stayed after it was retyped");
        Press(Wait.For(
            () => app.TryFindById("TeamHostSave", onScreen: false) is { IsEnabled: true } save ? save : null,
            PageTimeout,
            "Save priority to work",
            app.Describe));
        _ = app.FindById("TeamHostsNotice", PageTimeout, onScreen: false);
        Assert.Null(app.TryFindById("TeamHostsFailure", onScreen: false));
        InstalledApp.Log("the routing priority was refused, then saved");
        Press(Wait.For(
            () => app.TryFindById("TeamHostRemove", onScreen: false) is { IsEnabled: true } remove ? remove : null,
            PageTimeout,
            "Remove to work",
            app.Describe));
        _ = app.FindById("TeamRemoveTitle", PageTimeout, onScreen: false);
        InstalledApp.Log("Remove asks first");
        Press(app.FindById("TeamRemoveConfirm", PageTimeout, onScreen: false));
        Assert.True(
            Wait.Until(() => app.TryFindById("TeamRemoveConfirm", onScreen: false) is null, PageTimeout),
            "the removal stayed asked after its confirm");
        _ = app.FindById("TeamHostsNotice", PageTimeout, onScreen: false);
        Assert.Null(app.TryFindById("TeamHostsFailure", onScreen: false));
        InstalledApp.Log("the host was removed");
        Keyboard.Type(VirtualKeyShort.ESCAPE);
        Assert.True(
            Wait.Until(() => app.TryFindById("TeamHostsDialog", onScreen: false) is null, PageTimeout),
            "the hosts stayed open after Escape");
        Assert.Null(Failure(app));
        Assert.True(app.IsRunning, "the app ended in the team");
    }

    /// <summary>
    /// An event type's rows, each pressed as a member does: in the state
    /// actions, a test email's link sends it (the scene answers, and the
    /// dialog names the address); in the hosts, the one member who is not a
    /// host is added, a priority typed for them that is not a whole number
    /// is refused at the save, a host's role is changed, the added host is
    /// removed, and the save lands; in the questions, the choice question's
    /// Edit opens its form on its options and one is removed, and a
    /// question's Delete asks first and the delete lands.
    /// </summary>
    /// <remarks>
    /// A host's role shows no change of its own here: the row's choice reads
    /// the same whether the core took it or not, and the scene keeps nothing,
    /// so the event type read after the save is the recorded one.
    /// </remarks>
    [Fact]
    public void AnEventTypesRowsDoWhatTheySay()
    {
        RequireScriptedPackage();
        using var app = InstalledApp.Launch(SceneArgument);
        _ = app.MainWindow(_startTimeout);
        _ = Heading(app, WorkspaceName, _startTimeout);

        OpenUntilById(app, "NavScheduling");
        var eventTypes = OpenSettingsRowById(app, "SchedulingSectionEventTypes");
        _ = Heading(app, eventTypes, PageTimeout, "SchedulingAdminHeading");
        var list = app.FindById("SchedulingEventTypesList", PageTimeout);
        Activate(Wait.For(
            () => list.FindFirstDescendant(cf => cf.ByControlType(ControlType.ListItem)),
            PageTimeout,
            "an event type in the list",
            app.Describe));
        _ = app.FindById("SchedulingEventTypeName", PageTimeout);

        app.FindById("EventTypeManageButton", PageTimeout).AsButton().Invoke();
        _ = app.FindById("EventTypeActionsDialog", PageTimeout);
        Press(
            Wait.For(
                () => AllById(app, "EventTypeTestEmail").FirstOrDefault(),
                PageTimeout,
                "a test email's link",
                app.Describe),
            allow: "the scene answers a test email; nothing leaves the computer");
        _ = app.FindById("EventTypeActionsNotice", PageTimeout);
        InstalledApp.Log("a test email's link sent it");
        Keyboard.Type(VirtualKeyShort.ESCAPE);
        Assert.True(
            Wait.Until(() => app.TryFindById("EventTypeActionsDialog") is null, PageTimeout),
            "the state actions stayed open after Escape");

        app.FindById("EventTypeHostsButton", PageTimeout).AsButton().Invoke();
        var hostsDialog = app.FindById("EventTypeHostsDialog", PageTimeout);
        var add = Wait.For(
            () => AllById(app, "EventTypeHostAdd").FirstOrDefault(),
            PageTimeout,
            "a member to add as a host",
            app.Describe);
        var hosts = AllById(app, "EventTypeHostRemove").Length;
        Press(add);
        Expect(
            app,
            () => AllById(app, "EventTypeHostRemove").Length == hosts + 1 && AllById(app, "EventTypeHostAdd").Length == 0,
            "the member added as a host");
        InstalledApp.Log("Add made the member a host");
        TypeUntilItHolds(app, () => AllById(app, "EventTypeHostPriority").LastOrDefault(), "first");
        AutomationElement HostsSave() => Wait.For(
            () => hostsDialog.FindFirstDescendant(cf => cf.ByAutomationId("PrimaryButton")) is { IsEnabled: true } enabled ? enabled : null,
            PageTimeout,
            "the hosts' Save to work",
            app.Describe);
        Press(HostsSave());
        _ = app.FindById("EventTypeHostsValidation", PageTimeout, onScreen: false);
        Assert.NotNull(app.TryFindById("EventTypeHostsDialog"));
        InstalledApp.Log("a priority that is not a whole number was refused");

        Choose(app, AllById(app, "EventTypeHostRole")[0], items => items.FirstOrDefault(item => !IsSelected(item)), "another role");
        Press(AllById(app, "EventTypeHostRemove")[^1]);
        Expect(app, () => AllById(app, "EventTypeHostRemove").Length == hosts, "the added host to go after their Remove");
        InstalledApp.Log("Remove took the host off");
        Press(HostsSave());
        Assert.True(
            Wait.Until(() => app.TryFindById("EventTypeHostsDialog") is null, PageTimeout),
            "the hosts stayed open after their save");

        app.FindById("EventTypeQuestionsButton", PageTimeout).AsButton().Invoke();
        _ = app.FindById("EventTypeQuestionsDialog", PageTimeout);
        // The first question is a choice: its form holds its options.
        Press(Wait.For(
            () => AllById(app, "EventTypeQuestionEdit").FirstOrDefault(),
            PageTimeout,
            "a question's Edit",
            app.Describe));
        _ = app.FindById("EventTypeQuestionFormTitle", PageTimeout, onScreen: false);
        var options = Wait.For(
            () => AllById(app, "EventTypeQuestionRemoveOption") is { Length: > 1 } found ? found : null,
            PageTimeout,
            "the question's options",
            app.Describe).Length;
        Press(AllById(app, "EventTypeQuestionRemoveOption")[0]);
        Expect(app, () => AllById(app, "EventTypeQuestionRemoveOption").Length == options - 1, "an option to go after its Remove");
        InstalledApp.Log("Edit opened the question, and an option was removed");
        Press(app.FindById("EventTypeQuestionCancel", PageTimeout, onScreen: false));
        Press(Wait.For(
            () => AllById(app, "EventTypeQuestionDelete").FirstOrDefault(),
            PageTimeout,
            "a question's Delete",
            app.Describe));
        _ = app.FindById("EventTypeQuestionDeleteTitle", PageTimeout, onScreen: false);
        Press(app.FindById("EventTypeQuestionDeleteConfirm", PageTimeout, onScreen: false));
        _ = app.FindById("EventTypeQuestionsNotice", PageTimeout);
        InstalledApp.Log("a question's Delete asked first, and the delete landed");
        Keyboard.Type(VirtualKeyShort.ESCAPE);
        Assert.True(
            Wait.Until(() => app.TryFindById("EventTypeQuestionsDialog") is null, PageTimeout),
            "the questions stayed open after Escape");
        Assert.Null(Failure(app));
        Assert.True(app.IsRunning, "the app ended in the event types");
    }

    /// <summary>
    /// Call handling's ways of answering: choosing the one not chosen makes
    /// the core hold a change, so Save works (it does not before), and the
    /// save lands with the choice (the scene answers a save as stored).
    /// </summary>
    [Fact]
    public void CallHandlingSavesTheWayOfAnsweringChosen()
    {
        RequireScriptedPackage();
        using var app = InstalledApp.Launch(SceneArgument);
        _ = app.MainWindow(_startTimeout);
        _ = Heading(app, WorkspaceName, _startTimeout);

        OpenUntilById(app, "NavSettings");
        var title = OpenSettingsRowById(app, "SettingsRowCallHandling");
        _ = Heading(app, title, PageTimeout);
        var other = Wait.For(
            () => AllById(app, "CallHandlingMode").FirstOrDefault(mode => mode.IsEnabled && !IsSelected(mode)),
            PageTimeout,
            "a way of answering not chosen",
            app.Describe);
        var name = NameOf(other);
        Assert.False(app.FindById("CallHandlingSave", PageTimeout, onScreen: false).IsEnabled, "Save worked before anything changed");

        Press(Reveal(app, "CallHandlingScroll", () => AllById(app, "CallHandlingMode").FirstOrDefault(mode => NameOf(mode) == name), $"the way \"{name}\""));
        Press(Wait.For(
            () => app.TryFindById("CallHandlingSave", onScreen: false) is { IsEnabled: true } save ? save : null,
            PageTimeout,
            $"Save to work once \"{name}\" was chosen",
            app.Describe));
        _ = app.FindById("CallHandlingNotice", PageTimeout, onScreen: false);
        Expect(app, () => AllById(app, "CallHandlingMode").Any(mode => NameOf(mode) == name && IsSelected(mode)), $"\"{name}\" chosen, as saved");
        InstalledApp.Log($"\"{name}\" was saved");
        Assert.Null(Failure(app));
        Assert.True(app.IsRunning, "the app ended in call handling");
    }

    /// <summary>
    /// The carrier accounts' rows: the first channel's sender is changed to
    /// the other account and the change lands (the scene answers it); the
    /// first account's Make default lands; its Edit opens the form on it;
    /// and its Remove asks first, and Cancel keeps it.
    /// </summary>
    [Fact]
    public void TheMessagingRowsDoWhatTheySay()
    {
        RequireScriptedPackage();
        using var app = InstalledApp.Launch(SceneArgument);
        _ = app.MainWindow(_startTimeout);
        _ = Heading(app, WorkspaceName, _startTimeout);

        OpenUntilById(app, "NavSettings");
        var title = OpenSettingsRowById(app, "SettingsRowMessaging");
        _ = Heading(app, title, PageTimeout);

        // The scene's first channel sends from the first account: the other is chosen.
        var picker = Reveal(app, "MessagingScroll", () => AllById(app, "MessagingChannelSender").FirstOrDefault(), "the first channel's sender");
        Choose(app, picker, items => items.FirstOrDefault(item => !IsSelected(item)), "the other account");
        ScrollTo(app, "MessagingScroll", 0);
        _ = app.FindById("MessagingSavedNotice", PageTimeout);
        InstalledApp.Log("the channel's sender changed");
        PutAway(app, "MessagingSavedNotice");

        Press(Wait.For(
            () => AllById(app, "MessagingMakeDefault").FirstOrDefault(),
            PageTimeout,
            "an account's Make default",
            app.Describe));
        ScrollTo(app, "MessagingScroll", 0);
        _ = app.FindById("MessagingSavedNotice", PageTimeout);
        Assert.Null(app.TryFindById("MessagingFailedNotice", onScreen: false));
        InstalledApp.Log("Make default landed");

        Press(AllById(app, "MessagingEditAccount")[0]);
        _ = app.FindById("MessagingFormTitle", PageTimeout, onScreen: false);
        InstalledApp.Log("Edit opened the account's form");
        Press(app.FindById("MessagingCloseForm", PageTimeout, onScreen: false));
        Expect(app, () => app.TryFindById("MessagingFormTitle", onScreen: false) is null, "the form to close");

        Press(AllById(app, "MessagingRemoveAccount")[0]);
        _ = app.FindById("MessagingConfirmTitle", PageTimeout, onScreen: false);
        InstalledApp.Log("Remove asked first");
        Press(app.FindById("MessagingKeepAccount", PageTimeout, onScreen: false));
        Expect(app, () => app.TryFindById("MessagingConfirmTitle", onScreen: false) is null, "the question to go after Cancel");
        Assert.Null(Failure(app));
        Assert.True(app.IsRunning, "the app ended in the carrier accounts");
    }

    /// <summary>
    /// The workflows' rows: the first workflow's Show runs opens its runs (the
    /// recorded page, which offers More runs), More runs adds the next page,
    /// the last; and the second workflow's switch, turned, waits for its
    /// change (the scene never answers it), so it does not work meanwhile.
    /// </summary>
    [Fact]
    public void AWorkflowsRunsOpenAndPageOnAndItsSwitchIsSent()
    {
        RequireScriptedPackage();
        using var app = InstalledApp.Launch(SceneArgument);
        _ = app.MainWindow(_startTimeout);
        _ = Heading(app, WorkspaceName, _startTimeout);

        OpenUntilById(app, "NavWorkflows");
        Press(Wait.For(
            () => AllById(app, "WorkflowRunsButton").FirstOrDefault(),
            PageTimeout,
            "a workflow's Show runs",
            app.Describe));
        var more = app.FindById("WorkflowMoreRuns", PageTimeout, onScreen: false);
        var firstPage = AllById(app, "WorkflowRunWhen").Length;
        Assert.True(firstPage > 0, "Show runs showed no run");
        InstalledApp.Log(FormattableString.Invariant($"Show runs opened {firstPage} runs"));
        Press(more);
        Expect(
            app,
            () => app.TryFindById("WorkflowMoreRuns", onScreen: false) is null && AllById(app, "WorkflowRunWhen").Length > firstPage,
            "the next page of runs, the last");
        InstalledApp.Log(FormattableString.Invariant($"More runs: {AllById(app, "WorkflowRunWhen").Length} runs"));

        var second = Wait.For(
            () => AllById(app, "WorkflowSwitch") is { Length: > 1 } found && found[1].IsEnabled ? found[1] : null,
            PageTimeout,
            "the second workflow's switch, working",
            app.Describe);
        var name = NameOf(second);
        second.Patterns.Toggle.Pattern.Toggle();
        Expect(
            app,
            () => AllById(app, "WorkflowSwitch").FirstOrDefault(found => NameOf(found) == name) is { IsEnabled: false },
            $"\"{name}\" waiting for its change");
        InstalledApp.Log($"\"{name}\" was turned and waits for the change");
        Assert.Null(Failure(app));
        Assert.True(app.IsRunning, "the app ended in the workflows");
    }

    /// <summary>
    /// Voice Studio's Voice part, behind Advanced: a slider left at the
    /// service's default has "use the default" ticked and does not move;
    /// unticking it gives the slider a value of its own, so it moves.
    /// </summary>
    [Fact]
    public void VoiceStudiosUseTheDefaultFreesItsSlider()
    {
        RequireScriptedPackage();
        using var app = InstalledApp.Launch(SceneArgument);
        _ = app.MainWindow(_startTimeout);
        _ = Heading(app, WorkspaceName, _startTimeout);

        OpenUntilById(app, "NavSettings");
        var title = OpenSettingsRowById(app, "SettingsRowVoiceStudio");
        _ = Heading(app, title, PageTimeout);
        // The chain's last part is the voice (scripted.rs,
        // voice_studio_offers_a_slider_left_at_its_default).
        Activate(Reveal(
            app,
            "VoiceStudioScroll",
            () => app.TryFindById("StudioChain", onScreen: false)?.FindAllDescendants(cf => cf.ByControlType(ControlType.ListItem)).LastOrDefault(),
            "the chain's last part"));
        var advanced = Wait.For(
            () => app.TryFindById("StudioAdvanced", onScreen: false) is { } found && found.Patterns.ExpandCollapse.IsSupported ? found : null,
            PageTimeout,
            "Advanced",
            app.Describe);
        advanced.Patterns.ExpandCollapse.Pattern.Expand();
        _ = Wait.For(
            () => AllById(app, "StudioUseDefault").FirstOrDefault(IsTicked),
            PageTimeout,
            "a slider's \"use the default\", ticked",
            app.Describe);
        var moving = AllById(app, "StudioSlider").Count(slider => slider.IsEnabled);

        Press(Reveal(app, "VoiceStudioScroll", () => AllById(app, "StudioUseDefault").FirstOrDefault(IsTicked), "a ticked \"use the default\""));
        Expect(app, () => AllById(app, "StudioSlider").Count(slider => slider.IsEnabled) == moving + 1, "the slider to move once \"use the default\" was unticked");
        InstalledApp.Log("unticking \"use the default\" gave the slider a value");
        Assert.Null(Failure(app));
        Assert.True(app.IsRunning, "the app ended in Voice Studio");
    }

    /// <summary>Every element whose AutomationId is <paramref name="automationId"/>, on the screen or scrolled off it, in order.</summary>
    /// <summary>
    /// Types <paramref name="text"/> into the box <paramref name="box"/> finds
    /// until a fresh look at it still reads it, then lets the window take it
    /// in. A row's box can be made again as a dialog settles (after Add, the
    /// hosts' rows are), which drops what was set into the one before; and a
    /// box's TextChanged reaches the core after UI Automation set its text,
    /// so a press right after could overtake it (the hosts once saved without
    /// the priority typed).
    /// </summary>
    private static void TypeUntilItHolds(InstalledApp app, Func<AutomationElement?> box, string text)
    {
        Expect(
            app,
            () =>
            {
                if (box()?.AsTextBox() is not { } current)
                {
                    return false;
                }
                if (current.Text != text)
                {
                    current.Text = text;
                    return false;
                }
                return true;
            },
            $"\"{text}\" to stay typed");
        FlaUI.Core.Input.Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));
    }

    private static AutomationElement[] AllById(InstalledApp app, string automationId) =>
        app.TryMainWindow()?.FindAllDescendants(cf => cf.ByAutomationId(automationId)) ?? [];

    /// <summary>Waits for <paramref name="condition"/>; at the deadline, fails naming <paramref name="what"/> and what UI Automation held.</summary>
    private static void Expect(InstalledApp app, Func<bool> condition, string what) =>
        _ = Wait.For(() => condition() ? what : null, PageTimeout, what, app.Describe);

    /// <summary>"Monday" of "Add range: Monday": what follows the last colon.</summary>
    private static string AfterColon(string name) =>
        name[(name.LastIndexOf(": ", StringComparison.Ordinal) + 2)..];

    /// <summary>Whether a radio button, or a choice box's item, is the one chosen.</summary>
    private static bool IsSelected(AutomationElement element) =>
        element.Patterns.SelectionItem.PatternOrDefault is { } item && item.IsSelected.ValueOrDefault;

    /// <summary>Whether a check box is ticked.</summary>
    private static bool IsTicked(AutomationElement element) =>
        element.Patterns.Toggle.PatternOrDefault is { } toggle && toggle.ToggleState.ValueOrDefault == ToggleState.On;

    /// <summary>
    /// What <paramref name="find"/> finds, brought onto the screen: the scroll
    /// viewer <paramref name="scrollId"/> is scrolled down a tenth at a time
    /// until it shows, as a member scrolls to it (a click needs it on screen).
    /// </summary>
    private static AutomationElement Reveal(InstalledApp app, string scrollId, Func<AutomationElement?> find, string what) =>
        Wait.For(
            () =>
            {
                for (var percent = 0; percent <= 100; percent += 10)
                {
                    ScrollTo(app, scrollId, percent);
                    Thread.Sleep(150);
                    if (find() is { IsOffscreen: false } shown)
                    {
                        return shown;
                    }
                }
                return null;
            },
            PageTimeout,
            $"{what} on the screen",
            app.Describe);

    /// <summary>
    /// Opens the choice box <paramref name="box"/>, chooses the item
    /// <paramref name="pick"/> picks of its items (<paramref name="what"/>),
    /// and closes it, as a member does.
    /// </summary>
    private static void Choose(InstalledApp app, AutomationElement box, Func<AutomationElement[], AutomationElement?> pick, string what)
    {
        box.Patterns.ExpandCollapse.Pattern.Expand();
        var item = Wait.For(
            () => pick(box.FindAllDescendants(cf => cf.ByControlType(ControlType.ListItem))),
            PageTimeout,
            what,
            app.Describe);
        item.Patterns.SelectionItem.Pattern.Select();
        if (box.Patterns.ExpandCollapse.Pattern.ExpandCollapseState.ValueOrDefault != ExpandCollapseState.Collapsed)
        {
            box.Patterns.ExpandCollapse.Pattern.Collapse();
        }
    }

    /// <summary>Closes the open notice <paramref name="noticeId"/> with its own close button, as a member does.</summary>
    private static void PutAway(InstalledApp app, string noticeId)
    {
        var notice = app.FindById(noticeId, PageTimeout);
        Press(Wait.For(
            () => notice.FindFirstDescendant(cf => cf.ByControlType(ControlType.Button)),
            PageTimeout,
            $"the close button of {noticeId}",
            app.Describe));
        Expect(app, () => app.TryFindById(noticeId) is null, $"{noticeId} to close");
    }

    /// <summary>
    /// The settings, tab by tab: the booking page opens with what it read,
    /// its Edit saves it, and the logo's Remove is asked once in place (its
    /// Cancel keeps the logo), then lands; the assistant's switch makes its
    /// Save work and the save lands; the profile's name saves; and a
    /// notification's switch saves. Escape closes each dialog. Choose image
    /// opens the Windows file chooser, which a walk cannot drive: the view
    /// model's tests hand it a file.
    /// </summary>
    [Fact]
    public void TheSettingsSaveTabByTab()
    {
        RequireScriptedPackage();
        using var app = InstalledApp.Launch(SceneArgument);
        _ = app.MainWindow(_startTimeout);
        _ = Heading(app, WorkspaceName, _startTimeout);

        OpenUntilById(app, "NavScheduling");
        ScrollRowIntoView(app, "SchedulingSectionSettings");
        var settings = OpenSettingsRowById(app, "SchedulingSectionSettings");
        _ = Heading(app, settings, PageTimeout, "SchedulingAdminHeading");

        AutomationElement Confirm(AutomationElement dialog, string what) => Wait.For(
            () => dialog.FindFirstDescendant(cf => cf.ByAutomationId("PrimaryButton")) is { IsEnabled: true } enabled ? enabled : null,
            PageTimeout,
            what,
            app.Describe);

        // The tab, by its id; then what it read, by the id that names the tab
        // ("SchedulingSettingsGroupsAssistant"): the same in every language.
        void ShowTab(string tab)
        {
            var tabId = "SchedulingSettingsTab" + tab;
            var groupsId = "SchedulingSettingsGroups" + tab;
            var tab = app.FindById(tabId, PageTimeout);
            if (tab.Patterns.SelectionItem.PatternOrDefault is { } item)
            {
                item.Select();
            }
            else
            {
                Press(tab);
            }
            Expect(
                app,
                () => app.TryFindById(groupsId, onScreen: false)?.FindFirstDescendant(cf => cf.ByControlType(ControlType.Text)) is not null
                    && app.TryFindById("SchedulingSettingsEdit", onScreen: false) is not null,
                $"the {tab} tab to show what it read");
        }

        void CloseWithEscape(string dialogId, string what)
        {
            Keyboard.Type(VirtualKeyShort.ESCAPE);
            Assert.True(
                Wait.Until(() => app.TryFindById(dialogId, onScreen: false) is null, PageTimeout),
                $"{what} stayed open after Escape");
        }

        // The booking page: read, edited and saved.
        ShowTab("BookingPage");
        Press(app.FindById("SchedulingSettingsEdit", PageTimeout, onScreen: false));
        var branding = app.FindById("SettingsBrandingDialog", PageTimeout);
        TypeUntilItHolds(app, () => app.TryFindById("SettingsBusinessName", onScreen: false), "Walk Agency");
        Press(Confirm(branding, "the booking page's Save to work"));
        _ = app.FindById("SettingsBrandingNotice", PageTimeout, onScreen: false);
        InstalledApp.Log("the booking page saved");

        // The logo's removal: asked in place, kept on Cancel, then confirmed.
        Press(app.FindById("SettingsImageLogoRemove", PageTimeout, onScreen: false));
        _ = app.FindById("SettingsImageLogoRemoveTitle", PageTimeout, onScreen: false);
        Press(app.FindById("SettingsImageLogoRemoveCancel", PageTimeout, onScreen: false));
        Assert.True(
            Wait.Until(() => app.TryFindById("SettingsImageLogoRemoveTitle", onScreen: false) is null, PageTimeout),
            "the logo's removal stayed asked after Cancel");
        Press(app.FindById("SettingsImageLogoRemove", PageTimeout, onScreen: false));
        Press(app.FindById("SettingsImageLogoRemoveConfirm", PageTimeout, onScreen: false));
        _ = app.FindById("SettingsImageLogoNotice", PageTimeout, onScreen: false);
        InstalledApp.Log("the logo's removal was asked once, then landed");
        CloseWithEscape("SettingsBrandingDialog", "the booking page");

        // The assistant: its Save works once the switch changed, and lands.
        ShowTab("Assistant");
        Press(app.FindById("SchedulingSettingsEdit", PageTimeout, onScreen: false));
        var assistant = app.FindById("SettingsAssistantDialog", PageTimeout);
        var save = assistant.FindFirstDescendant(cf => cf.ByAutomationId("PrimaryButton"));
        Assert.False(save?.IsEnabled ?? false, "the assistant's Save worked before anything changed");
        app.FindById("SettingsAssistantEnabled", PageTimeout, onScreen: false).Patterns.Toggle.Pattern.Toggle();
        Press(Confirm(assistant, "the assistant's Save to work once switched"));
        _ = app.FindById("SettingsAssistantNotice", PageTimeout, onScreen: false);
        InstalledApp.Log("the assistant saved");
        CloseWithEscape("SettingsAssistantDialog", "the assistant");

        // The profile: a name typed and saved.
        ShowTab("Profile");
        Press(app.FindById("SchedulingSettingsEdit", PageTimeout, onScreen: false));
        var profile = app.FindById("SettingsProfileDialog", PageTimeout);
        TypeUntilItHolds(app, () => app.TryFindById("SettingsProfileName", onScreen: false), "Walk Member");
        Press(Confirm(profile, "the profile's Save to work"));
        _ = app.FindById("SettingsProfileNotice", PageTimeout, onScreen: false);
        Assert.Null(app.TryFindById("SettingsProfileError", onScreen: false));
        InstalledApp.Log("the profile saved");
        CloseWithEscape("SettingsProfileDialog", "the profile");

        // The notifications: a switch flipped and saved.
        ShowTab("Notifications");
        Press(app.FindById("SchedulingSettingsEdit", PageTimeout, onScreen: false));
        var notifications = app.FindById("SettingsNotificationsDialog", PageTimeout);
        var before = IsTicked(app.FindById("SettingsNotifynotify_reschedule", PageTimeout, onScreen: false));
        app.FindById("SettingsNotifynotify_reschedule", PageTimeout, onScreen: false).Patterns.Toggle.Pattern.Toggle();
        Expect(
            app,
            () => app.TryFindById("SettingsNotifynotify_reschedule", onScreen: false) is { } now && IsTicked(now) != before,
            "the switch to hold its new state");
        Press(Confirm(notifications, "the notifications' Save to work"));
        _ = app.FindById("SettingsNotificationsNotice", PageTimeout, onScreen: false);
        InstalledApp.Log("the notifications saved");
        CloseWithEscape("SettingsNotificationsDialog", "the notifications");

        Assert.Null(Failure(app));
        Assert.True(app.IsRunning, "the app ended in the settings");
    }

    /// <summary>
    /// Developer's main flow, each row's button pressed and its effect seen:
    /// a key is made and shown once, and Copy puts it on the clipboard (the
    /// core then says Copied); Zapier's Revoke asks once, naming it, and the
    /// revoke lands; on the apps tab the MCP address shows and Raycast's
    /// Revoke asks, kept with Escape; on the webhooks tab the first webhook's
    /// Deliveries opens its deliveries under their heading and closes them
    /// again, its Edit opens its form, where an event is switched on and the
    /// save lands, and the second webhook's Delete asks, naming nothing but
    /// the webhook, and lands. Escape closes each dialog.
    /// </summary>
    [Fact]
    public void DevelopersRowsActAndARevokeAsksOnce()
    {
        RequireScriptedPackage();
        using var app = InstalledApp.Launch(SceneArgument);
        var window = app.MainWindow(_startTimeout);
        _ = Heading(app, WorkspaceName, _startTimeout);

        OpenUntilById(app, "NavScheduling");
        // Developer is the booking pages' last row, below the list's fold.
        ScrollRowIntoView(app, "SchedulingSectionDeveloper");
        var developer = OpenSettingsRowById(app, "SchedulingSectionDeveloper");
        _ = Heading(app, developer, PageTimeout, "SchedulingAdminHeading");
        _ = app.FindById("DeveloperKeyRevoke", PageTimeout, onScreen: false);
        InstalledApp.Log("the keys show");

        // A key, made and shown once; Copy reaches the core, which says Copied.
        Press(app.FindById("DeveloperCreateKey", PageTimeout, onScreen: false));
        var keyDialog = app.FindById("DeveloperKeyDialog", PageTimeout);
        app.FindById("DeveloperKeyName", PageTimeout).AsTextBox().Text = "Walk key";
        Press(Wait.For(
            () => keyDialog.FindFirstDescendant(cf => cf.ByAutomationId("PrimaryButton")) is { IsEnabled: true } enabled ? enabled : null,
            PageTimeout,
            "Create key to work",
            app.Describe));
        var shown = app.FindById("DeveloperSecretValue", PageTimeout, onScreen: false).AsTextBox();
        // The key itself is never logged: only that one shows.
        Assert.False(string.IsNullOrEmpty(shown.Text), "no key was shown");
        Press(app.FindById("DeveloperSecretCopy", PageTimeout, onScreen: false));
        Assert.True(
            Wait.Until(() => app.TryFindById("DeveloperSecretCopy", onScreen: false)?.Name == "Copied", PageTimeout),
            "Copy did not reach the core");
        Assert.Null(app.TryFindById("DeveloperSecretCopyFailed", onScreen: false));
        InstalledApp.Log("a key was made, shown once and copied");
        Keyboard.Type(VirtualKeyShort.ESCAPE);
        Assert.True(
            Wait.Until(() => app.TryFindById("DeveloperKeyDialog", onScreen: false) is null, PageTimeout),
            "the key stayed shown after Escape");

        // Zapier's Revoke: asked once, naming it, then it lands.
        Press(app.FindById("DeveloperKeyRevoke", PageTimeout, onScreen: false));
        var removal = app.FindById("DeveloperRemovalDialog", PageTimeout);
        Assert.Equal("Revoke Zapier?", removal.Name);
        _ = app.FindById("DeveloperRemovalBody", PageTimeout, onScreen: false);
        Press(Wait.For(
            () => removal.FindFirstDescendant(cf => cf.ByAutomationId("PrimaryButton")) is { IsEnabled: true } enabled ? enabled : null,
            PageTimeout,
            "Revoke key to work",
            app.Describe));
        Assert.True(
            Wait.Until(() => app.TryFindById("DeveloperRemovalDialog", onScreen: false) is null, PageTimeout),
            "the revoke stayed asked after its confirm");
        Assert.True(
            Wait.Until(() => app.TryFindById("DeveloperRemoving", onScreen: false) is null, PageTimeout),
            "the revoke stayed on its way");
        Assert.Null(app.TryFindById("DeveloperRemovalFailure", onScreen: false));
        InstalledApp.Log("Revoke asked once, and the key was revoked");

        // The apps: the MCP address, and Raycast's Revoke asked, then kept.
        ScrollTo(app, "SchedulingDeveloper", 0);
        Press(app.FindById("DeveloperTabApps", PageTimeout));
        var mcp = app.FindById("DeveloperMcpUrl", PageTimeout, onScreen: false);
        Assert.EndsWith("/mcp", mcp.Name, StringComparison.Ordinal);
        Press(app.FindById("DeveloperAppRevoke", PageTimeout, onScreen: false));
        Assert.Equal("Revoke Raycast?", app.FindById("DeveloperRemovalDialog", PageTimeout).Name);
        Keyboard.Type(VirtualKeyShort.ESCAPE);
        Assert.True(
            Wait.Until(() => app.TryFindById("DeveloperRemovalDialog", onScreen: false) is null, PageTimeout),
            "the app's revoke stayed asked after Escape");
        InstalledApp.Log("the apps show beside the MCP address, and Revoke asks");

        // The webhooks: the first one's deliveries open under their heading, and close.
        ScrollTo(app, "SchedulingDeveloper", 0);
        Press(app.FindById("DeveloperTabWebhooks", PageTimeout));
        Press(app.FindById("DeveloperWebhookDeliveries", PageTimeout, onScreen: false));
        var heading = app.FindById("DeveloperDeliveriesHeading", PageTimeout, onScreen: false);
        Assert.Contains("hooks.contract.test/bookings", heading.Name, StringComparison.Ordinal);
        Assert.True(
            Wait.Until(() => window.FindAllDescendants(cf => cf.ByAutomationId("DeveloperDeliveryOutcome")).Length == 2, PageTimeout),
            "the deliveries did not fill in");
        InstalledApp.Log("a webhook's deliveries opened");
        Press(app.FindById("DeveloperWebhookDeliveries", PageTimeout, onScreen: false));
        Assert.True(
            Wait.Until(() => app.TryFindById("DeveloperDeliveriesHeading", onScreen: false) is null, PageTimeout),
            "the deliveries stayed open after Deliveries was pressed again");

        // Its Edit: its own form, an event switched on, and the save lands.
        Press(app.FindById("DeveloperWebhookEdit", PageTimeout, onScreen: false));
        var editor = app.FindById("DeveloperWebhookDialog", PageTimeout);
        Assert.Equal("Edit webhook", editor.Name);
        Assert.Contains(
            "hooks.contract.test/bookings",
            app.FindById("DeveloperWebhookUrlFixed", PageTimeout, onScreen: false).Name,
            StringComparison.Ordinal);
        Press(app.FindById("DeveloperWebhookEventbooking.rescheduled", PageTimeout));
        Press(Wait.For(
            () => editor.FindFirstDescendant(cf => cf.ByAutomationId("PrimaryButton")) is { IsEnabled: true } enabled ? enabled : null,
            PageTimeout,
            "Save changes to work",
            app.Describe));
        _ = app.FindById("DeveloperWebhookNotice", PageTimeout, onScreen: false);
        Assert.Null(app.TryFindById("DeveloperWebhookFailure", onScreen: false));
        InstalledApp.Log("a webhook's events were saved");
        Keyboard.Type(VirtualKeyShort.ESCAPE);
        Assert.True(
            Wait.Until(() => app.TryFindById("DeveloperWebhookDialog", onScreen: false) is null, PageTimeout),
            "the webhook's form stayed open after Escape");

        // The second webhook's Delete: asked once, then it lands.
        var deletes = Wait.For(
            () => window.FindAllDescendants(cf => cf.ByAutomationId("DeveloperWebhookDelete")) is { Length: 2 } both ? both : null,
            PageTimeout,
            "both webhooks' Delete",
            app.Describe);
        Press(deletes[1]);
        var delete = app.FindById("DeveloperRemovalDialog", PageTimeout);
        Assert.Equal("Delete this webhook?", delete.Name);
        Press(Wait.For(
            () => delete.FindFirstDescendant(cf => cf.ByAutomationId("PrimaryButton")) is { IsEnabled: true } enabled ? enabled : null,
            PageTimeout,
            "Delete webhook to work",
            app.Describe));
        Assert.True(
            Wait.Until(() => app.TryFindById("DeveloperRemovalDialog", onScreen: false) is null, PageTimeout),
            "the delete stayed asked after its confirm");
        Assert.True(
            Wait.Until(() => app.TryFindById("DeveloperRemoving", onScreen: false) is null, PageTimeout),
            "the delete stayed on its way");
        Assert.Null(app.TryFindById("DeveloperRemovalFailure", onScreen: false));
        InstalledApp.Log("Delete asked once, and the webhook was deleted");
        Assert.Null(Failure(app));
        Assert.True(app.IsRunning, "the app ended in Developer");
    }

    /// <summary>
    /// Scrolls the list row holding <paramref name="rowId"/> into view, as a
    /// member scrolls the list to a row below its fold.
    /// </summary>
    private static void ScrollRowIntoView(InstalledApp app, string rowId)
    {
        var walker = app.Automation.TreeWalkerFactory.GetControlViewWalker();
        var title = app.FindById(rowId, PageTimeout, onScreen: false);
        for (var at = walker.GetParent(title); at is not null; at = walker.GetParent(at))
        {
            if (at.Properties.ControlType.ValueOrDefault == ControlType.ListItem)
            {
                at.Patterns.ScrollItem.PatternOrDefault?.ScrollIntoView();
                break;
            }
        }
        _ = app.FindById(rowId, PageTimeout);
    }

    /// <summary>
    /// Scrolls the scroll viewer <paramref name="automationId"/> to
    /// <paramref name="percent"/> of its height (0 the top, 100 the end), as a
    /// member scrolls to what is below the fold; nothing when it all fits.
    /// </summary>
    private static void ScrollTo(InstalledApp app, string automationId, double percent)
    {
        // UI Automation's "no scroll" for the axis left alone.
        const double NoScroll = -1;
        var viewer = app.FindById(automationId, PageTimeout);
        if (viewer.Patterns.Scroll.PatternOrDefault is { } scroll && scroll.VerticallyScrollable.ValueOrDefault)
        {
            scroll.SetScrollPercent(NoScroll, percent);
        }
    }

    /// <summary>
    /// Skips off Windows, and unless DISTRICTAI_UI_SCRIPTED is 1 (a scripted
    /// package is installed). With it, the package must be installed.
    /// </summary>
    private static void RequireScriptedPackage()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The UI tests run on Windows only.");
        Assert.SkipUnless(
            Environment.GetEnvironmentVariable("DISTRICTAI_UI_SCRIPTED") == "1",
            "The scene walk needs the scripted test package (DISTRICTAI_UI_SCRIPTED=1; CONTRIBUTING.md, \"UI smoke tests\").");
        Assert.True(
            Native.IsPackageInstalled(InstalledApp.Family),
            $"The package family {InstalledApp.Family} is not installed for this user.");
    }
}
