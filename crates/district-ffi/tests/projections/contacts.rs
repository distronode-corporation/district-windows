//! The contacts and one contact (src/contacts.rs).

use super::*;

/// This area's snapshot cases.
pub(crate) fn cases() -> Vec<Case> {
    [contacts_cases(), contact_cases()]
        .into_iter()
        .flatten()
        .collect()
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
