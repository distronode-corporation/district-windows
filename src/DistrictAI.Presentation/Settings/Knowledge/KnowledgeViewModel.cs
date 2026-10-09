using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DistrictAI.Core;
using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels.Calls;

namespace DistrictAI.ViewModels.Settings.Knowledge;

/// <summary>One mode, as a choice.</summary>
/// <param name="Mode">Which.</param>
/// <param name="Label">Its name.</param>
/// <param name="Body">What it means.</param>
public sealed record ModeItem(KnowledgeModeChoice Mode, string Label, string Body)
{
    /// <inheritdoc/>
    public override string ToString() => Label;
}

/// <summary>One document, as its row shows it.</summary>
/// <param name="Id">Its id, which a delete names.</param>
/// <param name="Title">Its title.</param>
/// <param name="Line">Where it stands, how many pieces, when it was added, and where it was read from.</param>
/// <param name="Failed">Whether it failed, so the row says so plainly.</param>
/// <param name="ShowDelete">Whether its delete button shows.</param>
/// <param name="CanDelete">Whether that button works now.</param>
/// <param name="DeleteName">The button's name, with the document's title.</param>
public sealed record DocumentItem(string Id, string Title, string Line, bool Failed, bool ShowDelete, bool CanDelete, string DeleteName)
{
    internal static DocumentItem For(KnowledgeDocumentView document, KnowledgeView view) =>
        new(
            document.Id,
            document.Title,
            string.Join(" · ", new[]
            {
                document.StateLabel,
                document.Pieces ?? string.Empty,
                "Added " + Display.When(document.CreatedAt),
                document.SourceUrl ?? string.Empty,
            }.Where(part => part.Length > 0)),
            document.State == DocumentState.Failed,
            view.ShowDelete,
            view.CanDelete,
            $"{view.DeleteLabel}: {document.Title}");
}

/// <summary>
/// District Studio's Knowledge: where answers come from (switching to the
/// linked mode asks first), adding a document (billed by its length, sent
/// once when Add is pressed), filling the form from a plain text file, and
/// the documents, each deleted only after a question. A viewer reads it all
/// and is offered no control. Every word but a file's refusal is the core's.
/// </summary>
public sealed partial class KnowledgeViewModel : ObservableObject
{
    /// <summary>What a picked file that could not be read at all says.</summary>
    internal const string FileUnreadable = "This file could not be read.";

    private readonly TextEcho _titleEcho = new();
    private readonly TextEcho _contentEcho = new();
    private PageContext? _context;
    private bool _writing;
    private int _storedMode = -1;

    /// <summary>
    /// Why a picked file was refused, read here before anything reached the
    /// core (not text, too large): the core has no upload to refuse it.
    /// </summary>
    internal Func<PickedFileView, KnowledgeFileRead> FileReader { get; init; } = DistrictFfi.KnowledgeFile;

    /// <summary>The modes offered.</summary>
    public ObservableCollection<ModeItem> Modes { get; } = [];

    /// <summary>The documents, newest first.</summary>
    public ObservableCollection<DocumentItem> Documents { get; } = [];

    /// <summary>The core's view of the screen, as last shown.</summary>
    [ObservableProperty]
    public partial KnowledgeView? View { get; set; }

    /// <summary>The heading.</summary>
    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;

    /// <summary>For a viewer, that this is read only, or empty.</summary>
    [ObservableProperty]
    public partial string ViewerNote { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="ViewerNote"/>.</summary>
    [ObservableProperty]
    public partial bool HasViewerNote { get; set; }

    /// <summary>Whether the notice shows.</summary>
    [ObservableProperty]
    public partial bool HasNotice { get; set; }

    /// <summary>"Saved.", or why the last write failed.</summary>
    [ObservableProperty]
    public partial string NoticeText { get; set; } = string.Empty;

    /// <summary>Whether the notice is a success, rather than a failure.</summary>
    [ObservableProperty]
    public partial bool NoticeSaved { get; set; }

    /// <summary>The mode's heading.</summary>
    [ObservableProperty]
    public partial string ModeHeading { get; set; } = string.Empty;

    /// <summary>Whether the mode is being read.</summary>
    [ObservableProperty]
    public partial bool ModeLoading { get; set; }

    /// <summary>When the mode could not be read: that it cannot be changed now, and why.</summary>
    [ObservableProperty]
    public partial string ModeUnavailable { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="ModeUnavailable"/>.</summary>
    [ObservableProperty]
    public partial bool HasModeUnavailable { get; set; }

    /// <summary>Whether "Try again" is offered for the mode.</summary>
    [ObservableProperty]
    public partial bool CanRetryMode { get; set; }

    /// <summary>Whether the modes show.</summary>
    [ObservableProperty]
    public partial bool HasModes { get; set; }

    /// <summary>The stored mode's place in <see cref="Modes"/>, or -1.</summary>
    [ObservableProperty]
    public partial int SelectedModeIndex { get; set; } = -1;

    /// <summary>Whether a mode can be chosen now.</summary>
    [ObservableProperty]
    public partial bool CanChangeMode { get; set; }

    /// <summary>The heading over a stored mode this app does not know.</summary>
    [ObservableProperty]
    public partial string ModeUnknownTitle { get; set; } = string.Empty;

    /// <summary>That mode, as stored, or empty.</summary>
    [ObservableProperty]
    public partial string ModeUnknown { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="ModeUnknown"/>.</summary>
    [ObservableProperty]
    public partial bool HasModeUnknown { get; set; }

    /// <summary>Whether the add form shows.</summary>
    [ObservableProperty]
    public partial bool ShowAdd { get; set; }

    /// <summary>The add form's heading.</summary>
    [ObservableProperty]
    public partial string AddHeading { get; set; } = string.Empty;

    /// <summary>That adding is billed by length and sent once.</summary>
    [ObservableProperty]
    public partial string AddBilled { get; set; } = string.Empty;

    /// <summary>The title box's label.</summary>
    [ObservableProperty]
    public partial string TitleLabel { get; set; } = string.Empty;

    /// <summary>The new document's title, as typed.</summary>
    [ObservableProperty]
    public partial string DraftTitle { get; set; } = string.Empty;

    /// <summary>The text box's label.</summary>
    [ObservableProperty]
    public partial string ContentLabel { get; set; } = string.Empty;

    /// <summary>The new document's text, as typed.</summary>
    [ObservableProperty]
    public partial string DraftContent { get; set; } = string.Empty;

    /// <summary>Whether the boxes can be typed in.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ChooseFileCommand))]
    public partial bool DraftEditable { get; set; }

    /// <summary>Why the last Add was refused, or empty.</summary>
    [ObservableProperty]
    public partial string AddRejected { get; set; } = string.Empty;

    /// <summary>Whether there is an <see cref="AddRejected"/>.</summary>
    [ObservableProperty]
    public partial bool HasAddRejected { get; set; }

    /// <summary>Whether an add is on its way.</summary>
    [ObservableProperty]
    public partial bool Adding { get; set; }

    /// <summary>Whether Add can be pressed.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    public partial bool AddEnabled { get; set; }

    /// <summary>The add button's words.</summary>
    [ObservableProperty]
    public partial string AddLabel { get; set; } = string.Empty;

    /// <summary>The button that reads a text file into the form.</summary>
    [ObservableProperty]
    public partial string FileLabel { get; set; } = string.Empty;

    /// <summary>What it takes.</summary>
    [ObservableProperty]
    public partial string FileHint { get; set; } = string.Empty;

    /// <summary>Why the last file picked was refused, or empty.</summary>
    [ObservableProperty]
    public partial string FileRefusal { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="FileRefusal"/>.</summary>
    [ObservableProperty]
    public partial bool HasFileRefusal { get; set; }

    /// <summary>The documents' heading.</summary>
    [ObservableProperty]
    public partial string DocumentsHeading { get; set; } = string.Empty;

    /// <summary>Whether the documents are being read.</summary>
    [ObservableProperty]
    public partial bool DocumentsLoading { get; set; }

    /// <summary>When the documents could not be read, the heading.</summary>
    [ObservableProperty]
    public partial string DocumentsFailedTitle { get; set; } = string.Empty;

    /// <summary>Why.</summary>
    [ObservableProperty]
    public partial string DocumentsFailure { get; set; } = string.Empty;

    /// <summary>Whether the documents could not be read.</summary>
    [ObservableProperty]
    public partial bool HasDocumentsFailure { get; set; }

    /// <summary>Whether "Try again" is offered for the documents.</summary>
    [ObservableProperty]
    public partial bool CanRetryDocuments { get; set; }

    /// <summary>Whether the list is read and empty.</summary>
    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    /// <summary>The empty list's heading.</summary>
    [ObservableProperty]
    public partial string EmptyTitle { get; set; } = string.Empty;

    /// <summary>The empty list's text.</summary>
    [ObservableProperty]
    public partial string EmptyBody { get; set; } = string.Empty;

    /// <summary>Whether the question before a write is showing.</summary>
    [ObservableProperty]
    public partial bool ConfirmOpen { get; set; }

    /// <summary>The question's heading.</summary>
    [ObservableProperty]
    public partial string ConfirmTitle { get; set; } = string.Empty;

    /// <summary>What the write does.</summary>
    [ObservableProperty]
    public partial string ConfirmBody { get; set; } = string.Empty;

    /// <summary>The confirming button.</summary>
    [ObservableProperty]
    public partial string ConfirmAction { get; set; } = string.Empty;

    /// <summary>The button that answers no.</summary>
    [ObservableProperty]
    public partial string ConfirmCancel { get; set; } = string.Empty;

    /// <summary>Whether the write destroys something (a delete).</summary>
    [ObservableProperty]
    public partial bool ConfirmDestructive { get; set; }

    internal void Attach(PageContext context) => _context = context;

    internal void Show(KnowledgeView view)
    {
        View = view;
        Title = view.Title;
        ViewerNote = view.ViewerNote ?? string.Empty;
        HasViewerNote = ViewerNote.Length > 0;
        HasNotice = view.Notice is not null;
        NoticeText = view.Notice?.Message ?? string.Empty;
        NoticeSaved = view.Notice?.Saved ?? false;
        ShowMode(view);
        ShowAdd = view.ShowAdd;
        AddHeading = view.AddHeading;
        AddBilled = view.AddBilled;
        TitleLabel = view.TitleLabel;
        ContentLabel = view.ContentLabel;
        ShowDraft(view.DraftTitle, view.DraftContent);
        DraftEditable = view.DraftEditable;
        AddRejected = view.AddRejected ?? string.Empty;
        HasAddRejected = AddRejected.Length > 0;
        Adding = view.Adding;
        AddEnabled = view.AddEnabled;
        AddLabel = view.AddLabel;
        FileLabel = view.FileLabel;
        FileHint = view.FileHint;
        ShowDocuments(view);
        ConfirmOpen = view.Confirm is not null;
        ConfirmTitle = view.Confirm?.Title ?? string.Empty;
        ConfirmBody = view.Confirm?.Body ?? string.Empty;
        ConfirmAction = view.Confirm?.Action ?? string.Empty;
        ConfirmCancel = view.Confirm?.CancelLabel ?? string.Empty;
        ConfirmDestructive = view.Confirm?.Destructive ?? false;
    }

    private void ShowMode(KnowledgeView view)
    {
        ModeHeading = view.ModeHeading;
        ModeLoading = view.ModeLoading;
        ModeUnavailable = view.ModeUnavailable is { } unavailable
            ? unavailable + Environment.NewLine + Display.Failure(view.ModeFailure)
            : string.Empty;
        HasModeUnavailable = ModeUnavailable.Length > 0;
        CanRetryMode = view.ModeFailure?.Retryable ?? false;
        ModeUnknownTitle = view.ModeUnknownTitle;
        ModeUnknown = view.ModeUnknown ?? string.Empty;
        HasModeUnknown = ModeUnknown.Length > 0;
        CanChangeMode = view.CanChangeMode;
        _writing = true;
        try
        {
            Display.Sync(Modes, [.. view.Modes.Select(mode => new ModeItem(mode.Mode, mode.Label, mode.Body))]);
            HasModes = Modes.Count > 0;
            _storedMode = Array.FindIndex(view.Modes, mode => mode.Selected);
            // Written back even when unchanged, so a choice the core did not
            // take (a question answered Cancel) is undone on screen.
            SelectedModeIndex = _storedMode;
            OnPropertyChanged(nameof(SelectedModeIndex));
        }
        finally
        {
            _writing = false;
        }
    }

    private void ShowDraft(string title, string content)
    {
        _writing = true;
        try
        {
            if (_titleEcho.Write(title, DraftTitle))
            {
                DraftTitle = title;
            }
            if (_contentEcho.Write(content, DraftContent))
            {
                DraftContent = content;
            }
        }
        finally
        {
            _writing = false;
        }
    }

    private void ShowDocuments(KnowledgeView view)
    {
        DocumentsHeading = view.DocumentsHeading;
        DocumentsLoading = view.DocumentsLoading;
        DocumentsFailedTitle = view.DocumentsFailedTitle ?? string.Empty;
        DocumentsFailure = Display.Failure(view.DocumentsFailure);
        HasDocumentsFailure = DocumentsFailedTitle.Length > 0;
        CanRetryDocuments = view.DocumentsFailure?.Retryable ?? false;
        IsEmpty = view.Empty is not null;
        EmptyTitle = view.Empty?.Title ?? string.Empty;
        EmptyBody = view.Empty?.Body ?? string.Empty;
        Display.Sync(Documents, [.. view.Documents.Select(document => DocumentItem.For(document, view))]);
    }

    partial void OnSelectedModeIndexChanged(int value)
    {
        if (!_writing && value >= 0 && value < Modes.Count && value != _storedMode)
        {
            Send(new KnowledgeAction.SelectMode(Modes[value].Mode));
        }
    }

    partial void OnDraftTitleChanged(string value)
    {
        if (_writing)
        {
            return;
        }
        _titleEcho.Typed(value);
        ClearFileRefusal();
        Send(new KnowledgeAction.EditTitle(value));
    }

    partial void OnDraftContentChanged(string value)
    {
        if (_writing)
        {
            return;
        }
        _contentEcho.Typed(value);
        ClearFileRefusal();
        Send(new KnowledgeAction.EditContent(value));
    }

    private void ClearFileRefusal()
    {
        FileRefusal = string.Empty;
        HasFileRefusal = false;
    }

    /// <summary>Whether "Read a text file" may open the file chooser now.</summary>
    internal bool MayPickFile => _context is not null && ShowAdd && DraftEditable;

    /// <summary>
    /// What the file chooser returned: nothing when cancelled; else the file,
    /// refused here when it is not plain text, is too large, or could not be
    /// read, and otherwise put in the form (its name as the title when the
    /// title is empty). Nothing is added until Add.
    /// </summary>
    internal void FilePicked(IReadOnlyList<PickedFile> picked)
    {
        if (!MayPickFile || picked.Count == 0)
        {
            return;
        }
        var read = picked[0].Data is { } data ? FileReader(data) : new KnowledgeFileRead.Refused(FileUnreadable);
        if (read is KnowledgeFileRead.Text text)
        {
            if (DraftTitle.Trim().Length == 0 && text.Title.Length > 0)
            {
                DraftTitle = text.Title;
            }
            DraftContent = text.Content;
            ClearFileRefusal();
        }
        else if (read is KnowledgeFileRead.Refused refused)
        {
            FileRefusal = refused.Reason;
            HasFileRefusal = true;
        }
    }

    /// <summary>The delete button of the document <paramref name="id"/> was pressed: the core asks first.</summary>
    internal void AskDelete(string id)
    {
        if (View?.CanDelete == true)
        {
            Send(new KnowledgeAction.AskDelete(id));
        }
    }

    /// <summary>The question was answered: yes or no.</summary>
    internal void Answer(bool yes) => Send(yes ? new KnowledgeAction.Confirm() : new KnowledgeAction.Cancel());

    /// <summary>Forwards one of the screen's actions.</summary>
    internal void Send(KnowledgeAction action) => _context?.Send(new UiEvent.Knowledge(action));

    [RelayCommand(CanExecute = nameof(AddEnabled))]
    private void Add()
    {
        ClearFileRefusal();
        Send(new KnowledgeAction.Add());
    }

    /// <summary>Asks the page to open the file chooser; the page hands the result to <see cref="FilePicked"/>.</summary>
    [RelayCommand(CanExecute = nameof(DraftEditable))]
    private void ChooseFile() => FileRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>"Read a text file" was pressed: the page opens the Windows file chooser.</summary>
    public event EventHandler? FileRequested;

    [RelayCommand]
    private void DismissNotice() => Send(new KnowledgeAction.DismissNotice());

    /// <summary>Reads the section again: after a failed read of the documents or the mode.</summary>
    [RelayCommand]
    private void Retry() => _context?.Send(new UiEvent.Refresh());
}
