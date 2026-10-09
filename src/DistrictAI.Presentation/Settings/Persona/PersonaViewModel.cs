using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels.Calls;

namespace DistrictAI.ViewModels.Settings.Persona;

/// <summary>One choice of a picker whose choices come from the core.</summary>
/// <param name="Value">What is sent when it is chosen.</param>
/// <param name="Label">What it reads.</param>
public sealed record ChoiceItem(string Value, string Label)
{
    /// <inheritdoc/>
    public override string ToString() => Label;

    /// <summary>
    /// The picker's choices, with the chosen value first when the choices do
    /// not list it, so a stored value shows as it is stored.
    /// </summary>
    internal static List<ChoiceItem> For(PickerView picker)
    {
        var choices = picker.Choices.Select(choice => new ChoiceItem(choice.Value, choice.Label)).ToList();
        if (!choices.Any(choice => choice.Value == picker.Selected))
        {
            choices.Insert(0, new ChoiceItem(picker.Selected, picker.SelectedLabel));
        }
        return choices;
    }
}

/// <summary>
/// District Studio's Persona: the receptionist's texts, its language and
/// answer length as the core offers them, Save and its notice, and the
/// audition dialog (a real, billed call). Every word is the core's; the boxes
/// type ahead of it and are written back only with a value they did not send.
/// </summary>
public sealed partial class PersonaViewModel : ObservableObject
{
    private readonly Dictionary<PersonaField, TextEcho> _echoes = new()
    {
        [PersonaField.Name] = new TextEcho(),
        [PersonaField.Greeting] = new TextEcho(),
        [PersonaField.Personality] = new TextEcho(),
    };

    private PageContext? _context;
    private bool _writing;

    /// <summary>The languages the stored engine speaks.</summary>
    public ObservableCollection<ChoiceItem> Languages { get; } = [];

    /// <summary>The answer lengths it offers.</summary>
    public ObservableCollection<ChoiceItem> ResponseLengths { get; } = [];

    /// <summary>The core's view of the screen, as last shown.</summary>
    [ObservableProperty]
    public partial PersonaView? View { get; set; }

    /// <summary>The heading.</summary>
    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;

    /// <summary>Whether the section is being read.</summary>
    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    /// <summary>Whether the form shows.</summary>
    [ObservableProperty]
    public partial bool IsReady { get; set; }

    /// <summary>Whether a status shows instead of the form: a failed read, or a save not read back.</summary>
    [ObservableProperty]
    public partial bool HasStatus { get; set; }

    /// <summary>The status's heading.</summary>
    [ObservableProperty]
    public partial string StatusTitle { get; set; } = string.Empty;

    /// <summary>The status's text.</summary>
    [ObservableProperty]
    public partial string StatusBody { get; set; } = string.Empty;

    /// <summary>The status's button: "Try again", or "Read them again" after a save not read back.</summary>
    [ObservableProperty]
    public partial string StatusAction { get; set; } = string.Empty;

    /// <summary>Whether the status has its button.</summary>
    [ObservableProperty]
    public partial bool HasStatusAction { get; set; }

    /// <summary>The texts' heading.</summary>
    [ObservableProperty]
    public partial string TextsHeading { get; set; } = string.Empty;

    /// <summary>The receptionist's name, as typed.</summary>
    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    /// <summary>Its opening line, as typed.</summary>
    [ObservableProperty]
    public partial string Greeting { get; set; } = string.Empty;

    /// <summary>How it behaves, as typed.</summary>
    [ObservableProperty]
    public partial string Personality { get; set; } = string.Empty;

    /// <summary>Whether the texts can be edited.</summary>
    [ObservableProperty]
    public partial bool TextEditable { get; set; }

    /// <summary>The line under the texts.</summary>
    [ObservableProperty]
    public partial string ClearHint { get; set; } = string.Empty;

    /// <summary>The language half's heading.</summary>
    [ObservableProperty]
    public partial string EngineHeading { get; set; } = string.Empty;

    /// <summary>Whether the language half shows (both reads in).</summary>
    [ObservableProperty]
    public partial bool HasEngine { get; set; }

    /// <summary>The stored engine's name.</summary>
    [ObservableProperty]
    public partial string EngineLabel { get; set; } = string.Empty;

    /// <summary>Where the engine and voice are changed.</summary>
    [ObservableProperty]
    public partial string StudioHint { get; set; } = string.Empty;

    /// <summary>The button that opens Voice Studio.</summary>
    [ObservableProperty]
    public partial string StudioLabel { get; set; } = string.Empty;

    /// <summary>The language chosen.</summary>
    [ObservableProperty]
    public partial ChoiceItem? SelectedLanguage { get; set; }

    /// <summary>The answer length chosen.</summary>
    [ObservableProperty]
    public partial ChoiceItem? SelectedResponseLength { get; set; }

    /// <summary>Whether the language half can be edited.</summary>
    [ObservableProperty]
    public partial bool EngineEditable { get; set; }

    /// <summary>Why the language half is not shown, or empty.</summary>
    [ObservableProperty]
    public partial string EngineNote { get; set; } = string.Empty;

    /// <summary>Whether there is an <see cref="EngineNote"/>.</summary>
    [ObservableProperty]
    public partial bool HasEngineNote { get; set; }

    /// <summary>Whether "Try again" is offered for the options.</summary>
    [ObservableProperty]
    public partial bool CanRetryOptions { get; set; }

    /// <summary>Fitting the voice chain to a new language, or empty.</summary>
    [ObservableProperty]
    public partial string RefitLine { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="RefitLine"/>.</summary>
    [ObservableProperty]
    public partial bool HasRefitLine { get; set; }

    /// <summary>Whether the save notice shows.</summary>
    [ObservableProperty]
    public partial bool HasNotice { get; set; }

    /// <summary>"Saved.", or why the save failed.</summary>
    [ObservableProperty]
    public partial string NoticeText { get; set; } = string.Empty;

    /// <summary>Whether the notice is a save, rather than a failure.</summary>
    [ObservableProperty]
    public partial bool NoticeSaved { get; set; }

    /// <summary>Whether a save is on its way.</summary>
    [ObservableProperty]
    public partial bool Saving { get; set; }

    /// <summary>Whether Save works.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial bool CanSave { get; set; }

    /// <summary>The save button's words.</summary>
    [ObservableProperty]
    public partial string SaveLabel { get; set; } = string.Empty;

    /// <summary>Whether "Try this receptionist" shows.</summary>
    [ObservableProperty]
    public partial bool CanPreview { get; set; }

    /// <summary>Whether it can be pressed now.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenPreviewCommand))]
    public partial bool PreviewEnabled { get; set; }

    /// <summary>Its words.</summary>
    [ObservableProperty]
    public partial string PreviewLabel { get; set; } = string.Empty;

    /// <summary>Whether the audition dialog is open.</summary>
    [ObservableProperty]
    public partial bool AuditionOpen { get; set; }

    /// <summary>The dialog's heading.</summary>
    [ObservableProperty]
    public partial string AuditionTitle { get; set; } = string.Empty;

    /// <summary>What the audition is, said before it starts: a real, billed call.</summary>
    [ObservableProperty]
    public partial string BilledNote { get; set; } = string.Empty;

    /// <summary>Where it stands, or empty.</summary>
    [ObservableProperty]
    public partial string AuditionState { get; set; } = string.Empty;

    /// <summary>Whether there is an <see cref="AuditionState"/>.</summary>
    [ObservableProperty]
    public partial bool HasAuditionState { get; set; }

    /// <summary>Whether it is being started or joined.</summary>
    [ObservableProperty]
    public partial bool AuditionWaiting { get; set; }

    /// <summary>Why it could not start, or empty.</summary>
    [ObservableProperty]
    public partial string AuditionFailure { get; set; } = string.Empty;

    /// <summary>Whether there is an <see cref="AuditionFailure"/>.</summary>
    [ObservableProperty]
    public partial bool HasAuditionFailure { get; set; }

    /// <summary>Whether Start shows.</summary>
    [ObservableProperty]
    public partial bool ShowStart { get; set; }

    /// <summary>Whether Start works now.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartPreviewCommand))]
    public partial bool CanStart { get; set; }

    /// <summary>Whether Stop shows.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StopPreviewCommand))]
    public partial bool ShowStop { get; set; }

    /// <summary>The line while another audition must wait, or empty.</summary>
    [ObservableProperty]
    public partial string Cooling { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="Cooling"/> line.</summary>
    [ObservableProperty]
    public partial bool HasCooling { get; set; }

    /// <summary>Start's words.</summary>
    [ObservableProperty]
    public partial string StartLabel { get; set; } = string.Empty;

    /// <summary>Stop's words.</summary>
    [ObservableProperty]
    public partial string StopLabel { get; set; } = string.Empty;

    internal void Attach(PageContext context) => _context = context;

    internal void Show(PersonaView view)
    {
        View = view;
        Title = view.Title;
        ShowStatus(view.Status);
        TextsHeading = view.TextsHeading;
        ShowText(PersonaField.Name, view.Name);
        ShowText(PersonaField.Greeting, view.Greeting);
        ShowText(PersonaField.Personality, view.Personality);
        TextEditable = view.TextEditable;
        ClearHint = view.ClearHint;
        EngineHeading = view.EngineHeading;
        ShowEngine(view.Engine);
        EngineEditable = view.EngineEditable;
        EngineNote = view.EngineNote ?? string.Empty;
        HasEngineNote = EngineNote.Length > 0;
        CanRetryOptions = view.CanRetryOptions;
        RefitLine = view.RefitLine ?? string.Empty;
        HasRefitLine = RefitLine.Length > 0;
        HasNotice = view.Notice is not null;
        NoticeText = view.Notice?.Message ?? string.Empty;
        NoticeSaved = view.Notice?.Saved ?? false;
        Saving = view.Saving;
        CanSave = view.CanSave;
        SaveLabel = view.SaveLabel;
        CanPreview = view.CanPreview;
        PreviewEnabled = view.PreviewEnabled;
        PreviewLabel = view.PreviewLabel;
        ShowAudition(view.Audition);
    }

    private void ShowStatus(SectionStatus status)
    {
        IsLoading = status is SectionStatus.Loading;
        IsReady = status is SectionStatus.Ready;
        (StatusTitle, StatusBody, StatusAction) = status switch
        {
            SectionStatus.Failed failed => (failed.Title, Display.Failure(failed.Failure), failed.Failure.Retryable ? "Try again" : string.Empty),
            SectionStatus.Stale stale => (stale.Title, stale.Body, stale.Action),
            _ => (string.Empty, string.Empty, string.Empty),
        };
        HasStatus = StatusTitle.Length > 0;
        HasStatusAction = StatusAction.Length > 0;
    }

    private void ShowText(PersonaField field, string value)
    {
        var shown = field switch
        {
            PersonaField.Name => Name,
            PersonaField.Greeting => Greeting,
            _ => Personality,
        };
        if (!_echoes[field].Write(value, shown))
        {
            return;
        }
        _writing = true;
        try
        {
            switch (field)
            {
                case PersonaField.Name:
                    Name = value;
                    break;
                case PersonaField.Greeting:
                    Greeting = value;
                    break;
                default:
                    Personality = value;
                    break;
            }
        }
        finally
        {
            _writing = false;
        }
    }

    private void ShowEngine(PersonaEngineView? engine)
    {
        HasEngine = engine is not null;
        EngineLabel = engine?.EngineLabel ?? string.Empty;
        StudioHint = engine?.StudioHint ?? string.Empty;
        StudioLabel = engine?.StudioLabel ?? string.Empty;
        _writing = true;
        try
        {
            SelectedLanguage = Pick(Languages, engine?.Language);
            SelectedResponseLength = Pick(ResponseLengths, engine?.ResponseLength);
        }
        finally
        {
            _writing = false;
        }
    }

    /// <summary>Puts <paramref name="picker"/>'s choices in <paramref name="choices"/>, and answers the chosen one.</summary>
    private static ChoiceItem? Pick(ObservableCollection<ChoiceItem> choices, PickerView? picker)
    {
        Display.Sync(choices, picker is null ? [] : ChoiceItem.For(picker));
        return picker is null ? null : choices.FirstOrDefault(choice => choice.Value == picker.Selected);
    }

    private void ShowAudition(AuditionView? audition)
    {
        AuditionOpen = audition is not null;
        AuditionTitle = audition?.Title ?? string.Empty;
        BilledNote = audition?.BilledNote ?? string.Empty;
        AuditionState = audition?.State ?? string.Empty;
        HasAuditionState = AuditionState.Length > 0;
        AuditionWaiting = audition?.Waiting ?? false;
        AuditionFailure = Display.Failure(audition?.Failure);
        HasAuditionFailure = AuditionFailure.Length > 0;
        ShowStart = audition?.ShowStart ?? false;
        CanStart = audition?.CanStart ?? false;
        ShowStop = audition?.ShowStop ?? false;
        Cooling = audition?.Cooling ?? string.Empty;
        HasCooling = Cooling.Length > 0;
        StartLabel = audition?.StartLabel ?? string.Empty;
        StopLabel = audition?.StopLabel ?? string.Empty;
    }

    partial void OnNameChanged(string value) => Typed(PersonaField.Name, value);

    partial void OnGreetingChanged(string value) => Typed(PersonaField.Greeting, value);

    partial void OnPersonalityChanged(string value) => Typed(PersonaField.Personality, value);

    private void Typed(PersonaField field, string value)
    {
        if (_writing)
        {
            return;
        }
        _echoes[field].Typed(value);
        Send(new PersonaAction.EditText(field, value));
    }

    partial void OnSelectedLanguageChanged(ChoiceItem? value)
    {
        if (!_writing && value is not null)
        {
            Send(new PersonaAction.ChooseLanguage(value.Value));
        }
    }

    partial void OnSelectedResponseLengthChanged(ChoiceItem? value)
    {
        if (!_writing && value is not null)
        {
            Send(new PersonaAction.ChooseResponseLength(value.Value));
        }
    }

    /// <summary>Forwards one of the screen's actions.</summary>
    internal void Send(PersonaAction action) => _context?.Send(new UiEvent.Persona(action));

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save() => Send(new PersonaAction.Save());

    [RelayCommand]
    private void DismissNotice() => Send(new PersonaAction.DismissSaveNotice());

    [RelayCommand]
    private void OpenStudio() => Send(new PersonaAction.OpenVoiceStudio());

    /// <summary>Reads the section again: after a failed read, a save not read back, or options that failed.</summary>
    [RelayCommand]
    private void Retry() => _context?.Send(new UiEvent.Refresh());

    [RelayCommand(CanExecute = nameof(PreviewEnabled))]
    private void OpenPreview() => Send(new PersonaAction.OpenPreview());

    [RelayCommand(CanExecute = nameof(CanStart))]
    private void StartPreview() => Send(new PersonaAction.StartPreview());

    [RelayCommand(CanExecute = nameof(ShowStop))]
    private void StopPreview() => Send(new PersonaAction.StopPreview());

    /// <summary>The dialog was closed by the member: any audition stops.</summary>
    internal void ClosePreview() => Send(new PersonaAction.ClosePreview());
}
