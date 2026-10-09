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
}
