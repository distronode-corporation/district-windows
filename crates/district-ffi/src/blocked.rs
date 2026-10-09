//! The callers the workspace has blocked, below contacts: the list, and
//! unblocking a caller after the core's question, one request per caller at a
//! time. What the Linux blocked view shows.

use district_core::{
    BlockedList, BlockedScreen, Capabilities, ContactConfirmation, ContactsEvent, Event, Model,
    Route, SignedIn, blocked_label, format_phone_number,
};
use district_model::BlockedContact;
use serde::Serialize;

use crate::screen::ScreenView;
use crate::views::{EmptyView, FailureView, LoadStatus, failure};

/// Whether this version has the area's screens. The packet that builds them
/// sets it; until then [`crate::nav::built`] says no for its routes.
pub(crate) const BUILT: bool = true;

/// The screen's heading, as the Linux app words it.
pub const BLOCKED_TITLE: &str = "Blocked callers";

/// The body of the empty list, as the Linux app words it.
pub const BLOCKED_EMPTY_BODY: &str =
    "Callers you block from a contact's page are listed here, where you can unblock them.";

/// The blocked callers screen.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct BlockedView {
    /// The heading, [`BLOCKED_TITLE`].
    pub title: String,
    /// Where the read stands.
    pub status: LoadStatus,
    /// The callers blocked now, most recently blocked first.
    pub rows: Vec<BlockedRowView>,
    /// What to say when the list is read and has none.
    pub empty: Option<EmptyView>,
    /// Whether "Unblock" is offered: the member's role may change contacts.
    pub can_unblock: bool,
    /// The question the core is asking before an unblock, while it asks.
    pub confirming: Option<UnblockQuestionView>,
    /// Why the last unblock failed, shown beside the list.
    pub failure: Option<FailureView>,
}

impl Default for BlockedView {
    /// The screen before anything is read.
    fn default() -> Self {
        Self {
            title: BLOCKED_TITLE.to_owned(),
            status: LoadStatus::Loading,
            rows: Vec::new(),
            empty: None,
            can_unblock: false,
            confirming: None,
            failure: None,
        }
    }
}

/// One blocked caller.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct BlockedRowView {
    /// The caller's contact id, to unblock them by.
    pub contact_id: String,
    /// The name, or else the number (the core's `blocked_label`).
    pub name: String,
    /// The number, when the name is not the number already.
    pub phone_number: Option<String>,
    /// When they were blocked, ISO 8601, for the app to say in local time.
    pub blocked_at: Option<String>,
    /// Whether their unblock is on its way (show progress, not the button).
    pub unblocking: bool,
}

/// The question before an unblock.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct UnblockQuestionView {
    /// The caller asked about.
    pub contact_id: String,
    /// What they are called, for the dialog's heading.
    pub name: String,
    /// The question, in the core's words.
    pub question: String,
    /// The confirming button's label, in the core's words.
    pub action: String,
}

/// Something the member did on the blocked callers screen.
#[derive(Clone, Debug, PartialEq, Eq, uniffi::Enum)]
pub enum BlockedAction {
    /// Open the blocked callers.
    Open,
    /// "Unblock" on a caller: the core asks first.
    AskUnblock {
        /// The caller's contact id.
        contact_id: String,
    },
    /// Answer the question yes.
    ConfirmUnblock,
    /// Answer it no.
    CancelUnblock,
    /// Put away the last unblock's failure.
    DismissFailure,
}

/// The core events `action` is, in the order the core is to hear them.
pub(crate) fn events(action: BlockedAction) -> Vec<Event> {
    vec![match action {
        BlockedAction::Open => Event::Navigate(Route::BlockedContacts),
        BlockedAction::AskUnblock { contact_id } => {
            Event::Contacts(ContactsEvent::AskUnblock { contact_id })
        }
        BlockedAction::ConfirmUnblock => Event::Contacts(ContactsEvent::ConfirmUnblock),
        BlockedAction::CancelUnblock => Event::Contacts(ContactsEvent::CancelUnblock),
        BlockedAction::DismissFailure => Event::Contacts(ContactsEvent::DismissUnblockFailure),
    }]
}

fn row(caller: &BlockedContact, screen: &BlockedScreen) -> BlockedRowView {
    let name = blocked_label(caller);
    BlockedRowView {
        contact_id: caller.contact_id.clone(),
        phone_number: caller
            .phone_number
            .as_deref()
            .map(format_phone_number)
            .filter(|number| !number.trim().is_empty() && *number != name),
        name,
        blocked_at: caller.blocked_at.clone(),
        unblocking: screen.unblocking.contains(&caller.contact_id),
    }
}

/// The blocked callers, as the Linux blocked view shows them.
pub(crate) fn blocked_view(screen: &BlockedScreen, capabilities: &Capabilities) -> BlockedView {
    let mut view = BlockedView {
        can_unblock: capabilities.can_change,
        confirming: screen
            .confirming
            .as_ref()
            .map(|caller| UnblockQuestionView {
                contact_id: caller.contact_id.clone(),
                name: blocked_label(caller),
                question: screen.question().to_owned(),
                action: ContactConfirmation::Unblock.action().to_owned(),
            }),
        failure: failure(screen.failure.as_ref()),
        ..BlockedView::default()
    };
    match &screen.list {
        BlockedList::NotLoaded | BlockedList::Loading => {}
        BlockedList::Failed(failure) => {
            view.status = LoadStatus::failed(BlockedList::FAILED_TITLE, failure);
        }
        BlockedList::Ready(rows) => {
            view.status = LoadStatus::Ready;
            view.rows = rows.iter().map(|caller| row(caller, screen)).collect();
            view.empty = rows
                .is_empty()
                .then(|| EmptyView::new(BlockedList::EMPTY_TITLE, BLOCKED_EMPTY_BODY));
        }
    }
    view
}

/// The page of the blocked callers, for a signed-in model.
pub(crate) fn screen(_model: &Model, signed_in: &SignedIn) -> ScreenView {
    ScreenView::BlockedContacts {
        view: blocked_view(&signed_in.blocked, &signed_in.capabilities()),
    }
}
