using System.Runtime.InteropServices;
using Windows.ApplicationModel.DataTransfer;

namespace DistrictAI.Platform;

/// <summary>
/// Copies text to the Windows clipboard: a guest link, an id, a secret shown
/// once. Call from the UI thread.
/// </summary>
internal static class ClipboardService
{
    /// <summary>
    /// Puts <paramref name="text"/> on the clipboard, kept there after the app
    /// quits. False when Windows refused it, which happens while another app
    /// holds the clipboard open; the caller says the copy did not happen.
    /// </summary>
    public static bool CopyText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
        package.SetText(text);
        try
        {
            Clipboard.SetContent(package);
            Clipboard.Flush();
            return true;
        }
        catch (COMException)
        {
            return false;
        }
    }

    /// <summary>
    /// Puts a secret (an API key, a webhook's signing secret) on the
    /// clipboard, kept out of the clipboard's history and of its sync to the
    /// member's other devices, and not kept after the app quits. False when
    /// Windows refused it.
    /// </summary>
    public static bool CopySecret(string secret)
    {
        ArgumentNullException.ThrowIfNull(secret);
        var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
        package.SetText(secret);
        var options = new ClipboardContentOptions { IsAllowedInHistory = false, IsRoamable = false };
        try
        {
            return Clipboard.SetContentWithOptions(package, options);
        }
        catch (COMException)
        {
            return false;
        }
    }
}
