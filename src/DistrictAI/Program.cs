using System.Diagnostics;
using DistrictAI.Core;
using DistrictAI.L10n;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace DistrictAI;

/// <summary>
/// The entry point, before any XAML: one process per user. A second launch (a
/// <c>districtai://auth</c> link from the browser, a click on the Start menu
/// tile, a toast's button) hands its activation to the first and exits, so the
/// first keeps its in-memory sign-in attempt (the PKCE verifier) and its window.
/// </summary>
public static class Program
{
    /// <summary>The key the first instance registers under.</summary>
    public const string InstanceKey = "main";

    [STAThread]
    private static int Main()
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
        var main = AppInstance.FindOrRegisterForKey(InstanceKey);
        if (!main.IsCurrent)
        {
            RedirectTo(main, activation);
            return 0;
        }
        // The language of every string the app writes, before any XAML loads:
        // Canadian French when this is a French build (every release from 3.0)
        // and the user's Windows languages pick French, else English. Windows'
        // own resources follow it.
        S.Current = Languages.French ? S.Locale.FrCa : S.Locale.En;
        Languages.ApplyToWindows();
        TraceToFileForTheWalk();
        Application.Start(started =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            // Every continuation on the UI thread, an async void method's
            // exception among them, is reported before it ends the app.
            SynchronizationContext.SetSynchronizationContext(new ReportingSynchronizationContext(context));
            // The application object lives as long as Application.Start runs.
            _ = new App(activation);
        });
        return 0;
    }

    /// <summary>The file a run started with a scripted scene keeps the trace in.</summary>
    internal const string WalkTraceFile = "district-trace.log";

    /// <summary>
    /// A run started with a scripted scene (the UI walk's) keeps the app's
    /// trace in <see cref="WalkTraceFile"/> in the package's temporary folder,
    /// flushed as it is written, so what <see cref="UiFaults"/> reports is on
    /// disk before a crash ends the app; the walk prints it when the app
    /// crashed. Any other run writes no file. Never in the way of starting.
    /// </summary>
    private static void TraceToFileForTheWalk()
    {
        if (!Environment.GetCommandLineArgs().Any(arg => arg.StartsWith("--district-scripted-scene=", StringComparison.Ordinal)))
        {
            return;
        }
        try
        {
            var path = Path.Combine(Windows.Storage.ApplicationData.Current.TemporaryFolder.Path, WalkTraceFile);
            Trace.Listeners.Add(new TextWriterTraceListener(path));
            Trace.AutoFlush = true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            Debug.WriteLine($"District AI: the walk's trace file was not opened: {error.Message}");
        }
    }

    /// <summary>
    /// Hands <paramref name="activation"/> to the first instance, waits until it
    /// has it, and brings its window forward.
    /// </summary>
    private static void RedirectTo(AppInstance main, AppActivationArguments activation)
    {
        using var redirected = new ManualResetEvent(initialState: false);
        _ = Task.Run(async () =>
        {
            try
            {
                await main.RedirectActivationToAsync(activation);
            }
            finally
            {
                redirected.Set();
            }
        });
        // This is an STA thread, and the redirection is delivered through COM:
        // waiting without pumping COM messages could wait forever.
        ReadOnlySpan<HANDLE> handles = [new HANDLE(redirected.SafeWaitHandle.DangerousGetHandle())];
        PInvoke.CoWaitForMultipleObjects(default, uint.MaxValue, handles, out _).ThrowOnFailure();
        using var first = Process.GetProcessById((int)main.ProcessId);
        _ = PInvoke.SetForegroundWindow(new HWND(first.MainWindowHandle));
    }
}
