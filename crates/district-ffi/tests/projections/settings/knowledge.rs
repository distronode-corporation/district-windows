//! The Knowledge section (src/settings/knowledge.rs).

use district_core::{Effect, Event, KnowledgeEvent, Route, WorkspaceSection};
use district_ffi::settings::knowledge::{
    DocumentState, FILE_NOT_TEXT, FILE_TOO_LARGE, KnowledgeAction, KnowledgeFileRead,
    KnowledgeModeChoice, KnowledgeView, MAX_FILE_BYTES, PickedFileView, knowledge_file,
};
use district_ffi::{Held, NavDestination, ScreenView, UiEvent, leaves_unsaved, screen_view};
use district_model::KnowledgeMode;
use serde_json::{Value, json};

use super::super::{Case, Session, contracts, offered, route, server_error, signed_in, viewer};

/// This area's snapshot cases.
pub(crate) fn cases() -> Vec<Case> {
    vec![
        ("knowledge-loading", open(signed_in())),
        ("knowledge-loaded", ready(signed_in())),
        (
            "knowledge-internal",
            read(signed_in(), Ok(documents()), Ok(mode("internal"))),
        ),
        (
            "knowledge-empty",
            read(signed_in(), Ok(no_documents()), Ok(mode("internal"))),
        ),
        (
            "knowledge-document-failed",
            read(signed_in(), Ok(failed_document()), Ok(mode("internal"))),
        ),
        (
            "knowledge-documents-failed",
            read(signed_in(), Err(()), Ok(mode("internal"))),
        ),
        (
            "knowledge-mode-failed",
            read(signed_in(), Ok(documents()), Err(())),
        ),
        (
            "knowledge-mode-unknown",
            read(signed_in(), Ok(documents()), Ok(mode("hybrid"))),
        ),
        (
            "knowledge-viewer",
            read(viewer(), Ok(documents()), Ok(mode("linked"))),
        ),
        ("knowledge-drafting", drafted(internal())),
        ("knowledge-from-file", from_file(internal())),
        (
            "knowledge-add-rejected",
            act(internal(), KnowledgeAction::Add),
        ),
        ("knowledge-adding", adding()),
        ("knowledge-added", added(Ok(()))),
        ("knowledge-add-failed", added(Err(()))),
        ("knowledge-delete-asked", delete_asked()),
        (
            "knowledge-deleting",
            act(delete_asked(), KnowledgeAction::Confirm),
        ),
        ("knowledge-deleted", deleted()),
        ("knowledge-linked-asked", linked_asked()),
        (
            "knowledge-mode-changing",
            act(linked_asked(), KnowledgeAction::Confirm),
        ),
        ("knowledge-mode-changed", mode_changed(Ok(()))),
        ("knowledge-mode-change-failed", mode_changed(Err(()))),
    ]
}

fn act(session: Session, action: KnowledgeAction) -> Session {
    session.ui(UiEvent::Knowledge { action })
}

fn open(session: Session) -> Session {
    act(session, KnowledgeAction::Open)
}

/// The contract's documents: one ready in four pieces, one being processed.
fn documents() -> Value {
    contracts::json("district-knowledge.json")
}

fn no_documents() -> Value {
    json!({ "success": true, "documents": [] })
}

/// The contract's documents, the second one failed.
fn failed_document() -> Value {
    let mut list = documents();
    list["documents"][1]["status"] = json!("failed");
    list
}

fn mode(mode: &str) -> Value {
    json!({ "success": true, "mode": mode })
}

/// The section opened and its two reads answered, each `Ok` with its JSON or
/// failed.
fn read(session: Session, documents: Result<Value, ()>, mode: Result<Value, ()>) -> Session {
    open(session)
        .answer(
            |e| matches!(e, Effect::LoadKnowledge { .. }),
            |ticket| Event::KnowledgeLoaded {
                ticket,
                result: documents
                    .map(|value| contracts::decode("documents", value))
                    .map_err(|()| server_error()),
            },
        )
        .answer(
            |e| matches!(e, Effect::LoadKnowledgeMode { .. }),
            |ticket| Event::KnowledgeModeLoaded {
                ticket,
                result: mode
                    .map(|value| contracts::decode("mode", value))
                    .map_err(|()| server_error()),
            },
        )
}

/// Read from the contract: its documents, and the linked mode.
fn ready(session: Session) -> Session {
    read(
        session,
        Ok(documents()),
        Ok(contracts::json("district-knowledge-mode.json")),
    )
}

/// Read, with the workspace's own knowledge base in force.
fn internal() -> Session {
    read(signed_in(), Ok(documents()), Ok(mode("internal")))
}

fn drafted(session: Session) -> Session {
    let titled = act(
        session,
        KnowledgeAction::EditTitle {
            value: "Holiday hours".to_owned(),
        },
    );
    act(
        titled,
        KnowledgeAction::EditContent {
            value: "Closed on public holidays.".to_owned(),
        },
    )
}

/// The form filled from a picked text file, as the page does it: the title
/// from its name, the text from its bytes.
fn from_file(session: Session) -> Session {
    let KnowledgeFileRead::Text { title, content } = knowledge_file(PickedFileView {
        file_name: "C:\\Users\\a\\Documents\\Parking.txt".to_owned(),
        size: 38,
        bytes: b"Free parking behind the clinic.\r\nAsk!\r\n".to_vec(),
    }) else {
        panic!("a text file is read");
    };
    let titled = act(session, KnowledgeAction::EditTitle { value: title });
    act(titled, KnowledgeAction::EditContent { value: content })
}

fn adding() -> Session {
    act(drafted(internal()), KnowledgeAction::Add)
}

/// The add answered `written`; when it landed, the list read again.
fn added(written: Result<(), ()>) -> Session {
    let landed = written.is_ok();
    let session = adding().answer(
        |e| matches!(e, Effect::AddKnowledgeDocument { .. }),
        |ticket| Event::SettingsWritten {
            ticket,
            result: written.map_err(|()| server_error()),
        },
    );
    if !landed {
        return session;
    }
    session.answer(
        |e| matches!(e, Effect::LoadKnowledge { .. }),
        |ticket| Event::KnowledgeLoaded {
            ticket,
            result: {
                let mut list = documents();
                let added = json!({
                    "id": "doc_contract_created",
                    "title": "Holiday hours",
                    "sourceType": "text",
                    "sourceUrl": null,
                    "status": "ready",
                    "chunkCount": 1,
                    "createdAt": "2026-08-16T09:00:00.000Z"
                });
                list["documents"].as_array_mut().unwrap().insert(0, added);
                Ok(contracts::decode("documents", list))
            },
        },
    )
}

fn delete_asked() -> Session {
    act(
        internal(),
        KnowledgeAction::AskDelete {
            document_id: "doc_contract_ready".to_owned(),
        },
    )
}

/// The delete landed, and the list read again without the document.
fn deleted() -> Session {
    act(delete_asked(), KnowledgeAction::Confirm)
        .answer(
            |e| matches!(e, Effect::DeleteKnowledgeDocument { .. }),
            |ticket| Event::SettingsWritten {
                ticket,
                result: Ok(()),
            },
        )
        .answer(
            |e| matches!(e, Effect::LoadKnowledge { .. }),
            |ticket| Event::KnowledgeLoaded {
                ticket,
                result: {
                    let mut list = documents();
                    list["documents"].as_array_mut().unwrap().remove(0);
                    Ok(contracts::decode("documents", list))
                },
            },
        )
}

fn linked_asked() -> Session {
    act(
        internal(),
        KnowledgeAction::SelectMode {
            mode: KnowledgeModeChoice::Linked,
        },
    )
}

/// The switch to the linked mode answered: the mode stored, or refused.
fn mode_changed(written: Result<(), ()>) -> Session {
    act(linked_asked(), KnowledgeAction::Confirm).answer(
        |e| matches!(e, Effect::SetKnowledgeMode { .. }),
        |ticket| Event::KnowledgeModeLoaded {
            ticket,
            result: written
                .map(|()| contracts::decode("mode", mode("linked")))
                .map_err(|()| server_error()),
        },
    )
}

fn view(session: &Session) -> KnowledgeView {
    match screen_view(&session.model) {
        ScreenView::Knowledge { view } => view,
        other => panic!("not the knowledge base: {other:?}"),
    }
}

fn count(session: &Session, pick: fn(&Effect) -> bool) -> usize {
    session.pending.iter().filter(|e| pick(e)).count()
}

/// Built, the section opens from the hub's row and shows the mode, the add
/// form and the documents.
#[test]
fn built_it_opens_and_shows_mode_form_and_documents() {
    let session = ready(signed_in());
    assert_eq!(
        route(&session),
        Route::Workspace(WorkspaceSection::Knowledge)
    );
    assert!(offered(&session).contains(&NavDestination::Settings));
    let shown = view(&session);
    assert_eq!(shown.title, "Knowledge");
    assert_eq!(shown.viewer_note, None);
    assert!(shown.show_add && shown.add_enabled && shown.can_change_mode);
    assert!(shown.add_billed.contains("billed"), "{}", shown.add_billed);
    let modes: Vec<_> = shown.modes.iter().map(|m| (m.mode, m.selected)).collect();
    assert_eq!(
        modes,
        [
            (KnowledgeModeChoice::Internal, false),
            (KnowledgeModeChoice::Linked, true)
        ]
    );
    let states: Vec<_> = shown.documents.iter().map(|d| d.state).collect();
    assert_eq!(states, [DocumentState::Ready, DocumentState::Processing]);
    assert_eq!(shown.documents[0].pieces.as_deref(), Some("4 pieces"));
    assert_eq!(
        shown.documents[1].source_url.as_deref(),
        Some("https://contract.test/service-area")
    );
    assert!(shown.show_delete && shown.can_delete);
}

#[test]
fn each_action_is_its_core_event() {
    let knowledge = Event::Knowledge;
    for (action, event) in [
        (
            KnowledgeAction::Open,
            Event::Navigate(Route::Workspace(WorkspaceSection::Knowledge)),
        ),
        (
            KnowledgeAction::EditTitle {
                value: "T".to_owned(),
            },
            knowledge(KnowledgeEvent::EditTitle("T".to_owned())),
        ),
        (
            KnowledgeAction::EditContent {
                value: "C".to_owned(),
            },
            knowledge(KnowledgeEvent::EditContent("C".to_owned())),
        ),
        (KnowledgeAction::Add, knowledge(KnowledgeEvent::Add)),
        (
            KnowledgeAction::AskDelete {
                document_id: "d".to_owned(),
            },
            knowledge(KnowledgeEvent::AskDelete {
                document_id: "d".to_owned(),
            }),
        ),
        (
            KnowledgeAction::SelectMode {
                mode: KnowledgeModeChoice::Internal,
            },
            knowledge(KnowledgeEvent::SelectMode(KnowledgeMode::Internal)),
        ),
        (
            KnowledgeAction::SelectMode {
                mode: KnowledgeModeChoice::Linked,
            },
            knowledge(KnowledgeEvent::SelectMode(KnowledgeMode::Linked)),
        ),
        (KnowledgeAction::Confirm, knowledge(KnowledgeEvent::Confirm)),
        (KnowledgeAction::Cancel, knowledge(KnowledgeEvent::Cancel)),
        (
            KnowledgeAction::DismissNotice,
            knowledge(KnowledgeEvent::DismissNotice),
        ),
    ] {
        assert_eq!(UiEvent::Knowledge { action }.events(), [event]);
    }
}

/// Add is sent once, only when pressed, and only with a title and text; the
/// form is read only until the answer, and a failed add keeps the text.
#[test]
fn a_document_is_added_once_and_only_by_add() {
    let pick = |e: &Effect| matches!(e, Effect::AddKnowledgeDocument { .. });
    let drafted = drafted(internal());
    assert_eq!(count(&drafted, pick), 0, "typing sends nothing");
    let rejected = view(&act(internal(), KnowledgeAction::Add));
    assert!(rejected.add_rejected.is_some());
    let sending = adding();
    assert_eq!(count(&sending, pick), 1);
    let shown = view(&sending);
    assert!(shown.adding && !shown.add_enabled && !shown.draft_editable && !shown.can_delete);
    assert!(!shown.can_change_mode);
    let again = act(sending, KnowledgeAction::Add);
    assert_eq!(count(&again, pick), 1, "a second press sends nothing more");
    let done = view(&added(Ok(())));
    assert_eq!(done.notice.map(|n| n.saved), Some(true));
    assert_eq!(
        (done.draft_title.as_str(), done.draft_content.as_str()),
        ("", "")
    );
    assert_eq!(done.documents.len(), 3);
    let failed = view(&added(Err(())));
    assert_eq!(failed.notice.map(|n| n.saved), Some(false));
    assert_eq!(
        failed.draft_title, "Holiday hours",
        "a failed add keeps the text"
    );
}

/// A picked text file fills the form and sends nothing; a file that is not
/// text, or is too large, is refused before anything reaches the core.
#[test]
fn a_text_file_fills_the_form_and_others_are_refused() {
    let session = from_file(internal());
    let shown = view(&session);
    assert_eq!(shown.draft_title, "Parking");
    assert_eq!(
        shown.draft_content,
        "Free parking behind the clinic.\nAsk!\n"
    );
    assert_eq!(
        count(&session, |e| matches!(
            e,
            Effect::AddKnowledgeDocument { .. }
        )),
        0
    );
    let picked = |name: &str, bytes: &[u8], size: u64| PickedFileView {
        file_name: name.to_owned(),
        size,
        bytes: bytes.to_vec(),
    };
    assert_eq!(
        knowledge_file(picked("scan.txt", b"%PDF-1.7\n", 9)),
        KnowledgeFileRead::Refused {
            reason: FILE_NOT_TEXT.to_owned()
        }
    );
    assert_eq!(
        knowledge_file(picked("big.txt", b"a", MAX_FILE_BYTES + 1)),
        KnowledgeFileRead::Refused {
            reason: FILE_TOO_LARGE.to_owned()
        }
    );
}

/// Deleting asks first, in the core's words; Cancel sends nothing, and the
/// list is read again after the delete.
#[test]
fn a_delete_asks_first() {
    let pick = |e: &Effect| matches!(e, Effect::DeleteKnowledgeDocument { .. });
    let asked = delete_asked();
    let question = view(&asked).confirm.expect("the question shows");
    assert!(question.destructive);
    assert!(question.body.contains("Refund policy"), "{}", question.body);
    assert_eq!(count(&asked, pick), 0);
    let cancelled = act(delete_asked(), KnowledgeAction::Cancel);
    assert_eq!(view(&cancelled).confirm, None);
    assert_eq!(count(&cancelled, pick), 0);
    assert_eq!(count(&act(asked, KnowledgeAction::Confirm), pick), 1);
    let done = view(&deleted());
    assert_eq!(done.documents.len(), 1);
    assert_eq!(done.notice.map(|n| n.saved), Some(true));
}

/// Switching to the linked mode asks first, since it sends questions out of
/// region; switching back does not. The mode shown is the one stored.
#[test]
fn the_linked_mode_asks_first() {
    let pick = |e: &Effect| matches!(e, Effect::SetKnowledgeMode { .. });
    let asked = linked_asked();
    let question = view(&asked).confirm.expect("the question shows");
    assert!(!question.destructive);
    assert!(question.body.contains("Atlassian"), "{}", question.body);
    assert_eq!(count(&asked, pick), 0);
    assert_eq!(count(&act(asked, KnowledgeAction::Confirm), pick), 1);
    let changed = mode_changed(Ok(()));
    let selected: Vec<_> = view(&changed)
        .modes
        .iter()
        .filter(|m| m.selected)
        .map(|m| m.mode)
        .collect();
    assert_eq!(selected, [KnowledgeModeChoice::Linked]);
    let back = act(
        changed,
        KnowledgeAction::SelectMode {
            mode: KnowledgeModeChoice::Internal,
        },
    );
    assert_eq!(view(&back).confirm, None);
    assert_eq!(count(&back, pick), 1, "back to internal is not asked");
    let failed = view(&mode_changed(Err(())));
    assert_eq!(failed.notice.map(|n| n.saved), Some(false));
}

/// A mode that could not be read is shown as such, never as the default, and
/// cannot be changed; a mode this app does not know is shown as stored.
#[test]
fn an_unread_or_unknown_mode_is_not_shown_as_a_choice() {
    let failed = view(&read(signed_in(), Ok(documents()), Err(())));
    assert!(failed.modes.is_empty() && !failed.can_change_mode);
    assert!(failed.mode_unavailable.is_some() && failed.mode_failure.is_some());
    let unknown = view(&read(signed_in(), Ok(documents()), Ok(mode("hybrid"))));
    assert_eq!(
        unknown.mode_unknown.as_deref(),
        Some("Stored as \"hybrid\".")
    );
    assert!(unknown.modes.iter().all(|m| !m.selected));
}

/// A viewer reads the documents and the mode, and is offered no control; the
/// core refuses a viewer's writes too.
#[test]
fn a_viewer_reads_and_changes_nothing() {
    let session = read(viewer(), Ok(documents()), Ok(mode("linked")));
    assert_eq!(
        route(&session),
        Route::Workspace(WorkspaceSection::Knowledge)
    );
    let shown = view(&session);
    assert!(shown.viewer_note.is_some());
    assert!(!shown.show_add && !shown.show_delete && !shown.can_delete);
    assert!(!shown.can_change_mode);
    assert_eq!(shown.modes.len(), 1, "the stored mode alone");
    assert_eq!(shown.documents.len(), 2);
    let tried = act(
        act(
            act(
                session,
                KnowledgeAction::AskDelete {
                    document_id: "doc_contract_ready".to_owned(),
                },
            ),
            KnowledgeAction::Confirm,
        ),
        KnowledgeAction::SelectMode {
            mode: KnowledgeModeChoice::Internal,
        },
    );
    assert!(!tried.pending.iter().any(|e| matches!(
        e,
        Effect::DeleteKnowledgeDocument { .. } | Effect::SetKnowledgeMode { .. }
    )));
}

/// A half-typed document is not an unsaved change (the core's rule): leaving
/// never asks.
#[test]
fn leaving_a_draft_does_not_ask() {
    let drafted = drafted(internal());
    for leave in [
        UiEvent::Back,
        UiEvent::Refresh,
        UiEvent::Navigate {
            destination: NavDestination::Overview,
        },
    ] {
        assert!(
            !leave
                .clone()
                .events()
                .iter()
                .any(|event| leaves_unsaved(&drafted.model, event)),
            "{leave:?}"
        );
        let mut held = Held::default();
        assert_eq!(
            held.pass(&drafted.model, leave.clone().events()),
            leave.events()
        );
        assert!(held.question().is_none());
    }
}
