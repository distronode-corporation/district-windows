using DistrictAI.Core;
using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels.Settings.Knowledge;
using Xunit;

namespace DistrictAI.Presentation.Tests.Settings.Knowledge;

public sealed class KnowledgeViewModelTests
{
    private static readonly KnowledgeModeChoiceView[] _modes =
    [
        new(KnowledgeModeChoice.Internal, "Your own knowledge base", "Answers come only from the documents added here.", true),
        new(KnowledgeModeChoice.Linked, "Linked support knowledge base", "Callers' questions are sent to Atlassian.", false),
    ];

    private static KnowledgeDocumentView Document(
        string id = "doc-1",
        string title = "Refund policy",
        DocumentState state = DocumentState.Ready,
        string stateLabel = "Ready",
        string? pieces = "4 pieces",
        string? url = null) =>
        new(id, title, state, stateLabel, pieces, "2026-08-15T14:30:00.000Z", url);

    private static KnowledgeView View(
        string? viewerNote = null,
        SaveNoticeView? notice = null,
        bool modeLoading = false,
        string? modeUnavailable = null,
        FailureView? modeFailure = null,
        KnowledgeModeChoiceView[]? modes = null,
        string? modeUnknown = null,
        bool canChangeMode = true,
        bool showAdd = true,
        string draftTitle = "",
        string draftContent = "",
        bool draftEditable = true,
        string? addRejected = null,
        bool adding = false,
        bool addEnabled = true,
        bool documentsLoading = false,
        string? documentsFailedTitle = null,
        FailureView? documentsFailure = null,
        KnowledgeDocumentView[]? documents = null,
        EmptyView? empty = null,
        bool showDelete = true,
        bool canDelete = true,
        KnowledgeConfirmView? confirm = null) =>
        new(
            Title: "Knowledge",
            ViewerNote: viewerNote,
            Notice: notice,
            ModeHeading: "Where answers come from",
            ModeLoading: modeLoading,
            ModeUnavailable: modeUnavailable,
            ModeFailure: modeFailure,
            Modes: modes ?? _modes,
            ModeUnknown: modeUnknown,
            ModeUnknownTitle: "A setting this app does not know",
            CanChangeMode: canChangeMode,
            ShowAdd: showAdd,
            AddHeading: "Add a document",
            AddBilled: "Adding a document is billed by its length.",
            TitleLabel: "Title",
            DraftTitle: draftTitle,
            ContentLabel: "The document's text",
            DraftContent: draftContent,
            DraftEditable: draftEditable,
            AddRejected: addRejected,
            Adding: adding,
            AddEnabled: addEnabled,
            AddLabel: "Add document",
            FileLabel: "Read a text file",
            FileHint: "A plain text file.",
            DocumentsHeading: "Documents",
            DocumentsLoading: documentsLoading,
            DocumentsFailedTitle: documentsFailedTitle,
            DocumentsFailure: documentsFailure,
            Documents: documents ?? [Document(), Document("doc-2", "Service area", DocumentState.Processing, "Processing", null, "https://example.com/area")],
            Empty: empty,
            ShowDelete: showDelete,
            CanDelete: canDelete,
            DeleteLabel: "Delete this document",
            Confirm: confirm);

    /// <summary>
    /// Stands in for district-ffi's <c>knowledge_file</c> (its own tests pin the
    /// real rules): a PDF's first bytes are refused as not text, a file Windows
    /// says is over 1 MB as too large, and anything else is its name less the
    /// extension and its text.
    /// </summary>
    private static KnowledgeFileRead Read(PickedFileView file)
    {
        if (file.Size > 1024 * 1024)
        {
            return new KnowledgeFileRead.Refused("The file must be 1 MB or smaller.");
        }
        var text = System.Text.Encoding.UTF8.GetString(file.Bytes);
        return text.StartsWith("%PDF-", StringComparison.Ordinal)
            ? new KnowledgeFileRead.Refused("Only a plain text file (.txt or .md) can be read in.")
            : new KnowledgeFileRead.Text(Path.GetFileNameWithoutExtension(file.FileName), text.Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    private static (KnowledgeViewModel Model, RecordingSink Sink) Attached(Func<PickedFileView, KnowledgeFileRead>? reader = null)
    {
        var model = new KnowledgeViewModel { FileReader = reader ?? Read };
        var (context, sink) = Pages.Context();
        model.Attach(context);
        return (model, sink);
    }

    private static PickedFile File(string name, string text) =>
        new(name, new PickedFileView(name, (ulong)text.Length, System.Text.Encoding.UTF8.GetBytes(text)));

    [Fact]
    public void ThePageShowsTheCoreValues()
    {
        var (model, sink) = Attached();
        var view = View();
        model.Show(view);

        Assert.Same(view, model.View);
        Assert.Equal("Knowledge", model.Title);
        Assert.False(model.HasViewerNote || model.HasNotice || model.HasModeUnavailable || model.HasModeUnknown);
        Assert.Equal("Where answers come from", model.ModeHeading);
        Assert.True(model.HasModes && model.CanChangeMode);
        Assert.Equal(0, model.SelectedModeIndex);
        Assert.Equal("Your own knowledge base", model.Modes[0].ToString());
        Assert.True(model.ShowAdd && model.DraftEditable && model.AddEnabled);
        Assert.Equal(("Add a document", "Title", "The document's text"), (model.AddHeading, model.TitleLabel, model.ContentLabel));
        Assert.Contains("billed", model.AddBilled, StringComparison.Ordinal);
        Assert.Equal(("Add document", "Read a text file"), (model.AddLabel, model.FileLabel));
        Assert.Equal("A plain text file.", model.FileHint);
        Assert.Equal("Documents", model.DocumentsHeading);
        Assert.Equal(2, model.Documents.Count);
        var ready = model.Documents[0];
        Assert.StartsWith("Ready · 4 pieces · Added ", ready.Line, StringComparison.Ordinal);
        Assert.False(ready.Failed);
        Assert.True(ready.ShowDelete && ready.CanDelete);
        Assert.Equal("Delete this document: Refund policy", ready.DeleteName);
        Assert.StartsWith("Processing · Added ", model.Documents[1].Line, StringComparison.Ordinal);
        Assert.EndsWith("https://example.com/area", model.Documents[1].Line, StringComparison.Ordinal);
        Assert.False(model.ConfirmOpen);
        Assert.Empty(sink.Sent);
    }

    [Fact]
    public void LoadingFailuresAndAnEmptyList()
    {
        var model = new KnowledgeViewModel();
        model.Show(View(modeLoading: true, modes: [], documentsLoading: true, documents: []));
        Assert.True(model.ModeLoading && model.DocumentsLoading);
        Assert.False(model.HasModes);
        Assert.Equal(-1, model.SelectedModeIndex);

        model.Show(View(
            modeUnavailable: "It cannot be changed right now.",
            modeFailure: V.Failure("Offline.", retryable: true),
            modes: [],
            canChangeMode: false,
            documentsFailedTitle: "Could not load the documents",
            documentsFailure: V.Failure("Refused."),
            documents: []));
        Assert.True(model.HasModeUnavailable && model.CanRetryMode);
        Assert.Equal("It cannot be changed right now." + Environment.NewLine + "Offline.", model.ModeUnavailable);
        Assert.True(model.HasDocumentsFailure);
        Assert.False(model.CanRetryDocuments);
        Assert.Equal(("Could not load the documents", "Refused."), (model.DocumentsFailedTitle, model.DocumentsFailure));

        model.Show(View(documents: [], empty: new EmptyView("No documents yet", "Add a document above."), modeUnknown: "Stored as \"hybrid\"."));
        Assert.True(model.IsEmpty);
        Assert.Equal(("No documents yet", "Add a document above."), (model.EmptyTitle, model.EmptyBody));
        Assert.True(model.HasModeUnknown);
        Assert.Equal("A setting this app does not know", model.ModeUnknownTitle);
        Assert.False(model.HasModeUnavailable || model.HasDocumentsFailure);
    }

    [Fact]
    public void TypingSendsEachBoxAndAddIsSentOnlyByAdd()
    {
        var (model, sink) = Attached();
        model.Show(View());

        model.DraftTitle = "Holiday hours";
        model.DraftContent = "Closed.";
        // The core's echo does not overwrite the boxes.
        model.Show(View(draftTitle: "Holiday hours", draftContent: "Closed."));
        Assert.Equal(("Holiday hours", "Closed."), (model.DraftTitle, model.DraftContent));
        Assert.Equal(2, sink.Sent.Count);

        model.AddCommand.Execute(null);
        model.Show(View(draftTitle: "Holiday hours", draftContent: "Closed.", draftEditable: false, adding: true, addEnabled: false, canDelete: false, canChangeMode: false));
        Assert.True(model.Adding);
        Assert.False(model.AddCommand.CanExecute(null) || model.ChooseFileCommand.CanExecute(null));
        Assert.False(model.Documents[0].CanDelete);

        // Added: the core empties the form.
        model.Show(View(notice: new SaveNoticeView("Saved.", true)));
        Assert.Equal((string.Empty, string.Empty), (model.DraftTitle, model.DraftContent));
        Assert.True(model.HasNotice && model.NoticeSaved);

        Assert.Equal(
            [
                new UiEvent.Knowledge(new KnowledgeAction.EditTitle("Holiday hours")),
                new UiEvent.Knowledge(new KnowledgeAction.EditContent("Closed.")),
                new UiEvent.Knowledge(new KnowledgeAction.Add()),
            ],
            sink.Sent);
    }

    [Fact]
    public void AnAddWithoutTextSaysWhy()
    {
        var model = new KnowledgeViewModel();
        model.Show(View(addRejected: "A title and some text are both needed."));
        Assert.True(model.HasAddRejected);
        Assert.Equal("A title and some text are both needed.", model.AddRejected);
    }

    [Fact]
    public void ATextFileFillsTheFormAndNothingIsAdded()
    {
        var (model, sink) = Attached();
        model.Show(View());
        Assert.True(model.MayPickFile);

        model.FilePicked([File("Parking.txt", "Free parking behind the clinic.\r\n")]);

        Assert.Equal(("Parking", "Free parking behind the clinic.\n"), (model.DraftTitle, model.DraftContent));
        Assert.False(model.HasFileRefusal);
        Assert.Equal(
            [
                new UiEvent.Knowledge(new KnowledgeAction.EditTitle("Parking")),
                new UiEvent.Knowledge(new KnowledgeAction.EditContent("Free parking behind the clinic.\n")),
            ],
            sink.Sent);

        // A title already typed is kept; the text is replaced.
        model.FilePicked([File("Other.md", "Second.")]);
        Assert.Equal(("Parking", "Second."), (model.DraftTitle, model.DraftContent));
        Assert.Equal(new UiEvent.Knowledge(new KnowledgeAction.EditContent("Second.")), sink.Sent[^1]);
    }

    [Fact]
    public void AFileThatIsNotTextOrTooLargeIsRefusedHere()
    {
        var (model, sink) = Attached();
        model.Show(View());

        var pdf = new PickedFileView("scan.txt", 9, "%PDF-1.7\n"u8.ToArray());
        model.FilePicked([new PickedFile("scan.txt", pdf)]);
        Assert.True(model.HasFileRefusal);
        Assert.StartsWith("Only a plain text file", model.FileRefusal, StringComparison.Ordinal);

        var large = new PickedFileView("big.txt", (1024 * 1024) + 1, "a"u8.ToArray());
        model.FilePicked([new PickedFile("big.txt", large)]);
        Assert.Equal("The file must be 1 MB or smaller.", model.FileRefusal);

        model.FilePicked([new PickedFile("gone.txt", null)]);
        Assert.Equal(KnowledgeViewModel.FileUnreadable, model.FileRefusal);
        Assert.Empty(sink.Sent);

        // Typing puts the refusal away.
        model.DraftContent = "Typed instead.";
        Assert.False(model.HasFileRefusal);
    }

    [Fact]
    public void APickIsIgnoredWhenCancelledOrNotAllowed()
    {
        var (model, sink) = Attached(_ => throw new InvalidOperationException("not read"));
        model.Show(View());
        model.FilePicked([]);

        model.Show(View(draftEditable: false));
        Assert.False(model.MayPickFile);
        model.FilePicked([File("a.txt", "A")]);

        var viewer = new KnowledgeViewModel();
        viewer.Show(View(showAdd: false));
        Assert.False(viewer.MayPickFile);
        Assert.Empty(sink.Sent);
    }

    [Fact]
    public void ChooseFileAsksThePage()
    {
        var (model, _) = Attached();
        model.Show(View());
        var asked = 0;
        model.FileRequested += (_, _) => asked++;
        model.ChooseFileCommand.Execute(null);
        Assert.Equal(1, asked);
    }

    [Fact]
    public void ChoosingAModeSendsItAndACancelledOneGoesBack()
    {
        var (model, sink) = Attached();
        model.Show(View());

        model.SelectedModeIndex = 1;
        Assert.Equal([new UiEvent.Knowledge(new KnowledgeAction.SelectMode(KnowledgeModeChoice.Linked))], sink.Sent);

        var question = new KnowledgeConfirmView("Send questions to Atlassian?", "Out of region.", "Send questions to Atlassian", "Cancel", false);
        model.Show(View(confirm: question));
        Assert.True(model.ConfirmOpen);
        Assert.False(model.ConfirmDestructive);
        Assert.Equal(("Send questions to Atlassian?", "Out of region.", "Cancel"), (model.ConfirmTitle, model.ConfirmBody, model.ConfirmCancel));
        Assert.Equal("Send questions to Atlassian", model.ConfirmAction);

        model.Answer(false);
        // The core kept the stored mode: it shows again, and nothing more is sent.
        var changed = new List<string?>();
        model.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        model.Show(View());
        Assert.Equal(0, model.SelectedModeIndex);
        Assert.Contains(nameof(KnowledgeViewModel.SelectedModeIndex), changed);
        Assert.False(model.ConfirmOpen);

        model.SelectedModeIndex = 0;
        model.SelectedModeIndex = -1;
        model.SelectedModeIndex = 5;
        Assert.Equal(
            [
                new UiEvent.Knowledge(new KnowledgeAction.SelectMode(KnowledgeModeChoice.Linked)),
                new UiEvent.Knowledge(new KnowledgeAction.Cancel()),
            ],
            sink.Sent);
    }

    [Fact]
    public void ADeleteAsksFirst()
    {
        var (model, sink) = Attached();
        model.Show(View());
        model.AskDelete("doc-1");
        model.Show(View(confirm: new KnowledgeConfirmView("Delete this document?", "Deleted.", "Delete", "Cancel", true)));
        Assert.True(model.ConfirmOpen && model.ConfirmDestructive);
        model.Answer(true);

        model.Show(View(canDelete: false));
        model.AskDelete("doc-1");

        Assert.Equal(
            [
                new UiEvent.Knowledge(new KnowledgeAction.AskDelete("doc-1")),
                new UiEvent.Knowledge(new KnowledgeAction.Confirm()),
            ],
            sink.Sent);
    }

    [Fact]
    public void AViewerReadsAndIsOfferedNoControl()
    {
        var model = new KnowledgeViewModel();
        model.Show(View(
            viewerNote: "You have read-only access to this workspace.",
            modes: [_modes[0]],
            canChangeMode: false,
            showAdd: false,
            showDelete: false,
            canDelete: false));
        Assert.True(model.HasViewerNote);
        Assert.False(model.ShowAdd || model.CanChangeMode);
        Assert.All(model.Documents, document => Assert.False(document.ShowDelete));
        Assert.Single(model.Modes);
    }

    [Fact]
    public void AFailedDocumentAndTheOtherCommands()
    {
        var (model, sink) = Attached();
        model.Show(View(documents: [Document(state: DocumentState.Failed, stateLabel: "Failed", pieces: null)]));
        Assert.True(model.Documents[0].Failed);
        Assert.StartsWith("Failed · Added ", model.Documents[0].Line, StringComparison.Ordinal);

        model.DismissNoticeCommand.Execute(null);
        model.RetryCommand.Execute(null);
        Assert.Equal(
            [
                new UiEvent.Knowledge(new KnowledgeAction.DismissNotice()),
                new UiEvent.Refresh(),
            ],
            sink.Sent);
    }
}
