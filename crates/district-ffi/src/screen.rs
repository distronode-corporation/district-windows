//! The screen inside the window's frame.
//!
//! Outside a session this is the whole of the sign-in page. While signed in it
//! names the route showing; each route's own projection arrives with the wave
//! that builds its page (overview, account and devices first).

use district_core::{Route, SessionState, SignOutScope, SignedOutWhy, WorkspaceSection};
use serde::Serialize;

/// The heading of the signed-out screen on a first run.
pub const WELCOME_TITLE: &str = "Welcome to District AI";
/// Its body.
pub const WELCOME_BODY: &str = "Sign in with your District AI account. Signing in happens in \
    your browser, so this app never sees your password.";
/// The body after a sign-out that went as it should.
pub const SIGNED_OUT_BODY: &str = "Sign in again whenever you are ready.";
/// A note under a start-up check that will try again by itself.
pub const RETRYING: &str = "District AI will try again by itself.";

/// What the screen inside the frame shows.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Enum)]
pub enum ScreenView {
    /// Everything outside a session: looking for a stored one, signed out,
    /// signing in through the browser, and signing out.
    Session {
        /// The page.
        view: SessionScreen,
    },
    /// A screen of the signed-in app.
    Route {
        /// Which one.
        route: RouteView,
    },
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

/// A screen of the signed-in app. One variant per core [`Route`], with each
/// workspace settings section its own.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash, Serialize, uniffi::Enum)]
#[allow(
    missing_docs,
    reason = "each variant is the core Route of the same name"
)]
pub enum RouteView {
    Overview,
    Inbox,
    Thread,
    Calls,
    CallDetail,
    Contacts,
    ContactDetail,
    BlockedContacts,
    Hq,
    Analytics,
    Marketplace,
    Billing,
    Workflows,
    Scheduling,
    Desk,
    DeskTicket,
    DeskSettings,
    Support,
    SupportRequest,
    Rooms,
    Dialer,
    Account,
    Devices,
    WorkspaceHub,
    Persona,
    VoiceStudio,
    Tools,
    Directory,
    Routing,
    CallHandling,
    Knowledge,
    Messaging,
    Members,
    Numbers,
}

impl From<&Route> for RouteView {
    fn from(route: &Route) -> Self {
        match route {
            Route::Overview => Self::Overview,
            Route::Inbox => Self::Inbox,
            Route::Thread { .. } => Self::Thread,
            Route::Calls => Self::Calls,
            Route::CallDetail { .. } => Self::CallDetail,
            Route::Contacts => Self::Contacts,
            Route::ContactDetail { .. } => Self::ContactDetail,
            Route::BlockedContacts => Self::BlockedContacts,
            Route::Hq => Self::Hq,
            Route::Analytics => Self::Analytics,
            Route::Marketplace => Self::Marketplace,
            Route::Billing => Self::Billing,
            Route::Workflows => Self::Workflows,
            Route::Scheduling => Self::Scheduling,
            Route::Desk => Self::Desk,
            Route::DeskTicket { .. } => Self::DeskTicket,
            Route::DeskSettings => Self::DeskSettings,
            Route::Support => Self::Support,
            Route::SupportRequest { .. } => Self::SupportRequest,
            Route::Rooms => Self::Rooms,
            Route::Dialer => Self::Dialer,
            Route::Account => Self::Account,
            Route::Devices => Self::Devices,
            Route::Workspace(section) => (*section).into(),
        }
    }
}

impl From<WorkspaceSection> for RouteView {
    fn from(section: WorkspaceSection) -> Self {
        match section {
            WorkspaceSection::Hub => Self::WorkspaceHub,
            WorkspaceSection::Persona => Self::Persona,
            WorkspaceSection::VoiceStudio => Self::VoiceStudio,
            WorkspaceSection::Tools => Self::Tools,
            WorkspaceSection::Directory => Self::Directory,
            WorkspaceSection::Routing => Self::Routing,
            WorkspaceSection::CallHandling => Self::CallHandling,
            WorkspaceSection::Knowledge => Self::Knowledge,
            WorkspaceSection::Messaging => Self::Messaging,
            WorkspaceSection::Members => Self::Members,
            WorkspaceSection::Numbers => Self::Numbers,
        }
    }
}

/// The screen for `session`.
pub fn screen_view(session: &SessionState) -> ScreenView {
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
        SessionState::SignedIn(signed_in) => {
            return ScreenView::Route {
                route: (&signed_in.route).into(),
            };
        }
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
    use std::collections::HashSet;

    use district_auth::StoreErrorKind;
    use district_core::{ServiceSignOut, SignOutOutcome, SignedOut, SigningOut};

    use super::*;

    /// Every route the core has, each its own view.
    #[test]
    fn every_route_has_a_view_of_its_own() {
        let id = || "id-1".to_owned();
        let mut routes = vec![
            Route::Overview,
            Route::Inbox,
            Route::Thread { thread_key: id() },
            Route::Calls,
            Route::CallDetail { call_id: id() },
            Route::Contacts,
            Route::ContactDetail { contact_id: id() },
            Route::BlockedContacts,
            Route::Hq,
            Route::Analytics,
            Route::Marketplace,
            Route::Billing,
            Route::Workflows,
            Route::Scheduling,
            Route::Desk,
            Route::DeskTicket { ticket_id: id() },
            Route::DeskSettings,
            Route::Support,
            Route::SupportRequest { key: id() },
            Route::Rooms,
            Route::Dialer,
            Route::Account,
            Route::Devices,
        ];
        routes.extend(WorkspaceSection::ALL.map(Route::Workspace));
        let views: HashSet<RouteView> = routes.iter().map(RouteView::from).collect();
        assert_eq!(views.len(), routes.len());
    }

    fn session_screen(view: ScreenView) -> Option<SessionScreen> {
        if let ScreenView::Session { view } = view {
            Some(view)
        } else {
            None
        }
    }

    fn signed_out(outcome: SignOutOutcome) -> SessionScreen {
        let session = SessionState::SignedOut(SignedOut {
            why: SignedOutWhy::SignedOut(outcome),
            sign_in_error: None,
        });
        session_screen(screen_view(&session)).expect("signed out is a session screen")
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
        let view = session_screen(screen_view(&session)).expect("signing out is a session screen");
        assert!(view.body.starts_with("Every device is signed out."));
        assert!(view.busy);
    }
}
