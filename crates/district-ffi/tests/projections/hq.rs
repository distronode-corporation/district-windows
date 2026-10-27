//! District HQ (src/hq.rs).

use district_core::{
    HqEvent, MediaEvent, MediaUpdate, MicrophoneState, Participant, SignedIn, Speaker,
    SpeechSegment,
};
use district_ffi::ReportTarget;
use district_ffi::hq::{
    HqAction, HqMessageView, HqView, HqVoiceCardView, HqVoiceDockView, HqVoiceStage, RichBlock,
};
use district_ffi::l10n::{cs, l, s};

use super::calls_live::with_calls;
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
        // By voice.
        ("hq-voice-disclosure", disclosing(with_calls())),
        ("hq-voice-disclosure-region", disclosing(in_canada())),
        ("hq-voice-connecting", accepted(with_calls())),
        ("hq-voice-listening", listening(with_calls())),
        ("hq-voice-card", proposed(listening(with_calls()), "p-1")),
        (
            "hq-voice-card-confirm-failed",
            voice_confirmed(
                clicked_confirm(proposed(listening(with_calls()), "p-1")),
                Err(server_error()),
            ),
        ),
        (
            "hq-voice-ended",
            agent_left(proposed(listening(with_calls()), "p-1")),
        ),
        (
            "hq-voice-failed",
            issued(accepted(with_calls()), Err(server_error())),
        ),
        ("hq-voice-without-calls", on_hq(signed_in())),
        (
            "hq-voice-dock",
            proposed(listening(with_calls()), "p-1").ui(UiEvent::Navigate {
                destination: NavDestination::Calls,
            }),
        ),
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
    assert_eq!(card.note.as_deref(), Some(s::hq_viewer_note(l())));
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

// District HQ by voice.

/// The room the voice credential names.
const ROOM: &str = "hq_ws-1_00000000-0000-4000-8000-000000000000";

/// On District HQ, Start voice pressed: the residency statement.
fn disclosing(session: Session) -> Session {
    on_hq(session).ui(hq(HqAction::StartVoice))
}

/// The statement accepted: the session's credential asked for.
fn accepted(session: Session) -> Session {
    disclosing(session).ui(hq(HqAction::AcceptVoiceDisclosure))
}

/// The voice credential, as the service sends it, with the room's passphrase.
fn credential() -> district_model::HqVoiceTokenResponse {
    contracts::decode(
        "hq voice credential",
        json!({
            "success": true,
            "token": "contract-livekit-hq-voice-jwt",
            "url": "wss://media.example.com",
            "roomName": ROOM,
            "processingRegion": "us",
            "e2ee": {"key": "YFT+/ceW4bhgizPFpeX9E5a3PVZ9T07aWRQq4uwQwW0="},
        }),
    )
}

/// The credential's answer.
fn issued(
    session: Session,
    result: Result<district_model::HqVoiceTokenResponse, ApiError>,
) -> Session {
    session.answer(
        |e| matches!(e, Effect::RequestHqVoice { .. }),
        |ticket| Event::HqVoiceIssued { ticket, result },
    )
}

/// The media session the core asked the engine to join.
fn media_session(session: &Session) -> Ticket {
    session
        .pending
        .iter()
        .rev()
        .find_map(|effect| match effect {
            Effect::ConnectMedia { session, .. } => Some(*session),
            _ => None,
        })
        .expect("the room is joined")
}

/// What the engine reports on the voice session.
fn media(session: Session, event: MediaEvent) -> Session {
    let id = media_session(&session);
    session.send(Event::Media(MediaUpdate { session: id, event }))
}

/// Joined, the agent in the room, the microphone on, and no state from the
/// agent yet.
fn joined(session: Session) -> Session {
    let session = issued(accepted(session), Ok(credential()));
    let session = media(session, MediaEvent::Connected);
    let session = media(
        session,
        MediaEvent::ParticipantJoined(Participant::new("agent-hq", None, true)),
    );
    media(session, MediaEvent::Microphone(MicrophoneState::On))
}

/// Joined, and the agent listening.
fn listening(session: Session) -> Session {
    media(
        joined(session),
        MediaEvent::AgentState("listening".to_owned()),
    )
}

/// A packet from the voice agent, as JSON.
fn packet(session: Session, packet: Value) -> Session {
    media(
        session,
        MediaEvent::AgentData {
            topic: district_model::HQ_PROPOSAL_TOPIC.to_owned(),
            payload: packet.to_string().into_bytes(),
        },
    )
}

/// A change proposed by voice.
fn proposed(session: Session, id: &str) -> Session {
    packet(
        session,
        json!({
            "type": "HQ_PROPOSAL",
            "proposalId": id,
            "tool": "update_persona",
            "args": {"greeting": "Good afternoon, thanks for calling Analytical Engines."},
            "summary": "Update the greeting.",
        }),
    )
}

fn clicked_confirm(session: Session) -> Session {
    let id = voice_view(&session).cards[0].proposal_id.clone();
    session.ui(hq(HqAction::ConfirmVoiceCard { proposal_id: id }))
}

fn voice_confirmed(session: Session, result: Result<&str, ApiError>) -> Session {
    session.answer(
        |e| matches!(e, Effect::ConfirmHqVoice { .. }),
        |ticket| Event::HqVoiceConfirmed {
            ticket,
            result: result.map(contracts::read),
        },
    )
}

/// The agent leaves: the session ends.
fn agent_left(session: Session) -> Session {
    media(
        session,
        MediaEvent::ParticipantLeft {
            identity: "agent-hq".to_owned(),
        },
    )
}

/// Words spoken in the session.
fn said(session: Session, speaker: Speaker, id: &str, text: &str, is_final: bool) -> Session {
    media(
        session,
        MediaEvent::Speech(SpeechSegment {
            segment_id: id.to_owned(),
            speaker,
            text: text.to_owned(),
            is_final,
        }),
    )
}

/// Signed in, in a build with calls, to a workspace whose data lives in
/// Canada.
fn in_canada() -> Session {
    signed_in_as(
        CoreConfig {
            calls_available: true,
            ..config()
        },
        Ok(contracts::decode(
            "one workspace in Canada",
            json!({
                "success": true,
                "workspaces": [{
                    "id": "ws-1",
                    "name": "Example Dental",
                    "region": "ca",
                    "role": "agency",
                    "subscriptionTier": "VoicePro"
                }],
                "total": 1,
                "limit": 100,
                "offset": 0,
                "degradedRegions": [],
                "inactiveCount": 0,
                "defaultWorkspaceId": "ws-1"
            }),
        )),
    )
}

fn voice_view(session: &Session) -> district_ffi::hq::HqVoiceView {
    view(session).voice
}

fn dock(session: &Session) -> Option<HqVoiceDockView> {
    shell_view(&session.model).voice_dock
}

#[test]
fn each_voice_action_is_its_core_event() {
    let cases = [
        (HqAction::StartVoice, Event::Hq(HqEvent::StartVoice)),
        (
            HqAction::AcceptVoiceDisclosure,
            Event::Hq(HqEvent::AcceptVoiceDisclosure),
        ),
        (
            HqAction::DeclineVoiceDisclosure,
            Event::Hq(HqEvent::DeclineVoiceDisclosure),
        ),
        (HqAction::StopVoice, Event::Hq(HqEvent::StopVoice)),
        (
            HqAction::ConfirmVoiceCard {
                proposal_id: "p-1".to_owned(),
            },
            Event::Hq(HqEvent::ConfirmVoiceCard("p-1".to_owned())),
        ),
        (
            HqAction::CancelVoiceCard {
                proposal_id: "p-1".to_owned(),
            },
            Event::Hq(HqEvent::CancelVoiceCard("p-1".to_owned())),
        ),
        (
            HqAction::DismissVoiceNotice,
            Event::Hq(HqEvent::DismissVoiceNotice),
        ),
    ];
    for (action, event) in cases {
        assert_eq!(hq(action).events(), [event]);
    }
}

/// Where the voice is processed is said before anything starts, and nothing
/// is asked for until the member accepts; Not now closes it.
#[test]
fn the_statement_comes_first_and_asks_for_nothing() {
    let session = on_hq(with_calls());
    let shown = view(&session);
    assert!(shown.show_voice_start && shown.can_start_voice);
    assert_eq!(shown.voice_start_label, cs::hq_voice_start(l()));
    assert_eq!(shown.voice.stage, HqVoiceStage::Off);
    assert_eq!(shown.voice.line, None);
    let session = session.ui(hq(HqAction::StartVoice));
    let shown = view(&session);
    let statement = shown.voice_disclosure.expect("the statement");
    assert!(
        statement
            .body
            .starts_with("Your voice is processed in the United States,")
    );
    assert_eq!(statement.region_notice, None, "a United States workspace");
    assert!(!shown.show_voice_start, "the statement's own Start voice");
    assert_eq!(shown.voice.stage, HqVoiceStage::Disclosing);
    assert!(
        !session
            .pending
            .iter()
            .any(|effect| matches!(effect, Effect::RequestHqVoice { .. })),
        "nothing before the member accepts"
    );
    let declined = session.ui(hq(HqAction::DeclineVoiceDisclosure));
    let shown = view(&declined);
    assert_eq!(shown.voice_disclosure, None);
    assert!(shown.show_voice_start);
    assert_eq!(shown.voice.stage, HqVoiceStage::Off);
}

/// A workspace whose data lives outside the United States is told so above
/// the statement, in the web's words.
#[test]
fn a_workspace_in_canada_is_told_its_voice_leaves_it() {
    let statement = view(&disclosing(in_canada()))
        .voice_disclosure
        .expect("the statement");
    let notice = statement.region_notice.expect("the line of its own");
    assert!(notice.contains("Canada"), "{notice}");
    assert!(notice.contains("United States"), "{notice}");
}

/// The line says what the session is doing, from the credential to the
/// agent's state, with a ring until the agent has joined.
#[test]
fn the_line_follows_the_session() {
    let session = accepted(with_calls());
    let shown = voice_view(&session);
    assert_eq!(shown.stage, HqVoiceStage::Starting);
    assert_eq!(shown.line.as_deref(), Some(cs::hq_voice_connecting(l())));
    assert!(shown.busy && shown.can_stop && !shown.show_microphone);
    assert!(!view(&session).show_voice_start);

    let session = issued(session, Ok(credential()));
    assert_eq!(voice_view(&session).stage, HqVoiceStage::Live);
    assert_eq!(
        voice_view(&session).line.as_deref(),
        Some(cs::hq_voice_connecting(l()))
    );
    let session = media(session, MediaEvent::Connected);
    assert_eq!(
        voice_view(&session).line.as_deref(),
        Some(cs::hq_voice_agent_joining(l()))
    );
    assert!(voice_view(&session).busy);
    for (state, line) in [
        ("listening", cs::hq_voice_listening(l())),
        ("idle", cs::hq_voice_listening(l())),
        ("thinking", cs::hq_voice_thinking(l())),
        ("speaking", cs::hq_voice_speaking(l())),
        ("initializing", cs::hq_voice_agent_joining(l())),
    ] {
        let now = media(
            joined(with_calls()),
            MediaEvent::AgentState(state.to_owned()),
        );
        let shown = voice_view(&now);
        assert_eq!(shown.line.as_deref(), Some(line), "{state}");
        assert_eq!(shown.busy, state == "initializing", "{state}");
        assert!(!shown.line_is_problem);
        assert!(shown.show_microphone && !shown.muted);
    }
    // A notice on the room is a problem, said instead.
    let reconnecting = media(listening(with_calls()), MediaEvent::Reconnecting);
    let shown = voice_view(&reconnecting);
    assert!(shown.line_is_problem && !shown.busy);
    assert_eq!(shown.line.as_deref(), Some(cs::media_reconnecting(l())));
}

/// The microphone button says whether the microphone is off, and pressing it
/// asks for the other state.
#[test]
fn the_microphone_mutes_and_unmutes() {
    let session = listening(with_calls());
    assert!(!voice_view(&session).muted);
    let session = session.ui(UiEvent::Microphone { on: false });
    assert!(matches!(
        session.pending.last(),
        Some(Effect::SetMicrophone { enabled: false, .. })
    ));
    let session = media(session, MediaEvent::Microphone(MicrophoneState::Off));
    assert!(voice_view(&session).muted);
}

/// Both sides' words join the conversation as they are spoken, the member's
/// growing in place until final.
#[test]
fn spoken_words_join_the_conversation() {
    let session = said(
        listening(with_calls()),
        Speaker::Member,
        "m1",
        "Change the",
        false,
    );
    let session = said(session, Speaker::Member, "m1", "Change the greeting", true);
    let session = said(session, Speaker::Agent, "a1", "I've proposed it.", false);
    assert_eq!(
        view(&session).messages,
        [
            HqMessageView::Spoken {
                index: 0,
                text: "Change the greeting".to_owned(),
                member: true,
                is_final: true,
            },
            HqMessageView::Spoken {
                index: 1,
                text: "I've proposed it.".to_owned(),
                member: false,
                is_final: false,
            },
        ]
    );
}

/// A change proposed by voice is a card, applied only by Confirm; one click
/// in flight; a failed confirmation keeps the card and says the change may
/// have been made.
#[test]
fn a_voice_card_is_confirmed_by_click() {
    let session = proposed(proposed(listening(with_calls()), "p-1"), "p-1");
    let cards = voice_view(&session).cards;
    assert_eq!(cards.len(), 1, "a repeat is the same card");
    let card = &cards[0];
    assert_eq!(card.summary, "Update the greeting.");
    assert_eq!(card.note.as_deref(), Some(cs::hq_voice_card_note(l())));
    assert!(card.can_confirm && card.can_cancel && card.suggested && !card.busy);

    let session = clicked_confirm(session);
    let card = &voice_view(&session).cards[0];
    assert!(card.busy && !card.can_confirm && !card.can_cancel);
    assert_eq!(card.note.as_deref(), Some(cs::hq_applying(l())));

    let session = voice_confirmed(session, Err(server_error()));
    let card = &voice_view(&session).cards[0];
    assert!(card.failure.is_some() && card.can_confirm);
    assert_eq!(card.note.as_deref(), Some(s::hq_unknown_note(l())));

    let session = voice_confirmed(clicked_confirm(session), Ok("district-hq-confirm.json"));
    assert_eq!(voice_view(&session).cards, []);
    assert!(
        view(&session)
            .messages
            .iter()
            .any(|message| matches!(message, HqMessageView::Note { applied: true, .. }))
    );
}

/// Cancel by click: a ring while it is sent, a note once done, which is good
/// news; a failed cancel keeps the card with no note of its own.
#[test]
fn a_voice_card_is_cancelled_by_click() {
    let session = proposed(listening(with_calls()), "p-2");
    let session = session.ui(hq(HqAction::CancelVoiceCard {
        proposal_id: "p-2".to_owned(),
    }));
    let card = &voice_view(&session).cards[0];
    assert!(card.busy);
    assert_eq!(card.note.as_deref(), Some(cs::hq_voice_cancelling(l())));
    let failed = session.answer(
        |e| matches!(e, Effect::CancelHqVoice { .. }),
        |ticket| Event::HqVoiceCancelled {
            ticket,
            result: Err(server_error()),
        },
    );
    let card = &voice_view(&failed).cards[0];
    assert!(card.failure.is_some() && card.note.is_none() && card.can_cancel);
    let done = failed
        .ui(hq(HqAction::CancelVoiceCard {
            proposal_id: "p-2".to_owned(),
        }))
        .answer(
            |e| matches!(e, Effect::CancelHqVoice { .. }),
            |ticket| Event::HqVoiceCancelled {
                ticket,
                result: Ok(district_model::HqProposalCancelResponse { success: true }),
            },
        );
    assert_eq!(voice_view(&done).cards, []);
    assert!(view(&done).messages.contains(&HqMessageView::Note {
        index: 0,
        text: cs::hq_note_cancelled(l()).to_owned(),
        applied: true,
    }));
}

/// Only the first card's Confirm is the suggested action, and a card resolved
/// by voice goes.
#[test]
fn one_suggested_confirm_and_cards_resolved_by_voice_go() {
    let session = proposed(proposed(listening(with_calls()), "p-3"), "p-4");
    let suggested: Vec<bool> = voice_view(&session)
        .cards
        .iter()
        .map(|card: &HqVoiceCardView| card.suggested)
        .collect();
    assert_eq!(suggested, [true, false]);
    let session = packet(
        session,
        json!({
            "type": "HQ_PROPOSAL_RESOLVED", "proposalId": "p-3",
            "status": "applied", "via": "voice",
        }),
    );
    let cards = voice_view(&session).cards;
    assert_eq!(cards.len(), 1);
    assert!(cards[0].suggested, "the next is the first");
}

/// A viewer is offered no voice at all, and a card can never be confirmed by
/// a role that may not change the workspace.
#[test]
fn a_viewer_is_offered_no_voice() {
    let session = on_hq(viewer());
    let shown = view(&session);
    assert!(!shown.show_voice_start && !shown.can_start_voice);
    assert_eq!(shown.voice.line, None);
}

/// A build without calls says so instead of offering a session it could not
/// join.
#[test]
fn without_calls_it_says_it_cannot() {
    let shown = view(&on_hq(signed_in()));
    assert!(shown.show_voice_start && !shown.can_start_voice);
    assert_eq!(
        shown.voice.line.as_deref(),
        Some(cs::media_unavailable(l()))
    );
    assert!(!shown.voice.line_is_problem);
}

/// The session ends when the agent leaves: a problem line to dismiss, Start
/// voice back, and the card left to be clicked.
#[test]
fn the_agent_leaving_ends_the_session() {
    let session = agent_left(proposed(listening(with_calls()), "p-1"));
    let shown = view(&session);
    assert_eq!(shown.voice.stage, HqVoiceStage::Ended);
    assert_eq!(
        shown.voice.line.as_deref(),
        Some(cs::hq_voice_ended_agent(l()))
    );
    assert!(shown.voice.line_is_problem && shown.voice.can_dismiss);
    assert!(!shown.voice.can_stop && !shown.voice.show_microphone);
    assert!(shown.show_voice_start && shown.can_start_voice);
    assert_eq!(shown.voice.cards.len(), 1);
    let dismissed = session.ui(hq(HqAction::DismissVoiceNotice));
    assert_eq!(voice_view(&dismissed).line, None);
    assert!(!voice_view(&dismissed).can_dismiss);
}

/// A session that could not start says why, as a problem.
#[test]
fn a_session_that_could_not_start_says_why() {
    let session = issued(accepted(with_calls()), Err(server_error()));
    let shown = voice_view(&session);
    assert_eq!(shown.stage, HqVoiceStage::Failed);
    assert!(shown.line_is_problem && shown.can_dismiss && !shown.busy);
    assert!(shown.line.is_some());
}

/// The session goes on off District HQ: the dock holds its controls and the
/// changes left, and Review in District HQ goes back. Stop voice there ends
/// it, and the dock stays while a card is left to decide.
#[test]
fn the_dock_holds_the_session_off_district_hq() {
    let session = proposed(listening(with_calls()), "p-1");
    assert_eq!(dock(&session), None, "District HQ has its own controls");
    let session = session.ui(UiEvent::Navigate {
        destination: NavDestination::Calls,
    });
    assert!(
        !session
            .pending
            .iter()
            .any(|effect| matches!(effect, Effect::DisconnectMedia { .. })),
        "the session goes on"
    );
    let shown = dock(&session).expect("the dock");
    assert_eq!(shown.title, cs::hq_voice_dock_label(l()));
    assert_eq!(shown.review_label, cs::hq_voice_dock_review(l()));
    assert!(shown.can_review);
    assert_eq!(
        shown.voice.line.as_deref(),
        Some(cs::hq_voice_listening(l()))
    );
    assert!(shown.voice.can_stop && shown.voice.show_microphone);
    assert_eq!(shown.voice.cards.len(), 1);

    let session = session.ui(hq(HqAction::StopVoice));
    let shown = dock(&session).expect("the card is left to decide");
    assert!(!shown.voice.can_stop && shown.can_review);
    assert_eq!(shown.voice.stage, HqVoiceStage::Off);

    let back = session.ui(hq(HqAction::Open));
    assert_eq!(route(&back), Route::Hq);
    assert_eq!(dock(&back), None);
    assert_eq!(voice_view(&back).cards.len(), 1);
}

/// Off District HQ with the session stopped, a card cancelled from the dock
/// that the service no longer knows: the card goes, and a notice says why.
fn forgotten_off_hq() -> Session {
    proposed(listening(with_calls()), "p-1")
        .ui(UiEvent::Navigate {
            destination: NavDestination::Calls,
        })
        .ui(hq(HqAction::StopVoice))
        .ui(hq(HqAction::CancelVoiceCard {
            proposal_id: "p-1".to_owned(),
        }))
        .answer(
            |e| matches!(e, Effect::CancelHqVoice { .. }),
            |ticket| Event::HqVoiceCancelled {
                ticket,
                result: Err(ApiError::Server {
                    status: 404,
                    detail: district_api::ErrorDetail {
                        code: Some("not_found".to_owned()),
                        ..district_api::ErrorDetail::default()
                    },
                }),
            },
        )
}

/// In the dock, a card's notice (the service no longer knows the change) is
/// put away by its dismiss button, and with nothing left the dock goes.
#[test]
fn the_dock_dismisses_a_cards_notice() {
    let session = forgotten_off_hq();
    let shown = dock(&session).expect("the notice keeps the dock");
    assert!(shown.voice.notice.is_some() && shown.voice.can_dismiss);
    assert!(shown.voice.cards.is_empty() && !shown.can_review);
    // On District HQ the notice is apart, with its own dismiss button.
    let on_page = voice_view(&session.ui(hq(HqAction::Open)));
    assert!(on_page.notice.is_some() && !on_page.can_dismiss);
    let session = forgotten_off_hq().ui(hq(HqAction::DismissVoiceNotice));
    assert_eq!(dock(&session), None, "nothing left to show");
}

/// Nothing to show, no dock: before any session, and on District HQ.
#[test]
fn no_session_no_dock() {
    assert_eq!(dock(&with_calls()), None);
    assert_eq!(dock(&disclosing(with_calls())), None);
}
