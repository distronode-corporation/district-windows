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
/// DISTRICTAI_SCREENSHOTS set to a folder, each page is also saved there at
/// 1920x1080, for the Store listing.
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
        if (shots is { Length: > 0 })
        {
            Size(app, handle, 1920, 1080);
        }

        // Signed in by the scene, on the overview.
        _ = Heading(app, WorkspaceName, _startTimeout);
        var entries = Wait.For(
            () => NavEntries(window) is { Length: >= 5 } found ? found : null,
            _pageTimeout,
            "the navigation pane's entries",
            app.Describe);
        InstalledApp.Log($"the pane offers {entries.Length}: [{string.Join(", ", entries)}]");

        var problems = new List<string>();
        var index = 0;
        foreach (var entry in entries)
        {
            index++;
            var clock = Stopwatch.StartNew();
            try
            {
                Open(app, entry);
                var expected = _headings.FirstOrDefault(known => entry.StartsWith(known.Entry, StringComparison.Ordinal)).Heading;
                var heading = Heading(app, expected, _pageTimeout);
                // Loaded: the status panel's progress ring gone.
                _ = Wait.For(
                    () => app.TryFind(ControlType.ProgressBar, "Loading") is null ? heading : null,
                    _pageTimeout,
                    $"\"{entry}\" to finish loading",
                    app.Describe);
                if (Failure(app) is { } failure)
                {
                    problems.Add($"\"{entry}\" shows \"{failure}\".{Environment.NewLine}{app.Describe()}");
                    continue;
                }
                InstalledApp.Log(FormattableString.Invariant($"\"{entry}\": \"{heading.Name}\" in {clock.ElapsedMilliseconds} ms"));
                if (shots is { Length: > 0 })
                {
                    Save(handle, Path.Combine(shots, FormattableString.Invariant($"{index:D2}-{FileName(entry)}.png")));
                }
            }
            catch (TimeoutException error)
            {
                problems.Add($"\"{entry}\": {error.Message}");
            }
            if (!app.IsRunning)
            {
                problems.Add($"the app ended after \"{entry}\" was opened");
                break;
            }
        }
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine + Environment.NewLine, problems));
        Assert.True(app.IsRunning, "the app ended during the walk");
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
        InstalledApp.Log($"opened \"{entry}\"");
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

    /// <summary>Sizes the window to <paramref name="width"/> by <paramref name="height"/>, and waits until it is.</summary>
    private static void Size(InstalledApp app, nint handle, int width, int height)
    {
        _ = Native.MoveWindow(handle, 0, 0, width, height, repaint: true);
        var sized = Wait.Until(
            () =>
            {
                var bounds = app.MainWindow(_pageTimeout).BoundingRectangle;
                return bounds.Width == width && bounds.Height == height;
            },
            TimeSpan.FromSeconds(10));
        var actual = app.MainWindow(_pageTimeout).BoundingRectangle;
        InstalledApp.Log(FormattableString.Invariant($"window {actual.Width}x{actual.Height} (asked for {width}x{height})"));
        Assert.True(sized, FormattableString.Invariant($"the window is {actual.Width}x{actual.Height}, not {width}x{height}: is the display smaller?"));
    }

    /// <summary>Saves what the window draws, all of it, as a PNG.</summary>
    private static void Save(nint handle, string path)
    {
        _ = Native.SetForegroundWindow(handle);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var bounds = WindowBounds(handle);
        using var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
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
        bitmap.Save(path, ImageFormat.Png);
        InstalledApp.Log(FormattableString.Invariant($"saved {path} ({bounds.Width}x{bounds.Height})"));
    }

    private static Rectangle WindowBounds(nint handle)
    {
        using var automation = new FlaUI.UIA3.UIA3Automation();
        var element = automation.FromHandle(handle);
        var bounds = element.BoundingRectangle;
        return new Rectangle(bounds.X, bounds.Y, bounds.Width, bounds.Height);
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
