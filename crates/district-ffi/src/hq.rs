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

use district_core::{
    Capabilities, Event, HqAuthor, HqEvent, HqMessage, HqNote, HqPhase, HqScreen, HqText, Model,
    Route, SignedIn,
};
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
        /// Whether it is good news (the change was made) rather than something
        /// to look at.
        applied: bool,
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
    }]
}

/// The page of District HQ, for a signed-in model.
pub(crate) fn screen(_model: &Model, signed_in: &SignedIn) -> ScreenView {
    ScreenView::Hq {
        view: hq_view(&signed_in.hq, &signed_in.capabilities()),
    }
}

/// The conversation `hq`, for a member with `capabilities`.
fn hq_view(hq: &HqScreen, capabilities: &Capabilities) -> HqView {
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

/// One line of the conversation, the `index`th.
fn message_view(message: &HqMessage, index: u32, capabilities: &Capabilities) -> HqMessageView {
    match (&message.text, message.author) {
        (HqText::Note(note), _) => HqMessageView::Note {
            index,
            text: note.text().to_owned(),
            applied: matches!(note, HqNote::Applied),
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
            ],
            thinking: Some(cs::hq_thinking(l()).to_owned()),
            failure: Some(failure.clone()),
            can_retry: true,
            card: Some(HqCardView {
                title: cs::hq_confirm_title(l()).to_owned(),
                summary: "Update the greeting.".to_owned(),
                note: Some(s::hq_unknown_note(l()).to_owned()),
                applying: Some(cs::hq_applying(l()).to_owned()),
                failure: Some(failure),
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
        };
        assert!(!hq_view(&hq, &member).can_retry);
        hq.phase = HqPhase::Failed(failure(true));
        assert!(hq_view(&hq, &member).can_retry);
    }

    #[test]
    fn notes_say_whether_the_change_was_made() {
        let member = Capabilities::for_role(Some("client"));
        for (note, applied) in [
            (HqNote::Applied, true),
            (HqNote::NotApplied, false),
            (HqNote::Mismatched, false),
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
