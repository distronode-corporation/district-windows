//! The blocked callers (src/blocked.rs): the list, and unblocking a caller
//! after the core's question.

use district_core::{ContactWritten, ContactsEvent};
use district_ffi::blocked::{BLOCKED_EMPTY_BODY, BlockedAction, BlockedView};
use district_model::ContactBlockResponse;

use super::contacts::{blocked_answer, the_contact_blocked};
use super::*;

fn act(session: Session, action: BlockedAction) -> Session {
    session.ui(UiEvent::Blocked { action })
}

fn open(session: Session) -> Session {
    act(session, BlockedAction::Open)
}

fn read(session: Session, blocked: Value) -> Session {
    session.answer(
        |e| matches!(e, Effect::LoadBlocked { .. }),
        |ticket| Event::BlockedLoaded {
            ticket,
            result: Ok(blocked_answer(blocked)),
        },
    )
}

/// Two callers: the fixture's contact, and one the receptionist could not
/// name (so the number is the name), plus a row that is no block at all.
fn two() -> Value {
    json!([
        the_contact_blocked(),
        {
            "contactId": "contact_contract_2",
            "name": "Unknown",
            "phoneNumber": "14165550181",
            "blockedAt": "2026-08-20T16:30:00.000Z"
        },
        {
            "contactId": "contact_contract_3",
            "name": "Unblocked long ago",
            "phoneNumber": null,
            "blockedAt": null
        }
    ])
}

fn listed() -> Session {
    read(open(signed_in()), two())
}

fn asked() -> Session {
    act(
        listed(),
        BlockedAction::AskUnblock {
            contact_id: "contact_contract_1".to_owned(),
        },
    )
}

fn unblocking() -> Session {
    act(asked(), BlockedAction::ConfirmUnblock)
}

fn unblocked(session: Session, result: Result<ContactWritten, ApiError>) -> Session {
    session.answer(
        |e| matches!(e, Effect::WriteContact { .. }),
        |ticket| Event::ContactWritten { ticket, result },
    )
}

/// This area's snapshot cases.
pub(crate) fn cases() -> Vec<Case> {
    vec![
        ("blocked-loading", open(signed_in())),
        ("blocked-loaded", listed()),
        ("blocked-empty", read(open(signed_in()), json!([]))),
        (
            "blocked-failed",
            open(signed_in()).answer(
                |e| matches!(e, Effect::LoadBlocked { .. }),
                |ticket| Event::BlockedLoaded {
                    ticket,
                    result: Err(server_error()),
                },
            ),
        ),
        ("blocked-confirming", asked()),
        ("blocked-unblocking", unblocking()),
        (
            "blocked-unblock-failed",
            unblocked(unblocking(), Err(server_error())),
        ),
        ("blocked-viewer", read(open(viewer()), two())),
    ]
}

fn view(session: &Session) -> BlockedView {
    match screen_view(&session.model) {
        ScreenView::BlockedContacts { view } => view,
        other => panic!("not the blocked callers: {other:?}"),
    }
}

#[test]
fn each_action_is_its_core_event() {
    for (action, event) in [
        (BlockedAction::Open, Event::Navigate(Route::BlockedContacts)),
        (
            BlockedAction::AskUnblock {
                contact_id: "c-1".to_owned(),
            },
            Event::Contacts(ContactsEvent::AskUnblock {
                contact_id: "c-1".to_owned(),
            }),
        ),
        (
            BlockedAction::ConfirmUnblock,
            Event::Contacts(ContactsEvent::ConfirmUnblock),
        ),
        (
            BlockedAction::CancelUnblock,
            Event::Contacts(ContactsEvent::CancelUnblock),
        ),
        (
            BlockedAction::DismissFailure,
            Event::Contacts(ContactsEvent::DismissUnblockFailure),
        ),
    ] {
        assert_eq!(UiEvent::Blocked { action }.events(), [event]);
    }
}

/// Built, the screen shows; the pane has no entry of its own (it is below
/// contacts), so the contacts entry stays selected.
#[test]
fn built_it_shows_below_contacts() {
    let session = listed();
    assert_eq!(route(&session), Route::BlockedContacts);
    let shell = shell_json(&session);
    assert_eq!(shell["nav_selected"], json!(NavDestination::Contacts));
}

/// The list shows only live blocks, each named as the core names it, with
/// the number only when it is not the name already.
#[test]
fn the_list_names_each_caller_once() {
    let shown = view(&listed());
    let rows: Vec<(&str, &str, Option<&str>)> = shown
        .rows
        .iter()
        .map(|row| {
            (
                row.contact_id.as_str(),
                row.name.as_str(),
                row.phone_number.as_deref(),
            )
        })
        .collect();
    assert_eq!(
        rows,
        [
            (
                "contact_contract_1",
                "Contract Test Caller",
                Some("+1 416 555 0142")
            ),
            ("contact_contract_2", "+1 416 555 0181", None),
        ]
    );
    assert!(shown.can_unblock);
    let empty = view(&read(open(signed_in()), json!([])));
    assert_eq!(empty.empty.unwrap().body, BLOCKED_EMPTY_BODY);
}

/// Unblocking asks first, sends once however often it is confirmed, and
/// takes the caller off the list when it lands; a failure shows beside the
/// list until dismissed.
#[test]
fn unblocking_asks_sends_once_and_lands() {
    let question = view(&asked()).confirming.unwrap();
    assert_eq!(question.action, "Unblock");
    assert_eq!(question.name, "Contract Test Caller");
    assert_eq!(
        view(&act(asked(), BlockedAction::CancelUnblock)).confirming,
        None
    );

    let twice = act(
        act(
            unblocking(),
            BlockedAction::AskUnblock {
                contact_id: "contact_contract_1".to_owned(),
            },
        ),
        BlockedAction::ConfirmUnblock,
    );
    let sent = twice
        .pending
        .iter()
        .filter(|e| matches!(e, Effect::WriteContact { .. }))
        .count();
    assert_eq!(sent, 1);
    assert!(view(&twice).rows[0].unblocking);

    let landed = unblocked(
        unblocking(),
        Ok(ContactWritten::Blocked(ContactBlockResponse {
            success: true,
            contact_id: "contact_contract_1".to_owned(),
            name: "Contract Test Caller".to_owned(),
            phone_number: Some("14165550142".to_owned()),
            blocked_at: None,
        })),
    );
    let after = view(&landed);
    assert_eq!(after.rows.len(), 1);
    assert_eq!(after.rows[0].contact_id, "contact_contract_2");

    let failed = unblocked(unblocking(), Err(server_error()));
    assert!(view(&failed).failure.is_some());
    assert!(!view(&failed).rows[0].unblocking);
    let dismissed = act(failed, BlockedAction::DismissFailure);
    assert_eq!(view(&dismissed).failure, None);
}

/// A viewer reads the list but is offered no Unblock, and the core asks
/// nothing if one is sent anyway.
#[test]
fn a_viewer_cannot_unblock() {
    let session = read(open(viewer()), two());
    assert!(!view(&session).can_unblock);
    let session = act(
        session,
        BlockedAction::AskUnblock {
            contact_id: "contact_contract_1".to_owned(),
        },
    );
    assert_eq!(view(&session).confirming, None);
}

/// Before anything is read, the screen is the default one: the heading and a
/// progress ring.
#[test]
fn the_screen_before_a_read_is_the_default() {
    let loading = view(&open(signed_in()));
    assert_eq!(
        loading,
        BlockedView {
            can_unblock: true,
            ..BlockedView::default()
        }
    );
}
