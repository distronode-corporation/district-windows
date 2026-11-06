//! Supervising a live call from its call page: listening in, a private note
//! to the receptionist (a whisper), and taking the call over by voice, as the
//! core's `district_core::supervise` runs them (core 8.0.0).
//!
//! The rules are the core's, and nothing here relaxes one: every request is
//! sent once and never repeated; a call id the routes could read as another
//! kind of room (any `_`) is offered nothing; an answer without
//! `success: true`, or with a blank credential or server, is a failure; the
//! service works out who the member is from the session, so no identity is
//! sent. This module only says what the core says, so the page never shows
//! "listening" or "on the call" before the room is joined
//! ([`SuperviseState`] follows the core's phase, which moves only when the
//! engine reports the room joined), and the microphone it shows is the one
//! the engine reports ([`MicrophoneView`]), never one inferred from the phase.
//!
//! # The live calls
//!
//! The call log's calls in progress, newest first, are the Live calls list
//! ([`live_calls_view`]), as Android's is (district-android #117): the core
//! reads the log again whenever a call starts, changes or ends, so a call
//! leaves the list when it ends. Opening one shows its page, where the
//! controls are.
//!
//! # When a session ends
//!
//! The core ends a session when the member stops or hands back, when the call
//! ends or its room goes, when the page is left, the workspace is switched or
//! closed, at sign-out or when the session ends, and when the desktop sleeps
//! or the app quits; each forgets a credential still being asked for, so a
//! late answer joins nothing. The one end the core cannot see is the window
//! closing to the tray, which leaves no controls on screen: the window sends
//! [`SuperviseAction::End`] then, which [`end_events`] turns into whichever
//! core event ends the session in the state it is in.

use district_core::{
    CallLog, Event, MediaConnection, MediaSession, MicrophoneState, Model, Route, SessionState,
    SignedIn, SuperviseControls, SuperviseEvent, SupervisePhase, Supervision,
};
use serde::Serialize;

use crate::calls::{CallRowView, call_row};
use crate::l10n::{cs, l, s};
use crate::views::FailureView;

/// The status of a call under way, the only one supervised.
const IN_PROGRESS: &str = "in-progress";

/// Where supervising the open call stands, as the page shows it.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash, Serialize, uniffi::Enum)]
pub enum SuperviseState {
    /// Not in the call's room: Listen and Take over are offered.
    Idle,
    /// Asking for the listen-only credential, or joining the room with it.
    /// Never shown as listening until the engine says the room is joined.
    Connecting,
    /// In the room, listening. The caller does not hear the member.
    Listening,
    /// Asking for the takeover credential, or joining the room again with the
    /// microphone. Never shown as on the call until the room is joined.
    TakingOver,
    /// On the call: the receptionist has stepped back.
    OnCall,
    /// Listening or on the call, with the connection lost for a moment and
    /// being resumed.
    Reconnecting,
    /// The session ended without the member ending it: the call ended, or the
    /// connection went for good. [`SuperviseView::status`] says which.
    Ended,
}

/// The local microphone, as the engine last reported it on the supervise
/// session.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash, Serialize, uniffi::Enum)]
pub enum MicrophoneView {
    /// No session holds a microphone: not in the room, or listening (the
    /// listen-only credential publishes nothing).
    None,
    /// In the room with the microphone off: not published yet, or muted.
    Off,
    /// Sending: the caller hears the member.
    On,
    /// It could not be opened (no device, or Windows refused it): nobody hears
    /// the member. C# shows "Microphone blocked" with a link to
    /// `ms-settings:privacy-microphone`.
    Blocked,
}

/// One line of the call, as the receptionist's transcript gives it.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct SuperviseLineView {
    /// "Caller", or the receptionist's own name.
    pub speaker: String,
    /// What was said.
    pub text: String,
    /// Whether the caller said it.
    pub caller: bool,
}

/// The note to the receptionist.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct WhisperView {
    /// "Note to your receptionist".
    pub title: String,
    /// What is typed. C# writes its box from this only when the box is not
    /// focused, as for every other field the core holds.
    pub draft: String,
    /// The box's placeholder: an example, or why a note cannot be sent.
    pub placeholder: String,
    /// "Send note".
    pub send_label: String,
    /// Whether the box takes a note at all: a member whose role may send one,
    /// to a call with a receptionist on it, while not on the call themselves.
    pub can_edit: bool,
    /// Whether Send works: the core's `can_whisper`, and a draft that is not
    /// blank.
    pub can_send: bool,
    /// Whether a note is on its way. One at a time; it is never sent again by
    /// itself.
    pub sending: bool,
    /// Why the last note was not sent or not delivered. The draft is kept.
    pub failure: Option<FailureView>,
    /// What the receptionist did with the last note, once the service says.
    pub outcome: Option<String>,
    /// The first words of that note, to say which one.
    pub outcome_words: Option<String>,
}

/// The takeover confirmation.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct TakeOverConfirmView {
    /// "Take over this call?"
    pub title: String,
    /// What happens: the receptionist steps back and the microphone is live.
    pub body: String,
    /// "Take over".
    pub confirm: String,
    /// "Cancel".
    pub cancel: String,
}

/// Supervising the open call: the card on its page, while the call is in
/// progress or a session on it ended and says how.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct SuperviseView {
    /// Where it stands.
    pub state: SuperviseState,
    /// The line saying so ("Connecting you to the call.", "You are
    /// listening", "Reconnecting. The call is still here."), or `None` while
    /// idle.
    pub status: Option<String>,
    /// "Listen in".
    pub listen_title: String,
    /// "Hear the call as it happens. The caller does not hear you."
    pub listen_body: String,
    /// "Listen".
    pub listen_label: String,
    /// Whether Listen works.
    pub can_listen: bool,
    /// "Stop listening".
    pub stop_listening_label: String,
    /// Whether Stop listening works: listening, or connecting to.
    pub can_stop_listening: bool,
    /// Whether the member's role lets them whisper and take over (a viewer
    /// sees neither).
    pub can_supervise: bool,
    /// "Take over the call".
    pub take_over_label: String,
    /// Whether Take over the call works.
    pub can_take_over: bool,
    /// "Hand back to your receptionist".
    pub hand_back_label: String,
    /// Whether Hand back works.
    pub can_hand_back: bool,
    /// The confirmation, while it shows. Nothing is asked of the service
    /// until its Take over is pressed.
    pub confirm: Option<TakeOverConfirmView>,
    /// What being on the call means, while on it.
    pub on_call_body: Option<String>,
    /// Why the last listen or takeover did not happen.
    pub failure: Option<FailureView>,
    /// The note.
    pub whisper: WhisperView,
    /// "Live transcript".
    pub lines_heading: String,
    /// The call's lines heard in this session, oldest first.
    pub lines: Vec<SuperviseLineView>,
    /// What shows while listening or on the call and nothing has been heard
    /// yet.
    pub lines_waiting: Option<String>,
    /// The microphone, as the engine reports it.
    pub microphone: MicrophoneView,
    /// The line saying where the microphone stands, while in the room with
    /// one: whether the caller hears the member.
    pub microphone_line: Option<String>,
    /// Whether the mute button works: on the call and connected, with a
    /// microphone that could be opened.
    pub can_mute: bool,
    /// The connection's notice (audio that could not be decrypted), other
    /// than reconnecting, which is the [`status`](Self::status), and a
    /// blocked microphone, which is [`MicrophoneView::Blocked`].
    pub media_notice: Option<String>,
}

/// The Live calls list, over the call log: the calls under way.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct LiveCallsView {
    /// "Live calls".
    pub title: String,
    /// What can be done from one: listen, a note, take over.
    pub body: String,
    /// The calls under way, newest first. Never empty: with none, there is
    /// no list.
    pub rows: Vec<CallRowView>,
}

/// What the member does on the card. Each is one core event, except
/// [`End`](Self::End).
#[derive(Clone, Debug, PartialEq, Eq, uniffi::Enum)]
pub enum SuperviseAction {
    /// Listen in.
    Listen,
    /// Stop listening.
    StopListening,
    /// The note's text changed.
    EditWhisper {
        /// What the box holds now.
        text: String,
    },
    /// Send the note.
    SendWhisper,
    /// Take over the call: shows the confirmation, asks for nothing.
    TakeOver,
    /// Close the confirmation.
    CancelTakeOver,
    /// Take over, from the confirmation.
    ConfirmTakeOver,
    /// Hand the call back to the receptionist.
    HandBack,
    /// Put away a failure, or how the session ended.
    Dismiss,
    /// The window closed to the tray: end the session, whatever it is doing,
    /// so the microphone is never live without the controls that stop it on
    /// screen. Which core event that is depends on the state, so the actor
    /// asks [`end_events`] with the model in hand.
    End,
}

/// The core event of `action`. [`SuperviseAction::End`] has none of its own:
/// see [`end_events`].
pub(crate) fn events(action: SuperviseAction) -> Vec<Event> {
    let one = match action {
        SuperviseAction::Listen => SuperviseEvent::Listen,
        SuperviseAction::StopListening => SuperviseEvent::StopListening,
        SuperviseAction::EditWhisper { text } => SuperviseEvent::EditWhisper(text),
        SuperviseAction::SendWhisper => SuperviseEvent::SendWhisper,
        SuperviseAction::TakeOver => SuperviseEvent::TakeOver,
        SuperviseAction::CancelTakeOver => SuperviseEvent::CancelTakeOver,
        SuperviseAction::ConfirmTakeOver => SuperviseEvent::ConfirmTakeOver,
        SuperviseAction::HandBack => SuperviseEvent::HandBack,
        SuperviseAction::Dismiss => SuperviseEvent::Dismiss,
        SuperviseAction::End => return Vec::new(),
    };
    vec![Event::Supervise(one)]
}

/// The core events that end the open call's supervise session in `model`'s
/// state, for the window closing to the tray.
///
/// Listening or connecting to listen is Stop listening; on the call, or
/// taking over with the listening room still held, is Hand back. Each leaves
/// the room and forgets any credential being asked for. A takeover still
/// being asked for with no room held has no control of its own (the core
/// offers Hand back only once a room is held), and its credential would
/// otherwise join the call with the microphone on while the window is hidden:
/// only leaving the call's page forgets it, so the page goes back to the call
/// log. With no session, nothing.
pub(crate) fn end_events(model: &Model) -> Vec<Event> {
    let SessionState::SignedIn(signed_in) = model.session() else {
        return Vec::new();
    };
    let Some(supervision) = signed_in.supervision() else {
        return Vec::new();
    };
    let controls = signed_in.supervise_controls();
    let event = match supervision.phase {
        SupervisePhase::Idle | SupervisePhase::Ended(_) => return Vec::new(),
        SupervisePhase::Joining | SupervisePhase::Listening if controls.can_stop_listening => {
            Event::Supervise(SuperviseEvent::StopListening)
        }
        SupervisePhase::TakingOver | SupervisePhase::OnCall if controls.can_hand_back => {
            Event::Supervise(SuperviseEvent::HandBack)
        }
        // A takeover asked for with no room held, or a session the controls
        // no longer offer a way out of (the call read again as over between
        // two turns): closing the page ends it, as the core's
        // `stop_supervising` does for every state.
        SupervisePhase::Joining
        | SupervisePhase::Listening
        | SupervisePhase::TakingOver
        | SupervisePhase::OnCall => Event::Navigate(Route::Calls),
    };
    vec![event]
}

/// The card for the open call in `signed_in`, or `None` while there is
/// nothing to show: a call that is not under way, with no session on it that
/// ended and says how.
pub(crate) fn supervise_view(signed_in: &SignedIn) -> Option<SuperviseView> {
    let supervision = signed_in.supervision()?;
    let controls = signed_in.supervise_controls();
    let ended = matches!(supervision.phase, SupervisePhase::Ended(_));
    (controls.live || ended).then(|| {
        card(
            supervision,
            controls,
            signed_in.supervise_session(),
            signed_in.whisper_placeholder(),
        )
    })
}

/// The card for `supervision`, with what `controls` offer, the supervise
/// `session` when one is held, and the note's `placeholder`.
fn card(
    supervision: &Supervision,
    controls: SuperviseControls,
    session: Option<&MediaSession>,
    placeholder: &str,
) -> SuperviseView {
    let reconnecting =
        session.is_some_and(|session| session.connection == MediaConnection::Reconnecting);
    let phase = supervision.phase;
    let state = match phase {
        SupervisePhase::Idle => SuperviseState::Idle,
        SupervisePhase::Joining => SuperviseState::Connecting,
        SupervisePhase::Listening | SupervisePhase::OnCall if reconnecting => {
            SuperviseState::Reconnecting
        }
        SupervisePhase::Listening => SuperviseState::Listening,
        SupervisePhase::TakingOver => SuperviseState::TakingOver,
        SupervisePhase::OnCall => SuperviseState::OnCall,
        SupervisePhase::Ended(_) => SuperviseState::Ended,
    };
    let status = if state == SuperviseState::Reconnecting {
        Some(cs::media_reconnecting(l()).to_owned())
    } else {
        supervision.status().map(str::to_owned)
    };
    let microphone = microphone(supervision, session);
    let on_call = phase == SupervisePhase::OnCall;
    let draft_blank = supervision.whisper.draft.trim().is_empty();
    SuperviseView {
        state,
        status,
        listen_title: cs::supervise_listen_title(l()).to_owned(),
        listen_body: cs::supervise_listen_body(l()).to_owned(),
        listen_label: cs::supervise_listen_action(l()).to_owned(),
        can_listen: controls.can_listen,
        stop_listening_label: cs::supervise_stop_listening(l()).to_owned(),
        can_stop_listening: controls.can_stop_listening,
        can_supervise: controls.can_supervise,
        take_over_label: cs::supervise_take_over(l()).to_owned(),
        can_take_over: controls.can_take_over,
        hand_back_label: cs::supervise_hand_back(l()).to_owned(),
        can_hand_back: controls.can_hand_back,
        confirm: (supervision.confirming_takeover && controls.can_take_over).then(|| {
            TakeOverConfirmView {
                title: cs::supervise_take_over_confirm_title(l()).to_owned(),
                body: cs::supervise_take_over_confirm_body(l()).to_owned(),
                confirm: cs::supervise_take_over_confirm(l()).to_owned(),
                cancel: cs::supervise_take_over_cancel(l()).to_owned(),
            }
        }),
        on_call_body: on_call.then(|| cs::supervise_on_call_body(l()).to_owned()),
        failure: supervision.failure.as_ref().map(FailureView::from),
        whisper: WhisperView {
            title: cs::supervise_whisper_title(l()).to_owned(),
            draft: supervision.whisper.draft.clone(),
            placeholder: placeholder.to_owned(),
            send_label: cs::supervise_whisper_send(l()).to_owned(),
            // `can_whisper` without the note in flight: the box stays
            // editable while one is on its way.
            can_edit: controls.can_whisper || supervision.whisper.is_sending(),
            can_send: controls.can_whisper && !draft_blank,
            sending: supervision.whisper.is_sending(),
            failure: supervision.whisper.failure.as_ref().map(FailureView::from),
            outcome: supervision.whisper.outcome_line().map(str::to_owned),
            outcome_words: supervision
                .whisper
                .outcome
                .as_ref()
                .map(|outcome| outcome.first_words.trim().to_owned())
                .filter(|words| !words.is_empty()),
        },
        lines_heading: cs::transcript_heading(l()).to_owned(),
        lines: supervision
            .lines()
            .iter()
            .map(|line| SuperviseLineView {
                speaker: Supervision::speaker_label(line).to_owned(),
                text: line.text.clone(),
                caller: line.is_caller(),
            })
            .collect(),
        lines_waiting: (matches!(state, SuperviseState::Listening | SuperviseState::OnCall)
            && supervision.lines().is_empty())
        .then(|| cs::supervise_transcript_waiting(l()).to_owned()),
        microphone,
        microphone_line: match microphone {
            MicrophoneView::On => Some(s::supervise_microphone_on(l()).to_owned()),
            MicrophoneView::Off => Some(s::supervise_microphone_off(l()).to_owned()),
            MicrophoneView::None | MicrophoneView::Blocked => None,
        },
        can_mute: on_call
            && session.is_some_and(|session| {
                session.connection == MediaConnection::Connected
                    && session.microphone != MicrophoneState::Unavailable
            }),
        media_notice: session
            .filter(|_| !reconnecting && microphone != MicrophoneView::Blocked)
            .and_then(MediaSession::notice)
            .map(str::to_owned),
    }
}

/// The microphone as the engine reports it on the supervise session. A
/// listening session holds none: its credential publishes nothing, and the
/// core never turns its microphone on.
fn microphone(supervision: &Supervision, session: Option<&MediaSession>) -> MicrophoneView {
    let Some(session) = session else {
        return MicrophoneView::None;
    };
    let with_microphone = matches!(
        supervision.phase,
        SupervisePhase::TakingOver | SupervisePhase::OnCall
    );
    match session.microphone {
        // What the engine reports wins over the phase: a microphone on is on.
        MicrophoneState::On => MicrophoneView::On,
        MicrophoneState::Unavailable => MicrophoneView::Blocked,
        MicrophoneState::Off if with_microphone => MicrophoneView::Off,
        MicrophoneState::Off => MicrophoneView::None,
    }
}

/// The Live calls list: the call log's calls in progress, newest first, or
/// `None` when the log is not read or has none.
pub(crate) fn live_calls_view(log: &CallLog) -> Option<LiveCallsView> {
    let CallLog::Ready(page) = log else {
        return None;
    };
    let rows: Vec<CallRowView> = page
        .calls
        .iter()
        .filter(|call| call.status == IN_PROGRESS)
        .map(call_row)
        .collect();
    (!rows.is_empty()).then(|| LiveCallsView {
        title: s::supervise_live_calls_title(l()).to_owned(),
        body: s::supervise_live_calls_body(l()).to_owned(),
        rows,
    })
}
