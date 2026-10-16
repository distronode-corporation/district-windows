//! The event types: the list, with the create form, and one event type, with
//! its details, its hosts and its booking questions, each read on its own and
//! failing alone, and its four writes (the editor, the state actions, the
//! hosts and the questions), as District AI for iPhone's
//! `SchedulingEventTypesView`, `SchedulingEventTypeView` and their sheets.
//!
//! The core decides what is offered, and this projects it: every write needs
//! a member who may change things, and one event type's writes need it read
//! (the hosts are a whole list the scheduler replaces, so their dialog offers
//! no save until it has read them). The create is offered over a list that
//! was read, whose slugs a new booking page avoids. A dialog is open while
//! the core holds it: the editor and the hosts close once their save lands,
//! the state actions and the questions stay open and say what the last write
//! came to until it is put away. Deleting an event type, or one of its
//! questions, is asked once more inside its dialog.

use district_core::scheduling_admin::{
    ActionsDialog, EventTypeDialog, EventTypeEditor, EventTypeEvent, EventTypeForm,
    EventTypeSection, EventTypesEvent, EventTypesSection, HostsDialog, QuestionForm,
    QuestionsDialog, SectionLoad, minutes_label, unique_slug,
};
use district_core::{Event, SchedulingAdmin, SchedulingAdminEvent};
use district_l10n::scheduling_strings as ss;
use district_model::{SchedulingHost, SchedulingQuestion};
use serde::Serialize;

use super::bookings::ChoiceRowView;
use super::{DialogView, SchedulingAdminPage, dialog_view, landed, load_status};
use crate::l10n::l;
use crate::views::{EmptyView, FactView, LoadStatus};
use crate::workflows::RunTone;

/// Whether this version has the section's pages.
pub(crate) const BUILT: bool = true;

/// The event types list.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct SchedulingEventTypesView {
    /// Where the read of the list stands.
    pub status: LoadStatus,
    /// The event types.
    pub rows: Vec<EventTypeRowView>,
    /// What the list says when there are none.
    pub empty: EmptyView,
    /// Whether "Create event type" is offered: the member may change things
    /// and the list was read.
    pub offers_create: bool,
    /// "Create event type".
    pub create_label: String,
    /// The create form, while it is open.
    pub create: Option<EventTypeEditorView>,
}

/// One event type in the list, which opens it
/// (`SchedulingAdminAction::OpenEventType`).
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct EventTypeRowView {
    /// Its slug.
    pub slug: String,
    /// Its name.
    pub name: String,
    /// "30 min · Phone".
    pub subtitle: String,
    /// The state badge: active, inactive or archived.
    pub state_label: String,
    /// Its tone.
    pub tone: RunTone,
    /// Its public booking page's address, or "Not public".
    pub booking_page: String,
}

/// One event type: what it is, who hosts it and what it asks.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct SchedulingEventTypeView {
    /// Its slug.
    pub slug: String,
    /// Its name once read, its slug before.
    pub name: String,
    /// Where the read of the event type stands.
    pub status: LoadStatus,
    /// What its booking page says about it, or empty.
    pub description: String,
    /// The details' heading.
    pub details_heading: String,
    /// The details that say something: duration, interval, location and its
    /// value, notice, how far ahead, the buffers, the state.
    pub facts: Vec<FactView>,
    /// Whether the four writes are offered: the member may change things and
    /// the event type was read.
    pub offers_writes: bool,
    /// "Edit", which opens the editor (`EventTypeAction::OpenEditor`).
    pub edit_label: String,
    /// "Manage", which opens the state actions.
    pub manage_label: String,
    /// "Hosts", which opens the hosts.
    pub hosts_label: String,
    /// "Questions", which opens the booking questions.
    pub questions_label: String,
    /// The hosts' heading.
    pub hosts_heading: String,
    /// Where the read of the hosts stands; a failure is said in its card.
    pub hosts_status: LoadStatus,
    /// The hosts: their name, and their role (archived when they are).
    pub hosts: Vec<FactView>,
    /// Said when it has no host, and so cannot be booked.
    pub no_hosts: String,
    /// The questions' heading.
    pub questions_heading: String,
    /// Where the read of the questions stands; a failure is said in its card.
    pub questions_status: LoadStatus,
    /// The questions in the order the booker sees them: what each asks, and
    /// its type (required when it is).
    pub questions: Vec<FactView>,
    /// Said when it asks nothing.
    pub no_questions: String,
    /// The editor, while it is open.
    pub editor: Option<EventTypeEditorView>,
    /// The state actions, while they are open.
    pub actions: Option<EventTypeActionsView>,
    /// The hosts dialog, while it is open.
    pub hosts_dialog: Option<EventTypeHostsView>,
    /// The questions dialog, while it is open.
    pub questions_dialog: Option<EventTypeQuestionsView>,
}

/// An event type's fields as the form holds them: numbers as typed, so a box
/// emptied to retype it stays empty. What a change to the form sends back
/// whole (`EventTypeAction::EditForm`).
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct EventTypeFormView {
    /// Its name.
    pub name: String,
    /// What the booking page says about it.
    pub description: String,
    /// Minutes.
    pub duration: String,
    /// Minutes between start times.
    pub interval: String,
    /// The location type's wire value.
    pub location: String,
    /// The phone number, address or link.
    pub location_value: String,
    /// Whether it can be booked.
    pub is_active: bool,
    /// Whether it is listed on the booking page.
    pub is_public: bool,
    /// Whether taken times show as taken.
    pub show_taken_slots: bool,
    /// Minutes kept free before.
    pub buffer_before: String,
    /// Minutes kept free after.
    pub buffer_after: String,
    /// Minimum notice, minutes.
    pub min_notice: String,
    /// How many days ahead; 0 for no limit.
    pub max_future_days: String,
    /// Bookings one person may hold; 0 for no limit.
    pub max_active_bookings: String,
}

impl From<&EventTypeForm> for EventTypeFormView {
    fn from(form: &EventTypeForm) -> Self {
        Self {
            name: form.name.clone(),
            description: form.description.clone(),
            duration: form.duration.clone(),
            interval: form.interval.clone(),
            location: form.location.clone(),
            location_value: form.location_value.clone(),
            is_active: form.is_active,
            is_public: form.is_public,
            show_taken_slots: form.show_taken_slots,
            buffer_before: form.buffer_before.clone(),
            buffer_after: form.buffer_after.clone(),
            min_notice: form.min_notice.clone(),
            max_future_days: form.max_future_days.clone(),
            max_active_bookings: form.max_active_bookings.clone(),
        }
    }
}

impl From<EventTypeFormView> for EventTypeForm {
    fn from(form: EventTypeFormView) -> Self {
        Self {
            name: form.name,
            description: form.description,
            duration: form.duration,
            interval: form.interval,
            location: form.location,
            location_value: form.location_value,
            is_active: form.is_active,
            is_public: form.is_public,
            show_taken_slots: form.show_taken_slots,
            buffer_before: form.buffer_before,
            buffer_after: form.buffer_after,
            min_notice: form.min_notice,
            max_future_days: form.max_future_days,
            max_active_bookings: form.max_active_bookings,
        }
    }
}

/// The create or edit form: a create shows the name, the booking page it
/// makes, the duration and the location; an edit shows every field.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct EventTypeEditorView {
    /// Its title ("Create event type", "Edit event type"), the save ("Save",
    /// "Saving…"), the way out and the save's failure.
    pub dialog: DialogView,
    /// Whether this is a create, which shows the short form.
    pub creating: bool,
    /// The fields, as the core holds them.
    pub form: EventTypeFormView,
    /// The booking page's slug: the event type's own, or the one a create
    /// makes from the name, avoiding the slugs listed.
    pub booking_page: String,
    /// The duration as read: "45 min", or "Not set" while it is not a number.
    pub duration_shown: String,
    /// The location types to choose from, the one in the form selected.
    pub locations: Vec<ChoiceRowView>,
    /// The location value field's label ("Phone number", "Address", "Meeting
    /// link"), or `None` for a location that makes its own link.
    pub location_value_label: Option<String>,
    /// Why the form cannot be sent, before anything was sent.
    pub validation: Option<String>,
    /// "Name".
    pub name_label: String,
    /// The name's placeholder.
    pub name_placeholder: String,
    /// "Booking page".
    pub booking_page_label: String,
    /// That it cannot be changed.
    pub booking_page_hint: String,
    /// "Description".
    pub description_label: String,
    /// Where it shows.
    pub description_hint: String,
    /// "Duration".
    pub duration_label: String,
    /// In minutes.
    pub duration_hint: String,
    /// "Starts every".
    pub interval_label: String,
    /// What it means.
    pub interval_hint: String,
    /// "Location".
    pub location_label: String,
    /// The switch that says it can be booked.
    pub active_label: String,
    /// The switch that lists it on the booking page.
    pub listed_label: String,
    /// The switch that shows taken times as taken.
    pub show_taken_label: String,
    /// "Buffer before".
    pub buffer_before_label: String,
    /// What it keeps free.
    pub buffer_before_hint: String,
    /// "Buffer after".
    pub buffer_after_label: String,
    /// What it keeps free.
    pub buffer_after_hint: String,
    /// "Minimum notice".
    pub min_notice_label: String,
    /// What it means.
    pub min_notice_hint: String,
    /// "Bookable ahead".
    pub max_future_label: String,
    /// "0 means no limit."
    pub max_future_hint: String,
    /// "Active bookings per attendee".
    pub max_active_label: String,
    /// "0 means no limit."
    pub max_active_hint: String,
}

/// An event type's state actions: on or off, archive or restore, a test
/// email of each kind to the member, and delete, asked once more.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct EventTypeActionsView {
    /// Its title (the event type's name), the way out, and why the last
    /// action failed. It has no confirm: each action is its own button.
    pub dialog: DialogView,
    /// "Turn off" for an active one, "Turn on" otherwise.
    pub toggle_label: String,
    /// "Archive", or "Restore" for an archived one.
    pub archive_label: String,
    /// "Send test email".
    pub test_email_heading: String,
    /// The test emails: the kind sent, and its label.
    pub test_emails: Vec<ChoiceRowView>,
    /// "Delete event type".
    pub delete_label: String,
    /// Whether an action can be pressed: none is on its way.
    pub can_act: bool,
    /// What the last action came to ("Event type turned off"), until it is
    /// put away (`EventTypeAction::DismissNotice`).
    pub notice: Option<String>,
    /// The delete, while it is being asked about.
    pub delete: Option<SchedulingAskView>,
}

/// A question asked once more before something is removed.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct SchedulingAskView {
    /// "Delete event type?".
    pub title: String,
    /// What goes, and what stays.
    pub body: String,
    /// The destructive button, never the default.
    pub confirm: String,
    /// The way back.
    pub cancel: String,
}

/// Who takes an event type's bookings, and how they are routed among them.
/// Saved whole.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct EventTypeHostsView {
    /// Its title, the save (offered once the hosts are read), the way out and
    /// why the save failed.
    pub dialog: DialogView,
    /// Where the read of the hosts stands; a failed one offers "Try again"
    /// (`EventTypeAction::RetryDialogRead`).
    pub status: LoadStatus,
    /// "Routing".
    pub routing_label: String,
    /// The routing modes, the one chosen selected.
    pub routing_modes: Vec<ChoiceRowView>,
    /// Whether the rotation shows: for round robin only.
    pub shows_rotation: bool,
    /// "Rotation".
    pub rotation_label: String,
    /// What it decides.
    pub rotation_hint: String,
    /// The rotations, the one chosen selected.
    pub rotations: Vec<ChoiceRowView>,
    /// The hosts, as edited.
    pub hosts: Vec<EventTypeHostView>,
    /// The roles a host can have, none selected.
    pub roles: Vec<ChoiceRowView>,
    /// Said when the hosts are read and there are none: nobody can be booked.
    pub empty: String,
    /// "Priority".
    pub priority_label: String,
    /// "Remove".
    pub remove_label: String,
    /// "Add".
    pub add_label: String,
    /// The scheduler's users who could be added: not hosts already, not
    /// archived; none when they could not be read.
    pub candidates: Vec<ChoiceRowView>,
    /// Where the read of those users stands, which follows the hosts' own; a
    /// failed one leaves the hosts editable, with nobody to add, and offers
    /// "Try again" (`EventTypeAction::RetryDialogRead`).
    pub candidates_status: LoadStatus,
    /// Why the hosts cannot be saved, before anything was sent.
    pub validation: Option<String>,
}

/// One host, as edited.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct EventTypeHostView {
    /// Their scheduler user id.
    pub user_id: String,
    /// Their name, or their address for a blank one.
    pub name: String,
    /// Their address.
    pub email: String,
    /// Their role's wire value, one of `roles`.
    pub role: String,
    /// Their place in the rotation, as typed.
    pub priority: String,
}

/// An event type's booking questions: the list, one question's form in its
/// place while it is open, and the delete, asked once more.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct EventTypeQuestionsView {
    /// Its title, the way out, and why the last write failed. It has no
    /// confirm: the form has its own save.
    pub dialog: DialogView,
    /// Where the read of the questions stands.
    pub status: LoadStatus,
    /// The questions in the order the booker sees them.
    pub questions: Vec<EventTypeQuestionView>,
    /// Said when the questions are read and there are none.
    pub empty: String,
    /// "Add question".
    pub add_label: String,
    /// Whether a question can be added: the list was read and nothing is on
    /// its way.
    pub can_add: bool,
    /// "Edit", on each question.
    pub edit_label: String,
    /// "Delete", on each question.
    pub delete_label: String,
    /// What the last write came to ("Question added"), until it is put away
    /// (`EventTypeAction::DismissNotice`).
    pub notice: Option<String>,
    /// One question's form, while it is open.
    pub form: Option<QuestionEditorView>,
    /// A question's delete, while it is being asked about.
    pub delete: Option<SchedulingAskView>,
}

/// One booking question in the list.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct EventTypeQuestionView {
    /// Its id, which its Edit and Delete name.
    pub id: String,
    /// What it asks.
    pub label: String,
    /// Its type, and required when it is: "Text · required".
    pub value: String,
}

/// One question's fields as the form holds them, what a change to the form
/// sends back whole (`EventTypeAction::EditQuestionForm`).
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct QuestionFormView {
    /// The question's id, for an edit; `None` for a new one.
    pub id: Option<String>,
    /// The question.
    pub label: String,
    /// Its type's wire value: `text`, `select` or `checkbox`.
    pub kind: String,
    /// A select's choices, which `AddOption` and `RemoveOption` change.
    pub options: Vec<String>,
    /// Whether it must be answered.
    pub required: bool,
    /// Its place, as typed.
    pub position: String,
}

impl From<&QuestionForm> for QuestionFormView {
    fn from(form: &QuestionForm) -> Self {
        Self {
            id: form.id.clone(),
            label: form.label.clone(),
            kind: form.kind.clone(),
            options: form.options.clone(),
            required: form.required,
            position: form.position.clone(),
        }
    }
}

impl From<QuestionFormView> for QuestionForm {
    fn from(form: QuestionFormView) -> Self {
        Self {
            id: form.id,
            label: form.label,
            kind: form.kind,
            options: form.options,
            required: form.required,
            position: form.position,
        }
    }
}

/// One question's form.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct QuestionEditorView {
    /// "Add question" or "Edit question".
    pub title: String,
    /// The fields, as the core holds them.
    pub form: QuestionFormView,
    /// The question types, the one chosen selected.
    pub kinds: Vec<ChoiceRowView>,
    /// Whether the choices show: for a select only.
    pub shows_options: bool,
    /// "Label".
    pub label_label: String,
    /// The label's placeholder.
    pub label_placeholder: String,
    /// "Type".
    pub type_label: String,
    /// "Required".
    pub required_label: String,
    /// "Position".
    pub position_label: String,
    /// What it orders.
    pub position_hint: String,
    /// "Options".
    pub options_label: String,
    /// What they are.
    pub options_hint: String,
    /// The new choice's placeholder.
    pub option_placeholder: String,
    /// "Add option".
    pub add_option_label: String,
    /// "Remove", on each choice.
    pub remove_label: String,
    /// "Save question" for an edit, "Add question" for a new one.
    pub save_label: String,
    /// "Cancel", which closes the form and keeps the dialog.
    pub cancel_label: String,
    /// Whether the save can be pressed: nothing is on its way.
    pub can_save: bool,
    /// Why the question cannot be sent, before anything was sent.
    pub validation: Option<String>,
}

/// Something the member did on the event types list.
#[derive(Clone, Debug, PartialEq, Eq, uniffi::Enum)]
#[allow(
    clippy::large_enum_variant,
    reason = "one action is made per press or keystroke and lowered from C# at once; UniFFI cannot carry a Box"
)]
pub enum EventTypesAction {
    /// Open the create form.
    OpenCreate,
    /// The create form's fields changed.
    EditForm {
        /// The whole form, as it holds now.
        form: EventTypeFormView,
    },
    /// Create it.
    Save,
    /// Close the create form.
    CloseEditor,
}

/// Something the member did on one event type.
#[derive(Clone, Debug, PartialEq, Eq, uniffi::Enum)]
#[allow(
    clippy::large_enum_variant,
    reason = "one action is made per press or keystroke and lowered from C# at once; UniFFI cannot carry a Box"
)]
pub enum EventTypeAction {
    /// Open the editor.
    OpenEditor,
    /// The editor's fields changed.
    EditForm {
        /// The whole form, as it holds now.
        form: EventTypeFormView,
    },
    /// Save the editor.
    Save,
    /// Open the state actions.
    OpenActions,
    /// Turn it on or off.
    ToggleActive,
    /// Archive or restore it.
    ToggleArchived,
    /// Send the member a test email of this kind.
    SendTestEmail {
        /// The kind, as the actions list it.
        kind: String,
    },
    /// Ask about deleting it.
    AskDelete,
    /// Do not delete it.
    CancelDelete,
    /// Delete it, and go back to the list.
    ConfirmDelete,
    /// Open the hosts.
    OpenHosts,
    /// The routing mode changed.
    SetRoutingMode {
        /// Its wire value.
        mode: String,
    },
    /// The rotation changed.
    SetRotation {
        /// Its wire value.
        rotation: String,
    },
    /// Add a candidate as a host.
    AddHost {
        /// Their scheduler user id.
        user_id: String,
    },
    /// Take a host off.
    RemoveHost {
        /// Their scheduler user id.
        user_id: String,
    },
    /// A host's role changed.
    SetHostRole {
        /// Their scheduler user id.
        user_id: String,
        /// The role's wire value.
        role: String,
    },
    /// A host's priority changed.
    SetHostPriority {
        /// Their scheduler user id.
        user_id: String,
        /// The priority, as typed.
        priority: String,
    },
    /// Save the hosts.
    SaveHosts,
    /// "Try again" in the hosts dialog: read its hosts again after that read
    /// failed, else the users who could be added after theirs did.
    RetryDialogRead,
    /// Open the questions.
    OpenQuestions,
    /// Start a new question, placed last.
    AddQuestion,
    /// Edit a question.
    EditQuestion {
        /// Its id.
        id: String,
    },
    /// The question's fields changed.
    EditQuestionForm {
        /// The whole form, as it holds now.
        form: QuestionFormView,
    },
    /// Add a choice to a select.
    AddOption {
        /// The choice, as typed.
        option: String,
    },
    /// Take a choice off.
    RemoveOption {
        /// The choice.
        option: String,
    },
    /// Close the question's form.
    CancelQuestion,
    /// Save the question.
    SaveQuestion,
    /// Ask about deleting a question.
    AskDeleteQuestion {
        /// Its id.
        id: String,
    },
    /// Do not delete it.
    CancelDeleteQuestion,
    /// Delete it.
    ConfirmDeleteQuestion,
    /// Put away what the last write came to.
    DismissNotice,
    /// Close the dialog.
    CloseDialog,
}

/// The core events `action` on the list is.
pub(crate) fn events(action: EventTypesAction) -> Vec<Event> {
    let event = match action {
        EventTypesAction::OpenCreate => EventTypesEvent::OpenCreate,
        EventTypesAction::EditForm { form } => EventTypesEvent::EditForm(Box::new(form.into())),
        EventTypesAction::Save => EventTypesEvent::Save,
        EventTypesAction::CloseEditor => EventTypesEvent::CloseEditor,
    };
    vec![Event::SchedulingAdmin(SchedulingAdminEvent::EventTypes(
        event,
    ))]
}

/// The core events `action` on one event type is.
pub(crate) fn detail_events(action: EventTypeAction) -> Vec<Event> {
    use EventTypeAction as A;
    use EventTypeEvent as E;
    let event = match action {
        A::OpenEditor => E::OpenEditor,
        A::EditForm { form } => E::EditForm(form.into()),
        A::Save => E::Save,
        A::OpenActions => E::OpenActions,
        A::ToggleActive => E::ToggleActive,
        A::ToggleArchived => E::ToggleArchived,
        A::SendTestEmail { kind } => E::SendTestEmail(kind),
        A::AskDelete => E::AskDelete,
        A::CancelDelete => E::CancelDelete,
        A::ConfirmDelete => E::ConfirmDelete,
        A::OpenHosts => E::OpenHosts,
        A::SetRoutingMode { mode } => E::SetRoutingMode(mode),
        A::SetRotation { rotation } => E::SetRotation(rotation),
        A::AddHost { user_id } => E::AddHost(user_id),
        A::RemoveHost { user_id } => E::RemoveHost(user_id),
        A::SetHostRole { user_id, role } => E::SetHostRole { user_id, role },
        A::SetHostPriority { user_id, priority } => E::SetHostPriority { user_id, priority },
        A::SaveHosts => E::SaveHosts,
        A::RetryDialogRead => E::RetryDialogRead,
        A::OpenQuestions => E::OpenQuestions,
        A::AddQuestion => E::AddQuestion,
        A::EditQuestion { id } => E::EditQuestion(id),
        A::EditQuestionForm { form } => E::EditQuestionForm(form.into()),
        A::AddOption { option } => E::AddOption(option),
        A::RemoveOption { option } => E::RemoveOption(option),
        A::CancelQuestion => E::CancelQuestion,
        A::SaveQuestion => E::SaveQuestion,
        A::AskDeleteQuestion { id } => E::AskDeleteQuestion(id),
        A::CancelDeleteQuestion => E::CancelDeleteQuestion,
        A::ConfirmDeleteQuestion => E::ConfirmDeleteQuestion,
        A::DismissNotice => E::DismissNotice,
        A::CloseDialog => E::CloseDialog,
    };
    vec![Event::SchedulingAdmin(SchedulingAdminEvent::EventType(
        event,
    ))]
}

/// The list's page, for a member who may change things when `can_change`.
/// Before the list is entered, it is being read.
pub(crate) fn list_page(admin: &SchedulingAdmin, can_change: bool) -> SchedulingAdminPage {
    let fresh = EventTypesSection::default();
    SchedulingAdminPage::EventTypes {
        view: list_view(admin.event_types.as_ref().unwrap_or(&fresh), can_change),
    }
}

fn list_view(section: &EventTypesSection, can_change: bool) -> SchedulingEventTypesView {
    let rows = section.rows();
    let taken: Vec<String> = rows.iter().map(|row| row.slug.clone()).collect();
    SchedulingEventTypesView {
        status: load_status(&section.list),
        rows: rows
            .into_iter()
            .map(|row| EventTypeRowView {
                slug: row.slug,
                name: row.name,
                subtitle: row.subtitle,
                state_label: row.state.to_owned(),
                tone: RunTone::from(row.tone),
                booking_page: row.booking_page,
            })
            .collect(),
        empty: EmptyView::new(
            EventTypesSection::empty_title(),
            EventTypesSection::empty_body(),
        ),
        offers_create: can_change && section.list.value().is_some(),
        create_label: EventTypesSection::create_label().to_owned(),
        create: section
            .editor
            .as_ref()
            .map(|editor| editor_view(editor, &taken)),
    }
}

/// One event type's page. Before it is entered, its three reads are on their
/// way.
pub(crate) fn detail_page(
    admin: &SchedulingAdmin,
    slug: &str,
    can_change: bool,
) -> SchedulingAdminPage {
    let fresh = EventTypeSection {
        slug: slug.to_owned(),
        event_type: SectionLoad::Loading,
        hosts: SectionLoad::Loading,
        questions: SectionLoad::Loading,
        dialog: None,
    };
    let section = admin
        .event_type
        .as_ref()
        .filter(|section| section.slug == slug)
        .unwrap_or(&fresh);
    SchedulingAdminPage::EventType {
        view: detail_view(section, can_change),
    }
}

fn detail_view(section: &EventTypeSection, can_change: bool) -> SchedulingEventTypeView {
    let [details_heading, hosts_heading, questions_heading] = EventTypeSection::headings();
    let [no_hosts, no_questions] = EventTypeSection::empties();
    let [edit, manage, hosts_label, questions_label] = EventTypeSection::write_labels();
    let read = section.event_type.value();
    let (mut editor, mut actions, mut hosts_dialog, mut questions_dialog) =
        (None, None, None, None);
    match &section.dialog {
        Some(EventTypeDialog::Edit(dialog)) => editor = Some(editor_view(dialog, &[])),
        Some(EventTypeDialog::Actions(dialog)) => actions = Some(actions_view(dialog)),
        Some(EventTypeDialog::Hosts(dialog)) => hosts_dialog = Some(hosts_view(dialog)),
        Some(EventTypeDialog::Questions(dialog)) => questions_dialog = Some(questions_view(dialog)),
        None => {}
    }
    SchedulingEventTypeView {
        slug: section.slug.clone(),
        name: section.title(),
        status: load_status(&section.event_type),
        description: read
            .and_then(|item| item.description())
            .unwrap_or_default()
            .to_owned(),
        details_heading: details_heading.to_owned(),
        facts: section
            .facts()
            .into_iter()
            .map(|(label, value)| FactView {
                label: label.to_owned(),
                value,
            })
            .collect(),
        offers_writes: can_change && read.is_some(),
        edit_label: edit.to_owned(),
        manage_label: manage.to_owned(),
        hosts_label: hosts_label.to_owned(),
        questions_label: questions_label.to_owned(),
        hosts_heading: hosts_heading.to_owned(),
        hosts_status: load_status(&section.hosts),
        hosts: section
            .hosts
            .value()
            .map(|hosts| hosts.iter().map(host_fact).collect())
            .unwrap_or_default(),
        no_hosts: no_hosts.to_owned(),
        questions_heading: questions_heading.to_owned(),
        questions_status: load_status(&section.questions),
        questions: section
            .questions
            .value()
            .map(|questions| {
                in_order(questions)
                    .into_iter()
                    .map(|question| FactView {
                        label: question.label.clone(),
                        value: question_value(question),
                    })
                    .collect()
            })
            .unwrap_or_default(),
        no_questions: no_questions.to_owned(),
        editor,
        actions,
        hosts_dialog,
        questions_dialog,
    }
}

/// A label from a list of (wire value, label) for `value`; a value this build
/// does not know is shown as it came.
fn label_of(choices: &[(&str, &'static str)], value: &str) -> String {
    choices
        .iter()
        .find(|(wire, _)| *wire == value)
        .map_or_else(|| value.to_owned(), |(_, label)| (*label).to_owned())
}

/// `choices` as a dialog lists them, `selected` chosen.
fn choices(choices: &[(&str, &'static str)], selected: &str) -> Vec<ChoiceRowView> {
    choices
        .iter()
        .map(|(value, label)| ChoiceRowView {
            value: (*value).to_owned(),
            label: (*label).to_owned(),
            selected: *value == selected,
        })
        .collect()
}

/// A host as the event type's card says it: their name (their address for a
/// blank one), and their role in the core's words, archived when they are.
fn host_fact(host: &SchedulingHost) -> FactView {
    let name = if host.name.is_empty() {
        &host.email
    } else {
        &host.name
    };
    let role = label_of(&HostsDialog::roles(), &host.role);
    FactView {
        label: name.clone(),
        value: if host.archived {
            ss::host_archived(l(), &role)
        } else {
            role
        },
    }
}

/// The questions in the order the booker sees them.
fn in_order(questions: &[SchedulingQuestion]) -> Vec<&SchedulingQuestion> {
    let mut ordered: Vec<&SchedulingQuestion> = questions.iter().collect();
    ordered.sort_by_key(|question| question.position);
    ordered
}

/// A question's type in the core's words, and required when it is.
fn question_value(question: &SchedulingQuestion) -> String {
    let kind = label_of(&QuestionsDialog::kinds(), &question.kind);
    if question.required {
        ss::question_required(l(), &kind)
    } else {
        kind
    }
}

/// The minutes typed, once a whole number of at least 1.
fn typed_minutes(raw: &str) -> Option<i64> {
    raw.trim()
        .parse::<i64>()
        .ok()
        .filter(|minutes| *minutes >= 1)
}

/// The editor; `taken` is the slugs listed, which a create's booking page
/// avoids.
fn editor_view(editor: &EventTypeEditor, taken: &[String]) -> EventTypeEditorView {
    let [
        name_label,
        name_placeholder,
        booking_page_label,
        booking_page_hint,
        description_label,
        description_hint,
        duration_label,
        duration_hint,
        interval_label,
        interval_hint,
        location_label,
        active_label,
        listed_label,
        show_taken_label,
        save,
        saving,
        cancel,
    ] = EventTypeEditor::labels();
    let [
        (buffer_before_label, buffer_before_hint),
        (buffer_after_label, buffer_after_hint),
        (min_notice_label, min_notice_hint),
        (max_future_label, max_future_hint),
        (max_active_label, max_active_hint),
    ] = EventTypeEditor::count_labels();
    let busy = editor.write.busy();
    let form = &editor.form;
    let booking_page = match &editor.slug {
        Some(slug) => slug.clone(),
        None => unique_slug(&form.name, taken),
    };
    EventTypeEditorView {
        dialog: dialog_view(
            [editor.title(), "", if busy { saving } else { save }, cancel],
            &editor.write,
            true,
            false,
        ),
        creating: editor.is_creating(),
        form: form.into(),
        booking_page,
        duration_shown: minutes_label(typed_minutes(&form.duration)),
        locations: editor
            .location_choices()
            .into_iter()
            .map(|(value, label)| ChoiceRowView {
                selected: value == form.location,
                value,
                label,
            })
            .collect(),
        location_value_label: editor.location_value_label().map(str::to_owned),
        validation: editor.validation.map(str::to_owned),
        name_label: name_label.to_owned(),
        name_placeholder: name_placeholder.to_owned(),
        booking_page_label: booking_page_label.to_owned(),
        booking_page_hint: booking_page_hint.to_owned(),
        description_label: description_label.to_owned(),
        description_hint: description_hint.to_owned(),
        duration_label: duration_label.to_owned(),
        duration_hint: duration_hint.to_owned(),
        interval_label: interval_label.to_owned(),
        interval_hint: interval_hint.to_owned(),
        location_label: location_label.to_owned(),
        active_label: active_label.to_owned(),
        listed_label: listed_label.to_owned(),
        show_taken_label: show_taken_label.to_owned(),
        buffer_before_label: buffer_before_label.to_owned(),
        buffer_before_hint: buffer_before_hint.to_owned(),
        buffer_after_label: buffer_after_label.to_owned(),
        buffer_after_hint: buffer_after_hint.to_owned(),
        min_notice_label: min_notice_label.to_owned(),
        min_notice_hint: min_notice_hint.to_owned(),
        max_future_label: max_future_label.to_owned(),
        max_future_hint: max_future_hint.to_owned(),
        max_active_label: max_active_label.to_owned(),
        max_active_hint: max_active_hint.to_owned(),
    }
}

/// The state actions.
fn actions_view(actions: &ActionsDialog) -> EventTypeActionsView {
    let busy = actions.write.busy();
    EventTypeActionsView {
        dialog: dialog_view(
            [
                actions.event_type.name.as_str(),
                "",
                "",
                ss::common_cancel(l()),
            ],
            &actions.write,
            false,
            false,
        ),
        toggle_label: actions.toggle_label().to_owned(),
        archive_label: actions.archive_label().to_owned(),
        test_email_heading: ActionsDialog::test_email_heading().to_owned(),
        test_emails: choices(&ActionsDialog::test_emails(), ""),
        delete_label: ActionsDialog::delete_label().to_owned(),
        can_act: !busy,
        notice: landed(&actions.write),
        delete: actions.confirming_delete.then(|| SchedulingAskView {
            title: ActionsDialog::delete_title().to_owned(),
            body: ActionsDialog::delete_body().to_owned(),
            confirm: ActionsDialog::delete_label().to_owned(),
            cancel: ss::common_cancel(l()).to_owned(),
        }),
    }
}

/// The hosts dialog.
fn hosts_view(hosts: &HostsDialog) -> EventTypeHostsView {
    let [
        title,
        empty,
        routing_label,
        rotation_label,
        rotation_hint,
        priority_label,
        add_label,
        remove_label,
    ] = HostsDialog::labels();
    let busy = hosts.write.busy();
    EventTypeHostsView {
        dialog: dialog_view(
            [
                title,
                "",
                if busy {
                    ss::common_saving(l())
                } else {
                    ss::common_save(l())
                },
                ss::common_cancel(l()),
            ],
            &hosts.write,
            hosts.load.value().is_some(),
            false,
        ),
        status: load_status(&hosts.load),
        routing_label: routing_label.to_owned(),
        routing_modes: choices(&HostsDialog::routing_modes(), &hosts.routing_mode),
        shows_rotation: hosts.shows_rotation(),
        rotation_label: rotation_label.to_owned(),
        rotation_hint: rotation_hint.to_owned(),
        rotations: choices(&HostsDialog::rotations(), &hosts.rotation),
        hosts: hosts
            .rows
            .iter()
            .map(|row| EventTypeHostView {
                user_id: row.user_id.clone(),
                name: row.name.clone(),
                email: row.email.clone(),
                role: row.role.clone(),
                priority: row.priority.clone(),
            })
            .collect(),
        roles: choices(&HostsDialog::roles(), ""),
        empty: empty.to_owned(),
        priority_label: priority_label.to_owned(),
        remove_label: remove_label.to_owned(),
        add_label: add_label.to_owned(),
        candidates: hosts
            .candidates
            .iter()
            .map(|user| ChoiceRowView {
                value: user.id.clone(),
                label: if user.name.is_empty() {
                    user.email.clone()
                } else {
                    user.name.clone()
                },
                selected: false,
            })
            .collect(),
        candidates_status: load_status(&hosts.candidates_load),
        validation: hosts.validation.map(str::to_owned),
    }
}

/// The questions dialog.
fn questions_view(questions: &QuestionsDialog) -> EventTypeQuestionsView {
    let [
        title,
        empty,
        label_label,
        label_placeholder,
        type_label,
        required_label,
        position_label,
        position_hint,
        options_label,
        options_hint,
        option_placeholder,
        add_option_label,
        save_question,
        delete_title,
        delete_body,
        delete_confirm,
        edit_label,
        delete_label,
    ] = QuestionsDialog::labels();
    let busy = questions.write.busy();
    let read = questions.questions.value();
    EventTypeQuestionsView {
        dialog: dialog_view(
            [title, "", "", ss::common_cancel(l())],
            &questions.write,
            false,
            false,
        ),
        status: load_status(&questions.questions),
        questions: read
            .map(|list| {
                in_order(list)
                    .into_iter()
                    .map(|question| EventTypeQuestionView {
                        id: question.id.clone(),
                        label: question.label.clone(),
                        value: question_value(question),
                    })
                    .collect()
            })
            .unwrap_or_default(),
        empty: empty.to_owned(),
        add_label: ss::question_add_title(l()).to_owned(),
        can_add: read.is_some() && !busy,
        edit_label: edit_label.to_owned(),
        delete_label: delete_label.to_owned(),
        notice: landed(&questions.write),
        form: questions.editing.as_ref().map(|form| QuestionEditorView {
            title: questions.editor_title().to_owned(),
            form: form.into(),
            kinds: choices(&QuestionsDialog::kinds(), &form.kind),
            shows_options: form.kind == "select",
            label_label: label_label.to_owned(),
            label_placeholder: label_placeholder.to_owned(),
            type_label: type_label.to_owned(),
            required_label: required_label.to_owned(),
            position_label: position_label.to_owned(),
            position_hint: position_hint.to_owned(),
            options_label: options_label.to_owned(),
            options_hint: options_hint.to_owned(),
            option_placeholder: option_placeholder.to_owned(),
            add_option_label: add_option_label.to_owned(),
            remove_label: ss::common_remove(l()).to_owned(),
            save_label: if form.id.is_some() {
                save_question
            } else {
                ss::question_add_title(l())
            }
            .to_owned(),
            cancel_label: ss::common_cancel(l()).to_owned(),
            can_save: !busy,
            validation: questions.validation.map(str::to_owned),
        }),
        delete: questions
            .confirming_delete
            .as_ref()
            .map(|_| SchedulingAskView {
                title: delete_title.to_owned(),
                body: delete_body.to_owned(),
                confirm: delete_confirm.to_owned(),
                cancel: ss::common_cancel(l()).to_owned(),
            }),
    }
}
