//! Booking pages (src/scheduling.rs).

use district_api::ErrorDetail;
use district_core::SchedulingEvent;
use district_ffi::NavDestination;
use district_ffi::scheduling::{SchedulingAction, SchedulingState, SchedulingView};

use super::*;

/// This area's snapshot cases.
pub(crate) fn cases() -> Vec<Case> {
    vec![
        ("scheduling-loading", opened(signed_in())),
        (
            "scheduling-failed",
            status(opened(signed_in()), Err(server_error())),
        ),
        ("scheduling-live", live(signed_in())),
        (
            "scheduling-refreshing",
            live(signed_in()).ui(action(SchedulingAction::CheckAgain)),
        ),
        (
            "scheduling-not-set-up",
            fixture(signed_in(), "district-scheduling-status-legacy.json"),
        ),
        (
            "scheduling-not-offered",
            status(
                opened(signed_in()),
                Ok(json!({"eligible": false, "canManage": false, "tenant": null})),
            ),
        ),
        (
            "scheduling-provisioning",
            fixture(signed_in(), "district-scheduling-status-provisioning.json"),
        ),
        (
            "scheduling-setup-failed",
            fixture(signed_in(), "district-scheduling-status-error.json"),
        ),
        (
            "scheduling-switched-off",
            tenant_status(signed_in(), "disabled"),
        ),
        ("scheduling-unknown", tenant_status(signed_in(), "paused")),
        ("scheduling-enabling", enabling(signed_in())),
        (
            "scheduling-enable-refused",
            enabled(enabling(signed_in()), Err(forbidden())),
        ),
        ("scheduling-awaiting-browser", awaiting_browser(signed_in())),
        ("scheduling-minting", minting(signed_in())),
        (
            "scheduling-hand-off-refused",
            handed_off(minting(signed_in()), Err(forbidden())),
        ),
        (
            "scheduling-hand-off-opened",
            handed_off(minting(signed_in()), Ok(on_the_web())),
        ),
    ]
}

fn action(action: SchedulingAction) -> UiEvent {
    UiEvent::Scheduling { action }
}

fn opened(session: Session) -> Session {
    session.ui(action(SchedulingAction::Open))
}

fn status(session: Session, result: Result<Value, ApiError>) -> Session {
    session.answer(
        |e| matches!(e, Effect::LoadSchedulingStatus { .. }),
        |ticket| Event::SchedulingStatusLoaded {
            ticket,
            result: result.map(|value| contracts::decode("scheduling status", value)),
        },
    )
}

fn fixture(session: Session, name: &str) -> Session {
    status(opened(session), Ok(contracts::json(name)))
}

/// Live, from the fixture.
fn live(session: Session) -> Session {
    fixture(session, "district-scheduling-status-ready.json")
}

/// The fixture's booking pages, in another state.
fn tenant_status(session: Session, state: &str) -> Session {
    let mut value = contracts::json("district-scheduling-status-ready.json");
    value["tenant"]["status"] = json!(state);
    status(opened(session), Ok(value))
}

fn forbidden() -> ApiError {
    ApiError::Forbidden(ErrorDetail::default())
}

fn enabling(session: Session) -> Session {
    fixture(session, "district-scheduling-status-legacy.json").ui(action(SchedulingAction::Enable))
}

fn enabled(session: Session, result: Result<Value, ApiError>) -> Session {
    session.answer(
        |e| matches!(e, Effect::EnableScheduling { .. }),
        |ticket| Event::SchedulingEnabled {
            ticket,
            result: result.map(|value| contracts::decode("scheduling enable", value)),
        },
    )
}

/// "Manage on the web" pressed: the start page is open in the browser, whose
/// answer is awaited.
fn awaiting_browser(session: Session) -> Session {
    live(session).ui(action(SchedulingAction::ManageOnWeb))
}

/// The browser did not answer in time: the link is being asked for.
fn minting(session: Session) -> Session {
    awaiting_browser(session).answer(
        |e| matches!(e, Effect::Wait { .. }),
        |ticket| Event::WaitOver { ticket },
    )
}

fn on_the_web() -> Value {
    json!({"url": "https://www.distronode.com/api/district/handoff?code=one-time", "expiresIn": 60})
}

fn handed_off(session: Session, result: Result<Value, ApiError>) -> Session {
    session.answer(
        |e| matches!(e, Effect::RequestSchedulingHandOff { .. }),
        |ticket| Event::SchedulingHandOffReady {
            ticket,
            result: result.map(|value| contracts::decode("scheduling hand-off", value)),
        },
    )
}

fn view(session: &Session) -> SchedulingView {
    let ScreenView::Scheduling { view } = screen_view(&session.model) else {
        panic!("not the booking pages");
    };
    view
}

/// Built, the area is offered and shows its card.
#[test]
fn built_it_is_offered_and_shows() {
    let session = live(signed_in());
    assert_eq!(route(&session), Route::Scheduling);
    assert!(offered(&session).contains(&NavDestination::Scheduling));
    let view = view(&session);
    assert_eq!(view.state, Some(SchedulingState::Live));
    assert!(view.offers_web && view.web_enabled);
    assert!(!view.offers_enable && !view.offers_check);
}

/// "Manage on the web" opens the start page in the browser and waits for its
/// answer; the link the core then asks for is opened at once, and none of it
/// reaches the screen.
#[test]
fn the_hand_off_opens_in_the_browser_and_shows_no_link() {
    let waiting = awaiting_browser(signed_in());
    assert!(
        waiting
            .pending
            .iter()
            .any(|e| matches!(e, Effect::OpenOneTimeUrl { .. }))
    );
    let shown = view(&waiting);
    assert!(shown.opening && shown.web_enabled);

    let asking = minting(signed_in());
    let shown = view(&asking);
    assert!(shown.opening && !shown.web_enabled);

    let opened = handed_off(minting(signed_in()), Ok(on_the_web()));
    let opens = opened
        .pending
        .iter()
        .filter(|e| matches!(e, Effect::OpenOneTimeUrl { .. }))
        .count();
    assert_eq!(opens, 2, "the start page, then the link");
    let shown = view(&opened);
    assert!(!shown.opening && shown.notice.is_none());
    let json = serde_json::to_string(&screen_view(&opened.model)).unwrap();
    assert!(!json.contains("one-time"), "{json}");
}

/// Turning them on is offered only where the service says this member may.
#[test]
fn enable_is_the_services_to_offer() {
    assert!(
        view(&fixture(
            signed_in(),
            "district-scheduling-status-legacy.json"
        ))
        .offers_enable
    );
    // A setup that failed, but the service says this member may not manage it.
    let failed = view(&fixture(
        signed_in(),
        "district-scheduling-status-error.json",
    ));
    assert!(!failed.offers_enable);
    assert!(failed.offers_check);
    assert_eq!(
        failed.problem.as_deref(),
        Some("cloudflare refused the dns record (HTTP 403)")
    );
    let enabling = view(&enabling(signed_in()));
    assert!(enabling.enabling && enabling.offers_enable);
}

#[test]
fn each_action_is_its_core_event() {
    for (sent, event) in [
        (SchedulingAction::Open, Event::Navigate(Route::Scheduling)),
        (
            SchedulingAction::ManageOnWeb,
            Event::Scheduling(SchedulingEvent::ManageOnWeb),
        ),
        (
            SchedulingAction::Enable,
            Event::Scheduling(SchedulingEvent::Enable),
        ),
        (SchedulingAction::CheckAgain, Event::Refresh),
        (
            SchedulingAction::DismissNotice,
            Event::Scheduling(SchedulingEvent::DismissNotice),
        ),
    ] {
        assert_eq!(action(sent).events(), [event]);
    }
}
