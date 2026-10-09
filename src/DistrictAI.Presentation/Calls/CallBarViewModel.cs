using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DistrictAI.Core.Ffi;

namespace DistrictAI.ViewModels.Calls;

/// <summary>
/// The strip under every signed-in screen while a call is under way, copied
/// from the core's <see cref="ActiveCallView"/>: who the call is with, where it
/// stands or how long it has been answered, mute and hang up; once over, how it
/// ended, until it is put away.
/// </summary>
/// <remarks>
/// While the call is answered the core's <see cref="ActiveCallView.StateLabel"/>
/// is its length, which the core moves on only when its model changes. So the
/// strip counts from <see cref="ActiveCallView.ConnectedAt"/> itself, once a
/// second, on a <see cref="TimeProvider"/> timer that runs only while the core
/// gives that instant (answered, and not over). Each tick is posted to the
/// <see cref="SynchronizationContext"/> the timer was started on (the UI
/// thread's, in the app), so the strip changes only on that thread. The length
/// shown is this app's own; the call log is the record.
/// </remarks>
public sealed partial class CallBarViewModel : ObservableObject
{
    private static readonly TimeSpan _tickEvery = TimeSpan.FromSeconds(1);

    private readonly TimeProvider _time;
    private PageContext? _context;
    private ITimer? _timer;
    private bool _ticking;
    private DateTimeOffset? _connectedAt;
    private bool _writing;

    /// <summary>A strip that reads the time from the system clock.</summary>
    public CallBarViewModel()
        : this(TimeProvider.System)
    {
    }

    /// <summary>A strip that reads the time, and counts, on <paramref name="time"/>.</summary>
    internal CallBarViewModel(TimeProvider time) => _time = time;

    /// <summary>Whether there is a call to show.</summary>
    [ObservableProperty]
    public partial bool IsShown { get; set; }

    /// <summary>Who the call is with.</summary>
    [ObservableProperty]
    public partial string Peer { get; set; } = string.Empty;

    /// <summary>Where the call stands, how long it has been answered, or how it ended.</summary>
    [ObservableProperty]
    public partial string Status { get; set; } = string.Empty;

    /// <summary>
    /// Whether the microphone is off. The mute button writes it, which asks the
    /// core for the other state; the core's answer writes it back.
    /// </summary>
    [ObservableProperty]
    public partial bool Muted { get; set; }

    /// <summary>Whether the mute button works: the call's audio is up and the call is not over.</summary>
    [ObservableProperty]
    public partial bool CanMute { get; set; }

    /// <summary>Whether the call is not over, so mute and Hang up are shown.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(HangUpCommand))]
    public partial bool CanHangUp { get; set; }

    /// <summary>How the call ended, when that is not already the <see cref="Status"/>; or empty.</summary>
    [ObservableProperty]
    public partial string Ended { get; set; } = string.Empty;

    /// <summary>Whether there is an <see cref="Ended"/>.</summary>
    [ObservableProperty]
    public partial bool HasEnded { get; set; }

    /// <summary>The note under an ended call that was answered, or empty.</summary>
    [ObservableProperty]
    public partial string EndedNote { get; set; } = string.Empty;

    /// <summary>Whether there is an <see cref="EndedNote"/>.</summary>
    [ObservableProperty]
    public partial bool HasEndedNote { get; set; }

    /// <summary>Why the call was never placed or could not be connected, or empty.</summary>
    [ObservableProperty]
    public partial string Failure { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="Failure"/>.</summary>
    [ObservableProperty]
    public partial bool HasFailure { get; set; }

    /// <summary>Whether the call is over, so Dismiss puts it away.</summary>
    [ObservableProperty]
    public partial bool CanDismiss { get; set; }

    /// <summary>The call's connection notice (reconnecting, for one), or empty.</summary>
    [ObservableProperty]
    public partial string MediaNotice { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="MediaNotice"/> to show.</summary>
    [ObservableProperty]
    public partial bool HasMediaNotice { get; set; }

    /// <summary>Whether the microphone could not be used, so nobody hears the member.</summary>
    [ObservableProperty]
    public partial bool MicrophoneDenied { get; set; }

    internal void Attach(PageContext context) => _context = context;

    /// <summary>Draws <paramref name="call"/>, or hides the strip when there is none.</summary>
    internal void Show(ActiveCallView? call)
    {
        IsShown = call is not null;
        if (call is null)
        {
            StopTimer();
            _connectedAt = null;
            MicrophoneDenied = false;
            HasMediaNotice = false;
            return;
        }
        Peer = call.Peer;
        _writing = true;
        try
        {
            Muted = call.Muted;
        }
        finally
        {
            _writing = false;
        }
        CanMute = call.CanMute;
        CanHangUp = call.CanHangUp;
        Ended = call.Ended is { } ended && ended != call.StateLabel ? ended : string.Empty;
        HasEnded = Ended.Length > 0;
        EndedNote = call.EndedNote ?? string.Empty;
        HasEndedNote = EndedNote.Length > 0;
        Failure = Display.Failure(call.Failure);
        HasFailure = call.Failure is not null;
        CanDismiss = !call.CanHangUp;
        MicrophoneDenied = call.MicrophoneDenied;
        // The microphone's own bar says what to do about it; any other notice
        // shows on its own.
        MediaNotice = call.MediaNotice ?? string.Empty;
        HasMediaNotice = MediaNotice.Length > 0 && !call.MicrophoneDenied;

        _connectedAt = ParseInstant(call.ConnectedAt);
        if (_connectedAt is null)
        {
            StopTimer();
            Status = call.StateLabel;
            return;
        }
        UpdateDuration();
        StartTimer();
    }

    partial void OnMutedChanged(bool value)
    {
        if (!_writing)
        {
            _context?.Send(new UiEvent.Microphone(!value));
        }
    }

    [RelayCommand(CanExecute = nameof(CanHangUp))]
    private void HangUp() => _context?.Send(new UiEvent.HangUp());

    [RelayCommand]
    private void Dismiss() => _context?.Send(new UiEvent.DismissCall());

    /// <summary>
    /// How long a call answered <paramref name="elapsed"/> ago has lasted, as
    /// the core writes it: <c>mm:ss</c>, the minutes running past 59.
    /// </summary>
    internal static string FormatDuration(TimeSpan elapsed)
    {
        // A clock that disagrees with the service's by a little reads as zero,
        // not as a negative length.
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }
        return string.Create(CultureInfo.InvariantCulture, $"{(long)elapsed.TotalMinutes:00}:{elapsed.Seconds:00}");
    }

    /// <summary>An ISO 8601 instant from the core, or null for none or one that does not parse.</summary>
    private static DateTimeOffset? ParseInstant(string? iso) =>
        DateTimeOffset.TryParse(
            iso,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var instant)
            ? instant
            : null;

    private void UpdateDuration()
    {
        if (_connectedAt is { } connectedAt)
        {
            Status = FormatDuration(_time.GetUtcNow() - connectedAt);
        }
    }

    private void StartTimer()
    {
        if (_timer is null)
        {
            // Made stopped, and started below. Ticks go back to the thread
            // that started it.
            var ui = SynchronizationContext.Current;
            _timer = _time.CreateTimer(_ => OnTick(ui), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
        if (!_ticking)
        {
            _ticking = true;
            _timer.Change(_tickEvery, _tickEvery);
        }
    }

    private void StopTimer()
    {
        if (_ticking)
        {
            _ticking = false;
            _timer!.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
    }

    private void OnTick(SynchronizationContext? ui)
    {
        if (ui is null)
        {
            UpdateDuration();
        }
        else
        {
            ui.Post(_ => UpdateDuration(), null);
        }
    }
}
