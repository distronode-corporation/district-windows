//! The knowledge base the receptionist answers from (District Studio's
//! Knowledge), as District AI for Linux shows it (`pages/knowledge.rs`).
//!
//! Where answers come from is one choice of two, and switching to the linked
//! mode, which sends callers' questions to Atlassian, asks first; a mode that
//! could not be read is shown as such, never as the default. A document is a
//! title and its text, added once when the member presses Add (billed by its
//! length, which the form says), and deleted only after a question. The core
//! has no upload: "Read a text file" fills the two boxes from a plain text
//! file picked here ([`knowledge_file`]), which is refused here when it is not
//! text or is too large, and nothing is sent until Add. Each document says
//! where it stands (ready, processing, failed). A viewer reads all of it and is
//! offered no control; the core refuses a viewer's writes too. A half-typed
//! document is not an unsaved change (the core's rule, as on Android), so
//! leaving never asks here.

use district_core::{
    Event, KnowledgeConfirm, KnowledgeDocuments, KnowledgeEvent, KnowledgeModeView,
    KnowledgeSection, KnowledgeWrite, Model, Route, SignedIn, WorkspaceSection,
    knowledge_mode_body, knowledge_mode_label,
};
use district_model::{KnowledgeDocument, KnowledgeMode};
use serde::Serialize;

use super::{SaveNoticeView, save_notice};
/// A picked file, as [`knowledge_file`] reads it (crate::files).
pub use crate::files::PickedFileView;
use crate::files::{FileKind, FilePickView, file_kind};
use crate::screen::ScreenView;
use crate::views::{EmptyView, FailureView, humanize};

/// Whether this version has the area's screens.
pub(crate) const BUILT: bool = true;

/// The section's heading: its row's title in the hub, which opens it.
pub const TITLE: &str = "Knowledge";
/// The mode's heading, as the Linux app words it.
pub const MODE_HEADING: &str = "Where answers come from";
/// The heading of a stored mode this app does not know, as the Linux app words it.
pub const MODE_UNKNOWN_TITLE: &str = "A setting this app does not know";
/// The add form's heading, as the Linux app words it.
pub const ADD_HEADING: &str = "Add a document";
/// The title box's label.
pub const TITLE_LABEL: &str = "Title";
/// The text box's label, as the Linux app words it.
pub const CONTENT_LABEL: &str = "The document's text";
/// The add button, as the Linux app words it.
pub const ADD_ACTION: &str = "Add document";
/// The button that fills the form from a text file.
pub const FILE_ACTION: &str = "Read a text file";
/// What a file fills, what it may be, and that nothing is sent yet.
pub const FILE_HINT: &str = "A plain text file (.txt or .md) of up to 1 MB fills the title and \
    the text. Nothing is added until you press Add document.";
/// The documents' heading, as the Linux app words it.
pub const DOCUMENTS_HEADING: &str = "Documents";
/// The heading when the documents could not be read, as the Linux app words it.
pub const DOCUMENTS_FAILED: &str = "Could not load the documents";
/// A document's delete button, as the Linux app names it.
pub const DELETE_LABEL: &str = "Delete this document";
/// The question's button that answers no.
pub const CANCEL: &str = "Cancel";

/// The extensions the file chooser offers: plain text only.
pub const FILE_EXTENSIONS: [&str; 2] = [".txt", ".md"];
/// The largest file read in, in bytes. The service takes any length and bills
/// by it, so this app keeps a file to a size a person would paste.
pub const MAX_FILE_BYTES: u64 = 1024 * 1024;
/// Why a picked file is refused when it holds nothing.
pub const FILE_EMPTY: &str = "This file is empty.";
/// Why a picked file is refused when it holds no words.
pub const FILE_NO_TEXT: &str = "This file has no text to add.";
/// Why a picked file is refused when it is not plain text.
pub const FILE_NOT_TEXT: &str = "Only a plain text file (.txt or .md) can be read in. Copy the \
    text of another kind of document into the box instead.";
/// Why a picked file is refused for its size.
pub const FILE_TOO_LARGE: &str = "The file must be 1 MB or smaller.";

/// The longest title the service keeps, in characters.
const TITLE_CHARS: usize = 200;

/// The knowledge base section.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct KnowledgeView {
    /// The heading, [`TITLE`].
    pub title: String,
    /// For a viewer, that this is read only and who may change it; else none.
    pub viewer_note: Option<String>,
    /// How the last add, delete or change of mode ended, until dismissed.
    pub notice: Option<SaveNoticeView>,
    /// The mode's heading, [`MODE_HEADING`].
    pub mode_heading: String,
    /// Whether the mode is being read (show a progress ring).
    pub mode_loading: bool,
    /// When the mode could not be read: that it cannot be changed now, in the
    /// core's words. The mode is not shown then, never as the default.
    pub mode_unavailable: Option<String>,
    /// Why it could not be read.
    pub mode_failure: Option<FailureView>,
    /// The modes, once read: both for a member who may change it, the stored
    /// one alone for a viewer.
    pub modes: Vec<KnowledgeModeChoiceView>,
    /// A stored mode this app does not know, as stored ("Stored as ...").
    pub mode_unknown: Option<String>,
    /// The heading over it, [`MODE_UNKNOWN_TITLE`].
    pub mode_unknown_title: String,
    /// Whether a mode can be chosen: read, the member may change it, and
    /// nothing on its way.
    pub can_change_mode: bool,
    /// Whether the add form shows: the member may change the workspace.
    pub show_add: bool,
    /// The add form's heading, [`ADD_HEADING`].
    pub add_heading: String,
    /// That adding is billed by length and sent once, in the core's words.
    pub add_billed: String,
    /// The title box's label, [`TITLE_LABEL`].
    pub title_label: String,
    /// The new document's title, as typed.
    pub draft_title: String,
    /// The text box's label, [`CONTENT_LABEL`].
    pub content_label: String,
    /// The new document's text, as typed.
    pub draft_content: String,
    /// Whether the boxes can be typed in: nothing on its way.
    pub draft_editable: bool,
    /// Why the last Add was refused (a title and text are both needed), until
    /// the member types again.
    pub add_rejected: Option<String>,
    /// Whether an add is on its way (show a progress ring).
    pub adding: bool,
    /// Whether Add can be pressed: nothing on its way. The core says what is
    /// missing when it is pressed too early.
    pub add_enabled: bool,
    /// The add button, [`ADD_ACTION`].
    pub add_label: String,
    /// The button that reads a text file into the form, [`FILE_ACTION`].
    pub file_label: String,
    /// What it takes, [`FILE_HINT`].
    pub file_hint: String,
    /// The documents' heading, [`DOCUMENTS_HEADING`].
    pub documents_heading: String,
    /// Whether the documents are being read.
    pub documents_loading: bool,
    /// When they could not be read, [`DOCUMENTS_FAILED`].
    pub documents_failed_title: Option<String>,
    /// Why.
    pub documents_failure: Option<FailureView>,
    /// The documents, newest first.
    pub documents: Vec<KnowledgeDocumentView>,
    /// What an empty list says, when the read is in and there are none.
    pub empty: Option<EmptyView>,
    /// Whether each document's delete button shows: the member may change the
    /// workspace.
    pub show_delete: bool,
    /// Whether it can be pressed now: nothing on its way.
    pub can_delete: bool,
    /// The delete button's name, [`DELETE_LABEL`].
    pub delete_label: String,
    /// The question asked before a delete or a switch to the linked mode,
    /// while it is.
    pub confirm: Option<KnowledgeConfirmView>,
}

/// Where answers may come from.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash, Serialize, uniffi::Enum)]
pub enum KnowledgeModeChoice {
    /// From the documents added here, searched in the workspace's own region.
    Internal,
    /// Sent to Atlassian, whose assistant writes the answers.
    Linked,
}

impl From<KnowledgeModeChoice> for KnowledgeMode {
    fn from(choice: KnowledgeModeChoice) -> Self {
        match choice {
            KnowledgeModeChoice::Internal => Self::Internal,
            KnowledgeModeChoice::Linked => Self::Linked,
        }
    }
}

/// The modes, in the order they are offered.
const MODES: [KnowledgeModeChoice; 2] =
    [KnowledgeModeChoice::Internal, KnowledgeModeChoice::Linked];

/// One mode, as a choice.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct KnowledgeModeChoiceView {
    /// Which.
    pub mode: KnowledgeModeChoice,
    /// Its name, in the core's words.
    pub label: String,
    /// What it means, in the core's words.
    pub body: String,
    /// Whether it is the one stored.
    pub selected: bool,
}

/// Where a document stands.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash, Serialize, uniffi::Enum)]
pub enum DocumentState {
    /// Ready to answer from.
    Ready,
    /// Being cut into pieces and embedded.
    Processing,
    /// Not usable: adding it again is the way.
    Failed,
    /// A state added later, shown as the service names it.
    Other,
}

/// One document.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct KnowledgeDocumentView {
    /// Its id, which a delete names.
    pub id: String,
    /// Its title.
    pub title: String,
    /// Where it stands.
    pub state: DocumentState,
    /// Where it stands, in words ("Ready", "Processing", or the service's own).
    pub state_label: String,
    /// How many pieces it was cut into ("4 pieces"), when any.
    pub pieces: Option<String>,
    /// When it was added, ISO 8601, for the app to say in local time.
    pub created_at: String,
    /// The address it was read from, or none for pasted text.
    pub source_url: Option<String>,
}

/// A question before a write.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct KnowledgeConfirmView {
    /// The heading.
    pub title: String,
    /// What the write does, in the core's words.
    pub body: String,
    /// The confirming button.
    pub action: String,
    /// The button that answers no, [`CANCEL`].
    pub cancel_label: String,
    /// Whether it destroys something (a delete), rather than moves where
    /// questions go.
    pub destructive: bool,
}

/// Something the member did on the knowledge base section.
#[derive(Clone, Debug, PartialEq, Eq, uniffi::Enum)]
pub enum KnowledgeAction {
    /// Open the knowledge base section.
    Open,
    /// The new document's title changed: what the box holds now.
    EditTitle {
        /// The text.
        value: String,
    },
    /// Its text changed.
    EditContent {
        /// The text.
        value: String,
    },
    /// Add it: billed, sent once.
    Add,
    /// Ask before deleting a listed document.
    AskDelete {
        /// The document.
        document_id: String,
    },
    /// Choose where answers come from; the linked mode asks first.
    SelectMode {
        /// Which.
        mode: KnowledgeModeChoice,
    },
    /// Answer yes to the question showing.
    Confirm,
    /// Answer no.
    Cancel,
    /// Put the notice away.
    DismissNotice,
}

/// The core events `action` is, in the order the core is to hear them.
pub(crate) fn events(action: KnowledgeAction) -> Vec<Event> {
    let knowledge = Event::Knowledge;
    vec![match action {
        KnowledgeAction::Open => Event::Navigate(Route::Workspace(WorkspaceSection::Knowledge)),
        KnowledgeAction::EditTitle { value } => knowledge(KnowledgeEvent::EditTitle(value)),
        KnowledgeAction::EditContent { value } => knowledge(KnowledgeEvent::EditContent(value)),
        KnowledgeAction::Add => knowledge(KnowledgeEvent::Add),
        KnowledgeAction::AskDelete { document_id } => {
            knowledge(KnowledgeEvent::AskDelete { document_id })
        }
        KnowledgeAction::SelectMode { mode } => knowledge(KnowledgeEvent::SelectMode(mode.into())),
        KnowledgeAction::Confirm => knowledge(KnowledgeEvent::Confirm),
        KnowledgeAction::Cancel => knowledge(KnowledgeEvent::Cancel),
        KnowledgeAction::DismissNotice => knowledge(KnowledgeEvent::DismissNotice),
    }]
}

/// The page of the knowledge base section, for a signed-in model.
pub(crate) fn screen(_model: &Model, signed_in: &SignedIn) -> ScreenView {
    ScreenView::Knowledge {
        view: knowledge_view(
            signed_in.knowledge.as_ref(),
            signed_in.capabilities().can_change,
        ),
    }
}

/// The section `section` holds, for a member who may change it or not; being
/// read when there is none yet.
fn knowledge_view(section: Option<&KnowledgeSection>, can_change: bool) -> KnowledgeView {
    let busy = section.is_some_and(KnowledgeSection::busy);
    let mode = section.map_or(&KnowledgeModeView::Loading, |s| &s.mode);
    let documents = section.map_or(&KnowledgeDocuments::Loading, |s| &s.documents);
    let stored = match mode {
        KnowledgeModeView::Ready(stored) => Some(stored.as_str()),
        _ => None,
    };
    let known = MODES
        .iter()
        .any(|choice| stored == Some(KnowledgeMode::from(*choice).as_str()));
    KnowledgeView {
        title: TITLE.to_owned(),
        viewer_note: (!can_change).then(|| KnowledgeSection::VIEWER.to_owned()),
        notice: section.and_then(|s| save_notice(&s.write)),
        mode_heading: MODE_HEADING.to_owned(),
        mode_loading: *mode == KnowledgeModeView::Loading,
        mode_unavailable: matches!(mode, KnowledgeModeView::Failed(_))
            .then(|| KnowledgeSection::MODE_UNAVAILABLE.to_owned()),
        mode_failure: match mode {
            KnowledgeModeView::Failed(failure) => Some(failure.into()),
            _ => None,
        },
        modes: stored.map_or_else(Vec::new, |stored| mode_choices(stored, can_change)),
        mode_unknown: stored
            .filter(|_| !known)
            .map(|stored| format!("Stored as \"{stored}\".")),
        mode_unknown_title: MODE_UNKNOWN_TITLE.to_owned(),
        can_change_mode: can_change && section.is_some_and(KnowledgeSection::can_change_mode),
        show_add: can_change,
        add_heading: ADD_HEADING.to_owned(),
        add_billed: KnowledgeSection::ADD_BILLED.to_owned(),
        title_label: TITLE_LABEL.to_owned(),
        draft_title: section.map_or_else(String::new, |s| s.title.clone()),
        content_label: CONTENT_LABEL.to_owned(),
        draft_content: section.map_or_else(String::new, |s| s.content.clone()),
        draft_editable: section.is_some() && !busy,
        add_rejected: section
            .filter(|s| s.add_rejected)
            .map(|_| KnowledgeSection::ADD_REJECTED.to_owned()),
        adding: busy && section.and_then(|s| s.last_write) == Some(KnowledgeWrite::Add),
        add_enabled: section.is_some() && !busy,
        add_label: ADD_ACTION.to_owned(),
        file_label: FILE_ACTION.to_owned(),
        file_hint: FILE_HINT.to_owned(),
        documents_heading: DOCUMENTS_HEADING.to_owned(),
        documents_loading: *documents == KnowledgeDocuments::Loading,
        documents_failed_title: matches!(documents, KnowledgeDocuments::Failed(_))
            .then(|| DOCUMENTS_FAILED.to_owned()),
        documents_failure: match documents {
            KnowledgeDocuments::Failed(failure) => Some(failure.into()),
            _ => None,
        },
        documents: match documents {
            KnowledgeDocuments::Ready(documents) => documents.iter().map(document).collect(),
            _ => Vec::new(),
        },
        empty: matches!(documents, KnowledgeDocuments::Ready(list) if list.is_empty())
            .then(|| EmptyView::new(KnowledgeSection::EMPTY_TITLE, KnowledgeSection::EMPTY_BODY)),
        show_delete: can_change,
        can_delete: can_change && section.is_some() && !busy,
        delete_label: DELETE_LABEL.to_owned(),
        confirm: section
            .and_then(|s| s.confirming.as_ref())
            .map(confirm_view),
    }
}

/// The modes offered with `stored` chosen: both for a member who may change
/// it, the stored one alone (when known) for a viewer.
fn mode_choices(stored: &str, can_change: bool) -> Vec<KnowledgeModeChoiceView> {
    MODES
        .into_iter()
        .map(|choice| {
            let mode = KnowledgeMode::from(choice);
            KnowledgeModeChoiceView {
                mode: choice,
                label: knowledge_mode_label(mode).to_owned(),
                body: knowledge_mode_body(mode).to_owned(),
                selected: stored == mode.as_str(),
            }
        })
        .filter(|choice| can_change || choice.selected)
        .collect()
}

/// The row of `document`.
fn document(document: &KnowledgeDocument) -> KnowledgeDocumentView {
    KnowledgeDocumentView {
        id: document.id.clone(),
        title: document.title.clone(),
        state: match document.status.trim() {
            "ready" => DocumentState::Ready,
            "processing" => DocumentState::Processing,
            "failed" => DocumentState::Failed,
            _ => DocumentState::Other,
        },
        state_label: humanize(&document.status),
        pieces: match document.chunk_count {
            count if count <= 0 => None,
            1 => Some("1 piece".to_owned()),
            count => Some(format!("{count} pieces")),
        },
        created_at: document.created_at.clone(),
        source_url: document.source_url.clone(),
    }
}

/// The question `confirm`, in the core's words.
fn confirm_view(confirm: &KnowledgeConfirm) -> KnowledgeConfirmView {
    KnowledgeConfirmView {
        title: confirm.title().to_owned(),
        body: confirm.body(),
        action: confirm.action().to_owned(),
        cancel_label: CANCEL.to_owned(),
        destructive: matches!(confirm, KnowledgeConfirm::Delete { .. }),
    }
}

/// What a text file picked for a document gave: its title and text, to put in
/// the form, or why it was refused.
#[derive(Clone, Debug, PartialEq, Eq, uniffi::Enum)]
pub enum KnowledgeFileRead {
    /// Plain text: the file's name less its extension, and its text.
    Text {
        /// The title it suggests, for an empty title box.
        title: String,
        /// The text.
        content: String,
    },
    /// Refused, and why. Nothing is put in the form.
    Refused {
        /// Why, in words to show.
        reason: String,
    },
}

/// The file chooser for a document: [`FILE_EXTENSIONS`], one file, read no
/// further than one byte past [`MAX_FILE_BYTES`], so a larger one is seen to be.
#[uniffi::export]
pub fn knowledge_file_pick() -> FilePickView {
    FilePickView {
        extensions: FILE_EXTENSIONS.iter().map(|e| (*e).to_owned()).collect(),
        max_files: 1,
        read_cap: MAX_FILE_BYTES + 1,
    }
}

/// `file` as a document's title and text, or why it is refused: too large,
/// empty, not plain text (its first bytes, never its name, decide), or no
/// words in it.
#[uniffi::export]
pub fn knowledge_file(file: PickedFileView) -> KnowledgeFileRead {
    match read_text(&file) {
        Ok(content) => KnowledgeFileRead::Text {
            title: title_of(&file.file_name),
            content,
        },
        Err(reason) => KnowledgeFileRead::Refused {
            reason: reason.to_owned(),
        },
    }
}

/// The text of `file`, or why it is refused.
fn read_text(file: &PickedFileView) -> Result<String, &'static str> {
    let bytes = file.bytes.as_slice();
    if file.size > MAX_FILE_BYTES || bytes.len() as u64 > MAX_FILE_BYTES {
        return Err(FILE_TOO_LARGE);
    }
    if bytes.is_empty() {
        return Err(FILE_EMPTY);
    }
    if file_kind(bytes.iter().take(12).copied().collect()) != FileKind::Unknown {
        return Err(FILE_NOT_TEXT);
    }
    let text = decode(bytes).ok_or(FILE_NOT_TEXT)?;
    if text
        .chars()
        .any(|c| c.is_control() && !matches!(c, '\t' | '\n' | '\r' | '\u{c}'))
    {
        return Err(FILE_NOT_TEXT);
    }
    let text = text.replace("\r\n", "\n");
    if text.trim().is_empty() {
        return Err(FILE_NO_TEXT);
    }
    Ok(text)
}

/// `bytes` as text: UTF-8, with or without its mark, or UTF-16 with its mark
/// (as Notepad saves "Unicode"). Anything else is not plain text.
fn decode(bytes: &[u8]) -> Option<String> {
    let utf16 = |rest: &[u8], unit: fn([u8; 2]) -> u16| {
        let (pairs, odd) = rest.as_chunks::<2>();
        if !odd.is_empty() {
            return None;
        }
        let units: Vec<u16> = pairs.iter().map(|pair| unit(*pair)).collect();
        String::from_utf16(&units).ok()
    };
    if let Some(rest) = bytes.strip_prefix(&[0xef, 0xbb, 0xbf]) {
        String::from_utf8(rest.to_vec()).ok()
    } else if let Some(rest) = bytes.strip_prefix(&[0xff, 0xfe]) {
        utf16(rest, u16::from_le_bytes)
    } else if let Some(rest) = bytes.strip_prefix(&[0xfe, 0xff]) {
        utf16(rest, u16::from_be_bytes)
    } else {
        String::from_utf8(bytes.to_vec()).ok()
    }
}

/// The title a file's name suggests: without its folder, its last extension
/// and any control character, no longer than the service keeps; the name as
/// it is when that leaves nothing.
fn title_of(file_name: &str) -> String {
    let name: String = file_name
        .rsplit(['/', '\\'])
        .next()
        .unwrap_or_default()
        .chars()
        .filter(|c| !c.is_control())
        .collect();
    let stem = match name.rsplit_once('.') {
        Some((stem, _)) if !stem.trim().is_empty() => stem,
        _ => name.as_str(),
    };
    stem.trim().chars().take(TITLE_CHARS).collect()
}

#[cfg(test)]
impl KnowledgeView {
    /// A view for the boundary's own tests (a screen's trip to C# and back).
    pub(crate) fn sample() -> Self {
        knowledge_view(None, true)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn file(name: &str, bytes: &[u8]) -> PickedFileView {
        PickedFileView {
            file_name: name.to_owned(),
            size: bytes.len() as u64,
            bytes: bytes.to_vec(),
        }
    }

    fn refused(reason: &str) -> KnowledgeFileRead {
        KnowledgeFileRead::Refused {
            reason: reason.to_owned(),
        }
    }

    #[test]
    fn a_section_not_read_yet_is_loading() {
        let view = knowledge_view(None, true);
        assert!(view.mode_loading && view.documents_loading);
        assert!(view.modes.is_empty() && view.documents.is_empty());
        assert!(!view.add_enabled && !view.can_delete && !view.can_change_mode);
        assert_eq!(view.viewer_note, None);
        assert!(knowledge_view(None, false).viewer_note.is_some());
    }

    #[test]
    fn a_text_file_fills_the_form() {
        assert_eq!(
            knowledge_file(file(
                "C:\\Docs\\Refund policy.txt",
                b"We refund in 30 days.\r\n"
            )),
            KnowledgeFileRead::Text {
                title: "Refund policy".to_owned(),
                content: "We refund in 30 days.\n".to_owned(),
            }
        );
        // With a UTF-8 mark, and as Notepad's "Unicode" (UTF-16, both orders).
        let marked = knowledge_file(file("hours.md", b"\xef\xbb\xbfOpen 9 to 5"));
        let utf16le: Vec<u8> = [0xff, 0xfe]
            .into_iter()
            .chain("Open 9 to 5".encode_utf16().flat_map(u16::to_le_bytes))
            .collect();
        let utf16be: Vec<u8> = [0xfe, 0xff]
            .into_iter()
            .chain("Open 9 to 5".encode_utf16().flat_map(u16::to_be_bytes))
            .collect();
        for read in [
            marked,
            knowledge_file(file("hours.md", &utf16le)),
            knowledge_file(file("hours.md", &utf16be)),
        ] {
            assert_eq!(
                read,
                KnowledgeFileRead::Text {
                    title: "hours".to_owned(),
                    content: "Open 9 to 5".to_owned(),
                }
            );
        }
    }

    #[test]
    fn the_title_loses_folder_extension_and_control_characters() {
        assert_eq!(title_of("../a/b/notes.v2.txt"), "notes.v2");
        assert_eq!(title_of("ro\r\nof.txt"), "roof");
        assert_eq!(title_of(".txt"), ".txt");
        assert_eq!(title_of("README"), "README");
        assert_eq!(title_of("folder\\"), "");
        assert_eq!(title_of(&format!("{}.txt", "a".repeat(300))).len(), 200);
    }

    #[test]
    fn a_file_that_is_not_text_is_refused_by_its_bytes() {
        let not_text = refused(FILE_NOT_TEXT);
        for (name, bytes) in [
            ("scan.txt", b"%PDF-1.7\n%\xe2\xe3\xcf\xd3".as_slice()),
            ("photo.md", b"\x89PNG\r\n\x1a\n\0\0\0\rIHDR".as_slice()),
            ("latin1.txt", b"caf\xe9".as_slice()),
            ("binary.txt", b"abc\0def".as_slice()),
            ("odd.txt", b"\xff\xfeA".as_slice()),
            ("lone.txt", b"\xff\xfe\x00\xd8".as_slice()),
            ("bad.txt", b"\xef\xbb\xbf\xff".as_slice()),
        ] {
            assert_eq!(knowledge_file(file(name, bytes)), not_text, "{name}");
        }
    }

    #[test]
    fn an_empty_or_large_file_is_refused() {
        assert_eq!(knowledge_file(file("empty.txt", b"")), refused(FILE_EMPTY));
        assert_eq!(
            knowledge_file(file("blank.txt", b" \r\n\t ")),
            refused(FILE_NO_TEXT)
        );
        let cap = usize::try_from(MAX_FILE_BYTES).unwrap();
        let mut large = vec![b'a'; cap + 1];
        assert_eq!(
            knowledge_file(file("large.txt", &large)),
            refused(FILE_TOO_LARGE)
        );
        large.truncate(cap);
        assert!(matches!(
            knowledge_file(file("large.txt", &large)),
            KnowledgeFileRead::Text { .. }
        ));
        // Windows says it is larger than what was read: still too large.
        let mut said = file("large.txt", b"short");
        said.size = MAX_FILE_BYTES + 10;
        assert_eq!(knowledge_file(said), refused(FILE_TOO_LARGE));
    }

    #[test]
    fn the_chooser_offers_plain_text_and_reads_past_the_limit() {
        let pick = knowledge_file_pick();
        assert_eq!(pick.extensions, [".txt", ".md"]);
        assert_eq!(pick.max_files, 1);
        assert_eq!(pick.read_cap, MAX_FILE_BYTES + 1);
    }

    #[test]
    fn a_document_says_where_it_stands() {
        let row = |status: &str, chunk_count| {
            document(&KnowledgeDocument {
                id: "d".to_owned(),
                title: "T".to_owned(),
                source_type: "text".to_owned(),
                source_url: None,
                status: status.to_owned(),
                chunk_count,
                created_at: "2026-08-15T14:30:00.000Z".to_owned(),
            })
        };
        assert_eq!(row("ready", 4).state, DocumentState::Ready);
        assert_eq!(row("ready", 4).pieces.as_deref(), Some("4 pieces"));
        assert_eq!(row("ready", 1).pieces.as_deref(), Some("1 piece"));
        assert_eq!(row("processing", 0).pieces, None);
        assert_eq!(row("processing", 0).state, DocumentState::Processing);
        assert_eq!(row("failed", 0).state, DocumentState::Failed);
        let later = row("re_indexing", -1);
        assert_eq!(later.state, DocumentState::Other);
        assert_eq!(later.state_label, "Re indexing");
        assert_eq!(later.pieces, None);
    }
}
