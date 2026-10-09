using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DistrictAI.Core;
using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels.Calls;

namespace DistrictAI.ViewModels.Desk;

/// <summary>
/// The help desk's settings and its logo, copied from the core's
/// <see cref="DeskSettingsView"/>. No form shows before the settings are read;
/// each switch and the name box send their change as it happens, and Save
/// sends only what changed. A picked logo is checked first
/// (<see cref="DistrictFfi.DeskLogoProblem"/>): a GIF, or anything else the
/// service does not host, or a file over the size limit, is refused here with
/// that sentence and never sent.
/// </summary>
public sealed partial class DeskSettingsViewModel : ObservableObject
{
    private readonly TextEcho _brandEcho = new();
    private PageContext? _context;
    private bool _writing;

    /// <summary>Why the last logo picked was refused before it was sent, or null.</summary>
    private string? _refusal;

    /// <summary>The core's logo note when <see cref="_refusal"/> was set, so a newer one replaces it.</summary>
    private string _noteAtRefusal = string.Empty;

    /// <summary>
    /// Why the service would not host a picked file as the logo, or null:
    /// district-ffi's own check. The tests, which load no native library,
    /// stand in for it.
    /// </summary>
    internal Func<PickedFileView, string?> LogoProblem { get; init; } = file => DistrictFfi.DeskLogoProblem(file);

    /// <summary>Loading, and a read that failed.</summary>
    public LoadStateViewModel Load { get; } = new();

    /// <summary>The core's view of the screen.</summary>
    [ObservableProperty]
    public partial DeskSettingsView? View { get; set; }

    /// <summary>The heading.</summary>
    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;

    /// <summary>What the settings say under their heading.</summary>
    [ObservableProperty]
    public partial string Intro { get; set; } = string.Empty;

    /// <summary>The "desk on" switch's label.</summary>
    [ObservableProperty]
    public partial string EnabledLabel { get; set; } = string.Empty;

    /// <summary>The note under the "desk on" switch.</summary>
    [ObservableProperty]
    public partial string EnabledNote { get; set; } = string.Empty;

    /// <summary>Whether the desk takes tickets, as the form has it.</summary>
    [ObservableProperty]
    public partial bool Enabled { get; set; }

    /// <summary>The "email customers" switch's label.</summary>
    [ObservableProperty]
    public partial string NotifyLabel { get; set; } = string.Empty;

    /// <summary>Whether customers are emailed replies, as the form has it.</summary>
    [ObservableProperty]
    public partial bool Notify { get; set; }

    /// <summary>The name box's label.</summary>
    [ObservableProperty]
    public partial string BrandLabel { get; set; } = string.Empty;

    /// <summary>The name customers see, as typed.</summary>
    [ObservableProperty]
    public partial string BrandName { get; set; } = string.Empty;

    /// <summary>Whether the form can be changed.</summary>
    [ObservableProperty]
    public partial bool CanEdit { get; set; }

    /// <summary>Whether "Save" works.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial bool CanSave { get; set; }

    /// <summary>Whether a save is on its way.</summary>
    [ObservableProperty]
    public partial bool Saving { get; set; }

    /// <summary>Why the last save failed, or empty.</summary>
    [ObservableProperty]
    public partial string SaveFailure { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="SaveFailure"/>.</summary>
    [ObservableProperty]
    public partial bool HasSaveFailure { get; set; }

    /// <summary>What the logo is and what the service takes.</summary>
    [ObservableProperty]
    public partial string LogoHelp { get; set; } = string.Empty;

    /// <summary>Whether there is a logo, in words.</summary>
    [ObservableProperty]
    public partial string LogoLine { get; set; } = string.Empty;

    /// <summary>Whether "Choose an image" works.</summary>
    [ObservableProperty]
    public partial bool CanChooseLogo { get; set; }

    /// <summary>Whether "Remove" shows: there is a logo.</summary>
    [ObservableProperty]
    public partial bool ShowRemoveLogo { get; set; }

    /// <summary>Whether "Remove" works.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveLogoCommand))]
    public partial bool CanRemoveLogo { get; set; }

    /// <summary>Whether a logo upload or removal is on its way.</summary>
    [ObservableProperty]
    public partial bool LogoBusy { get; set; }

    /// <summary>
    /// Why the last logo picked was refused, or the last logo change failed,
    /// or that a removed logo's file may still be reachable; or empty.
    /// </summary>
    [ObservableProperty]
    public partial string LogoNote { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="LogoNote"/>.</summary>
    [ObservableProperty]
    public partial bool HasLogoNote { get; set; }

    /// <summary>Whether the logo note says something went wrong (rather than a note about a removal).</summary>
    [ObservableProperty]
    public partial bool LogoNoteIsFailure { get; set; }

    internal void Attach(PageContext context)
    {
        _context = context;
        Load.Attach(context);
    }

    internal void Show(DeskSettingsView view)
    {
        View = view;
        Load.Show(view.Status, true, null, false, null);
        Title = view.Title;
        Intro = view.Intro;
        EnabledLabel = view.EnabledLabel;
        EnabledNote = view.EnabledNote;
        NotifyLabel = view.NotifyLabel;
        BrandLabel = view.BrandLabel;
        _writing = true;
        try
        {
            Enabled = view.Enabled;
            Notify = view.NotifyCustomersByEmail;
            if (_brandEcho.Write(view.BrandName, BrandName))
            {
                BrandName = view.BrandName;
            }
        }
        finally
        {
            _writing = false;
        }
        CanEdit = view.CanEdit;
        CanSave = view.CanSave;
        Saving = view.Saving;
        SaveFailure = Display.Failure(view.SaveFailure);
        HasSaveFailure = view.SaveFailure is not null;
        LogoHelp = view.LogoHelp;
        LogoLine = view.LogoLine;
        CanChooseLogo = view.CanChooseLogo;
        ShowRemoveLogo = view.ShowRemoveLogo;
        CanRemoveLogo = view.CanRemoveLogo;
        LogoBusy = view.LogoBusy;
        ShowLogoNote();
    }

    /// <summary>The core's own note on the logo: its failure, or that a removed logo's file was kept.</summary>
    private string CoreLogoNote() =>
        View?.LogoFailure is { } failure ? Display.Failure(failure) : View?.LogoFileKept ?? string.Empty;

    private void ShowLogoNote()
    {
        var core = CoreLogoNote();
        // A refusal here stands until the core says something newer about the
        // logo, or a change of it starts.
        if (_refusal is not null && (core != _noteAtRefusal || LogoBusy))
        {
            _refusal = null;
        }
        LogoNote = _refusal ?? core;
        HasLogoNote = LogoNote.Length > 0;
        LogoNoteIsFailure = _refusal is not null || View?.LogoFailure is not null;
    }

    partial void OnEnabledChanged(bool value)
    {
        if (!_writing)
        {
            Send(new DeskAction.SetEnabled(value));
        }
    }

    partial void OnNotifyChanged(bool value)
    {
        if (!_writing)
        {
            Send(new DeskAction.SetNotify(value));
        }
    }

    partial void OnBrandNameChanged(string value)
    {
        if (_writing)
        {
            return;
        }
        _brandEcho.Typed(value);
        Send(new DeskAction.EditBrandName(value));
    }

    /// <summary>Saves what changed: turned off as it is pressed, so one press is one save.</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        if (!CanSave)
        {
            return;
        }
        CanSave = false;
        Send(new DeskAction.SaveSettings());
    }

    /// <summary>Takes the logo down: turned off as it is pressed, so one press is one removal.</summary>
    [RelayCommand(CanExecute = nameof(CanRemoveLogo))]
    private void RemoveLogo()
    {
        if (!CanRemoveLogo)
        {
            return;
        }
        CanRemoveLogo = false;
        Send(new DeskAction.DeleteLogo());
    }

    /// <summary>Puts away the save's failure and the logo's note.</summary>
    [RelayCommand]
    private void DismissFailures()
    {
        _refusal = null;
        ShowLogoNote();
        Send(new DeskAction.DismissSettingsFailures());
    }

    /// <summary>Whether "Choose an image" may open the file chooser now.</summary>
    internal bool MayPickLogo => _context is not null && CanChooseLogo;

    /// <summary>
    /// What the file chooser returned: nothing when cancelled; else the first
    /// file, refused here when the service would not host it (a GIF, too
    /// large), reported as unreadable when it could not be read, and sent to
    /// the core otherwise.
    /// </summary>
    internal void PickedLogo(IReadOnlyList<PickedFile> picked)
    {
        if (!MayPickLogo || picked.Count == 0)
        {
            return;
        }
        if (picked[0].Data is not { } data)
        {
            _refusal = null;
            ShowLogoNote();
            Send(new DeskAction.LogoUnreadable());
            return;
        }
        if (LogoProblem(data) is { } problem)
        {
            _refusal = problem;
            _noteAtRefusal = CoreLogoNote();
            ShowLogoNote();
            return;
        }
        _refusal = null;
        ShowLogoNote();
        CanChooseLogo = false;
        Send(new DeskAction.UploadLogo(data));
    }

    /// <summary>Forwards one of the screen's actions.</summary>
    internal void Send(DeskAction action) => _context?.Send(new UiEvent.Desk(action));
}
