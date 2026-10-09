using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels.Calls;
using DistrictAI.ViewModels.Settings.CallHandling;

namespace DistrictAI.ViewModels.Settings.Directory;

/// <summary>
/// One entry of the transfer directory: its heading and line as the core
/// gives them, and its two boxes, which type ahead of the core and are written
/// back only with a value they did not send. Nothing here keeps or writes a
/// number anywhere but the box.
/// </summary>
public sealed partial class DirectoryEntryItem : ObservableObject
{
    private readonly DirectoryViewModel _owner;
    private readonly TextEcho _nameEcho = new();
    private readonly TextEcho _numberEcho = new();
    private bool _writing;

    internal DirectoryEntryItem(DirectoryViewModel owner, uint index)
    {
        _owner = owner;
        Index = index;
    }

    /// <summary>Its position, which its edits and its removal name.</summary>
    public uint Index { get; }

    /// <summary>Its heading: the name, or "No name".</summary>
    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;

    /// <summary>The line under it: the number grouped for reading, or that it has none.</summary>
    [ObservableProperty]
    public partial string Line { get; set; } = string.Empty;

    /// <summary>The name, as typed.</summary>
    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    /// <summary>The number, as typed.</summary>
    [ObservableProperty]
    public partial string PhoneNumber { get; set; } = string.Empty;

    /// <summary>Whether it can be changed now.</summary>
    [ObservableProperty]
    public partial bool CanEdit { get; set; }

    /// <summary>The name box's name for screen readers.</summary>
    public string NameLabel => "Name of " + Title;

    /// <summary>The number box's name for screen readers.</summary>
    public string NumberLabel => "Phone number of " + Title;

    /// <summary>The removal's name for screen readers.</summary>
    public string RemoveName => "Remove " + Title + " from the directory";

    partial void OnTitleChanged(string value)
    {
        OnPropertyChanged(nameof(NameLabel));
        OnPropertyChanged(nameof(NumberLabel));
        OnPropertyChanged(nameof(RemoveName));
    }

    internal void Show(DirectoryEntryView entry, bool canEdit)
    {
        _writing = true;
        try
        {
            Title = entry.Title;
            Line = entry.Line;
            CanEdit = canEdit;
            if (_nameEcho.Write(entry.Name, Name))
            {
                Name = entry.Name;
            }
            if (_numberEcho.Write(entry.PhoneNumber, PhoneNumber))
            {
                PhoneNumber = entry.PhoneNumber;
            }
        }
        finally
        {
            _writing = false;
        }
    }

    partial void OnNameChanged(string value)
    {
        if (!_writing)
        {
            _nameEcho.Typed(value);
            _owner.Edit(Index, DirectoryEntryField.Name, value);
        }
    }

    partial void OnPhoneNumberChanged(string value)
    {
        if (!_writing)
        {
            _numberEcho.Typed(value);
            _owner.Edit(Index, DirectoryEntryField.PhoneNumber, value);
        }
    }

    /// <summary>Takes this entry off the list (nothing is saved yet).</summary>
    [RelayCommand]
    private void Remove()
    {
        if (CanEdit)
        {
            _owner.Send(new DirectoryAction.Remove(Index));
        }
    }
}

/// <summary>
/// The transfer directory: who the receptionist can put a live caller through
/// to. No list shows before the settings are read; an entry being added needs
/// a name and a number; the save replaces the whole directory, so the core
/// asks first, and its question is shown as long as it asks.
/// </summary>
public sealed partial class DirectoryViewModel : ObservableObject
{
    private readonly TextEcho _newNameEcho = new();
    private readonly TextEcho _newNumberEcho = new();
    private PageContext? _context;
    private bool _writing;

    /// <summary>The entries, in order.</summary>
    public ObservableCollection<DirectoryEntryItem> Entries { get; } = [];

    /// <summary>The core's view of the screen.</summary>
    [ObservableProperty]
    public partial DirectoryView? View { get; set; }

    /// <summary>The heading.</summary>
    [ObservableProperty]
    public partial string Title { get; set; } = "Transfer directory";

    /// <summary>What it says under the heading.</summary>
    [ObservableProperty]
    public partial string Intro { get; set; } = string.Empty;

    /// <summary>Whether the settings are being read.</summary>
    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    /// <summary>Whether the list shows.</summary>
    [ObservableProperty]
    public partial bool IsReady { get; set; }

    /// <summary>The page's status heading (a failed read, a save not read back, or a directory this app cannot edit), or empty.</summary>
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

    /// <summary>How many entries lack a name or a number, or empty.</summary>
    [ObservableProperty]
    public partial string Incomplete { get; set; } = string.Empty;

    /// <summary>Whether there is an <see cref="Incomplete"/>.</summary>
    [ObservableProperty]
    public partial bool HasIncomplete { get; set; }

    /// <summary>The new entry's name, as typed.</summary>
    [ObservableProperty]
    public partial string NewName { get; set; } = string.Empty;

    /// <summary>The new entry's number, as typed.</summary>
    [ObservableProperty]
    public partial string NewPhoneNumber { get; set; } = string.Empty;

    /// <summary>Why the last "Add" did nothing, or empty.</summary>
    [ObservableProperty]
    public partial string AddRejected { get; set; } = string.Empty;

    /// <summary>Whether there is an <see cref="AddRejected"/>.</summary>
    [ObservableProperty]
    public partial bool HasAddRejected { get; set; }

    /// <summary>Whether the list can be changed now.</summary>
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

    internal void Show(DirectoryView view)
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
        Incomplete = view.Incomplete ?? string.Empty;
        HasIncomplete = Incomplete.Length > 0;
        AddRejected = view.AddRejected ?? string.Empty;
        HasAddRejected = AddRejected.Length > 0;
        CanEdit = view.CanEdit;
        CanSave = view.CanSave;
        Saving = view.Saving;
        (Notice, HasNotice, NoticeSaved) = CallHandlingViewModel.Words(view.Notice);
        Confirming = view.Confirming;
        _writing = true;
        try
        {
            if (_newNameEcho.Write(view.NewName, NewName))
            {
                NewName = view.NewName;
            }
            if (_newNumberEcho.Write(view.NewPhoneNumber, NewPhoneNumber))
            {
                NewPhoneNumber = view.NewPhoneNumber;
            }
        }
        finally
        {
            _writing = false;
        }
        // The rows are made again only when the number of entries changes, so
        // a box being typed in keeps its place.
        if (Entries.Count != view.Entries.Length)
        {
            Entries.Clear();
            foreach (var entry in view.Entries)
            {
                Entries.Add(new DirectoryEntryItem(this, entry.Index));
            }
        }
        for (var i = 0; i < view.Entries.Length; i++)
        {
            Entries[i].Show(view.Entries[i], view.CanEdit);
        }
    }

    internal void Edit(uint index, DirectoryEntryField field, string value)
    {
        if (CanEdit)
        {
            Send(new DirectoryAction.Edit(index, field, value));
        }
    }

    partial void OnNewNameChanged(string value)
    {
        if (!_writing)
        {
            _newNameEcho.Typed(value);
            Send(new DirectoryAction.EditNewName(value));
        }
    }

    partial void OnNewPhoneNumberChanged(string value)
    {
        if (!_writing)
        {
            _newNumberEcho.Typed(value);
            Send(new DirectoryAction.EditNewPhoneNumber(value));
        }
    }

    /// <summary>Adds the new entry to the list (nothing is saved yet); the core refuses one without a name and a number.</summary>
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void Add()
    {
        if (CanEdit)
        {
            Send(new DirectoryAction.Add());
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
        Send(new DirectoryAction.Save());
    }

    /// <summary>The answer to the core's question.</summary>
    internal void Answer(bool confirmed) =>
        Send(confirmed ? new DirectoryAction.ConfirmSave() : new DirectoryAction.CancelSave());

    /// <summary>Puts the save's notice away.</summary>
    [RelayCommand]
    private void DismissNotice() => Send(new DirectoryAction.DismissNotice());

    /// <summary>The status's button: reads the settings again.</summary>
    [RelayCommand]
    private void Retry() => _context?.Send(new UiEvent.Refresh());

    /// <summary>Forwards one of the screen's actions.</summary>
    internal void Send(DirectoryAction action) => _context?.Send(new UiEvent.Directory(action));
}
