//! What C# shows of calls: the dialler, the call under way and the call
//! ringing here, projected from the core's `SignedIn::{dialer, active_call,
//! ring, media}`.
//!
//! The words are the core's, as District AI for Linux shows them
//! (`pages/dialer.rs` and `pages/call_bar.rs`): nothing here writes a sentence
//! of its own. Each projection matches the core's enums exhaustively, with no
//! catch-all arm, so a core bump that adds a phase fails this crate's build.

use std::time::{Duration, SystemTime, UNIX_EPOCH};

use district_core::{
    ActiveCall, CallDirection, CallEnd, CallPhase, DialerScreen, FailureText, IncomingRing,
    MediaOwner, MediaSession, MicrophoneState, Notification, RingEnd, RingPhase, SignedIn,
    WorkspacesState,
};

/// Something that failed, as the screen says it. The contract's shared record;
/// it moves to the shared views module when the screens' projections land.
#[derive(Clone, Debug, PartialEq, Eq, serde::Serialize, uniffi::Record)]
pub struct FailureView {
    /// What went wrong, and what to do about it.
    pub message: String,
    /// "Affected regions: ..." when regions did not answer.
    pub regions_line: Option<String>,
    /// Whether trying again could help.
    pub retryable: bool,
}

impl From<&FailureText> for FailureView {
    fn from(failure: &FailureText) -> Self {
        Self {
            message: failure.message.clone(),
            regions_line: failure.regions_line(),
            retryable: failure.retryable,
        }
    }
}

/// The dialler: the number typed, how it reads, and whether Call works.
#[derive(Clone, Debug, PartialEq, Eq, serde::Serialize, uniffi::Record)]
pub struct DialerView {
    /// What the member typed, exactly as typed. The box shows this and is
    /// never rewritten to match [`formatted`](Self::formatted).
    pub number: String,
    /// Whether Call works: a role that may place calls, enough digits, and no
    /// call, meeting or ring in the way.
    pub can_dial: bool,
    /// The dial is on its way.
    pub dialing: bool,
    /// Why the last call could not be placed, in the service's words, while
    /// its summary is on screen.
    pub failure: Option<FailureView>,
    /// "Finish the call or meeting you are in first." while one is in the way.
    pub note: Option<String>,
    /// The number as it reads: its digits grouped, nothing removed.
    pub formatted: String,
    /// The line above the keypad.
    pub hint: String,
    /// "Placing a call turns on your microphone."
    pub microphone_note: String,
}

/// The phone call on this desktop, placed or answered, until its summary is
/// put away.
#[derive(Clone, Debug, PartialEq, Eq, serde::Serialize, uniffi::Record)]
pub struct ActiveCallView {
    /// The call's id. Always `None` in this version: core 1.2.0 keeps it
    /// private to the call, and nothing on screen needs it (hanging up and
    /// putting the summary away name no call).
    pub call_id: Option<String>,
    /// Who the call is with: the number dialled, grouped, or "Caller" for an
    /// answered call, whose caller nothing names.
    pub peer: String,
    /// Where it stands ("Placing the call.", "Connecting.", "Calling."), how
    /// long it has been answered (`mm:ss`), or how it ended.
    pub state_label: String,
    /// When it was answered, ISO 8601 UTC, while it is answered and not over,
    /// so C# can tick the duration between the core's own ticks.
    pub connected_at: Option<String>,
    /// The microphone is off: pressing the button turns it on.
    pub muted: bool,
    /// Whether Hang up is offered: while it is not over.
    pub can_hang_up: bool,
    /// How it ended, once it is over: the line to show, then Dismiss.
    pub ended: Option<String>,
    /// Why it was never placed or could not be connected, in the service's
    /// words.
    pub failure: Option<FailureView>,
    /// The microphone could not be used (no device, or Windows refused it), so
    /// nobody hears the member. C# shows "Allow microphone access in Windows
    /// Settings" with a link to `ms-settings:privacy-microphone`.
    pub microphone_denied: bool,
    /// Placed from the dialler, rather than rung here and answered.
    pub outbound: bool,
    /// Whether the microphone button works: the call's media is up and the
    /// call is not over.
    pub can_mute: bool,
    /// The note under an ended call that was answered: the call log is the
    /// record, and the length here is this app's own count.
    pub ended_note: Option<String>,
    /// The call's connection notice: reconnecting, audio that could not be
    /// decrypted, or a microphone that could not be used.
    pub media_notice: Option<String>,
}

/// A call ringing here, or the last ring's ending until it is put away.
#[derive(Clone, Debug, PartialEq, Eq, serde::Serialize, uniffi::Record)]
pub struct IncomingRingView {
    /// The call: what Answer and Decline name.
    pub call_id: String,
    /// The heading: "Incoming call", "Another call is ringing" or "Missed
    /// call", naming the workspace when it is not the one open ("Incoming call
    /// in Bravo Client"). Nothing names the caller: the ring carries ids only.
    pub caller: String,
    /// The line under it: where the call came from, or how the ring ended.
    pub detail: Option<String>,
    /// "Answering puts your microphone on this call." while it can be answered.
    pub note: Option<String>,
    /// Whether Answer is shown.
    pub show_answer: bool,
    /// Whether Answer works.
    pub can_answer: bool,
    /// Whether Decline works (it is shown while the ring is live).
    pub can_decline: bool,
    /// The answer is on its way.
    pub answering: bool,
    /// Ringing, waiting or being answered; false once it ended.
    pub live: bool,
    /// The ringtone is sounding: the strip shows it in the accent colour.
    pub sounding: bool,
    /// The name of the call's workspace, when it is not the one open.
    pub workspace_name: Option<String>,
}

/// The dialler of `signed_in`.
pub fn dialer_view(signed_in: &SignedIn) -> DialerView {
    let dialer = &signed_in.dialer;
    let call = signed_in
        .active_call
        .as_ref()
        .filter(|call| matches!(call.direction, CallDirection::Outbound { .. }));
    DialerView {
        number: dialer.entry.clone(),
        can_dial: signed_in.can_place_call(),
        dialing: call.is_some_and(|call| call.phase == CallPhase::Dialing),
        failure: call.and_then(|call| match &call.phase {
            CallPhase::Ended(CallEnd::NotPlaced(failure)) => Some(FailureView::from(failure)),
            CallPhase::Dialing
            | CallPhase::Connecting
            | CallPhase::Ringing
            | CallPhase::InCall
            | CallPhase::Ended(CallEnd::HungUp | CallEnd::Remote | CallEnd::Failed(_)) => None,
        }),
        note: signed_in
            .media_busy()
            .then(|| DialerScreen::BUSY_NOTE.to_owned()),
        formatted: dialer.formatted(),
        hint: DialerScreen::HINT.to_owned(),
        microphone_note: DialerScreen::MICROPHONE_NOTE.to_owned(),
    }
}

/// The call under way in `signed_in`, or its summary, as of `now`.
pub fn active_call_view(signed_in: &SignedIn, now: SystemTime) -> Option<ActiveCallView> {
    let call = signed_in.active_call.as_ref()?;
    let session = signed_in
        .media
        .as_ref()
        .filter(|media| media.owner == MediaOwner::Call);
    Some(call_view(call, session, now))
}

fn call_view(call: &ActiveCall, session: Option<&MediaSession>, now: SystemTime) -> ActiveCallView {
    let over = call.is_over();
    let microphone = session.map_or(MicrophoneState::Off, |session| session.microphone);
    let (muted, microphone_denied) = match microphone {
        MicrophoneState::On => (false, false),
        MicrophoneState::Off => (true, false),
        MicrophoneState::Unavailable => (true, true),
    };
    let (connected_at, failure) = match &call.phase {
        CallPhase::InCall => (
            Some(iso_utc(
                now.checked_sub(Duration::from_secs(call.elapsed_secs))
                    .unwrap_or(UNIX_EPOCH),
            )),
            None,
        ),
        CallPhase::Ended(CallEnd::NotPlaced(failure) | CallEnd::Failed(failure)) => {
            (None, Some(FailureView::from(failure)))
        }
        CallPhase::Dialing
        | CallPhase::Connecting
        | CallPhase::Ringing
        | CallPhase::Ended(CallEnd::HungUp | CallEnd::Remote) => (None, None),
    };
    ActiveCallView {
        call_id: None,
        peer: call.title(),
        state_label: call.status(),
        connected_at,
        muted,
        can_hang_up: !over,
        ended: over.then(|| call.status()),
        failure,
        microphone_denied,
        outbound: is_outbound(&call.direction),
        can_mute: !over && session.is_some(),
        ended_note: (over && call.was_answered()).then(|| ActiveCall::ENDED_NOTE.to_owned()),
        media_notice: session.and_then(MediaSession::notice).map(str::to_owned),
    }
}

/// Whether a call went out from the dialler.
fn is_outbound(direction: &CallDirection) -> bool {
    match direction {
        CallDirection::Outbound { .. } => true,
        CallDirection::Inbound => false,
    }
}

/// The call ringing here in `signed_in`, or the last ring's ending.
pub fn incoming_ring_view(signed_in: &SignedIn) -> Option<IncomingRingView> {
    let ring = signed_in.ring.ring.as_ref()?;
    let elsewhere = elsewhere(signed_in, &ring.workspace_id);
    Some(ring_view(ring, elsewhere))
}

fn ring_view(ring: &IncomingRing, elsewhere: Option<&str>) -> IncomingRingView {
    let title = match &ring.phase {
        RingPhase::Waiting => Notification::WAITING_TITLE,
        RingPhase::Ended(RingEnd::Missed) => Notification::MISSED_TITLE,
        RingPhase::Ringing
        | RingPhase::Answering
        | RingPhase::Ended(RingEnd::CallEnded | RingEnd::AnswerFailed(_)) => IncomingRing::TITLE,
    };
    IncomingRingView {
        call_id: ring.call_id.clone(),
        caller: match elsewhere {
            Some(workspace) => format!("{title} in {workspace}"),
            None => title.to_owned(),
        },
        detail: Some(ring.message()),
        note: ring
            .can_answer()
            .then(|| IncomingRing::MICROPHONE_NOTE.to_owned()),
        show_answer: matches!(ring.phase, RingPhase::Ringing | RingPhase::Answering),
        can_answer: ring.can_answer(),
        can_decline: ring.can_decline(),
        answering: ring.phase == RingPhase::Answering,
        live: ring.is_live(),
        sounding: ring.phase == RingPhase::Ringing,
        workspace_name: elsewhere.map(str::to_owned),
    }
}

/// The name of the workspace `workspace_id`, when it is not the one open. `None`
/// for the open one, before the list is read, and for one the list no longer
/// holds.
fn elsewhere<'a>(signed_in: &'a SignedIn, workspace_id: &str) -> Option<&'a str> {
    let WorkspacesState::Ready(workspaces) = &signed_in.workspaces else {
        return None;
    };
    if workspaces.active().id == workspace_id {
        return None;
    }
    workspaces
        .list
        .iter()
        .find(|entry| entry.id == workspace_id)
        .map(|entry| entry.name.as_str())
}

/// `time` as ISO 8601 UTC to the second: `2026-10-08T18:49:50Z`.
pub(crate) fn iso_utc(time: SystemTime) -> String {
    let secs = time
        .duration_since(UNIX_EPOCH)
        .map_or(0, |since| since.as_secs());
    let (days, rest) = (secs / 86_400, secs % 86_400);
    let (hour, minute, second) = (rest / 3600, rest % 3600 / 60, rest % 60);
    // Howard Hinnant's days-to-civil, for days since 1970-01-01 (never
    // negative here).
    let z = days + 719_468;
    let era = z / 146_097;
    let doe = z % 146_097;
    let yoe = (doe - doe / 1460 + doe / 36_524 - doe / 146_096) / 365;
    let doy = doe - (365 * yoe + yoe / 4 - yoe / 100);
    let mp = (5 * doy + 2) / 153;
    let day = doy - (153 * mp + 2) / 5 + 1;
    let month = if mp < 10 { mp + 3 } else { mp - 9 };
    let year = yoe + era * 400 + u64::from(month <= 2);
    format!("{year:04}-{month:02}-{day:02}T{hour:02}:{minute:02}:{second:02}Z")
}

#[cfg(test)]
mod tests {
    use district_api::{ApiError, RetryReason};
    use district_auth::AccessClaims;
    use district_core::{
        CallEvent, CoreConfig, DialerEvent, Effect, Event, MediaEvent, MediaUpdate, Model,
        SessionState, Ticket,
    };
    use district_model::{DialResponse, WorkspaceListResponse};
    use serde_json::json;

    use super::*;

    /// 2026-10-08T18:00:00Z.
    fn now() -> SystemTime {
        UNIX_EPOCH + Duration::from_secs(1_791_482_400)
    }

    /// A number in the fictional 555-01XX range.
    const NUMBER: &str = "+12125550142";

    /// Signed in, with calls available, as `role` in "Example Dental" (open)
    /// and "Bravo Client".
    fn signed_in_as(role: &str) -> Model {
        let (mut model, effects) = Model::new(CoreConfig {
            web_base_url: "https://www.distronode.com".to_owned(),
            app_version: "0.1.0".to_owned(),
            calls_available: true,
        });
        let ticket = ticket_in(effects);
        let effects = model.update(Event::SessionRestored {
            ticket,
            result: Ok(AccessClaims {
                user_id: "user-1".to_owned(),
                device_id: "device-windows-1".to_owned(),
                expires_at_secs: 4_000_000_000,
            }),
        });
        let ticket = ticket_in(effects);
        let list: WorkspaceListResponse = serde_json::from_value(json!({
            "success": true,
            "workspaces": [
                {"id": "ws-1", "name": "Example Dental", "region": "us", "role": role,
                 "subscriptionTier": "VoicePro"},
                {"id": "ws-2", "name": "Bravo Client", "region": "us", "role": role,
                 "subscriptionTier": "VoicePro"}
            ],
            "total": 2, "limit": 100, "offset": 0, "degradedRegions": [],
            "inactiveCount": 0, "defaultWorkspaceId": "ws-1"
        }))
        .unwrap();
        model.update(Event::WorkspacesLoaded {
            ticket,
            remembered: None,
            result: Ok(list),
        });
        model
    }

    fn signed_in(model: &Model) -> &SignedIn {
        let SessionState::SignedIn(signed_in) = model.session() else {
            panic!("not signed in")
        };
        signed_in
    }

    /// The ticket (or media session) of the first effect that carries one of
    /// those the tests answer.
    fn ticket_in(effects: Vec<Effect>) -> Ticket {
        effects
            .into_iter()
            .find_map(|effect| match effect {
                Effect::RestoreSession { ticket }
                | Effect::LoadWorkspaces { ticket }
                | Effect::Dial { ticket, .. } => Some(ticket),
                Effect::ConnectMedia { session, .. } => Some(session),
                _ => None,
            })
            .expect("the effect the test answers")
    }

    fn signed_in_mut(model: &Model) -> SignedIn {
        signed_in(model).clone()
    }

    /// The number typed and Call pressed: the dial's ticket.
    fn dial(model: &mut Model) -> Ticket {
        model.update(Event::Dialer(DialerEvent::Edit(NUMBER.to_owned())));
        ticket_in(model.update(Event::Dialer(DialerEvent::Dial)))
    }

    /// The dial answered, and the session the call's media joins as.
    fn dialled(model: &mut Model, ticket: Ticket) -> Ticket {
        let answer: DialResponse = serde_json::from_value(json!({
            "success": true,
            "callId": "CA-1",
            "roomName": "direct_ws-1-CA-1",
            "token": "jwt",
            "url": "wss://media.example.com"
        }))
        .unwrap();
        ticket_in(model.update(Event::Dialled {
            ticket,
            result: Ok(answer),
        }))
    }

    fn media(model: &mut Model, session: Ticket, event: MediaEvent) {
        model.update(Event::Media(MediaUpdate { session, event }));
    }

    #[test]
    fn the_dialler_reads_the_number_and_says_when_call_works() {
        let mut model = signed_in_as("agency");
        let view = dialer_view(signed_in(&model));
        assert_eq!(
            view,
            DialerView {
                number: String::new(),
                can_dial: false,
                dialing: false,
                failure: None,
                note: None,
                formatted: String::new(),
                hint: DialerScreen::HINT.to_owned(),
                microphone_note: DialerScreen::MICROPHONE_NOTE.to_owned(),
            }
        );
        model.update(Event::Dialer(DialerEvent::Edit(NUMBER.to_owned())));
        let view = dialer_view(signed_in(&model));
        assert_eq!(view.number, NUMBER);
        assert_eq!(view.formatted, "+1 212 555 0142");
        assert!(view.can_dial);
    }

    #[test]
    fn a_viewer_cannot_dial() {
        let mut model = signed_in_as("viewer");
        model.update(Event::Dialer(DialerEvent::Edit(NUMBER.to_owned())));
        assert!(!dialer_view(signed_in(&model)).can_dial);
    }

    #[test]
    fn a_placed_call_from_dial_to_answer_to_hang_up() {
        let mut model = signed_in_as("agency");
        assert_eq!(active_call_view(signed_in(&model), now()), None);

        let ticket = dial(&mut model);
        let dialer = dialer_view(signed_in(&model));
        assert!(dialer.dialing && !dialer.can_dial);
        assert_eq!(dialer.note.as_deref(), Some(DialerScreen::BUSY_NOTE));
        let view = active_call_view(signed_in(&model), now()).unwrap();
        assert_eq!(
            view,
            ActiveCallView {
                call_id: None,
                peer: "+1 212 555 0142".to_owned(),
                state_label: ActiveCall::DIALING.to_owned(),
                connected_at: None,
                muted: true,
                can_hang_up: true,
                ended: None,
                failure: None,
                microphone_denied: false,
                outbound: true,
                can_mute: false,
                ended_note: None,
                media_notice: None,
            }
        );

        let session = dialled(&mut model, ticket);
        assert_eq!(
            active_call_view(signed_in(&model), now())
                .unwrap()
                .state_label,
            ActiveCall::CONNECTING
        );
        media(&mut model, session, MediaEvent::Connected);
        media(
            &mut model,
            session,
            MediaEvent::Microphone(MicrophoneState::On),
        );
        let view = active_call_view(signed_in(&model), now()).unwrap();
        assert_eq!(view.state_label, ActiveCall::RINGING);
        assert!(view.can_mute && !view.muted);

        // The far end picks up: a person joins the room.
        media(
            &mut model,
            session,
            MediaEvent::ParticipantJoined(district_core::Participant {
                identity: "phone".to_owned(),
                name: None,
                is_agent: false,
                audio: true,
                video: false,
            }),
        );
        let view = active_call_view(signed_in(&model), now()).unwrap();
        assert_eq!(view.state_label, "00:00");
        assert_eq!(view.connected_at.as_deref(), Some("2026-10-08T18:00:00Z"));

        media(&mut model, session, MediaEvent::Reconnecting);
        let view = active_call_view(signed_in(&model), now()).unwrap();
        assert_eq!(
            view.media_notice.as_deref(),
            Some(MediaSession::RECONNECTING)
        );
        media(&mut model, session, MediaEvent::Connected);
        media(
            &mut model,
            session,
            MediaEvent::Microphone(MicrophoneState::Unavailable),
        );
        let view = active_call_view(signed_in(&model), now()).unwrap();
        assert!(view.microphone_denied && view.muted);
        assert_eq!(
            view.media_notice.as_deref(),
            Some(MediaSession::MICROPHONE_UNAVAILABLE)
        );
        media(
            &mut model,
            session,
            MediaEvent::Microphone(MicrophoneState::Off),
        );
        let view = active_call_view(signed_in(&model), now()).unwrap();
        assert!(view.muted && !view.microphone_denied);

        model.update(Event::Call(CallEvent::HangUp));
        let view = active_call_view(signed_in(&model), now()).unwrap();
        assert!(!view.can_hang_up && !view.can_mute);
        assert_eq!(view.connected_at, None);
        assert_eq!(view.ended.as_deref(), Some("Call ended. It lasted 00:00."));
        assert_eq!(view.ended_note.as_deref(), Some(ActiveCall::ENDED_NOTE));
        assert_eq!(view.failure, None);

        model.update(Event::Call(CallEvent::Dismiss));
        assert_eq!(active_call_view(signed_in(&model), now()), None);
    }

    #[test]
    fn a_refused_dial_says_why_on_the_call_and_the_dialler() {
        let mut model = signed_in_as("agency");
        let ticket = dial(&mut model);
        model.update(Event::Dialled {
            ticket,
            result: Err(ApiError::TokenUnavailable(RetryReason::Offline)),
        });
        let view = active_call_view(signed_in(&model), now()).unwrap();
        let failure = view.failure.clone().expect("the refusal");
        assert_eq!(view.ended.as_deref(), Some(failure.message.as_str()));
        assert_eq!(view.ended_note, None);
        assert_eq!(dialer_view(signed_in(&model)).failure, Some(failure));
    }

    #[test]
    fn a_call_that_could_not_connect_reports_its_failure() {
        let mut model = signed_in_as("agency");
        let ticket = dial(&mut model);
        let session = dialled(&mut model, ticket);
        media(
            &mut model,
            session,
            MediaEvent::Disconnected(district_core::DisconnectReason::ConnectFailed),
        );
        let view = active_call_view(signed_in(&model), now()).unwrap();
        assert_eq!(
            view.failure.map(|failure| failure.message),
            view.ended.clone()
        );
        assert!(view.ended.is_some());
        // Not a refused dial, so the dialler says nothing of it.
        assert_eq!(dialer_view(signed_in(&model)).failure, None);
    }

    #[test]
    fn a_call_answered_long_ago_started_that_long_ago() {
        let mut model = signed_in_as("agency");
        let ticket = dial(&mut model);
        let session = dialled(&mut model, ticket);
        media(&mut model, session, MediaEvent::Connected);
        media(
            &mut model,
            session,
            MediaEvent::ParticipantJoined(district_core::Participant {
                identity: "phone".to_owned(),
                name: None,
                is_agent: false,
                audio: true,
                video: false,
            }),
        );
        let mut state = signed_in_mut(&model);
        let call = state.active_call.as_mut().unwrap();
        call.elapsed_secs = 3725;
        let view = active_call_view(&state, now()).unwrap();
        assert_eq!(view.state_label, "1:02:05");
        assert_eq!(view.connected_at.as_deref(), Some("2026-10-08T16:57:55Z"));
        // A clock before the epoch, or before the call, never underflows.
        let view = active_call_view(&state, UNIX_EPOCH).unwrap();
        assert_eq!(view.connected_at.as_deref(), Some("1970-01-01T00:00:00Z"));
    }

    /// The helper refuses a model that is not signed in, rather than
    /// projecting nothing.
    #[test]
    #[should_panic(expected = "not signed in")]
    fn the_helper_needs_a_signed_in_model() {
        let (model, _) = Model::new(CoreConfig {
            web_base_url: "https://www.distronode.com".to_owned(),
            app_version: "0.1.0".to_owned(),
            calls_available: true,
        });
        signed_in(&model);
    }

    #[test]
    fn an_answered_call_is_not_outbound() {
        assert!(!is_outbound(&CallDirection::Inbound));
        assert!(is_outbound(&CallDirection::Outbound {
            number: NUMBER.to_owned()
        }));
    }

    fn ring(workspace_id: &str, phase: RingPhase) -> IncomingRing {
        IncomingRing {
            workspace_id: workspace_id.to_owned(),
            call_id: "call-1".to_owned(),
            phase,
        }
    }

    fn with_ring(model: &Model, ring: IncomingRing) -> SignedIn {
        let mut state = signed_in_mut(model);
        state.ring.ring = Some(ring);
        state
    }

    #[test]
    fn a_ring_offers_what_its_phase_allows() {
        let model = signed_in_as("agency");
        assert_eq!(incoming_ring_view(signed_in(&model)), None);

        let state = with_ring(&model, ring("ws-1", RingPhase::Ringing));
        assert_eq!(
            incoming_ring_view(&state),
            Some(IncomingRingView {
                call_id: "call-1".to_owned(),
                caller: IncomingRing::TITLE.to_owned(),
                detail: Some(IncomingRing::BODY.to_owned()),
                note: Some(IncomingRing::MICROPHONE_NOTE.to_owned()),
                show_answer: true,
                can_answer: true,
                can_decline: true,
                answering: false,
                live: true,
                sounding: true,
                workspace_name: None,
            })
        );

        let view =
            incoming_ring_view(&with_ring(&model, ring("ws-1", RingPhase::Waiting))).unwrap();
        assert_eq!(view.caller, Notification::WAITING_TITLE);
        assert_eq!(view.detail.as_deref(), Some(IncomingRing::WAITING_BODY));
        assert!(!view.show_answer && !view.can_answer && view.can_decline && !view.sounding);

        let view =
            incoming_ring_view(&with_ring(&model, ring("ws-1", RingPhase::Answering))).unwrap();
        assert!(view.show_answer && !view.can_answer && !view.can_decline && view.answering);
        assert_eq!(view.note, None);

        let view = incoming_ring_view(&with_ring(
            &model,
            ring("ws-1", RingPhase::Ended(RingEnd::Missed)),
        ))
        .unwrap();
        assert_eq!(view.caller, Notification::MISSED_TITLE);
        assert_eq!(view.detail.as_deref(), Some(IncomingRing::MISSED));
        assert!(!view.live && !view.show_answer && !view.can_decline);

        let view = incoming_ring_view(&with_ring(
            &model,
            ring("ws-1", RingPhase::Ended(RingEnd::CallEnded)),
        ))
        .unwrap();
        assert_eq!(view.caller, IncomingRing::TITLE);
        assert_eq!(view.detail.as_deref(), Some(IncomingRing::CALL_ENDED));

        let failure = FailureText {
            message: "That call could not be answered.".to_owned(),
            degraded_regions: Vec::new(),
            session_ended: None,
            retryable: true,
        };
        let view = incoming_ring_view(&with_ring(
            &model,
            ring(
                "ws-1",
                RingPhase::Ended(RingEnd::AnswerFailed(failure.clone())),
            ),
        ))
        .unwrap();
        assert_eq!(view.detail, Some(failure.message));
    }

    #[test]
    fn a_ring_from_another_workspace_names_it() {
        let model = signed_in_as("agency");
        let view =
            incoming_ring_view(&with_ring(&model, ring("ws-2", RingPhase::Ringing))).unwrap();
        assert_eq!(view.caller, "Incoming call in Bravo Client");
        assert_eq!(view.workspace_name.as_deref(), Some("Bravo Client"));
        // A workspace the list no longer holds, and a list not read yet.
        let view =
            incoming_ring_view(&with_ring(&model, ring("ws-9", RingPhase::Ringing))).unwrap();
        assert_eq!(view.caller, IncomingRing::TITLE);
        let mut state = with_ring(&model, ring("ws-2", RingPhase::Ringing));
        state.workspaces = WorkspacesState::Loading;
        assert_eq!(incoming_ring_view(&state).unwrap().workspace_name, None);
    }

    #[test]
    fn times_read_as_iso_8601_utc() {
        assert_eq!(iso_utc(UNIX_EPOCH), "1970-01-01T00:00:00Z");
        assert_eq!(iso_utc(now()), "2026-10-08T18:00:00Z");
        // A leap day, and the last second of a century's leap year.
        assert_eq!(
            iso_utc(UNIX_EPOCH + Duration::from_secs(951_782_400)),
            "2000-02-29T00:00:00Z"
        );
        assert_eq!(
            iso_utc(UNIX_EPOCH + Duration::from_secs(978_307_199)),
            "2000-12-31T23:59:59Z"
        );
        assert_eq!(
            iso_utc(UNIX_EPOCH - Duration::from_secs(1)),
            "1970-01-01T00:00:00Z"
        );
    }
}
