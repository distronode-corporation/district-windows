//! Contacts and one contact, read-only: what the Linux contacts page and
//! contact view show, without the forms.

use district_core::{
    Capabilities, ContactDetailScreen, ContactList, ContactView, ContactsScreen, contact_label,
    format_phone_number,
};
use district_model::Contact;
use serde::Serialize;
use serde_json::{Map, Value};

use crate::calls::{dialable, number_facts};
use crate::views::{
    AI_DOSSIER, AiTextView, EmptyView, FactView, FailureView, LoadStatus, PagingView,
    ReportAvailability, facts, failure, humanize,
};

/// The heading of a contact that could not be read, as the Linux app words it.
pub const CONTACT_FAILED_TITLE: &str = "Could not load this contact";

/// The separator between the parts of one line.
const DOT: &str = " \u{b7} ";

/// The contacts list.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct ContactsView {
    /// Where the first read stands.
    pub status: LoadStatus,
    /// The contacts.
    pub rows: Vec<ContactRowView>,
    /// What to say when the list is read and has none.
    pub empty: Option<EmptyView>,
    /// How many there are ("128 contacts"), once read.
    pub total_label: Option<String>,
    /// The next page, and the first page read again.
    pub paging: PagingView,
}

/// One contact in the list.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct ContactRowView {
    /// The contact, to open it by.
    pub contact_id: String,
    /// The name, or else the number or address (the core's `contact_label`).
    pub name: String,
    /// The number and address, leaving out whichever the name already is.
    pub detail: String,
}

/// One contact.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct ContactDetailView {
    /// The contact's id, as the route carries it.
    pub contact_id: String,
    /// Where the read stands.
    pub status: LoadStatus,
    /// The name, or else the number or address. Empty until read.
    pub name: String,
    /// The number and address on one line, under the name.
    pub reach: String,
    /// Number, address, company, website and the rest, each only when known.
    pub facts: Vec<FactView>,
    /// What is known about the phone number: location, line type, carrier.
    pub number_facts: Vec<FactView>,
    /// When it was added, ISO 8601.
    pub created_at: Option<String>,
    /// When it last changed, ISO 8601.
    pub updated_at: Option<String>,
    /// Where the web research stands ("Searching the web", "Complete").
    pub research_status: Option<String>,
    /// What the research found, labelled as AI.
    pub dossier: Option<AiTextView>,
    /// Why the last thing done here failed.
    pub failure: Option<FailureView>,
    /// How to offer Report for the dossier.
    pub report: ReportAvailability,
    /// The number to call, in E.164, when the contact has one.
    pub phone_number: Option<String>,
}

fn count(total: i64) -> String {
    match total {
        1 => "1 contact".to_owned(),
        total => format!("{total} contacts"),
    }
}

fn contact_row(contact: &Contact) -> ContactRowView {
    let name = contact_label(contact);
    let detail = [
        contact.phone_number.as_deref().map(format_phone_number),
        contact.email.clone(),
    ]
    .into_iter()
    .flatten()
    .filter(|part| !part.trim().is_empty() && *part != name)
    .collect::<Vec<_>>()
    .join(DOT);
    ContactRowView {
        contact_id: contact.id.clone(),
        name,
        detail,
    }
}

/// The contacts list, as the Linux contacts page shows it.
pub(crate) fn contacts_view(contacts: &ContactsScreen) -> ContactsView {
    let nothing = ContactsView {
        status: LoadStatus::Loading,
        rows: Vec::new(),
        empty: None,
        total_label: None,
        paging: PagingView::default(),
    };
    match &contacts.list {
        ContactList::NotLoaded | ContactList::Loading => nothing,
        ContactList::Failed(failure) => ContactsView {
            status: LoadStatus::failed(ContactList::FAILED_TITLE, failure),
            ..nothing
        },
        ContactList::Ready(rows) => ContactsView {
            status: LoadStatus::Ready,
            rows: rows.contacts.iter().map(contact_row).collect(),
            empty: rows
                .contacts
                .is_empty()
                .then(|| EmptyView::new(ContactList::EMPTY_TITLE, ContactList::EMPTY_BODY)),
            total_label: (!rows.contacts.is_empty()).then(|| count(rows.total)),
            paging: (&rows.paging).into(),
        },
    }
}

/// Where a research run stands, as the Linux app words it.
fn research_status(contact: &Contact) -> Option<String> {
    let status = contact.dgi_status.as_deref()?;
    Some(
        match status {
            district_model::DGI_PENDING => "Queued",
            district_model::DGI_CRAWLING => "Searching the web",
            district_model::DGI_SYNTHESIZING => "Writing the summary",
            district_model::DGI_COMPLETE => "Complete",
            district_model::DGI_FAILED => "Did not finish",
            other => return Some(humanize(other)),
        }
        .to_owned(),
    )
}

/// What the research found, one "Heading: value" line each, the summary first,
/// as the Linux app lists them.
fn findings(intelligence: &Map<String, Value>) -> Vec<String> {
    let text = |value: &Value| match value {
        Value::String(text) => Some(text.clone()),
        Value::Number(number) => Some(number.to_string()),
        _ => None,
    };
    let mut keys: Vec<&String> = intelligence.keys().collect();
    keys.sort_by_key(|key| (key.as_str() != "summary", key.as_str()));
    keys.into_iter()
        .filter_map(|key| {
            let shown = match &intelligence[key] {
                Value::Array(items) => {
                    let items: Vec<String> = items.iter().filter_map(text).collect();
                    (!items.is_empty()).then(|| items.join(", "))
                }
                other => text(other),
            }?;
            (!shown.trim().is_empty()).then(|| format!("{}: {shown}", humanize(key)))
        })
        .collect()
}

fn contact_facts(contact: &Contact) -> Vec<FactView> {
    let company = contact.company.as_ref();
    facts([
        (
            "Phone number",
            contact.phone_number.as_deref().map(format_phone_number),
        ),
        ("Email address", contact.email.clone()),
        ("Company", company.and_then(|company| company.name.clone())),
        (
            "Industry",
            company.and_then(|company| company.industry.clone()),
        ),
        ("Website", contact.website.clone()),
        ("Budget", contact.budget.clone()),
        ("Timeline", contact.timeline.clone()),
        ("Latest context", contact.latest_context_summary.clone()),
    ])
}

/// One contact, as the Linux contact view shows it.
pub(crate) fn contact_detail_view(
    screen: &ContactDetailScreen,
    capabilities: &Capabilities,
) -> ContactDetailView {
    let mut view = ContactDetailView {
        contact_id: screen.contact_id.clone(),
        status: LoadStatus::Loading,
        name: String::new(),
        reach: String::new(),
        facts: Vec::new(),
        number_facts: Vec::new(),
        created_at: None,
        updated_at: None,
        research_status: None,
        dossier: None,
        failure: failure(screen.failure.as_ref()),
        report: ReportAvailability::Hidden,
        phone_number: None,
    };
    match &screen.contact {
        ContactView::Loading => {}
        ContactView::Failed(failure) => {
            view.status = LoadStatus::failed(CONTACT_FAILED_TITLE, failure);
        }
        ContactView::Ready(details) => {
            let contact = &details.contact;
            let found = contact
                .intelligence
                .as_ref()
                .map(findings)
                .unwrap_or_default();
            let dossier =
                (!found.is_empty()).then(|| AiTextView::new(AI_DOSSIER, found.join("\n")));
            let [location, line_type, carrier] = number_facts(details.phone_intel.as_ref());
            view = ContactDetailView {
                status: LoadStatus::Ready,
                name: contact_label(contact),
                reach: [
                    contact.phone_number.as_deref().map(format_phone_number),
                    contact.email.clone(),
                ]
                .into_iter()
                .flatten()
                .filter(|part| !part.trim().is_empty())
                .collect::<Vec<_>>()
                .join(DOT),
                facts: contact_facts(contact),
                number_facts: facts([location, line_type, carrier]),
                created_at: Some(contact.created_at.clone()).filter(|at| !at.trim().is_empty()),
                updated_at: contact.last_updated.clone(),
                research_status: research_status(contact),
                report: ReportAvailability::for_content(dossier.is_some(), capabilities),
                dossier,
                phone_number: dialable(contact.phone_number.as_deref()),
                ..view
            };
        }
    }
    view
}

#[cfg(test)]
mod tests {
    use serde_json::json;

    use super::*;

    #[test]
    fn findings_put_the_summary_first_and_skip_what_says_nothing() {
        let Value::Object(map) = json!({
            "topics": ["roofing", 3, null],
            "summary": "Runs a roofing firm.",
            "empty": [],
            "blank": " ",
            "nested": {"a": 1},
            "founded_year": 1999
        }) else {
            unreachable!()
        };
        assert_eq!(
            findings(&map),
            [
                "Summary: Runs a roofing firm.",
                "Founded year: 1999",
                "Topics: roofing, 3",
            ]
        );
        assert_eq!(count(1), "1 contact");
        assert_eq!(count(12), "12 contacts");
    }
}
