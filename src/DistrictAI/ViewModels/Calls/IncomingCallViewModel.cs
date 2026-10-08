using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DistrictAI.Core;
using DistrictAI.Core.Ffi;

namespace DistrictAI.ViewModels.Calls;

/// <summary>
/// The banner of a call ringing here, copied from the core's
/// <see cref="IncomingRingView"/>: who is calling, the line under it, and
/// Answer and Decline, each sent with the call it names. The ringtone and the
/// toast, for a window that is hidden, are the core's effects, not the banner's.
/// </summary>
public sealed partial class IncomingCallViewModel : ObservableObject
{
    private CoreHost? _core;

    /// <summary>Whether a call is ringing.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AnswerCommand), nameof(DeclineCommand))]
    public partial bool IsShown { get; set; }

    /// <summary>The ringing call's id: what Answer and Decline name.</summary>
    [ObservableProperty]
    public partial string CallId { get; set; } = string.Empty;

    /// <summary>Who is calling, as the core puts it.</summary>
    [ObservableProperty]
    public partial string Caller { get; set; } = string.Empty;

    /// <summary>The line under the caller, or empty.</summary>
    [ObservableProperty]
    public partial string Detail { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="Detail"/>.</summary>
    [ObservableProperty]
    public partial bool HasDetail { get; set; }

    /// <summary>
    /// What a screen reader announces when the call starts ringing:
    /// "Incoming call", who from, and the line under it.
    /// </summary>
    [ObservableProperty]
    public partial string Announcement { get; set; } = string.Empty;

    internal void Attach(CoreHost core) => _core = core;

    /// <summary>
    /// Draws <paramref name="ring"/>, or hides the banner when nothing rings,
    /// and answers whether a call started ringing (one not shown before), which
    /// the banner announces.
    /// </summary>
    internal bool Show(IncomingRingView? ring)
    {
        if (ring is null)
        {
            IsShown = false;
            CallId = string.Empty;
            return false;
        }
        var started = !IsShown || CallId != ring.CallId;
        CallId = ring.CallId;
        Caller = ring.Caller;
        Detail = ring.Detail ?? string.Empty;
        HasDetail = !string.IsNullOrEmpty(ring.Detail);
        Announcement = HasDetail
            ? $"Incoming call from {ring.Caller}. {ring.Detail}"
            : $"Incoming call from {ring.Caller}.";
        IsShown = true;
        return started;
    }

    [RelayCommand(CanExecute = nameof(IsShown))]
    private void Answer() => _core?.Send(new UiEvent.Answer(CallId));

    [RelayCommand(CanExecute = nameof(IsShown))]
    private void Decline() => _core?.Send(new UiEvent.Decline(CallId));
}
