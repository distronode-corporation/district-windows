using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DistrictAI.Core.Ffi;

namespace DistrictAI.ViewModels.Calls;

/// <summary>
/// The banner of a call ringing here, copied from the core's
/// <see cref="IncomingRingView"/>: the heading ("Incoming call", naming the
/// workspace when it is not the one open), where the call came from, Answer and
/// Decline, each sent with the call it names; once the ring ends, how it ended,
/// until it is put away. The ringtone and the toast, for a window that is
/// hidden, are the core's effects, not the banner's.
/// </summary>
public sealed partial class IncomingCallViewModel : ObservableObject
{
    private PageContext? _context;

    /// <summary>Whether there is a ring to show.</summary>
    [ObservableProperty]
    public partial bool IsShown { get; set; }

    /// <summary>The ringing call's id: what Answer and Decline name.</summary>
    [ObservableProperty]
    public partial string CallId { get; set; } = string.Empty;

    /// <summary>The heading. Nothing names the caller: the ring carries ids only.</summary>
    [ObservableProperty]
    public partial string Heading { get; set; } = string.Empty;

    /// <summary>Where the call came from, or how the ring ended; or empty.</summary>
    [ObservableProperty]
    public partial string Detail { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="Detail"/>.</summary>
    [ObservableProperty]
    public partial bool HasDetail { get; set; }

    /// <summary>"Answering puts your microphone on this call." while it can be answered; or empty.</summary>
    [ObservableProperty]
    public partial string Note { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="Note"/>.</summary>
    [ObservableProperty]
    public partial bool HasNote { get; set; }

    /// <summary>Whether Answer is shown.</summary>
    [ObservableProperty]
    public partial bool ShowAnswer { get; set; }

    /// <summary>Whether Answer works.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AnswerCommand))]
    public partial bool CanAnswer { get; set; }

    /// <summary>Whether the ring is live (ringing, waiting or being answered), so Decline is shown.</summary>
    [ObservableProperty]
    public partial bool Live { get; set; }

    /// <summary>Whether Decline works.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeclineCommand))]
    public partial bool CanDecline { get; set; }

    /// <summary>Whether the answer is on its way.</summary>
    [ObservableProperty]
    public partial bool Answering { get; set; }

    /// <summary>Whether the ring is over, so Dismiss puts it away.</summary>
    [ObservableProperty]
    public partial bool CanDismiss { get; set; }

    /// <summary>Whether the ringtone is sounding, which the banner shows in the accent colour.</summary>
    [ObservableProperty]
    public partial bool Sounding { get; set; }

    /// <summary>What a screen reader announces when the call starts ringing: the heading and the line under it.</summary>
    [ObservableProperty]
    public partial string Announcement { get; set; } = string.Empty;

    internal void Attach(PageContext context) => _context = context;

    /// <summary>
    /// Draws <paramref name="ring"/>, or hides the banner when there is none,
    /// and answers whether a call started ringing (one not shown before), which
    /// the banner announces.
    /// </summary>
    internal bool Show(IncomingRingView? ring)
    {
        if (ring is null)
        {
            IsShown = false;
            CallId = string.Empty;
            CanAnswer = false;
            CanDecline = false;
            return false;
        }
        var started = !IsShown || CallId != ring.CallId;
        CallId = ring.CallId;
        Heading = ring.Caller;
        Detail = ring.Detail ?? string.Empty;
        HasDetail = Detail.Length > 0;
        Note = ring.Note ?? string.Empty;
        HasNote = Note.Length > 0;
        ShowAnswer = ring.ShowAnswer;
        CanAnswer = ring.CanAnswer;
        Live = ring.Live;
        CanDecline = ring.CanDecline;
        Answering = ring.Answering;
        CanDismiss = !ring.Live;
        Sounding = ring.Sounding;
        Announcement = HasDetail ? $"{Heading}. {Detail}" : Heading;
        IsShown = true;
        return started;
    }

    [RelayCommand(CanExecute = nameof(CanAnswer))]
    private void Answer() => _context?.Send(new UiEvent.Answer(CallId));

    [RelayCommand(CanExecute = nameof(CanDecline))]
    private void Decline() => _context?.Send(new UiEvent.Decline(CallId));

    [RelayCommand]
    private void Dismiss() => _context?.Send(new UiEvent.DismissRing());
}
