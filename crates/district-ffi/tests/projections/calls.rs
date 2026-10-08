//! The call log and one call (src/calls.rs).

use super::*;

/// This area's snapshot cases.
pub(crate) fn cases() -> Vec<Case> {
    [calls_cases(), call_cases()]
        .into_iter()
        .flatten()
        .collect()
}

pub(crate) fn calls(session: Session) -> Session {
    session.ui(UiEvent::Navigate {
        destination: NavDestination::Calls,
    })
}

pub(crate) fn calls_page(session: Session, value: Value) -> Session {
    session.answer(
        |e| matches!(e, Effect::LoadCalls { .. }),
        |ticket| Event::CallsLoaded {
            ticket,
            result: Ok(contracts::decode("calls", value)),
        },
    )
}

pub(crate) fn calls_failed(session: Session) -> Session {
    session.answer(
        |e| matches!(e, Effect::LoadCalls { .. }),
        |ticket| Event::CallsLoaded {
            ticket,
            result: Err(server_error()),
        },
    )
}

/// A full first page: the fixture's calls, repeated under new ids.
pub(crate) fn full_page() -> Value {
    let calls = contracts::json("district-calls.json");
    let rows = calls.as_array().expect("the call log is a list");
    let page: Vec<Value> = (0..25)
        .map(|n| with(rows[n % rows.len()].clone(), "id", format!("call-{n}")))
        .collect();
    Value::Array(page)
}

pub(crate) fn calls_cases() -> Vec<Case> {
    let full = || calls_page(calls(signed_in()), full_page());
    vec![
        ("calls-loading", calls(signed_in())),
        (
            "calls-loaded",
            calls_page(calls(signed_in()), contracts::json("district-calls.json")),
        ),
        ("calls-empty", calls_page(calls(signed_in()), json!([]))),
        ("calls-failed", calls_failed(calls(signed_in()))),
        ("calls-loading-more", full().ui(UiEvent::LoadMoreCalls)),
        (
            "calls-more-failed",
            calls_failed(full().ui(UiEvent::LoadMoreCalls)),
        ),
    ]
}

pub(crate) fn open_call(session: Session, call_id: &str) -> Session {
    session.ui(UiEvent::OpenCall {
        call_id: call_id.to_owned(),
    })
}

pub(crate) fn call_loaded(session: Session, call: Value) -> Session {
    session.answer(
        |e| matches!(e, Effect::LoadCall { .. }),
        |ticket| Event::CallLoaded {
            ticket,
            result: Ok(contracts::decode("call", call)),
        },
    )
}

pub(crate) fn call_failed(session: Session) -> Session {
    session.answer(
        |e| matches!(e, Effect::LoadCall { .. }),
        |ticket| Event::CallLoaded {
            ticket,
            result: Err(server_error()),
        },
    )
}

pub(crate) fn transcript(session: Session, text: &str) -> Session {
    session.answer(
        |e| matches!(e, Effect::LoadTranscript { .. }),
        |ticket| Event::TranscriptLoaded {
            ticket,
            result: Ok(contracts::decode(
                "transcript",
                with(
                    contracts::json("district-call-transcript.json"),
                    "transcript",
                    text,
                ),
            )),
        },
    )
}

/// An outbound call from the log, to a number with nothing known about it,
/// transferred, with a follow-up sent.
pub(crate) fn outbound_call() -> Value {
    let calls = contracts::json("district-calls.json");
    let mut call = calls[2].clone();
    call["number"] = json!("+1 416 555 0171");
    call["followUp"] = json!({"email": "ada@example.com", "sms": null, "sentAt": null});
    call["transferStatus"] = json!("completed");
    call["transferReason"] = json!("caller_requested_human");
    json!({"success": true, "call": call})
}

pub(crate) fn on_call(session: Session) -> Session {
    call_loaded(
        open_call(session, "call_contract_answered"),
        contracts::json("district-call-detail.json"),
    )
}

pub(crate) fn call_read(session: Session) -> Session {
    let text = contracts::json("district-call-transcript.json")["transcript"]
        .as_str()
        .expect("a transcript")
        .to_owned();
    transcript(on_call(session), &text)
}

pub(crate) fn call_cases() -> Vec<Case> {
    vec![
        (
            "call-loading",
            open_call(signed_in(), "call_contract_answered"),
        ),
        ("call-loaded", call_read(signed_in())),
        ("call-viewer", call_read(viewer())),
        (
            "call-outbound-no-transcript",
            transcript(
                call_loaded(
                    open_call(signed_in(), "call_contract_outbound"),
                    outbound_call(),
                ),
                "",
            ),
        ),
        (
            "call-failed",
            call_failed(open_call(signed_in(), "call_gone")).answer(
                |e| matches!(e, Effect::LoadTranscript { .. }),
                |ticket| Event::TranscriptLoaded {
                    ticket,
                    result: Err(server_error()),
                },
            ),
        ),
        (
            "call-refresh-failed",
            call_failed(call_read(signed_in()).ui(UiEvent::Refresh)),
        ),
    ]
}
