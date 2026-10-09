using System.Runtime.InteropServices;
using Windows.ApplicationModel.DataTransfer;

namespace DistrictAI.Platform;

/// <summary>
/// Copies text to the Windows clipboard: a guest link, an id. Call from the UI
/// thread.
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
}
