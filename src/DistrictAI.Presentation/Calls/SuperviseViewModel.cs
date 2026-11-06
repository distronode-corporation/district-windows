using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DistrictAI.Core.Ffi;
using DistrictAI.L10n;

namespace DistrictAI.ViewModels.Calls;

/// <summary>
/// Supervising a live call from its page, copied from the core's
/// <see cref="SuperviseView"/>: Listen and Stop listening, the note to the
/// receptionist, Take over (after its confirmation) and Hand back, the call's
/// lines, and the microphone as the call engine reports it.
/// </summary>
/// <remarks>
/// Every rule is the core's. Each button sends one
/// <see cref="UiEvent.Supervise"/> and nothing else: no request is repeated
/// here, and the card says "listening" or "on the call" only once the core
/// does, which is once the engine has joined the room. The mute button is a
/// plain button whose words come only from the engine's report
/// (<see cref="MuteLabel"/>): pressing it asks the core for the other state
/// and changes nothing on screen until the engine says the microphone moved,
/// so it never says off while the microphone is live. Narrator hears the moments that matter without
/// looking (<see cref="Announced"/>): the microphone going live, the session
/// ending, and the receptionist's answer to a note.
/// </remarks>
public sealed partial class SuperviseViewModel : ObservableObject
{
    private readonly TextEcho _echo = new();
    private PageContext? _context;
    private bool _writing;
    private SuperviseView? _last;

    /// <summary>Raised with a line for Narrator to read out politely.</summary>
    public event EventHandler<string>? Announced;

    /// <summary>Whether the card shows: the call is under way, or a session on it ended and says how.</summary>
    [ObservableProperty]
    public partial bool IsShown { get; set; }

    /// <summary>Where the session stands.</summary>
    [ObservableProperty]
    public partial SuperviseState State { get; set; }

    /// <summary>The line saying so, or empty while idle.</summary>
    [ObservableProperty]
    public partial string Status { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="Status"/>.</summary>
    [ObservableProperty]
    public partial bool HasStatus { get; set; }

    /// <summary>Whether a connection is being made: the credential asked for, or the room being joined.</summary>
    [ObservableProperty]
    public partial bool IsWorking { get; set; }

    /// <summary>"Listen in".</summary>
    [ObservableProperty]
    public partial string ListenTitle { get; set; } = string.Empty;

    /// <summary>What listening is.</summary>
    [ObservableProperty]
    public partial string ListenBody { get; set; } = string.Empty;

    /// <summary>"Listen".</summary>
    [ObservableProperty]
    public partial string ListenLabel { get; set; } = string.Empty;

    /// <summary>Whether Listen works.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ListenCommand))]
    public partial bool CanListen { get; set; }

    /// <summary>"Stop listening".</summary>
    [ObservableProperty]
    public partial string StopListeningLabel { get; set; } = string.Empty;

    /// <summary>Whether Stop listening works.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StopListeningCommand))]
    public partial bool CanStopListening { get; set; }

    /// <summary>Whether the member's role may send a note and take over: a viewer sees neither.</summary>
    [ObservableProperty]
    public partial bool CanSupervise { get; set; }

    /// <summary>"Take over the call".</summary>
    [ObservableProperty]
    public partial string TakeOverLabel { get; set; } = string.Empty;

    /// <summary>Whether Take over the call works.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TakeOverCommand))]
    public partial bool CanTakeOver { get; set; }

    /// <summary>"Hand back to your receptionist".</summary>
    [ObservableProperty]
    public partial string HandBackLabel { get; set; } = string.Empty;

    /// <summary>Whether Hand back works.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(HandBackCommand))]
    public partial bool CanHandBack { get; set; }

    /// <summary>Whether the takeover confirmation shows. Nothing is asked of the service until its Take over.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmTakeOverCommand))]
    public partial bool IsConfirming { get; set; }

    /// <summary>"Take over this call?"</summary>
    [ObservableProperty]
    public partial string ConfirmTitle { get; set; } = string.Empty;

    /// <summary>What the caller hears, and that the microphone goes live.</summary>
    [ObservableProperty]
    public partial string ConfirmBody { get; set; } = string.Empty;

    /// <summary>"Take over".</summary>
    [ObservableProperty]
    public partial string ConfirmLabel { get; set; } = string.Empty;

    /// <summary>"Cancel".</summary>
    [ObservableProperty]
    public partial string CancelLabel { get; set; } = string.Empty;

    /// <summary>What being on the call means, or empty.</summary>
    [ObservableProperty]
    public partial string OnCallBody { get; set; } = string.Empty;

    /// <summary>Whether there is an <see cref="OnCallBody"/>.</summary>
    [ObservableProperty]
    public partial bool HasOnCallBody { get; set; }

    /// <summary>Why the last listen or takeover did not happen, or empty.</summary>
    [ObservableProperty]
    public partial string Failure { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="Failure"/>.</summary>
    [ObservableProperty]
    public partial bool HasFailure { get; set; }

    /// <summary>Whether Dismiss puts something away: a failure, or how the session ended.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DismissCommand))]
    public partial bool CanDismiss { get; set; }

    /// <summary>"Note to your receptionist".</summary>
    [ObservableProperty]
    public partial string WhisperTitle { get; set; } = string.Empty;

    /// <summary>
    /// The note's box. A change the member makes is sent to the core; the
    /// core's draft is written back only when it is one the box did not send
    /// (cleared once the note is delivered).
    /// </summary>
    [ObservableProperty]
    public partial string WhisperDraft { get; set; } = string.Empty;

    /// <summary>The box's placeholder: an example, or why no note can be sent.</summary>
    [ObservableProperty]
    public partial string WhisperPlaceholder { get; set; } = string.Empty;

    /// <summary>"Send note".</summary>
    [ObservableProperty]
    public partial string WhisperSendLabel { get; set; } = string.Empty;

    /// <summary>Whether the box takes a note.</summary>
    [ObservableProperty]
    public partial bool CanEditWhisper { get; set; }

    /// <summary>Whether Send works: one note at a time, and not a blank one.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendWhisperCommand))]
    public partial bool CanSendWhisper { get; set; }

    /// <summary>Whether a note is on its way.</summary>
    [ObservableProperty]
    public partial bool WhisperSending { get; set; }

    /// <summary>Why the last note was not delivered, or empty. The draft is kept.</summary>
    [ObservableProperty]
    public partial string WhisperFailure { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="WhisperFailure"/>.</summary>
    [ObservableProperty]
    public partial bool HasWhisperFailure { get; set; }

    /// <summary>What the receptionist did with the last note, or empty.</summary>
    [ObservableProperty]
    public partial string WhisperOutcome { get; set; } = string.Empty;

    /// <summary>The first words of that note, to say which one, or empty.</summary>
    [ObservableProperty]
    public partial string WhisperOutcomeWords { get; set; } = string.Empty;

    /// <summary>Whether there are <see cref="WhisperOutcomeWords"/>.</summary>
    [ObservableProperty]
    public partial bool HasWhisperOutcomeWords { get; set; }

    /// <summary>Whether there is a <see cref="WhisperOutcome"/>.</summary>
    [ObservableProperty]
    public partial bool HasWhisperOutcome { get; set; }

    /// <summary>"Live transcript".</summary>
    [ObservableProperty]
    public partial string LinesHeading { get; set; } = string.Empty;

    /// <summary>The call's lines heard in this session, oldest first.</summary>
    public ObservableCollection<SuperviseLineItem> Lines { get; } = [];

    /// <summary>Whether there are <see cref="Lines"/>.</summary>
    [ObservableProperty]
    public partial bool HasLines { get; set; }

    /// <summary>What shows in the room before anything has been heard, or empty.</summary>
    [ObservableProperty]
    public partial string LinesWaiting { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="LinesWaiting"/>.</summary>
    [ObservableProperty]
    public partial bool HasLinesWaiting { get; set; }

    /// <summary>The microphone, as the engine reports it.</summary>
    [ObservableProperty]
    public partial MicrophoneView Microphone { get; set; }

    /// <summary>Whether the caller hears the member, said plainly, or empty.</summary>
    [ObservableProperty]
    public partial string MicrophoneLine { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="MicrophoneLine"/>.</summary>
    [ObservableProperty]
    public partial bool HasMicrophoneLine { get; set; }

    /// <summary>Whether the microphone could not be opened, so nobody hears the member.</summary>
    [ObservableProperty]
    public partial bool MicrophoneBlocked { get; set; }

    /// <summary>Whether the mute button shows: on the call with a microphone.</summary>
    [ObservableProperty]
    public partial bool ShowsMute { get; set; }

    /// <summary>Whether the mute button works.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ToggleMuteCommand))]
    public partial bool CanMute { get; set; }

    /// <summary>The mute button's words: "Mute" while the engine says the microphone is on, "Unmute" otherwise.</summary>
    [ObservableProperty]
    public partial string MuteLabel { get; set; } = string.Empty;

    /// <summary>The connection's notice, or empty.</summary>
    [ObservableProperty]
    public partial string MediaNotice { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="MediaNotice"/>.</summary>
    [ObservableProperty]
    public partial bool HasMediaNotice { get; set; }

    internal void Attach(PageContext context) => _context = context;

    /// <summary>Draws <paramref name="view"/>, or hides the card when there is none.</summary>
    internal void Show(SuperviseView? view)
    {
        var last = _last;
        _last = view;
        IsShown = view is not null;
        if (view is null)
        {
            Display.Sync(Lines, []);
            HasLines = false;
            return;
        }
        State = view.State;
        Status = view.Status ?? string.Empty;
        HasStatus = Status.Length > 0;
        IsWorking = view.State is SuperviseState.Connecting or SuperviseState.TakingOver;
        ListenTitle = view.ListenTitle;
        ListenBody = view.ListenBody;
        ListenLabel = view.ListenLabel;
        CanListen = view.CanListen;
        StopListeningLabel = view.StopListeningLabel;
        CanStopListening = view.CanStopListening;
        CanSupervise = view.CanSupervise;
        TakeOverLabel = view.TakeOverLabel;
        CanTakeOver = view.CanTakeOver;
        HandBackLabel = view.HandBackLabel;
        CanHandBack = view.CanHandBack;
        IsConfirming = view.Confirm is not null;
        ConfirmTitle = view.Confirm?.Title ?? string.Empty;
        ConfirmBody = view.Confirm?.Body ?? string.Empty;
        ConfirmLabel = view.Confirm?.Confirm ?? string.Empty;
        CancelLabel = view.Confirm?.Cancel ?? string.Empty;
        OnCallBody = view.OnCallBody ?? string.Empty;
        HasOnCallBody = OnCallBody.Length > 0;
        Failure = Display.Failure(view.Failure);
        HasFailure = view.Failure is not null;
        CanDismiss = view.Failure is not null || view.Whisper.Failure is not null || view.State == SuperviseState.Ended;
        ShowWhisper(view.Whisper);
        LinesHeading = view.LinesHeading;
        Display.Sync(Lines, [.. view.Lines.Select(SuperviseLineItem.From)]);
        HasLines = Lines.Count > 0;
        LinesWaiting = view.LinesWaiting ?? string.Empty;
        HasLinesWaiting = LinesWaiting.Length > 0;
        Microphone = view.Microphone;
        MicrophoneLine = view.MicrophoneLine ?? string.Empty;
        HasMicrophoneLine = MicrophoneLine.Length > 0;
        MicrophoneBlocked = view.Microphone == MicrophoneView.Blocked;
        ShowsMute = view.State == SuperviseState.OnCall || (view.State == SuperviseState.Reconnecting && view.Microphone != MicrophoneView.None);
        CanMute = view.CanMute;
        MuteLabel = view.Microphone == MicrophoneView.On ? S.CallBarMute : S.RoomsUnmute;
        MediaNotice = view.MediaNotice ?? string.Empty;
        HasMediaNotice = MediaNotice.Length > 0;
        Announce(last, view);
    }

    private void ShowWhisper(WhisperView whisper)
    {
        WhisperTitle = whisper.Title;
        if (_echo.Write(whisper.Draft, WhisperDraft))
        {
            _writing = true;
            try
            {
                WhisperDraft = whisper.Draft;
            }
            finally
            {
                _writing = false;
            }
        }
        WhisperPlaceholder = whisper.Placeholder;
        WhisperSendLabel = whisper.SendLabel;
        CanEditWhisper = whisper.CanEdit;
        CanSendWhisper = whisper.CanSend;
        WhisperSending = whisper.Sending;
        WhisperFailure = Display.Failure(whisper.Failure);
        HasWhisperFailure = whisper.Failure is not null;
        WhisperOutcome = whisper.Outcome ?? string.Empty;
        HasWhisperOutcome = WhisperOutcome.Length > 0;
        WhisperOutcomeWords = whisper.OutcomeWords ?? string.Empty;
        HasWhisperOutcomeWords = WhisperOutcomeWords.Length > 0;
    }

    /// <summary>The receptionist's answer to a note, then the note's first words, as Narrator reads them.</summary>
    private static string Outcome(WhisperView whisper) =>
        string.Join(" ", new[] { whisper.Outcome, whisper.OutcomeWords }.Where(part => !string.IsNullOrEmpty(part)));

    /// <summary>
    /// What changed since <paramref name="last"/> that Narrator should hear:
    /// the microphone going live, the session ending, a new answer to a note.
    /// Nothing is said for the card as first drawn.
    /// </summary>
    private void Announce(SuperviseView? last, SuperviseView view)
    {
        if (last is null)
        {
            return;
        }
        if (view.Microphone == MicrophoneView.On && last.Microphone != MicrophoneView.On && view.MicrophoneLine is { } live)
        {
            Announced?.Invoke(this, live);
        }
        if (view.State == SuperviseState.Ended && last.State != SuperviseState.Ended && view.Status is { } ended)
        {
            Announced?.Invoke(this, ended);
        }
        var outcome = Outcome(view.Whisper);
        if (outcome.Length > 0 && outcome != Outcome(last.Whisper))
        {
            Announced?.Invoke(this, outcome);
        }
    }

    partial void OnWhisperDraftChanged(string value)
    {
        if (_writing)
        {
            return;
        }
        _echo.Typed(value);
        Send(new SuperviseAction.EditWhisper(value));
    }

    private void Send(SuperviseAction action) => _context?.Send(new UiEvent.Supervise(action));

    [RelayCommand(CanExecute = nameof(CanListen))]
    private void Listen() => Send(new SuperviseAction.Listen());

    [RelayCommand(CanExecute = nameof(CanStopListening))]
    private void StopListening() => Send(new SuperviseAction.StopListening());

    [RelayCommand(CanExecute = nameof(CanTakeOver))]
    private void TakeOver() => Send(new SuperviseAction.TakeOver());

    [RelayCommand]
    private void CancelTakeOver() => Send(new SuperviseAction.CancelTakeOver());

    [RelayCommand(CanExecute = nameof(IsConfirming))]
    private void ConfirmTakeOver() => Send(new SuperviseAction.ConfirmTakeOver());

    [RelayCommand(CanExecute = nameof(CanHandBack))]
    private void HandBack() => Send(new SuperviseAction.HandBack());

    [RelayCommand(CanExecute = nameof(CanSendWhisper))]
    private void SendWhisper() => Send(new SuperviseAction.SendWhisper());

    /// <summary>
    /// Asks for the microphone the engine does not report: off while it is on,
    /// on otherwise. What the card shows waits for the engine's answer.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanMute))]
    private void ToggleMute() => _context?.Send(new UiEvent.Microphone(_last?.Microphone != MicrophoneView.On));

    [RelayCommand(CanExecute = nameof(CanDismiss))]
    private void Dismiss() => Send(new SuperviseAction.Dismiss());
}

/// <summary>One line of the call, as the receptionist's transcript gives it.</summary>
/// <param name="Speaker">"Caller", or the receptionist's name.</param>
/// <param name="Text">What was said.</param>
/// <param name="Caller">Whether the caller said it.</param>
public sealed record SuperviseLineItem(string Speaker, string Text, bool Caller)
{
    /// <summary>What a screen reader says for the line.</summary>
    public string AccessibleName => $"{Speaker}: {Text}";

    internal static SuperviseLineItem From(SuperviseLineView line) => new(line.Speaker, line.Text, line.Caller);

    /// <inheritdoc/>
    public override string ToString() => AccessibleName;
}
