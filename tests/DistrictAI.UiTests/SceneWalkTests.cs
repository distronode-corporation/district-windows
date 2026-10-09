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
    /// The pages whose heading is not their pane entry's own name, by the start
    /// of the entry's name (the inbox's name carries its unread count). Every
    /// other entry's page must show its entry's name as its level-one heading,
    /// so building an area adds nothing here.
    /// </summary>
    private static readonly (string Entry, string Heading)[] _headings =
    [
        ("Overview", WorkspaceName),
        ("Inbox", "Conversations"),
    ];

    /// <summary>
    /// Pages a button on another page opens, not a pane entry: from the page
    /// of <c>Entry</c>, the button named <c>Button</c>, and the heading it
    /// shows, and, for a panel on the same page, the text box that shows it
    /// open (or null). Walked when the button is there (its area built).
    /// </summary>
    private static readonly (string Entry, string Button, string Heading, string? Edit)[] _subPages =
    [
        ("Contacts", "Blocked callers", "Blocked callers", null),
        // The plan chooser opens on the billing page itself; nothing is bought.
        ("Billing", "Choose a plan", "Billing", "Promotion code"),
    ];

    /// <summary>
    /// The settings sections built so far, by their hub row's title: each is
    /// opened from the Workspace settings hub, and its page's level-one
    /// heading must be that title. A section's packet adds its row here.
    /// </summary>
    private static readonly string[] _settingsSections =
    [
        "Persona",
        "Voice",
        "Messaging accounts",
        "Members",
        "Call handling",
        "Call routing rules",
        "Transfer directory",
        "Skills",
        "Knowledge",
    ];

    /// <summary>
    /// The conversation the Inbox opens first: the scene answers the inbox with
    /// the core's district-conversations.json, whose first thread is this
    /// contact's (the thread page's heading), and its timeline and draft from
    /// fixtures too, so the reply box shows (scripted.rs,
    /// the_first_conversation_opens_with_its_reply_box).
    /// </summary>
    public const string ConversationTitle = "Contract Test Caller";

    /// <summary>The reply box's UI Automation name (Views/Composer/ComposerBox.xaml).</summary>
    public const string ReplyBoxName = "Reply";

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
            () => NavEntries(window) is { Length: >= 5 } found ? found : null,
            PageTimeout,
            "the navigation pane's entries",
            app.Describe);
        InstalledApp.Log($"the pane offers {entries.Length}: [{string.Join(", ", entries)}]");

        if (shots is { Length: > 0 })
        {
            // The whole window once, title bar included, then the pages.
            Save(handle, Path.Combine(shots, "00-window.png"), clientOnly: false);
            SizeClient(handle, 1920, 1080);
        }

        var problems = new List<string>();
        var index = 0;
        foreach (var entry in entries)
        {
            var expected = _headings.FirstOrDefault(known => entry.StartsWith(known.Entry, StringComparison.Ordinal)).Heading ?? entry;
            if (!Visit(app, handle, entry, () => Open(app, entry), expected, shots, ++index, problems))
            {
                break;
            }
            // The reply box lives in a conversation, not behind a pane entry:
            // from the Inbox, the first conversation is opened and its box
            // waited for by name.
            if (entry.StartsWith("Inbox", StringComparison.Ordinal)
                && !Visit(app, handle, "Conversation", () => OpenFirstConversation(app), ConversationTitle, shots, ++index, problems, ReplyBoxName))
            {
                break;
            }
            // The built settings sections, each from the hub's row, and back to
            // the hub for the next.
            if (entry.StartsWith("Workspace settings", StringComparison.Ordinal))
            {
                foreach (var section in _settingsSections)
                {
                    if (!Visit(app, handle, section, () => OpenSettingsRow(app, section), section, shots, ++index, problems)
                        || !Visit(app, handle, entry, () => Open(app, entry), entry, null, index, problems))
                    {
                        break;
                    }
                }
            }
            foreach (var sub in _subPages.Where(sub => entry.StartsWith(sub.Entry, StringComparison.Ordinal)))
            {
                if (app.TryFind(ControlType.Button, sub.Button) is null)
                {
                    InstalledApp.Log($"\"{entry}\" has no \"{sub.Button}\" button: not walked");
                    continue;
                }
                if (!Visit(app, handle, sub.Button, () => app.Find(ControlType.Button, sub.Button, PageTimeout).AsButton().Invoke(), sub.Heading, shots, ++index, problems, sub.Edit))
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

        OpenUntil(app, "Calls", "Calls");
        app.Find(ControlType.Button, "Place a call", PageTimeout).AsButton().Invoke();
        app.Find(ControlType.Edit, "Number to call", PageTimeout).AsTextBox().Text = "+12125550142";
        Wait.For(
            () => app.TryFind(ControlType.Button, "Call") is { IsEnabled: true } call ? call : null,
            PageTimeout,
            "Call to work",
            app.Describe).AsButton().Invoke();

        _ = app.Find(null, "Live transcript", PageTimeout);
        _ = app.Find(ControlType.Text, "I'd like to book a cleaning on Thursday.", PageTimeout);
        InstalledApp.Log("the call strip shows the live transcript");
        if (shots is { Length: > 0 })
        {
            Save(handle, Path.Combine(shots, "90-live-transcript.png"), clientOnly: true);
        }
        app.Find(ControlType.Button, "Hang up", PageTimeout).AsButton().Invoke();
        _ = app.Find(ControlType.Text, "Call ended", PageTimeout);
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

        OpenUntil(app, "Calls", "Calls");
        app.Find(ControlType.Button, "Place a call", PageTimeout).AsButton().Invoke();
        app.Find(ControlType.Edit, "Number to call", PageTimeout).AsTextBox().Text = "+12125550142";
        Wait.For(
            () => app.TryFind(ControlType.Button, "Call") is { IsEnabled: true } call ? call : null,
            PageTimeout,
            "Call to work",
            app.Describe).AsButton().Invoke();
        _ = app.Find(null, "Live transcript", PageTimeout);
        var mute = Wait.For(
            () => app.TryFind(ControlType.Button, "Mute") is { IsEnabled: true } found && found.Patterns.Toggle.IsSupported ? found : null,
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
        _ = app.Find(ControlType.Text, "Call ended", PageTimeout);
        InstalledApp.Log("Ctrl+Shift+H: the call ended");
        Assert.True(app.IsRunning, "the app ended during the call");
    }

    private static ToggleState? MuteState(InstalledApp app) =>
        app.TryFind(ControlType.Button, "Mute") is { } mute && mute.Patterns.Toggle.IsSupported
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

        OpenUntil(app, "Meeting rooms", "Meeting rooms");
        var row = Wait.For(
            () => window.FindAllDescendants(cf => cf.ByControlType(ControlType.ListItem))
                .FirstOrDefault(item => item.Name.StartsWith("Weekly review", StringComparison.Ordinal)),
            PageTimeout,
            "the meeting \"Weekly review\"",
            app.Describe);
        Activate(row);
        _ = app.Find(ControlType.Text, "Minutes", PageTimeout);
        _ = app.Find(ControlType.Text, "Action items", PageTimeout);
        _ = app.Find(ControlType.Button, "Report", PageTimeout);
        InstalledApp.Log("the meeting record shows its minutes and action items, with Report");
        if (shots is { Length: > 0 })
        {
            Save(handle, Path.Combine(shots, "91-meeting-record.png"), clientOnly: true);
        }
        app.Find(ControlType.Button, "Close the meeting", PageTimeout).AsButton().Invoke();
        Assert.True(app.IsRunning, "the app ended in the meeting rooms");
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
