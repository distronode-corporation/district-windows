// SPDX-FileCopyrightText: Distronode Corporation
//
// SPDX-License-Identifier: Apache-2.0

namespace DistrictAI.ViewModels.AccountSecurity;

/// <summary>The clipboard, for a secret: the app fills it with the Windows clipboard.</summary>
public interface ISecretClipboard
{
    /// <summary>
    /// Puts <paramref name="secret"/> on the clipboard, kept out of its history
    /// and of its sync to other devices. False when Windows refused it.
    /// </summary>
    bool Copy(string secret);

    /// <summary>Empties the clipboard if it still holds <paramref name="secret"/>, and only then.</summary>
    void ClearIfStill(string secret);
}

/// <summary>
/// Copies of a secret shown once (the authenticator key, the recovery codes):
/// each goes to the clipboard only when its button is pressed, and is taken
/// off again after <see cref="Lifetime"/>, unless something else was copied
/// since. A later copy, even of the same text, starts the time again: the
/// earlier copy's timer is stopped, so it can never empty the clipboard under
/// the later copy.
/// </summary>
public sealed class SecretCopies
{
    /// <summary>How long a secret stays on the clipboard.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);

    private readonly ISecretClipboard _clipboard;
    private readonly TimeProvider _time;
    private ITimer? _timer;
    private long _copies;

    /// <summary>Copies to <paramref name="clipboard"/>, timed by <paramref name="time"/>.</summary>
    public SecretCopies(ISecretClipboard clipboard, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(clipboard);
        ArgumentNullException.ThrowIfNull(time);
        _clipboard = clipboard;
        _time = time;
    }

    /// <summary>Copies <paramref name="secret"/>, for <see cref="Lifetime"/>. False when Windows refused it.</summary>
    public bool Copy(string secret)
    {
        ArgumentNullException.ThrowIfNull(secret);
        if (!_clipboard.Copy(secret))
        {
            return false;
        }
        var copy = ++_copies;
        _timer?.Dispose();
        // The clipboard is the UI thread's: the timer's tick goes back to the
        // thread that copied.
        var ui = SynchronizationContext.Current;
        _timer = _time.CreateTimer(
            _ =>
            {
                if (ui is null)
                {
                    Expire(copy, secret);
                }
                else
                {
                    ui.Post(_ => Expire(copy, secret), null);
                }
            },
            null,
            Lifetime,
            Timeout.InfiniteTimeSpan);
        return true;
    }

    private void Expire(long copy, string secret)
    {
        // Only the latest copy, and only while the clipboard still holds it.
        if (copy == _copies)
        {
            _clipboard.ClearIfStill(secret);
        }
    }
}
