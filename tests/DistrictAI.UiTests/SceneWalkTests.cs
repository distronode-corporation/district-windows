using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using Xunit;

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
    private static readonly TimeSpan _pageTimeout = TimeSpan.FromSeconds(30);

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
    /// shows. Walked when the button is there (its area built).
    /// </summary>
    private static readonly (string Entry, string Button, string Heading)[] _subPages =
    [
        ("Contacts", "Blocked callers", "Blocked callers"),
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

    /// <summary>What a page that failed shows (StatusPanel, UnavailablePage, the core's failure titles).</summary>
    private static readonly string[] _failureTexts = ["Something went wrong", "Could not load", "Not in this version yet"];

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
            _pageTimeout,
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
                if (!Visit(app, handle, sub.Button, () => app.Find(ControlType.Button, sub.Button, _pageTimeout).AsButton().Invoke(), sub.Heading, shots, ++index, problems))
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

        Open(app, "Calls");
        app.Find(ControlType.Button, "Place a call", _pageTimeout).AsButton().Invoke();
        app.Find(ControlType.Edit, "Number to call", _pageTimeout).AsTextBox().Text = "+12125550142";
        Wait.For(
            () => app.TryFind(ControlType.Button, "Call") is { IsEnabled: true } call ? call : null,
            _pageTimeout,
            "Call to work",
            app.Describe).AsButton().Invoke();

        _ = app.Find(null, "Live transcript", _pageTimeout);
        _ = app.Find(ControlType.Text, "I'd like to book a cleaning on Thursday.", _pageTimeout);
        InstalledApp.Log("the call strip shows the live transcript");
        if (shots is { Length: > 0 })
        {
            Save(handle, Path.Combine(shots, "90-live-transcript.png"), clientOnly: true);
        }
        app.Find(ControlType.Button, "Hang up", _pageTimeout).AsButton().Invoke();
        _ = app.Find(ControlType.Text, "Call ended", _pageTimeout);
        Assert.True(app.IsRunning, "the app ended during the call");
    }

    /// <summary>
    /// Opens a page with <paramref name="open"/> and checks it: its heading
    /// (<paramref name="expected"/>, or any), loaded, no failure, then saves
    /// it when screenshots are on. With <paramref name="editName"/>, the
    /// page is ready only once a text box of that name shows. Problems go in
    /// <paramref name="problems"/>; false when the app has ended.
    /// </summary>
    private static bool Visit(InstalledApp app, nint handle, string name, Action open, string? expected, string? shots, int index, List<string> problems, string? editName = null)
    {
        var clock = Stopwatch.StartNew();
        try
        {
            open();
            InstalledApp.Log($"opened \"{name}\"");
            var heading = Heading(app, expected, _pageTimeout);
            // Loaded: the status panel's progress ring gone.
            _ = Wait.For(
                () => app.TryFind(ControlType.ProgressBar, "Loading") is null ? heading : null,
                _pageTimeout,
                $"\"{name}\" to finish loading",
                app.Describe);
            if (editName is not null)
            {
                _ = app.Find(ControlType.Edit, editName, _pageTimeout);
            }
            if (Failure(app) is { } failure)
            {
                problems.Add($"\"{name}\" shows \"{failure}\".{Environment.NewLine}{app.Describe()}");
            }
            else
            {
                InstalledApp.Log(FormattableString.Invariant($"\"{name}\": \"{heading.Name}\" in {clock.ElapsedMilliseconds} ms"));
                if (shots is { Length: > 0 })
                {
                    Save(handle, Path.Combine(shots, FormattableString.Invariant($"{index:D2}-{FileName(name)}.png")), clientOnly: true);
                }
            }
        }
        catch (TimeoutException error)
        {
            problems.Add($"\"{name}\": {error.Message}");
        }
        if (app.IsRunning)
        {
            return true;
        }
        problems.Add($"the app ended after \"{name}\" was opened");
        return false;
    }

    /// <summary>The names of the pane's entries, in order (headings and separators are not entries).</summary>
    private static string[] NavEntries(Window window) =>
        [.. window.FindAllDescendants(cf => cf.ByControlType(ControlType.ListItem))
            .Where(item => item.ClassName.EndsWith(".NavigationViewItem", StringComparison.Ordinal) || item.ClassName == "NavigationViewItem")
            .Select(item => item.Name)
            .Where(name => name.Length > 0)
            .Distinct()];

    /// <summary>
    /// Opens the hub's row for <paramref name="title"/>, as a click on it does.
    /// A row's name is its title, then what the section holds.
    /// </summary>
    private static void OpenSettingsRow(InstalledApp app, string title)
    {
        var row = Wait.For(
            () => app.Automation.GetDesktop()
                .FindFirstChild(cf => cf.ByProcessId(app.ProcessId).And(cf.ByControlType(ControlType.Window)))
                ?.FindAllDescendants(cf => cf.ByControlType(ControlType.ListItem))
                .FirstOrDefault(item => !item.Properties.IsOffscreen.ValueOrDefault
                    && NameOf(item).StartsWith(title + ". ", StringComparison.Ordinal)),
            _pageTimeout,
            $"the settings row \"{title}\"",
            app.Describe);
        if (row.Patterns.Invoke.IsSupported)
        {
            row.Patterns.Invoke.Pattern.Invoke();
        }
        else
        {
            row.Click();
        }
    }

    /// <summary>Opens the Inbox's first conversation, as a click on it does.</summary>
    private static void OpenFirstConversation(InstalledApp app)
    {
        var list = app.Find(ControlType.List, "Conversations", _pageTimeout);
        var first = Wait.For(
            () => list.FindFirstDescendant(cf => cf.ByControlType(ControlType.ListItem)),
            _pageTimeout,
            "a conversation in the Inbox",
            app.Describe);
        if (first.Patterns.Invoke.IsSupported)
        {
            first.Patterns.Invoke.Pattern.Invoke();
        }
        else
        {
            first.Click();
        }
    }

    /// <summary>Chooses the entry named <paramref name="entry"/>, as a click does.</summary>
    private static void Open(InstalledApp app, string entry)
    {
        var item = app.Find(ControlType.ListItem, entry, _pageTimeout);
        if (item.Patterns.Invoke.IsSupported)
        {
            item.Patterns.Invoke.Pattern.Invoke();
        }
        else
        {
            item.Click();
        }
    }

    /// <summary>
    /// The page's level-one heading: <paramref name="expected"/>, or any
    /// heading when no text is expected, but never the unavailable page's.
    /// </summary>
    private static AutomationElement Heading(InstalledApp app, string? expected, TimeSpan timeout) =>
        Wait.For(
            () => app.Automation.GetDesktop()
                .FindFirstChild(cf => cf.ByProcessId(app.ProcessId).And(cf.ByControlType(ControlType.Window)))
                ?.FindAllDescendants(cf => cf.ByControlType(ControlType.Text))
                .FirstOrDefault(text =>
                    !text.Properties.IsOffscreen.ValueOrDefault
                    && text.Properties.HeadingLevel.ValueOrDefault == HeadingLevel.Level1
                    && NameOf(text) is { Length: > 0 } name
                    && (expected is null || name == expected)),
            timeout,
            expected is null ? "a level-one heading" : $"the heading \"{expected}\"",
            app.Describe);

    /// <summary>
    /// The element's name, or empty when UI Automation has none for it: a text
    /// element can be gone, or never named, between being listed and being
    /// read (seen as PropertyNotSupportedException on the Inbox).
    /// </summary>
    private static string NameOf(AutomationElement element) => element.Properties.Name.ValueOrDefault ?? string.Empty;

    /// <summary>What shows that the page failed, or null.</summary>
    private static string? Failure(InstalledApp app)
    {
        var window = app.Automation.GetDesktop()
            .FindFirstChild(cf => cf.ByProcessId(app.ProcessId).And(cf.ByControlType(ControlType.Window)));
        if (window is null)
        {
            return "no window";
        }
        if (window.FindFirstDescendant(cf => cf.ByControlType(ControlType.Button).And(cf.ByName("Try again"))) is { IsOffscreen: false })
        {
            return "Try again";
        }
        return window.FindAllDescendants(cf => cf.ByControlType(ControlType.Text))
            .Where(text => !text.Properties.IsOffscreen.ValueOrDefault)
            .Select(NameOf)
            .FirstOrDefault(name => _failureTexts.Any(failure => name.StartsWith(failure, StringComparison.Ordinal)));
    }

    /// <summary>
    /// Makes the window's client area <paramref name="width"/> by
    /// <paramref name="height"/>, and waits until it is. A window with its
    /// title bar and frame would not fit a display of that size (the hosted
    /// runner's largest), so the frame is taken off first.
    /// </summary>
    private static void SizeClient(nint handle, int width, int height)
    {
        Native.Frameless(handle, width, height);
        var sized = Wait.Until(
            () => Native.GetClientRect(handle, out var now) && now.Width == width && now.Height == height,
            TimeSpan.FromSeconds(10));
        _ = Native.GetClientRect(handle, out var actual);
        InstalledApp.Log(FormattableString.Invariant($"client area {actual.Width}x{actual.Height} (asked for {width}x{height})"));
        Assert.True(sized, FormattableString.Invariant($"the client area is {actual.Width}x{actual.Height}, not {width}x{height}: is the display smaller?"));
    }

    /// <summary>
    /// Saves what the window draws as a PNG: its client area, or the visible
    /// window (title bar and frame, without the invisible resize borders).
    /// </summary>
    private static void Save(nint handle, string path, bool clientOnly)
    {
        _ = Native.SetForegroundWindow(handle);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _ = Native.GetWindowRect(handle, out var outer);
        Native.Rect area;
        if (clientOnly)
        {
            _ = Native.GetClientRect(handle, out var client);
            var origin = default(Native.Point);
            _ = Native.ClientToScreen(handle, ref origin);
            area = new Native.Rect { Left = origin.X, Top = origin.Y, Right = origin.X + client.Width, Bottom = origin.Y + client.Height };
        }
        else
        {
            area = Native.VisibleBounds(handle);
        }
        using var whole = new Bitmap(outer.Width, outer.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(whole))
        {
            var dc = graphics.GetHdc();
            try
            {
                Assert.True(Native.PrintWindow(handle, dc, 2), "PrintWindow failed");
            }
            finally
            {
                graphics.ReleaseHdc(dc);
            }
        }
        var crop = new Rectangle(area.Left - outer.Left, area.Top - outer.Top, area.Width, area.Height);
        using var image = whole.Clone(crop, PixelFormat.Format32bppArgb);
        image.Save(path, ImageFormat.Png);
        InstalledApp.Log(FormattableString.Invariant($"saved {path} ({image.Width}x{image.Height})"));
    }

    private static string FileName(string entry) =>
        new([.. entry.Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-')]);

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
