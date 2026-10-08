//! The window's frame: which part of the session the app is in, and while
//! signed in, which tab is highlighted, the workspace switcher, the inbox's
//! unread count, the live updates' state and a report under way.

use district_core::{
    LiveStatus, Model, Route, SessionState, SignedIn, Tab, WorkspaceRole, WorkspacesState,
};
use district_model::WorkspaceEntry;
use serde::Serialize;

use crate::report::{ReportStatus, report_status};
use crate::views::humanize;

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
    /// The workspace switcher.
    pub workspaces: WorkspaceSwitcherView,
    /// How many messages in the open workspace nobody has read, for the inbox
    /// badge. 0 when the count is not known, and outside a session.
    pub unread: u32,
    /// What the live updates have to say, when anything.
    pub live: Option<LiveBannerView>,
    /// The report this session started, while it is under way or until its
    /// outcome is dismissed (`UiEvent::DismissReport`).
    pub report: Option<ReportStatus>,
}

/// The workspaces the member can open, and the open one.
#[derive(Clone, Debug, Default, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct WorkspaceSwitcherView {
    /// The workspaces, in the service's order. Empty while none is open.
    pub entries: Vec<WorkspaceEntryView>,
    /// The open one.
    pub active_id: Option<String>,
    /// Whether to offer the switcher (more than one workspace).
    pub can_switch: bool,
    /// A warning that the list may be short because a region did not answer.
    pub partial_warning: Option<String>,
    /// With no workspace open, the heading saying why.
    pub title: Option<String>,
    /// With no workspace open, the text saying why.
    pub message: Option<String>,
}

/// One workspace in the switcher.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct WorkspaceEntryView {
    /// The workspace, to select it by.
    pub id: String,
    /// Its name.
    pub name: String,
    /// The member's role in it ("Agency", "Client", "Viewer").
    pub role_label: String,
}

/// A line over every screen about the live updates.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct LiveBannerView {
    /// What to say.
    pub message: String,
    /// Whether to offer "Refresh" (the updates stopped, so new calls and
    /// messages appear only when the screen is read again).
    pub can_refresh: bool,
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

/// The frame for `model`. `reporting` says whether this session started a
/// report that has not been dismissed.
pub fn shell_view(model: &Model, reporting: bool) -> ShellView {
    shell_for(model.session(), reporting)
}

/// The frame for `session`.
pub(crate) fn shell_for(session: &SessionState, reporting: bool) -> ShellView {
    let outside = |phase| ShellView {
        phase,
        tab: None,
        can_go_back: false,
        notice: None,
        workspaces: WorkspaceSwitcherView::default(),
        unread: 0,
        live: None,
        report: None,
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
            workspaces: switcher(&signed_in.workspaces),
            unread: signed_in
                .unread
                .map_or(0, |count| u32::try_from(count.max(0)).unwrap_or(u32::MAX)),
            live: live_banner(signed_in),
            report: report_status(signed_in, reporting),
        },
    }
}

fn role_label(raw: &str) -> String {
    match WorkspaceRole::from_wire(raw) {
        Some(WorkspaceRole::Agency) => "Agency".to_owned(),
        Some(WorkspaceRole::Client) => "Client".to_owned(),
        Some(WorkspaceRole::Viewer) => "Viewer".to_owned(),
        None => humanize(raw),
    }
}

fn entry(workspace: &WorkspaceEntry) -> WorkspaceEntryView {
    WorkspaceEntryView {
        id: workspace.id.clone(),
        name: workspace.name.clone(),
        role_label: role_label(&workspace.role),
    }
}

fn switcher(state: &WorkspacesState) -> WorkspaceSwitcherView {
    match state {
        WorkspacesState::Ready(workspaces) => WorkspaceSwitcherView {
            entries: workspaces.list.iter().map(entry).collect(),
            active_id: Some(workspaces.active().id.clone()),
            can_switch: workspaces.can_switch(),
            partial_warning: workspaces.partial_warning(),
            title: None,
            message: None,
        },
        WorkspacesState::Loading
        | WorkspacesState::NoWorkspaces
        | WorkspacesState::BillingBlocked { .. }
        | WorkspacesState::Unavailable(_) => WorkspaceSwitcherView {
            title: state.title().map(str::to_owned),
            message: state.message(),
            ..WorkspaceSwitcherView::default()
        },
    }
}

fn live_banner(signed_in: &SignedIn) -> Option<LiveBannerView> {
    let status = &signed_in.live.status;
    status.message().map(|message| LiveBannerView {
        message,
        can_refresh: matches!(status, LiveStatus::Stopped(_)),
    })
}

fn has_parent(route: &Route) -> bool {
    route.parent().is_some()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn a_role_is_named_and_an_unknown_one_is_humanized() {
        assert_eq!(role_label("agency"), "Agency");
        assert_eq!(role_label(" Client "), "Client");
        assert_eq!(role_label("viewer"), "Viewer");
        assert_eq!(role_label("billing_admin"), "Billing admin");
    }

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
