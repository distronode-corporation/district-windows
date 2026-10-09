//! District HQ (src/hq.rs).

use district_core::{HqEvent, SignedIn};
use district_ffi::ReportTarget;
use district_ffi::hq::{HqAction, HqMessageView, HqView, RichBlock};

use super::*;

/// An answer with each part of the rich text subset in it, two links among
/// them, and two that must not become links.
const RICH_ANSWER: &str = "## This week\n\
    You had **19 calls**, and *3* were missed:\n\
    - 12 answered by the receptionist\n\
    - 4 sent to `voicemail`\n  \
      - 2 after hours\n\
    \n\
    See [the dashboard](https://www.distronode.com/dashboard) or https://example.com/calls.\n\
    [Not this](javascript:alert(1)) and <b>not this</b> either.";

/// This area's snapshot cases.
pub(crate) fn cases() -> Vec<Case> {
    vec![
        ("hq-empty", on_hq(signed_in())),
        ("hq-asking", asking(on_hq(signed_in()), "How many calls?")),
        (
            "hq-answer-rich-text",
            answered(asking(on_hq(signed_in()), "How did this week go?"), rich()),
        ),
        ("hq-confirming", confirming(signed_in())),
        ("hq-applying", applying(signed_in())),
        (
            "hq-confirmed",
            confirmed(applying(signed_in()), Ok("district-hq-confirm.json")),
        ),
        (
            "hq-confirm-failed",
            confirmed(applying(signed_in()), Err(server_error())),
        ),
        ("hq-dismissed", dismissed(confirming(signed_in()))),
        (
            "hq-failed",
            failed(asking(on_hq(signed_in()), "How many calls?")),
        ),
        ("hq-viewer-proposal", confirming(viewer())),
        ("hq-reported", reported(answered_simple(signed_in()))),
    ]
}

fn hq(action: HqAction) -> UiEvent {
    UiEvent::Hq { action }
}

fn on_hq(session: Session) -> Session {
    session.ui(hq(HqAction::Open))
}

fn asking(session: Session, prompt: &str) -> Session {
    session.ui(hq(HqAction::Ask {
        prompt: prompt.to_owned(),
    }))
}

fn rich() -> Value {
    json!({ "success": true, "answer": RICH_ANSWER })
}

fn answered(session: Session, answer: Value) -> Session {
    session.answer(
        |e| matches!(e, Effect::AskHq { .. }),
        |ticket| Event::HqAnswered {
            ticket,
            result: Ok(contracts::decode("hq answer", answer)),
        },
    )
}

fn failed(session: Session) -> Session {
    session.answer(
        |e| matches!(e, Effect::AskHq { .. }),
        |ticket| Event::HqAnswered {
            ticket,
            result: Err(server_error()),
        },
    )
}

fn answered_simple(session: Session) -> Session {
    answered(
        asking(on_hq(session), "How many calls?"),
        contracts::json("district-hq-answer.json"),
    )
}

/// On HQ with the contract's change proposed.
fn confirming(session: Session) -> Session {
    answered(
        asking(on_hq(session), "Change the greeting"),
        contracts::json("district-hq-pending-write.json"),
    )
}

fn applying(session: Session) -> Session {
    confirming(session).ui(hq(HqAction::Confirm))
}

fn confirmed(session: Session, result: Result<&str, ApiError>) -> Session {
    session.answer(
        |e| matches!(e, Effect::ConfirmHq { .. }),
        |ticket| Event::HqConfirmed {
            ticket,
            result: result.map(contracts::read),
        },
    )
}

fn dismissed(session: Session) -> Session {
    session.ui(hq(HqAction::Dismiss))
}

fn reported(session: Session) -> Session {
    session.ui(UiEvent::Report {
        target: ReportTarget::HqAnswer,
        note: "The number is wrong.".to_owned(),
    })
}

fn view(session: &Session) -> HqView {
    match screen_view(&session.model) {
        ScreenView::Hq { view } => view,
        other => panic!("not District HQ: {other:?}"),
    }
}

fn signed_in_state(session: &Session) -> &SignedIn {
    match session.model.session() {
        SessionState::SignedIn(signed_in) => signed_in,
        other => panic!("not signed in: {other:?}"),
    }
}

/// Built, District HQ is offered to every role the core allows it, and its
/// route shows its page.
#[test]
fn built_it_is_offered_and_shown() {
    for session in [on_hq(signed_in()), on_hq(viewer())] {
        assert_eq!(route(&session), Route::Hq);
        assert!(offered(&session).contains(&NavDestination::Hq));
        assert_eq!(view(&session).messages, []);
    }
}

#[test]
fn each_action_is_its_core_event() {
    let cases = [
        (HqAction::Open, Event::Navigate(Route::Hq)),
        (
            HqAction::Ask {
                prompt: " Hi ".to_owned(),
            },
            Event::Hq(HqEvent::Ask(" Hi ".to_owned())),
        ),
        (HqAction::Retry, Event::Hq(HqEvent::Retry)),
        (HqAction::Confirm, Event::Hq(HqEvent::Confirm)),
        (HqAction::Dismiss, Event::Hq(HqEvent::Dismiss)),
        (
            HqAction::OpenLink {
                url: "https://example.com".to_owned(),
            },
            Event::Hq(HqEvent::OpenLink("https://example.com".to_owned())),
        ),
    ];
    for (action, event) in cases {
        assert_eq!(hq(action).events(), [event]);
    }
}

/// A link in an answer opens through the core, which opens only a web page.
#[test]
fn only_a_web_link_is_opened() {
    let session = answered(asking(on_hq(signed_in()), "Links?"), rich());
    let links: Vec<String> = view(&session)
        .messages
        .iter()
        .filter_map(|message| match message {
            HqMessageView::Answer { text, .. } => Some(text.blocks.clone()),
            _ => None,
        })
        .flatten()
        .flat_map(|block| match block {
            RichBlock::Paragraph { runs }
            | RichBlock::Heading { runs }
            | RichBlock::ListItem { runs, .. } => runs,
            RichBlock::Code { .. } => Vec::new(),
        })
        .filter_map(|run| run.link)
        .collect();
    assert_eq!(
        links,
        [
            "https://www.distronode.com/dashboard",
            "https://example.com/calls"
        ]
    );
    let before = session.pending.len();
    let opened = session.ui(hq(HqAction::OpenLink {
        url: links[0].clone(),
    }));
    assert!(matches!(
        opened.pending.last(),
        Some(Effect::OpenUrl { url }) if url == "https://www.distronode.com/dashboard"
    ));
    let after = opened.pending.len();
    assert_eq!(after, before + 1);
    let refused = opened.ui(hq(HqAction::OpenLink {
        url: "javascript:alert(1)".to_owned(),
    }));
    assert_eq!(refused.pending.len(), after, "nothing opened");
}

/// A viewer can ask and Report on the web, and never confirm a change.
#[test]
fn a_viewer_asks_but_never_confirms() {
    let session = confirming(viewer());
    let shown = view(&session);
    let card = shown.card.expect("the proposal is shown");
    assert!(!card.can_confirm);
    assert!(card.can_dismiss);
    assert_eq!(card.note.as_deref(), Some(district_ffi::hq::VIEWER_NOTE));
    assert!(shown.can_ask);
    assert!(shown.messages.iter().any(|message| matches!(
        message,
        HqMessageView::Answer {
            report: district_ffi::ReportAvailability::OnWeb,
            ..
        }
    )));
    let before = session.pending.len();
    let pressed = session.ui(hq(HqAction::Confirm));
    assert_eq!(pressed.pending.len(), before, "nothing is sent");
    assert!(view(&pressed).card.is_some());
}

/// A question that failed stays in the conversation, and trying again sends
/// it once more without adding it a second time.
#[test]
fn a_failed_question_stays_and_is_asked_again() {
    let session = failed(asking(on_hq(signed_in()), "How many calls?"));
    let shown = view(&session);
    assert!(shown.failure.is_some());
    assert!(shown.can_retry);
    assert_eq!(shown.messages.len(), 1);
    let again = session.ui(hq(HqAction::Retry));
    assert!(matches!(again.pending.last(), Some(Effect::AskHq { .. })));
    let shown = view(&again);
    assert_eq!(shown.messages.len(), 1);
    assert!(shown.thinking.is_some());
    assert!(!shown.can_ask);
    assert_eq!(signed_in_state(&again).hq.transcript.len(), 1);
}

/// A report on an answer stays on District HQ and names only the kind.
#[test]
fn a_report_on_an_answer_stays_on_the_page() {
    let session = reported(answered_simple(signed_in()));
    assert!(matches!(screen_view(&session.model), ScreenView::Hq { .. }));
    let message = session
        .pending
        .iter()
        .find_map(|effect| match effect {
            Effect::CreateSupportRequest { draft, .. } => Some(draft.message.clone()),
            _ => None,
        })
        .expect("a report on its way");
    assert!(message.contains("District HQ answer"), "{message}");
    assert!(!message.contains("19 calls"), "{message}");
}
