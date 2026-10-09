using System.Diagnostics;
using System.Text;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using Xunit;

namespace DistrictAI.UiTests;

/// <summary>
/// The installed District AI package, started by its application user model
/// ID as the Start menu starts it, and read through UI Automation. Every wait
/// polls with a deadline, and a wait that runs out says what UI Automation
/// held at that moment.
/// </summary>
internal sealed class InstalledApp : IDisposable
{
    /// <summary>
    /// The Store flavour's package family. The CI test package has the same
    /// one: its throwaway certificate's subject is the manifest's publisher.
    /// </summary>
    public const string DefaultFamily = "DistronodeCorporation.42101E4C5A5B6_kp9h3zhgekf9c";

    /// <summary>The executable's process name.</summary>
    public const string ProcessName = "DistrictAI";

    /// <summary>The window's title (MainWindow.xaml).</summary>
    public const string WindowTitle = "District AI";

    /// <summary>The package family under test: DISTRICTAI_PACKAGE_FAMILY, or <see cref="DefaultFamily"/>.</summary>
    public static string Family =>
        Environment.GetEnvironmentVariable("DISTRICTAI_PACKAGE_FAMILY") is { Length: > 0 } family ? family : DefaultFamily;

    /// <summary>The application user model ID of the package's one application.</summary>
    public static string Aumid => $"{Family}!App";

    private readonly UIA3Automation _automation = new();

    private InstalledApp(int processId)
    {
        ProcessId = processId;
    }

    /// <summary>The first instance's process.</summary>
    public int ProcessId { get; }

    /// <summary>The automation the tests read the UI with.</summary>
    public UIA3Automation Automation => _automation;

    /// <summary>
    /// Ends any running copy, then starts the package by its application user
    /// model ID (IApplicationActivationManager, as the shell does).
    /// </summary>
    public static InstalledApp Launch()
    {
        StopAll();
        using var app = Application.LaunchStoreApp(Aumid);
        Log($"launched {Aumid}: process {app.ProcessId}");
        return new InstalledApp(app.ProcessId);
    }

    /// <summary>Starts the package again while it runs: a second process, which hands its launch to the first.</summary>
    public static void LaunchAgain()
    {
        using var app = Application.LaunchStoreApp(Aumid);
        Log($"launched {Aumid} again: process {app.ProcessId}");
    }

    /// <summary>Opens <paramref name="uri"/> as a browser hands a link to Windows.</summary>
    public static void OpenLink(string uri)
    {
        using var opened = Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
        Log($"opened {uri}");
    }

    /// <summary>The ids of every District AI process now running.</summary>
    public static int[] RunningProcessIds()
    {
        var processes = Process.GetProcessesByName(ProcessName);
        try
        {
            return [.. processes.Select(process => process.Id).Order()];
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }

    /// <summary>Whether the first instance's process is still running.</summary>
    public bool IsRunning => RunningProcessIds().Contains(ProcessId);

    /// <summary>The app's top-level window, waited for while it is not on the screen.</summary>
    public Window MainWindow(TimeSpan timeout) =>
        Wait.For(
            () => _automation.GetDesktop()
                .FindFirstChild(cf => cf.ByProcessId(ProcessId).And(cf.ByControlType(ControlType.Window)).And(cf.ByName(WindowTitle)))
                ?.AsWindow(),
            timeout,
            "the District AI window",
            Describe);

    /// <summary>
    /// The control in the app's window that UI Automation names
    /// <paramref name="name"/>, of <paramref name="type"/> (any, when null),
    /// on screen; waited for.
    /// </summary>
    public AutomationElement Find(ControlType? type, string name, TimeSpan timeout) =>
        Wait.For(
            () => TryFind(type, name),
            timeout,
            type is null ? $"an element named \"{name}\"" : $"a {type} named \"{name}\"",
            Describe);

    /// <summary>What <see cref="Find"/> finds, now, or null.</summary>
    public AutomationElement? TryFind(ControlType? type, string name) =>
        _automation.GetDesktop()
            .FindFirstChild(cf => cf.ByProcessId(ProcessId).And(cf.ByControlType(ControlType.Window)))
            ?.FindFirstDescendant(cf => type is { } wanted ? cf.ByControlType(wanted).And(cf.ByName(name)) : cf.ByName(name))
            is { IsOffscreen: false } found ? found : null;

    /// <summary>
    /// What UI Automation holds now: the desktop's top-level windows, and the
    /// app's own windows with every element under them.
    /// </summary>
    public string Describe()
    {
        var text = new StringBuilder();
        text.AppendLine(FormattableString.Invariant($"District AI processes: [{string.Join(", ", RunningProcessIds())}], first instance {ProcessId}"));
        try
        {
            var desktop = _automation.GetDesktop();
            text.AppendLine("Top-level windows:");
            foreach (var child in desktop.FindAllChildren())
            {
                text.Append("  ").AppendLine(Line(child));
            }
            foreach (var window in desktop.FindAllChildren(cf => cf.ByProcessId(ProcessId)))
            {
                text.AppendLine(FormattableString.Invariant($"Tree of {Line(window)}:"));
                Tree(window, 1, text, new Budget(500));
            }
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            text.AppendLine(FormattableString.Invariant($"(reading UI Automation failed: {error.GetType().Name}: {error.Message})"));
        }
        return text.ToString();
    }

    /// <summary>Ends the app if a test left it running.</summary>
    public void Dispose()
    {
        StopAll();
        _automation.Dispose();
    }

    /// <summary>Ends every District AI process, and waits until none is left.</summary>
    public static void StopAll()
    {
        foreach (var id in RunningProcessIds())
        {
            try
            {
                using var process = Process.GetProcessById(id);
                process.Kill();
            }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // Already gone.
            }
        }
        _ = Wait.Until(() => RunningProcessIds().Length == 0, TimeSpan.FromSeconds(15));
    }

    /// <summary>A line of the test's output.</summary>
    public static void Log(string line) =>
        TestContext.Current.TestOutputHelper?.WriteLine(FormattableString.Invariant($"[{DateTime.UtcNow:HH:mm:ss.fff}] {line}"));

    private void Tree(AutomationElement parent, int depth, StringBuilder text, Budget budget)
    {
        var walker = _automation.TreeWalkerFactory.GetControlViewWalker();
        for (var child = walker.GetFirstChild(parent); child is not null; child = walker.GetNextSibling(child))
        {
            if (!budget.Take())
            {
                text.Append(' ', depth * 2).AppendLine("(more elements, not listed)");
                return;
            }
            text.Append(' ', depth * 2).AppendLine(Line(child));
            if (depth < 25)
            {
                Tree(child, depth + 1, text, budget);
            }
        }
    }

    private static string Line(AutomationElement element)
    {
        string Read(Func<string> read)
        {
            try
            {
                return read();
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                return "?";
            }
        }
        return FormattableString.Invariant(
            $"{Read(() => element.ControlType.ToString())} \"{Read(() => element.Name)}\" id={Read(() => element.AutomationId)} class={Read(() => element.ClassName)} pid={Read(() => element.Properties.ProcessId.ValueOrDefault.ToString(System.Globalization.CultureInfo.InvariantCulture))} offscreen={Read(() => element.IsOffscreen.ToString())}");
    }

    private sealed class Budget(int left)
    {
        public bool Take() => left-- > 0;
    }
}
