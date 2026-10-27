using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DistrictAI.Core.Ffi;

namespace DistrictAI.ViewModels.Hq;

/// <summary>
/// District HQ: the conversation, the prompt box, the card in front of a
/// proposed change, and Report on each answer; and by voice, Start voice,
/// where the voice is processed (said before anything starts), the session's
/// controls and a card per change proposed by voice. Every word but the
/// prompt is the core's; what the buttons do is decided there too.
/// </summary>
public sealed partial class HqViewModel : ObservableObject
{
    private PageContext? _context;

    /// <summary>The conversation, oldest first.</summary>
    public ObservableCollection<HqMessageItem> Messages { get; } = [];

    /// <summary>The core's view of the screen, as last shown.</summary>
    [ObservableProperty]
    public partial HqView? View { get; set; }

    /// <summary>The heading.</summary>
    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;

    /// <summary>Whether the conversation is empty, so its heading and body show instead.</summary>
    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    /// <summary>Whether there is a conversation to show.</summary>
    [ObservableProperty]
    public partial bool HasMessages { get; set; }

    /// <summary>An empty conversation's heading.</summary>
    [ObservableProperty]
    public partial string EmptyTitle { get; set; } = string.Empty;

    /// <summary>An empty conversation's body.</summary>
    [ObservableProperty]
    public partial string EmptyBody { get; set; } = string.Empty;

    /// <summary>Whether a prompt is being answered.</summary>
    [ObservableProperty]
    public partial bool IsThinking { get; set; }

    /// <summary>The line while it is.</summary>
    [ObservableProperty]
    public partial string ThinkingText { get; set; } = string.Empty;

    /// <summary>Whether the last prompt failed.</summary>
    [ObservableProperty]
    public partial bool HasFailure { get; set; }

    /// <summary>Why.</summary>
    [ObservableProperty]
    public partial string FailureText { get; set; } = string.Empty;

    /// <summary>Whether "Try again" is offered.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RetryCommand))]
    public partial bool CanRetry { get; set; }

    /// <summary>The "Try again" button's words.</summary>
    [ObservableProperty]
    public partial string RetryLabel { get; set; } = string.Empty;

    /// <summary>Whether a change is proposed, so its card shows.</summary>
    [ObservableProperty]
    public partial bool HasCard { get; set; }

    /// <summary>The card's heading.</summary>
    [ObservableProperty]
    public partial string CardTitle { get; set; } = string.Empty;

    /// <summary>The service's sentence saying what the change would do.</summary>
    [ObservableProperty]
    public partial string CardSummary { get; set; } = string.Empty;

    /// <summary>The note under it, or empty.</summary>
    [ObservableProperty]
    public partial string CardNote { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="CardNote"/>.</summary>
    [ObservableProperty]
    public partial bool HasCardNote { get; set; }

    /// <summary>Whether the confirmed change is being applied.</summary>
    [ObservableProperty]
    public partial bool IsApplying { get; set; }

    /// <summary>The line while it is.</summary>
    [ObservableProperty]
    public partial string ApplyingText { get; set; } = string.Empty;

    /// <summary>Why the confirmation failed, or empty.</summary>
    [ObservableProperty]
    public partial string CardFailure { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="CardFailure"/>.</summary>
    [ObservableProperty]
    public partial bool HasCardFailure { get; set; }

    /// <summary>The confirming button's words.</summary>
    [ObservableProperty]
    public partial string ConfirmLabel { get; set; } = string.Empty;

    /// <summary>The declining button's words.</summary>
    [ObservableProperty]
    public partial string DismissLabel { get; set; } = string.Empty;

    /// <summary>Whether Confirm works now.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    public partial bool CanConfirm { get; set; }

    /// <summary>Whether Dismiss works now.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DismissCommand))]
    public partial bool CanDismiss { get; set; }

    /// <summary>What is typed in the prompt box.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AskCommand))]
    public partial string Draft { get; set; } = string.Empty;

    /// <summary>Whether a prompt can be sent now.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AskCommand))]
    public partial bool CanAsk { get; set; }

    /// <summary>The prompt box's placeholder, and its accessible name.</summary>
    [ObservableProperty]
    public partial string PromptPlaceholder { get; set; } = string.Empty;

    /// <summary>The send button's words.</summary>
    [ObservableProperty]
    public partial string AskLabel { get; set; } = string.Empty;

    /// <summary>The line under the prompt box.</summary>
    [ObservableProperty]
    public partial string Footnote { get; set; } = string.Empty;

    /// <summary>The voice session's controls and the changes proposed by voice.</summary>
    public HqVoiceViewModel Voice { get; } = new();

    /// <summary>Whether Start voice is shown: to a member who may talk, with no session and no statement up.</summary>
    [ObservableProperty]
    public partial bool ShowVoiceStart { get; set; }

    /// <summary>Whether Start voice works now.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartVoiceCommand))]
    public partial bool CanStartVoice { get; set; }

    /// <summary>Start voice's words.</summary>
    [ObservableProperty]
    public partial string VoiceStartLabel { get; set; } = string.Empty;

    /// <summary>Whether the residency statement is up.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AcceptVoiceCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeclineVoiceCommand))]
    public partial bool HasDisclosure { get; set; }

    /// <summary>The statement's heading.</summary>
    [ObservableProperty]
    public partial string DisclosureTitle { get; set; } = string.Empty;

    /// <summary>For a workspace whose data lives outside the United States, the line above the statement; or empty.</summary>
    [ObservableProperty]
    public partial string RegionNotice { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="RegionNotice"/>.</summary>
    [ObservableProperty]
    public partial bool HasRegionNotice { get; set; }

    /// <summary>Where the voice is processed, the web dashboard's words.</summary>
    [ObservableProperty]
    public partial string DisclosureBody { get; set; } = string.Empty;

    /// <summary>The statement's Start voice.</summary>
    [ObservableProperty]
    public partial string AcceptLabel { get; set; } = string.Empty;

    /// <summary>The statement's Not now.</summary>
    [ObservableProperty]
    public partial string DeclineLabel { get; set; } = string.Empty;

    internal void Attach(PageContext context)
    {
        _context = context;
        Voice.Attach(context);
    }

    internal void Show(HqView view) => Show(view, _context?.ReportSending ?? false);

    internal void Show(HqView view, bool reportSending)
    {
        View = view;
        Title = view.Title;
        IsEmpty = view.Empty is not null;
        HasMessages = view.Messages.Length > 0;
        EmptyTitle = view.Empty?.Title ?? string.Empty;
        EmptyBody = view.Empty?.Body ?? string.Empty;
        Display.Sync(Messages, [.. view.Messages.Select(message => HqMessageItem.From(message, !reportSending)).Where(item => item.Index >= 0)]);
        IsThinking = view.Thinking is not null;
        ThinkingText = view.Thinking ?? string.Empty;
        FailureText = Display.Failure(view.Failure);
        HasFailure = FailureText.Length > 0;
        CanRetry = view.CanRetry;
        RetryLabel = view.RetryLabel;
        ShowCard(view.Card);
        CanAsk = view.CanAsk;
        PromptPlaceholder = view.PromptPlaceholder;
        AskLabel = view.AskLabel;
        Footnote = view.Footnote;
        ShowVoice(view);
    }

    private void ShowVoice(HqView view)
    {
        Voice.Show(view.Voice);
        ShowVoiceStart = view.ShowVoiceStart;
        CanStartVoice = view.CanStartVoice;
        VoiceStartLabel = view.VoiceStartLabel;
        var disclosure = view.VoiceDisclosure;
        HasDisclosure = disclosure is not null;
        DisclosureTitle = disclosure?.Title ?? string.Empty;
        RegionNotice = disclosure?.RegionNotice ?? string.Empty;
        HasRegionNotice = RegionNotice.Length > 0;
        DisclosureBody = disclosure?.Body ?? string.Empty;
        AcceptLabel = disclosure?.AcceptLabel ?? string.Empty;
        DeclineLabel = disclosure?.DeclineLabel ?? string.Empty;
    }

    private void ShowCard(HqCardView? card)
    {
        HasCard = card is not null;
        CardTitle = card?.Title ?? string.Empty;
        CardSummary = card?.Summary ?? string.Empty;
        CardNote = card?.Note ?? string.Empty;
        HasCardNote = CardNote.Length > 0;
        IsApplying = card?.Applying is not null;
        ApplyingText = card?.Applying ?? string.Empty;
        CardFailure = Display.Failure(card?.Failure);
        HasCardFailure = CardFailure.Length > 0;
        ConfirmLabel = card?.ConfirmLabel ?? string.Empty;
        DismissLabel = card?.DismissLabel ?? string.Empty;
        CanConfirm = card?.CanConfirm ?? false;
        CanDismiss = card?.CanDismiss ?? false;
    }

    /// <summary>Forwards one of the screen's actions.</summary>
    internal void Send(HqAction action) => _context?.Send(new UiEvent.Hq(action));

    /// <summary>Whether <see cref="AskCommand"/> would send something: a prompt is typed, and one can be sent now.</summary>
    private bool MayAsk() => CanAsk && !string.IsNullOrWhiteSpace(Draft);

    /// <summary>Sends what is typed, trimmed, and empties the box. A blank prompt sends nothing.</summary>
    [RelayCommand(CanExecute = nameof(MayAsk))]
    private void Ask()
    {
        if (!MayAsk() || _context is null)
        {
            return;
        }
        var prompt = Draft.Trim();
        Draft = string.Empty;
        Send(new HqAction.Ask(prompt));
    }

    [RelayCommand(CanExecute = nameof(CanRetry))]
    private void Retry() => Send(new HqAction.Retry());

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm() => Send(new HqAction.Confirm());

    [RelayCommand(CanExecute = nameof(CanDismiss))]
    private void Dismiss() => Send(new HqAction.Dismiss());

    /// <summary>Start voice: the residency statement the first time in a workspace, the session after that.</summary>
    [RelayCommand(CanExecute = nameof(CanStartVoice))]
    private void StartVoice() => Send(new HqAction.StartVoice());

    /// <summary>The statement's Start voice: the session and the microphone start.</summary>
    [RelayCommand(CanExecute = nameof(HasDisclosure))]
    private void AcceptVoice() => Send(new HqAction.AcceptVoiceDisclosure());

    /// <summary>The statement's Not now: nothing starts.</summary>
    [RelayCommand(CanExecute = nameof(HasDisclosure))]
    private void DeclineVoice() => Send(new HqAction.DeclineVoiceDisclosure());

    /// <summary>A link in an answer was clicked: the core opens it, when it is a web page.</summary>
    internal void OpenLink(string url) => Send(new HqAction.OpenLink(url));

    /// <summary>What a report about an answer is about: its kind, which is all the support desk is told.</summary>
    internal static ReportTarget AnswerTarget => new ReportTarget.HqAnswer();
}
