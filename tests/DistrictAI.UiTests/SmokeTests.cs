using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using Xunit;

// One app per user: two tests at once would drive the same instance.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace DistrictAI.UiTests;

/// <summary>
/// The installed package, started as a user starts it, up to the sign-in page:
/// what a broken package, manifest or start-up shows first. Nothing here signs
/// in or reaches the service.
/// </summary>
public sealed class SmokeTests
{
    /// <summary>A cold start: the runtime, the core's restore of a session that is not there, the first frame.</summary>
    private static readonly TimeSpan _startTimeout = TimeSpan.FromSeconds(90);

    private static readonly TimeSpan _stepTimeout = TimeSpan.FromSeconds(30);

    // The sign-in page's button (Views/SignInPage.xaml) and error bar, and the
    // signed-out heading district-ffi gives a first run (screen.rs, WELCOME_TITLE).
    private const string SignInButton = "Sign in with your browser";
    private const string SignInProblem = "Sign-in problem";
    private const string WelcomeTitle = "Welcome to District AI";

    // TrayIcon.cs: the icon's callback message (WM_APP + 1) and id, and App.TrayMenu's items.
    private const uint TrayCallbackMessage = Native.WmApp + 1;
    private const int TrayIconId = 1;
    private const string TrayOpen = "Open District AI";
    private const string TrayQuit = "Quit";

    [Fact]
    public void StartsAndShowsTheSignInPage()
    {
        RequireInstalledApp();
        using var app = InstalledApp.Launch();

        var window = app.MainWindow(_startTimeout);
        var button = app.Find(ControlType.Button, SignInButton, _startTimeout);
        _ = app.Find(ControlType.Text, WelcomeTitle, _stepTimeout);

        Assert.True(button.IsEnabled, $"\"{SignInButton}\" is disabled.{Environment.NewLine}{app.Describe()}");
        Assert.True(Native.IsWindowVisible(window.Properties.NativeWindowHandle.Value), "the window is not visible");
        Assert.Equal([app.ProcessId], InstalledApp.RunningProcessIds());
    }

    [Fact]
    public void ALinkWhileRunningGoesToTheFirstInstance()
    {
        RequireInstalledApp();
        using var app = InstalledApp.Launch();
        var window = app.MainWindow(_startTimeout);
        _ = app.Find(ControlType.Button, SignInButton, _startTimeout);
        var handle = window.Properties.NativeWindowHandle.Value;

        // A sign-in answer nobody asked for: the core says so on the sign-in
        // page and starts nothing, so seeing that message proves the second
        // process handed the link to the first.
        InstalledApp.OpenLink("districtai://auth?code=x&state=y");
        var others = new HashSet<int>();
        _ = Wait.For(
            () =>
            {
                others.UnionWith(InstalledApp.RunningProcessIds().Where(id => id != app.ProcessId));
                return app.TryFind(null, SignInProblem);
            },
            _stepTimeout,
            $"\"{SignInProblem}\" after the link",
            app.Describe);
        InstalledApp.Log($"other District AI processes seen meanwhile: [{string.Join(", ", others)}]");

        // The second process exits once it has handed the link over.
        Assert.True(
            Wait.Until(() => InstalledApp.RunningProcessIds().SequenceEqual([app.ProcessId]), _stepTimeout),
            $"expected only the first instance ({app.ProcessId}) to be left.{Environment.NewLine}{app.Describe()}");

        // And the first still answers: its window's thread, and its UI.
        Assert.True(Native.Responds(handle, TimeSpan.FromSeconds(5)), "the window did not answer WM_NULL within 5 s");
        var button = app.Find(ControlType.Button, SignInButton, _stepTimeout);
        Assert.True(button.IsEnabled, $"\"{SignInButton}\" is disabled after the link.{Environment.NewLine}{app.Describe()}");
        Assert.Equal([app.ProcessId], InstalledApp.RunningProcessIds());
    }

    [Fact]
    public void ClosingTheWindowHidesItAndTheAppKeepsRunning()
    {
        RequireInstalledApp();
        using var app = InstalledApp.Launch();
        var window = app.MainWindow(_startTimeout);
        _ = app.Find(ControlType.Button, SignInButton, _startTimeout);
        var handle = window.Properties.NativeWindowHandle.Value;

        window.Close();
        Assert.True(
            Wait.Until(() => !Native.IsWindowVisible(handle), _stepTimeout),
            $"the window is still visible after Close.{Environment.NewLine}{app.Describe()}");

        // Still running a moment later (a crash on close would show by now).
        Assert.False(
            Wait.Until(() => !app.IsRunning, TimeSpan.FromSeconds(3)),
            "the app ended when its window was closed");

        // Starting it again shows the same window from the same process.
        InstalledApp.LaunchAgain();
        Assert.True(
            Wait.Until(() => Native.IsWindowVisible(handle), _stepTimeout),
            $"starting the app again did not show its window.{Environment.NewLine}{app.Describe()}");
        _ = app.Find(ControlType.Button, SignInButton, _stepTimeout);
        Assert.True(
            Wait.Until(() => InstalledApp.RunningProcessIds().SequenceEqual([app.ProcessId]), _stepTimeout),
            $"expected only the first instance ({app.ProcessId}) to be left.{Environment.NewLine}{app.Describe()}");
    }

    [Fact]
    public void QuitFromTheTrayMenuEndsTheApp()
    {
        RequireInstalledApp();
        using var app = InstalledApp.Launch();
        var window = app.MainWindow(_startTimeout);
        _ = app.Find(ControlType.Button, SignInButton, _startTimeout);
        var handle = window.Properties.NativeWindowHandle.Value;

        ReportTheNotificationArea(app);

        // What Explorer sends the window when its icon is right-clicked
        // (NOTIFYICON_VERSION_4): WM_CONTEXTMENU and the icon's id in lParam,
        // the anchor point in wParam. The menu, its items and Quit are the
        // app's own code from there on.
        const int x = 400;
        const int y = 400;
        Assert.True(
            Native.PostMessage(handle, TrayCallbackMessage, (nuint)(x | (y << 16)), (nint)(Native.WmContextMenu | (TrayIconId << 16))),
            "PostMessage to the window failed");
        var menu = Wait.For(
            () => app.Automation.GetDesktop()
                .FindFirstChild(cf => cf.ByProcessId(app.ProcessId).And(cf.ByControlType(ControlType.Menu))),
            _stepTimeout,
            "the tray menu",
            app.Describe);
        var items = menu.FindAllDescendants(cf => cf.ByControlType(ControlType.MenuItem)).Select(item => item.Name).ToArray();
        InstalledApp.Log($"tray menu items: [{string.Join(", ", items)}]");
        Assert.Equal([TrayOpen, TrayQuit], items);

        var quit = menu.FindFirstDescendant(cf => cf.ByControlType(ControlType.MenuItem).And(cf.ByName(TrayQuit)));
        Assert.NotNull(quit);
        _ = quit.AsMenuItem().Invoke();
        Assert.True(
            Wait.Until(() => !app.IsRunning, _stepTimeout),
            $"the app was still running {_stepTimeout.TotalSeconds} s after Quit.{Environment.NewLine}{app.Describe()}");
        Assert.Empty(InstalledApp.RunningProcessIds());
    }

    /// <summary>
    /// Whether the icon itself can be reached and right-clicked on this
    /// machine: the taskbar's "Show Hidden Icons" flyout opened, the icon
    /// looked for by its tooltip, right-clicked, and the menu that opens (if
    /// one does) closed again. Reported, never asserted: it depends on the
    /// shell's flyout, real mouse input and the foreground, none of which the
    /// app controls.
    /// </summary>
    private static void ReportTheNotificationArea(InstalledApp app)
    {
        AutomationElement? Menu() =>
            app.Automation.GetDesktop().FindFirstChild(cf => cf.ByProcessId(app.ProcessId).And(cf.ByControlType(ControlType.Menu)));
        try
        {
            var desktop = app.Automation.GetDesktop();
            var taskbar = desktop.FindFirstChild(cf => cf.ByClassName("Shell_TrayWnd"));
            var chevron = taskbar?.FindFirstDescendant(cf => cf.ByControlType(ControlType.Button).And(cf.ByName("Show Hidden Icons")));
            if (chevron is null)
            {
                InstalledApp.Log($"notification area: {(taskbar is null ? "no taskbar" : "no \"Show Hidden Icons\" button")} in UI Automation");
                return;
            }
            chevron.Click();
            AutomationElement? icon = null;
            var found = Wait.Until(
                () => (icon = app.Automation.GetDesktop().FindFirstDescendant(cf => cf.ByControlType(ControlType.Button).And(cf.ByName(InstalledApp.WindowTitle)))) is not null,
                TimeSpan.FromSeconds(5));
            if (!found || icon is null)
            {
                InstalledApp.Log("notification area: the District AI icon was not found by its tooltip, on the taskbar or in the flyout");
                Keyboard.Type(VirtualKeyShort.ESCAPE);
                return;
            }
            InstalledApp.Log($"notification area: icon found (class {icon.ClassName}, parent window {icon.Parent?.ClassName}); right-clicking it");
            icon.RightClick();
            var menu = Wait.Until(() => Menu() is not null, TimeSpan.FromSeconds(5));
            var items = menu ? string.Join(", ", Menu()?.FindAllDescendants(cf => cf.ByControlType(ControlType.MenuItem)).Select(item => item.Name) ?? []) : string.Empty;
            InstalledApp.Log(menu ? $"notification area: the right click opened the app's menu: [{items}]" : "notification area: the right click opened no menu within 5 s");
            Keyboard.Type(VirtualKeyShort.ESCAPE);
            var closed = Wait.Until(() => Menu() is null, TimeSpan.FromSeconds(5));
            Keyboard.Type(VirtualKeyShort.ESCAPE);
            InstalledApp.Log($"notification area: menu {(closed ? "closed" : "STILL OPEN")} after Escape");
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            InstalledApp.Log($"notification area: driving it failed: {error.GetType().Name}: {error.Message}");
        }
    }

    /// <summary>
    /// Skips off Windows. On Windows the package must be installed: the tests
    /// skip without it, unless DISTRICTAI_UI_TESTS_REQUIRED is 1 (CI sets it),
    /// when they fail.
    /// </summary>
    private static void RequireInstalledApp()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The UI tests run on Windows only.");
        if (Native.IsPackageInstalled(InstalledApp.Family))
        {
            return;
        }
        var why = $"The package family {InstalledApp.Family} is not installed for this user (CONTRIBUTING.md, \"UI smoke tests\").";
        if (Environment.GetEnvironmentVariable("DISTRICTAI_UI_TESTS_REQUIRED") == "1")
        {
            Assert.Fail(why);
        }
        Assert.Skip(why);
    }
}
