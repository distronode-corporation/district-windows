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
        "SchedulingSectionBookings",
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
        // waited for by its heading's id, as the walk does.
        _ = OpenById(app, "NavInbox");
        _ = app.FindById("InboxHeading", PageTimeout);
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
            () => owned.FindFirstDescendant(cf => cf.ByControlType(ControlType.Button).And(cf.ByName("Release this number"))),
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
