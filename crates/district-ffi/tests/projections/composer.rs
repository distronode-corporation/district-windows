//! The conversation's reply box (src/composer.rs).

use district_core::{MAX_ATTACHMENT_BYTES, ThreadEvent, ThreadScreen};
use district_ffi::composer::{ComposerAction, ComposerView, PickedFileView};
use district_ffi::{ReportAvailability, ReportTarget};

use super::inbox::{conversations, inbox, open_thread, timeline, with_unread};
use super::*;

/// The thread every case opens: a contact's, answered by text message.
const THREAD: &str = "contact:contact_contract_1";

/// The start of a PNG, as a real file begins.
const PNG: &[u8] = b"\x89PNG\r\n\x1a\n\0\0\0\rIHDR";
/// The start of a HEIC photo, as an iPhone saves one.
const HEIC: &[u8] = b"\0\0\0\x18ftypheic\0\0\0\0mif1heic";

/// This area's snapshot cases.
pub(crate) fn cases() -> Vec<Case> {
    vec![
        ("composer-empty", ready()),
        ("composer-draft-restored", ready_with("district-draft.json")),
        ("composer-typing", typed(ready(), "On my way.")),
        (
            "composer-uploading",
            attach(typed(ready(), "Roof:"), "roof.png", PNG),
        ),
        (
            "composer-attachments",
            with_images(typed(ready(), "Roof:"), 2),
        ),
        (
            "composer-refused-heic",
            attach(typed(ready(), "Roof:"), "IMG_0001.jpg", HEIC),
        ),
        (
            "composer-refused-sixth",
            attach(with_images(typed(ready(), "Roof:"), 5), "sixth.png", PNG),
        ),
        (
            "composer-refused-too-large",
            attach(typed(ready(), "Roof:"), "large.png", &oversized()),
        ),
        (
            "composer-unreadable",
            act(ready(), ComposerAction::AttachFailed),
        ),
        (
            "composer-sending",
            act(typed(ready(), "On my way."), ComposerAction::Send),
        ),
        ("composer-sent", sent(Ok(()))),
        ("composer-send-failed", sent(Err(server_error()))),
        (
            "composer-drafting",
            act(ready(), ComposerAction::DraftReply),
        ),
        ("composer-draft-ready", ai_draft(Ok(()))),
        ("composer-draft-failed", ai_draft(Err(server_error()))),
        ("composer-email-thread", email_thread()),
    ]
}

/// `session` after the member did `action` in the reply box.
fn act(session: Session, action: ComposerAction) -> Session {
    session.ui(UiEvent::Composer { action })
}

fn typed(session: Session, text: &str) -> Session {
    act(
        session,
        ComposerAction::Compose {
            text: text.to_owned(),
        },
    )
}

fn file(name: &str, bytes: &[u8]) -> PickedFileView {
    PickedFileView {
        file_name: name.to_owned(),
        size: bytes.len() as u64,
        bytes: bytes.to_vec(),
    }
}

fn attach(session: Session, name: &str, bytes: &[u8]) -> Session {
    act(
        session,
        ComposerAction::Attach {
            file: file(name, bytes),
        },
    )
}

/// A PNG one byte larger than the service takes, as C# reads one to its cap.
fn oversized() -> Vec<u8> {
    let mut bytes = PNG.to_vec();
    bytes.resize(MAX_ATTACHMENT_BYTES + 1, 0);
    bytes
}

/// The contract thread opened and read, with the saved draft `draft` (a
/// fixture name) read with it.
fn ready_with(draft: &str) -> Session {
    timeline(open_thread(signed_in()), "district-timeline.json").answer(
        |e| matches!(e, Effect::LoadDraft { .. }),
        |ticket| Event::DraftLoaded {
            ticket,
            result: Ok(contracts::read(draft)),
        },
    )
}

/// The contract thread opened and read, with no saved draft.
fn ready() -> Session {
    ready_with("district-draft-null.json")
}

/// `session` with `count` images uploaded, each at its own address.
fn with_images(mut session: Session, count: usize) -> Session {
    for n in 1..=count {
        let mut upload = contracts::json("district-media-upload.json");
        upload["media"]["url"] = json!(format!("https://www.distronode.test/api/media/roof-{n}"));
        session = attach(session, &format!("roof-{n}.png"), PNG).answer(
            |e| matches!(e, Effect::UploadMedia { .. }),
            |ticket| Event::MediaUploaded {
                ticket,
                result: Ok(contracts::decode("media upload", upload)),
            },
        );
    }
    session
}

/// A message sent from the box, answered `Ok` or with `error`.
fn sent(result: Result<(), ApiError>) -> Session {
    act(typed(ready(), "On my way."), ComposerAction::Send).answer(
        |e| matches!(e, Effect::SendMessage { .. }),
        |ticket| Event::MessageSent {
            ticket,
            result: result.map(|()| contracts::read("district-message-send.json")),
        },
    )
}

/// A reply asked of the model, answered `Ok` with the contract's or with
/// `error`.
fn ai_draft(result: Result<(), ApiError>) -> Session {
    act(ready(), ComposerAction::DraftReply).answer(
        |e| matches!(e, Effect::GenerateAiDraft { .. }),
        |ticket| Event::AiDraftWritten {
            ticket,
            result: result.map(|()| contracts::read("district-ai-draft.json")),
        },
    )
}

/// The contract thread when its contact can be reached only by email: the box
/// shows, without Attach.
fn email_thread() -> Session {
    let mut list = contracts::json("district-conversations.json");
    list["conversations"][0]["canSms"] = json!(false);
    with_unread(conversations(inbox(signed_in()), list))
        .ui(UiEvent::OpenThread {
            thread_key: THREAD.to_owned(),
        })
        .answer(
            |e| matches!(e, Effect::LoadTimeline { .. }),
            |ticket| Event::TimelineLoaded {
                ticket,
                result: Ok(contracts::read("district-timeline.json")),
            },
        )
}

/// The reply box and the thread's note in `session`.
fn reply_box(session: &Session) -> (Option<ComposerView>, String) {
    let ScreenView::Thread { view } = screen_view(&session.model) else {
        panic!("a thread is open");
    };
    (view.composer, view.read_only_note)
}

fn shown(session: &Session) -> ComposerView {
    reply_box(session).0.expect("the reply box shows")
}

#[test]
fn each_action_is_its_core_event() {
    let one = |action| UiEvent::Composer { action }.events();
    assert_eq!(
        one(ComposerAction::Compose {
            text: "On my way.".to_owned(),
        }),
        [Event::Thread(ThreadEvent::Compose("On my way.".to_owned()))]
    );
    assert_eq!(
        one(ComposerAction::Send),
        [Event::Thread(ThreadEvent::Send)]
    );
    assert_eq!(
        one(ComposerAction::AttachFailed),
        [Event::Thread(ThreadEvent::AttachFailed)]
    );
    assert_eq!(
        one(ComposerAction::RemoveAttachment {
            url: "https://www.distronode.test/api/media/roof-1".to_owned(),
        }),
        [Event::Thread(ThreadEvent::RemoveAttachment(
            "https://www.distronode.test/api/media/roof-1".to_owned()
        ))]
    );
    assert_eq!(
        one(ComposerAction::DraftReply),
        [Event::Thread(ThreadEvent::DraftReply)]
    );
    assert_eq!(
        one(ComposerAction::DismissFailure),
        [Event::Thread(ThreadEvent::DismissFailure)]
    );
}

/// An attached file reaches the core named without its folder and typed by
/// its bytes, not its name.
#[test]
fn an_attachment_is_typed_by_its_bytes() {
    let events = UiEvent::Composer {
        action: ComposerAction::Attach {
            file: file("C:\\Users\\a\\Pictures\\IMG_0001.jpg", HEIC),
        },
    }
    .events();
    let [Event::Thread(ThreadEvent::Attach(picked))] = events.as_slice() else {
        panic!("one attach: {events:?}");
    };
    assert_eq!(picked.file_name, "IMG_0001.jpg");
    assert_eq!(picked.mime_type, "image/heic");
    assert_eq!(picked.bytes, HEIC);
}

/// The box shows for a member who may reply, and says nothing in place of
/// itself; the navigation pane is unchanged by it.
#[test]
fn a_member_gets_the_box() {
    let session = ready();
    let (composer, note) = reply_box(&session);
    let composer = composer.expect("the reply box shows");
    assert!(composer.show_attach && composer.can_attach);
    assert!(!composer.can_send, "nothing typed");
    assert!(composer.can_draft_reply);
    assert_eq!(note, "");
    // The pane's 2.0 entries are nav.rs's to pin; the box adds no entry and
    // the thread keeps the inbox highlighted.
    let offered = offered(&session);
    assert!(
        [
            NavDestination::Overview,
            NavDestination::Inbox,
            NavDestination::Calls,
            NavDestination::Contacts,
            NavDestination::Account,
        ]
        .iter()
        .all(|entry| offered.contains(entry)),
        "{offered:?}"
    );
    assert_eq!(
        shell_json(&session)["nav_selected"],
        json!(NavDestination::Inbox)
    );
}

/// A viewer gets no box, and the core's words for why; so does a thread
/// opened from a search result the inbox list does not hold, which has no
/// published reply address.
#[test]
fn no_box_where_the_member_cannot_reply() {
    let viewing = timeline(open_thread(viewer()), "district-timeline.json");
    assert_eq!(
        reply_box(&viewing),
        (None, ThreadScreen::READ_ONLY_ROLE.to_owned())
    );
    let mut hits = super::inbox::search_hits();
    hits["results"][0]["threadKey"] = json!("contact:contact_elsewhere");
    let searched = super::inbox::searched(
        with_unread(inbox(signed_in())).answer(
            |e| matches!(e, Effect::LoadConversations { .. }),
            |ticket| Event::ConversationsLoaded {
                ticket,
                result: Ok(contracts::read("district-conversations.json")),
            },
        ),
        "roof",
        Ok(hits),
    )
    .ui(UiEvent::OpenThread {
        thread_key: "contact:contact_elsewhere".to_owned(),
    });
    assert_eq!(
        reply_box(&searched),
        (None, ThreadScreen::NO_REPLY_TARGET.to_owned())
    );
}

/// Send works once something is typed, and not again while the message is
/// on its way: a second press asks for no second send.
#[test]
fn one_press_is_one_message() {
    let typed = typed(ready(), "On my way.");
    assert!(shown(&typed).can_send);
    let sending = act(act(typed, ComposerAction::Send), ComposerAction::Send);
    assert!(!shown(&sending).can_send);
    let sends = sending
        .pending
        .iter()
        .filter(|e| matches!(e, Effect::SendMessage { .. }))
        .count();
    assert_eq!(sends, 1);
}

/// A sent message leaves the box empty, and a failed one keeps the text and
/// says why until dismissed.
#[test]
fn a_failed_send_keeps_the_text_until_dismissed() {
    assert_eq!(shown(&sent(Ok(()))).text, "");
    let failed = sent(Err(server_error()));
    let composer = shown(&failed);
    assert_eq!(composer.text, "On my way.");
    assert!(composer.failure.is_some());
    assert_eq!(
        shown(&act(failed, ComposerAction::DismissFailure)).failure,
        None
    );
}

/// The images are named in the order picked, a removed one goes, and the
/// names close up.
#[test]
fn an_image_can_be_taken_off() {
    let held = with_images(typed(ready(), "Roof:"), 3);
    let names: Vec<_> = shown(&held)
        .attachments
        .into_iter()
        .map(|a| (a.name, a.url))
        .collect();
    assert_eq!(names[0].0, "Image 1");
    assert_eq!(names[2].0, "Image 3");
    let removed = act(
        held,
        ComposerAction::RemoveAttachment {
            url: names[0].1.clone(),
        },
    );
    let left: Vec<_> = shown(&removed)
        .attachments
        .into_iter()
        .map(|a| (a.name, a.url))
        .collect();
    assert_eq!(
        left,
        [
            ("Image 1".to_owned(), names[1].1.clone()),
            ("Image 2".to_owned(), names[2].1.clone()),
        ]
    );
}

/// What the model wrote goes into the box as text to edit, the button can be
/// pressed again, and Report on it raises a report that names its kind and
/// the conversation's contact.
#[test]
fn an_ai_draft_is_text_to_edit_and_can_be_reported() {
    let drafting = act(ready(), ComposerAction::DraftReply);
    assert!(!shown(&drafting).can_draft_reply);
    let written = ai_draft(Ok(()));
    let composer = shown(&written);
    assert_eq!(
        composer.text,
        "Thanks for waiting - Thursday at 2pm is confirmed. See you then."
    );
    assert!(composer.can_draft_reply && composer.can_send);
    assert_eq!(composer.ai_draft.report, ReportAvailability::InApp);
    let events = UiEvent::Report {
        target: ReportTarget::AiDraft {
            thread_key: THREAD.to_owned(),
        },
        note: String::new(),
    }
    .events();
    let message = format!("{events:?}");
    assert!(message.contains("Contact: contact_contract_1"), "{message}");
    assert!(!message.contains("Thursday at 2pm"), "{message}");
}

/// A reply typed and not yet saved (the save waits for the typing to stop) is
/// saved when the app quits, rather than lost: district-core-rust 2.0.0's
/// `Event::Quitting` writes it, and `Core::shutdown` runs what Quitting asks
/// for within its budget (core.rs).
#[test]
fn quitting_saves_a_reply_still_waiting_to_be_saved() {
    let saves = |session: &Session| -> Vec<(String, String)> {
        session
            .pending
            .iter()
            .filter_map(|effect| match effect {
                Effect::SaveDraft { draft, .. } => {
                    Some((draft.thread_key.clone(), draft.body.clone()))
                }
                _ => None,
            })
            .collect()
    };
    let typing = typed(ready(), "On my way.");
    assert_eq!(saves(&typing), [], "the save waits for the typing to stop");
    let quit = typing.send(Event::Quitting);
    assert_eq!(saves(&quit), [(THREAD.to_owned(), "On my way.".to_owned())]);

    // With nothing typed, quitting saves no reply.
    assert_eq!(saves(&ready().send(Event::Quitting)), []);
}
