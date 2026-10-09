using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels.Settings.Persona;

namespace DistrictAI.ViewModels.Settings.VoiceStudio;

/// <summary>One recipe of the tier.</summary>
/// <param name="RecipeId">The recipe, to apply it by.</param>
/// <param name="Name">Its name.</param>
/// <param name="Description">What it is for.</param>
/// <param name="Facts">Its time to first word, where it is processed, its channel.</param>
/// <param name="Badge">"Default" on the service's default, else empty.</param>
/// <param name="Selected">Whether the held engine started from it.</param>
public sealed record StudioRecipeItem(string RecipeId, string Name, string Description, string Facts, string Badge, bool Selected)
{
    /// <summary>Whether it carries the default badge.</summary>
    public bool HasBadge => Badge.Length > 0;

    /// <summary>What a screen reader says for it.</summary>
    public string AccessibleName =>
        string.Join(". ", new[] { Name, Badge, Description, Facts }.Select(part => part.TrimEnd('.')).Where(part => part.Length > 0));

    internal static StudioRecipeItem From(StudioRecipeView recipe, string badge) =>
        new(recipe.RecipeId, recipe.Name, recipe.Description, recipe.Facts, recipe.IsDefault ? badge : string.Empty, recipe.Selected);

    /// <inheritdoc/>
    public override string ToString() => AccessibleName;
}

/// <summary>One leg of the signal chain.</summary>
/// <param name="Leg">The leg, to open the editor on it by.</param>
/// <param name="Title">The leg's name and its model.</param>
/// <param name="Detail">What it does, where, how fast.</param>
/// <param name="Open">Whether the editor is open on it.</param>
public sealed record StudioBlockItem(string Leg, string Title, string Detail, bool Open)
{
    /// <summary>What a screen reader says for it.</summary>
    public string AccessibleName => Title + ". " + Detail;

    internal static StudioBlockItem From(StudioBlockView block) => new(block.Leg, block.Title, block.Detail, block.Open);

    /// <inheritdoc/>
    public override string ToString() => AccessibleName;
}

/// <summary>
/// One picker of the editor (vendor, model, location or voice). It stays the
/// same object across the core's snapshots, so its box keeps focus; a choice
/// the member makes is sent, and one the snapshot makes is not.
/// </summary>
public sealed partial class StudioPickerItem : ObservableObject
{
    private readonly Action<StudioPickerItem, string> _pick;
    private bool _updating;

    internal StudioPickerItem(StudioPicker picker, Action<StudioPickerItem, string> pick)
    {
        Picker = picker;
        _pick = pick;
    }

    /// <summary>Which picker.</summary>
    public StudioPicker Picker { get; }

    /// <summary>The value held.</summary>
    public string Selected { get; private set; } = string.Empty;

    /// <summary>Its label, and its accessible name.</summary>
    [ObservableProperty]
    public partial string Label { get; set; } = string.Empty;

    /// <summary>What it offers; a held value the core does not list is first, as the settings kit shows one.</summary>
    public ObservableCollection<ChoiceItem> Options { get; } = [];

    /// <summary>The chosen option's index.</summary>
    [ObservableProperty]
    public partial int SelectedIndex { get; set; } = -1;

    internal void Update(StudioPickerView view)
    {
        _updating = true;
        try
        {
            Label = view.Label;
            Selected = view.Choices.Selected;
            Display.Sync(Options, ChoiceItem.For(view.Choices));
            SelectedIndex = IndexOf(Options, Selected);
        }
        finally
        {
            _updating = false;
        }
    }

    partial void OnSelectedIndexChanged(int value)
    {
        if (!_updating && value >= 0 && value < Options.Count && Options[value].Value != Selected)
        {
            _pick(this, Options[value].Value);
        }
    }

    internal static int IndexOf(IReadOnlyList<ChoiceItem> options, string value)
    {
        for (var i = 0; i < options.Count; i++)
        {
            if (options[i].Value == value)
            {
                return i;
            }
        }
        return -1;
    }
}

/// <summary>
/// One tuning control of the open leg: a slider (with its "use the default"
/// box), a select, a switch or key terms. It stays the same object across the
/// core's snapshots while its key does, so a slider being dragged or terms
/// being typed are never pulled out from under the member; what the member
/// changes is sent, what a snapshot writes is not.
/// </summary>
public sealed partial class StudioTuningItem : ObservableObject
{
    private readonly Action<VoiceStudioAction> _send;
    private bool _updating;
    private long? _heldValue;
    private long _start;
    private string _heldChoice = string.Empty;
    private bool _heldOn;
    private string _heldTerms = string.Empty;

    internal StudioTuningItem(string key, Action<VoiceStudioAction> send)
    {
        Key = key;
        _send = send;
    }

    /// <summary>The key it writes.</summary>
    public string Key { get; }

    /// <summary>Its label, and its accessible name.</summary>
    [ObservableProperty]
    public partial string Label { get; set; } = string.Empty;

    /// <summary>What it does, or empty.</summary>
    [ObservableProperty]
    public partial string Description { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="Description"/>.</summary>
    [ObservableProperty]
    public partial bool HasDescription { get; set; }

    /// <summary>A heading above it ("Interruptions"), or empty.</summary>
    [ObservableProperty]
    public partial string Heading { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="Heading"/>.</summary>
    [ObservableProperty]
    public partial bool HasHeading { get; set; }

    /// <summary>Whether it is a slider.</summary>
    [ObservableProperty]
    public partial bool IsSlider { get; set; }

    /// <summary>Whether it is a select.</summary>
    [ObservableProperty]
    public partial bool IsSelect { get; set; }

    /// <summary>Whether it is a switch.</summary>
    [ObservableProperty]
    public partial bool IsSwitch { get; set; }

    /// <summary>Whether it is key terms.</summary>
    [ObservableProperty]
    public partial bool IsLines { get; set; }

    /// <summary>The slider's lowest value.</summary>
    [ObservableProperty]
    public partial double Minimum { get; set; }

    /// <summary>The slider's highest value.</summary>
    [ObservableProperty]
    public partial double Maximum { get; set; }

    /// <summary>The slider's step.</summary>
    [ObservableProperty]
    public partial double Step { get; set; }

    /// <summary>The slider's value, or where it starts while the default is used.</summary>
    [ObservableProperty]
    public partial double Value { get; set; }

    /// <summary>The slider's value as words.</summary>
    [ObservableProperty]
    public partial string ValueText { get; set; } = string.Empty;

    /// <summary>The "use the default" box's label, or empty when the value cannot be unset.</summary>
    [ObservableProperty]
    public partial string DefaultLabel { get; set; } = string.Empty;

    /// <summary>Whether there is a "use the default" box.</summary>
    [ObservableProperty]
    public partial bool HasDefault { get; set; }

    /// <summary>Whether the service's default is used (the box ticked).</summary>
    [ObservableProperty]
    public partial bool UseDefault { get; set; }

    /// <summary>Whether the slider moves: it has a value of its own.</summary>
    [ObservableProperty]
    public partial bool SliderEnabled { get; set; }

    /// <summary>The select's choices.</summary>
    public ObservableCollection<ChoiceItem> Options { get; } = [];

    /// <summary>The select's chosen index.</summary>
    [ObservableProperty]
    public partial int SelectedIndex { get; set; } = -1;

    /// <summary>Whether the switch is on.</summary>
    [ObservableProperty]
    public partial bool On { get; set; }

    /// <summary>Key terms, one per line, as shown.</summary>
    [ObservableProperty]
    public partial string Text { get; set; } = string.Empty;

    /// <summary>Whether <paramref name="view"/> is drawn by a control of this kind.</summary>
    internal bool Fits(StudioTuningView view) => view.Key == Key && view.Control switch
    {
        StudioControlView.Slider => IsSlider,
        StudioControlView.Select => IsSelect,
        StudioControlView.Switch => IsSwitch,
        _ => IsLines,
    };

    internal void Update(StudioTuningView view)
    {
        _updating = true;
        try
        {
            Label = view.Label;
            Description = view.Description ?? string.Empty;
            HasDescription = Description.Length > 0;
            Heading = view.Heading ?? string.Empty;
            HasHeading = Heading.Length > 0;
            IsSlider = view.Control is StudioControlView.Slider;
            IsSelect = view.Control is StudioControlView.Select;
            IsSwitch = view.Control is StudioControlView.Switch;
            IsLines = view.Control is StudioControlView.Lines;
            switch (view.Control)
            {
                case StudioControlView.Slider slider:
                    _heldValue = slider.Value;
                    _start = slider.Start;
                    Minimum = slider.Min / Scale;
                    Maximum = slider.Max / Scale;
                    // A slider with no step of its own moves by hundredths.
                    Step = slider.Step > 0 ? slider.Step / Scale : 0.01;
                    Value = (slider.Value ?? slider.Start) / Scale;
                    ValueText = slider.ValueText;
                    DefaultLabel = slider.DefaultLabel ?? string.Empty;
                    HasDefault = DefaultLabel.Length > 0;
                    UseDefault = slider.Value is null;
                    SliderEnabled = slider.Value is not null || !HasDefault;
                    break;
                case StudioControlView.Select select:
                    _heldChoice = select.Selected;
                    Display.Sync(Options, [.. select.Options.Select(Choice)]);
                    SelectedIndex = StudioPickerItem.IndexOf(Options, select.Selected);
                    break;
                case StudioControlView.Switch flag:
                    _heldOn = flag.On;
                    On = flag.On;
                    break;
                case StudioControlView.Lines lines:
                    _heldTerms = lines.Text;
                    // What is being typed stays while it says the same as the
                    // terms held, so a new line or a space is not eaten.
                    if (Terms(Text) != lines.Text)
                    {
                        Text = lines.Text;
                    }
                    break;
            }
        }
        finally
        {
            _updating = false;
        }
    }

    internal static ChoiceItem Choice(ChoiceView choice) => new(choice.Value, choice.Label);

    /// <summary>The slider's numbers cross in thousandths (district-ffi's SLIDER_SCALE).</summary>
    private const double Scale = 1000.0;

    /// <summary>Key terms as the core reads them: one per line, trimmed, no blanks, no repeats.</summary>
    internal static string Terms(string text) =>
        string.Join('\n', text.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0).Distinct());

    partial void OnValueChanged(double value)
    {
        var thousandths = (long)Math.Round(value * Scale);
        if (!_updating && IsSlider && _heldValue is { } held && thousandths != held)
        {
            _send(new VoiceStudioAction.SetNumber(Key, thousandths));
        }
    }

    partial void OnUseDefaultChanged(bool value)
    {
        if (_updating || !HasDefault || value == (_heldValue is null))
        {
            return;
        }
        _send(new VoiceStudioAction.SetNumber(Key, value ? null : _start));
    }

    partial void OnSelectedIndexChanged(int value)
    {
        if (!_updating && value >= 0 && value < Options.Count && Options[value].Value != _heldChoice)
        {
            _send(new VoiceStudioAction.SetChoice(Key, Options[value].Value));
        }
    }

    partial void OnOnChanged(bool value)
    {
        if (!_updating && value != _heldOn)
        {
            _send(new VoiceStudioAction.SetFlag(Key, value));
        }
    }

    partial void OnTextChanged(string value)
    {
        if (!_updating && Terms(value) != _heldTerms)
        {
            _send(new VoiceStudioAction.SetLines(Key, value));
        }
    }
}
