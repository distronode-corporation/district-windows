//! The contacts and one contact (src/contacts.rs): the list, the form adding
//! a contact, one contact, and its changes.

use district_api::ErrorDetail;
use district_core::{ContactForm, ContactWrite, ContactWritten, ContactsEvent};
use district_ffi::{ContactFormInput, ContactsAction};
use district_model::{ContactBlockResponse, ContactMutationResponse};

use super::*;

/// This area's snapshot cases.
pub(crate) fn cases() -> Vec<Case> {
    [
        contacts_cases(),
        contact_cases(),
        create_cases(),
        change_cases(),
    ]
    .into_iter()
    .flatten()
    .collect()
}

fn act(session: Session, action: ContactsAction) -> Session {
    session.ui(UiEvent::Contacts { action })
}

fn input(name: &str, phone_number: &str, email: &str) -> ContactFormInput {
    ContactFormInput {
        name: name.to_owned(),
        phone_number: phone_number.to_owned(),
        email: email.to_owned(),
    }
}

/// The contacts read, for the agency.
fn listed() -> Session {
    contacts_page(
        contacts(signed_in()),
        contracts::json("district-contacts.json"),
    )
}

/// The form adding a contact, open on the list.
fn creating() -> Session {
    act(listed(), ContactsAction::StartCreate)
}

/// The form filled in with a contact the core would send.
fn filled() -> Session {
    act(
        creating(),
        ContactsAction::EditCreate {
            form: input("Grace Hopper", "+1 416 555 0181", ""),
        },
    )
}

/// The service's answer to a contact that already exists.
fn conflict() -> ApiError {
    ApiError::Conflict(ErrorDetail {
        message: Some("A contact with this phone number already exists.".to_owned()),
        code: None,
        degraded_regions: Vec::new(),
    })
}

fn create_cases() -> Vec<Case> {
    vec![
        ("contact-create-empty", creating()),
        (
            "contact-create-invalid",
            act(
                creating(),
                ContactsAction::EditCreate {
                    form: input("Grace Hopper", " ", ""),
                },
            ),
        ),
        ("contact-create-ready", filled()),
        (
            "contact-create-saving",
            act(filled(), ContactsAction::SubmitCreate),
        ),
        (
            "contact-create-failed",
            act(filled(), ContactsAction::SubmitCreate).answer(
                |e| matches!(e, Effect::CreateContact { .. }),
                |ticket| Event::ContactCreated {
                    ticket,
                    result: Err(conflict()),
                },
            ),
        ),
        (
            "contacts-viewer",
            contacts_page(
                contacts(viewer()),
                contracts::json("district-contacts.json"),
            ),
        ),
    ]
}

/// The blocked list, as the service answers it: `blocked` callers, each a
/// contact id, a name, a number and when.
pub(crate) fn blocked_answer(blocked: Value) -> district_model::BlockedContactsResponse {
    contracts::decode(
        "blocked callers",
        json!({"success": true, "blocked": blocked}),
    )
}

/// The fixture's contact, blocked since September.
pub(crate) fn the_contact_blocked() -> Value {
    json!({
        "contactId": "contact_contract_1",
        "name": "Contract Test Caller",
        "phoneNumber": "14165550142",
        "blockedAt": "2026-09-01T09:00:00.000Z"
    })
}

/// The fixture's contact open and read, with the blocked list read as
/// `blocked`, for `session`'s member.
fn opened_as(session: Session, blocked: Value) -> Session {
    contact_loaded(
        open_contact(session),
        contracts::json("district-contact-detail.json"),
    )
    .answer(
        |e| matches!(e, Effect::LoadBlocked { .. }),
        |ticket| Event::BlockedLoaded {
            ticket,
            result: Ok(blocked_answer(blocked)),
        },
    )
}

/// The fixture's contact open, read and not blocked, for the agency.
fn opened() -> Session {
    opened_as(signed_in(), json!([]))
}

fn written(session: Session, result: Result<ContactWritten, ApiError>) -> Session {
    session.answer(
        |e| matches!(e, Effect::WriteContact { .. }),
        |ticket| Event::ContactWritten { ticket, result },
    )
}

fn change_cases() -> Vec<Case> {
    let editing = || act(opened(), ContactsAction::StartEdit);
    let renamed = || {
        act(
            editing(),
            ContactsAction::Edit {
                form: input("Ada Lovelace", "14165550142", "ada@example.com"),
            },
        )
    };
    vec![
        ("contact-writable", opened()),
        ("contact-edit-open", editing()),
        (
            "contact-edit-invalid",
            act(
                editing(),
                ContactsAction::Edit {
                    form: input("Ada Lovelace", "", ""),
                },
            ),
        ),
        (
            "contact-edit-saving",
            act(renamed(), ContactsAction::SaveEdit),
        ),
        (
            "contact-edit-failed",
            written(
                act(renamed(), ContactsAction::SaveEdit),
                Err(server_error()),
            ),
        ),
        (
            "contact-confirm-delete",
            act(opened(), ContactsAction::AskDelete),
        ),
        (
            "contact-deleting",
            act(
                act(opened(), ContactsAction::AskDelete),
                ContactsAction::Confirm,
            ),
        ),
        (
            "contact-confirm-clear-research",
            act(opened(), ContactsAction::AskClearResearch),
        ),
        (
            "contact-confirm-block",
            act(opened(), ContactsAction::AskBlock),
        ),
        (
            "contact-blocked",
            opened_as(signed_in(), json!([the_contact_blocked()])),
        ),
        (
            "contact-confirm-unblock",
            act(
                opened_as(signed_in(), json!([the_contact_blocked()])),
                ContactsAction::AskBlock,
            ),
        ),
        ("contact-enriching", act(opened(), ContactsAction::Enrich)),
        (
            "contact-change-failed",
            written(
                act(
                    act(opened(), ContactsAction::AskBlock),
                    ContactsAction::Confirm,
                ),
                Err(server_error()),
            ),
        ),
        (
            "contact-viewer-blocked",
            opened_as(viewer(), json!([the_contact_blocked()])),
        ),
    ]
}

pub(crate) fn contacts(session: Session) -> Session {
    session.ui(UiEvent::Navigate {
        destination: NavDestination::Contacts,
    })
}

pub(crate) fn contacts_page(session: Session, value: Value) -> Session {
    session.answer(
        |e| matches!(e, Effect::LoadContacts { .. }),
        |ticket| Event::ContactsLoaded {
            ticket,
            result: Ok(contracts::decode("contacts", value)),
        },
    )
}

/// The fixture's two contacts as the first page of ten.
pub(crate) fn first_of_ten() -> Value {
    let mut page = contracts::json("district-contacts.json");
    page["total"] = json!(10);
    page["limit"] = json!(2);
    page
}

pub(crate) fn contacts_cases() -> Vec<Case> {
    vec![
        ("contacts-loading", contacts(signed_in())),
        (
            "contacts-loaded",
            contacts_page(
                contacts(signed_in()),
                contracts::json("district-contacts.json"),
            ),
        ),
        (
            "contacts-empty",
            contacts_page(
                contacts(signed_in()),
                json!({"success": true, "contacts": [], "total": 0, "limit": 25, "offset": 0}),
            ),
        ),
        (
            "contacts-failed",
            contacts(signed_in()).answer(
                |e| matches!(e, Effect::LoadContacts { .. }),
                |ticket| Event::ContactsLoaded {
                    ticket,
                    result: Err(server_error()),
                },
            ),
        ),
        (
            "contacts-loading-more",
            contacts_page(contacts(signed_in()), first_of_ten()).ui(UiEvent::LoadMoreContacts),
        ),
    ]
}

pub(crate) fn open_contact(session: Session) -> Session {
    session.ui(UiEvent::OpenContact {
        contact_id: "contact_contract_1".to_owned(),
    })
}

pub(crate) fn contact_loaded(session: Session, value: Value) -> Session {
    session.answer(
        |e| matches!(e, Effect::LoadContact { .. }),
        |ticket| Event::ContactLoaded {
            ticket,
            result: Ok(contracts::decode("contact", value)),
        },
    )
}

pub(crate) fn contact_cases() -> Vec<Case> {
    let detail = || contracts::json("district-contact-detail.json");
    let mut bare = detail();
    bare["contact"]["intelligence"] = Value::Null;
    bare["contact"]["dgiStatus"] = json!("crawling");
    bare["contact"]["company"] = Value::Null;
    bare["contact"]["phoneNumber"] = json!("4165550142");
    bare["phoneIntel"] = Value::Null;
    vec![
        ("contact-loading", open_contact(signed_in())),
        (
            "contact-loaded",
            contact_loaded(open_contact(signed_in()), detail()),
        ),
        (
            "contact-viewer",
            contact_loaded(open_contact(viewer()), detail()),
        ),
        (
            "contact-no-research",
            contact_loaded(open_contact(signed_in()), bare),
        ),
        (
            "contact-failed",
            open_contact(signed_in()).answer(
                |e| matches!(e, Effect::LoadContact { .. }),
                |ticket| Event::ContactLoaded {
                    ticket,
                    result: Err(server_error()),
                },
            ),
        ),
    ]
}

/// The screen showing in `session`'s contact detail.
fn detail(session: &Session) -> district_ffi::ContactDetailView {
    match screen_view(&session.model) {
        ScreenView::ContactDetail { view } => view,
        other => panic!("not a contact: {other:?}"),
    }
}

fn list(session: &Session) -> district_ffi::ContactsView {
    match screen_view(&session.model) {
        ScreenView::Contacts { view } => view,
        other => panic!("not the contacts: {other:?}"),
    }
}

#[test]
fn each_action_is_its_core_event() {
    let form = || input("Ada", "+1 416 555 0142", "ada@example.com");
    let core_form = || ContactForm {
        name: "Ada".to_owned(),
        phone_number: "+1 416 555 0142".to_owned(),
        email: "ada@example.com".to_owned(),
    };
    for (action, event) in [
        (ContactsAction::StartCreate, ContactsEvent::StartCreate),
        (
            ContactsAction::EditCreate { form: form() },
            ContactsEvent::EditCreate(core_form()),
        ),
        (ContactsAction::SubmitCreate, ContactsEvent::SubmitCreate),
        (ContactsAction::CancelCreate, ContactsEvent::CancelCreate),
        (ContactsAction::StartEdit, ContactsEvent::StartEdit),
        (
            ContactsAction::Edit { form: form() },
            ContactsEvent::Edit(core_form()),
        ),
        (ContactsAction::SaveEdit, ContactsEvent::SaveEdit),
        (ContactsAction::CancelEdit, ContactsEvent::CancelEdit),
        (ContactsAction::AskDelete, ContactsEvent::AskDelete),
        (
            ContactsAction::AskClearResearch,
            ContactsEvent::AskClearIntel,
        ),
        (ContactsAction::AskBlock, ContactsEvent::AskBlock),
        (ContactsAction::Enrich, ContactsEvent::Enrich),
        (ContactsAction::Confirm, ContactsEvent::Confirm),
        (ContactsAction::Cancel, ContactsEvent::Cancel),
        (
            ContactsAction::DismissFailure,
            ContactsEvent::DismissFailure,
        ),
    ] {
        assert_eq!(
            UiEvent::Contacts {
                action: action.clone()
            }
            .events(),
            [Event::Contacts(event)],
            "{action:?}"
        );
    }
}

/// Every action crosses to C# and back intact, through UniFFI's own
/// converters (the ones the generated bindings call).
#[test]
fn every_action_survives_the_trip_to_csharp() {
    use uniffi::{Lift, Lower};
    for action in [
        ContactsAction::EditCreate {
            form: input("Ada", "+1 416 555 0142", ""),
        },
        ContactsAction::Edit {
            form: input("", "", "ada@example.com"),
        },
        ContactsAction::AskClearResearch,
    ] {
        let action = UiEvent::Contacts { action };
        let buffer =
            <UiEvent as Lower<district_ffi::UniFfiTag>>::lower_into_rust_buffer(action.clone());
        let back =
            <UiEvent as Lift<district_ffi::UniFfiTag>>::try_lift_from_rust_buffer(buffer).unwrap();
        assert_eq!(back, action);
    }
}

/// A role that may change nothing sees no write controls anywhere: no "Add
/// contact", no controls on a contact, and the core refuses the form and
/// every question if they are sent anyway.
#[test]
fn a_viewer_sees_no_write_controls() {
    let viewer_list = contacts_page(
        contacts(viewer()),
        contracts::json("district-contacts.json"),
    );
    assert!(!list(&viewer_list).can_create);
    let viewer_list = act(viewer_list, ContactsAction::StartCreate);
    assert_eq!(list(&viewer_list).create, None);

    let mut contact = opened_as(viewer(), json!([the_contact_blocked()]));
    let view = detail(&contact);
    assert_eq!(view.writes, None);
    assert!(
        view.blocked,
        "a viewer still reads that the caller is blocked"
    );
    for action in [
        ContactsAction::StartEdit,
        ContactsAction::AskDelete,
        ContactsAction::AskClearResearch,
        ContactsAction::AskBlock,
        ContactsAction::Enrich,
    ] {
        contact = act(contact, action);
        let view = detail(&contact);
        assert_eq!(
            (view.editing, view.confirming, view.busy),
            (None, None, None)
        );
    }
    assert!(contact.pending.iter().all(|effect| !matches!(
        effect,
        Effect::WriteContact { .. } | Effect::CreateContact { .. }
    )));

    // The agency, by contrast, has every control the contact allows.
    let writes = detail(&opened())
        .writes
        .expect("the agency may change contacts");
    assert!(writes.can_edit && writes.can_delete && writes.can_block);
    assert!(
        writes.can_clear_research,
        "the fixture has research to clear"
    );
    assert!(!writes.can_enrich, "the fixture's research is complete");
    assert!(list(&listed()).can_create);
}

/// The form: the core's hint, what the button may do, and the request it
/// sends; once the contact is added, the form closes and the list is read
/// again.
#[test]
fn adding_a_contact_sends_the_form_once_and_reads_the_list_again() {
    let invalid = act(
        creating(),
        ContactsAction::EditCreate {
            form: input("Grace Hopper", "", ""),
        },
    );
    let form = list(&invalid).create.unwrap();
    assert_eq!(
        form.hint.as_deref(),
        Some(ContactForm::NEEDS_PHONE_OR_EMAIL)
    );
    assert!(!form.can_submit);
    let invalid = act(invalid, ContactsAction::SubmitCreate);
    assert!(
        !invalid
            .pending
            .iter()
            .any(|e| matches!(e, Effect::CreateContact { .. }))
    );

    // Pressed twice: one request.
    let saving = act(
        act(filled(), ContactsAction::SubmitCreate),
        ContactsAction::SubmitCreate,
    );
    let sent: Vec<&Effect> = saving
        .pending
        .iter()
        .filter(|e| matches!(e, Effect::CreateContact { .. }))
        .collect();
    assert_eq!(sent.len(), 1);
    let form = list(&saving).create.unwrap();
    assert!(form.saving && !form.can_submit);
    // Closing is refused while it saves: the answer belongs to the form.
    let saving = act(saving, ContactsAction::CancelCreate);
    assert!(list(&saving).create.is_some());

    let added = saving.answer(
        |e| matches!(e, Effect::CreateContact { .. }),
        |ticket| Event::ContactCreated {
            ticket,
            result: Ok(ContactMutationResponse {
                success: true,
                id: Some("contact-new".to_owned()),
            }),
        },
    );
    assert_eq!(list(&added).create, None);
    assert!(
        added
            .pending
            .iter()
            .any(|e| matches!(e, Effect::LoadContacts { offset: 0, .. }))
    );

    // Cancel closes a form that is not saving.
    assert_eq!(
        list(&act(filled(), ContactsAction::CancelCreate)).create,
        None
    );
}

/// The edit form opens filled with the contact, a failure shows on the form
/// rather than the page, and closing it puts the failure back on the page.
#[test]
fn editing_shows_its_failure_on_the_form() {
    let open = act(opened(), ContactsAction::StartEdit);
    let form = detail(&open).editing.unwrap();
    assert_eq!(
        form.form,
        input("Contract Test Caller", "14165550142", "ada@example.com")
    );
    assert_eq!(
        (form.title.as_str(), form.submit_label.as_str()),
        ("Edit contact", "Save")
    );
    let failed = written(
        act(
            act(
                open,
                ContactsAction::Edit {
                    form: input("Ada Lovelace", "14165550142", "ada@example.com"),
                },
            ),
            ContactsAction::SaveEdit,
        ),
        Err(server_error()),
    );
    let view = detail(&failed);
    assert!(view.editing.unwrap().failure.is_some());
    assert_eq!(view.failure, None);
    let closed = act(failed, ContactsAction::CancelEdit);
    let view = detail(&closed);
    assert_eq!(view.editing, None);
    assert!(view.failure.is_some());
    let dismissed = act(closed, ContactsAction::DismissFailure);
    assert_eq!(detail(&dismissed).failure, None);

    // Saving an unchanged form closes it without a request.
    let unchanged = act(
        act(opened(), ContactsAction::StartEdit),
        ContactsAction::SaveEdit,
    );
    assert_eq!(detail(&unchanged).editing, None);
    assert!(
        !unchanged
            .pending
            .iter()
            .any(|e| matches!(e, Effect::WriteContact { .. }))
    );
}

/// Each question is the core's, with its own button; no answers it, yes
/// sends the one change it asked about.
#[test]
fn each_question_is_the_core_s_and_yes_sends_its_change() {
    for (ask, action, destructive, write) in [
        (
            ContactsAction::AskDelete,
            "Delete",
            true,
            ContactWrite::Delete,
        ),
        (
            ContactsAction::AskClearResearch,
            "Clear research",
            true,
            ContactWrite::ClearIntel,
        ),
        (
            ContactsAction::AskBlock,
            "Block",
            true,
            ContactWrite::Block(true),
        ),
    ] {
        let asked = act(opened(), ask.clone());
        let question = detail(&asked).confirming.expect("the core asks first");
        assert_eq!(
            (question.action.as_str(), question.destructive),
            (action, destructive),
            "{ask:?}"
        );
        assert!(!question.question.is_empty());
        assert_eq!(detail(&act(asked, ContactsAction::Cancel)).confirming, None);
        let confirmed = act(act(opened(), ask), ContactsAction::Confirm);
        let sent: Vec<&ContactWrite> = confirmed
            .pending
            .iter()
            .filter_map(|e| match e {
                Effect::WriteContact { write, .. } => Some(write),
                _ => None,
            })
            .collect();
        assert_eq!(sent, [&write]);
        let view = detail(&confirmed);
        assert!(view.busy.is_some());
        let writes = view.writes.unwrap();
        assert!(!writes.can_edit && !writes.can_delete && !writes.can_block);
    }

    // A blocked caller is asked about unblocking, which is not destructive.
    let blocked = opened_as(signed_in(), json!([the_contact_blocked()]));
    assert!(detail(&blocked).blocked);
    let question = detail(&act(blocked, ContactsAction::AskBlock))
        .confirming
        .unwrap();
    assert_eq!(
        (question.action.as_str(), question.destructive),
        ("Unblock", false)
    );
}

/// Deleting lands on the contacts list, without the contact; blocking says
/// so on the contact.
#[test]
fn a_change_that_lands_shows_its_outcome() {
    let listed_then_open = contact_loaded(
        open_contact(listed()),
        contracts::json("district-contact-detail.json"),
    );
    let deleted = written(
        act(
            act(listed_then_open, ContactsAction::AskDelete),
            ContactsAction::Confirm,
        ),
        Ok(ContactWritten::Deleted),
    );
    assert_eq!(route(&deleted), Route::Contacts);
    assert!(
        !list(&deleted)
            .rows
            .iter()
            .any(|row| row.contact_id == "contact_contract_1")
    );

    let blocked = written(
        act(
            act(opened(), ContactsAction::AskBlock),
            ContactsAction::Confirm,
        ),
        Ok(ContactWritten::Blocked(ContactBlockResponse {
            success: true,
            contact_id: "contact_contract_1".to_owned(),
            name: "Contract Test Caller".to_owned(),
            phone_number: Some("14165550142".to_owned()),
            blocked_at: Some("2026-10-08T12:00:00.000Z".to_owned()),
        })),
    );
    let view = detail(&blocked);
    assert!(view.blocked);
    assert_eq!(view.busy, None);
}
