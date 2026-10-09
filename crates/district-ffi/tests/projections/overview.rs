//! The overview (src/overview.rs).

use district_core::{BillingDestination, PurchaseState, WorkspacesState};
use district_ffi::billing::{BillingAction, PlanTierView};
use district_ffi::{LoadStatus, OverviewView};
use district_live::{LiveError, LiveUpdate, WorkspaceUpdate};

use super::billing::{action, buying, choose, not_buying};
use super::*;

/// This area's snapshot cases.
pub(crate) fn cases() -> Vec<Case> {
    overview_cases()
}

pub(crate) fn overview_loaded(session: Session) -> Session {
    session
        .answer(
            |e| matches!(e, Effect::LoadOverview { .. }),
            |ticket| Event::OverviewLoaded {
                ticket,
                result: Ok(contracts::decode(
                    "district-overview.json",
                    with(
                        contracts::json("district-overview.json"),
                        "workspaceId",
                        "ws-1",
                    ),
                )),
            },
        )
        .answer(
            |e| matches!(e, Effect::LoadUnreadCount { .. }),
            |ticket| Event::UnreadCountLoaded {
                ticket,
                result: Ok(unread_count()),
            },
        )
}

pub(crate) fn no_workspaces(degraded: &[&str]) -> WorkspaceListResponse {
    contracts::decode(
        "no workspaces",
        json!({
            "success": true, "workspaces": [], "total": 0, "limit": 100, "offset": 0,
            "degradedRegions": degraded, "inactiveCount": 0, "defaultWorkspaceId": null
        }),
    )
}

pub(crate) fn overview_cases() -> Vec<Case> {
    vec![
        ("signed-in-overview", signed_in()),
        (
            "overview-loaded-finish-setup",
            overview_loaded(signed_in()).answer(
                |e| matches!(e, Effect::LoadSetupStatus { .. }),
                |ticket| Event::SetupStatusLoaded {
                    ticket,
                    result: Ok(true),
                },
            ),
        ),
        (
            "overview-refreshing",
            overview_loaded(signed_in()).ui(UiEvent::Refresh),
        ),
        (
            "overview-live-stopped",
            overview_loaded(signed_in()).send(Event::Live(WorkspaceUpdate {
                workspace_id: "ws-1".to_owned(),
                update: LiveUpdate::Ended(Some(LiveError::Protocol)),
            })),
        ),
        (
            "overview-failed",
            signed_in().answer(
                |e| matches!(e, Effect::LoadOverview { .. }),
                |ticket| Event::OverviewLoaded {
                    ticket,
                    result: Err(server_error()),
                },
            ),
        ),
        ("overview-no-workspace", no_workspace(buying)),
        (
            "overview-no-workspace-purchases-off",
            no_workspace(not_buying),
        ),
        (
            "overview-no-workspace-confirming",
            no_workspace(buying).ui(choose(Some("save-100"))),
        ),
        (
            "overview-regions-unreachable",
            signed_in_to(Ok(contracts::read("district-workspace-list-degraded.json"))),
        ),
        (
            "overview-workspaces-failed",
            signed_in_to(Err(server_error())),
        ),
        (
            "overview-switcher-partial",
            signed_in_to(Ok(contracts::read("district-workspace-list-partial.json"))),
        ),
    ]
}

/// An account with no workspace, the purchases setting answered by `setting`.
fn no_workspace(setting: fn(Session) -> Session) -> Session {
    setting(signed_in_to(Ok(no_workspaces(&[]))))
}

fn overview_of(session: &Session) -> OverviewView {
    let ScreenView::Overview { view } = screen_view(&session.model) else {
        panic!("not the overview");
    };
    view
}

/// The no-workspace status page: its heading and words.
fn status_of(view: &OverviewView) -> (String, String) {
    let LoadStatus::Failed { title, failure } = &view.status else {
        panic!("{:?}", view.status);
    };
    (title.clone(), failure.message.clone())
}

/// An account with no workspace (a new one, made by sign-up) is offered the
/// billing screen's chooser on the overview, told to choose a plan rather than
/// to contact support, and offered nothing to manage.
#[test]
fn an_account_with_no_workspace_is_offered_the_plans() {
    let view = overview_of(&no_workspace(buying));
    let (title, message) = status_of(&view);
    assert_eq!(title, "No workspace found");
    assert_eq!(message, WorkspacesState::CHOOSE_PLAN_MESSAGE);
    assert!(!message.contains("support"), "{message}");
    let purchase = view.purchase.expect("the chooser");
    assert_eq!(purchase.plans.len(), 4);
    assert_eq!(purchase.terms.len(), 2);
    assert_eq!(purchase.prices_note, PurchaseState::PRICES_NOTE);
    assert!(!purchase.offers_manage, "nothing to manage yet");
    assert!(purchase.enabled && !purchase.opening && purchase.confirming.is_none());
}

/// With purchases off, or before the setting is read, the page is today's:
/// no chooser, and the core's words for an account with no workspace.
#[test]
fn purchases_off_keeps_the_no_workspace_page() {
    const TODAY: &str =
        "This account is not linked to a District workspace yet. Please contact support.";
    for session in [
        no_workspace(not_buying),
        signed_in_to(Ok(no_workspaces(&[]))),
    ] {
        let view = overview_of(&session);
        assert_eq!(view.purchase, None);
        assert_eq!(status_of(&view).1, TODAY);
    }
    // Billing that lapsed is not an account to sell a first plan to.
    let blocked = buying(signed_in_to(Ok(contracts::decode(
        "inactive",
        json!({
            "success": true, "workspaces": [], "total": 0, "limit": 100, "offset": 0,
            "degradedRegions": [], "inactiveCount": 1, "defaultWorkspaceId": null
        }),
    ))));
    assert_eq!(overview_of(&blocked).purchase, None);
}

/// Choose, confirm (the step names Stripe), and the hand-off asks for the
/// checkout with `reason=no-workspace`; closing the checkout window lists the
/// workspaces again and opens the new one.
#[test]
fn checkout_from_the_overview_makes_and_opens_the_first_workspace() {
    let session = no_workspace(buying).ui(choose(Some("save-100")));
    let confirm = overview_of(&session)
        .purchase
        .unwrap()
        .confirming
        .expect("the step naming Stripe");
    assert_eq!(confirm.title, PurchaseState::CONFIRM_TITLE);
    assert_eq!(confirm.body, PurchaseState::CONFIRM_BODY);
    assert_eq!(confirm.action, PurchaseState::CONFIRM_ACTION);
    assert_eq!(confirm.cancel, PurchaseState::CANCEL_ACTION);
    assert_eq!(confirm.promo.as_deref(), Some("SAVE-100"));

    let session = session.ui(action(BillingAction::ConfirmPurchase));
    assert!(session.pending.iter().any(|e| matches!(
        e,
        Effect::OpenEmbedded {
            view: district_core::EmbeddedView::NewPrivate,
            ..
        }
    )));
    let shown = overview_of(&session).purchase.unwrap();
    assert!(shown.opening);
    assert_eq!(shown.opening_label, PurchaseState::OPENING);

    // The start page did not answer in time: the link is asked for, for no
    // workspace, so checkout is told why.
    let session = session.answer(
        |e| matches!(e, Effect::Wait { .. }),
        |ticket| Event::WaitOver { ticket },
    );
    let Some(Effect::RequestBillingHandOff {
        workspace_id,
        destination,
        ..
    }) = session
        .pending
        .iter()
        .find(|e| matches!(e, Effect::RequestBillingHandOff { .. }))
    else {
        panic!("{:?}", session.pending);
    };
    assert_eq!(*workspace_id, None);
    assert!(matches!(destination, BillingDestination::Checkout(_)));
    assert_eq!(
        destination.next(workspace_id.is_some()),
        "/checkout?tier=VoicePro&term=annual&promo=SAVE-100&reason=no-workspace"
    );
    let session = session.answer(
        |e| matches!(e, Effect::RequestBillingHandOff { .. }),
        |ticket| Event::BillingHandOffReady {
            ticket,
            result: Ok(contracts::decode(
                "hand-off",
                json!({"url": "https://www.distronode.com/dashboard/handoff?code=one-time", "expiresIn": 60}),
            )),
        },
    );

    // Paid, the window closes: the workspaces are listed again, and the one
    // checkout made opens.
    let session = session.ui(action(BillingAction::CheckoutClosed));
    assert_eq!(overview_of(&session).status, LoadStatus::Loading);
    let session = overview_loaded(session.answer(
        |e| matches!(e, Effect::LoadWorkspaces { .. }),
        |ticket| Event::WorkspacesLoaded {
            ticket,
            remembered: None,
            result: Ok(contracts::decode(
                "the new workspace",
                json!({
                    "success": true,
                    "workspaces": [{
                        "id": "ws-1", "name": "Example Dental", "region": "us",
                        "role": "owner", "subscriptionTier": "VoicePro"
                    }],
                    "total": 1, "limit": 100, "offset": 0,
                    "degradedRegions": [], "inactiveCount": 0, "defaultWorkspaceId": "ws-1"
                }),
            )),
        },
    ));
    let view = overview_of(&session);
    assert_eq!(view.status, LoadStatus::Ready);
    assert_eq!(view.workspace_name, "Example Dental");
    assert_eq!(view.purchase, None);
}

/// The chooser on the overview sends the billing screen's own actions: a code
/// the core does not take sends nothing, and Cancel goes back.
#[test]
fn the_overview_chooser_is_the_billing_screens() {
    let session = no_workspace(buying).ui(action(BillingAction::ChoosePlan {
        tier: PlanTierView::VoiceSolo,
        term: district_ffi::billing::PlanTermView::Monthly,
        promo: Some("SAVE&tier=x".to_owned()),
    }));
    assert!(overview_of(&session).purchase.unwrap().confirming.is_none());
    let session = session
        .ui(choose(None))
        .ui(action(BillingAction::CancelPurchase));
    assert!(overview_of(&session).purchase.unwrap().confirming.is_none());
}
