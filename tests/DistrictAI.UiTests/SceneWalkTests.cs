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
    /// The heading each 1.0 page shows, by the start of its entry's name (the
    /// inbox's name carries its unread count). An area of 2.0 that is not
    /// listed must still show a level-one heading; add it here once built.
    /// </summary>
    private static readonly (string Entry, string Heading)[] _headings =
    [
        ("Overview", WorkspaceName),
        ("Inbox", "Conversations"),
        ("Calls", "Calls"),
        ("Contacts", "Contacts"),
        ("Account", "Account"),
        ("Analytics", "Analytics"),
        ("Support", "Support"),
        ("District HQ", "District HQ"),
        ("Help desk", "Help desk"),
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
            var expected = _headings.FirstOrDefault(known => entry.StartsWith(known.Entry, StringComparison.Ordinal)).Heading;
            if (!Visit(app, handle, entry, () => Open(app, entry), expected, shots, ++index, problems))
            {
                break;
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
    /// Opens a page with <paramref name="open"/> and checks it: its heading
    /// (<paramref name="expected"/>, or any), loaded, no failure, then saves
    /// it when screenshots are on. Problems go in <paramref name="problems"/>;
    /// false when the app has ended.
    /// </summary>
    private static bool Visit(InstalledApp app, nint handle, string name, Action open, string? expected, string? shots, int index, List<string> problems)
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
                    !text.IsOffscreen
                    && text.Properties.HeadingLevel.ValueOrDefault == HeadingLevel.Level1
                    && (expected is null ? text.Name.Length > 0 : text.Name == expected)),
            timeout,
            expected is null ? "a level-one heading" : $"the heading \"{expected}\"",
            app.Describe);

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
            .Where(text => !text.IsOffscreen)
            .Select(text => text.Name)
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
