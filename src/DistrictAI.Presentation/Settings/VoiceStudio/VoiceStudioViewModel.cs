using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DistrictAI.Core.Ffi;

namespace DistrictAI.ViewModels.Settings.VoiceStudio;

/// <summary>
/// Voice Studio, District Studio's Voice: the recipes of the Stable or Latest
/// tier, the signal chain with the editor of the leg it is open on, the time
/// to first word, where the call is processed, and the save. Every word but
/// the heading and a failed read is the service's, as the core hands it over.
/// The core holds the edits; each control sends what the member changes and
/// nothing a snapshot writes. Leaving with edits not saved is asked about by
/// the window ("Discard your changes?"), from the core.
/// </summary>
public sealed partial class VoiceStudioViewModel : ObservableObject
{
    private PageContext? _context;
    private string _tier = string.Empty;
    private bool _updating;

    /// <summary>The core's view of the screen.</summary>
    [ObservableProperty]
    public partial VoiceStudioView? View { get; set; }

    /// <summary>The heading, "Voice".</summary>
    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;

    /// <summary>Loading, and a read that failed.</summary>
    public LoadStateViewModel Load { get; } = new();

    /// <summary>Whether the Studio is read and shown.</summary>
    [ObservableProperty]
    public partial bool HasStudio { get; set; }

    /// <summary>The line under the heading.</summary>
    [ObservableProperty]
    public partial string Description { get; set; } = string.Empty;

    /// <summary>Whether the controls work (read, nothing on its way, a role that may change it).</summary>
    [ObservableProperty]
    public partial bool Editable { get; set; }

    /// <summary>The tier switch's label.</summary>
    [ObservableProperty]
    public partial string TierLabel { get; set; } = string.Empty;

    /// <summary>What the tiers mean.</summary>
    [ObservableProperty]
    public partial string TierDescription { get; set; } = string.Empty;

    /// <summary>Stable and Latest.</summary>
    public ObservableCollection<StudioChoiceItem> Tiers { get; } = [];

    /// <summary>The tier shown, as an index of <see cref="Tiers"/>.</summary>
    [ObservableProperty]
    public partial int TierIndex { get; set; } = -1;

    /// <summary>The recipes' heading.</summary>
    [ObservableProperty]
    public partial string RecipesLabel { get; set; } = string.Empty;

    /// <summary>This tier's recipes.</summary>
    public ObservableCollection<StudioRecipeItem> Recipes { get; } = [];

    /// <summary>"Based on Fastest, 2 changes.", or empty.</summary>
    [ObservableProperty]
    public partial string BasedOn { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="BasedOn"/> line, and so a Reset.</summary>
    [ObservableProperty]
    public partial bool HasBasedOn { get; set; }

    /// <summary>"Reset".</summary>
    [ObservableProperty]
    public partial string ResetLabel { get; set; } = string.Empty;

    /// <summary>The chain's heading.</summary>
    [ObservableProperty]
    public partial string ChainLabel { get; set; } = string.Empty;

    /// <summary>The legs of the held engine.</summary>
    public ObservableCollection<StudioBlockItem> Blocks { get; } = [];

    /// <summary>The editor's heading: the open leg's name.</summary>
    [ObservableProperty]
    public partial string EditorTitle { get; set; } = string.Empty;

    /// <summary>The open leg's pickers, the voice last.</summary>
    public ObservableCollection<StudioPickerItem> Pickers { get; } = [];

    /// <summary>The open leg's tuning shown with its pickers.</summary>
    public ObservableCollection<StudioTuningItem> Controls { get; } = [];

    /// <summary>The open leg's tuning behind Advanced.</summary>
    public ObservableCollection<StudioTuningItem> AdvancedControls { get; } = [];

    /// <summary>"Advanced".</summary>
    [ObservableProperty]
    public partial string AdvancedLabel { get; set; } = string.Empty;

    /// <summary>Whether anything is behind Advanced.</summary>
    [ObservableProperty]
    public partial bool HasAdvanced { get; set; }

    /// <summary>The meter's heading.</summary>
    [ObservableProperty]
    public partial string MeterHeading { get; set; } = string.Empty;

    /// <summary>What the meter measures.</summary>
    [ObservableProperty]
    public partial string MeterDescription { get; set; } = string.Empty;

    /// <summary>"About 970 ms".</summary>
    [ObservableProperty]
    public partial string MeterHeadline { get; set; } = string.Empty;

    /// <summary>Why the sum is "at least", or empty.</summary>
    [ObservableProperty]
    public partial string MeterNote { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="MeterNote"/>.</summary>
    [ObservableProperty]
    public partial bool HasMeterNote { get; set; }

    /// <summary>Each stage with its number.</summary>
    public ObservableCollection<FactItem> Stages { get; } = [];

    /// <summary>Where the numbers come from.</summary>
    [ObservableProperty]
    public partial string MeterSource { get; set; } = string.Empty;

    /// <summary>"Where the call is processed".</summary>
    [ObservableProperty]
    public partial string ResidencyHeading { get; set; } = string.Empty;

    /// <summary>The residency sentence.</summary>
    [ObservableProperty]
    public partial string ResidencyText { get; set; } = string.Empty;

    /// <summary>One line per leg that leaves the region, or empty.</summary>
    [ObservableProperty]
    public partial string LegsOut { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="LegsOut"/>.</summary>
    [ObservableProperty]
    public partial bool HasLegsOut { get; set; }

    /// <summary>How the last save went, or empty.</summary>
    [ObservableProperty]
    public partial string Notice { get; set; } = string.Empty;

    /// <summary>Whether the notice says it saved.</summary>
    [ObservableProperty]
    public partial bool NoticeSaved { get; set; }

    /// <summary>Whether the notice says it saved and was not read back.</summary>
    [ObservableProperty]
    public partial bool NoticeWarning { get; set; }

    /// <summary>Whether the notice says it did not save.</summary>
    [ObservableProperty]
    public partial bool NoticeError { get; set; }

    /// <summary>"Unsaved changes" or "All changes saved".</summary>
    [ObservableProperty]
    public partial string Pending { get; set; } = string.Empty;

    /// <summary>The save button's label.</summary>
    [ObservableProperty]
    public partial string SaveLabel { get; set; } = string.Empty;

    /// <summary>Whether Save works.</summary>
    [ObservableProperty]
    public partial bool CanSave { get; set; }

    /// <summary>Whether a save is on its way.</summary>
    [ObservableProperty]
    public partial bool Saving { get; set; }

    internal void Attach(PageContext context)
    {
        _context = context;
        Load.Attach(context);
    }

    internal void Show(VoiceStudioView view)
    {
        View = view;
        Title = view.Title;
        Load.Show(view.Status, hasRows: true, empty: null, refreshing: false, refreshFailure: null);
        var studio = view.Studio;
        HasStudio = view.Status is LoadStatus.Ready && studio is not null;
        if (studio is null)
        {
            Editable = false;
            CanSave = false;
            Saving = false;
            return;
        }
        _updating = true;
        try
        {
            Description = studio.Description;
            Editable = studio.Editable;
            TierLabel = studio.TierLabel;
            TierDescription = studio.TierDescription;
            _tier = studio.Tier;
            Display.Sync(Tiers, [.. studio.Tiers.Select(StudioChoiceItem.From)]);
            TierIndex = StudioPickerItem.IndexOf(Tiers, studio.Tier);
        }
        finally
        {
            _updating = false;
        }
        RecipesLabel = studio.RecipesLabel;
        Display.Sync(Recipes, [.. studio.Recipes.Select(recipe => StudioRecipeItem.From(recipe, studio.DefaultBadge))]);
        BasedOn = studio.BasedOn ?? string.Empty;
        HasBasedOn = BasedOn.Length > 0;
        ResetLabel = studio.ResetLabel;
        ChainLabel = studio.ChainLabel;
        Display.Sync(Blocks, [.. studio.Blocks.Select(StudioBlockItem.From)]);

        var editor = studio.Editor;
        EditorTitle = editor.Title;
        Keep(Pickers, editor.Pickers, (item, picker) => item.Picker == picker.Picker, picker => new StudioPickerItem(picker.Picker, Pick), (item, picker) => item.Update(picker));
        Keep(Controls, [.. editor.Controls.Where(control => !control.Advanced)], (item, control) => item.Fits(control), Tuning, (item, control) => item.Update(control));
        Keep(AdvancedControls, [.. editor.Controls.Where(control => control.Advanced)], (item, control) => item.Fits(control), Tuning, (item, control) => item.Update(control));
        AdvancedLabel = editor.AdvancedLabel;
        HasAdvanced = editor.HasAdvanced;

        var meter = studio.Meter;
        MeterHeading = meter.Heading;
        MeterDescription = meter.Description;
        MeterHeadline = meter.Headline;
        MeterNote = meter.Note ?? string.Empty;
        HasMeterNote = MeterNote.Length > 0;
        Display.Sync(Stages, [.. meter.Stages.Select(FactItem.From)]);
        MeterSource = meter.Source;

        ResidencyHeading = studio.Residency.Heading;
        ResidencyText = studio.Residency.Text;
        LegsOut = string.Join(Environment.NewLine, studio.Residency.LegsOut);
        HasLegsOut = LegsOut.Length > 0;

        Notice = studio.Notice?.Text ?? string.Empty;
        NoticeSaved = studio.Notice?.Tone == StudioNoticeTone.Success;
        NoticeWarning = studio.Notice?.Tone == StudioNoticeTone.Warning;
        NoticeError = studio.Notice?.Tone == StudioNoticeTone.Error;
        Pending = studio.Pending;
        SaveLabel = studio.SaveLabel;
        CanSave = studio.CanSave;
        Saving = studio.Saving;
    }

    /// <summary>
    /// Makes <paramref name="target"/> show <paramref name="source"/>, keeping
    /// each item that still <paramref name="fits"/> its place, so a control
    /// the member is using is not made again under them.
    /// </summary>
    private static void Keep<TItem, TView>(
        ObservableCollection<TItem> target,
        TView[] source,
        Func<TItem, TView, bool> fits,
        Func<TView, TItem> make,
        Action<TItem, TView> update)
    {
        for (var i = 0; i < source.Length; i++)
        {
            if (i >= target.Count)
            {
                target.Add(make(source[i]));
            }
            else if (!fits(target[i], source[i]))
            {
                target[i] = make(source[i]);
            }
            update(target[i], source[i]);
        }
        while (target.Count > source.Length)
        {
            target.RemoveAt(target.Count - 1);
        }
    }

    private StudioTuningItem Tuning(StudioTuningView control) => new(control.Key, Edit);

    /// <summary>Sends an edit, while the Studio may be changed.</summary>
    private void Edit(VoiceStudioAction action)
    {
        if (Editable)
        {
            Send(action);
        }
    }

    private void Pick(StudioPickerItem picker, string value) => Edit(new VoiceStudioAction.Pick(picker.Picker, value));

    partial void OnTierIndexChanged(int value)
    {
        if (!_updating && value >= 0 && value < Tiers.Count && Tiers[value].Value != _tier)
        {
            Edit(new VoiceStudioAction.SelectTier(Tiers[value].Value));
        }
    }

    /// <summary>Forwards one of the screen's actions.</summary>
    internal void Send(VoiceStudioAction action) => _context?.Send(new UiEvent.VoiceStudio(action));

    /// <summary>Applies <paramref name="recipe"/>.</summary>
    internal void ApplyRecipe(StudioRecipeItem recipe) => Edit(new VoiceStudioAction.ApplyRecipe(recipe.RecipeId));

    /// <summary>Opens the editor on <paramref name="block"/>'s leg; allowed while a save is on its way.</summary>
    internal void SelectLeg(StudioBlockItem block)
    {
        if (HasStudio && !block.Open)
        {
            Send(new VoiceStudioAction.SelectLeg(block.Leg));
        }
    }

    [RelayCommand]
    private void Reset() => Edit(new VoiceStudioAction.Reset());

    [RelayCommand]
    private void Save()
    {
        if (!CanSave)
        {
            return;
        }
        // Until the core's next snapshot: one press, one save.
        CanSave = false;
        Send(new VoiceStudioAction.Save());
    }

    [RelayCommand]
    private void DismissNotice() => Send(new VoiceStudioAction.DismissNotice());
}
