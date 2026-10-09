//! Support requests, one request, and the form raising one (src/support.rs).

use district_core::{SupportEvent, SupportForm};
use district_ffi::support::{SupportAction, SupportKind};
use district_model::SupportRequestKind;

use super::*;

/// This area's snapshot cases.
pub(crate) fn cases() -> Vec<Case> {
    [list_cases(), compose_cases(), request_cases()]
        .into_iter()
        .flatten()
        .collect()
}

fn support(action: SupportAction) -> UiEvent {
    UiEvent::Support { action }
}

pub(crate) fn opened(session: Session) -> Session {
    session.ui(support(SupportAction::Open))
}

pub(crate) fn listed(session: Session, result: Result<Value, ApiError>) -> Session {
    session.answer(
        |e| matches!(e, Effect::LoadSupportRequests { .. }),
        |ticket| Event::SupportRequestsLoaded {
            ticket,
            result: result.map(|value| contracts::decode("support requests", value)),
        },
    )
}

fn requests() -> Value {
    contracts::json("district-support-requests.json")
}

/// The support page, its list read from the fixture.
pub(crate) fn on_support(session: Session) -> Session {
    listed(opened(session), Ok(requests()))
}

/// As many requests as the service ever sends, the fixture's first repeated.
fn at_the_cap() -> Value {
    let one = requests()["requests"][0].clone();
    let all: Vec<Value> = (0..100)
        .map(|n| {
            let mut request = one.clone();
            request["issueKey"] = json!(format!("DA-{}", 100 + n));
            request["id"] = json!(format!("support_{n}"));
            request
        })
        .collect();
    json!({"success": true, "requests": all})
}

fn list_cases() -> Vec<Case> {
    vec![
        ("support-loading", opened(signed_in())),
        ("support-loaded", on_support(signed_in())),
        (
            "support-empty",
            listed(
                opened(signed_in()),
                Ok(json!({"success": true, "requests": []})),
            ),
        ),
        (
            "support-failed",
            listed(opened(signed_in()), Err(server_error())),
        ),
        (
            "support-refreshing",
            on_support(signed_in()).ui(UiEvent::Refresh),
        ),
        (
            "support-refresh-failed",
            listed(
                on_support(signed_in()).ui(UiEvent::Refresh),
                Err(server_error()),
            ),
        ),
        (
            "support-capped",
            listed(opened(signed_in()), Ok(at_the_cap())),
        ),
    ]
}

/// The whole form as typed.
pub(crate) fn typed(session: Session, kind: SupportKind, subject: &str, message: &str) -> Session {
    session.ui(support(SupportAction::EditRequest {
        kind,
        subject: subject.to_owned(),
        message: message.to_owned(),
    }))
}

/// The support page with the form open.
pub(crate) fn composing(session: Session) -> Session {
    on_support(session).ui(support(SupportAction::StartRequest))
}

/// A request the service takes.
pub(crate) fn ready(session: Session) -> Session {
    typed(
        composing(session),
        SupportKind::Question,
        "Voicemail greeting",
        "How do I change the greeting callers hear?",
    )
}

pub(crate) fn created(session: Session, result: Result<Value, ApiError>) -> Session {
    session.answer(
        |e| matches!(e, Effect::CreateSupportRequest { .. }),
        |ticket| Event::SupportRequestCreated {
            ticket,
            result: result.map(|value| contracts::decode("support create", value)),
        },
    )
}

fn submitted(session: Session) -> Session {
    ready(session).ui(support(SupportAction::SubmitRequest))
}

fn compose_cases() -> Vec<Case> {
    let create = || Ok(contracts::json("district-support-request-create.json"));
    vec![
        ("support-compose-open", composing(signed_in())),
        (
            "support-compose-incomplete",
            typed(composing(signed_in()), SupportKind::Problem, "Hi", ""),
        ),
        ("support-compose-ready", ready(signed_in())),
        ("support-compose-submitting", submitted(signed_in())),
        (
            "support-compose-failed",
            created(submitted(signed_in()), Err(server_error())),
        ),
        ("support-filed", created(submitted(signed_in()), create())),
        (
            "support-filed-dismissed",
            created(submitted(signed_in()), create()).ui(support(SupportAction::DismissSubmitted)),
        ),
        (
            "support-compose-discarded",
            ready(signed_in()).ui(support(SupportAction::CancelRequest)),
        ),
    ]
}

/// The request `DA-42`, opened from the list.
pub(crate) fn request_open(session: Session) -> Session {
    on_support(session).ui(support(SupportAction::OpenRequest {
        key: "DA-42".to_owned(),
    }))
}

pub(crate) fn request_read(session: Session, result: Result<Value, ApiError>) -> Session {
    session.answer(
        |e| matches!(e, Effect::LoadSupportRequest { .. }),
        |ticket| Event::SupportRequestLoaded {
            ticket,
            result: result.map(|value| contracts::decode("support request", value)),
        },
    )
}

/// The request `DA-42`, read.
pub(crate) fn on_request(session: Session) -> Session {
    request_read(
        request_open(session),
        Ok(contracts::json("district-support-request.json")),
    )
}

fn replying(session: Session) -> Session {
    on_request(session).ui(support(SupportAction::EditReply {
        text: "Still failing as of this morning.".to_owned(),
    }))
}

fn reply_sent(session: Session) -> Session {
    replying(session).ui(support(SupportAction::SendReply))
}

fn replied(session: Session, result: Result<Value, ApiError>) -> Session {
    session.answer(
        |e| matches!(e, Effect::ReplyToSupportRequest { .. }),
        |ticket| Event::SupportReplied {
            ticket,
            result: result.map(|value| contracts::decode("support reply", value)),
        },
    )
}

fn asked_to_close(session: Session) -> Session {
    on_request(session).ui(support(SupportAction::AskClose))
}

fn closing(session: Session) -> Session {
    asked_to_close(session).ui(support(SupportAction::ConfirmClose))
}

fn closed(session: Session, result: Result<Value, ApiError>) -> Session {
    session.answer(
        |e| matches!(e, Effect::CloseSupportRequest { .. }),
        |ticket| Event::SupportRequestClosed {
            ticket,
            result: result.map(|value| contracts::decode("support close", value)),
        },
    )
}

fn request_cases() -> Vec<Case> {
    vec![
        ("support-request-loading", request_open(signed_in())),
        ("support-request-loaded", on_request(signed_in())),
        (
            "support-request-failed",
            request_read(request_open(signed_in()), Err(server_error())),
        ),
        (
            "support-request-refresh-failed",
            request_read(
                on_request(signed_in()).ui(UiEvent::Refresh),
                Err(server_error()),
            ),
        ),
        ("support-request-replying", replying(signed_in())),
        ("support-request-reply-sending", reply_sent(signed_in())),
        (
            "support-request-reply-failed",
            replied(reply_sent(signed_in()), Err(server_error())),
        ),
        (
            "support-request-replied",
            replied(
                reply_sent(signed_in()),
                Ok(contracts::json("district-support-reply.json")),
            ),
        ),
        ("support-request-confirm-close", asked_to_close(signed_in())),
        ("support-request-closing", closing(signed_in())),
        (
            "support-request-closed",
            closed(
                closing(signed_in()),
                Ok(contracts::json("district-support-close.json")),
            ),
        ),
        (
            "support-request-close-failed",
            closed(closing(signed_in()), Err(server_error())),
        ),
        (
            "support-request-failures-dismissed",
            closed(closing(signed_in()), Err(server_error()))
                .ui(support(SupportAction::DismissFailures)),
        ),
    ]
}

/// Built, the area is offered to a member and its screens show.
#[test]
fn built_it_is_offered_and_shows() {
    let session = on_support(signed_in());
    assert_eq!(route(&session), Route::Support);
    assert!(offered(&session).contains(&NavDestination::Support));
    let ScreenView::Support { view } = screen_view(&session.model) else {
        panic!("not the support page");
    };
    assert_eq!(view.open.len(), 2);
    assert_eq!(view.resolved.len(), 1);
    assert_eq!(view.open[1].key, "support_pending");
    assert_eq!(view.open[1].reference, "Not filed yet");
}

/// Support refuses a viewer, reads included: the pane does not offer it, and
/// the core does not open either screen for one.
#[test]
fn a_viewer_is_never_shown_support() {
    let viewer = viewer();
    let before = route(&viewer);
    assert!(!offered(&viewer).contains(&NavDestination::Support));
    let tried = opened(viewer);
    assert_eq!(route(&tried), before);
    let tried = tried.ui(support(SupportAction::OpenRequest {
        key: "DA-42".to_owned(),
    }));
    assert_eq!(route(&tried), before);
    assert!(tried.pending.iter().all(|effect| !matches!(
        effect,
        Effect::LoadSupportRequests { .. } | Effect::LoadSupportRequest { .. }
    )));
}

/// The form is sent as the member typed it, and the request goes once.
#[test]
fn the_request_sent_is_the_one_typed() {
    let session = submitted(signed_in());
    let drafts: Vec<_> = session
        .pending
        .iter()
        .filter_map(|effect| match effect {
            Effect::CreateSupportRequest { draft, .. } => Some(draft.clone()),
            _ => None,
        })
        .collect();
    assert_eq!(drafts.len(), 1);
    assert_eq!(drafts[0].kind, SupportRequestKind::Question);
    assert_eq!(drafts[0].subject, "Voicemail greeting");
    let again = session.ui(support(SupportAction::SubmitRequest));
    let sends = again
        .pending
        .iter()
        .filter(|effect| matches!(effect, Effect::CreateSupportRequest { .. }))
        .count();
    assert_eq!(sends, 1);
}

#[test]
fn each_action_is_its_core_event() {
    let one = |action| support(action).events();
    assert_eq!(one(SupportAction::Open), [Event::Navigate(Route::Support)]);
    assert_eq!(
        one(SupportAction::OpenRequest {
            key: "DA-42".to_owned(),
        }),
        [Event::Navigate(Route::SupportRequest {
            key: "DA-42".to_owned(),
        })]
    );
    for (kind, core) in [
        (SupportKind::Problem, SupportRequestKind::Problem),
        (SupportKind::Question, SupportRequestKind::Question),
        (SupportKind::Suggestion, SupportRequestKind::Suggestion),
    ] {
        assert_eq!(
            one(SupportAction::EditRequest {
                kind,
                subject: "Subject".to_owned(),
                message: "Message".to_owned(),
            }),
            [Event::Support(SupportEvent::EditRequest(SupportForm {
                kind: core,
                subject: "Subject".to_owned(),
                message: "Message".to_owned(),
            }))]
        );
    }
    assert_eq!(
        one(SupportAction::EditReply {
            text: "Thanks".to_owned(),
        }),
        [Event::Support(SupportEvent::EditReply("Thanks".to_owned()))]
    );
    for (action, event) in [
        (SupportAction::StartRequest, SupportEvent::StartRequest),
        (SupportAction::SubmitRequest, SupportEvent::SubmitRequest),
        (SupportAction::CancelRequest, SupportEvent::CancelRequest),
        (
            SupportAction::DismissSubmitted,
            SupportEvent::DismissSubmitted,
        ),
        (SupportAction::SendReply, SupportEvent::SendReply),
        (SupportAction::AskClose, SupportEvent::AskClose),
        (SupportAction::ConfirmClose, SupportEvent::ConfirmClose),
        (SupportAction::CancelClose, SupportEvent::CancelClose),
        (
            SupportAction::DismissFailures,
            SupportEvent::DismissFailures,
        ),
    ] {
        assert_eq!(one(action), [Event::Support(event)]);
    }
}
