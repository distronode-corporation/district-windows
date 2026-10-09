//! The overview of the open workspace, as the Linux overview page shows it.

use district_core::{
    FINISH_SETUP_ACTION, FINISH_SETUP_BODY, FINISH_SETUP_TITLE, OverviewScreen, SignedIn,
    WorkspacesState,
};
use serde::Serialize;

use crate::calls::{CallRowView, call_row};
use crate::views::{FactView, FailureView, LoadStatus};

/// The heading of an overview that could not be read, as the Linux app words it.
pub const OVERVIEW_FAILED_TITLE: &str = "Could not load the overview";
/// What the recent calls say when there are none, as the Linux app words it.
pub const NO_RECENT_CALLS: &str =
    "No calls yet. They appear here as your receptionist answers them.";

/// The overview.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct OverviewView {
    /// Where the read stands. With no workspace open, `Failed` carries the
    /// workspace list's own heading and words (no workspace, billing, a list
    /// that could not be read).
    pub status: LoadStatus,
    /// The open workspace's name, once the overview is read.
    pub workspace_name: String,
    /// The four figures, as the Linux app labels them.
    pub metrics: Vec<FactView>,
    /// The latest calls.
    pub recent_calls: Vec<CallRowView>,
    /// What to say when there are no recent calls.
    pub recent_calls_empty: Option<String>,
    /// "Read-only access", for a member who can change nothing here.
    pub read_only_badge: Option<String>,
    /// The card sending the owner to finish setting up on the web.
    pub finish_setup: Option<FinishSetupView>,
    /// Whether a reload is under way with the overview still showing.
    pub refreshing: bool,
    /// Why the last reload failed, shown beside the overview. Always `None`
    /// with core 2.0.0, where a failed reload replaces the overview with its
    /// failure (`status`); kept so a core that keeps the content can say so.
    pub refresh_failure: Option<FailureView>,
}

/// The card sending the owner to finish setting up on the web.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct FinishSetupView {
    /// Its heading.
    pub title: String,
    /// Its body.
    pub body: String,
    /// Its button, which sends `UiEvent::OpenFinishSetup`.
    pub action: String,
}

/// The overview for `signed_in`.
pub(crate) fn overview_view(signed_in: &SignedIn) -> OverviewView {
    let mut view = OverviewView {
        status: LoadStatus::Loading,
        workspace_name: String::new(),
        metrics: Vec::new(),
        recent_calls: Vec::new(),
        recent_calls_empty: None,
        read_only_badge: None,
        finish_setup: None,
        refreshing: false,
        refresh_failure: None,
    };
    let workspaces = match &signed_in.workspaces {
        WorkspacesState::Loading => return view,
        WorkspacesState::Ready(workspaces) => workspaces,
        other @ (WorkspacesState::NoWorkspaces
        | WorkspacesState::BillingBlocked { .. }
        | WorkspacesState::Unavailable(_)) => {
            let retryable =
                matches!(other, WorkspacesState::Unavailable(failure) if failure.retryable);
            let regions_line = match other {
                WorkspacesState::Unavailable(failure) => failure.regions_line(),
                _ => None,
            };
            view.status = LoadStatus::Failed {
                failure: FailureView {
                    message: other.message().unwrap_or_default(),
                    regions_line,
                    retryable,
                },
                title: other.title().unwrap_or_default().to_owned(),
            };
            return view;
        }
    };
    match &signed_in.overview {
        OverviewScreen::Loading => {}
        OverviewScreen::Failed(failure) => {
            view.status = LoadStatus::failed(OVERVIEW_FAILED_TITLE, failure);
        }
        OverviewScreen::Loaded(content) => {
            let overview = &content.overview;
            let figures = &overview.metrics;
            let metric = |value: String, label: &str| FactView {
                label: label.to_owned(),
                value,
            };
            view = OverviewView {
                status: LoadStatus::Ready,
                workspace_name: workspaces.active().name.clone(),
                metrics: vec![
                    metric(figures.total_calls.to_string(), "Calls"),
                    metric(figures.calls_this_week.to_string(), "Calls this week"),
                    metric(figures.total_contacts.to_string(), "Contacts"),
                    metric(overview.avg_duration_label.clone(), "Average call"),
                ],
                recent_calls: overview.recent_calls.iter().map(call_row).collect(),
                recent_calls_empty: overview
                    .recent_calls
                    .is_empty()
                    .then(|| NO_RECENT_CALLS.to_owned()),
                read_only_badge: content.read_only_badge().map(str::to_owned),
                finish_setup: content.show_finish_setup.then(|| FinishSetupView {
                    title: FINISH_SETUP_TITLE.to_owned(),
                    body: FINISH_SETUP_BODY.to_owned(),
                    action: FINISH_SETUP_ACTION.to_owned(),
                }),
                refreshing: content.refreshing,
                refresh_failure: None,
            };
        }
    }
    view
}
