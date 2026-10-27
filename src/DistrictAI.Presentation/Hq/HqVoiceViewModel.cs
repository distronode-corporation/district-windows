using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DistrictAI.Core.Ffi;

namespace DistrictAI.ViewModels.Hq;

/// <summary>
/// A District HQ voice session's controls and the changes it proposed, copied
/// from the core's <see cref="HqVoiceView"/>: the line saying what the
/// assistant is doing (or why the session ended), Stop voice, the microphone,
/// and a card per change proposed by voice. District HQ and the voice dock
/// each hold one. Nothing is decided here: a change is applied only by the
/// member's spoken "confirm" or a click on its Confirm, and the core keeps one
/// click in flight per card.
/// </summary>
public sealed partial class HqVoiceViewModel : ObservableObject
{
    private PageContext? _context;
    private bool _writing;

    /// <summary>The changes proposed by voice and not yet resolved, oldest first.</summary>
    public ObservableCollection<HqVoiceCardItem> Cards { get; } = [];

    /// <summary>Where the session stands, for the keyboard to follow it.</summary>
    [ObservableProperty]
    public partial HqVoiceStage Stage { get; set; }

    /// <summary>What the session is doing, or why it ended or could not start; or empty.</summary>
    [ObservableProperty]
    public partial string Line { get; set; } = string.Empty;

    /// <summary>Whether the line is news, shown quietly.</summary>
    [ObservableProperty]
    public partial bool ShowsNews { get; set; }

    /// <summary>Whether the line is a problem, shown as an error.</summary>
    [ObservableProperty]
    public partial bool ShowsProblem { get; set; }

    /// <summary>Whether to show a progress ring by the line.</summary>
    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>Whether Stop voice is shown: a session is being started or is under way.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    public partial bool CanStop { get; set; }

    /// <summary>Stop voice's words.</summary>
    [ObservableProperty]
    public partial string StopLabel { get; set; } = string.Empty;

    /// <summary>Whether the microphone button is shown: while the session's room is live.</summary>
    [ObservableProperty]
    public partial bool ShowMicrophone { get; set; }

    /// <summary>
    /// Whether the microphone is off. The microphone button writes it, which
    /// asks the core for the other state; the core's answer writes it back.
    /// </summary>
    [ObservableProperty]
    public partial bool Muted { get; set; }

    /// <summary>Whether to offer putting away what the line (or, in the dock, a notice) says.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DismissCommand))]
    public partial bool CanDismiss { get; set; }

    /// <summary>Why the last card went without a result, or empty.</summary>
    [ObservableProperty]
    public partial string Notice { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="Notice"/>.</summary>
    [ObservableProperty]
    public partial bool HasNotice { get; set; }

    /// <summary>Whether a change proposed by voice is waiting.</summary>
    [ObservableProperty]
    public partial bool HasCards { get; set; }

    internal void Attach(PageContext context) => _context = context;

    /// <summary>Draws <paramref name="view"/>. The stage is written first, so a page sees it change before any control it held hides.</summary>
    internal void Show(HqVoiceView view)
    {
        Stage = view.Stage;
        Line = view.Line ?? string.Empty;
        ShowsProblem = Line.Length > 0 && view.LineIsProblem;
        ShowsNews = Line.Length > 0 && !view.LineIsProblem;
        IsBusy = view.Busy;
        CanStop = view.CanStop;
        StopLabel = view.StopLabel;
        ShowMicrophone = view.ShowMicrophone;
        _writing = true;
        try
        {
            Muted = view.Muted;
        }
        finally
        {
            _writing = false;
        }
        CanDismiss = view.CanDismiss;
        Notice = Display.Failure(view.Notice);
        HasNotice = Notice.Length > 0;
        Display.Sync(Cards, [.. view.Cards.Select(HqVoiceCardItem.From)]);
        HasCards = Cards.Count > 0;
    }

    partial void OnMutedChanged(bool value)
    {
        if (!_writing)
        {
            _context?.Send(new UiEvent.Microphone(!value));
        }
    }

    /// <summary>Applies the change on <paramref name="card"/>, when its Confirm works.</summary>
    internal void Confirm(HqVoiceCardItem card)
    {
        if (card.CanConfirm)
        {
            Send(new HqAction.ConfirmVoiceCard(card.ProposalId));
        }
    }

    /// <summary>Cancels the change on <paramref name="card"/>, when its Cancel works.</summary>
    internal void Cancel(HqVoiceCardItem card)
    {
        if (card.CanCancel)
        {
            Send(new HqAction.CancelVoiceCard(card.ProposalId));
        }
    }

    private void Send(HqAction action) => _context?.Send(new UiEvent.Hq(action));

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop() => Send(new HqAction.StopVoice());

    [RelayCommand(CanExecute = nameof(CanDismiss))]
    private void Dismiss() => Send(new HqAction.DismissVoiceNotice());

    /// <summary>Puts away the notice District HQ shows apart from the line.</summary>
    [RelayCommand]
    private void DismissNotice() => Send(new HqAction.DismissVoiceNotice());
}
