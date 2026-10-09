using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DistrictAI.Core.Ffi;

namespace DistrictAI.ViewModels.Settings.Tools;

/// <summary>
/// One tool's switch. It shows the core's value, and a flip by the member is
/// sent to the core; the core's next view decides what it shows.
/// </summary>
public sealed partial class ToolItem : ObservableObject
{
    private readonly ToolsViewModel _owner;
    private bool _writing;

    internal ToolItem(ToolsViewModel owner, string id)
    {
        _owner = owner;
        Id = id;
    }

    /// <summary>The tool's id, as stored.</summary>
    public string Id { get; }

    /// <summary>Its name, or its id when the core has none for it.</summary>
    [ObservableProperty]
    public partial string Label { get; set; } = string.Empty;

    /// <summary>A line under the switch, or empty.</summary>
    [ObservableProperty]
    public partial string Note { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="Note"/>.</summary>
    [ObservableProperty]
    public partial bool HasNote { get; set; }

    /// <summary>Whether the switch is on.</summary>
    [ObservableProperty]
    public partial bool IsOn { get; set; }

    /// <summary>Whether the switch works now.</summary>
    [ObservableProperty]
    public partial bool IsEnabled { get; set; }

    /// <summary>Shows <paramref name="row"/>, writing the switch back even when unchanged, so a flip the core did not take is undone.</summary>
    internal void Show(ToolRowView row, bool editable)
    {
        Label = row.Label;
        Note = row.Note ?? string.Empty;
        HasNote = Note.Length > 0;
        IsEnabled = editable;
        _writing = true;
        try
        {
            IsOn = row.Enabled;
            OnPropertyChanged(nameof(IsOn));
        }
        finally
        {
            _writing = false;
        }
    }

    partial void OnIsOnChanged(bool value)
    {
        if (!_writing)
        {
            _owner.Send(new ToolsAction.Toggle(Id, value));
        }
    }
}

/// <summary>
/// District Studio's Skills: a switch for every tool the core lists, saved
/// together as the list read with the switches applied, and outside research
/// on contacts, a switch of its own saved alone. Every word is the core's.
/// </summary>
public sealed partial class ToolsViewModel : ObservableObject
{
    private PageContext? _context;
    private bool _writing;

    /// <summary>The tools' switches, in the core's order.</summary>
    public ObservableCollection<ToolItem> Tools { get; } = [];

    /// <summary>The core's view of the screen, as last shown.</summary>
    [ObservableProperty]
    public partial ToolsView? View { get; set; }

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

    /// <summary>The tools' heading.</summary>
    [ObservableProperty]
    public partial string ToolsHeading { get; set; } = string.Empty;

    /// <summary>What the tools are, and what saving them does.</summary>
    [ObservableProperty]
    public partial string ToolsNote { get; set; } = string.Empty;

    /// <summary>Whether the switches work.</summary>
    [ObservableProperty]
    public partial bool Editable { get; set; }

    /// <summary>Whether the tools' save notice shows.</summary>
    [ObservableProperty]
    public partial bool HasToolsNotice { get; set; }

    /// <summary>"Saved.", or why the tools save failed.</summary>
    [ObservableProperty]
    public partial string ToolsNoticeText { get; set; } = string.Empty;

    /// <summary>Whether that notice is a save, rather than a failure.</summary>
    [ObservableProperty]
    public partial bool ToolsNoticeSaved { get; set; }

    /// <summary>Whether the tools save is on its way.</summary>
    [ObservableProperty]
    public partial bool ToolsSaving { get; set; }

    /// <summary>Whether the tools' Save works.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveToolsCommand))]
    public partial bool CanSaveTools { get; set; }

    /// <summary>The tools' save button's words.</summary>
    [ObservableProperty]
    public partial string SaveToolsLabel { get; set; } = string.Empty;

    /// <summary>The research part's heading.</summary>
    [ObservableProperty]
    public partial string ResearchHeading { get; set; } = string.Empty;

    /// <summary>The research switch's title.</summary>
    [ObservableProperty]
    public partial string ResearchTitle { get; set; } = string.Empty;

    /// <summary>What it does.</summary>
    [ObservableProperty]
    public partial string ResearchBody { get; set; } = string.Empty;

    /// <summary>Whether research is on.</summary>
    [ObservableProperty]
    public partial bool ResearchOn { get; set; }

    /// <summary>Whether the research save notice shows.</summary>
    [ObservableProperty]
    public partial bool HasResearchNotice { get; set; }

    /// <summary>"Saved.", or why the research save failed.</summary>
    [ObservableProperty]
    public partial string ResearchNoticeText { get; set; } = string.Empty;

    /// <summary>Whether that notice is a save, rather than a failure.</summary>
    [ObservableProperty]
    public partial bool ResearchNoticeSaved { get; set; }

    /// <summary>Whether the research save is on its way.</summary>
    [ObservableProperty]
    public partial bool ResearchSaving { get; set; }

    /// <summary>Whether the research Save works.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveResearchCommand))]
    public partial bool CanSaveResearch { get; set; }

    /// <summary>The research save button's words.</summary>
    [ObservableProperty]
    public partial string SaveResearchLabel { get; set; } = string.Empty;

    internal void Attach(PageContext context) => _context = context;

    internal void Show(ToolsView view)
    {
        View = view;
        Title = view.Title;
        ShowStatus(view.Status);
        ToolsHeading = view.ToolsHeading;
        ToolsNote = view.ToolsNote;
        Editable = view.Editable;
        ShowTools(view.Tools, view.Editable);
        HasToolsNotice = view.ToolsNotice is not null;
        ToolsNoticeText = view.ToolsNotice?.Message ?? string.Empty;
        ToolsNoticeSaved = view.ToolsNotice?.Saved ?? false;
        ToolsSaving = view.ToolsSaving;
        CanSaveTools = view.CanSaveTools;
        SaveToolsLabel = view.SaveToolsLabel;
        ResearchHeading = view.ResearchHeading;
        ResearchTitle = view.ResearchTitle;
        ResearchBody = view.ResearchBody;
        _writing = true;
        try
        {
            ResearchOn = view.ResearchEnabled;
            OnPropertyChanged(nameof(ResearchOn));
        }
        finally
        {
            _writing = false;
        }
        HasResearchNotice = view.ResearchNotice is not null;
        ResearchNoticeText = view.ResearchNotice?.Message ?? string.Empty;
        ResearchNoticeSaved = view.ResearchNotice?.Saved ?? false;
        ResearchSaving = view.ResearchSaving;
        CanSaveResearch = view.CanSaveResearch;
        SaveResearchLabel = view.SaveResearchLabel;
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

    /// <summary>The switches for <paramref name="rows"/>: built again only when the tools listed change, so focus stays on a switch.</summary>
    private void ShowTools(ToolRowView[] rows, bool editable)
    {
        if (!Tools.Select(tool => tool.Id).SequenceEqual(rows.Select(row => row.Id)))
        {
            Tools.Clear();
            foreach (var row in rows)
            {
                Tools.Add(new ToolItem(this, row.Id));
            }
        }
        for (var i = 0; i < rows.Length; i++)
        {
            Tools[i].Show(rows[i], editable);
        }
    }

    partial void OnResearchOnChanged(bool value)
    {
        if (!_writing)
        {
            Send(new ToolsAction.SetResearch(value));
        }
    }

    /// <summary>Forwards one of the screen's actions.</summary>
    internal void Send(ToolsAction action) => _context?.Send(new UiEvent.Tools(action));

    [RelayCommand(CanExecute = nameof(CanSaveTools))]
    private void SaveTools() => Send(new ToolsAction.SaveTools());

    [RelayCommand(CanExecute = nameof(CanSaveResearch))]
    private void SaveResearch() => Send(new ToolsAction.SaveResearch());

    [RelayCommand]
    private void DismissNotices() => Send(new ToolsAction.DismissNotices());

    /// <summary>Reads the section again: after a failed read, or a save not read back.</summary>
    [RelayCommand]
    private void Retry() => _context?.Send(new UiEvent.Refresh());
}
