using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DistrictAI.Core;
using DistrictAI.Core.Ffi;
using Microsoft.UI.Xaml;

namespace DistrictAI.ViewModels.Calls;

/// <summary>
/// The strip under every signed-in screen while a call is under way, copied
/// from the core's <see cref="ActiveCallView"/>: who the call is with, where it
/// stands, how long it has been answered, mute and hang up; once over, how it
/// ended, until it is put away.
/// </summary>
/// <remarks>
/// The core says when the call was answered (<see cref="ActiveCallView.ConnectedAt"/>)
/// and the strip counts from it, once a second, on a <see cref="DispatcherTimer"/>
/// that runs only while the call is answered and not over. The length shown is
/// this app's own; the call log is the record.
/// </remarks>
public sealed partial class CallBarViewModel : ObservableObject
{
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(1);

    private readonly Func<DateTimeOffset> _now;
    private CoreHost? _core;
    private DispatcherTimer? _timer;
    private DateTimeOffset? _connectedAt;
    private bool _writing;

    /// <summary>A strip that reads the time from the system clock.</summary>
    public CallBarViewModel()
        : this(() => DateTimeOffset.UtcNow)
    {
    }

    /// <summary>A strip that reads the time from <paramref name="now"/>.</summary>
    internal CallBarViewModel(Func<DateTimeOffset> now) => _now = now;

    /// <summary>Whether there is a call to show.</summary>
    [ObservableProperty]
    public partial bool IsShown { get; set; }

    /// <summary>Who the call is with.</summary>
    [ObservableProperty]
    public partial string Peer { get; set; } = string.Empty;

    /// <summary>Where the call stands, as the core puts it.</summary>
    [ObservableProperty]
    public partial string StateLabel { get; set; } = string.Empty;

    /// <summary>How long the call has been answered, as <c>m:ss</c> or <c>h:mm:ss</c>; empty before it is.</summary>
    [ObservableProperty]
    public partial string Duration { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="Duration"/>.</summary>
    [ObservableProperty]
    public partial bool HasDuration { get; set; }

    /// <summary>
    /// Whether the microphone is muted. The mute button writes it, which sends
    /// the change to the core; the core's answer writes it back.
    /// </summary>
    [ObservableProperty]
    public partial bool Muted { get; set; }

    /// <summary>Whether the call is still on, so mute is offered.</summary>
    [ObservableProperty]
    public partial bool IsLive { get; set; }

    /// <summary>Whether Hang up works.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(HangUpCommand))]
    public partial bool CanHangUp { get; set; }

    /// <summary>How the call ended, or empty while it is on.</summary>
    [ObservableProperty]
    public partial string Ended { get; set; } = string.Empty;

    /// <summary>Whether there is an <see cref="Ended"/>.</summary>
    [ObservableProperty]
    public partial bool HasEnded { get; set; }

    /// <summary>Why the call failed, or empty.</summary>
    [ObservableProperty]
    public partial string Failure { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="Failure"/>.</summary>
    [ObservableProperty]
    public partial bool HasFailure { get; set; }

    /// <summary>Whether the call is over (ended or failed), so it can be put away.</summary>
    [ObservableProperty]
    public partial bool CanDismiss { get; set; }

    /// <summary>Whether Windows refused the app the microphone.</summary>
    [ObservableProperty]
    public partial bool MicrophoneDenied { get; set; }

    internal void Attach(CoreHost core) => _core = core;

    /// <summary>Draws <paramref name="call"/>, or hides the strip when there is none.</summary>
    internal void Show(ActiveCallView? call)
    {
        IsShown = call is not null;
        if (call is null)
        {
            StopTimer();
            _connectedAt = null;
            Duration = string.Empty;
            HasDuration = false;
            MicrophoneDenied = false;
            return;
        }
        var over = call.Ended is not null || call.Failure is not null;
        Peer = call.Peer;
        StateLabel = call.StateLabel;
        _writing = true;
        try
        {
            Muted = call.Muted;
        }
        finally
        {
            _writing = false;
        }
        IsLive = !over;
        CanHangUp = call.CanHangUp;
        Ended = call.Ended ?? string.Empty;
        HasEnded = call.Ended is not null;
        Failure = FailureText.Of(call.Failure);
        HasFailure = call.Failure is not null;
        CanDismiss = over;
        MicrophoneDenied = call.MicrophoneDenied;

        _connectedAt = ParseInstant(call.ConnectedAt);
        HasDuration = _connectedAt is not null;
        if (_connectedAt is null)
        {
            StopTimer();
            Duration = string.Empty;
            return;
        }
        // Once over, the length stays as it was last counted.
        UpdateDuration();
        if (over)
        {
            StopTimer();
        }
        else
        {
            StartTimer();
        }
    }

    partial void OnMutedChanged(bool value)
    {
        if (!_writing)
        {
            _core?.Send(new UiEvent.Microphone(!value));
        }
    }

    [RelayCommand(CanExecute = nameof(CanHangUp))]
    private void HangUp() => _core?.Send(new UiEvent.HangUp());

    [RelayCommand]
    private void Dismiss() => _core?.Send(new UiEvent.DismissCall());

    /// <summary>How long a call answered <paramref name="elapsed"/> ago has lasted: <c>m:ss</c>, or <c>h:mm:ss</c> from an hour.</summary>
    internal static string FormatDuration(TimeSpan elapsed)
    {
        // A clock that disagrees with the service's by a little reads as zero,
        // not as a negative length.
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }
        return elapsed.TotalHours >= 1
            ? string.Create(CultureInfo.InvariantCulture, $"{(int)elapsed.TotalHours}:{elapsed.Minutes:00}:{elapsed.Seconds:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{elapsed.Minutes}:{elapsed.Seconds:00}");
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
            Duration = FormatDuration(_now() - connectedAt);
        }
    }

    private void StartTimer()
    {
        if (_timer is null)
        {
            _timer = new DispatcherTimer { Interval = Tick };
            _timer.Tick += OnTick;
        }
        if (!_timer.IsEnabled)
        {
            _timer.Start();
        }
    }

    private void StopTimer() => _timer?.Stop();

    private void OnTick(object? sender, object e) => UpdateDuration();
}
