using System.Diagnostics;
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
        Application.Start(started =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            // The application object lives as long as Application.Start runs.
            _ = new App(activation);
        });
        return 0;
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
