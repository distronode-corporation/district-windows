//! Workflows, their runs and the outbound campaign's switch (src/workflows.rs).

use district_core::WorkflowsEvent;
use district_ffi::workflows::{RunTone, WorkflowsAction, WorkflowsView};

use super::*;

const ACTIVE: &str = "wf_contract_active";
const PAUSED: &str = "wf_contract_paused";

/// This area's snapshot cases.
pub(crate) fn cases() -> Vec<Case> {
    vec![
        ("workflows-loading", open(signed_in())),
        ("workflows-loaded", loaded(signed_in())),
        (
            "workflows-empty",
            workflows(
                campaign(open(signed_in()), "district-campaign-status-empty.json"),
                json!({"success": true, "workflows": []}),
            ),
        ),
        (
            "workflows-failed",
            open(signed_in())
                .answer(
                    |e| matches!(e, Effect::LoadCampaign { .. }),
                    |ticket| Event::CampaignLoaded {
                        ticket,
                        result: Err(server_error()),
                    },
                )
                .answer(
                    |e| matches!(e, Effect::LoadWorkflows { .. }),
                    |ticket| Event::WorkflowsLoaded {
                        ticket,
                        result: Err(server_error()),
                    },
                ),
        ),
        ("workflows-viewer", loaded(viewer())),
        (
            "workflows-runs-loading",
            expand(loaded(signed_in()), ACTIVE),
        ),
        ("workflows-runs-open", runs_open()),
        (
            "workflows-runs-more-loading",
            runs_open().ui(action(WorkflowsAction::LoadMoreRuns {
                workflow_id: ACTIVE.to_owned(),
            })),
        ),
        (
            "workflows-runs-failed",
            expand(loaded(signed_in()), ACTIVE).answer(
                |e| matches!(e, Effect::LoadWorkflowRuns { .. }),
                |ticket| Event::WorkflowRunsLoaded {
                    ticket,
                    result: Err(server_error()),
                },
            ),
        ),
        (
            "workflows-runs-empty",
            expand(loaded(signed_in()), PAUSED).answer(
                |e| matches!(e, Effect::LoadWorkflowRuns { .. }),
                |ticket| Event::WorkflowRunsLoaded {
                    ticket,
                    result: Ok(contracts::decode(
                        "no runs",
                        json!({
                            "success": true,
                            "runs": [],
                            "total": 0,
                            "limit": 10,
                            "offset": 0,
                            "hasMore": false
                        }),
                    )),
                },
            ),
        ),
        ("workflows-switching", switching()),
        (
            "workflows-switch-refused",
            switching().answer(
                |e| matches!(e, Effect::SetWorkflowActive { .. }),
                |ticket| Event::WorkflowActiveSet {
                    ticket,
                    result: Err(server_error()),
                },
            ),
        ),
        ("workflows-confirm-pause", ask_pause()),
        (
            "workflows-confirm-resume",
            workflows(
                campaign(open(signed_in()), "district-campaign-status-empty.json"),
                contracts::json("district-workflows.json"),
            )
            .ui(action(WorkflowsAction::AskCampaign { enable: true })),
        ),
        ("workflows-campaign-pending", pausing()),
        (
            "workflows-campaign-paused",
            pausing().answer(
                |e| matches!(e, Effect::SetCampaignEnabled { .. }),
                |ticket| Event::CampaignSet {
                    ticket,
                    result: Ok(contracts::read("district-campaign-pause.json")),
                },
            ),
        ),
        (
            "workflows-campaign-refused",
            pausing().answer(
                |e| matches!(e, Effect::SetCampaignEnabled { .. }),
                |ticket| Event::CampaignSet {
                    ticket,
                    result: Err(server_error()),
                },
            ),
        ),
    ]
}

fn action(action: WorkflowsAction) -> UiEvent {
    UiEvent::Workflows { action }
}

fn open(session: Session) -> Session {
    session.ui(UiEvent::Navigate {
        destination: NavDestination::Workflows,
    })
}

fn campaign(session: Session, fixture: &str) -> Session {
    let answer: district_model::CampaignStatusResponse = contracts::read(fixture);
    session.answer(
        |e| matches!(e, Effect::LoadCampaign { .. }),
        |ticket| Event::CampaignLoaded {
            ticket,
            result: Ok(answer),
        },
    )
}

fn workflows(session: Session, value: Value) -> Session {
    session.answer(
        |e| matches!(e, Effect::LoadWorkflows { .. }),
        |ticket| Event::WorkflowsLoaded {
            ticket,
            result: Ok(contracts::decode("workflows", value)),
        },
    )
}

/// The workflows screen read: the campaign calling, and the contract's two
/// workflows.
fn loaded(session: Session) -> Session {
    workflows(
        campaign(open(session), "district-campaign-status.json"),
        contracts::json("district-workflows.json"),
    )
}

fn expand(session: Session, workflow_id: &str) -> Session {
    session.ui(action(WorkflowsAction::ToggleExpanded {
        workflow_id: workflow_id.to_owned(),
    }))
}

fn runs_open() -> Session {
    expand(loaded(signed_in()), ACTIVE).answer(
        |e| matches!(e, Effect::LoadWorkflowRuns { .. }),
        |ticket| Event::WorkflowRunsLoaded {
            ticket,
            result: Ok(contracts::read("district-workflow-runs.json")),
        },
    )
}

fn switching() -> Session {
    loaded(signed_in()).ui(action(WorkflowsAction::SetActive {
        workflow_id: PAUSED.to_owned(),
        active: true,
    }))
}

fn ask_pause() -> Session {
    loaded(signed_in()).ui(action(WorkflowsAction::AskCampaign { enable: false }))
}

fn pausing() -> Session {
    ask_pause().ui(action(WorkflowsAction::ConfirmCampaign))
}

fn view(session: &Session) -> WorkflowsView {
    match screen_view(&session.model) {
        ScreenView::Workflows { view } => view,
        other => panic!("not the workflows: {other:?}"),
    }
}

/// Whether `session` has an effect pending that `pick` finds.
fn pending(session: &Session, pick: fn(&Effect) -> bool) -> bool {
    session.pending.iter().any(pick)
}

#[test]
fn built_it_shows_and_is_offered_to_every_role() {
    for session in [loaded(signed_in()), loaded(viewer())] {
        assert_eq!(route(&session), Route::Workflows);
        assert!(offered(&session).contains(&NavDestination::Workflows));
        assert_eq!(
            shell_json(&session)["nav_selected"],
            json!(NavDestination::Workflows)
        );
        assert_eq!(view(&session).rows.len(), 2);
    }
}

#[test]
fn each_action_is_its_core_event() {
    let id = || "wf-1".to_owned();
    let pairs = [
        (WorkflowsAction::Open, Event::Navigate(Route::Workflows)),
        (
            WorkflowsAction::ToggleExpanded { workflow_id: id() },
            Event::Workflows(WorkflowsEvent::ToggleExpanded { workflow_id: id() }),
        ),
        (
            WorkflowsAction::LoadMoreRuns { workflow_id: id() },
            Event::Workflows(WorkflowsEvent::LoadMoreRuns { workflow_id: id() }),
        ),
        (
            WorkflowsAction::SetActive {
                workflow_id: id(),
                active: false,
            },
            Event::Workflows(WorkflowsEvent::SetActive {
                workflow_id: id(),
                active: false,
            }),
        ),
        (
            WorkflowsAction::DismissToggleFailure,
            Event::Workflows(WorkflowsEvent::DismissToggleFailure),
        ),
        (
            WorkflowsAction::AskCampaign { enable: true },
            Event::Workflows(WorkflowsEvent::AskCampaign { enable: true }),
        ),
        (
            WorkflowsAction::ConfirmCampaign,
            Event::Workflows(WorkflowsEvent::ConfirmCampaign),
        ),
        (
            WorkflowsAction::CancelCampaign,
            Event::Workflows(WorkflowsEvent::CancelCampaign),
        ),
    ];
    for (action, event) in pairs {
        assert_eq!(UiEvent::Workflows { action }.events(), [event]);
    }
}

#[test]
fn opening_reads_the_campaign_and_the_workflows() {
    let session = open(signed_in());
    assert!(pending(&session, |e| matches!(
        e,
        Effect::LoadCampaign { .. }
    )));
    assert!(pending(&session, |e| matches!(
        e,
        Effect::LoadWorkflows { .. }
    )));
    let view = view(&session);
    assert_eq!(view.title, "Workflows");
    assert_eq!(view.campaign.title, "Outbound campaign");
}

#[test]
fn the_campaign_card_reads_the_service_s_state() {
    let active = view(&loaded(signed_in())).campaign;
    assert_eq!(active.badge.as_deref(), Some("Active"));
    assert_eq!(active.batch, "25");
    assert_eq!(active.goal, "Book demos with lapsed trials");
    assert!(active.show_pause && active.can_pause && !active.show_resume);
    let never_set = view(&workflows(
        campaign(open(signed_in()), "district-campaign-status-empty.json"),
        json!({"success": true, "workflows": []}),
    ))
    .campaign;
    assert_eq!(never_set.badge.as_deref(), Some("Paused"));
    assert_eq!(never_set.batch, "No batch size set yet.");
    assert_eq!(never_set.goal, "No goal set yet.");
    assert!(never_set.show_resume && never_set.can_resume && !never_set.show_pause);
}

/// The campaign's switch goes through the core's question: nothing is sent
/// until it is answered yes, "no" sends nothing, and the card shows the state
/// the service answers with.
#[test]
fn the_campaign_switch_asks_first() {
    let asked = ask_pause();
    let question = view(&asked).confirming.expect("the core asks");
    assert_eq!(question.title, "Pause the outbound campaign?");
    assert_eq!(question.action, "Pause campaign");
    assert!(!question.enable);
    assert!(!pending(&asked, |e| matches!(
        e,
        Effect::SetCampaignEnabled { .. }
    )));

    let cancelled = ask_pause().ui(action(WorkflowsAction::CancelCampaign));
    assert_eq!(view(&cancelled).confirming, None);
    assert!(!pending(&cancelled, |e| matches!(
        e,
        Effect::SetCampaignEnabled { .. }
    )));

    let sent = pausing();
    assert_eq!(view(&sent).confirming, None);
    assert!(view(&sent).campaign.pending);
    assert!(!view(&sent).campaign.can_pause);
    assert!(pending(&sent, |e| matches!(
        e,
        Effect::SetCampaignEnabled { enabled: false, .. }
    )));
    // Still the last state the service sent, until it answers.
    assert_eq!(view(&sent).campaign.badge.as_deref(), Some("Active"));

    let refused = sent.answer(
        |e| matches!(e, Effect::SetCampaignEnabled { .. }),
        |ticket| Event::CampaignSet {
            ticket,
            result: Err(server_error()),
        },
    );
    let card = view(&refused).campaign;
    assert!(card.failure.is_some());
    assert_eq!(card.badge.as_deref(), Some("Active"));
    assert!(!card.pending && card.can_pause);
}

/// A viewer sees everything and changes nothing: no campaign buttons, the
/// core's note saying why, switches that do not work, and what it sends is
/// refused by the core.
#[test]
fn a_viewer_reads_and_changes_nothing() {
    let session = loaded(viewer());
    let view_now = view(&session);
    assert!(!view_now.campaign.can_change);
    assert!(!view_now.campaign.show_pause && !view_now.campaign.show_resume);
    assert!(!view_now.campaign.can_pause && !view_now.campaign.can_resume);
    assert!(view_now.campaign.note.starts_with("You are a viewer"));
    assert!(view_now.rows.iter().all(|row| !row.can_switch));

    let session = session
        .ui(action(WorkflowsAction::AskCampaign { enable: false }))
        .ui(action(WorkflowsAction::ConfirmCampaign))
        .ui(action(WorkflowsAction::SetActive {
            workflow_id: PAUSED.to_owned(),
            active: true,
        }));
    assert_eq!(view(&session).confirming, None);
    assert!(!pending(&session, |e| matches!(
        e,
        Effect::SetCampaignEnabled { .. } | Effect::SetWorkflowActive { .. }
    )));
    // Reading runs is for every member.
    let session = expand(session, ACTIVE);
    assert!(pending(&session, |e| matches!(
        e,
        Effect::LoadWorkflowRuns { .. }
    )));
}

/// A switch shows the value asked for at once, works once at a time, and is
/// put back with the reason when the service refuses.
#[test]
fn a_switch_shows_at_once_and_is_put_back_when_refused() {
    let row = |session: &Session| {
        view(session)
            .rows
            .into_iter()
            .find(|row| row.workflow_id == PAUSED)
            .unwrap()
    };
    let session = switching();
    let shown = row(&session);
    assert!(shown.active && shown.switching && !shown.can_switch);
    let refused = session.answer(
        |e| matches!(e, Effect::SetWorkflowActive { .. }),
        |ticket| Event::WorkflowActiveSet {
            ticket,
            result: Err(server_error()),
        },
    );
    let put_back = row(&refused);
    assert!(!put_back.active && !put_back.switching && put_back.can_switch);
    assert!(view(&refused).toggle_failure.is_some());
    let dismissed = refused.ui(action(WorkflowsAction::DismissToggleFailure));
    assert_eq!(view(&dismissed).toggle_failure, None);

    let accepted = switching().answer(
        |e| matches!(e, Effect::SetWorkflowActive { .. }),
        |ticket| Event::WorkflowActiveSet {
            ticket,
            result: Ok(contracts::decode("toggled", json!({"success": true}))),
        },
    );
    let kept = row(&accepted);
    assert!(kept.active && !kept.switching);
}

/// A workflow's runs open under it, read once, a page at a time; a first page
/// that failed is read again only with the whole screen.
#[test]
fn runs_open_under_their_workflow() {
    let runs = |session: &Session| {
        view(session)
            .rows
            .into_iter()
            .find(|row| row.workflow_id == ACTIVE)
            .unwrap()
            .runs
    };
    let session = expand(loaded(signed_in()), ACTIVE);
    let reading = runs(&session).expect("open");
    assert!(reading.loading);
    assert_eq!(reading.note.as_deref(), Some("Reading runs."));

    let open = runs_open();
    let read = runs(&open).unwrap();
    assert_eq!(read.runs.len(), 4);
    assert_eq!(
        read.runs.iter().map(|run| run.tone).collect::<Vec<_>>(),
        [
            RunTone::Success,
            RunTone::Warning,
            RunTone::Danger,
            RunTone::Neutral
        ]
    );
    assert_eq!(read.runs[2].detail, "notify_ops webhook returned 502");
    assert!(read.can_load_more && !read.can_retry && read.note.is_none());

    let more = open.ui(action(WorkflowsAction::LoadMoreRuns {
        workflow_id: ACTIVE.to_owned(),
    }));
    assert!(pending(&more, |e| matches!(
        e,
        Effect::LoadWorkflowRuns { offset: 4, .. }
    )));

    let closed = runs_open().ui(action(WorkflowsAction::ToggleExpanded {
        workflow_id: ACTIVE.to_owned(),
    }));
    assert_eq!(runs(&closed), None);

    let failed = session.answer(
        |e| matches!(e, Effect::LoadWorkflowRuns { .. }),
        |ticket| Event::WorkflowRunsLoaded {
            ticket,
            result: Err(server_error()),
        },
    );
    let failed = runs(&failed).unwrap();
    assert!(failed.failure.is_some() && failed.can_retry && !failed.can_load_more);
    assert_eq!(failed.note, None);
}

#[test]
fn an_empty_list_says_so() {
    let empty = view(&workflows(
        campaign(open(signed_in()), "district-campaign-status.json"),
        json!({"success": true, "workflows": []}),
    ));
    let words = empty.empty.expect("an empty list says so");
    assert_eq!(words.title, "No workflows yet");
    assert!(empty.rows.is_empty());
}
