//! The help desk: the queue of tickets the workspace's own customers raised,
//! raising one for a customer, one ticket with its conversation, its status
//! and the reply, and the desk's settings with its logo.
//!
//! The state, the bounds and most of the words are the core's (district-core's
//! `desk.rs`): a desk that is switched off is not an empty queue, every change
//! is one at a time and adopts what the service answers with, and the settings
//! form sends only what changed. The words the core does not have are District
//! AI for Linux's (`pages/desk.rs`, `pages/desk_ticket.rs`,
//! `pages/desk_settings.rs`, `pages/ticket_form.rs` and their `.ui` files).
//!
//! Every route behind the desk refuses a viewer, reads included, because
//! tickets carry customers' names, contact details and correspondence. So the
//! navigation pane never offers the desk to a viewer, the core never opens its
//! screens for one (and leaves them when a role narrows), and nothing here
//! offers a control the member's role may not use.
//!
//! The logo is checked here before it is sent ([`desk_logo_problem`]): the
//! service hosts only PNG, JPEG and WebP images, and the core sends whatever
//! it is handed. A GIF, or anything else, or an image over the core's size
//! limit, is refused with a sentence that says so and never reaches the core.

use district_core::{
    Capabilities, DESK_MESSAGE_MAX, DESK_SUBJECT_MAX, DESK_SUBJECT_MIN, DeskCompose, DeskEvent,
    DeskQueue, DeskScreen, DeskSettingsForm, DeskSettingsView as DeskSettingsRead, DeskTicketForm,
    DeskTicketScreen, DeskTicketView as DeskTicketRead, DeskTickets, Event, MAX_ATTACHMENT_BYTES,
    Model, Route, SignedIn, desk_author_label, desk_status, desk_status_label, format_phone_number,
};
use district_model::{DeskMessage, DeskTicketDetail, DeskTicketStatus, DeskTicketSummary};
use serde::Serialize;

use crate::files::{LOGO_TYPES, picked_attachment};
use crate::screen::ScreenView;
use crate::views::{EmptyView, FactView, FailureView, LoadStatus, facts, failure, humanize};

/// A picked file, as [`DeskAction::UploadLogo`] carries it (crate::files).
pub use crate::files::PickedFileView;

/// Whether this version has the area's screens.
pub(crate) const BUILT: bool = true;

/// The queue's heading, as the Linux app words it.
pub const DESK_TITLE: &str = "Help desk";
/// A ticket's heading before it is read, as the Linux app words it.
pub const TICKET_TITLE: &str = "Ticket";
/// The heading when a ticket could not be read, as the Linux app words it.
pub const TICKET_FAILED_TITLE: &str = "Could not load this ticket";
/// The settings page's heading.
pub const SETTINGS_TITLE: &str = "Help desk settings";
/// What the settings say under their heading, as the Linux app words it.
pub const SETTINGS_INTRO: &str = "Leave the name blank to show customers the workspace's own name.";
/// The form raising a ticket: its heading, as the Linux app words it.
pub const COMPOSE_TITLE: &str = "New ticket";
/// The form's button that raises the ticket, as the Linux app words it.
pub const COMPOSE_SUBMIT: &str = "Raise";
/// What the form says about the customer's details, as the Linux app words it.
pub const CUSTOMER_NOTE: &str =
    "Optional. Replies are emailed to the address given here, when the desk emails customers.";
/// A row whose ticket names no customer, as the Linux app words it.
pub const NO_CUSTOMER: &str = "No customer details";
/// The "desk on" switch, as the Linux app words it.
pub const ENABLED_LABEL: &str = "Take tickets";
/// The note under the "desk on" switch, as the Linux app words it.
pub const ENABLED_NOTE: &str = "While the desk is off, no tickets are recorded.";
/// The "email customers" switch, as the Linux app words it.
pub const NOTIFY_LABEL: &str = "Email customers your replies";
/// The name box, as the Linux app words it.
pub const BRAND_LABEL: &str = "Name customers see";
/// The logo's line when one is published, as the Linux app words it.
pub const LOGO_PUBLISHED: &str = "A logo is published.";
/// The logo's line when there is none, as the Linux app words it.
pub const LOGO_NONE: &str = "No logo yet.";
/// What the logo is and what the service takes, as the Linux app words it.
pub const LOGO_HELP: &str = "Shown to customers on the help desk's pages. A PNG, JPEG or WebP \
    image; District AI checks its size and dimensions.";
/// Why a picked logo is refused for its type (a GIF among them), naming the
/// types the Linux app offers as a logo.
pub const LOGO_TYPE_REFUSED: &str = "The logo must be a PNG, JPEG or WebP image.";
/// The note after a reply the customer was emailed, as the Linux app words it.
pub const NOTIFIED: &str = "The customer was emailed your reply.";
/// The note after a reply the customer was not emailed, as the Linux app
/// words it.
pub const NOT_NOTIFIED: &str = "The customer was not emailed this reply.";

/// The queue, with the form that raises a ticket.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct DeskView {
    /// The heading, [`DESK_TITLE`].
    pub title: String,
    /// Where the queue's read stands. A desk that is off is read: see `off`.
    pub status: LoadStatus,
    /// The switched-off state, in place of the queue, while the desk is off.
    pub off: Option<DeskOffView>,
    /// The filter's choices, each with how many tickets it shows once read.
    pub filters: Vec<DeskFilterView>,
    /// Whether the filter can be used: the queue is read.
    pub can_filter: bool,
    /// The tickets the filter shows, most recently updated first.
    pub rows: Vec<DeskRowView>,
    /// What to say when the queue is read and has no tickets at all.
    pub empty: Option<EmptyView>,
    /// The line when the filter matches nothing in a queue that has tickets.
    pub none_matching: Option<String>,
    /// Whether the queue is being read again, with these still showing.
    pub refreshing: bool,
    /// The confirmation of a ticket raised ("Ticket T-41 is open."), until
    /// dismissed.
    pub submitted: Option<String>,
    /// Whether "New ticket" is offered: the member may use the desk, it is
    /// on and read, and no form is open.
    pub can_start: bool,
    /// Whether "Settings" is offered: the member may use the desk.
    pub can_open_settings: bool,
    /// The form raising a ticket, while it is open.
    pub compose: Option<DeskComposeView>,
}

impl Default for DeskView {
    /// The queue before anything is read.
    fn default() -> Self {
        Self {
            title: DESK_TITLE.to_owned(),
            status: LoadStatus::Loading,
            off: None,
            filters: filters(None, None),
            can_filter: false,
            rows: Vec::new(),
            empty: None,
            none_matching: None,
            refreshing: false,
            submitted: None,
            can_start: false,
            can_open_settings: false,
            compose: None,
        }
    }
}

/// A desk that is switched off: nothing is being recorded, so there is no
/// queue to show, only the offer to turn it on.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct DeskOffView {
    /// The heading, in the core's words.
    pub title: String,
    /// The body, in the core's words.
    pub body: String,
    /// The button that turns it on, in the core's words.
    pub turn_on: String,
    /// Whether the button works: the member may use the desk and it is not
    /// already being turned on.
    pub can_turn_on: bool,
    /// Whether turning it on is on its way.
    pub enabling: bool,
    /// Why turning it on failed.
    pub failure: Option<FailureView>,
}

/// One of the filter's choices.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash, Serialize, uniffi::Enum)]
pub enum DeskFilter {
    /// Every ticket.
    All,
    /// Waiting on the workspace.
    Open,
    /// Waiting on the customer.
    Waiting,
    /// Resolved.
    Resolved,
}

impl DeskFilter {
    /// The filter's choices, in its order, as the Linux app has them.
    const ALL: [Self; 4] = [Self::All, Self::Open, Self::Waiting, Self::Resolved];

    /// The status this choice shows, or `None` for every ticket.
    fn status(self) -> Option<DeskTicketStatus> {
        match self {
            Self::All => None,
            Self::Open => Some(DeskTicketStatus::Open),
            Self::Waiting => Some(DeskTicketStatus::Waiting),
            Self::Resolved => Some(DeskTicketStatus::Resolved),
        }
    }
}

/// One of the filter's choices, as the filter shows it.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct DeskFilterView {
    /// The choice.
    pub filter: DeskFilter,
    /// Its label, with how many tickets it shows once the queue is read
    /// ("Open (2)"), as the Linux app words it.
    pub label: String,
    /// Whether it is the one chosen.
    pub selected: bool,
}

/// A ticket's status, as a ticket's buttons send it.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash, Serialize, uniffi::Enum)]
pub enum DeskStatus {
    /// Waiting on the workspace.
    Open,
    /// Waiting on the customer.
    Waiting,
    /// Resolved.
    Resolved,
}

impl From<DeskTicketStatus> for DeskStatus {
    fn from(status: DeskTicketStatus) -> Self {
        match status {
            DeskTicketStatus::Open => Self::Open,
            DeskTicketStatus::Waiting => Self::Waiting,
            DeskTicketStatus::Resolved => Self::Resolved,
        }
    }
}

impl From<DeskStatus> for DeskTicketStatus {
    fn from(status: DeskStatus) -> Self {
        match status {
            DeskStatus::Open => Self::Open,
            DeskStatus::Waiting => Self::Waiting,
            DeskStatus::Resolved => Self::Resolved,
        }
    }
}

/// The statuses, in the order of a ticket's buttons.
const STATUSES: [DeskTicketStatus; 3] = [
    DeskTicketStatus::Open,
    DeskTicketStatus::Waiting,
    DeskTicketStatus::Resolved,
];

/// One ticket in the queue.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct DeskRowView {
    /// What opens it ([`DeskAction::OpenTicket`]).
    pub ticket_id: String,
    /// The subject.
    pub subject: String,
    /// What a person calls it (`T-41`).
    pub reference: String,
    /// Who raised it, as far as it says, or [`NO_CUSTOMER`].
    pub requester: String,
    /// Its status, short, as the Linux app badges it in a row ("Waiting").
    pub status_label: String,
    /// Its status, when it is one this build knows, for the badge's style.
    pub status: Option<DeskStatus>,
    /// When it last changed, as an ISO 8601 instant.
    pub updated_at: String,
}

/// The form raising a ticket for a customer.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct DeskComposeView {
    /// The form's heading, [`COMPOSE_TITLE`].
    pub title: String,
    /// The button that raises it, [`COMPOSE_SUBMIT`].
    pub submit_label: String,
    /// The subject, as typed.
    pub subject: String,
    /// The customer's problem, as typed: it is recorded as theirs.
    pub message: String,
    /// The customer's name, as typed.
    pub requester_name: String,
    /// The customer's email address, as typed.
    pub requester_email: String,
    /// The customer's phone number, as typed.
    pub requester_phone: String,
    /// What the form says about the customer's details, [`CUSTOMER_NOTE`].
    pub customer_note: String,
    /// The longest subject the service takes, for the box's limit.
    pub subject_max: u32,
    /// The longest message the service takes, for the box's limit.
    pub message_max: u32,
    /// What the form needs before it can be sent, while it cannot be.
    pub needs: Option<String>,
    /// Whether the raise button works: the service's own bounds are met and
    /// nothing is on its way.
    pub can_submit: bool,
    /// Whether the ticket is on its way. The form cannot be changed or
    /// discarded meanwhile.
    pub submitting: bool,
    /// Why the last attempt failed. What was typed stays.
    pub failure: Option<FailureView>,
}

/// One ticket, its conversation, its status and the reply.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct DeskTicketView {
    /// The heading: the subject once read, [`TICKET_TITLE`] before.
    pub title: String,
    /// The ticket the page was opened for.
    pub ticket_id: String,
    /// Where the read stands.
    pub status: LoadStatus,
    /// Its reference and status ("T-41 · Open"), empty until read.
    pub reference_line: String,
    /// The status buttons, in their order, each with whether it is the
    /// ticket's own. None until read.
    pub statuses: Vec<DeskStatusChoiceView>,
    /// Whether the status buttons work (the ticket's own does nothing): read,
    /// the member may use the desk, and no change is on its way.
    pub can_change_status: bool,
    /// Whether a status change is on its way.
    pub status_changing: bool,
    /// Why the last status change failed.
    pub status_failure: Option<FailureView>,
    /// The customer's details and where the ticket came in, as the Linux app
    /// lists them; only those it has.
    pub details: Vec<FactView>,
    /// When it was raised, as an ISO 8601 instant. Empty until read.
    pub created_at: String,
    /// When it was resolved, as an ISO 8601 instant, while it is.
    pub resolved_at: Option<String>,
    /// The conversation, oldest first.
    pub messages: Vec<DeskMessageView>,
    /// Why the last read again failed, shown beside the ticket.
    pub refresh_failure: Option<FailureView>,
    /// The reply being written, as the core holds it.
    pub reply: String,
    /// Whether the reply box can be written in: read, the member may use the
    /// desk, and no reply is on its way.
    pub can_write_reply: bool,
    /// Whether "Send" works for the reply.
    pub can_reply: bool,
    /// Whether the reply is on its way.
    pub sending: bool,
    /// Why the last reply failed. What was written stays.
    pub send_failure: Option<FailureView>,
    /// Whether the customer was emailed the last reply, in words, when the
    /// service said.
    pub notified_note: Option<String>,
}

impl Default for DeskTicketView {
    /// A ticket not read yet.
    fn default() -> Self {
        Self {
            title: TICKET_TITLE.to_owned(),
            ticket_id: String::new(),
            status: LoadStatus::Loading,
            reference_line: String::new(),
            statuses: Vec::new(),
            can_change_status: false,
            status_changing: false,
            status_failure: None,
            details: Vec::new(),
            created_at: String::new(),
            resolved_at: None,
            messages: Vec::new(),
            refresh_failure: None,
            reply: String::new(),
            can_write_reply: false,
            can_reply: false,
            sending: false,
            send_failure: None,
            notified_note: None,
        }
    }
}

/// One of a ticket's status buttons.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct DeskStatusChoiceView {
    /// The status it moves the ticket to.
    pub status: DeskStatus,
    /// Its label, in the core's words ("Waiting on the customer").
    pub label: String,
    /// Whether it is the ticket's status now (that button does nothing).
    pub selected: bool,
}

/// One message of a ticket's conversation.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct DeskMessageView {
    /// The message's id.
    pub id: String,
    /// Who wrote it, in the core's words ("Your team", "Receptionist",
    /// "Customer").
    pub author: String,
    /// The text.
    pub body: String,
    /// When it was written, as an ISO 8601 instant.
    pub created_at: String,
    /// Whether the workspace's team wrote it (shown on the workspace's side).
    pub from_team: bool,
}

/// The desk's settings and its logo.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct DeskSettingsView {
    /// The heading, [`SETTINGS_TITLE`].
    pub title: String,
    /// What the settings say under their heading, [`SETTINGS_INTRO`].
    pub intro: String,
    /// Where the read stands. No form is offered before the settings are
    /// read: one not built from them could only save over them.
    pub status: LoadStatus,
    /// The "desk on" switch's label.
    pub enabled_label: String,
    /// The note under it.
    pub enabled_note: String,
    /// Whether the desk takes tickets, as the form has it.
    pub enabled: bool,
    /// The "email customers" switch's label.
    pub notify_label: String,
    /// Whether customers are emailed replies, as the form has it.
    pub notify_customers_by_email: bool,
    /// The name box's label.
    pub brand_label: String,
    /// The name customers see, as the form has it; blank for the workspace's.
    pub brand_name: String,
    /// Whether the form can be changed: read, the member may use the desk,
    /// and no save is on its way.
    pub can_edit: bool,
    /// Whether "Save" works: something changed, and neither a save nor a logo
    /// change is on its way.
    pub can_save: bool,
    /// Whether a save is on its way.
    pub saving: bool,
    /// Why the last save failed.
    pub save_failure: Option<FailureView>,
    /// What the logo is and what the service takes, [`LOGO_HELP`].
    pub logo_help: String,
    /// Whether there is a logo, in words. Empty until read.
    pub logo_line: String,
    /// The logo customers see, when there is one.
    pub logo_url: Option<String>,
    /// Whether "Choose an image" works: read, the member may use the desk,
    /// and neither a save nor a logo change is on its way.
    pub can_choose_logo: bool,
    /// Whether "Remove" shows: there is a logo.
    pub show_remove_logo: bool,
    /// Whether "Remove" works.
    pub can_remove_logo: bool,
    /// Whether a logo upload or removal is on its way.
    pub logo_busy: bool,
    /// Why the last logo change failed, in the service's words where it gave
    /// them (too large, a type it will not host, bad dimensions).
    pub logo_failure: Option<FailureView>,
    /// That the logo was taken down but its file may still be reachable, in
    /// the core's words.
    pub logo_file_kept: Option<String>,
}

impl Default for DeskSettingsView {
    /// The settings before they are read.
    fn default() -> Self {
        Self {
            title: SETTINGS_TITLE.to_owned(),
            intro: SETTINGS_INTRO.to_owned(),
            status: LoadStatus::Loading,
            enabled_label: ENABLED_LABEL.to_owned(),
            enabled_note: ENABLED_NOTE.to_owned(),
            enabled: false,
            notify_label: NOTIFY_LABEL.to_owned(),
            notify_customers_by_email: false,
            brand_label: BRAND_LABEL.to_owned(),
            brand_name: String::new(),
            can_edit: false,
            can_save: false,
            saving: false,
            save_failure: None,
            logo_help: LOGO_HELP.to_owned(),
            logo_line: String::new(),
            logo_url: None,
            can_choose_logo: false,
            show_remove_logo: false,
            can_remove_logo: false,
            logo_busy: false,
            logo_failure: None,
            logo_file_kept: None,
        }
    }
}

/// Something the member did on the help desk's screens.
#[derive(Clone, Debug, PartialEq, Eq, uniffi::Enum)]
pub enum DeskAction {
    /// Open the help desk.
    Open,
    /// Open a ticket.
    OpenTicket {
        /// Which one (its id, not its `T-` reference).
        ticket_id: String,
    },
    /// Open the help desk's settings.
    OpenSettings,
    /// Show only the tickets `filter` picks.
    Filter {
        /// The choice.
        filter: DeskFilter,
    },
    /// Turn the desk on, from the switched-off state.
    TurnOn,
    /// Open the form raising a ticket.
    StartTicket,
    /// The form changed: the whole form as it now reads.
    EditTicket {
        /// The subject.
        subject: String,
        /// The customer's problem.
        message: String,
        /// The customer's name.
        requester_name: String,
        /// The customer's email address.
        requester_email: String,
        /// The customer's phone number.
        requester_phone: String,
    },
    /// Raise the ticket, or try again after a failure.
    SubmitTicket,
    /// Close the form, dropping what was typed.
    CancelTicket,
    /// Put away the confirmation of a ticket raised.
    DismissSubmitted,
    /// The open ticket's reply changed.
    EditReply {
        /// The reply as it now reads.
        text: String,
    },
    /// Send the reply.
    SendReply,
    /// Move the open ticket to `status`. Not asked first: every status can be
    /// moved back, and the answer shows at once.
    SetStatus {
        /// Where to.
        status: DeskStatus,
    },
    /// Put away the open ticket's failures.
    DismissTicketFailures,
    /// The "desk on" switch.
    SetEnabled {
        /// Its new position.
        on: bool,
    },
    /// The "email customers" switch.
    SetNotify {
        /// Its new position.
        on: bool,
    },
    /// The name box changed.
    EditBrandName {
        /// The name as it now reads.
        name: String,
    },
    /// Save what changed.
    SaveSettings,
    /// Publish a picked image as the logo. Refused here, and never sent, when
    /// [`desk_logo_problem`] has a reason.
    UploadLogo {
        /// The file, as C# read it.
        file: PickedFileView,
    },
    /// The picked logo could not be read at all.
    LogoUnreadable,
    /// Take the logo down.
    DeleteLogo,
    /// Put away the settings' failures and notes.
    DismissSettingsFailures,
}

/// The core events `action` is, in the order the core is to hear them. A logo
/// the service would not host is none at all.
pub(crate) fn events(action: DeskAction) -> Vec<Event> {
    let event = match action {
        DeskAction::Open => return vec![Event::Navigate(Route::Desk)],
        DeskAction::OpenTicket { ticket_id } => {
            return vec![Event::Navigate(Route::DeskTicket { ticket_id })];
        }
        DeskAction::OpenSettings => return vec![Event::Navigate(Route::DeskSettings)],
        DeskAction::UploadLogo { file } => {
            if logo_problem(&file).is_some() {
                return Vec::new();
            }
            DeskEvent::UploadLogo(picked_attachment(file))
        }
        DeskAction::Filter { filter } => DeskEvent::Filter(filter.status()),
        DeskAction::TurnOn => DeskEvent::TurnOn,
        DeskAction::StartTicket => DeskEvent::StartTicket,
        DeskAction::EditTicket {
            subject,
            message,
            requester_name,
            requester_email,
            requester_phone,
        } => DeskEvent::EditTicket(DeskTicketForm {
            subject,
            message,
            requester_name,
            requester_email,
            requester_phone,
        }),
        DeskAction::SubmitTicket => DeskEvent::SubmitTicket,
        DeskAction::CancelTicket => DeskEvent::CancelTicket,
        DeskAction::DismissSubmitted => DeskEvent::DismissSubmitted,
        DeskAction::EditReply { text } => DeskEvent::EditReply(text),
        DeskAction::SendReply => DeskEvent::SendReply,
        DeskAction::SetStatus { status } => DeskEvent::SetStatus(status.into()),
        DeskAction::DismissTicketFailures => DeskEvent::DismissTicketFailures,
        DeskAction::SetEnabled { on } => DeskEvent::SetEnabled(on),
        DeskAction::SetNotify { on } => DeskEvent::SetNotify(on),
        DeskAction::EditBrandName { name } => DeskEvent::EditBrandName(name),
        DeskAction::SaveSettings => DeskEvent::SaveSettings,
        DeskAction::LogoUnreadable => DeskEvent::LogoUnreadable,
        DeskAction::DeleteLogo => DeskEvent::DeleteLogo,
        DeskAction::DismissSettingsFailures => DeskEvent::DismissSettingsFailures,
    };
    vec![Event::Desk(event)]
}

/// Why the service would not host `file` as the help desk's logo, or `None`
/// when it is one it takes: a PNG, JPEG or WebP image (its type sniffed from
/// its bytes, never its name, so a GIF renamed `.png` is still a GIF) of one
/// byte up to the core's limit on an image, five megabytes. The chooser reads
/// one byte past the limit, so a larger file arrives one byte over it.
#[uniffi::export]
pub fn desk_logo_problem(file: PickedFileView) -> Option<String> {
    logo_problem(&file)
}

/// [`desk_logo_problem`], on a borrowed file.
fn logo_problem(file: &PickedFileView) -> Option<String> {
    let head = PickedFileView {
        file_name: String::new(),
        size: file.size,
        bytes: file.bytes.iter().take(12).copied().collect(),
    };
    let mime_type = picked_attachment(head).mime_type;
    if !LOGO_TYPES.contains(&mime_type.as_str()) {
        Some(LOGO_TYPE_REFUSED.to_owned())
    } else if file.bytes.len() > MAX_ATTACHMENT_BYTES {
        Some(format!(
            "The logo must be between 1 byte and {} MB.",
            MAX_ATTACHMENT_BYTES / (1024 * 1024)
        ))
    } else {
        None
    }
}

/// The page of the help desk, for a signed-in model.
pub(crate) fn screen(_model: &Model, signed_in: &SignedIn) -> ScreenView {
    ScreenView::Desk {
        view: desk_view(&signed_in.desk, &signed_in.capabilities()),
    }
}

/// The page of the ticket asked for, for a signed-in model. A ticket the core
/// has not opened yet (or another one) shows as being read.
pub(crate) fn ticket_screen(_model: &Model, signed_in: &SignedIn, ticket_id: &str) -> ScreenView {
    let view = match signed_in
        .desk_ticket
        .as_ref()
        .filter(|screen| screen.ticket_id == ticket_id)
    {
        Some(screen) => ticket_view(screen, &signed_in.capabilities()),
        None => DeskTicketView {
            ticket_id: ticket_id.to_owned(),
            ..DeskTicketView::default()
        },
    };
    ScreenView::DeskTicket { view }
}

/// The page of the help desk's settings, for a signed-in model.
pub(crate) fn settings_screen(_model: &Model, signed_in: &SignedIn) -> ScreenView {
    ScreenView::DeskSettings {
        view: settings_view(signed_in.desk_settings.as_ref(), &signed_in.capabilities()),
    }
}

/// The filter's choices, each counting `queue`'s tickets once it is read, with
/// `chosen` selected.
fn filters(queue: Option<&DeskTickets>, chosen: Option<DeskTicketStatus>) -> Vec<DeskFilterView> {
    DeskFilter::ALL
        .into_iter()
        .map(|filter| {
            let status = filter.status();
            let (words, count) = match status {
                None => ("Every ticket", queue.map(|queue| queue.tickets.len())),
                Some(status) => (
                    desk_status_label(status),
                    queue.map(|queue| queue.count(status)),
                ),
            };
            DeskFilterView {
                filter,
                label: match count {
                    Some(count) => format!("{words} ({count})"),
                    None => words.to_owned(),
                },
                selected: status == chosen,
            }
        })
        .collect()
}

fn desk_view(desk: &DeskScreen, capabilities: &Capabilities) -> DeskView {
    let allowed = capabilities.can_use_desk;
    let mut view = DeskView {
        submitted: desk.submitted.as_ref().map(|submitted| submitted.message()),
        can_open_settings: allowed,
        compose: desk.compose.as_ref().map(compose_view),
        ..DeskView::default()
    };
    let mut queue_read = None;
    match &desk.queue {
        DeskQueue::NotLoaded | DeskQueue::Loading => {}
        DeskQueue::Failed(failure) => {
            view.status = LoadStatus::failed(DeskQueue::FAILED_TITLE, failure);
        }
        DeskQueue::Off => {
            view.status = LoadStatus::Ready;
            view.off = Some(DeskOffView {
                title: DeskQueue::OFF_TITLE.to_owned(),
                body: DeskQueue::OFF_BODY.to_owned(),
                turn_on: DeskQueue::TURN_ON.to_owned(),
                can_turn_on: allowed && !desk.enabling,
                enabling: desk.enabling,
                failure: failure(desk.enable_failure.as_ref()),
            });
        }
        DeskQueue::Ready(queue) => {
            queue_read = Some(queue);
            view.status = LoadStatus::Ready;
            view.can_filter = true;
            view.rows = desk
                .visible()
                .unwrap_or_default()
                .into_iter()
                .map(row)
                .collect();
            view.empty = queue
                .tickets
                .is_empty()
                .then(|| EmptyView::new(DeskQueue::EMPTY_TITLE, DeskQueue::EMPTY_BODY));
            view.none_matching = (!queue.tickets.is_empty() && view.rows.is_empty())
                .then(|| DeskQueue::NONE_MATCHING.to_owned());
            view.refreshing = queue.refreshing;
            view.can_start = allowed && desk.compose.is_none();
        }
    }
    view.filters = filters(queue_read, desk.filter);
    view
}

/// Who raised `ticket`, as far as it says: its name, email address or phone
/// number, the first it has, or [`NO_CUSTOMER`].
fn requester(ticket: &DeskTicketSummary) -> String {
    [
        &ticket.requester_name,
        &ticket.requester_email,
        &ticket.requester_phone,
    ]
    .into_iter()
    .flatten()
    .find(|value| !value.trim().is_empty())
    .map_or_else(
        || NO_CUSTOMER.to_owned(),
        |value| format_phone_number(value),
    )
}

/// A status as it reads in full ("Waiting on the customer"), or the service's
/// own word for a status this build does not know.
fn status_label(status: &str) -> String {
    desk_status(status).map_or_else(
        || humanize(status),
        |known| desk_status_label(known).to_owned(),
    )
}

/// A status in a row of the queue, where it has little room: the wait is the
/// customer's, which the ticket itself says in full.
fn short_status_label(status: &str) -> String {
    match desk_status(status) {
        Some(DeskTicketStatus::Waiting) => "Waiting".to_owned(),
        _ => status_label(status),
    }
}

fn row(ticket: &DeskTicketSummary) -> DeskRowView {
    DeskRowView {
        ticket_id: ticket.id.clone(),
        subject: ticket.subject.clone(),
        reference: ticket.display_reference.clone(),
        requester: requester(ticket),
        status_label: short_status_label(&ticket.status),
        status: desk_status(&ticket.status).map(DeskStatus::from),
        updated_at: ticket.updated_at.clone(),
    }
}

/// What the form needs, from the core's own bounds, as the Linux app says it.
fn needs() -> String {
    format!("A subject of {DESK_SUBJECT_MIN} to {DESK_SUBJECT_MAX} characters, and a message.")
}

/// A bound of the core's, as the box's character limit.
fn limit(bound: usize) -> u32 {
    u32::try_from(bound).unwrap_or(u32::MAX)
}

fn compose_view(compose: &DeskCompose) -> DeskComposeView {
    let form = &compose.form;
    let can_submit = form.can_submit();
    DeskComposeView {
        title: COMPOSE_TITLE.to_owned(),
        submit_label: COMPOSE_SUBMIT.to_owned(),
        subject: form.subject.clone(),
        message: form.message.clone(),
        requester_name: form.requester_name.clone(),
        requester_email: form.requester_email.clone(),
        requester_phone: form.requester_phone.clone(),
        customer_note: CUSTOMER_NOTE.to_owned(),
        subject_max: limit(DESK_SUBJECT_MAX),
        message_max: limit(DESK_MESSAGE_MAX),
        needs: (!can_submit).then(needs),
        can_submit: can_submit && !compose.submitting,
        submitting: compose.submitting,
        failure: failure(compose.failure.as_ref()),
    }
}

/// Whether the customer was emailed the last reply, in words, when known.
fn notified_note(notified: Option<bool>) -> Option<String> {
    notified.map(|emailed| if emailed { NOTIFIED } else { NOT_NOTIFIED }.to_owned())
}

/// The ticket's details, as the Linux app lists them: only those it has. Its
/// dates go separately, for the app to say in local time.
fn details(ticket: &DeskTicketDetail) -> Vec<FactView> {
    facts([
        ("Name", ticket.requester_name.clone()),
        ("Email address", ticket.requester_email.clone()),
        (
            "Phone number",
            ticket.requester_phone.as_deref().map(format_phone_number),
        ),
        ("Came in by", Some(humanize(&ticket.source))),
    ])
}

fn message(message: &DeskMessage) -> DeskMessageView {
    DeskMessageView {
        id: message.id.clone(),
        author: desk_author_label(&message.author_type).to_owned(),
        body: message.body.clone(),
        created_at: message.created_at.clone(),
        from_team: message.author_type == "team",
    }
}

fn ticket_view(screen: &DeskTicketScreen, capabilities: &Capabilities) -> DeskTicketView {
    let controls = screen.controls(capabilities);
    let mut view = DeskTicketView {
        ticket_id: screen.ticket_id.clone(),
        can_change_status: controls.can_change_status,
        status_changing: screen.status_change.is_some(),
        status_failure: failure(screen.status_failure.as_ref()),
        refresh_failure: failure(screen.refresh_failure.as_ref()),
        reply: screen.reply.clone(),
        can_write_reply: capabilities.can_use_desk && screen.detail().is_some() && !screen.sending,
        can_reply: controls.can_reply,
        sending: screen.sending,
        send_failure: failure(screen.send_failure.as_ref()),
        notified_note: notified_note(screen.last_notified),
        ..DeskTicketView::default()
    };
    match &screen.ticket {
        DeskTicketRead::Loading => {}
        DeskTicketRead::Failed(failure) => {
            view.status = LoadStatus::failed(TICKET_FAILED_TITLE, failure);
        }
        DeskTicketRead::Ready(detail) => {
            view.status = LoadStatus::Ready;
            view.title = detail.subject.clone();
            view.reference_line = format!(
                "{} \u{b7} {}",
                detail.display_reference,
                status_label(&detail.status)
            );
            view.statuses = STATUSES
                .into_iter()
                .map(|status| DeskStatusChoiceView {
                    status: status.into(),
                    label: desk_status_label(status).to_owned(),
                    selected: controls.status == Some(status),
                })
                .collect();
            view.details = details(detail);
            view.created_at = detail.created_at.clone();
            view.resolved_at = detail.resolved_at.clone();
            view.messages = detail.messages.iter().map(message).collect();
        }
    }
    view
}

fn settings_view(
    settings: Option<&DeskSettingsRead>,
    capabilities: &Capabilities,
) -> DeskSettingsView {
    let mut view = DeskSettingsView::default();
    match settings {
        None | Some(DeskSettingsRead::Loading) => {}
        Some(DeskSettingsRead::Failed(failure)) => {
            view.status = LoadStatus::failed(DeskSettingsRead::FAILED_TITLE, failure);
        }
        Some(DeskSettingsRead::Ready(form)) => {
            draw_form(&mut view, form, capabilities.can_use_desk);
        }
    }
    view
}

/// The settings form `form`, for a member who may (`allowed`) use the desk.
fn draw_form(view: &mut DeskSettingsView, form: &DeskSettingsForm, allowed: bool) {
    // A save and a logo change each answer with the whole settings as stored,
    // so neither starts while the other is on its way.
    let writing = form.saving || form.logo_busy;
    let has_logo = form.stored.public_logo_url.is_some();
    view.status = LoadStatus::Ready;
    view.enabled = form.enabled;
    view.notify_customers_by_email = form.notify_customers_by_email;
    view.brand_name = form.brand_name.clone();
    view.can_edit = allowed && !form.saving;
    view.can_save = allowed && !writing && form.is_dirty();
    view.saving = form.saving;
    view.save_failure = failure(form.save_failure.as_ref());
    view.logo_line = if has_logo { LOGO_PUBLISHED } else { LOGO_NONE }.to_owned();
    view.logo_url = form.stored.public_logo_url.clone();
    view.can_choose_logo = allowed && !writing;
    view.show_remove_logo = has_logo;
    view.can_remove_logo = allowed && !writing && has_logo;
    view.logo_busy = form.logo_busy;
    view.logo_failure = failure(form.logo_failure.as_ref());
    view.logo_file_kept = form
        .logo_file_kept
        .then(|| DeskSettingsForm::LOGO_FILE_KEPT.to_owned());
}

#[cfg(test)]
mod tests {
    use district_core::Capabilities;

    use super::*;

    const PNG: &[u8] = b"\x89PNG\r\n\x1a\n\0\0\0\rIHDR";
    const JPEG: &[u8] = &[0xff, 0xd8, 0xff, 0xe0, 0, 0x10, b'J', b'F', b'I', b'F', 0];
    const WEBP: &[u8] = b"RIFF\x24\0\0\0WEBPVP8 ";
    const GIF: &[u8] = b"GIF89a\x01\0\x01\0";

    fn file(name: &str, bytes: &[u8]) -> PickedFileView {
        PickedFileView {
            file_name: name.to_owned(),
            size: bytes.len() as u64,
            bytes: bytes.to_vec(),
        }
    }

    #[test]
    fn each_status_crosses_both_ways() {
        for status in STATUSES {
            assert_eq!(DeskTicketStatus::from(DeskStatus::from(status)), status);
        }
    }

    #[test]
    fn the_words_the_core_has_no_sentence_for() {
        assert_eq!(needs(), "A subject of 3 to 200 characters, and a message.");
        assert_eq!(limit(usize::MAX), u32::MAX);
        assert_eq!(notified_note(Some(true)).as_deref(), Some(NOTIFIED));
        assert_eq!(notified_note(Some(false)).as_deref(), Some(NOT_NOTIFIED));
        assert_eq!(notified_note(None), None);
        assert_eq!(status_label("waiting"), "Waiting on the customer");
        assert_eq!(short_status_label("waiting"), "Waiting");
        assert_eq!(short_status_label("resolved"), "Resolved");
        assert_eq!(status_label("on-hold"), "On hold");
    }

    /// A GIF is refused for its type, whatever it is called, and so is
    /// anything that is not an image the service hosts; the size is the
    /// core's limit, to the byte.
    #[test]
    fn a_gif_is_refused_and_so_is_a_logo_too_large() {
        for (name, bytes) in [("logo.png", PNG), ("logo.jpg", JPEG), ("logo.webp", WEBP)] {
            assert_eq!(desk_logo_problem(file(name, bytes)), None, "{name}");
        }
        let refused = Some(LOGO_TYPE_REFUSED.to_owned());
        assert_eq!(desk_logo_problem(file("logo.gif", GIF)), refused);
        assert_eq!(desk_logo_problem(file("logo.png", GIF)), refused);
        assert_eq!(desk_logo_problem(file("empty.png", b"")), refused);
        assert_eq!(desk_logo_problem(file("doc.png", b"%PDF-1.7")), refused);
        let mut large = PNG.to_vec();
        large.resize(MAX_ATTACHMENT_BYTES + 1, 0);
        assert_eq!(
            desk_logo_problem(file("large.png", &large)),
            Some("The logo must be between 1 byte and 5 MB.".to_owned())
        );
        large.truncate(MAX_ATTACHMENT_BYTES);
        assert_eq!(desk_logo_problem(file("large.png", &large)), None);
    }

    /// A refused logo is never handed to the core; one it takes is, its type
    /// sniffed and its name without its folder.
    #[test]
    fn a_refused_logo_is_no_event() {
        assert!(
            events(DeskAction::UploadLogo {
                file: file("logo.gif", GIF)
            })
            .is_empty()
        );
        let sent = events(DeskAction::UploadLogo {
            file: file("C:\\logos\\logo.jpg", PNG),
        });
        let [Event::Desk(DeskEvent::UploadLogo(logo))] = sent.as_slice() else {
            panic!("not an upload: {sent:?}");
        };
        assert_eq!(logo.mime_type, "image/png");
        assert_eq!(logo.file_name, "logo.jpg");
    }

    /// No screen state at all reads as being read, and the settings offer
    /// nothing before they are read.
    #[test]
    fn nothing_read_offers_nothing() {
        let agency = Capabilities::for_role(Some("agency"));
        let settings = settings_view(None, &agency);
        assert_eq!(settings, DeskSettingsView::default());
        assert!(!settings.can_edit && !settings.can_choose_logo);
        let queue = desk_view(&DeskScreen::default(), &agency);
        assert_eq!(queue.status, LoadStatus::Loading);
        assert!(!queue.can_start && queue.can_open_settings);
        assert_eq!(
            queue
                .filters
                .iter()
                .map(|f| f.label.as_str())
                .collect::<Vec<_>>(),
            [
                "Every ticket",
                "Open",
                "Waiting on the customer",
                "Resolved"
            ]
        );
        assert!(queue.filters[0].selected);
        let viewer = Capabilities::for_role(Some("viewer"));
        assert!(!desk_view(&DeskScreen::default(), &viewer).can_open_settings);
    }
}
