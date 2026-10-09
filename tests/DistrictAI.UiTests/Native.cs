using System.Runtime.InteropServices;

namespace DistrictAI.UiTests;

/// <summary>The few Win32 calls the tests make that UI Automation does not cover.</summary>
internal static partial class Native
{
    /// <summary>WM_NULL: a message every window answers, to see that it still does.</summary>
    public const uint WmNull = 0x0000;

    /// <summary>WM_CONTEXTMENU, which a version 4 notification-area icon receives on a right click.</summary>
    public const uint WmContextMenu = 0x007B;

    /// <summary>WM_APP.</summary>
    public const uint WmApp = 0x8000;

    private const uint SmtoAbortIfHung = 0x0002;
    private const int ErrorSuccess = 0;
    private const int ErrorInsufficientBuffer = 122;

    /// <summary>Whether <paramref name="window"/> has the visible style.</summary>
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindowVisible(nint window);

    /// <summary>Puts a message in <paramref name="window"/>'s queue.</summary>
    [LibraryImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PostMessage(nint window, uint message, nuint wParam, nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "SendMessageTimeoutW", SetLastError = true)]
    private static partial nint SendMessageTimeout(nint window, uint message, nuint wParam, nint lParam, uint flags, uint timeout, out nuint result);

    /// <summary>Moves and sizes <paramref name="window"/> (outer size, in pixels).</summary>
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool MoveWindow(nint window, int x, int y, int width, int height, [MarshalAs(UnmanagedType.Bool)] bool repaint);

    /// <summary>Draws <paramref name="window"/> into <paramref name="dc"/>; flag 2 (PW_RENDERFULLCONTENT) includes DirectComposition content, which WinUI is.</summary>
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PrintWindow(nint window, nint dc, uint flags);

    /// <summary>Brings <paramref name="window"/> to the foreground.</summary>
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetForegroundWindow(nint window);

    /// <summary>A Win32 RECT.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        /// <summary>The left edge.</summary>
        public int Left;
        /// <summary>The top edge.</summary>
        public int Top;
        /// <summary>The right edge.</summary>
        public int Right;
        /// <summary>The bottom edge.</summary>
        public int Bottom;

        /// <summary>The width.</summary>
        public readonly int Width => Right - Left;

        /// <summary>The height.</summary>
        public readonly int Height => Bottom - Top;
    }

    /// <summary>The window's outer rectangle, invisible resize borders included, in screen pixels.</summary>
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetWindowRect(nint window, out Rect rect);

    /// <summary>The window's client area, from (0, 0).</summary>
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetClientRect(nint window, out Rect rect);

    /// <summary>Where the client area's (0, 0) is on the screen.</summary>
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ClientToScreen(nint window, ref Point point);

    /// <summary>A Win32 POINT.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Point
    {
        /// <summary>X.</summary>
        public int X;
        /// <summary>Y.</summary>
        public int Y;
    }

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static partial nint GetWindowLongPtr(nint window, int index);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static partial nint SetWindowLongPtr(nint window, int index, nint value);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);

    /// <summary>
    /// Takes the title bar and the sizing frame off <paramref name="window"/>
    /// and makes it <paramref name="width"/> by <paramref name="height"/> at
    /// (0, 0): its client area is then the whole window, which fits a display
    /// of that size.
    /// </summary>
    public static void Frameless(nint window, int width, int height)
    {
        const int gwlStyle = -16;
        const nint wsCaption = 0x00C00000;
        const nint wsThickFrame = 0x00040000;
        const uint swpNoZOrder = 0x0004;
        const uint swpFrameChanged = 0x0020;
        var style = GetWindowLongPtr(window, gwlStyle);
        _ = SetWindowLongPtr(window, gwlStyle, style & ~(wsCaption | wsThickFrame));
        _ = SetWindowPos(window, 0, 0, 0, width, height, swpNoZOrder | swpFrameChanged);
    }

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmGetWindowAttribute(nint window, uint attribute, out Rect value, uint size);

    /// <summary>The part of the window that is drawn (DWMWA_EXTENDED_FRAME_BOUNDS): its title bar and frame, without the invisible resize borders.</summary>
    public static Rect VisibleBounds(nint window)
    {
        Marshal.ThrowExceptionForHR(DwmGetWindowAttribute(window, 9, out var rect, (uint)Marshal.SizeOf<Rect>()));
        return rect;
    }

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int GetPackagesByPackageFamily(string packageFamilyName, ref uint count, nint packageFullNames, ref uint bufferLength, nint buffer);

    /// <summary>
    /// Whether <paramref name="window"/>'s thread answers a message within
    /// <paramref name="timeout"/>: false for a hung window, or one that is gone.
    /// </summary>
    public static bool Responds(nint window, TimeSpan timeout) =>
        SendMessageTimeout(window, WmNull, 0, 0, SmtoAbortIfHung, (uint)timeout.TotalMilliseconds, out _) != 0;

    /// <summary>Whether a package of <paramref name="family"/> is installed for this user.</summary>
    public static bool IsPackageInstalled(string family)
    {
        uint count = 0;
        uint length = 0;
        var error = GetPackagesByPackageFamily(family, ref count, 0, ref length, 0);
        return error is ErrorSuccess or ErrorInsufficientBuffer && count > 0;
    }

    private const uint CfUnicodeText = 13;

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenClipboard(nint owner);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseClipboard();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EmptyClipboard();

    [LibraryImport("user32.dll")]
    private static partial nint GetClipboardData(uint format);

    [LibraryImport("kernel32.dll")]
    private static partial nint GlobalLock(nint memory);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalUnlock(nint memory);

    /// <summary>
    /// The clipboard's text (CF_UNICODETEXT), or null when it holds none, and
    /// the clipboard emptied after: what the app copied (a guest link) is
    /// read once and not left behind.
    /// </summary>
    public static string? TakeClipboardText()
    {
        var opened = false;
        for (var attempt = 0; attempt < 20 && !opened; attempt++)
        {
            opened = OpenClipboard(0);
            if (!opened)
            {
                Thread.Sleep(50);
            }
        }
        if (!opened)
        {
            return null;
        }
        try
        {
            var data = GetClipboardData(CfUnicodeText);
            string? text = null;
            if (data != 0)
            {
                var pointer = GlobalLock(data);
                if (pointer != 0)
                {
                    try
                    {
                        text = Marshal.PtrToStringUni(pointer);
                    }
                    finally
                    {
                        _ = GlobalUnlock(data);
                    }
                }
            }
            _ = EmptyClipboard();
            return text;
        }
        finally
        {
            _ = CloseClipboard();
        }
    }

    [LibraryImport("user32.dll", EntryPoint = "SendMessageTimeoutW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint SendMessageTimeoutText(nint window, uint message, nuint wParam, string lParam, uint flags, uint timeout, out nuint result);

    /// <summary>
    /// Tells every top-level window that a setting changed
    /// (WM_SETTINGCHANGE to HWND_BROADCAST), naming the area, as Settings
    /// does: "ImmersiveColorSet" after the light or dark choice changes.
    /// </summary>
    public static void BroadcastSettingChange(string area)
    {
        const nint hwndBroadcast = 0xffff;
        const uint wmSettingChange = 0x001A;
        _ = SendMessageTimeoutText(hwndBroadcast, wmSettingChange, 0, area, SmtoAbortIfHung, 5000, out _);
    }
}
