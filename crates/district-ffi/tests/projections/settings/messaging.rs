//! The Messaging accounts section (src/settings/messaging.rs).

use district_core::{
    Effect, Event, MessagingEvent, MessagingFormEdit, MessagingWrite, Route, WorkspaceSection,
};
use district_ffi::settings::SectionStatus;
use district_ffi::settings::messaging::{
    KeyCheckOutcome, KeyField, MessagingAction, MessagingCarrier, MessagingSource, MessagingView,
    SenderChannel,
};
use district_ffi::{ScreenView, UiEvent, screen_view};
use serde_json::Value;

use super::super::{Case, Session, contracts, route, server_error, signed_in, viewer};

/// This area's snapshot cases.
pub(crate) fn cases() -> Vec<Case> {
    vec![
        ("messaging-loading", open(signed_in())),
        ("messaging-loaded", ready(signed_in())),
        (
            "messaging-none",
            loaded(
                open(signed_in()),
                Ok(contracts::json("district-messaging-unmanaged.json")),
            ),
        ),
        ("messaging-failed", loaded(open(signed_in()), Err(()))),
        ("messaging-viewer", ready(viewer())),
        (
            "messaging-add-open",
            act(ready(signed_in()), MessagingAction::StartAdd),
        ),
        ("messaging-add-typed", typed(signed_in())),
        ("messaging-checking", checking(signed_in())),
        (
            "messaging-check-passed",
            tested(
                checking(signed_in()),
                Ok(contracts::json("district-messaging-test.json")),
            ),
        ),
        (
            "messaging-check-refused",
            tested(
                checking(signed_in()),
                Ok(contracts::json("district-messaging-test-rejected.json")),
            ),
        ),
        (
            "messaging-check-unreachable",
            tested(checking(signed_in()), Err(())),
        ),
        ("messaging-saving", saving(signed_in())),
        ("messaging-saved", written(saving(signed_in()), Ok(()))),
        (
            "messaging-save-failed",
            written(saving(signed_in()), Err(())),
        ),
        ("messaging-edit-open", editing(signed_in())),
        (
            "messaging-edit-carrier-switched",
            act(
                editing(signed_in()),
                MessagingAction::SetCarrier {
                    carrier: MessagingCarrier::Sinch,
                },
            ),
        ),
        (
            "messaging-confirm-remove",
            act(
                ready(signed_in()),
                MessagingAction::AskRemove {
                    account_id: "acct-twilio".to_owned(),
                },
            ),
        ),
        ("messaging-removing", removing(signed_in())),
        (
            "messaging-owner-number-typed",
            act(
                ready(signed_in()),
                MessagingAction::EditOwnerNumber {
                    number: "+1 416 555 0144".to_owned(),
                },
            ),
        ),
    ]
}

fn act(session: Session, action: MessagingAction) -> Session {
    session.ui(UiEvent::Messaging { action })
}

fn open(session: Session) -> Session {
    act(session, MessagingAction::Open)
}

fn loaded(session: Session, result: Result<Value, ()>) -> Session {
    session.answer(
        |e| matches!(e, Effect::LoadMessaging { .. }),
        |ticket| Event::MessagingLoaded {
            ticket,
            result: result
                .map(|value| contracts::decode("messaging", value))
                .map_err(|()| server_error()),
        },
    )
}

/// The section, read from the core's fixture.
fn ready(session: Session) -> Session {
    loaded(
        open(session),
        Ok(contracts::json("district-messaging.json")),
    )
}

/// A new Twilio account, every key typed.
fn typed(session: Session) -> Session {
    let session = act(ready(session), MessagingAction::StartAdd);
    let session = act(
        session,
        MessagingAction::EditLabel {
            label: "Front desk".to_owned(),
        },
    );
    let session = act(
        session,
        MessagingAction::EditKey {
            field: KeyField::AccountSid,
            value: "AC0000000000000000000000000000test".to_owned(),
        },
    );
    act(
        session,
        MessagingAction::EditKey {
            field: KeyField::AuthToken,
            value: "auth-token-typed".to_owned(),
        },
    )
}

fn checking(session: Session) -> Session {
    act(typed(session), MessagingAction::CheckKeys)
}

fn tested(session: Session, result: Result<Value, ()>) -> Session {
    session.answer(
        |e| matches!(e, Effect::TestMessagingCredentials { .. }),
        |ticket| Event::MessagingCredentialsTested {
            ticket,
            result: result
                .map(|value| contracts::decode("messaging test", value))
                .map_err(|()| server_error()),
        },
    )
}

fn saving(session: Session) -> Session {
    act(typed(session), MessagingAction::Save)
}

fn written(session: Session, result: Result<(), ()>) -> Session {
    session.answer(
        |e| matches!(e, Effect::WriteMessaging { .. }),
        |ticket| Event::SettingsWritten {
            ticket,
            result: result.map_err(|()| server_error()),
        },
    )
}

fn editing(session: Session) -> Session {
    act(
        ready(session),
        MessagingAction::StartEdit {
            account_id: "acct-twilio".to_owned(),
        },
    )
}

fn removing(session: Session) -> Session {
    act(
        act(
            ready(session),
            MessagingAction::AskRemove {
                account_id: "acct-twilio".to_owned(),
            },
        ),
        MessagingAction::ConfirmRemove,
    )
}

fn view(session: &Session) -> MessagingView {
    let ScreenView::Messaging { view } = screen_view(&session.model) else {
        panic!("not the messaging section");
    };
    view
}

/// Built, the section opens and reads the accounts.
#[test]
fn built_it_opens_and_reads() {
    let session = ready(signed_in());
    assert_eq!(
        route(&session),
        Route::Workspace(WorkspaceSection::Messaging)
    );
    let view = view(&session);
    assert_eq!(view.status, SectionStatus::Ready);
    assert_eq!(view.title, "Messaging accounts");
    assert_eq!(view.accounts.len(), 2);
    assert!(view.accounts[1].is_default);
    assert_eq!(view.channels.len(), 3);
    assert_eq!(view.channels[0].picker.selected_label, "Twilio (main)");
    assert_eq!(
        view.channels[1].picker.selected_label,
        "The default account"
    );
}

/// No typed key is ever projected: a secret box says only whether it is
/// filled, and a key typed never appears anywhere on the screen.
#[test]
fn a_typed_key_is_never_projected() {
    for session in [
        typed(signed_in()),
        checking(signed_in()),
        saving(signed_in()),
    ] {
        let json = serde_json::to_string(&screen_view(&session.model)).unwrap();
        assert!(!json.contains("auth-token-typed"), "{json}");
        assert!(
            !json.contains("AC0000000000000000000000000000test"),
            "{json}"
        );
        let form = view(&session).form.expect("the form");
        assert!(
            form.keys
                .iter()
                .all(|key| key.filled && key.value.is_empty())
        );
    }
    // Saved: the form closes, and what was typed goes with it.
    let saved = written(saving(signed_in()), Ok(()));
    assert_eq!(view(&saved).form, None);
}

/// The save carries what was typed to the core's write, once.
#[test]
fn the_save_sends_the_typed_account() {
    let session = saving(signed_in());
    let writes: Vec<_> = session
        .pending
        .iter()
        .filter_map(|effect| match effect {
            Effect::WriteMessaging {
                write: MessagingWrite::SaveAccount(save),
                ..
            } => Some(save.clone()),
            _ => None,
        })
        .collect();
    assert_eq!(writes.len(), 1);
    assert_eq!(writes[0].label.as_deref(), Some("Front desk"));
}

/// Changing an existing account's carrier drops its stored keys: the form
/// says so and wants every key, and its boxes are empty.
#[test]
fn a_carrier_switch_wants_every_key() {
    let form = view(&act(
        editing(signed_in()),
        MessagingAction::SetCarrier {
            carrier: MessagingCarrier::Telnyx,
        },
    ))
    .form
    .expect("the form");
    assert!(form.carrier_switch.is_some());
    assert!(!form.can_save);
    assert_eq!(form.keys.len(), 1);
    assert!(!form.keys[0].filled);
    let kept = view(&editing(signed_in())).form.expect("the form");
    assert_eq!(kept.keys_note, "Leave blank to keep the saved value.");
    assert!(kept.can_save);
}

/// The check's answers read as Linux words them.
#[test]
fn the_check_says_what_the_carrier_said() {
    let check = |session: Session| view(&session).form.and_then(|form| form.test);
    assert_eq!(
        check(checking(signed_in())).map(|test| test.outcome),
        Some(KeyCheckOutcome::Running)
    );
    let passed = check(tested(
        checking(signed_in()),
        Ok(contracts::json("district-messaging-test.json")),
    ))
    .unwrap();
    assert_eq!(passed.outcome, KeyCheckOutcome::Passed);
    assert_eq!(
        passed.message,
        "The carrier accepted these keys, for Distronode Contract."
    );
    let refused = check(tested(
        checking(signed_in()),
        Ok(contracts::json("district-messaging-test-rejected.json")),
    ))
    .unwrap();
    assert_eq!(refused.outcome, KeyCheckOutcome::Refused);
    let unreachable = check(tested(checking(signed_in()), Err(()))).unwrap();
    assert_eq!(unreachable.outcome, KeyCheckOutcome::Unreachable);
}

/// A viewer reads the accounts, is told why nothing changes, and the core
/// refuses their changes.
#[test]
fn a_viewer_reads_and_changes_nothing() {
    let session = ready(viewer());
    let shown = view(&session);
    assert!(shown.viewer_note.is_some());
    assert!(!shown.offers_add && !shown.channels_editable);
    assert_eq!(shown.creator, None);
    assert!(shown.accounts.iter().all(|account| !account.offers_edit
        && !account.offers_remove
        && !account.offers_make_default));
    let tried = act(session, MessagingAction::StartAdd);
    assert_eq!(view(&tried).form, None);
}

#[test]
fn each_action_is_its_core_event() {
    let one = |action| UiEvent::Messaging { action }.events();
    let messaging = |event| [Event::Messaging(event)];
    let form = |edit| [Event::Messaging(MessagingEvent::Form(edit))];
    assert_eq!(
        one(MessagingAction::Open),
        [Event::Navigate(Route::Workspace(
            WorkspaceSection::Messaging
        ))]
    );
    assert_eq!(
        one(MessagingAction::StartAdd),
        messaging(MessagingEvent::StartAdd)
    );
    assert_eq!(
        one(MessagingAction::StartEdit {
            account_id: "a".to_owned()
        }),
        messaging(MessagingEvent::StartEdit {
            account_id: "a".to_owned()
        })
    );
    assert_eq!(
        one(MessagingAction::SetCarrier {
            carrier: MessagingCarrier::Sinch
        }),
        form(MessagingFormEdit::Provider(
            district_model::MessagingProvider::Sinch
        ))
    );
    assert_eq!(
        one(MessagingAction::SetSource {
            source: MessagingSource::Managed
        }),
        form(MessagingFormEdit::CredentialSource(
            district_model::MessagingCredentialSource::Managed
        ))
    );
    assert_eq!(
        one(MessagingAction::EditLabel {
            label: "x".to_owned()
        }),
        form(MessagingFormEdit::Label("x".to_owned()))
    );
    assert_eq!(
        one(MessagingAction::EditKey {
            field: KeyField::ApiKey,
            value: "k".to_owned()
        }),
        form(MessagingFormEdit::Credential {
            field: district_core::CredentialField::ApiKey,
            value: district_core::SecretText::new("k"),
        })
    );
    assert_eq!(
        one(MessagingAction::EditNumbers {
            numbers: "n".to_owned()
        }),
        form(MessagingFormEdit::PhoneNumbers("n".to_owned()))
    );
    assert_eq!(
        one(MessagingAction::SetMakeDefault { on: true }),
        form(MessagingFormEdit::MakeDefault(true))
    );
    assert_eq!(
        one(MessagingAction::CloseForm),
        messaging(MessagingEvent::CloseForm)
    );
    assert_eq!(
        one(MessagingAction::Save),
        messaging(MessagingEvent::SaveAccount)
    );
    assert_eq!(
        one(MessagingAction::CheckKeys),
        messaging(MessagingEvent::TestCredentials)
    );
    assert_eq!(
        one(MessagingAction::MakeDefault {
            account_id: "a".to_owned()
        }),
        messaging(MessagingEvent::SetDefault {
            account_id: "a".to_owned()
        })
    );
    assert_eq!(
        one(MessagingAction::SetChannelSender {
            channel: SenderChannel::Whatsapp,
            account_id: "a".to_owned()
        }),
        messaging(MessagingEvent::SetChannelDefault {
            channel: district_model::MessagingChannel::Whatsapp,
            account_id: "a".to_owned()
        })
    );
    assert_eq!(
        one(MessagingAction::AskRemove {
            account_id: "a".to_owned()
        }),
        messaging(MessagingEvent::AskDelete {
            account_id: "a".to_owned()
        })
    );
    assert_eq!(
        one(MessagingAction::ConfirmRemove),
        messaging(MessagingEvent::ConfirmDelete)
    );
    assert_eq!(
        one(MessagingAction::CancelRemove),
        messaging(MessagingEvent::CancelDelete)
    );
    assert_eq!(
        one(MessagingAction::EditOwnerNumber {
            number: "1".to_owned()
        }),
        messaging(MessagingEvent::EditCreatorCell("1".to_owned()))
    );
    assert_eq!(
        one(MessagingAction::SaveOwnerNumber),
        messaging(MessagingEvent::SaveCreatorCell)
    );
    assert_eq!(
        one(MessagingAction::DismissNotice),
        messaging(MessagingEvent::DismissNotice)
    );
}
