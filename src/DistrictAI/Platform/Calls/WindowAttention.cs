using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace DistrictAI.Platform.Calls;

/// <summary>
/// Brings the window to the user when the core asks (a call ringing here):
/// shown, even from the tray, restored if minimised, and in front if Windows
/// allows it. If it does not, the taskbar button flashes until the window is
/// brought forward.
/// </summary>
/// <remarks>
/// Windows lets a process take the foreground only in narrow cases (it is the
/// foreground process already, or the user just interacted with it), so a ring
/// that arrives while the user works in another app is usually refused. That is
/// by design, and the flash is the answer Windows gives instead.
/// </remarks>
internal sealed class WindowAttention(Window window)
{
    /// <summary>Shows and activates the window, flashing it if it cannot come to the front.</summary>
    public void Present()
    {
        var appWindow = window.AppWindow;
        appWindow.Show();
        if (appWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
        {
            presenter.Restore();
        }
        window.Activate();
        var hwnd = new HWND(WinRT.Interop.WindowNative.GetWindowHandle(window));
        if (PInvoke.SetForegroundWindow(hwnd) && PInvoke.GetForegroundWindow() == hwnd)
        {
            return;
        }
        // FLASHW_TIMERNOFG flashes until the window comes to the foreground, so
        // the flash ends by itself once the user brings it forward.
        var flash = new FLASHWINFO
        {
            cbSize = (uint)Marshal.SizeOf<FLASHWINFO>(),
            hwnd = hwnd,
            dwFlags = FLASHWINFO_FLAGS.FLASHW_ALL | FLASHWINFO_FLAGS.FLASHW_TIMERNOFG,
            uCount = 0,
            dwTimeout = 0,
        };
        _ = PInvoke.FlashWindowEx(in flash);
    }
}
