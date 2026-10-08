using System.Runtime.InteropServices;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace DistrictAI.Platform.Calls;

/// <summary>
/// The ringtone, looping while a call rings here: the core says when it starts
/// and when it stops.
/// </summary>
/// <remarks>
/// The sound is <c>Assets/Sounds/ringtone.wav</c>, two seconds written by
/// <c>scripts/make-ringtone.py</c>. Playing it never fails loudly: on a machine
/// with no audio device, or with playback refused, the call still rings in the
/// window and as a toast, so every failure here is swallowed and the player
/// stays usable for the next ring. <see cref="MediaPlayer"/> opens and plays
/// asynchronously, so neither <see cref="Start"/> nor <see cref="Stop"/> waits
/// on the audio stack.
/// </remarks>
internal sealed class RingtonePlayer : IDisposable
{
    private static readonly Uri Ringtone = new("ms-appx:///Assets/Sounds/ringtone.wav");

    private MediaPlayer? _player;
    private bool _disposed;

    /// <summary>
    /// Plays the ringtone from its start, looping, until <see cref="Stop"/>.
    /// Nothing changes if it is already playing.
    /// </summary>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        try
        {
            // Stop rewinds, so a new ring starts at the first strike.
            (_player ??= Create()).Play();
        }
        catch (Exception error) when (error is COMException or InvalidOperationException or UnauthorizedAccessException)
        {
            // No audio device, or no media stack: the ring is silent, not broken.
            Release();
        }
    }

    /// <summary>Stops the ringtone. Nothing happens if it is not playing.</summary>
    public void Stop()
    {
        if (_player is null)
        {
            return;
        }
        try
        {
            _player.Pause();
            _player.PlaybackSession.Position = TimeSpan.Zero;
        }
        catch (Exception error) when (error is COMException or InvalidOperationException or UnauthorizedAccessException)
        {
            // The player broke while ringing; a new one is made for the next ring.
            Release();
        }
    }

    private static MediaPlayer Create()
    {
        var player = new MediaPlayer
        {
            AudioCategory = MediaPlayerAudioCategory.Alerts,
            IsLoopingEnabled = true,
            AutoPlay = false,
            Source = MediaSource.CreateFromUri(Ringtone),
        };
        // The system media controls (the volume flyout's player) are for media
        // the user chose to play, not for a ringtone.
        player.CommandManager.IsEnabled = false;
        player.MediaFailed += OnMediaFailed;
        return player;
    }

    /// <summary>
    /// A failure to open or play the sound arrives here, on a background thread,
    /// instead of as an exception. There is nothing to do: the ring is silent.
    /// </summary>
    private static void OnMediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
    {
    }

    private void Release()
    {
        if (_player is null)
        {
            return;
        }
        _player.MediaFailed -= OnMediaFailed;
        _player.Dispose();
        _player = null;
    }

    /// <summary>Stops the ringtone and lets go of the player.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        Release();
    }
}
