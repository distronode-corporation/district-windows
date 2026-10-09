//! Workflows, their runs and the outbound campaign's switch, as the Linux
//! workflows page shows them.
//!
//! Every member reads all of it. Turning a workflow on or off, and pausing or
//! resuming the campaign, need a role that may change the workspace
//! (`Capabilities::can_change`); for any other role the switches are shown and
//! do not work, the campaign's buttons are not offered, and the card says why.
//! A switch is shown at once and put back by the core if the service refuses.
//! Pausing or resuming the campaign asks first ([`CampaignConfirmView`]) and
//! shows only the state the service answers with.

use district_core::{
    CampaignCard, CampaignConfirm, Event, Model, Route, RunHistory, SignedIn, Tone,
    WorkflowControls, WorkflowList, WorkflowsEvent, WorkflowsScreen, trigger_label,
};
use district_model::{CampaignStatus, WorkflowRun, WorkflowSummary};
use serde::Serialize;

use crate::screen::ScreenView;
use crate::views::{EmptyView, FailureView, LoadStatus, failure, humanize};

/// Whether this version has the area's screens. The packet that builds them
/// sets it; until then [`crate::nav::built`] says no for its routes.
pub(crate) const BUILT: bool = true;

/// The page's heading.
pub const WORKFLOWS_TITLE: &str = "Workflows";
/// The campaign's batch size row, as the Linux app labels it.
pub const BATCH_LABEL: &str = "Batch size";
/// The campaign's goal row, as the Linux app labels it.
pub const GOAL_LABEL: &str = "Goal";
/// The line under a workflow that has never run, after its trigger.
pub const NOT_RUN_YET: &str = "Not run yet";
/// The note while a page of runs is on its way, as the Linux app words it.
pub const READING_RUNS: &str = "Reading runs.";
/// The button that reads the next page of runs.
pub const MORE_RUNS: &str = "More runs";
/// Between the parts of a line.
const DOT: &str = " \u{b7} ";

/// The workflows screen.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct WorkflowsView {
    /// The heading, "Workflows".
    pub title: String,
    /// The outbound campaign's card.
    pub campaign: CampaignView,
    /// Where the read of the workflows stands. `Failed` carries "Could not
    /// load workflows", with "Try again" when the core says it may work.
    pub status: LoadStatus,
    /// What to say when the read is ready and there are no workflows.
    pub empty: Option<EmptyView>,
    /// The workflows, newest first.
    pub rows: Vec<WorkflowRowView>,
    /// Why the last change of a workflow failed, shown above the list with
    /// "Dismiss" (`WorkflowsAction::DismissToggleFailure`).
    pub toggle_failure: Option<FailureView>,
    /// The question before pausing or resuming the campaign, while it is asked.
    pub confirming: Option<CampaignConfirmView>,
}

/// The outbound campaign's card.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct CampaignView {
    /// The heading, "Outbound campaign".
    pub title: String,
    /// Where the read stands. `Failed` carries "Could not load the campaign".
    pub status: LoadStatus,
    /// Whether the campaign is calling, once read.
    pub active: bool,
    /// "Active" or "Paused", once read; `None` before.
    pub badge: Option<String>,
    /// The batch size row's label, "Batch size".
    pub batch_label: String,
    /// The batch size, or "No batch size set yet.".
    pub batch: String,
    /// The goal row's label, "Goal".
    pub goal_label: String,
    /// The goal, or "No goal set yet.".
    pub goal: String,
    /// Whether to show "Pause campaign": the campaign is calling and the role
    /// may change it.
    pub show_pause: bool,
    /// Whether "Pause campaign" works now (not while a change is on its way).
    pub can_pause: bool,
    /// Whether to show "Resume campaign": the campaign is not calling and the
    /// role may change it.
    pub show_resume: bool,
    /// Whether "Resume campaign" works now.
    pub can_resume: bool,
    /// "Pause campaign".
    pub pause_label: String,
    /// "Resume campaign".
    pub resume_label: String,
    /// Whether a pause or resume is on its way (show a progress ring).
    pub pending: bool,
    /// Why the last pause or resume failed, beside the last state the service
    /// sent.
    pub failure: Option<FailureView>,
    /// What is still changed on the web or, for a role that may not change the
    /// campaign, why the buttons are not there.
    pub note: String,
    /// Whether the member may change the campaign and the workflows; `false`
    /// is the read-only view.
    pub can_change: bool,
}

/// The question before pausing or resuming the campaign, in the core's words:
/// the devices' `ConfirmView`, with the heading and the body apart.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct CampaignConfirmView {
    /// The question's heading: "Pause the outbound campaign?".
    pub title: String,
    /// What it would do.
    pub body: String,
    /// The confirming button's label: "Pause campaign" or "Resume campaign".
    pub action: String,
    /// Whether answering yes resumes (`true`) or pauses.
    pub enable: bool,
}

impl From<CampaignConfirm> for CampaignConfirmView {
    fn from(confirm: CampaignConfirm) -> Self {
        Self {
            title: confirm.title().to_owned(),
            body: confirm.body().to_owned(),
            action: confirm.action().to_owned(),
            enable: confirm.enable,
        }
    }
}

/// One workflow.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct WorkflowRowView {
    /// The workflow, to act on it by.
    pub workflow_id: String,
    /// Its name.
    pub name: String,
    /// Whether it is on, or the value asked for while a change is on its way.
    pub active: bool,
    /// What starts it and how its last run went, joined by a middle dot:
    /// "After a missed call" and "Last run success", or "Not run yet". The
    /// app adds the time of [`WorkflowRowView::last_run_at`] in local time
    /// when there is one.
    pub detail: String,
    /// When its last run started, ISO 8601, or `None` when it never ran.
    pub last_run_at: Option<String>,
    /// Whether it is being turned on or off now.
    pub switching: bool,
    /// Whether its switch works: the role may change workflows and no change
    /// of it is on its way.
    pub can_switch: bool,
    /// The switch's accessible name, "Turn on " and the workflow's name.
    pub switch_name: String,
    /// Whether its runs are open.
    pub expanded: bool,
    /// Its runs, while they are open.
    pub runs: Option<RunsView>,
}

/// A workflow's runs, as far as they have been read.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct RunsView {
    /// The runs read, newest first.
    pub runs: Vec<RunView>,
    /// Whether a page is on its way (show a progress ring).
    pub loading: bool,
    /// "Reading runs." while a page is on its way, or "This workflow has not
    /// run yet." when it has none.
    pub note: Option<String>,
    /// Why the last page failed, beside the runs already read.
    pub failure: Option<FailureView>,
    /// Whether to offer "More runs" (`WorkflowsAction::LoadMoreRuns`).
    pub can_load_more: bool,
    /// "More runs".
    pub more_label: String,
    /// Whether to offer "Try again" for a first page that failed, which only
    /// reading the whole screen again does (`UiEvent::Refresh`).
    pub can_retry: bool,
}

/// One run of a workflow.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct RunView {
    /// The run's id.
    pub run_id: String,
    /// How it went, as a word: "Success", "Partial", "Failed", "Skipped".
    pub status: String,
    /// How the status reads.
    pub tone: RunTone,
    /// When it started, ISO 8601, for the app to say in local time.
    pub started_at: String,
    /// What each action did, and why the run failed ("Send sms: ok", "Send
    /// email: skipped (contact has no email address)", joined by middle dots).
    pub detail: String,
}

/// How a run's status reads, the core's `Tone`.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash, Serialize, uniffi::Enum)]
pub enum RunTone {
    /// It worked.
    Success,
    /// Some of it worked.
    Warning,
    /// It failed.
    Danger,
    /// Skipped, or a word this build does not know.
    Neutral,
}

impl From<Tone> for RunTone {
    fn from(tone: Tone) -> Self {
        match tone {
            Tone::Success => Self::Success,
            Tone::Warning => Self::Warning,
            Tone::Danger => Self::Danger,
            Tone::Neutral => Self::Neutral,
        }
    }
}

/// Something the member did on the workflows screen. Reading it all again is
/// `UiEvent::Refresh`.
#[derive(Clone, Debug, PartialEq, Eq, uniffi::Enum)]
pub enum WorkflowsAction {
    /// Open the workflows.
    Open,
    /// Open this workflow's runs, or close them when they are open.
    ToggleExpanded {
        /// The workflow.
        workflow_id: String,
    },
    /// Read the next page of this workflow's runs.
    LoadMoreRuns {
        /// The workflow.
        workflow_id: String,
    },
    /// Turn this workflow on or off.
    SetActive {
        /// The workflow.
        workflow_id: String,
        /// On (`true`) or off.
        active: bool,
    },
    /// Put away why turning a workflow on or off failed.
    DismissToggleFailure,
    /// Ask before pausing (`false`) or resuming (`true`) the campaign.
    AskCampaign {
        /// The state wanted.
        enable: bool,
    },
    /// Answer the campaign's question yes.
    ConfirmCampaign,
    /// Answer it no.
    CancelCampaign,
}

/// The core events `action` is, in the order the core is to hear them.
pub(crate) fn events(action: WorkflowsAction) -> Vec<Event> {
    vec![match action {
        WorkflowsAction::Open => Event::Navigate(Route::Workflows),
        WorkflowsAction::ToggleExpanded { workflow_id } => {
            Event::Workflows(WorkflowsEvent::ToggleExpanded { workflow_id })
        }
        WorkflowsAction::LoadMoreRuns { workflow_id } => {
            Event::Workflows(WorkflowsEvent::LoadMoreRuns { workflow_id })
        }
        WorkflowsAction::SetActive {
            workflow_id,
            active,
        } => Event::Workflows(WorkflowsEvent::SetActive {
            workflow_id,
            active,
        }),
        WorkflowsAction::DismissToggleFailure => {
            Event::Workflows(WorkflowsEvent::DismissToggleFailure)
        }
        WorkflowsAction::AskCampaign { enable } => {
            Event::Workflows(WorkflowsEvent::AskCampaign { enable })
        }
        WorkflowsAction::ConfirmCampaign => Event::Workflows(WorkflowsEvent::ConfirmCampaign),
        WorkflowsAction::CancelCampaign => Event::Workflows(WorkflowsEvent::CancelCampaign),
    }]
}

/// The page of the workflows, for a signed-in model.
pub(crate) fn screen(_model: &Model, signed_in: &SignedIn) -> ScreenView {
    ScreenView::Workflows {
        view: workflows_view(
            &signed_in.workflows,
            signed_in.capabilities().can_change,
            signed_in.workflow_controls(),
        ),
    }
}

/// The workflows screen `screen`, for a member who may change it when
/// `can_change`, with the controls the core offers.
fn workflows_view(
    screen: &WorkflowsScreen,
    can_change: bool,
    controls: WorkflowControls,
) -> WorkflowsView {
    let (status, empty, rows) = match &screen.list {
        WorkflowList::NotLoaded | WorkflowList::Loading => (LoadStatus::Loading, None, Vec::new()),
        WorkflowList::Failed(failure) => (
            LoadStatus::failed(WorkflowList::FAILED_TITLE, failure),
            None,
            Vec::new(),
        ),
        WorkflowList::Ready(workflows) => (
            LoadStatus::Ready,
            workflows
                .is_empty()
                .then(|| EmptyView::new(WorkflowList::EMPTY_TITLE, WorkflowList::EMPTY_BODY)),
            workflows
                .iter()
                .map(|workflow| workflow_row(screen, workflow, controls.can_toggle))
                .collect(),
        ),
    };
    WorkflowsView {
        title: WORKFLOWS_TITLE.to_owned(),
        campaign: campaign_view(screen, can_change, controls),
        status,
        empty,
        rows,
        toggle_failure: failure(screen.toggle_failure.as_ref()),
        confirming: screen.campaign_confirm.map(CampaignConfirmView::from),
    }
}

fn campaign_view(
    screen: &WorkflowsScreen,
    can_change: bool,
    controls: WorkflowControls,
) -> CampaignView {
    let mut view = CampaignView {
        title: CampaignCard::TITLE.to_owned(),
        status: LoadStatus::Loading,
        active: false,
        badge: None,
        batch_label: BATCH_LABEL.to_owned(),
        batch: String::new(),
        goal_label: GOAL_LABEL.to_owned(),
        goal: String::new(),
        show_pause: false,
        can_pause: controls.can_pause,
        show_resume: false,
        can_resume: controls.can_resume,
        pause_label: CampaignConfirm { enable: false }.action().to_owned(),
        resume_label: CampaignConfirm { enable: true }.action().to_owned(),
        pending: screen.campaign_pending,
        failure: failure(screen.campaign_failure.as_ref()),
        note: if can_change {
            CampaignCard::WEB_ONLY
        } else {
            CampaignCard::VIEWER
        }
        .to_owned(),
        can_change,
    };
    match &screen.campaign {
        CampaignCard::NotLoaded | CampaignCard::Loading => {}
        CampaignCard::Failed(failure) => {
            view.status = LoadStatus::failed(CampaignCard::FAILED_TITLE, failure);
        }
        CampaignCard::Ready(status) => ready_campaign(&mut view, status, can_change),
    }
    view
}

fn ready_campaign(view: &mut CampaignView, status: &CampaignStatus, can_change: bool) {
    let active = status.infinite_sdr_enabled;
    view.status = LoadStatus::Ready;
    view.active = active;
    view.badge = Some(
        if active {
            CampaignCard::ACTIVE
        } else {
            CampaignCard::PAUSED
        }
        .to_owned(),
    );
    view.batch = status.sdr_batch_size.map_or_else(
        || CampaignCard::NO_BATCH.to_owned(),
        |size| size.to_string(),
    );
    view.goal = status
        .sdr_campaign_goal
        .as_deref()
        .filter(|goal| !goal.trim().is_empty())
        .unwrap_or(CampaignCard::NO_GOAL)
        .to_owned();
    view.show_pause = active && can_change;
    view.show_resume = !active && can_change;
}

/// A trigger as it reads: the core's words, or the service's own for one this
/// build does not know.
fn trigger_words(trigger: &str) -> String {
    trigger_label(trigger).map_or_else(|| humanize(trigger), str::to_owned)
}

fn workflow_row(
    screen: &WorkflowsScreen,
    workflow: &WorkflowSummary,
    can_toggle: bool,
) -> WorkflowRowView {
    let switching = screen.is_toggling(&workflow.id);
    let expanded = screen.expanded.as_deref() == Some(workflow.id.as_str());
    let trigger = trigger_words(&workflow.trigger);
    let detail = match &workflow.latest_run {
        Some(run) => format!(
            "{trigger}{DOT}Last run {}",
            humanize(&run.status).to_lowercase()
        ),
        None => format!("{trigger}{DOT}{NOT_RUN_YET}"),
    };
    WorkflowRowView {
        workflow_id: workflow.id.clone(),
        name: workflow.name.clone(),
        active: workflow.active,
        detail,
        last_run_at: workflow
            .latest_run
            .as_ref()
            .map(|run| run.started_at.clone()),
        switching,
        can_switch: can_toggle && !switching,
        switch_name: format!("Turn on {}", workflow.name),
        expanded,
        runs: expanded.then(|| runs_view(screen.runs.get(&workflow.id))),
    }
}

fn runs_view(history: Option<&RunHistory>) -> RunsView {
    let history = history.cloned().unwrap_or_default();
    let note = if history.loading {
        Some(READING_RUNS)
    } else if history.failure.is_none() && history.runs.is_empty() {
        Some(RunHistory::EMPTY)
    } else {
        None
    };
    RunsView {
        runs: history.runs.iter().map(run_view).collect(),
        loading: history.loading,
        note: note.map(str::to_owned),
        failure: failure(history.failure.as_ref()),
        can_load_more: history.can_load_more(),
        more_label: MORE_RUNS.to_owned(),
        // A later page that failed is read again from where it stopped; a
        // first page that failed, only with the whole screen.
        can_retry: !history.loading && history.failure.is_some() && !history.can_load_more(),
    }
}

fn run_view(run: &WorkflowRun) -> RunView {
    RunView {
        run_id: run.id.clone(),
        status: humanize(&run.status),
        tone: Tone::of_run(&run.status).into(),
        started_at: run.started_at.clone(),
        detail: run_line(run),
    }
}

/// What a run's actions did, one after another, and why it failed.
fn run_line(run: &WorkflowRun) -> String {
    let mut parts: Vec<String> = run
        .action_results
        .iter()
        .map(|action| {
            let reason = action
                .reason
                .as_deref()
                .map(|reason| format!(" ({})", humanize(reason).to_lowercase()))
                .unwrap_or_default();
            format!(
                "{}: {}{reason}",
                humanize(&action.action_type),
                humanize(&action.outcome).to_lowercase()
            )
        })
        .collect();
    parts.extend(run.error.clone());
    parts.join(DOT)
}

/// A view with nothing read yet, for the shared tests that carry one across
/// the boundary.
#[cfg(test)]
pub(crate) fn sample_view() -> WorkflowsView {
    workflows_view(
        &WorkflowsScreen::default(),
        true,
        WorkflowControls::default(),
    )
}

#[cfg(test)]
mod tests {
    use district_model::{WorkflowActionResult, WorkflowLatestRun};

    use super::*;

    fn run(status: &str) -> WorkflowRun {
        WorkflowRun {
            id: format!("run-{status}"),
            workflow_id: "wf-1".to_owned(),
            trigger: "call_ended".to_owned(),
            status: status.to_owned(),
            started_at: "2026-08-18T11:30:00.000Z".to_owned(),
            finished_at: None,
            action_results: vec![
                WorkflowActionResult {
                    action_type: "send_sms".to_owned(),
                    outcome: "ok".to_owned(),
                    reason: None,
                },
                WorkflowActionResult {
                    action_type: "send_email".to_owned(),
                    outcome: "skipped".to_owned(),
                    reason: Some("Contact has no email".to_owned()),
                },
            ],
            error: Some("The carrier refused it.".to_owned()),
        }
    }

    #[test]
    fn a_run_reads_as_words() {
        let view = run_view(&run("partial"));
        assert_eq!(view.status, "Partial");
        assert_eq!(view.tone, RunTone::Warning);
        assert_eq!(
            view.detail,
            "Send sms: ok \u{b7} Send email: skipped (contact has no email) \u{b7} The carrier refused it."
        );
        assert_eq!(run_view(&run("success")).tone, RunTone::Success);
        assert_eq!(run_view(&run("failed")).tone, RunTone::Danger);
        assert_eq!(run_view(&run("skipped")).tone, RunTone::Neutral);
    }

    #[test]
    fn a_trigger_this_build_does_not_know_reads_as_the_service_spells_it() {
        assert_eq!(trigger_words("call_ended"), "After any call ends");
        assert_eq!(trigger_words("voicemail_left"), "Voicemail left");
    }

    #[test]
    fn a_row_says_its_trigger_and_last_run() {
        let mut workflow = WorkflowSummary {
            id: "wf-1".to_owned(),
            name: "Follow up".to_owned(),
            active: true,
            trigger: "call_ended_unanswered".to_owned(),
            created_at: "2026-08-18T10:00:00.000Z".to_owned(),
            latest_run: Some(WorkflowLatestRun {
                status: "failed".to_owned(),
                started_at: "2026-08-18T11:30:00.000Z".to_owned(),
            }),
        };
        let screen = WorkflowsScreen::default();
        let row = workflow_row(&screen, &workflow, false);
        assert_eq!(row.detail, "After a missed call \u{b7} Last run failed");
        assert_eq!(row.last_run_at.as_deref(), Some("2026-08-18T11:30:00.000Z"));
        assert!(!row.can_switch);
        assert_eq!(row.switch_name, "Turn on Follow up");
        assert_eq!(row.runs, None);
        workflow.latest_run = None;
        let row = workflow_row(&screen, &workflow, true);
        assert_eq!(row.detail, "After a missed call \u{b7} Not run yet");
        assert!(row.can_switch);
    }

    #[test]
    fn runs_not_yet_read_read_as_a_workflow_that_never_ran() {
        let view = runs_view(None);
        assert_eq!(view.note.as_deref(), Some(RunHistory::EMPTY));
        assert!(!view.can_retry && !view.can_load_more && !view.loading);
    }

    #[test]
    fn the_sample_has_nothing_read() {
        let view = sample_view();
        assert_eq!(view.status, LoadStatus::Loading);
        assert_eq!(view.campaign.status, LoadStatus::Loading);
        assert_eq!(view.campaign.note, CampaignCard::WEB_ONLY);
        assert_eq!(view.campaign.badge, None);
    }
}
