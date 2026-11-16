using System.Runtime.InteropServices;
using DistrictAI.ViewModels.AccountSecurity;
using Windows.ApplicationModel.DataTransfer;

namespace DistrictAI.Platform;

/// <summary>
/// The Windows clipboard, for a secret shown once (the authenticator key, the
/// recovery codes): copied out of the clipboard's history and its sync to the
/// member's other devices (<see cref="ClipboardService.CopySecret"/>), and
/// emptied again only while it still holds that secret. Call from the UI
/// thread; <see cref="SecretCopies"/> brings its timer back to it.
/// </summary>
internal sealed class WindowsSecretClipboard : ISecretClipboard
{
    public bool Copy(string secret) => ClipboardService.CopySecret(secret);

    public async void ClearIfStill(string secret)
    {
        try
        {
            var content = Clipboard.GetContent();
            if (!content.Contains(StandardDataFormats.Text))
            {
                return;
            }
            var text = await content.GetTextAsync();
            if (string.Equals(text, secret, StringComparison.Ordinal))
            {
                Clipboard.Clear();
            }
        }
        catch (COMException)
        {
            // Another app holds the clipboard open: what it holds is not
            // known to be ours, so it is left alone.
        }
    }
}
