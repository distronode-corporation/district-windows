//! Contacts and one contact: what the Linux contacts page, contact view and
//! contact form show, and the changes a member makes there (adding, editing,
//! deleting, running and clearing research, blocking and unblocking), each as
//! the core's events. The core decides what may be offered
//! (`ContactDetailScreen::controls`, `Capabilities::can_change`), what asks
//! first, and what the form says.

use district_core::{
    Capabilities, ContactAction, ContactConfirmation, ContactDetailScreen, ContactForm,
    ContactList, ContactView, ContactsEvent, ContactsScreen, CreateContact, Event, FailureText,
    contact_label, format_phone_number,
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

/// A contact's name, phone number and email address as the form holds them,
/// sent whole at every change (the core's `ContactForm`).
#[derive(Clone, Debug, Default, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct ContactFormInput {
    /// The name.
    pub name: String,
    /// The phone number, as typed.
    pub phone_number: String,
    /// The email address.
    pub email: String,
}

impl From<ContactFormInput> for ContactForm {
    fn from(input: ContactFormInput) -> Self {
        Self {
            name: input.name,
            phone_number: input.phone_number,
            email: input.email,
        }
    }
}

/// The form adding a contact, or changing the open one.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct ContactFormView {
    /// The dialog's heading: "Add contact" or "Edit contact".
    pub title: String,
    /// The sending button's label: "Add" or "Save".
    pub submit_label: String,
    /// What is typed, as the core holds it.
    pub form: ContactFormInput,
    /// The core's guidance under the form ("Enter a phone number or an email
    /// address."), or `None`.
    pub hint: Option<String>,
    /// Whether the sending button works: the core would send this form, and
    /// nothing is on its way.
    pub can_submit: bool,
    /// Whether the form is on its way (disable the fields and both buttons).
    pub saving: bool,
    /// Why the last attempt failed, in the core's (or the service's) words.
    pub failure: Option<FailureView>,
}

/// Which form [`ContactFormView`] is.
#[derive(Clone, Copy)]
enum FormKind {
    Create,
    Edit,
}

impl FormKind {
    /// The heading and the button, as the Linux app words them.
    fn words(self) -> (&'static str, &'static str) {
        match self {
            Self::Create => ("Add contact", "Add"),
            Self::Edit => ("Edit contact", "Save"),
        }
    }
}

fn form_view(
    kind: FormKind,
    form: &ContactForm,
    saving: bool,
    failure: Option<&FailureText>,
) -> ContactFormView {
    let (title, submit_label) = kind.words();
    ContactFormView {
        title: title.to_owned(),
        submit_label: submit_label.to_owned(),
        form: ContactFormInput {
            name: form.name.clone(),
            phone_number: form.phone_number.clone(),
            email: form.email.clone(),
        },
        hint: form.hint().map(str::to_owned),
        can_submit: form.can_submit() && !saving,
        saving,
        failure: crate::views::failure(failure),
    }
}

fn create_view(create: &CreateContact) -> ContactFormView {
    form_view(
        FormKind::Create,
        &create.form,
        create.saving,
        create.failure.as_ref(),
    )
}

/// The question the core asks before a change to a contact.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct ContactQuestionView {
    /// The question, in the core's words.
    pub question: String,
    /// The confirming button's label, in the core's words.
    pub action: String,
    /// Whether answering yes removes or hides something (every question but
    /// unblocking): the dialog's default button is then Cancel.
    pub destructive: bool,
}

impl From<ContactConfirmation> for ContactQuestionView {
    fn from(confirmation: ContactConfirmation) -> Self {
        Self {
            question: confirmation.question().to_owned(),
            action: confirmation.action().to_owned(),
            destructive: confirmation != ContactConfirmation::Unblock,
        }
    }
}

/// The changes a member may make to the open contact now, from the core's
/// `ContactControls`. Absent altogether for a role that may change nothing,
/// which reads that its access is read-only instead.
///
/// The controls' own words are fixed and live in the page, as 1.0's do; the
/// block control reads "Unblock" when [`ContactDetailView::blocked`].
#[derive(Clone, Copy, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct ContactWritesView {
    /// Whether "Edit" works.
    pub can_edit: bool,
    /// Whether "Delete" works.
    pub can_delete: bool,
    /// Whether "Run research" works (billed).
    pub can_enrich: bool,
    /// Whether "Clear research" is offered: there is research to clear.
    pub can_clear_research: bool,
    /// Whether the block control works.
    pub can_block: bool,
}

/// What the screen says while a change is on its way, as the Linux app words
/// it.
fn busy_words(action: ContactAction) -> &'static str {
    match action {
        ContactAction::Save => "Saving the contact",
        ContactAction::Delete => "Deleting the contact",
        ContactAction::Enrich => "Starting research",
        ContactAction::ClearIntel => "Clearing research",
        ContactAction::Block => "Blocking the caller",
        ContactAction::Unblock => "Unblocking the caller",
    }
}

/// Something the member did on the contacts or a contact.
#[derive(Clone, Debug, PartialEq, Eq, uniffi::Enum)]
pub enum ContactsAction {
    /// "Add contact": open the form.
    StartCreate,
    /// The form adding a contact changed: what it holds now.
    EditCreate {
        /// The whole form.
        form: ContactFormInput,
    },
    /// "Add", or Enter in the form.
    SubmitCreate,
    /// Close the form adding a contact. The core refuses while it is saving.
    CancelCreate,
    /// "Edit", on the open contact.
    StartEdit,
    /// The form changing the contact changed: what it holds now.
    Edit {
        /// The whole form.
        form: ContactFormInput,
    },
    /// "Save", or Enter in the form.
    SaveEdit,
    /// Close the form changing the contact. The core refuses while it is
    /// saving.
    CancelEdit,
    /// "Delete": the core asks first.
    AskDelete,
    /// "Clear research": the core asks first.
    AskClearResearch,
    /// "Block" or "Unblock": the core asks first.
    AskBlock,
    /// "Run research". Billed; the core does not ask first.
    Enrich,
    /// Answer the open contact's question yes.
    Confirm,
    /// Answer it no.
    Cancel,
    /// Put away the open contact's failure.
    DismissFailure,
}

/// The core events `action` is, in the order the core is to hear them.
pub(crate) fn events(action: ContactsAction) -> Vec<Event> {
    vec![Event::Contacts(match action {
        ContactsAction::StartCreate => ContactsEvent::StartCreate,
        ContactsAction::EditCreate { form } => ContactsEvent::EditCreate(form.into()),
        ContactsAction::SubmitCreate => ContactsEvent::SubmitCreate,
        ContactsAction::CancelCreate => ContactsEvent::CancelCreate,
        ContactsAction::StartEdit => ContactsEvent::StartEdit,
        ContactsAction::Edit { form } => ContactsEvent::Edit(form.into()),
        ContactsAction::SaveEdit => ContactsEvent::SaveEdit,
        ContactsAction::CancelEdit => ContactsEvent::CancelEdit,
        ContactsAction::AskDelete => ContactsEvent::AskDelete,
        ContactsAction::AskClearResearch => ContactsEvent::AskClearIntel,
        ContactsAction::AskBlock => ContactsEvent::AskBlock,
        ContactsAction::Enrich => ContactsEvent::Enrich,
        ContactsAction::Confirm => ContactsEvent::Confirm,
        ContactsAction::Cancel => ContactsEvent::Cancel,
        ContactsAction::DismissFailure => ContactsEvent::DismissFailure,
    })]
}

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
    /// Whether "Add contact" is offered: the member's role may change contacts.
    pub can_create: bool,
    /// The form adding a contact, while it is open.
    pub create: Option<ContactFormView>,
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
    /// Whether the caller is known to be blocked (say "Blocked" by the name).
    pub blocked: bool,
    /// The changes the member may make now; `None` for a role that may change
    /// nothing, which sees no controls at all.
    pub writes: Option<ContactWritesView>,
    /// What is on its way ("Deleting the contact"), while a change is.
    pub busy: Option<String>,
    /// The question the core is asking before a change, while it asks.
    pub confirming: Option<ContactQuestionView>,
    /// The form changing the contact, while it is open. A failure while it
    /// is open is shown on the form, not in [`ContactDetailView::failure`].
    pub editing: Option<ContactFormView>,
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
pub(crate) fn contacts_view(
    contacts: &ContactsScreen,
    capabilities: &Capabilities,
) -> ContactsView {
    let nothing = ContactsView {
        status: LoadStatus::Loading,
        rows: Vec::new(),
        empty: None,
        total_label: None,
        paging: PagingView::default(),
        can_create: capabilities.can_change,
        create: contacts.create.as_ref().map(create_view),
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
            ..nothing
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
    let controls = screen.controls(capabilities);
    let saving = screen.saving == Some(ContactAction::Save);
    let editing = screen
        .editing
        .as_ref()
        .map(|form| form_view(FormKind::Edit, form, saving, screen.failure.as_ref()));
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
        failure: failure(screen.failure.as_ref().filter(|_| editing.is_none())),
        report: ReportAvailability::Hidden,
        phone_number: None,
        blocked: controls.blocked,
        writes: capabilities.can_change.then_some(ContactWritesView {
            can_edit: controls.can_edit,
            can_delete: controls.can_delete,
            can_enrich: controls.can_enrich,
            can_clear_research: controls.can_clear_intel,
            can_block: controls.can_block,
        }),
        busy: screen.saving.map(|action| busy_words(action).to_owned()),
        confirming: screen.confirming.map(ContactQuestionView::from),
        editing,
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
        let map = json!({
            "topics": ["roofing", 3, null],
            "summary": "Runs a roofing firm.",
            "empty": [],
            "blank": " ",
            "nested": {"a": 1},
            "founded_year": 1999
        });
        let map = map.as_object().expect("an object");
        assert_eq!(
            findings(map),
            [
                "Summary: Runs a roofing firm.",
                "Founded year: 1999",
                "Topics: roofing, 3",
            ]
        );
        assert_eq!(count(1), "1 contact");
        let status = |dgi: Option<&str>| {
            let mut contact: Contact = serde_json::from_value(json!({
                "id": "c-1", "workspaceId": "ws-1", "name": "", "phoneNumber": null,
                "email": null, "socialHandles": null, "company": null, "intelligence": null,
                "visualMemory": null, "latestContextSummary": null, "dgiStatus": null,
                "dgiError": null, "budget": null, "timeline": null, "website": null,
                "lastUpdated": null, "createdAt": ""
            }))
            .unwrap();
            contact.dgi_status = dgi.map(str::to_owned);
            research_status(&contact)
        };
        assert_eq!(status(None), None);
        assert_eq!(status(Some("pending")).as_deref(), Some("Queued"));
        assert_eq!(
            status(Some("synthesizing")).as_deref(),
            Some("Writing the summary")
        );
        assert_eq!(status(Some("failed")).as_deref(), Some("Did not finish"));
        assert_eq!(status(Some("on_hold")).as_deref(), Some("On hold"));
        assert_eq!(count(12), "12 contacts");
    }

    #[test]
    fn each_change_says_what_is_on_its_way() {
        for (action, words) in [
            (ContactAction::Save, "Saving the contact"),
            (ContactAction::Delete, "Deleting the contact"),
            (ContactAction::Enrich, "Starting research"),
            (ContactAction::ClearIntel, "Clearing research"),
            (ContactAction::Block, "Blocking the caller"),
            (ContactAction::Unblock, "Unblocking the caller"),
        ] {
            assert_eq!(busy_words(action), words);
        }
    }
}
