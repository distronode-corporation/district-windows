// SPDX-FileCopyrightText: Distronode Corporation
//
// SPDX-License-Identifier: Apache-2.0

using System.Runtime.InteropServices;
using DistrictAI.ViewModels.AccountSecurity;
using Windows.ApplicationModel.DataTransfer;

namespace DistrictAI.Platform;

/// <summary>
/// The Windows clipboard, for a secret shown once (the authenticator key, the
/// recovery codes): copied out of the clipboard's history and its sync to the
/// member's other devices (<see cref="ClipboardService.CopySecret"/>), and
/// emptied again only while it still holds that copy. Call from the UI thread;
/// <see cref="SecretCopies"/> brings its timer back to it.
/// </summary>
/// <remarks>
/// "Still holds that copy" is Windows' clipboard sequence number, read right
/// after the copy and again just before the clear, with nothing awaited in
/// between: any change to the clipboard since, by any app, moves it, and the
/// clipboard is then left alone. Reading the text back instead would await,
/// and another app's copy landing during that wait would be erased.
/// </remarks>
internal sealed partial class WindowsSecretClipboard : ISecretClipboard
{
    private string? _copied;
    private uint _sequence;

    public bool Copy(string secret)
    {
        if (!ClipboardService.CopySecret(secret))
        {
            return false;
        }
        _copied = secret;
        _sequence = GetClipboardSequenceNumber();
        return true;
    }

    public void ClearIfStill(string secret)
    {
        if (!string.Equals(secret, _copied, StringComparison.Ordinal))
        {
            return;
        }
        _copied = null;
        // Zero: the sequence could not be read, so ownership is not known.
        if (_sequence == 0 || GetClipboardSequenceNumber() != _sequence)
        {
            return;
        }
        try
        {
            Clipboard.Clear();
        }
        catch (Exception error) when (error is COMException or UnauthorizedAccessException)
        {
            // Another app holds the clipboard open: the copy stays, as it
            // would after any refused clipboard write.
        }
    }

    [LibraryImport("user32.dll")]
    private static partial uint GetClipboardSequenceNumber();
}
