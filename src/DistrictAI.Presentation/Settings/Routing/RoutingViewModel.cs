using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels.Calls;
using DistrictAI.ViewModels.Settings.CallHandling;
using DistrictAI.ViewModels.Settings.Persona;

namespace DistrictAI.ViewModels.Settings.Routing;

/// <summary>
/// One routing rule's card: its three pickers, its two boxes, its engine
/// (shown, not changed here) and what it keeps. A change is sent as it
/// happens; the boxes type ahead of the core and are written back only with a
/// value they did not send.
/// </summary>
public sealed partial class RoutingRuleItem : ObservableObject
{
    private readonly RoutingViewModel _owner;
    private readonly TextEcho _valueEcho = new();
    private readonly TextEcho _instructionEcho = new();
    private bool _writing;

    internal RoutingRuleItem(RoutingViewModel owner, uint index)
    {
        _owner = owner;
        Index = index;
    }

    /// <summary>Its position, which its edits and its removal name.</summary>
    public uint Index { get; }

    /// <summary>The caller detail's choices.</summary>
    public ObservableCollection<ChoiceItem> FieldChoices { get; } = [];

    /// <summary>The comparison's choices.</summary>
    public ObservableCollection<ChoiceItem> OperatorChoices { get; } = [];

    /// <summary>The voices.</summary>
    public ObservableCollection<ChoiceItem> VoiceChoices { get; } = [];

    /// <summary>"Rule 1".</summary>
    [ObservableProperty]
    public partial string Heading { get; set; } = string.Empty;

    /// <summary>What it matches, and the voice it gives.</summary>
    [ObservableProperty]
    public partial string Summary { get; set; } = string.Empty;

    /// <summary>The caller detail picker's label.</summary>
    [ObservableProperty]
    public partial string FieldLabel { get; set; } = string.Empty;

    /// <summary>The caller detail chosen.</summary>
    [ObservableProperty]
    public partial ChoiceItem? Field { get; set; }

    /// <summary>The comparison picker's label.</summary>
    [ObservableProperty]
    public partial string OperatorLabel { get; set; } = string.Empty;

    /// <summary>The comparison chosen.</summary>
    [ObservableProperty]
    public partial ChoiceItem? Operator { get; set; }

    /// <summary>What it is compared with, as typed.</summary>
    [ObservableProperty]
    public partial string Value { get; set; } = string.Empty;

    /// <summary>The voice picker's label.</summary>
    [ObservableProperty]
    public partial string VoiceLabel { get; set; } = string.Empty;

    /// <summary>The voice chosen.</summary>
    [ObservableProperty]
    public partial ChoiceItem? Voice { get; set; }

    /// <summary>What the receptionist is told, as typed.</summary>
    [ObservableProperty]
    public partial string Instruction { get; set; } = string.Empty;

    /// <summary>The engine, in words.</summary>
    [ObservableProperty]
    public partial string Engine { get; set; } = string.Empty;

    /// <summary>What it stores beyond the builder's keys, or empty.</summary>
    [ObservableProperty]
    public partial string Kept { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="Kept"/>.</summary>
    [ObservableProperty]
    public partial bool HasKept { get; set; }

    /// <summary>Whether it can be changed now.</summary>
    [ObservableProperty]
    public partial bool CanEdit { get; set; }

    /// <summary>The removal's name for screen readers: which rule it removes.</summary>
    public string RemoveName => "Remove " + Heading;

    partial void OnHeadingChanged(string value) => OnPropertyChanged(nameof(RemoveName));

    internal void Show(RoutingRuleView rule, bool canEdit)
    {
        _writing = true;
        try
        {
            Heading = rule.Heading;
            Summary = rule.Summary;
            CanEdit = canEdit;
            FieldLabel = rule.Field.Label;
            Field = Pick(FieldChoices, rule.Field.Picker);
            OperatorLabel = rule.Operator.Label;
            Operator = Pick(OperatorChoices, rule.Operator.Picker);
            VoiceLabel = rule.Voice.Label;
            Voice = Pick(VoiceChoices, rule.Voice.Picker);
            if (_valueEcho.Write(rule.Value, Value))
            {
                Value = rule.Value;
            }
            if (_instructionEcho.Write(rule.Instruction, Instruction))
            {
                Instruction = rule.Instruction;
            }
            Engine = rule.Engine;
            Kept = rule.Kept ?? string.Empty;
            HasKept = Kept.Length > 0;
        }
        finally
        {
            _writing = false;
        }
    }

    /// <summary>Puts <paramref name="picker"/>'s choices in <paramref name="choices"/>, and answers the chosen one.</summary>
    private static ChoiceItem? Pick(ObservableCollection<ChoiceItem> choices, PickerView picker)
    {
        Display.Sync(choices, ChoiceItem.For(picker));
        return choices.FirstOrDefault(choice => choice.Value == picker.Selected);
    }

    partial void OnFieldChanged(ChoiceItem? value) => Chose(RoutingField.Field, value);

    partial void OnOperatorChanged(ChoiceItem? value) => Chose(RoutingField.Operator, value);

    partial void OnVoiceChanged(ChoiceItem? value) => Chose(RoutingField.Voice, value);

    private void Chose(RoutingField field, ChoiceItem? value)
    {
        if (!_writing && value is not null)
        {
            _owner.Edit(Index, field, value.Value);
        }
    }

    partial void OnValueChanged(string value)
    {
        if (!_writing)
        {
            _valueEcho.Typed(value);
            _owner.Edit(Index, RoutingField.Value, value);
        }
    }

    partial void OnInstructionChanged(string value)
    {
        if (!_writing)
        {
            _instructionEcho.Typed(value);
            _owner.Edit(Index, RoutingField.Instruction, value);
        }
    }

    /// <summary>Takes this rule off the list (nothing is saved yet).</summary>
    [RelayCommand]
    private void Remove()
    {
        if (CanEdit)
        {
            _owner.Send(new RoutingAction.Remove(Index));
        }
    }
}

/// <summary>
/// The routing rules: which callers get which voice and instruction, in the
/// order they are checked. No list shows before the settings are read; the
/// save replaces every rule, so the core asks first, and its question is shown
/// as long as it asks.
/// </summary>
public sealed partial class RoutingViewModel : ObservableObject
{
    private PageContext? _context;

    /// <summary>The rules' cards, in order.</summary>
    public ObservableCollection<RoutingRuleItem> Rules { get; } = [];

    /// <summary>The core's view of the screen.</summary>
    [ObservableProperty]
    public partial RoutingView? View { get; set; }

    /// <summary>The heading.</summary>
    [ObservableProperty]
    public partial string Title { get; set; } = "Call routing rules";

    /// <summary>What it says under the heading.</summary>
    [ObservableProperty]
    public partial string Intro { get; set; } = string.Empty;

    /// <summary>Whether the settings are being read.</summary>
    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    /// <summary>Whether the list shows.</summary>
    [ObservableProperty]
    public partial bool IsReady { get; set; }

    /// <summary>The page's status heading (a failed read, a save not read back, or rules this app cannot edit), or empty.</summary>
    [ObservableProperty]
    public partial string StatusTitle { get; set; } = string.Empty;

    /// <summary>What the status says.</summary>
    [ObservableProperty]
    public partial string StatusBody { get; set; } = string.Empty;

    /// <summary>The status's button ("Try again", "Read them again"), or empty.</summary>
    [ObservableProperty]
    public partial string StatusAction { get; set; } = string.Empty;

    /// <summary>Whether there is a status.</summary>
    [ObservableProperty]
    public partial bool HasStatus { get; set; }

    /// <summary>Whether the status has a button.</summary>
    [ObservableProperty]
    public partial bool HasStatusAction { get; set; }

    /// <summary>The empty list's heading.</summary>
    [ObservableProperty]
    public partial string EmptyTitle { get; set; } = string.Empty;

    /// <summary>The empty list's text.</summary>
    [ObservableProperty]
    public partial string EmptyBody { get; set; } = string.Empty;

    /// <summary>Whether the list is read and empty.</summary>
    [ObservableProperty]
    public partial bool ShowEmpty { get; set; }

    /// <summary>Whether the rules can be changed now.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    public partial bool CanEdit { get; set; }

    /// <summary>Whether "Save" works.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial bool CanSave { get; set; }

    /// <summary>Whether a save is on its way.</summary>
    [ObservableProperty]
    public partial bool Saving { get; set; }

    /// <summary>How the last save ended, or empty.</summary>
    [ObservableProperty]
    public partial string Notice { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="Notice"/>.</summary>
    [ObservableProperty]
    public partial bool HasNotice { get; set; }

    /// <summary>Whether the notice says it saved.</summary>
    [ObservableProperty]
    public partial bool NoticeSaved { get; set; }

    /// <summary>The core's question before the save, while it asks.</summary>
    public QuestionView? Confirming { get; private set; }

    internal void Attach(PageContext context) => _context = context;

    internal void Show(RoutingView view)
    {
        View = view;
        Title = view.Title;
        Intro = view.Intro;
        IsLoading = view.Status is SectionStatus.Loading;
        (StatusTitle, StatusBody, StatusAction) = (view.Status, view.Unmodellable) switch
        {
            (SectionStatus.Failed failed, _) => (failed.Title, Display.Failure(failed.Failure), failed.Failure.Retryable ? "Try again" : string.Empty),
            (SectionStatus.Stale stale, _) => (stale.Title, stale.Body, stale.Action),
            (_, { } unmodellable) => (unmodellable.Title, unmodellable.Body, string.Empty),
            _ => (string.Empty, string.Empty, string.Empty),
        };
        HasStatus = StatusTitle.Length > 0;
        HasStatusAction = StatusAction.Length > 0;
        IsReady = view.Status is SectionStatus.Ready && view.Unmodellable is null;
        EmptyTitle = view.Empty?.Title ?? string.Empty;
        EmptyBody = view.Empty?.Body ?? string.Empty;
        ShowEmpty = IsReady && view.Empty is not null;
        CanEdit = view.CanEdit;
        CanSave = view.CanSave;
        Saving = view.Saving;
        (Notice, HasNotice, NoticeSaved) = CallHandlingViewModel.Words(view.Notice);
        Confirming = view.Confirming;
        // The cards are made again only when the number of rules changes, so a
        // box being typed in keeps its place.
        if (Rules.Count != view.Rules.Length)
        {
            Rules.Clear();
            foreach (var rule in view.Rules)
            {
                Rules.Add(new RoutingRuleItem(this, rule.Index));
            }
        }
        for (var i = 0; i < view.Rules.Length; i++)
        {
            Rules[i].Show(view.Rules[i], view.CanEdit);
        }
    }

    internal void Edit(uint index, RoutingField field, string value)
    {
        if (CanEdit)
        {
            Send(new RoutingAction.Edit(index, field, value));
        }
    }

    /// <summary>Adds a rule with the web console's defaults (nothing is saved yet).</summary>
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void Add()
    {
        if (CanEdit)
        {
            Send(new RoutingAction.Add());
        }
    }

    /// <summary>"Save": the core asks first. Turned off as it is pressed, so one press is one question.</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        if (!CanSave)
        {
            return;
        }
        CanSave = false;
        Send(new RoutingAction.Save());
    }

    /// <summary>The answer to the core's question.</summary>
    internal void Answer(bool confirmed) =>
        Send(confirmed ? new RoutingAction.ConfirmSave() : new RoutingAction.CancelSave());

    /// <summary>Puts the save's notice away.</summary>
    [RelayCommand]
    private void DismissNotice() => Send(new RoutingAction.DismissNotice());

    /// <summary>The status's button: reads the settings again.</summary>
    [RelayCommand]
    private void Retry() => _context?.Send(new UiEvent.Refresh());

    /// <summary>Forwards one of the screen's actions.</summary>
    internal void Send(RoutingAction action) => _context?.Send(new UiEvent.Routing(action));
}
