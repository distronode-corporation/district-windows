//! The dialler, a placed call and ringing here (src/calls_live.rs).

use district_live::{LiveUpdate, WorkspaceUpdate};
use district_model::TelemetryEnvelope;

use super::*;

/// This area's snapshot cases.
pub(crate) fn cases() -> Vec<Case> {
    let mut cases = dialer_cases();
    cases.extend(transcript_cases());
    cases
}

/// Signed in to a build that can carry calls.
pub(crate) fn with_calls() -> Session {
    signed_in_with(CoreConfig {
        calls_available: true,
        ..config()
    })
}

/// The dialler and a placed call, in a build with calls; the dialler in one
/// without. No case reaches a call in progress, whose start time is the clock's.
pub(crate) fn dialer_cases() -> Vec<Case> {
    vec![
        ("dialer-without-calls", signed_in().ui(UiEvent::OpenDialer)),
        ("dialer", with_calls().ui(UiEvent::OpenDialer)),
        (
            "dialer-number",
            with_calls()
                .ui(UiEvent::OpenDialer)
                .ui(UiEvent::DialerEdit {
                    number: "+12125550142".to_owned(),
                }),
        ),
        (
            "call-dialing",
            with_calls().ui(UiEvent::CallNumber {
                number: "+12125550142".to_owned(),
            }),
        ),
        (
            "call-not-placed",
            with_calls()
                .ui(UiEvent::CallNumber {
                    number: "+12125550142".to_owned(),
                })
                .answer(
                    |e| matches!(e, Effect::Dial { .. }),
                    |ticket| Event::Dialled {
                        ticket,
                        result: Err(server_error()),
                    },
                ),
        ),
        (
            "account-ring-here",
            with_calls()
                .answer(
                    |e| matches!(e, Effect::ReadRingSetting { .. }),
                    |ticket| Event::RingSettingRead {
                        ticket,
                        ring_here: true,
                    },
                )
                .ui(UiEvent::Navigate {
                    destination: NavDestination::Account,
                }),
        ),
    ]
}

// The live transcript of a placed call (src/transcript.rs).

/// The call id the dial fixture answers with.
fn call_id() -> String {
    contracts::json("district-dial.json")["callId"]
        .as_str()
        .expect("the dial names its call")
        .to_owned()
}

/// A call placed and answered by the service: it has an id, so the core has
/// asked its workspace's socket for the live transcript. Its media is not up,
/// so no clock reaches the snapshot.
pub(crate) fn placed() -> Session {
    with_calls()
        .ui(UiEvent::CallNumber {
            number: "+12125550142".to_owned(),
        })
        .answer(
            |e| matches!(e, Effect::Dial { .. }),
            |ticket| Event::Dialled {
                ticket,
                result: Ok(contracts::read("district-dial.json")),
            },
        )
}

/// The recorded frame `name`, about the placed call, with `edit` made to its
/// data, as the workspace's socket delivers it.
fn frame(session: Session, name: &str, edit: impl FnOnce(&mut Value)) -> Session {
    let mut envelope: TelemetryEnvelope = contracts::read(name);
    envelope.workspace_id = "ws-1".to_owned();
    envelope.call_id = call_id();
    envelope.data["callId"] = json!(call_id());
    edit(&mut envelope.data);
    session.send(Event::Live(WorkspaceUpdate {
        workspace_id: "ws-1".to_owned(),
        update: LiveUpdate::Event(envelope),
    }))
}

/// The recorded snapshot: the assistant's greeting and the caller's
/// request, both final.
fn snapshot(session: Session, edit: impl FnOnce(&mut Value)) -> Session {
    frame(session, "telemetry-event-transcript-snapshot.json", edit)
}

/// Live: the snapshot, the greeting cut off, and the assistant's reply
/// still being heard.
fn live() -> Session {
    let session = snapshot(placed(), |data| {
        data["segments"][0]["interrupted"] = json!(true);
    });
    frame(
        session,
        "telemetry-event-transcript-segment-interim.json",
        |data| {
            let segment = &mut data["segment"];
            segment["segmentId"] = json!("item_c3");
            segment["index"] = json!(2);
            segment["seq"] = json!(3);
            segment["speaker"] = json!("agent");
            segment["speakerName"] = json!("Ava");
            segment["text"] = json!("Of course. Morning or");
        },
    )
}

/// The transcript's end, for `reason`.
fn ended(session: Session, reason: &str) -> Session {
    frame(session, "telemetry-event-transcript-ended.json", |data| {
        data["seq"] = json!(4);
        data["reason"] = json!(reason);
    })
}

/// Hung up: the live transcript ends, and the wait before the full one is
/// read starts.
fn hung_up() -> Session {
    live().ui(UiEvent::HangUp)
}

/// The wait run out and the full transcript asked for.
fn reading_full(session: Session) -> Session {
    session.answer(
        |e| matches!(e, Effect::Wait { .. }),
        |ticket| Event::WaitOver { ticket },
    )
}

fn full_read(session: Session, result: Result<Value, ApiError>) -> Session {
    session.answer(
        |e| matches!(e, Effect::LoadTranscript { .. }),
        |ticket| Event::TranscriptLoaded {
            ticket,
            result: result.map(|value| contracts::decode("full transcript", value)),
        },
    )
}

pub(crate) fn transcript_cases() -> Vec<Case> {
    vec![
        ("transcript-connecting", placed()),
        ("transcript-live", live()),
        (
            "transcript-live-waiting-incomplete",
            snapshot(placed(), |data| {
                data["segments"] = json!([]);
                data["lastSeq"] = json!(0);
                data["complete"] = json!(false);
            }),
        ),
        ("transcript-reconnecting", ended(live(), "agent_error")),
        ("transcript-ended-loading-full", hung_up()),
        (
            "transcript-ended-full",
            full_read(
                reading_full(hung_up()),
                Ok(contracts::json("district-call-transcript.json")),
            ),
        ),
        (
            "transcript-unavailable",
            frame(placed(), "telemetry-event-transcript-error.json", |_| {}),
        ),
    ]
}

/// The live transcript of the call in `session`, as C# reads it.
fn transcript_json(session: &Session) -> Value {
    shell_json(session)["call"]["transcript"].clone()
}

#[test]
fn a_placed_call_asks_for_its_transcript_once_it_has_an_id() {
    let dialing = with_calls().ui(UiEvent::CallNumber {
        number: "+12125550142".to_owned(),
    });
    assert_eq!(transcript_json(&dialing), Value::Null);
    let placed = placed();
    assert!(placed.pending.iter().any(|e| matches!(
        e,
        Effect::WatchTranscript { transcript: Some(watch), .. } if watch.call_id == call_id()
    )));
    let view = transcript_json(&placed);
    assert_eq!(view["phase"], "Connecting");
    assert_eq!(view["status"], "Connecting.");
    assert_eq!(view["heading"], "Live transcript");
}

#[test]
fn lines_carry_their_speaker_and_an_interim_line_is_marked() {
    let view = transcript_json(&live());
    let lines: Vec<(String, String, bool, Value)> = view["lines"]
        .as_array()
        .unwrap()
        .iter()
        .map(|line| {
            (
                line["speaker"].as_str().unwrap().to_owned(),
                line["text"].as_str().unwrap().to_owned(),
                line["is_final"].as_bool().unwrap(),
                line["note"].clone(),
            )
        })
        .collect();
    assert_eq!(
        lines,
        [
            (
                "Ava".to_owned(),
                "Good afternoon, Northside Dental. How can I help?".to_owned(),
                true,
                json!("Interrupted"),
            ),
            (
                "Caller".to_owned(),
                "I'd like to book a cleaning on Thursday.".to_owned(),
                true,
                Value::Null,
            ),
            (
                "Ava".to_owned(),
                "Of course. Morning or".to_owned(),
                false,
                Value::Null,
            ),
        ]
    );
    assert_eq!(view["status"], "Live");
    assert_eq!(view["waiting"], Value::Null);
    assert_eq!(view["incomplete"], Value::Null);
    assert_eq!(view["full"], Value::Null);
}

/// An interim line's final replaces it in place, and keeps its id.
#[test]
fn a_final_replaces_its_interim_in_place() {
    let finished = frame(live(), "telemetry-event-transcript-segment.json", |data| {
        let segment = &mut data["segment"];
        segment["segmentId"] = json!("item_c3");
        segment["index"] = json!(2);
        segment["seq"] = json!(4);
        segment["rev"] = json!(1);
        segment["speaker"] = json!("agent");
        segment["speakerName"] = json!(null);
        segment["text"] = json!("Of course. Morning or afternoon?");
    });
    let lines = transcript_json(&finished)["lines"].clone();
    assert_eq!(lines.as_array().unwrap().len(), 3);
    assert_eq!(lines[2]["id"], "item_c3");
    assert_eq!(lines[2]["is_final"], true);
    // With no persona named, the assistant is "Assistant".
    assert_eq!(lines[2]["speaker"], "Assistant");
    assert_eq!(lines[2]["text"], "Of course. Morning or afternoon?");
}

#[test]
fn a_speaker_this_client_does_not_know_is_another_speaker() {
    let session = snapshot(placed(), |data| {
        data["segments"][1]["speaker"] = json!("supervisor");
    });
    assert_eq!(
        transcript_json(&session)["lines"][1]["speaker"],
        "Other speaker"
    );
}

#[test]
fn after_the_call_the_full_transcript_is_read_and_said() {
    let loading = transcript_json(&hung_up());
    assert_eq!(loading["phase"], "Ended");
    assert_eq!(loading["status"], "Call ended");
    assert_eq!(
        loading["full"],
        json!({"loading": true, "text": null, "note": "Loading the full transcript."})
    );
    assert_eq!(loading["lines"].as_array().unwrap().len(), 3);

    let read = transcript_json(&full_read(
        reading_full(hung_up()),
        Ok(contracts::json("district-call-transcript.json")),
    ));
    assert_eq!(read["full"]["loading"], false);
    assert_eq!(
        read["full"]["text"],
        contracts::json("district-call-transcript.json")["transcript"]
    );

    // A read refused for good says why.
    let refused = transcript_json(&full_read(
        reading_full(hung_up()),
        Err(ApiError::Envelope {
            status: 404,
            code: "not_found".to_owned(),
            detail: ErrorDetail::default(),
        }),
    ));
    assert_eq!(refused["full"]["loading"], false);
    assert_eq!(refused["full"]["text"], Value::Null);
    assert!(refused["full"]["note"].as_str().is_some());
}

/// Still empty after every attempt: "No transcript for this call."
#[test]
fn a_full_transcript_still_empty_says_there_is_none() {
    let mut session = hung_up();
    for _ in 0..district_core::FINAL_FETCH_ATTEMPTS {
        session = full_read(
            reading_full(session),
            Ok(json!({"success": true, "transcript": ""})),
        );
    }
    assert_eq!(
        transcript_json(&session)["full"],
        json!({"loading": false, "text": null, "note": "No transcript for this call."})
    );
}

#[test]
fn no_live_transcript_says_the_call_log_has_it() {
    let view = transcript_json(&frame(
        placed(),
        "telemetry-event-transcript-error.json",
        |_| {},
    ));
    assert_eq!(view["phase"], "Unavailable");
    assert_eq!(
        view["status"],
        "No live transcript for this call. Its transcript is in the call log after the call."
    );
    assert_eq!(view["lines"], json!([]));
}

/// No line claims what the product does not do: nothing about calls being
/// end-to-end encrypted, and nothing about recording.
#[test]
fn the_transcript_says_nothing_about_recording_or_encryption() {
    for (name, session) in transcript_cases() {
        let text = transcript_json(&session).to_string().to_lowercase();
        for word in ["record", "encrypt"] {
            assert!(!text.contains(word), "{name}: {word}");
        }
    }
}
