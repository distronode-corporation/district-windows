using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace DistrictAI.Platform;

/// <summary>One item of the tray icon's menu.</summary>
/// <param name="Label">What the menu shows.</param>
/// <param name="Invoke">What choosing it does, run on the window's queue.</param>
internal sealed record TrayMenuItem(string Label, Action Invoke);

/// <summary>
/// The icon in the notification area, over <c>Shell_NotifyIcon</c>, and the
/// window's visibility as Windows sees it. Both come from one subclass of the
/// window's own procedure, so the icon lives exactly as long as the window and
/// needs no window of its own.
/// </summary>
/// <remarks>
/// Every callback (<see cref="Selected"/>, <see cref="VisibilityChanged"/>, a
/// menu item) runs on <c>queue</c> after the window message that caused it has
/// returned, never inside the window procedure.
/// </remarks>
internal sealed class TrayIcon : IDisposable
{
    /// <summary>The message the icon's clicks arrive as.</summary>
    private const uint CallbackMessage = PInvoke.WM_APP + 1;
    // shellapi.h's NIN_SELECT (WM_USER) and NIN_KEYSELECT (NIN_SELECT | NINF_KEY),
    // which the metadata CsWin32 reads does not carry.
    private const uint NinSelect = 0x0400;
    private const uint NinKeySelect = NinSelect | 0x1;
    private const uint IconId = 1;
    private const nuint SubclassId = 0x4441;
    private const int SizeMinimized = 1;

    private readonly HWND _window;
    private readonly DispatcherQueue _queue;
    private readonly string _iconPath;
    private readonly string _tooltip;
    private readonly Func<IReadOnlyList<TrayMenuItem?>> _menu;
    // Held for as long as the subclass is installed: the native side keeps
    // only a function pointer to it.
    private readonly SUBCLASSPROC _procedure;
    // Explorer broadcasts this when it starts again (after a crash, say), and
    // every icon must be added again.
    private readonly uint _taskbarCreated;
    private HICON _icon;
    private bool? _visible;
    private bool _disposed;

    /// <summary>
    /// Adds the icon for <paramref name="window"/>, drawn from the .ico at
    /// <paramref name="iconPath"/>, with <paramref name="tooltip"/> on hover. The
    /// menu is <paramref name="menu"/>'s answer at the moment it opens; a null
    /// item is a separator.
    /// </summary>
    public TrayIcon(nint window, DispatcherQueue queue, string iconPath, string tooltip, Func<IReadOnlyList<TrayMenuItem?>> menu)
    {
        _window = new HWND(window);
        _queue = queue;
        _iconPath = iconPath;
        _tooltip = tooltip;
        _menu = menu;
        _procedure = Procedure;
        _taskbarCreated = PInvoke.RegisterWindowMessage("TaskbarCreated");
        if (!PInvoke.SetWindowSubclass(_window, _procedure, SubclassId, 0))
        {
            throw new InvalidOperationException("Could not subclass the window for the tray icon.");
        }
        Add();
        ReportVisibility();
    }

    /// <summary>The icon was clicked once with the left button, or chosen with the keyboard.</summary>
    public event EventHandler? Selected;

    /// <summary>
    /// The window became visible or stopped being visible: shown, hidden,
    /// minimised or restored. Raised once with the state when the icon is made.
    /// </summary>
    public event EventHandler<bool>? VisibilityChanged;

    /// <summary>Shows <paramref name="text"/> as a notification from the icon.</summary>
    public void ShowTip(string title, string text)
    {
        var data = Data(NOTIFY_ICON_DATA_FLAGS.NIF_INFO);
        data.szInfoTitle = title;
        data.szInfo = text;
        data.dwInfoFlags = NOTIFY_ICON_INFOTIP_FLAGS.NIIF_NONE;
        _ = PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_MODIFY, in data);
    }

    /// <summary>Removes the icon and the subclass.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        var data = Data(0);
        _ = PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_DELETE, in data);
        _ = PInvoke.RemoveWindowSubclass(_window, _procedure, SubclassId);
        if (!_icon.IsNull)
        {
            _ = PInvoke.DestroyIcon(_icon);
            _icon = default;
        }
    }

    private void Add()
    {
        if (_icon.IsNull)
        {
            _icon = LoadIcon(_iconPath);
        }
        var data = Data(NOTIFY_ICON_DATA_FLAGS.NIF_MESSAGE | NOTIFY_ICON_DATA_FLAGS.NIF_ICON | NOTIFY_ICON_DATA_FLAGS.NIF_TIP | NOTIFY_ICON_DATA_FLAGS.NIF_SHOWTIP);
        data.uCallbackMessage = CallbackMessage;
        data.hIcon = _icon;
        data.szTip = _tooltip;
        // A previous instance's icon can linger until the mouse passes over it;
        // removing first makes the add succeed.
        _ = PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_DELETE, in data);
        _ = PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_ADD, in data);
        // Version 4: a single left click is NIN_SELECT, the menu key and the
        // right click are WM_CONTEXTMENU, each with where to put the menu.
        data.Anonymous.uVersion = PInvoke.NOTIFYICON_VERSION_4;
        _ = PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_SETVERSION, in data);
    }

    private NOTIFYICONDATAW Data(NOTIFY_ICON_DATA_FLAGS flags) => new()
    {
        cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
        hWnd = _window,
        uID = IconId,
        uFlags = flags,
    };

    /// <summary>The small-icon size of the .ico at <paramref name="path"/>, or no icon.</summary>
    private static unsafe HICON LoadIcon(string path)
    {
        var size = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CXSMICON);
        fixed (char* name = path)
        {
            // The raw overload: the friendly one wraps an icon in a handle that
            // would close it as a file.
            var handle = PInvoke.LoadImage(default, name, GDI_IMAGE_TYPE.IMAGE_ICON, size, size, IMAGE_FLAGS.LR_LOADFROMFILE);
            return new HICON(handle.Value);
        }
    }

    private LRESULT Procedure(HWND window, uint message, WPARAM wParam, LPARAM lParam, nuint id, nuint data)
    {
        if (message == CallbackMessage)
        {
            // Version 4: the event in the low word of lParam, the anchor point
            // (screen coordinates) in wParam.
            var notification = (uint)(lParam.Value & 0xFFFF);
            var x = (short)(wParam.Value & 0xFFFF);
            var y = (short)((wParam.Value >> 16) & 0xFFFF);
            switch (notification)
            {
                case NinSelect:
                case NinKeySelect:
                    _ = _queue.TryEnqueue(() => Selected?.Invoke(this, EventArgs.Empty));
                    break;
                case PInvoke.WM_CONTEXTMENU:
                    ShowMenu(x, y);
                    break;
                default:
                    break;
            }
            return new LRESULT(0);
        }
        if (message == _taskbarCreated)
        {
            Add();
        }
        var result = PInvoke.DefSubclassProc(window, message, wParam, lParam);
        if (message == PInvoke.WM_WINDOWPOSCHANGED)
        {
            // Every show, hide, minimise and restore ends here, whichever API
            // made it (WM_SHOWWINDOW is skipped for SW_SHOWNORMAL, WM_SIZE for
            // a hide).
            ReportVisibility();
        }
        return result;
    }

    private void ReportVisibility()
    {
        bool visible = PInvoke.IsWindowVisible(_window) && !PInvoke.IsIconic(_window);
        if (_visible == visible)
        {
            return;
        }
        _visible = visible;
        _ = _queue.TryEnqueue(() => VisibilityChanged?.Invoke(this, visible));
    }

    private void ShowMenu(int x, int y)
    {
        var items = _menu();
        using var menu = PInvoke.CreatePopupMenu_SafeHandle();
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            _ = item is null
                ? PInvoke.AppendMenu(menu, MENU_ITEM_FLAGS.MF_SEPARATOR, 0, null)
                : PInvoke.AppendMenu(menu, MENU_ITEM_FLAGS.MF_STRING, (nuint)(i + 1), item.Label);
        }
        // Without the foreground, the menu would not close when the user
        // clicks elsewhere.
        _ = PInvoke.SetForegroundWindow(_window);
        // TPM_RETURNCMD: the result is the chosen item's id, 0 for none.
        var chosen = PInvoke.TrackPopupMenuEx(
            menu,
            (uint)(TRACK_POPUP_MENU_FLAGS.TPM_RETURNCMD | TRACK_POPUP_MENU_FLAGS.TPM_NONOTIFY | TRACK_POPUP_MENU_FLAGS.TPM_RIGHTBUTTON),
            x,
            y,
            _window,
            null).Value;
        _ = PInvoke.PostMessage(_window, PInvoke.WM_NULL, default, default);
        if (chosen > 0 && items[chosen - 1] is { } picked)
        {
            _ = _queue.TryEnqueue(() => picked.Invoke());
        }
    }
}
