//! The window's frame: which part of the session the app is in, and while
//! signed in, which tab is highlighted.

use district_core::{Route, SessionState, Tab};
use serde::Serialize;

/// What the window's frame shows, whatever the screen inside it.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct ShellView {
    /// Where the session is. The app shows the sign-in page for every phase but
    /// [`SessionPhase::SignedIn`], and the navigation view for that one.
    pub phase: SessionPhase,
    /// The tab to highlight, while signed in.
    pub tab: Option<TabView>,
    /// Whether the screen showing has a parent to go back to.
    pub can_go_back: bool,
    /// A notice over every screen (a sign-in that could not be saved, a page no
    /// browser would open), until it is dismissed.
    pub notice: Option<String>,
}

/// Where the session is. One variant per [`SessionState`] variant.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash, Serialize, uniffi::Enum)]
pub enum SessionPhase {
    /// Looking for a stored session.
    Restoring,
    /// Nobody is signed in.
    SignedOut,
    /// A sign-in is under way in the browser.
    SigningIn,
    /// Someone is signed in.
    SignedIn,
    /// A sign-out is under way.
    SigningOut,
}

/// A tab of the navigation view. One variant per core [`Tab`].
#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash, Serialize, uniffi::Enum)]
pub enum TabView {
    /// The open workspace's summary.
    Overview,
    /// Message threads.
    Inbox,
    /// The call log.
    Calls,
    /// Contacts.
    Contacts,
    /// The account.
    Account,
}

impl From<Tab> for TabView {
    fn from(tab: Tab) -> Self {
        match tab {
            Tab::Overview => Self::Overview,
            Tab::Inbox => Self::Inbox,
            Tab::Calls => Self::Calls,
            Tab::Contacts => Self::Contacts,
            Tab::Account => Self::Account,
        }
    }
}

impl From<TabView> for Tab {
    fn from(tab: TabView) -> Self {
        match tab {
            TabView::Overview => Self::Overview,
            TabView::Inbox => Self::Inbox,
            TabView::Calls => Self::Calls,
            TabView::Contacts => Self::Contacts,
            TabView::Account => Self::Account,
        }
    }
}

/// The frame for `session`.
pub fn shell_view(session: &SessionState) -> ShellView {
    let outside = |phase| ShellView {
        phase,
        tab: None,
        can_go_back: false,
        notice: None,
    };
    match session {
        SessionState::Restoring(_) => outside(SessionPhase::Restoring),
        SessionState::SignedOut(_) => outside(SessionPhase::SignedOut),
        SessionState::SigningIn(_) => outside(SessionPhase::SigningIn),
        SessionState::SigningOut(_) => outside(SessionPhase::SigningOut),
        SessionState::SignedIn(signed_in) => ShellView {
            phase: SessionPhase::SignedIn,
            tab: Some(signed_in.route.tab().into()),
            can_go_back: has_parent(&signed_in.route),
            notice: signed_in.notice.as_ref().map(|notice| notice.message()),
        },
    }
}

fn has_parent(route: &Route) -> bool {
    route.parent().is_some()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn every_tab_survives_the_trip_to_csharp_and_back() {
        for tab in [
            Tab::Overview,
            Tab::Inbox,
            Tab::Calls,
            Tab::Contacts,
            Tab::Account,
        ] {
            assert_eq!(Tab::from(TabView::from(tab)), tab);
        }
    }
}
