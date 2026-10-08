//! Reporting AI-generated content: a support request to Distronode, raised
//! through the core's own support form without leaving the screen.
//!
//! The words and the shape of the message follow the iOS app's `ReportCopy`.
//! The message names what was reported by kind and id and quotes none of it: a
//! support request is a ticket at a vendor, and the content stays in the
//! workspace, where an agent can read it under the workspace's own rules.

use district_core::{Event, SignedIn, SupportEvent, SupportForm, SupportScreen};
use district_model::{SupportRequestFiling, SupportRequestKind};
use serde::Serialize;

use crate::views::ReportTarget;

/// The subject every report is raised under, which the support desk sorts on.
pub const REPORT_SUBJECT: &str = "Report: AI-generated content";
/// The longest note a report carries, in characters after trimming. The iOS
/// app's limit: well inside the service's 10,000, which the rest of the
/// message shares.
pub const NOTE_LIMIT: usize = 2000;
/// The message's first line, which says where the report came from.
pub const PREAMBLE: &str = "AI-generated content was reported from the District AI Windows app.";
/// What the message says when no note was added, so an absent note does not
/// read as a lost one.
pub const NO_NOTE: &str = "No note was added.";

/// The report this session started, while it is under way or until its outcome
/// is dismissed.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Enum)]
pub enum ReportStatus {
    /// On its way.
    Sending,
    /// Raised.
    Sent {
        /// The confirmation.
        message: String,
    },
    /// Not raised.
    Failed {
        /// Why, in the core's words.
        message: String,
    },
}

/// What the support desk receives about `target`.
fn reference(target: &ReportTarget) -> String {
    match target {
        ReportTarget::Call { call_id } => format!("Call: {call_id}"),
        ReportTarget::Contact { contact_id } => format!("Contact: {contact_id}"),
        ReportTarget::ThreadEvent {
            thread_key,
            event_id,
        } => format!("Conversation: {thread_key}\nEvent: {event_id}"),
    }
}

/// The report's message: where it came from, what it is about, and the note,
/// trimmed and cut to [`NOTE_LIMIT`] characters.
pub(crate) fn message(target: &ReportTarget, note: &str) -> String {
    let note: String = note.trim().chars().take(NOTE_LIMIT).collect();
    let note = note.trim_end();
    let tail = if note.is_empty() { NO_NOTE } else { note };
    format!("{PREAMBLE}\n{}\n\n{tail}", reference(target))
}

/// The core events that raise a report: open the support form, fill it in,
/// send it. The core accepts these on any screen, for a member whose role
/// allows support requests, and raising one does not navigate.
pub(crate) fn events(target: &ReportTarget, note: &str) -> Vec<Event> {
    vec![
        Event::Support(SupportEvent::StartRequest),
        Event::Support(SupportEvent::EditRequest(SupportForm {
            kind: SupportRequestKind::Problem,
            subject: REPORT_SUBJECT.to_owned(),
            message: message(target, note),
        })),
        Event::Support(SupportEvent::SubmitRequest),
    ]
}

/// The core events that put a report's outcome away: the confirmation, or a
/// failed draft (which a new report then starts afresh).
pub(crate) fn dismiss() -> Vec<Event> {
    vec![
        Event::Support(SupportEvent::DismissSubmitted),
        Event::Support(SupportEvent::CancelRequest),
    ]
}

/// The confirmation of a report raised, as the iOS app words it.
fn confirmation(filing: &SupportRequestFiling) -> &'static str {
    match filing {
        SupportRequestFiling::Filed(_) => "Reported. We'll review it.",
        SupportRequestFiling::Deduplicated => "You have already reported this. We'll review it.",
        SupportRequestFiling::Pending => "Reported. We have it and it is catching up.",
    }
}

/// Where the report this session started stands, from the support form's
/// state. `None` when no report was started (`reporting` is false), and when
/// the core refused to start one.
pub(crate) fn report_status(signed_in: &SignedIn, reporting: bool) -> Option<ReportStatus> {
    if !reporting {
        return None;
    }
    let support: &SupportScreen = &signed_in.support;
    match (&support.compose, &support.submitted) {
        (Some(compose), _) => Some(match &compose.failure {
            Some(failure) if !compose.submitting => ReportStatus::Failed {
                message: failure.message.clone(),
            },
            _ => ReportStatus::Sending,
        }),
        (None, Some(filing)) => Some(ReportStatus::Sent {
            message: confirmation(filing).to_owned(),
        }),
        (None, None) => None,
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn the_message_names_the_target_and_carries_the_note() {
        let call = ReportTarget::Call {
            call_id: "call-1".to_owned(),
        };
        assert_eq!(
            message(&call, "  wrong name  "),
            format!("{PREAMBLE}\nCall: call-1\n\nwrong name")
        );
        assert_eq!(
            message(
                &ReportTarget::Contact {
                    contact_id: "c-1".to_owned()
                },
                " \n "
            ),
            format!("{PREAMBLE}\nContact: c-1\n\n{NO_NOTE}")
        );
        let event = ReportTarget::ThreadEvent {
            thread_key: "contact:c-1".to_owned(),
            event_id: "e-1".to_owned(),
        };
        let long = "x".repeat(NOTE_LIMIT + 50);
        let sent = message(&event, &long);
        assert!(sent.starts_with(&format!(
            "{PREAMBLE}\nConversation: contact:c-1\nEvent: e-1\n\n"
        )));
        assert!(sent.ends_with(&"x".repeat(NOTE_LIMIT)));
        assert!(!sent.ends_with(&"x".repeat(NOTE_LIMIT + 1)));
        // Cut in the middle of a space run: no trailing space survives.
        let spaced = format!("{} tail", "y".repeat(NOTE_LIMIT - 1));
        assert!(message(&call, &spaced).ends_with(&"y".repeat(NOTE_LIMIT - 1)));
    }

    #[test]
    fn each_filing_has_its_confirmation() {
        assert_eq!(
            confirmation(&SupportRequestFiling::Filed("SUP-1".to_owned())),
            "Reported. We'll review it."
        );
        assert!(confirmation(&SupportRequestFiling::Deduplicated).starts_with("You have already"));
        assert!(confirmation(&SupportRequestFiling::Pending).contains("catching up"));
    }
}
