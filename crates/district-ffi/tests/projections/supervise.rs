//! Supervising a live call (src/supervise.rs): the Live calls list, the card
//! on the call's page in each of its states, and what happens to every
//! request the card sends when the state it started from ends before the
//! answer lands.
//!
//! # When the state ends first
//!
//! The standing rule after district-android #117's live-microphone bug (a
//! takeover granted after the listening room ended joined the call with the
//! microphone live): for each request, each way its starting state can end,
//! one test. The fix column names the code that makes each case hold; a test
//! there fails without it.
//!
//! | Request | Ends first | What holds | Fix | Test |
//! |---|---|---|---|---|
//! | Listen | the call ends (read again as over) | the late credential joins nothing | core `supervised_call_reread` | `a_late_listen_credential_joins_nothing` |
//! | Listen | sign-out, the session ends, a workspace switch, the page left, app quit | as above | core `close_call` / `end_voice` (via `stop_supervising`) | `a_late_listen_credential_joins_nothing` |
//! | Listen | the window closes to the tray | as above | `SuperviseAction::End` (`end_events`: Stop listening) | `a_late_listen_credential_joins_nothing` |
//! | Take over (from idle) | every end above | the late credential joins nothing, no microphone | core, and `end_events`: the call log for a takeover with no room | `a_late_takeover_credential_joins_nothing` |
//! | Take over (while listening) | every end above, and the listening room dropping | the listening room is left, the late credential joins nothing | core `supervise_media_ended`, and `end_events`: Hand back | `a_late_takeover_credential_joins_nothing` |
//! | Whisper | the call ends, the room drops, the window closes, app quit | the answer lands, nothing is sent again, nothing stays sending | core `call_whispered` | `a_late_whisper_answer_is_never_sent_again` |
//! | Whisper | sign-out, the session ends, a workspace switch, the page left | the answer is dropped with the page, nothing is sent again | core `close_call` (cancels the whisper's slot) | `a_late_whisper_answer_is_never_sent_again` |
//! | Hand back | Listen again before the old room's last reports | the old room's reports change nothing | core `media_update` (session ids) | `a_left_rooms_late_reports_change_nothing` |
//! | List re-read | a workspace switch, sign-out, the session ends | the late page is dropped: no other workspace's live call shows | core `close_screens` / `cancel_all` | `a_late_call_log_page_shows_no_live_call` |
//! | List re-read | the call ends | the call leaves the Live calls list | `live_calls_view` | `a_call_that_ends_leaves_the_live_list` |
//!
//! Every end also ends a session that is under way, with no answer waiting:
//! `every_end_leaves_the_room_and_closes_the_microphone`.

use district_api::UnauthorizedReason;
use district_core::{CoreConfig, DisconnectReason, MediaEvent, MediaUpdate, MicrophoneState};
use district_ffi::supervise::{MicrophoneView, SuperviseAction, SuperviseState, SuperviseView};
use district_ffi::{CallDetailView, CallsView};
use district_model::WorkspaceListResponse;

use super::calls::{call_loaded, calls, calls_page, open_call};
use super::*;

/// A live call's id: no `_`, so the supervise routes take it as a call's.
const LIVE: &str = "CA0123456789live";

/// What the scenes here run in: a build with calls.
fn with_calls() -> CoreConfig {
    CoreConfig {
        calls_available: true,
        ..config()
    }
}

/// The two workspaces a switch moves between, as their agency.
fn two_workspaces() -> WorkspaceListResponse {
    contracts::decode(
        "two workspaces",
        json!({
            "success": true,
            "workspaces": [
                {"id": "ws-1", "name": "Example Dental", "region": "us", "role": "agency",
                 "subscriptionTier": "VoicePro"},
                {"id": "ws-2", "name": "Bravo Client", "region": "us", "role": "agency",
                 "subscriptionTier": "VoicePro"}
            ],
            "total": 2, "limit": 100, "offset": 0, "degradedRegions": [],
            "inactiveCount": 0, "defaultWorkspaceId": "ws-1"
        }),
    )
}

/// Signed in to two workspaces, in a build with calls.
fn agency() -> Session {
    signed_in_as(with_calls(), Ok(two_workspaces()))
}

/// The contract list's viewer, in a build with calls.
fn viewer_with_calls() -> Session {
    signed_in_as(
        with_calls(),
        Ok(contracts::read("district-workspace-list.json")),
    )
}

/// The detail fixture as a call under way, `status`, with the receptionist
/// on it.
fn live_call(status: &str) -> Value {
    let mut call = contracts::json("district-call-detail.json");
    call["call"]["id"] = json!(LIVE);
    call["call"]["status"] = json!(status);
    call["call"]["receptionistOnCall"] = json!(true);
    call
}

/// The live call's page, read.
fn on_live_call(session: Session) -> Session {
    call_loaded(open_call(session, LIVE), live_call("in-progress"))
}

fn supervise(session: Session, action: SuperviseAction) -> Session {
    session.ui(UiEvent::Supervise { action })
}

/// The newest media session the core asked the engine to join.
fn media_session(session: &Session) -> Ticket {
    session
        .pending
        .iter()
        .rev()
        .find_map(|effect| match effect {
            Effect::ConnectMedia { session, .. } => Some(*session),
            _ => None,
        })
        .expect("a room was joined")
}

/// What the engine reports on the newest session.
fn media(session: Session, event: MediaEvent) -> Session {
    let id = media_session(&session);
    session.send(Event::Media(MediaUpdate { session: id, event }))
}

/// Listen pressed: the credential asked for.
fn listen(session: Session) -> Session {
    supervise(session, SuperviseAction::Listen)
}

/// The listen-only credential's answer.
fn listen_granted(session: Session) -> Session {
    session.answer(
        |e| matches!(e, Effect::RequestCallMonitor { .. }),
        |ticket| Event::CallMonitorIssued {
            ticket,
            result: Ok(contracts::read("district-call-monitor-token.json")),
        },
    )
}

/// In the call's room, listening.
fn listening(session: Session) -> Session {
    media(listen_granted(listen(session)), MediaEvent::Connected)
}

/// Take over confirmed: the takeover credential asked for.
fn take_over(session: Session) -> Session {
    let session = supervise(session, SuperviseAction::TakeOver);
    supervise(session, SuperviseAction::ConfirmTakeOver)
}

/// The takeover credential's answer.
fn takeover_granted(session: Session) -> Session {
    session.answer(
        |e| matches!(e, Effect::RequestCallTakeover { .. }),
        |ticket| Event::CallTakeoverIssued {
            ticket,
            result: Ok(contracts::read("district-call-takeover.json")),
        },
    )
}

/// On the call, with the microphone the engine reports on.
fn on_call(session: Session) -> Session {
    let session = media(takeover_granted(take_over(session)), MediaEvent::Connected);
    media(session, MediaEvent::Microphone(MicrophoneState::On))
}

/// A line of the call, as the receptionist sends it.
fn line(session: Session, role: &str, speaker: &str, text: &str) -> Session {
    let payload =
        json!({"type": "TRANSCRIPT_CHUNK", "role": role, "speaker": speaker, "text": text});
    media(
        session,
        MediaEvent::AgentData {
            topic: String::new(),
            payload: payload.to_string().into_bytes(),
        },
    )
}

/// The Live calls list's fixture: one call under way over the log's calls.
fn log_with_live_call() -> Value {
    let mut rows = contracts::json("district-calls.json");
    let mut live = rows[0].clone();
    live["id"] = json!(LIVE);
    live["status"] = json!("in-progress");
    rows.as_array_mut().unwrap().insert(0, live);
    rows
}

/// This area's snapshot cases.
pub(crate) fn cases() -> Vec<Case> {
    let live = || on_live_call(agency());
    vec![
        (
            "supervise-live-calls",
            calls_page(calls(agency()), log_with_live_call()),
        ),
        ("supervise-idle", live()),
        ("supervise-viewer", on_live_call(viewer_with_calls())),
        ("supervise-connecting", listen(live())),
        (
            "supervise-listening",
            line(
                line(
                    listening(live()),
                    "user",
                    "+14165550142",
                    "Do you have Thursday?",
                ),
                "assistant",
                "Receptionist",
                "Let me check.",
            ),
        ),
        (
            "supervise-confirming",
            supervise(listening(live()), SuperviseAction::TakeOver),
        ),
        ("supervise-taking-over", take_over(listening(live()))),
        ("supervise-on-call", on_call(listening(live()))),
        (
            "supervise-on-call-muted",
            media(
                on_call(listening(live())).ui(UiEvent::Microphone { on: false }),
                MediaEvent::Microphone(MicrophoneState::Off),
            ),
        ),
        (
            "supervise-reconnecting",
            media(on_call(listening(live())), MediaEvent::Reconnecting),
        ),
        (
            "supervise-microphone-blocked",
            media(
                on_call(listening(live())),
                MediaEvent::Microphone(MicrophoneState::Unavailable),
            ),
        ),
        (
            "supervise-ended",
            media(
                listening(live()),
                MediaEvent::Disconnected(DisconnectReason::RoomEnded),
            ),
        ),
        (
            "supervise-joined-elsewhere",
            media(
                listening(live()),
                MediaEvent::Disconnected(DisconnectReason::JoinedElsewhere),
            ),
        ),
        (
            "supervise-whisper-sending",
            supervise(
                supervise(
                    live(),
                    SuperviseAction::EditWhisper {
                        text: "Offer Thursday at 10.".to_owned(),
                    },
                ),
                SuperviseAction::SendWhisper,
            ),
        ),
    ]
}

/// The card on the call's page, if the page shows one.
fn card(session: &Session) -> Option<SuperviseView> {
    match screen_view(&session.model) {
        ScreenView::CallDetail {
            view: CallDetailView { supervise, .. },
        } => supervise,
        _ => None,
    }
}

/// The card, which must be showing.
fn shown(session: &Session) -> SuperviseView {
    card(session).expect("the call's page shows its supervise card")
}

/// The calls page, which must be showing.
fn calls_view(session: &Session) -> CallsView {
    match screen_view(&session.model) {
        ScreenView::Calls { view } => view,
        other => panic!("not the calls page: {other:?}"),
    }
}

/// The supervise session's room, while one is held.
fn supervise_room(session: &Session) -> bool {
    match session.model.session() {
        SessionState::SignedIn(signed_in) => signed_in.supervise_session().is_some(),
        _ => false,
    }
}

/// The ways the state a request started from can end before its answer.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
enum End {
    /// The open call read again, and over.
    CallEnds,
    /// The supervise room dropped under the session (only while one is held).
    RoomDrops,
    /// The member signed out.
    SignOut,
    /// The session ended on the server: a read refused as signed out.
    SessionEnds,
    /// Another workspace opened from the switcher.
    WorkspaceSwitch,
    /// The window closed to the tray.
    WindowCloses,
    /// The member left the call's page.
    PageLeft,
    /// The app quit.
    AppQuits,
}

const EVERY_END: [End; 8] = [
    End::CallEnds,
    End::RoomDrops,
    End::SignOut,
    End::SessionEnds,
    End::WorkspaceSwitch,
    End::WindowCloses,
    End::PageLeft,
    End::AppQuits,
];

/// The ends that need no room held.
const ENDS_WITHOUT_A_ROOM: [End; 7] = [
    End::CallEnds,
    End::SignOut,
    End::SessionEnds,
    End::WorkspaceSwitch,
    End::WindowCloses,
    End::PageLeft,
    End::AppQuits,
];

/// `end` happening to `session`.
fn end(session: Session, end: End) -> Session {
    match end {
        End::CallEnds => call_loaded(session.ui(UiEvent::Refresh), live_call("completed")),
        End::RoomDrops => media(
            session,
            MediaEvent::Disconnected(DisconnectReason::RoomEnded),
        ),
        End::SignOut => session.ui(UiEvent::SignOut),
        End::SessionEnds => session.ui(UiEvent::Refresh).answer(
            |e| matches!(e, Effect::LoadCall { .. }),
            |ticket| Event::CallLoaded {
                ticket,
                result: Err(ApiError::Unauthorized(UnauthorizedReason::SessionEnded)),
            },
        ),
        End::WorkspaceSwitch => session.ui(UiEvent::SelectWorkspace {
            workspace_id: "ws-2".to_owned(),
        }),
        End::WindowCloses => supervise(session, SuperviseAction::End),
        End::PageLeft => session.ui(UiEvent::Navigate {
            destination: NavDestination::Calls,
        }),
        End::AppQuits => session.send(Event::Quitting),
    }
}

/// The effects `event` asks for, the session moved on by it.
fn effects_of(session: Session, event: Event) -> (Session, Vec<Effect>) {
    let mut session = session;
    let effects = session.model.update(event);
    session.pending.extend(effects.clone());
    (session, effects)
}

/// The ticket of the newest pending effect `pick` finds, taken out.
fn take(session: &mut Session, pick: fn(&Effect) -> bool) -> Ticket {
    let index = session
        .pending
        .iter()
        .rposition(pick)
        .unwrap_or_else(|| panic!("not pending: {:?}", session.pending));
    session.pending.remove(index).ticket().expect("a ticket")
}

/// No room, no microphone, and nothing on the card says listening or on the
/// call.
fn nothing_live(session: &Session, what: &str) {
    assert!(!supervise_room(session), "{what}: a room is held");
    if let Some(view) = card(session) {
        assert!(
            !matches!(
                view.state,
                SuperviseState::Connecting
                    | SuperviseState::Listening
                    | SuperviseState::TakingOver
                    | SuperviseState::OnCall
                    | SuperviseState::Reconnecting
            ),
            "{what}: the card says {:?}",
            view.state
        );
        assert_eq!(view.microphone, MicrophoneView::None, "{what}");
    }
}

/// Whether `effects` join a room or turn a microphone on.
fn joins(effects: &[Effect]) -> bool {
    effects.iter().any(|effect| {
        matches!(
            effect,
            Effect::ConnectMedia { .. } | Effect::SetMicrophone { enabled: true, .. }
        )
    })
}

#[test]
fn a_late_listen_credential_joins_nothing() {
    for when in ENDS_WITHOUT_A_ROOM {
        let mut session = listen(on_live_call(agency()));
        let ticket = take(&mut session, |e| {
            matches!(e, Effect::RequestCallMonitor { .. })
        });
        let session = end(session, when);
        let (session, effects) = effects_of(
            session,
            Event::CallMonitorIssued {
                ticket,
                result: Ok(contracts::read("district-call-monitor-token.json")),
            },
        );
        assert!(!joins(&effects), "{when:?}: {effects:?}");
        nothing_live(&session, &format!("{when:?}"));
    }
}

#[test]
fn a_late_takeover_credential_joins_nothing() {
    let from_idle = ENDS_WITHOUT_A_ROOM.map(|when| (false, when));
    let while_listening = EVERY_END.map(|when| (true, when));
    for (was_listening, when) in from_idle.into_iter().chain(while_listening) {
        let start = on_live_call(agency());
        let start = if was_listening {
            listening(start)
        } else {
            start
        };
        let listening_room = was_listening.then(|| media_session(&start));
        let mut session = take_over(start);
        let ticket = take(&mut session, |e| {
            matches!(e, Effect::RequestCallTakeover { .. })
        });
        let before = session.pending.len();
        let session = end(session, when);
        // A room held is left by the end itself, unless it is what ended.
        if let (Some(room), false) = (listening_room, matches!(when, End::RoomDrops)) {
            assert!(
                session.pending[before..].iter().any(
                    |effect| matches!(effect, Effect::DisconnectMedia { session } if *session == room)
                ),
                "{when:?}: the listening room is still held: {:?}",
                &session.pending[before..]
            );
        }
        let (session, effects) = effects_of(
            session,
            Event::CallTakeoverIssued {
                ticket,
                result: Ok(contracts::read("district-call-takeover.json")),
            },
        );
        assert!(
            !joins(&effects),
            "{when:?} (listening: {was_listening}): {effects:?}"
        );
        nothing_live(&session, &format!("{when:?} (listening: {was_listening})"));
    }
}

#[test]
fn a_late_whisper_answer_is_never_sent_again() {
    let landing = [
        End::CallEnds,
        End::RoomDrops,
        End::WindowCloses,
        End::AppQuits,
    ];
    for when in EVERY_END {
        let start = listening(on_live_call(agency()));
        let start = supervise(
            start,
            SuperviseAction::EditWhisper {
                text: "Offer Thursday at 10.".to_owned(),
            },
        );
        let mut session = supervise(start, SuperviseAction::SendWhisper);
        let ticket = take(&mut session, |e| {
            matches!(e, Effect::SendCallWhisper { .. })
        });
        let session = end(session, when);
        let (session, effects) = effects_of(
            session,
            Event::CallWhispered {
                ticket,
                result: Ok(contracts::read("district-call-whisper.json")),
            },
        );
        assert!(
            !effects
                .iter()
                .chain(&session.pending)
                .any(|effect| matches!(effect, Effect::SendCallWhisper { .. })),
            "{when:?}: sent again"
        );
        nothing_live(&session, &format!("{when:?}"));
        if landing.contains(&when) {
            let view = shown(&session);
            assert!(!view.whisper.sending, "{when:?}: still sending");
            assert_eq!(view.whisper.failure, None, "{when:?}");
        } else {
            assert!(
                card(&session).is_none(),
                "{when:?}: a card outlived its page"
            );
        }
    }
}

#[test]
fn a_left_rooms_late_reports_change_nothing() {
    let session = on_call(listening(on_live_call(agency())));
    let old = media_session(&session);
    let session = supervise(session, SuperviseAction::HandBack);
    assert!(
        session
            .pending
            .iter()
            .any(|effect| matches!(effect, Effect::DisconnectMedia { session } if *session == old)),
        "hand back leaves the room"
    );
    assert_eq!(shown(&session).state, SuperviseState::Idle);
    let session = listening(session);
    assert_ne!(media_session(&session), old);
    let mut late = session;
    for event in [
        MediaEvent::Connected,
        MediaEvent::Microphone(MicrophoneState::On),
        MediaEvent::Disconnected(DisconnectReason::RoomEnded),
    ] {
        let effects = late.model.update(Event::Media(MediaUpdate {
            session: old,
            event,
        }));
        assert!(effects.is_empty(), "{effects:?}");
    }
    let view = shown(&late);
    assert_eq!(view.state, SuperviseState::Listening);
    assert_eq!(view.microphone, MicrophoneView::None);
}

#[test]
fn a_late_call_log_page_shows_no_live_call() {
    for when in [End::WorkspaceSwitch, End::SignOut, End::SessionEnds] {
        let session = calls_page(calls(agency()), contracts::json("district-calls.json"));
        let mut session = session.ui(UiEvent::Refresh);
        let ticket = take(&mut session, |e| matches!(e, Effect::LoadCalls { .. }));
        let session = match when {
            // The calls page has no call open to read again: the call log's
            // own read is refused.
            End::SessionEnds => session.ui(UiEvent::Refresh).answer(
                |e| matches!(e, Effect::LoadCalls { .. }),
                |ticket| Event::CallsLoaded {
                    ticket,
                    result: Err(ApiError::Unauthorized(UnauthorizedReason::SessionEnded)),
                },
            ),
            other => end(session, other),
        };
        let (session, _) = effects_of(
            session,
            Event::CallsLoaded {
                ticket,
                result: Ok(contracts::decode("calls", log_with_live_call())),
            },
        );
        if let ScreenView::Calls { view } = screen_view(&session.model) {
            assert_eq!(
                view.live, None,
                "{when:?}: another workspace's call is live"
            );
        }
    }
}

#[test]
fn a_call_that_ends_leaves_the_live_list() {
    let session = calls_page(calls(agency()), log_with_live_call());
    let live = calls_view(&session).live.expect("the live list");
    assert_eq!(live.rows.len(), 1);
    assert_eq!(live.rows[0].call_id, LIVE);
    let session = calls_page(
        session.ui(UiEvent::Refresh),
        contracts::json("district-calls.json"),
    );
    assert_eq!(calls_view(&session).live, None);
}

#[test]
fn every_end_leaves_the_room_and_closes_the_microphone() {
    for when in EVERY_END {
        for taken_over in [false, true] {
            let session = listening(on_live_call(agency()));
            let session = if taken_over {
                on_call(session)
            } else {
                session
            };
            let room = media_session(&session);
            let before = session.pending.len();
            let session = end(session, when);
            nothing_live(&session, &format!("{when:?} (on the call: {taken_over})"));
            if !matches!(when, End::RoomDrops) {
                assert!(
                    session.pending[before..].iter().any(
                        |effect| matches!(effect, Effect::DisconnectMedia { session } if *session == room)
                    ),
                    "{when:?} (on the call: {taken_over}): the room is still held"
                );
            }
        }
    }
}

#[test]
fn closing_the_window_with_nothing_under_way_changes_nothing() {
    // No call open, an idle card, and a session that already ended.
    for session in [
        agency(),
        on_live_call(agency()),
        media(
            listening(on_live_call(agency())),
            MediaEvent::Disconnected(DisconnectReason::RoomEnded),
        ),
    ] {
        let events = UiEvent::Supervise {
            action: SuperviseAction::End,
        }
        .events_in(&session.model);
        assert!(events.is_empty(), "{events:?}");
    }
    // Signed out, there is no session to end.
    let events = UiEvent::Supervise {
        action: SuperviseAction::End,
    }
    .events_in(&signed_out());
    assert!(events.is_empty(), "{events:?}");
    // And the action alone, without the model, is no event.
    assert!(
        UiEvent::Supervise {
            action: SuperviseAction::End
        }
        .events()
        .is_empty()
    );
}

#[test]
fn closing_the_window_while_taking_over_with_no_room_leaves_the_page() {
    let session = take_over(on_live_call(agency()));
    assert_eq!(
        UiEvent::Supervise {
            action: SuperviseAction::End
        }
        .events_in(&session.model),
        vec![Event::Navigate(Route::Calls)]
    );
    let session = take_over(listening(on_live_call(agency())));
    assert_eq!(
        UiEvent::Supervise {
            action: SuperviseAction::End
        }
        .events_in(&session.model),
        vec![Event::Supervise(district_core::SuperviseEvent::HandBack)]
    );
}

#[test]
fn the_card_shows_only_on_a_call_under_way_or_one_that_ended_with_a_session() {
    // A call that is over has no card, and the call log has no live list.
    let over = call_loaded(open_call(agency(), LIVE), live_call("completed"));
    assert_eq!(card(&over), None);
    // A call id the routes would read as another room's is offered nothing.
    let mut call = live_call("in-progress");
    call["call"]["id"] = json!("call_contract_answered");
    let underscored = call_loaded(open_call(agency(), "call_contract_answered"), call);
    assert_eq!(card(&underscored), None);
    // A call not read yet has no card.
    assert_eq!(card(&open_call(agency(), LIVE)), None);
    // The session ended with the call: the card says so, offering nothing.
    let ended = end(listening(on_live_call(agency())), End::CallEnds);
    let view = shown(&ended);
    assert_eq!(view.state, SuperviseState::Ended);
    assert_eq!(view.status.as_deref(), Some("The call has ended."));
    assert!(!view.can_listen && !view.can_take_over && !view.whisper.can_edit);
    // Dismissed, it goes.
    assert_eq!(card(&supervise(ended, SuperviseAction::Dismiss)), None);
}

#[test]
fn listening_and_on_the_call_wait_for_the_room() {
    // The credential is not the room: still connecting.
    let session = listen_granted(listen(on_live_call(agency())));
    assert_eq!(shown(&session).state, SuperviseState::Connecting);
    assert_eq!(shown(&session).microphone, MicrophoneView::None);
    let session = media(session, MediaEvent::Connected);
    assert_eq!(shown(&session).state, SuperviseState::Listening);
    // The takeover's credential, and the room joined again with the
    // microphone, are not on the call until the engine says so.
    let session = takeover_granted(take_over(session));
    assert_eq!(shown(&session).state, SuperviseState::TakingOver);
    let session = media(session, MediaEvent::Connected);
    let view = shown(&session);
    assert_eq!(view.state, SuperviseState::OnCall);
    // Joined, the engine has not said the microphone is sending: it is off.
    assert_eq!(view.microphone, MicrophoneView::Off);
    let view = shown(&media(session, MediaEvent::Microphone(MicrophoneState::On)));
    assert_eq!(view.microphone, MicrophoneView::On);
    assert!(view.can_mute);
}

#[test]
fn the_microphone_shown_is_the_one_the_engine_reports() {
    let session = on_call(listening(on_live_call(agency())));
    // Mute asked for: the card still says on until the engine says off.
    let session = session.ui(UiEvent::Microphone { on: false });
    assert_eq!(shown(&session).microphone, MicrophoneView::On);
    let session = media(session, MediaEvent::Microphone(MicrophoneState::Off));
    let view = shown(&session);
    assert_eq!(view.microphone, MicrophoneView::Off);
    assert_eq!(
        view.microphone_line.as_deref(),
        Some("Your microphone is off. The caller does not hear you.")
    );
    // Listening holds no microphone, whatever the engine last said of one.
    let listening = media(
        listening(on_live_call(agency())),
        MediaEvent::Microphone(MicrophoneState::Off),
    );
    assert_eq!(shown(&listening).microphone, MicrophoneView::None);
    assert_eq!(shown(&listening).microphone_line, None);
    // A microphone the engine reports on is on, in any phase.
    let odd = media(
        listening(on_live_call(agency())),
        MediaEvent::Microphone(MicrophoneState::On),
    );
    assert_eq!(shown(&odd).microphone, MicrophoneView::On);
    // Blocked: no mute, and the card's own line, not the engine's notice.
    let blocked = media(
        on_call(listening(on_live_call(agency()))),
        MediaEvent::Microphone(MicrophoneState::Unavailable),
    );
    let view = shown(&blocked);
    assert_eq!(view.microphone, MicrophoneView::Blocked);
    assert!(!view.can_mute);
    assert_eq!(view.media_notice, None);
    assert_eq!(view.microphone_line, None);
    // Reconnecting, nothing can be muted, and the status says why.
    let view = shown(&media(
        on_call(listening(on_live_call(agency()))),
        MediaEvent::Reconnecting,
    ));
    assert_eq!(view.state, SuperviseState::Reconnecting);
    assert!(!view.can_mute);
    assert_eq!(
        view.status.as_deref(),
        Some("Reconnecting. The call is still here.")
    );
    assert_eq!(view.media_notice, None);
    // Media from someone that could not be decrypted is the card's notice.
    let view = shown(&media(
        on_call(listening(on_live_call(agency()))),
        MediaEvent::EncryptionFailed,
    ));
    assert!(view.media_notice.is_some());
}

#[test]
fn a_viewer_listens_and_does_nothing_else() {
    let view = shown(&on_live_call(viewer_with_calls()));
    assert!(view.can_listen);
    assert!(!view.can_supervise && !view.can_take_over);
    assert!(!view.whisper.can_edit && !view.whisper.can_send);
    assert_eq!(view.whisper.placeholder, "Viewers cannot send notes");
}

#[test]
fn a_note_waits_for_words_and_its_outcome_says_which_one() {
    let session = on_live_call(agency());
    assert!(
        !shown(&session).whisper.can_send,
        "a blank note sends nothing"
    );
    let session = supervise(
        session,
        SuperviseAction::EditWhisper {
            text: "  Offer Thursday at 10.  ".to_owned(),
        },
    );
    assert!(shown(&session).whisper.can_send);
    let session = supervise(session, SuperviseAction::SendWhisper);
    let view = shown(&session);
    assert!(view.whisper.sending && !view.whisper.can_send && view.whisper.can_edit);
    let session = session.answer(
        |e| matches!(e, Effect::SendCallWhisper { .. }),
        |ticket| Event::CallWhispered {
            ticket,
            result: Ok(contracts::read("district-call-whisper.json")),
        },
    );
    let mut envelope: district_model::TelemetryEnvelope =
        contracts::read("telemetry-event-whisper-applied.json");
    envelope.workspace_id = "ws-1".to_owned();
    envelope.call_id = LIVE.to_owned();
    let session = session.send(Event::Live(district_live::WorkspaceUpdate {
        workspace_id: "ws-1".to_owned(),
        update: district_live::LiveUpdate::Event(envelope),
    }));
    let view = shown(&session);
    assert_eq!(view.whisper.draft, "");
    assert_eq!(
        view.whisper.outcome.as_deref(),
        Some("Your receptionist used your note.")
    );
    assert_eq!(
        view.whisper.outcome_words.as_deref(),
        Some("Good afternoon, Northside Dental. How can I help?")
    );
    // An outcome with no words names none.
    let mut envelope: district_model::TelemetryEnvelope =
        contracts::read("telemetry-event-whisper-not-applied.json");
    envelope.workspace_id = "ws-1".to_owned();
    envelope.call_id = LIVE.to_owned();
    envelope.data["firstWords"] = json!("  ");
    let session = session.send(Event::Live(district_live::WorkspaceUpdate {
        workspace_id: "ws-1".to_owned(),
        update: district_live::LiveUpdate::Event(envelope),
    }));
    assert_eq!(shown(&session).whisper.outcome_words, None);
}

#[test]
fn the_card_speaks_french() {
    use district_l10n::{Locale, with_locale};
    let session = on_call(listening(on_live_call(agency())));
    let view = with_locale(Locale::FrCa, || shown(&session));
    assert_eq!(
        view.status.as_deref(),
        Some("Vous êtes en ligne avec l'appelant")
    );
    assert_eq!(
        view.microphone_line.as_deref(),
        Some("Votre micro est ouvert. L'appelant vous entend.")
    );
    assert_eq!(
        view.hand_back_label,
        "Redonner l'appel à votre réceptionniste"
    );
    let list = with_locale(Locale::FrCa, || {
        calls_view(&calls_page(calls(agency()), log_with_live_call()))
    })
    .live
    .expect("the live list");
    assert_eq!(list.title, "Appels en cours");
}
