//! District HQ, the workspace assistant: the conversation, the card in front
//! of every change the assistant proposes, and Report on each answer.
//!
//! The member's own words are shown as the text they are. The assistant's are
//! Markdown, projected as rich text ([`crate::rich_text`]), and a link in them
//! goes back to the core ([`HqAction::OpenLink`]), which opens only a web page.
//! A proposed change is shown by the service's own summary of it, and is made
//! only when the member presses Confirm, which only a role that may change the
//! workspace is offered. The words not in the core are the Linux app's
//! (`pages/hq.rs` and `hq-page.ui`).
//!
//! By voice, the member talks to the same assistant (district-core's
//! `hq_voice`): where their voice is processed is said before anything starts,
//! a line says what the assistant is doing, both sides' words join the
//! conversation as they are transcribed, and every change proposed by voice is
//! a card of its own, applied only by the member's spoken "confirm" (which the
//! assistant hears and acts on) or by a click on its Confirm. The session goes
//! on when the member leaves District HQ, so its controls and its cards go
//! with them: the voice dock ([`HqVoiceDockView`], in the window's frame) on
//! every other screen. As District AI for Linux shows them (`pages/hq.rs` and
//! `pages/voice_dock.rs`).

use district_core::FailureText;
use district_core::{
    Capabilities, Event, HqAuthor, HqCardState, HqEvent, HqMessage, HqNote, HqPhase, HqScreen,
    HqText, HqVoiceCard, HqVoiceControls, HqVoicePhase, MediaConnection, MediaSession,
    MicrophoneState, Model, Route, SignedIn,
};
use district_model::HqAgentState;
use serde::Serialize;

use crate::l10n::{cs, l, s};
pub use crate::rich_text::{RichBlock, RichRun, RichTextView};
use crate::screen::ScreenView;
use crate::views::{EmptyView, FailureView, ReportAvailability};

/// Whether this version has the area's screens.
pub(crate) const BUILT: bool = true;

/// The District HQ screen.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct HqView {
    /// The heading, `hq.title`.
    pub title: String,
    /// What an empty conversation says, while nothing has been asked.
    pub empty: Option<EmptyView>,
    /// The conversation, oldest first. It only grows while the workspace is
    /// open, so a page may keep what it drew and add the rest.
    pub messages: Vec<HqMessageView>,
    /// The line while a prompt is being answered (show a progress ring).
    pub thinking: Option<String>,
    /// Why the last prompt went unanswered. The question stays in the
    /// conversation.
    pub failure: Option<FailureView>,
    /// Whether "Try again" is offered for [`failure`](Self::failure): only when
    /// trying again could help.
    pub can_retry: bool,
    /// The card of a change the assistant proposed, while there is one.
    pub card: Option<HqCardView>,
    /// Whether a prompt can be sent now: not while one is being answered or a
    /// change applied.
    pub can_ask: bool,
    /// The prompt box's placeholder, `hq.prompt_placeholder`.
    pub prompt_placeholder: String,
    /// The send button's label, `hq.ask_action`.
    pub ask_label: String,
    /// The "Try again" button's label, `hq.retry_action`.
    pub retry_label: String,
    /// The line under the prompt box, `hq.footnote`.
    pub footnote: String,
    /// The voice session's controls and the changes proposed by voice.
    pub voice: HqVoiceView,
    /// Whether to show Start voice: to an agency or client member, while no
    /// session is being started or under way and the residency statement
    /// (with its own Start voice) is not up.
    pub show_voice_start: bool,
    /// Whether Start voice works now: no session, call, room or audition
    /// under way, in a build with calls.
    pub can_start_voice: bool,
    /// Start voice's label, `hq.voice_start`, beside a microphone icon.
    pub voice_start_label: String,
    /// Where the member's voice is processed, said before the first session
    /// in a workspace and before anything is asked for.
    pub voice_disclosure: Option<HqVoiceDisclosureView>,
}

/// Where a voice session stands: what the keyboard follows (Start voice in
/// the residency statement, Stop voice once it starts, back to the voice
/// button when it ends). One variant per core `HqVoicePhase`.
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq, Hash, Serialize, uniffi::Enum)]
pub enum HqVoiceStage {
    /// No session.
    #[default]
    Off,
    /// The residency statement is up, and nothing has been asked for.
    Disclosing,
    /// The session is being asked for and its room joined.
    Starting,
    /// The session is under way.
    Live,
    /// It ended without the member stopping it.
    Ended,
    /// It could not start.
    Failed,
}

/// A voice session's controls and the changes it proposed: on District HQ,
/// and in the voice dock on every other screen.
#[derive(Clone, Debug, Default, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct HqVoiceView {
    /// Where the session stands.
    pub stage: HqVoiceStage,
    /// What the session is doing ("Listening. Speak to District HQ."), or why
    /// it ended or could not start. Never Markdown.
    pub line: Option<String>,
    /// Whether [`line`](Self::line) is a problem (show it as an error) rather
    /// than news.
    pub line_is_problem: bool,
    /// Whether to show a progress ring by the line: while the session is
    /// asked for and the assistant has not joined.
    pub busy: bool,
    /// Whether Stop voice is shown: a session is being started or is under
    /// way.
    pub can_stop: bool,
    /// Stop voice's label, `hq.voice_stop`.
    pub stop_label: String,
    /// Whether the microphone button is shown: while the session's room is
    /// live.
    pub show_microphone: bool,
    /// Whether the microphone is off (or could not be used): pressing the
    /// button asks for it on (`UiEvent::Microphone`).
    pub muted: bool,
    /// Whether to offer dismissing what the line says: after the session
    /// ended or could not start, and in the dock, a card's notice too.
    pub can_dismiss: bool,
    /// Why the last card went without a result (the service no longer knows
    /// it, or refused it as asked), until dismissed.
    pub notice: Option<FailureView>,
    /// The changes proposed by voice and not yet resolved, oldest first.
    pub cards: Vec<HqVoiceCardView>,
}

/// The residency statement, before the first voice session in a workspace.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct HqVoiceDisclosureView {
    /// The heading, `hq.voice_disclosure_title`.
    pub title: String,
    /// For a workspace whose data lives in Canada, Europe or Asia-Pacific, the
    /// line above the statement: its data stays there, its voice
    /// conversations are processed in the United States. `None` for a United
    /// States workspace.
    pub region_notice: Option<String>,
    /// The statement, the web dashboard's word for word.
    pub body: String,
    /// The button that starts the session, `hq.voice_disclosure_accept`.
    pub accept_label: String,
    /// The button that closes it, `hq.voice_disclosure_decline`.
    pub decline_label: String,
}

/// A change proposed by voice, until the member resolves it, by voice or by
/// a click on Confirm or Cancel.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct HqVoiceCardView {
    /// The proposal: what Confirm and Cancel name.
    pub proposal_id: String,
    /// The heading, `hq.confirm_title`.
    pub title: String,
    /// The service's sentence saying what the change would do.
    pub summary: String,
    /// The note under it: what to do with it, or what is being done; after a
    /// confirmation that failed, that the change may have been made.
    pub note: Option<String>,
    /// Why the last click failed. The card stays, for the member to decide
    /// again.
    pub failure: Option<FailureView>,
    /// Whether a click is being sent for it (show a progress ring).
    pub busy: bool,
    /// Cancel's label, `hq.voice_cancel_action`.
    pub cancel_label: String,
    /// Confirm's label, `hq.confirm_action`.
    pub confirm_label: String,
    /// Whether Cancel works now: nothing is in flight for it.
    pub can_cancel: bool,
    /// Whether Confirm works now: nothing is in flight for it, and the member
    /// may change the workspace.
    pub can_confirm: bool,
    /// Whether its Confirm is the suggested action: the first card's only, so
    /// that one is on screen at a time.
    pub suggested: bool,
}

/// The voice dock: a voice session's controls and cards on every screen but
/// District HQ, which has its own. The microphone is never live without the
/// controls that stop it on screen.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct HqVoiceDockView {
    /// Its heading and accessible name, `hq.voice_dock_label`.
    pub title: String,
    /// "Review in District HQ", `hq.voice_dock_review`: opens the
    /// conversation ([`HqAction::Open`]).
    pub review_label: String,
    /// Whether to offer it: while changes proposed by voice are waiting.
    pub can_review: bool,
    /// The session's controls and cards, as on District HQ.
    pub voice: HqVoiceView,
}

/// One line of the conversation.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Enum)]
pub enum HqMessageView {
    /// What the member asked, as they typed it.
    Question {
        /// Where it is in the conversation, from 0.
        index: u32,
        /// The words.
        text: String,
    },
    /// What District HQ answered: AI-generated, so Report is offered on it.
    Answer {
        /// Where it is in the conversation, from 0.
        index: u32,
        /// The answer, as rich text.
        text: RichTextView,
        /// How to offer Report.
        report: ReportAvailability,
    },
    /// What became of a confirmed change, written by the app.
    Note {
        /// Where it is in the conversation, from 0.
        index: u32,
        /// The sentence.
        text: String,
        /// Whether it is good news (the change was made, or a change proposed
        /// by voice was cancelled as asked) rather than something to look at.
        applied: bool,
    },
    /// Words spoken in a voice session, as transcribed: plain text, never
    /// Markdown, which grows until it is final. A line is drawn again when
    /// its words change.
    Spoken {
        /// Where it is in the conversation, from 0.
        index: u32,
        /// The words so far.
        text: String,
        /// The member's words, rather than the assistant's.
        member: bool,
        /// Whether they are the line's last (show the rest dimmed).
        is_final: bool,
    },
}

/// The card in front of a change the assistant proposed.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct HqCardView {
    /// The heading.
    pub title: String,
    /// The service's one sentence saying exactly what the change would do.
    pub summary: String,
    /// The note under it: nothing has changed yet, only certain roles can
    /// confirm, or the change may have been made.
    pub note: Option<String>,
    /// The line while the confirmed change is being applied (show a progress
    /// ring).
    pub applying: Option<String>,
    /// Why the confirmation failed. Confirm stays, as the member's decision.
    pub failure: Option<FailureView>,
    /// The confirming button's label.
    pub confirm_label: String,
    /// The declining button's label.
    pub dismiss_label: String,
    /// Whether Confirm works now.
    pub can_confirm: bool,
    /// Whether Dismiss works now.
    pub can_dismiss: bool,
}

/// Something the member did on District HQ.
#[derive(Clone, Debug, PartialEq, Eq, uniffi::Enum)]
pub enum HqAction {
    /// Open District HQ.
    Open,
    /// Send a prompt. A blank one is not sent.
    Ask {
        /// The prompt, as typed.
        prompt: String,
    },
    /// Ask again, after a question that failed.
    Retry,
    /// Apply the proposed change.
    Confirm,
    /// Set the proposed change aside. Nothing is sent to the service.
    Dismiss,
    /// A link in an answer was clicked: the core opens it in the browser when
    /// it is a web page, and does nothing otherwise.
    OpenLink {
        /// The run's [`link`](RichRun::link).
        url: String,
    },
    /// Start voice: the residency statement the first time in a workspace,
    /// the session itself after that.
    StartVoice,
    /// Start voice under the residency statement: the session and the
    /// microphone start.
    AcceptVoiceDisclosure,
    /// Not now, under the residency statement: nothing starts.
    DeclineVoiceDisclosure,
    /// Stop voice, on District HQ or in the dock. Also what the window sends
    /// when it is closed to the tray, which leaves no controls on screen.
    StopVoice,
    /// Confirm a change proposed by voice.
    ConfirmVoiceCard {
        /// The card's [`proposal_id`](HqVoiceCardView::proposal_id).
        proposal_id: String,
    },
    /// Cancel a change proposed by voice.
    CancelVoiceCard {
        /// The card's [`proposal_id`](HqVoiceCardView::proposal_id).
        proposal_id: String,
    },
    /// Put away what the voice controls say: why a session ended or could not
    /// start, or why a card went.
    DismissVoiceNotice,
}

/// The core events `action` is, in the order the core is to hear them.
pub(crate) fn events(action: HqAction) -> Vec<Event> {
    vec![match action {
        HqAction::Open => Event::Navigate(Route::Hq),
        HqAction::Ask { prompt } => Event::Hq(HqEvent::Ask(prompt)),
        HqAction::Retry => Event::Hq(HqEvent::Retry),
        HqAction::Confirm => Event::Hq(HqEvent::Confirm),
        HqAction::Dismiss => Event::Hq(HqEvent::Dismiss),
        HqAction::OpenLink { url } => Event::Hq(HqEvent::OpenLink(url)),
        HqAction::StartVoice => Event::Hq(HqEvent::StartVoice),
        HqAction::AcceptVoiceDisclosure => Event::Hq(HqEvent::AcceptVoiceDisclosure),
        HqAction::DeclineVoiceDisclosure => Event::Hq(HqEvent::DeclineVoiceDisclosure),
        HqAction::StopVoice => Event::Hq(HqEvent::StopVoice),
        HqAction::ConfirmVoiceCard { proposal_id } => {
            Event::Hq(HqEvent::ConfirmVoiceCard(proposal_id))
        }
        HqAction::CancelVoiceCard { proposal_id } => {
            Event::Hq(HqEvent::CancelVoiceCard(proposal_id))
        }
        HqAction::DismissVoiceNotice => Event::Hq(HqEvent::DismissVoiceNotice),
    }]
}

/// The page of District HQ, for a signed-in model.
pub(crate) fn screen(_model: &Model, signed_in: &SignedIn) -> ScreenView {
    ScreenView::Hq {
        view: hq_view(
            &signed_in.hq,
            &signed_in.capabilities(),
            voice_page(signed_in),
        ),
    }
}

/// The voice dock of `signed_in`: away from District HQ, while a session is
/// being started or is under way, has ended with something to say, or left
/// changes to decide, or a card left a notice. `None` otherwise.
pub(crate) fn voice_dock_view(signed_in: &SignedIn) -> Option<HqVoiceDockView> {
    let voice = &signed_in.hq.voice;
    let docked = signed_in.route != Route::Hq
        && (!matches!(voice.phase, HqVoicePhase::Off | HqVoicePhase::Disclosing)
            || !voice.cards.is_empty()
            || voice.notice.is_some());
    docked.then(|| HqVoiceDockView {
        title: cs::hq_voice_dock_label(l()).to_owned(),
        review_label: cs::hq_voice_dock_review(l()).to_owned(),
        can_review: !voice.cards.is_empty(),
        voice: voice_view(signed_in, true),
    })
}

/// What District HQ shows of the voice session, besides what the dock shows
/// too.
#[derive(Default)]
struct VoicePage {
    voice: HqVoiceView,
    show_start: bool,
    can_start: bool,
    disclosure: Option<HqVoiceDisclosureView>,
}

/// District HQ's voice controls in `signed_in`.
fn voice_page(signed_in: &SignedIn) -> VoicePage {
    let phase = &signed_in.hq.voice.phase;
    let controls = signed_in.hq_voice_controls();
    let member = signed_in.capabilities().can_change;
    VoicePage {
        voice: voice_view(signed_in, false),
        // While the statement is up, its own Start voice is the one.
        show_start: member && !controls.can_stop && *phase != HqVoicePhase::Disclosing,
        can_start: controls.can_start,
        disclosure: (*phase == HqVoicePhase::Disclosing).then(|| HqVoiceDisclosureView {
            title: cs::hq_voice_disclosure_title(l()).to_owned(),
            region_notice: signed_in.hq_voice_region_notice(),
            body: cs::hq_voice_disclosure(l()).to_owned(),
            accept_label: cs::hq_voice_disclosure_accept(l()).to_owned(),
            decline_label: cs::hq_voice_disclosure_decline(l()).to_owned(),
        }),
    }
}

/// The voice session's controls and cards in `signed_in`, on District HQ or
/// (`dock`) in the voice dock. On District HQ the line is for a member who may
/// talk; the dock shows only once a session started, and its dismiss button
/// puts away a card's notice too, which District HQ shows apart.
fn voice_view(signed_in: &SignedIn, dock: bool) -> HqVoiceView {
    let state = &signed_in.hq.voice;
    let controls = signed_in.hq_voice_controls();
    let capabilities = signed_in.capabilities();
    let session = signed_in.hq_voice_session();
    let line = (dock || capabilities.can_change)
        .then(|| voice_line(&state.phase, session, capabilities.can_dial))
        .flatten();
    let line_is_problem = line.as_ref().is_some_and(|(_, problem)| *problem);
    let live = matches!(state.phase, HqVoicePhase::Live { .. });
    let ended = matches!(
        state.phase,
        HqVoicePhase::Ended(_) | HqVoicePhase::Failed(_)
    );
    HqVoiceView {
        stage: stage(&state.phase),
        busy: !line_is_problem
            && matches!(
                state.phase,
                HqVoicePhase::Minting
                    | HqVoicePhase::Live {
                        agent: None | Some(HqAgentState::Initializing)
                    }
            ),
        line: line.map(|(text, _)| text),
        line_is_problem,
        can_stop: controls.can_stop,
        stop_label: cs::hq_voice_stop(l()).to_owned(),
        show_microphone: live && session.is_some(),
        muted: session.is_none_or(|session| session.microphone != MicrophoneState::On),
        can_dismiss: ended || (dock && state.notice.is_some()),
        notice: state.notice.as_ref().map(FailureView::from),
        cards: state
            .cards
            .iter()
            .enumerate()
            .map(|(index, card)| voice_card_view(card, &controls, index == 0))
            .collect(),
    }
}

/// Where `phase` stands, for the keyboard to follow.
fn stage(phase: &HqVoicePhase) -> HqVoiceStage {
    match phase {
        HqVoicePhase::Off => HqVoiceStage::Off,
        HqVoicePhase::Disclosing => HqVoiceStage::Disclosing,
        HqVoicePhase::Minting => HqVoiceStage::Starting,
        HqVoicePhase::Live { .. } => HqVoiceStage::Live,
        HqVoicePhase::Ended(_) => HqVoiceStage::Ended,
        HqVoicePhase::Failed(_) => HqVoiceStage::Failed,
    }
}

/// What the line about the voice session says, and whether it is a problem
/// rather than news, or `None` when there is nothing to say. `calls` is
/// whether this build can carry audio for this member at all.
fn voice_line(
    phase: &HqVoicePhase,
    session: Option<&MediaSession>,
    calls: bool,
) -> Option<(String, bool)> {
    let news = |text: &str| Some((text.to_owned(), false));
    match phase {
        HqVoicePhase::Off | HqVoicePhase::Disclosing if !calls => news(cs::media_unavailable(l())),
        HqVoicePhase::Off | HqVoicePhase::Disclosing => None,
        HqVoicePhase::Minting => news(cs::hq_voice_connecting(l())),
        HqVoicePhase::Live { agent } => {
            if let Some(notice) = session.and_then(MediaSession::notice) {
                return Some((notice.to_owned(), true));
            }
            match (session.map(|session| session.connection), agent) {
                (None | Some(MediaConnection::Connecting), _) => news(cs::hq_voice_connecting(l())),
                (_, None | Some(HqAgentState::Initializing)) => {
                    news(cs::hq_voice_agent_joining(l()))
                }
                (_, Some(HqAgentState::Idle | HqAgentState::Listening)) => {
                    news(cs::hq_voice_listening(l()))
                }
                (_, Some(HqAgentState::Thinking)) => news(cs::hq_voice_thinking(l())),
                (_, Some(HqAgentState::Speaking)) => news(cs::hq_voice_speaking(l())),
            }
        }
        HqVoicePhase::Ended(end) => end.message().map(|text| (text.to_owned(), true)),
        HqVoicePhase::Failed(failure) => Some((failure_text(failure), true)),
    }
}

/// `failure` as one block of text: what went wrong, then the regions that did
/// not answer, as C#'s `Display.Failure` writes a [`FailureView`].
fn failure_text(failure: &FailureText) -> String {
    match failure.regions_line() {
        Some(regions) => format!("{}\n{regions}", failure.message),
        None => failure.message.clone(),
    }
}

/// One change proposed by voice, the `first` of them or not.
fn voice_card_view(card: &HqVoiceCard, controls: &HqVoiceControls, first: bool) -> HqVoiceCardView {
    let (note, failure) = match &card.state {
        HqCardState::Open if controls.can_confirm => (Some(cs::hq_voice_card_note(l())), None),
        HqCardState::Open => (Some(s::hq_viewer_note(l())), None),
        HqCardState::Confirming => (Some(cs::hq_applying(l())), None),
        HqCardState::Cancelling => (Some(cs::hq_voice_cancelling(l())), None),
        // After a confirmation, the change may have been made before the
        // answer was lost.
        HqCardState::Failed {
            failure,
            confirming: true,
        } => (Some(s::hq_unknown_note(l())), Some(failure)),
        HqCardState::Failed {
            failure,
            confirming: false,
        } => (None, Some(failure)),
    };
    HqVoiceCardView {
        proposal_id: card.proposal.proposal_id.clone(),
        title: cs::hq_confirm_title(l()).to_owned(),
        summary: card.summary().to_owned(),
        note: note.map(str::to_owned),
        failure: failure.map(FailureView::from),
        busy: matches!(
            card.state,
            HqCardState::Confirming | HqCardState::Cancelling
        ),
        cancel_label: cs::hq_voice_cancel_action(l()).to_owned(),
        confirm_label: cs::hq_confirm_action(l()).to_owned(),
        can_cancel: card.is_actionable(),
        can_confirm: card.is_actionable() && controls.can_confirm,
        suggested: first,
    }
}

/// The conversation `hq`, for a member with `capabilities`, with the voice
/// controls `voice`.
fn hq_view(hq: &HqScreen, capabilities: &Capabilities, voice: VoicePage) -> HqView {
    let controls = hq.controls(capabilities);
    let failure = match &hq.phase {
        HqPhase::Failed(failure) => Some(FailureView::from(failure)),
        HqPhase::Idle
        | HqPhase::Thinking
        | HqPhase::Confirming(_)
        | HqPhase::Applying(_)
        | HqPhase::ConfirmFailed { .. } => None,
    };
    let can_retry = controls.can_retry && failure.as_ref().is_some_and(|f| f.retryable);
    let card = hq.phase.proposal().map(|proposal| HqCardView {
        title: cs::hq_confirm_title(l()).to_owned(),
        summary: proposal.summary.clone(),
        note: card_note(&hq.phase, controls.can_confirm).map(str::to_owned),
        applying: matches!(hq.phase, HqPhase::Applying(_)).then(|| cs::hq_applying(l()).to_owned()),
        failure: match &hq.phase {
            HqPhase::ConfirmFailed { failure, .. } => Some(failure.into()),
            _ => None,
        },
        confirm_label: cs::hq_confirm_action(l()).to_owned(),
        dismiss_label: cs::hq_dismiss_action(l()).to_owned(),
        can_confirm: controls.can_confirm,
        can_dismiss: controls.can_dismiss,
    });
    HqView {
        title: s::hq_title(l()).to_owned(),
        empty: hq
            .transcript
            .is_empty()
            .then(|| EmptyView::new(cs::hq_empty_title(l()), cs::hq_empty_body(l()))),
        messages: hq
            .transcript
            .iter()
            .zip(0u32..)
            .map(|(message, index)| message_view(message, index, capabilities))
            .collect(),
        thinking: (hq.phase == HqPhase::Thinking).then(|| cs::hq_thinking(l()).to_owned()),
        failure,
        can_retry,
        card,
        can_ask: controls.can_ask,
        prompt_placeholder: s::hq_prompt_placeholder(l()).to_owned(),
        ask_label: s::hq_ask_action(l()).to_owned(),
        retry_label: s::hq_retry_action(l()).to_owned(),
        footnote: s::hq_footnote(l()).to_owned(),
        voice: voice.voice,
        show_voice_start: voice.show_start,
        can_start_voice: voice.can_start,
        voice_start_label: cs::hq_voice_start(l()).to_owned(),
        voice_disclosure: voice.disclosure,
    }
}

/// The note under a proposed change: that nothing has changed yet (and, for a
/// role that cannot confirm, who can), or that it may have, once a
/// confirmation was sent and its answer lost; nothing while it is applied.
fn card_note(phase: &HqPhase, can_confirm: bool) -> Option<&'static str> {
    match phase {
        HqPhase::Confirming(_) if can_confirm => Some(cs::hq_confirm_note(l())),
        HqPhase::Confirming(_) => Some(s::hq_viewer_note(l())),
        HqPhase::ConfirmFailed { .. } => Some(s::hq_unknown_note(l())),
        HqPhase::Idle | HqPhase::Thinking | HqPhase::Applying(_) | HqPhase::Failed(_) => None,
    }
}

/// Whether `note` is good news: the change was made, or cancelled as asked.
/// The rest ask the member to look.
fn good_news(note: HqNote) -> bool {
    match note {
        HqNote::Applied | HqNote::Cancelled => true,
        HqNote::NotApplied | HqNote::Mismatched | HqNote::AlreadyHandled | HqNote::Expired => false,
    }
}

/// One line of the conversation, the `index`th.
fn message_view(message: &HqMessage, index: u32, capabilities: &Capabilities) -> HqMessageView {
    match (&message.text, message.author) {
        (HqText::Note(note), _) => HqMessageView::Note {
            index,
            text: note.text().to_owned(),
            applied: good_news(*note),
        },
        (HqText::Spoken { text, is_final, .. }, author) => HqMessageView::Spoken {
            index,
            text: text.clone(),
            member: author == HqAuthor::Member,
            is_final: *is_final,
        },
        (HqText::Said(text), HqAuthor::Member) => HqMessageView::Question {
            index,
            text: text.clone(),
        },
        (HqText::Said(text), HqAuthor::Assistant) => HqMessageView::Answer {
            index,
            text: crate::rich_text::parse(text),
            report: ReportAvailability::for_content(true, capabilities),
        },
    }
}

#[cfg(test)]
impl HqView {
    /// A view with every part filled in, for the boundary's own tests (a
    /// screen's trip to C# and back).
    pub(crate) fn sample() -> Self {
        let failure = FailureView {
            message: "Offline.".to_owned(),
            regions_line: None,
            retryable: true,
        };
        Self {
            title: s::hq_title(l()).to_owned(),
            empty: Some(EmptyView::new(
                cs::hq_empty_title(l()),
                cs::hq_empty_body(l()),
            )),
            messages: vec![
                HqMessageView::Question {
                    index: 0,
                    text: "How many calls?".to_owned(),
                },
                HqMessageView::Answer {
                    index: 1,
                    text: crate::rich_text::parse(
                        "# Calls\n**19** [calls](https://example.com)\n- `one`\n```\ncode\n```",
                    ),
                    report: ReportAvailability::InApp,
                },
                HqMessageView::Note {
                    index: 2,
                    text: HqNote::Applied.text().to_owned(),
                    applied: true,
                },
                HqMessageView::Spoken {
                    index: 3,
                    text: "Change the greeting".to_owned(),
                    member: true,
                    is_final: false,
                },
            ],
            thinking: Some(cs::hq_thinking(l()).to_owned()),
            failure: Some(failure.clone()),
            can_retry: true,
            card: Some(HqCardView {
                title: cs::hq_confirm_title(l()).to_owned(),
                summary: "Update the greeting.".to_owned(),
                note: Some(s::hq_unknown_note(l()).to_owned()),
                applying: Some(cs::hq_applying(l()).to_owned()),
                failure: Some(failure.clone()),
                confirm_label: cs::hq_confirm_action(l()).to_owned(),
                dismiss_label: cs::hq_dismiss_action(l()).to_owned(),
                can_confirm: true,
                can_dismiss: true,
            }),
            can_ask: true,
            prompt_placeholder: s::hq_prompt_placeholder(l()).to_owned(),
            ask_label: s::hq_ask_action(l()).to_owned(),
            retry_label: s::hq_retry_action(l()).to_owned(),
            footnote: s::hq_footnote(l()).to_owned(),
            voice: HqVoiceView {
                stage: HqVoiceStage::Live,
                line: Some(cs::hq_voice_listening(l()).to_owned()),
                line_is_problem: false,
                busy: false,
                can_stop: true,
                stop_label: cs::hq_voice_stop(l()).to_owned(),
                show_microphone: true,
                muted: false,
                can_dismiss: false,
                notice: Some(failure.clone()),
                cards: vec![HqVoiceCardView {
                    proposal_id: "p-1".to_owned(),
                    title: cs::hq_confirm_title(l()).to_owned(),
                    summary: "Update the greeting.".to_owned(),
                    note: Some(s::hq_unknown_note(l()).to_owned()),
                    failure: Some(failure.clone()),
                    busy: false,
                    cancel_label: cs::hq_voice_cancel_action(l()).to_owned(),
                    confirm_label: cs::hq_confirm_action(l()).to_owned(),
                    can_cancel: true,
                    can_confirm: true,
                    suggested: true,
                }],
            },
            show_voice_start: true,
            can_start_voice: true,
            voice_start_label: cs::hq_voice_start(l()).to_owned(),
            voice_disclosure: Some(HqVoiceDisclosureView {
                title: cs::hq_voice_disclosure_title(l()).to_owned(),
                region_notice: Some("Data in Canada.".to_owned()),
                body: cs::hq_voice_disclosure(l()).to_owned(),
                accept_label: cs::hq_voice_disclosure_accept(l()).to_owned(),
                decline_label: cs::hq_voice_disclosure_decline(l()).to_owned(),
            }),
        }
    }
}

#[cfg(test)]
mod tests {
    use district_core::FailureText;
    use district_model::HqPendingWrite;

    use super::*;

    fn proposal() -> HqPendingWrite {
        HqPendingWrite {
            tool: "update_persona".to_owned(),
            args: serde_json::Map::new(),
            summary: "Update the greeting.".to_owned(),
        }
    }

    fn failure(retryable: bool) -> FailureText {
        FailureText {
            message: "Offline.".to_owned(),
            degraded_regions: Vec::new(),
            session_ended: None,
            retryable,
        }
    }

    #[test]
    fn the_card_note_says_what_is_known() {
        let confirming = HqPhase::Confirming(proposal());
        assert_eq!(card_note(&confirming, true), Some(HqScreen::CONFIRM_NOTE));
        assert_eq!(card_note(&confirming, false), Some(s::hq_viewer_note(l())));
        let failed = HqPhase::ConfirmFailed {
            proposal: proposal(),
            failure: failure(true),
        };
        assert_eq!(card_note(&failed, true), Some(s::hq_unknown_note(l())));
        for phase in [
            HqPhase::Idle,
            HqPhase::Thinking,
            HqPhase::Applying(proposal()),
            HqPhase::Failed(failure(true)),
        ] {
            assert_eq!(card_note(&phase, true), None, "{phase:?}");
        }
    }

    /// A failure that trying again cannot help offers no "Try again".
    #[test]
    fn retry_is_offered_only_when_it_could_help() {
        let member = Capabilities::for_role(Some("client"));
        let mut hq = HqScreen {
            transcript: Vec::new(),
            phase: HqPhase::Failed(failure(false)),
            voice: district_core::HqVoice::default(),
        };
        assert!(!hq_view(&hq, &member, VoicePage::default()).can_retry);
        hq.phase = HqPhase::Failed(failure(true));
        assert!(hq_view(&hq, &member, VoicePage::default()).can_retry);
    }

    fn voice_card(summary: &str, state: HqCardState) -> HqVoiceCard {
        HqVoiceCard {
            proposal: district_model::HqVoiceProposal {
                proposal_id: "p-1".to_owned(),
                tool: "update_persona".to_owned(),
                args: serde_json::Map::new(),
                summary: summary.to_owned(),
            },
            state,
        }
    }

    /// A card a member who may not change the workspace sees (their role read
    /// again mid-session) says who can confirm it, and offers only Cancel.
    #[test]
    fn a_card_says_who_can_confirm_it() {
        let viewer = HqVoiceControls::default();
        let shown = voice_card_view(
            &voice_card("Update the greeting.", HqCardState::Open),
            &viewer,
            false,
        );
        assert_eq!(shown.note.as_deref(), Some(s::hq_viewer_note(l())));
        assert!(shown.can_cancel && !shown.can_confirm && !shown.suggested);
        // With no sentence from the service, the core's own.
        let shown = voice_card_view(&voice_card(" ", HqCardState::Open), &viewer, true);
        assert_eq!(shown.summary, cs::hq_voice_fallback_summary(l()));
    }

    /// A session that could not start because regions did not answer says
    /// which, under the reason, as C# writes a failure.
    #[test]
    fn a_failure_line_names_the_regions() {
        let failure = FailureText {
            degraded_regions: vec!["eu".to_owned()],
            ..failure(true)
        };
        let line = failure_text(&failure);
        let (message, regions) = line.split_once('\n').expect("two lines");
        assert_eq!(message, "Offline.");
        assert_eq!(Some(regions.to_owned()), failure.regions_line());
        assert_eq!(failure_text(&self::failure(true)), "Offline.");
    }

    #[test]
    fn notes_say_whether_the_change_was_made() {
        let member = Capabilities::for_role(Some("client"));
        for (note, applied) in [
            (HqNote::Applied, true),
            (HqNote::NotApplied, false),
            (HqNote::Mismatched, false),
            (HqNote::Cancelled, true),
            (HqNote::AlreadyHandled, false),
            (HqNote::Expired, false),
        ] {
            let message = HqMessage {
                author: HqAuthor::Assistant,
                text: HqText::Note(note),
            };
            assert_eq!(
                message_view(&message, 3, &member),
                HqMessageView::Note {
                    index: 3,
                    text: note.text().to_owned(),
                    applied,
                }
            );
        }
    }
}
