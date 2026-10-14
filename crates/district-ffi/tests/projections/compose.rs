//! Starting a conversation from the inbox (src/compose.rs).

use district_ffi::{ComposeAction, ComposeView, InboxView};

use super::contacts::contacts_page;
use super::inbox::{inbox, inbox_loaded};
use super::*;

/// This area's snapshot cases.
pub(crate) fn cases() -> Vec<Case> {
    vec![
        ("compose-loading", open(inbox_loaded())),
        ("compose-contacts", listed()),
        (
            "compose-contacts-failed",
            open(inbox_loaded()).answer(
                |e| matches!(e, Effect::LoadContacts { .. }),
                |ticket| Event::ContactsLoaded {
                    ticket,
                    result: Err(server_error()),
                },
            ),
        ),
        ("compose-contact-chosen", chosen()),
        ("compose-email-typed", typed_email()),
        ("compose-sending", sending()),
        ("compose-failed", sent(Err(server_error()))),
        ("compose-sent", sent(Ok(()))),
        ("compose-viewer", inbox(viewer())),
    ]
}

fn act(session: Session, action: ComposeAction) -> Session {
    session.ui(UiEvent::Compose { action })
}

/// The form open, its contacts being read.
fn open(session: Session) -> Session {
    act(session, ComposeAction::Open)
}

/// The form with the contract's contacts offered.
fn listed() -> Session {
    contacts_page(
        open(inbox_loaded()),
        contracts::json("district-contacts.json"),
    )
}

/// The first contact chosen, a text message and a body typed.
fn chosen() -> Session {
    let session = act(
        listed(),
        ComposeAction::ChooseContact {
            contact_id: "contact_contract_1".to_owned(),
        },
    );
    act(
        session,
        ComposeAction::EditBody {
            text: "Confirmed for Thursday at 2pm.".to_owned(),
        },
    )
}

/// An email address typed in and used: an email, which needs a subject.
fn typed_email() -> Session {
    let session = act(
        listed(),
        ComposeAction::Query {
            text: "new@example.com".to_owned(),
        },
    );
    let session = act(session, ComposeAction::UseTyped);
    act(
        session,
        ComposeAction::EditBody {
            text: "Hello.".to_owned(),
        },
    )
}

fn sending() -> Session {
    act(chosen(), ComposeAction::Send)
}

/// The message sent, answered `Ok` with the contract's or with `error`.
fn sent(result: Result<(), ApiError>) -> Session {
    sending().answer(
        |e| matches!(e, Effect::SendMessage { .. }),
        |ticket| Event::MessageSent {
            ticket,
            result: result.map(|()| contracts::read("district-message-send.json")),
        },
    )
}

fn inbox_of(session: &Session) -> InboxView {
    match screen_view(&session.model) {
        ScreenView::Inbox { view } => view,
        other => panic!("not the inbox: {other:?}"),
    }
}

fn form(session: &Session) -> ComposeView {
    inbox_of(session).compose.expect("the form is open")
}

/// "New" is offered to a member who can send, and not to a viewer, whose
/// "New" opens nothing.
#[test]
fn new_is_offered_to_a_member_who_can_send() {
    let entry = inbox_of(&inbox_loaded()).new_message;
    assert!(entry.offered);
    assert!(!entry.label.is_empty());
    let viewer = inbox(viewer());
    assert!(!inbox_of(&viewer).new_message.offered);
    assert_eq!(inbox_of(&open(viewer)).compose, None);
}

/// What is typed filters the contacts by name, number or address, and a
/// typed address can be used as it is.
#[test]
fn typing_filters_the_contacts_and_offers_what_is_typed() {
    let all = form(&listed());
    assert_eq!(all.suggestions.len(), 2);
    assert_eq!(all.typed_action, None);
    let filtered = form(&act(
        listed(),
        ComposeAction::Query {
            text: "sparse".to_owned(),
        },
    ));
    assert_eq!(filtered.suggestions.len(), 1);
    assert_eq!(filtered.suggestions[0].contact_id, "contact_contract_2");
    assert!(filtered.typed_action.is_some());
}

/// A contact with a number and an address is reached on either, a text
/// message first; an email needs a subject before it can be sent.
#[test]
fn a_contact_offers_its_channels_and_an_email_needs_a_subject() {
    let text = form(&chosen());
    assert_eq!(text.recipient.as_deref(), Some("Contract Test Caller"));
    assert_eq!(text.channels.len(), 2);
    assert_eq!(text.channel, 0);
    assert!(!text.requires_subject);
    assert!(text.can_send);

    let email = act(chosen(), ComposeAction::SelectChannel { index: 1 });
    let view = form(&email);
    assert_eq!(view.channel, 1);
    assert!(view.requires_subject);
    assert!(!view.can_send);
    let titled = act(
        email,
        ComposeAction::EditSubject {
            text: "Thursday".to_owned(),
        },
    );
    assert!(form(&titled).can_send);

    let typed = form(&typed_email());
    assert_eq!(typed.recipient.as_deref(), Some("new@example.com"));
    assert!(typed.requires_subject);
    assert!(!typed.can_send);
}

/// Send goes once; while it is on its way the form takes nothing, and a
/// failure keeps what was typed.
#[test]
fn a_send_goes_once_and_a_failure_keeps_the_form() {
    let session = sending();
    let sends = |session: &Session| {
        session
            .pending
            .iter()
            .filter(|e| matches!(e, Effect::SendMessage { .. }))
            .count()
    };
    assert_eq!(sends(&session), 1);
    assert!(form(&session).sending);
    let again = act(session, ComposeAction::Send);
    assert_eq!(sends(&again), 1);
    let cancelled = act(again, ComposeAction::Cancel);
    assert!(inbox_of(&cancelled).compose.is_some());

    let failed = form(&sent(Err(server_error())));
    assert!(!failed.sending);
    assert!(failed.failure.is_some());
    assert_eq!(failed.body, "Confirmed for Thursday at 2pm.");
}

/// Once sent, the form closes and the core looks up the message's thread,
/// which it then opens.
#[test]
fn a_sent_message_opens_its_thread() {
    let session = sent(Ok(()));
    assert_eq!(inbox_of(&session).compose, None);
    let found = session.answer(
        |e| matches!(e, Effect::FindMessageThread { .. }),
        |ticket| Event::MessageThreadFound {
            ticket,
            result: Ok(contracts::read("district-message-thread.json")),
        },
    );
    assert_eq!(
        route(&found),
        Route::Thread {
            thread_key: "contact:contact_contract_1".to_owned()
        }
    );
}

/// Cancel closes the form.
#[test]
fn cancel_closes_the_form() {
    let closed = act(listed(), ComposeAction::Cancel);
    assert_eq!(inbox_of(&closed).compose, None);
}
