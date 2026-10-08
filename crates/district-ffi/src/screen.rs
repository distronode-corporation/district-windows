//! The screen inside the window's frame.
//!
//! Outside a session this is the whole of the sign-in page. While signed in it
//! is the page of the route showing, projected from the core's state for that
//! screen. Routes this version has no page for are [`ScreenView::Unavailable`].

use district_core::{Model, Route, SessionState, SignOutScope, SignedIn, SignedOutWhy};
use serde::Serialize;

use crate::account::{AccountView, DevicesView, devices_view};
use crate::calls::{CallDetailView, CallsView, call_detail_view, calls_view};
use crate::contacts::{ContactDetailView, ContactsView, contact_detail_view, contacts_view};
use crate::inbox::{InboxView, ThreadView, inbox_view, thread_view};
use crate::overview::{OverviewView, overview_view};

/// The heading of the signed-out screen on a first run.
pub const WELCOME_TITLE: &str = "Welcome to District AI";
/// Its body.
pub const WELCOME_BODY: &str = "Sign in with your District AI account. Signing in happens in \
    your browser, so this app never sees your password.";
/// The body after a sign-out that went as it should.
pub const SIGNED_OUT_BODY: &str = "Sign in again whenever you are ready.";
/// A note under a start-up check that will try again by itself.
pub const RETRYING: &str = "District AI will try again by itself.";

/// The heading of a screen this version does not have.
pub const UNAVAILABLE_TITLE: &str = "Not in this version yet";
/// Its body.
pub const UNAVAILABLE_BODY: &str = "Use the web dashboard or the District AI phone apps for this.";

/// What the screen inside the frame shows.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Enum)]
pub enum ScreenView {
    /// Everything outside a session: looking for a stored one, signed out,
    /// signing in through the browser, and signing out.
    Session {
        /// The page.
        view: SessionScreen,
    },
    /// The open workspace's overview.
    Overview {
        /// The page.
        view: OverviewView,
    },
    /// The conversations.
    Inbox {
        /// The page.
        view: InboxView,
    },
    /// One conversation.
    Thread {
        /// The page.
        view: ThreadView,
    },
    /// The call log.
    Calls {
        /// The page.
        view: CallsView,
    },
    /// One call.
    CallDetail {
        /// The page.
        view: CallDetailView,
    },
    /// The contacts.
    Contacts {
        /// The page.
        view: ContactsView,
    },
    /// One contact.
    ContactDetail {
        /// The page.
        view: ContactDetailView,
    },
    /// The account.
    Account {
        /// The page.
        view: AccountView,
    },
    /// The devices signed in to the account.
    Devices {
        /// The page.
        view: DevicesView,
    },
    /// A screen of the core this version has no page for: every other route.
    Unavailable {
        /// [`UNAVAILABLE_TITLE`].
        title: String,
        /// [`UNAVAILABLE_BODY`].
        body: String,
    },
}

impl ScreenView {
    pub(crate) fn unavailable() -> Self {
        Self::Unavailable {
            title: UNAVAILABLE_TITLE.to_owned(),
            body: UNAVAILABLE_BODY.to_owned(),
        }
    }
}

/// The sign-in page, for every session state but signed in. The same fields,
/// words and buttons as the Linux app's session page.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct SessionScreen {
    /// The heading.
    pub title: String,
    /// The text under it.
    pub body: String,
    /// Whether something is under way (show a progress ring).
    pub busy: bool,
    /// Whether "Sign in with your browser" is offered.
    pub sign_in: bool,
    /// Whether "Try again" (the start-up check) is offered.
    pub retry: bool,
    /// Whether "Sign out again" is offered.
    pub retry_sign_out: bool,
    /// Whether "Cancel" (the sign-in) is offered.
    pub cancel: bool,
    /// Why the last sign-in failed, if it did.
    pub error: Option<String>,
}

/// The page of the route showing, for a signed-in `model`.
fn signed_in_view(model: &Model, signed_in: &SignedIn) -> ScreenView {
    let capabilities = signed_in.capabilities();
    match &signed_in.route {
        Route::Overview => ScreenView::Overview {
            view: overview_view(signed_in),
        },
        Route::Inbox => ScreenView::Inbox {
            view: inbox_view(&signed_in.inbox),
        },
        Route::Thread { thread_key } => ScreenView::Thread {
            view: thread_view(thread_key, signed_in.thread.as_ref(), &capabilities),
        },
        Route::Calls => ScreenView::Calls {
            view: calls_view(&signed_in.calls),
        },
        Route::CallDetail { call_id } => ScreenView::CallDetail {
            view: call_detail_view(
                signed_in
                    .call
                    .as_ref()
                    .filter(|screen| screen.call_id == *call_id)
                    .unwrap_or(&loading_call(call_id)),
                &capabilities,
            ),
        },
        Route::Contacts => ScreenView::Contacts {
            view: contacts_view(&signed_in.contacts),
        },
        Route::ContactDetail { contact_id } => ScreenView::ContactDetail {
            view: contact_detail_view(
                signed_in
                    .contact
                    .as_ref()
                    .filter(|screen| screen.contact_id == *contact_id)
                    .unwrap_or(&loading_contact(contact_id)),
                &capabilities,
            ),
        },
        Route::Account => model
            .account()
            .map_or_else(ScreenView::unavailable, |account| ScreenView::Account {
                view: account.into(),
            }),
        Route::Devices => ScreenView::Devices {
            view: devices_view(&signed_in.devices),
        },
        // Calls are placed in a later version.
        Route::Dialer
        | Route::BlockedContacts
        | Route::Hq
        | Route::Analytics
        | Route::Marketplace
        | Route::Billing
        | Route::Workflows
        | Route::Scheduling
        | Route::Desk
        | Route::DeskTicket { .. }
        | Route::DeskSettings
        | Route::Support
        | Route::SupportRequest { .. }
        | Route::Rooms
        | Route::Workspace(_) => ScreenView::unavailable(),
    }
}

/// A call not read yet: what the page shows before the core opens it.
fn loading_call(call_id: &str) -> district_core::CallDetailScreen {
    district_core::CallDetailScreen {
        call_id: call_id.to_owned(),
        call: district_core::CallView::Loading,
        transcript: district_core::TranscriptView::Loading,
        refresh_failure: None,
    }
}

/// A contact not read yet.
fn loading_contact(contact_id: &str) -> district_core::ContactDetailScreen {
    district_core::ContactDetailScreen {
        contact_id: contact_id.to_owned(),
        contact: district_core::ContactView::Loading,
        saving: None,
        failure: None,
        confirming: None,
        editing: None,
        blocked: None,
    }
}

/// The screen for `model`.
pub fn screen_view(model: &Model) -> ScreenView {
    session_view(model.session(), |signed_in| {
        signed_in_view(model, signed_in)
    })
}

/// The screen for `session`, with `signed_in` making the page of a signed-in
/// one.
pub(crate) fn session_view(
    session: &SessionState,
    signed_in: impl FnOnce(&SignedIn) -> ScreenView,
) -> ScreenView {
    let nothing = SessionScreen {
        title: "District AI".to_owned(),
        body: String::new(),
        busy: false,
        sign_in: false,
        retry: false,
        retry_sign_out: false,
        cancel: false,
        error: None,
    };
    let view = match session {
        SessionState::SignedIn(state) => return signed_in(state),
        SessionState::Restoring(restoring) => {
            let mut body = restoring.message();
            if restoring.retry_in.is_some() && !restoring.checking {
                body = format!("{body} {RETRYING}");
            }
            SessionScreen {
                body,
                busy: restoring.checking,
                retry: !restoring.checking,
                ..nothing
            }
        }
        SessionState::SignedOut(signed_out) => {
            let (title, body) = match &signed_out.why {
                SignedOutWhy::NeverSignedIn => (WELCOME_TITLE, WELCOME_BODY.to_owned()),
                SignedOutWhy::SignedOut(outcome) => {
                    let details = outcome.details();
                    let body = if details.is_empty() {
                        SIGNED_OUT_BODY.to_owned()
                    } else {
                        details.join("\n\n")
                    };
                    (outcome.headline(), body)
                }
                SignedOutWhy::SessionEnded(end) => (end.title(), end.message().to_owned()),
            };
            SessionScreen {
                title: title.to_owned(),
                body,
                sign_in: true,
                retry_sign_out: signed_out.can_retry_sign_out(),
                error: signed_out
                    .sign_in_error
                    .as_ref()
                    .map(|error| error.message()),
                ..nothing
            }
        }
        SessionState::SigningIn(signing_in) => SessionScreen {
            title: "Signing in".to_owned(),
            body: signing_in.phase.message().to_owned(),
            busy: true,
            cancel: signing_in.can_cancel(),
            ..nothing
        },
        SessionState::SigningOut(signing_out) => SessionScreen {
            title: "Signing out".to_owned(),
            body: match signing_out.scope {
                SignOutScope::ThisDevice => {
                    "Telling District AI, then removing your sign-in from this computer."
                }
                SignOutScope::Everywhere => {
                    "Every device is signed out. Removing your sign-in from this computer."
                }
            }
            .to_owned(),
            busy: true,
            ..nothing
        },
    };
    ScreenView::Session { view }
}

#[cfg(test)]
mod tests {
    use district_auth::StoreErrorKind;
    use district_core::{ServiceSignOut, SignOutOutcome, SignedOut, SigningOut};

    use super::*;

    fn session_screen(view: ScreenView) -> Option<SessionScreen> {
        match view {
            ScreenView::Session { view } => Some(view),
            _ => None,
        }
    }

    #[test]
    fn a_screen_with_no_page_says_so() {
        let view = ScreenView::unavailable();
        assert_eq!(session_screen(view.clone()), None);
        assert_eq!(
            view,
            ScreenView::Unavailable {
                title: UNAVAILABLE_TITLE.to_owned(),
                body: UNAVAILABLE_BODY.to_owned(),
            }
        );
    }

    fn signed_out(outcome: SignOutOutcome) -> SessionScreen {
        let session = SessionState::SignedOut(SignedOut {
            why: SignedOutWhy::SignedOut(outcome),
            sign_in_error: None,
        });
        session_screen(session_view(&session, |_| unreachable!()))
            .expect("signed out is a session screen")
    }

    #[test]
    fn a_clean_sign_out_says_so_and_a_messy_one_says_why() {
        let clean = signed_out(SignOutOutcome {
            scope: SignOutScope::ThisDevice,
            service: ServiceSignOut::Done,
            removed: Ok(()),
        });
        assert_eq!(clean.body, SIGNED_OUT_BODY);
        assert!(clean.sign_in);
        assert!(!clean.retry_sign_out);

        let messy = signed_out(SignOutOutcome {
            scope: SignOutScope::ThisDevice,
            service: ServiceSignOut::Stranded(StoreErrorKind::Locked),
            removed: Err(StoreErrorKind::Locked),
        });
        assert_ne!(messy.body, SIGNED_OUT_BODY);
        assert!(messy.retry_sign_out);
    }

    #[test]
    fn signing_out_everywhere_has_its_own_words() {
        let session = SessionState::SigningOut(SigningOut {
            scope: SignOutScope::Everywhere,
        });
        let view = session_screen(session_view(&session, |_| unreachable!()))
            .expect("signing out is a session screen");
        assert!(view.body.starts_with("Every device is signed out."));
        assert!(view.busy);
    }
}
