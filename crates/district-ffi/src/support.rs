//! Support requests from the workspace to Distronode: the list, one request
//! and its conversation, and the form that raises a request.
//!
//! The words are the core's (district-core's `support.rs`) and, where the core
//! has none, District AI for Linux's (`pages/support.rs`,
//! `pages/support_request.rs` and `pages/ticket_form.rs`). Every support route
//! refuses a viewer, reads included, so the navigation pane never offers this
//! area to one and the core never opens its screens for one.
//!
//! The form raising a request is the same draft a Report fills (report.rs),
//! which is why a Report is refused while a draft written here holds anything.

use district_core::{
    Event, FailureText, Model, Route, SUPPORT_MESSAGE_MAX, SUPPORT_SUBJECT_MAX,
    SUPPORT_SUBJECT_MIN, SignedIn, SupportCompose, SupportEvent, SupportForm, SupportList,
    SupportRequestScreen, SupportRequestView as SupportRead, SupportScreen, support_request_key,
};
use district_model::{SupportMessage, SupportRequestKind, SupportRequestSummary};
use serde::Serialize;

use crate::screen::ScreenView;
use crate::views::{EmptyView, FailureView, LoadStatus, failure};

/// Whether this version has the area's screens. The packet that builds them
/// sets it; until then [`crate::nav::built`] says no for its routes.
pub(crate) const BUILT: bool = true;

/// The list page's heading.
pub const SUPPORT_TITLE: &str = "Support";
/// The heading of a request not read yet, or that could not be read.
pub const REQUEST_TITLE: &str = "Support request";
/// The heading when a request could not be read, as the Linux app words it.
pub const REQUEST_FAILED_TITLE: &str = "Could not load this request";
/// What a request still being opened shows in place of its reference.
pub const NOT_FILED_YET: &str = "Not filed yet";
/// The form's heading.
pub const COMPOSE_TITLE: &str = "New support request";

/// The support requests, and the form raising one.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct SupportView {
    /// The heading.
    pub title: String,
    /// Where the list's first read stands.
    pub status: LoadStatus,
    /// What to say when the list is read and empty.
    pub empty: Option<EmptyView>,
    /// The requests still open, as the service orders them.
    pub open: Vec<SupportRowView>,
    /// The requests resolved.
    pub resolved: Vec<SupportRowView>,
    /// A note that the list is at the service's cap and may be missing older
    /// requests.
    pub capped_note: Option<String>,
    /// Whether the list is being read again, with these still showing.
    pub refreshing: bool,
    /// Why the last read again failed, shown beside the list.
    pub refresh_failure: Option<FailureView>,
    /// The confirmation of a request raised, until dismissed
    /// ([`SupportAction::DismissSubmitted`]).
    pub submitted: Option<String>,
    /// Whether "New request" is offered: no form is open.
    pub can_start: bool,
    /// The form raising a request, while it is open.
    pub compose: Option<SupportComposeView>,
}

impl Default for SupportView {
    fn default() -> Self {
        Self {
            title: SUPPORT_TITLE.to_owned(),
            status: LoadStatus::Loading,
            empty: None,
            open: Vec::new(),
            resolved: Vec::new(),
            capped_note: None,
            refreshing: false,
            refresh_failure: None,
            submitted: None,
            can_start: true,
            compose: None,
        }
    }
}

/// One request in the list.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct SupportRowView {
    /// What opens it ([`SupportAction::OpenRequest`]): its support desk key, or
    /// the service's own id before it has one.
    pub key: String,
    /// The subject.
    pub subject: String,
    /// Its support desk key (`DA-42`), or [`NOT_FILED_YET`].
    pub reference: String,
    /// Where it stands, in the support desk's own word, shown as it is.
    pub status: String,
    /// When it last changed, as an ISO 8601 instant.
    pub updated_at: String,
}

/// What a request is about, as the form offers it.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Serialize, uniffi::Enum)]
pub enum SupportKind {
    /// Something is broken.
    Problem,
    /// A question.
    Question,
    /// A suggestion.
    Suggestion,
}

impl From<SupportRequestKind> for SupportKind {
    fn from(kind: SupportRequestKind) -> Self {
        match kind {
            SupportRequestKind::Problem => Self::Problem,
            SupportRequestKind::Question => Self::Question,
            SupportRequestKind::Suggestion => Self::Suggestion,
        }
    }
}

impl From<SupportKind> for SupportRequestKind {
    fn from(kind: SupportKind) -> Self {
        match kind {
            SupportKind::Problem => Self::Problem,
            SupportKind::Question => Self::Question,
            SupportKind::Suggestion => Self::Suggestion,
        }
    }
}

/// One choice of what a request is about.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct SupportKindView {
    /// The kind.
    pub kind: SupportKind,
    /// Its label, in the core's words.
    pub label: String,
}

/// The form raising a request.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct SupportComposeView {
    /// The form's heading.
    pub title: String,
    /// The kinds to choose from, in the core's order.
    pub kinds: Vec<SupportKindView>,
    /// The kind chosen.
    pub kind: SupportKind,
    /// The subject, as typed.
    pub subject: String,
    /// The message, as typed.
    pub message: String,
    /// The longest subject the service takes, for the box's limit.
    pub subject_max: u32,
    /// The longest message the service takes, for the box's limit.
    pub message_max: u32,
    /// What the form needs before it can be sent, while it cannot be.
    pub needs: Option<String>,
    /// Whether "Send" works, by the service's own bounds.
    pub can_submit: bool,
    /// Whether the request is on its way. The form cannot be changed or
    /// discarded meanwhile.
    pub submitting: bool,
    /// Why the last attempt failed. Sending again sends the same draft, which
    /// the service recognises.
    pub failure: Option<FailureView>,
}

/// One support request.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct SupportRequestView {
    /// The heading: the subject once read, [`REQUEST_TITLE`] before.
    pub title: String,
    /// The key the page was opened by.
    pub key: String,
    /// Where the read stands.
    pub status: LoadStatus,
    /// Its support desk key (`DA-42`), or [`NOT_FILED_YET`]. Empty until read.
    pub reference: String,
    /// Where it stands, in the support desk's own word. Empty until read.
    pub status_name: String,
    /// Whether it is resolved.
    pub resolved: bool,
    /// When it was raised, as an ISO 8601 instant. Empty until read.
    pub created_at: String,
    /// The conversation, oldest first.
    pub messages: Vec<SupportMessageView>,
    /// Why the last read again failed, shown beside the request.
    pub refresh_failure: Option<FailureView>,
    /// The reply being written.
    pub reply: String,
    /// Whether "Send" works for the reply.
    pub can_reply: bool,
    /// Whether the reply is on its way.
    pub sending: bool,
    /// Why the last reply failed. What was written stays.
    pub send_failure: Option<FailureView>,
    /// Whether "Mark as resolved" shows: it can be closed, or a close is on
    /// its way.
    pub close_offered: bool,
    /// Whether "Mark as resolved" works.
    pub can_close: bool,
    /// Whether the close is on its way.
    pub closing: bool,
    /// Whether the question before closing is showing.
    pub confirming_close: bool,
    /// The question before closing.
    pub close_question: String,
    /// The closing button's label, and the question's yes.
    pub close_action: String,
    /// Why the last close failed.
    pub close_failure: Option<FailureView>,
    /// The confirmation of a close, until dismissed.
    pub closed_message: Option<String>,
}

impl Default for SupportRequestView {
    fn default() -> Self {
        Self {
            title: REQUEST_TITLE.to_owned(),
            key: String::new(),
            status: LoadStatus::Loading,
            reference: String::new(),
            status_name: String::new(),
            resolved: false,
            created_at: String::new(),
            messages: Vec::new(),
            refresh_failure: None,
            reply: String::new(),
            can_reply: false,
            sending: false,
            send_failure: None,
            close_offered: false,
            can_close: false,
            closing: false,
            confirming_close: false,
            close_question: SupportRequestScreen::CLOSE_QUESTION.to_owned(),
            close_action: SupportRequestScreen::CLOSE_ACTION.to_owned(),
            close_failure: None,
            closed_message: None,
        }
    }
}

/// One message of a request's conversation.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct SupportMessageView {
    /// The message's id.
    pub id: String,
    /// Who wrote it, as the service labels them (`You`, `Distronode Support`).
    pub author: String,
    /// The text.
    pub body: String,
    /// When it was written, as an ISO 8601 instant.
    pub created_at: String,
    /// Whether the workspace wrote it (shown on the workspace's side).
    pub from_workspace: bool,
}

/// Something the member did on the support screens.
#[derive(Clone, Debug, PartialEq, Eq, uniffi::Enum)]
pub enum SupportAction {
    /// Open the support requests.
    Open,
    /// Open a request.
    OpenRequest {
        /// Its support desk key (such as `DA-42`), or the service's own id.
        key: String,
    },
    /// Open the form raising a request, with a new draft.
    StartRequest,
    /// The form changed: the whole form as it now reads.
    EditRequest {
        /// What it is about.
        kind: SupportKind,
        /// The subject.
        subject: String,
        /// The message.
        message: String,
    },
    /// Send the request, or try again with the same draft.
    SubmitRequest,
    /// Discard the draft and close the form.
    CancelRequest,
    /// Put away the confirmation of a request raised.
    DismissSubmitted,
    /// The open request's reply changed.
    EditReply {
        /// The reply as it now reads.
        text: String,
    },
    /// Send the reply.
    SendReply,
    /// Ask before marking the open request resolved.
    AskClose,
    /// Answer the question yes.
    ConfirmClose,
    /// Answer it no.
    CancelClose,
    /// Put away the open request's failures and its closing confirmation.
    DismissFailures,
}

/// The core events `action` is, in the order the core is to hear them.
pub(crate) fn events(action: SupportAction) -> Vec<Event> {
    vec![match action {
        SupportAction::Open => Event::Navigate(Route::Support),
        SupportAction::OpenRequest { key } => Event::Navigate(Route::SupportRequest { key }),
        SupportAction::StartRequest => Event::Support(SupportEvent::StartRequest),
        SupportAction::EditRequest {
            kind,
            subject,
            message,
        } => Event::Support(SupportEvent::EditRequest(SupportForm {
            kind: kind.into(),
            subject,
            message,
        })),
        SupportAction::SubmitRequest => Event::Support(SupportEvent::SubmitRequest),
        SupportAction::CancelRequest => Event::Support(SupportEvent::CancelRequest),
        SupportAction::DismissSubmitted => Event::Support(SupportEvent::DismissSubmitted),
        SupportAction::EditReply { text } => Event::Support(SupportEvent::EditReply(text)),
        SupportAction::SendReply => Event::Support(SupportEvent::SendReply),
        SupportAction::AskClose => Event::Support(SupportEvent::AskClose),
        SupportAction::ConfirmClose => Event::Support(SupportEvent::ConfirmClose),
        SupportAction::CancelClose => Event::Support(SupportEvent::CancelClose),
        SupportAction::DismissFailures => Event::Support(SupportEvent::DismissFailures),
    }]
}

/// The page of the support requests, for a signed-in model.
pub(crate) fn screen(_model: &Model, signed_in: &SignedIn) -> ScreenView {
    ScreenView::Support {
        view: support_view(&signed_in.support),
    }
}

/// The page of the request asked for, for a signed-in model. A request the
/// core has not opened yet (or another one) shows as being read.
pub(crate) fn request_screen(_model: &Model, signed_in: &SignedIn, key: &str) -> ScreenView {
    let view = match signed_in
        .support_request
        .as_ref()
        .filter(|screen| screen.key == key)
    {
        Some(screen) => request_view(screen),
        None => SupportRequestView {
            key: key.to_owned(),
            ..SupportRequestView::default()
        },
    };
    ScreenView::SupportRequest { view }
}

fn support_view(support: &SupportScreen) -> SupportView {
    let mut view = SupportView {
        submitted: support
            .submitted
            .as_ref()
            .map(SupportScreen::submitted_message),
        can_start: support.compose.is_none(),
        compose: support.compose.as_ref().map(compose_view),
        ..SupportView::default()
    };
    match &support.list {
        SupportList::NotLoaded | SupportList::Loading => {}
        SupportList::Failed(failure) => {
            view.status = LoadStatus::failed(SupportList::FAILED_TITLE, failure);
        }
        SupportList::Ready(requests) => {
            view.status = LoadStatus::Ready;
            view.empty = requests
                .requests
                .is_empty()
                .then(|| EmptyView::new(SupportList::EMPTY_TITLE, SupportList::EMPTY_BODY));
            view.open = requests.open().into_iter().map(row).collect();
            view.resolved = requests.resolved().into_iter().map(row).collect();
            view.capped_note = requests
                .capped()
                .then(|| district_core::SupportRequests::CAPPED.to_owned());
            view.refreshing = requests.refreshing;
            view.refresh_failure = failure(requests.refresh_failure.as_ref());
        }
    }
    view
}

fn row(request: &SupportRequestSummary) -> SupportRowView {
    SupportRowView {
        key: support_request_key(request).to_owned(),
        subject: request.subject.clone(),
        reference: reference(request.issue_key.as_deref()),
        status: request.status_name.clone(),
        updated_at: request.updated_at.clone(),
    }
}

/// A request's reference: its support desk key, or that it has none yet.
fn reference(issue_key: Option<&str>) -> String {
    issue_key
        .filter(|key| !key.trim().is_empty())
        .unwrap_or(NOT_FILED_YET)
        .to_owned()
}

fn compose_view(compose: &SupportCompose) -> SupportComposeView {
    let form = &compose.form;
    let can_submit = form.can_submit();
    SupportComposeView {
        title: COMPOSE_TITLE.to_owned(),
        kinds: SupportForm::KINDS
            .iter()
            .map(|(kind, label)| SupportKindView {
                kind: (*kind).into(),
                label: (*label).to_owned(),
            })
            .collect(),
        kind: form.kind.into(),
        subject: form.subject.clone(),
        message: form.message.clone(),
        subject_max: limit(SUPPORT_SUBJECT_MAX),
        message_max: limit(SUPPORT_MESSAGE_MAX),
        needs: (!can_submit).then(needs),
        can_submit,
        submitting: compose.submitting,
        failure: failure(compose.failure.as_ref()),
    }
}

/// What the form needs, from the core's own bounds, as the Linux app says it.
fn needs() -> String {
    format!(
        "A subject of {SUPPORT_SUBJECT_MIN} to {SUPPORT_SUBJECT_MAX} characters, and a message."
    )
}

/// A bound of the core's, as the box's character limit.
fn limit(bound: usize) -> u32 {
    u32::try_from(bound).unwrap_or(u32::MAX)
}

fn request_view(screen: &SupportRequestScreen) -> SupportRequestView {
    let mut view = SupportRequestView {
        key: screen.key.clone(),
        refresh_failure: failure(screen.refresh_failure.as_ref()),
        reply: screen.reply.clone(),
        can_reply: screen.can_reply(),
        sending: screen.sending,
        send_failure: failure(screen.send_failure.as_ref()),
        close_offered: screen.can_close() || screen.closing,
        can_close: screen.can_close(),
        closing: screen.closing,
        confirming_close: screen.confirming_close,
        close_failure: failure(screen.close_failure.as_ref()),
        closed_message: screen
            .closed_as
            .as_deref()
            .map(SupportRequestScreen::closed_message),
        ..SupportRequestView::default()
    };
    match &screen.request {
        SupportRead::Loading => {}
        SupportRead::Failed(failure) => view.status = failed(failure),
        SupportRead::Ready(detail) => {
            view.status = LoadStatus::Ready;
            view.title = detail.subject.clone();
            view.reference = reference(detail.issue_key.as_deref());
            view.status_name = detail.status_name.clone();
            view.resolved = detail
                .status_category
                .eq_ignore_ascii_case(district_model::SUPPORT_STATUS_DONE);
            view.created_at = detail.created_at.clone();
            view.messages = detail.messages.iter().map(message).collect();
        }
    }
    view
}

fn failed(failure: &FailureText) -> LoadStatus {
    LoadStatus::failed(REQUEST_FAILED_TITLE, failure)
}

fn message(message: &SupportMessage) -> SupportMessageView {
    SupportMessageView {
        id: message.id.clone(),
        author: message.author.clone(),
        body: message.body.clone(),
        created_at: message.created_at.clone(),
        from_workspace: message.role == "customer",
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn each_kind_crosses_both_ways() {
        for kind in [
            SupportKind::Problem,
            SupportKind::Question,
            SupportKind::Suggestion,
        ] {
            assert_eq!(SupportKind::from(SupportRequestKind::from(kind)), kind);
        }
    }

    #[test]
    fn a_reference_is_the_key_or_that_there_is_none_yet() {
        assert_eq!(reference(Some("DA-42")), "DA-42");
        assert_eq!(reference(Some(" ")), NOT_FILED_YET);
        assert_eq!(reference(None), NOT_FILED_YET);
        assert_eq!(limit(usize::MAX), u32::MAX);
        assert_eq!(needs(), "A subject of 3 to 200 characters, and a message.");
    }
}
