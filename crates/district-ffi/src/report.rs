//! Reporting AI-generated content: a support request to Distronode, raised
//! through the core's own support form without leaving the screen.
//!
//! The words and the shape of the message follow the iOS app's `ReportCopy`.
//! The message names what was reported by kind and id and quotes none of it: a
//! support request is a ticket at a vendor, and the content stays in the
//! workspace, where an agent can read it under the workspace's own rules.
//!
//! The support form is also the one the Support area writes a request in, and
//! the core holds one draft. So a Report is refused while that draft is a
//! member's with anything typed in it ([`refusal`]): filling the form would
//! wipe what they wrote. With no such draft a Report works as it always has.

use district_core::{
    Event, Model, SessionState, SignedIn, SupportEvent, SupportForm, SupportScreen,
};
use district_model::{SupportRequestFiling, SupportRequestKind, ThreadRef};
use serde::Serialize;

use crate::events::UiEvent;
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

/// Why a Report is refused while a support request is being written. One
/// sentence, which the report dialog shows over the note it keeps.
pub const DRAFT_OPEN: &str =
    "Send or discard the support request you are writing in Support, then report this.";

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
        // Never the thread key itself: a thread with no contact is keyed by the
        // other party's phone number or email address, which must not reach
        // the support desk. A contact's thread names the contact by its id.
        ReportTarget::ThreadEvent {
            thread_key,
            event_id,
        } => match ThreadRef::from_thread_key(thread_key) {
            Some(ThreadRef::Contact(contact_id)) => {
                format!("Conversation event: {event_id}\nContact: {contact_id}")
            }
            Some(ThreadRef::Address(_)) | None => format!("Conversation event: {event_id}"),
        },
        // The same rule: a contact's thread names the contact, an address's
        // thread nothing but the kind.
        ReportTarget::AiDraft { thread_key } => match ThreadRef::from_thread_key(thread_key) {
            Some(ThreadRef::Contact(contact_id)) => {
                format!("{AI_DRAFT_REFERENCE}\nContact: {contact_id}")
            }
            Some(ThreadRef::Address(_)) | None => AI_DRAFT_REFERENCE.to_owned(),
        },
        ReportTarget::HqAnswer => HQ_ANSWER_REFERENCE.to_owned(),
    }
}

/// What the support desk receives about a reply the model wrote: its kind. The
/// service keeps no id for one, and the report quotes none of it.
const AI_DRAFT_REFERENCE: &str = "Conversation reply drafted with AI";

/// What the support desk receives about a District HQ answer: its kind. The
/// service keeps no id for one, and the report quotes none of it.
const HQ_ANSWER_REFERENCE: &str = "District HQ answer";

/// The report's message: where it came from, what it is about, and the note,
/// trimmed and cut to [`NOTE_LIMIT`] characters.
pub(crate) fn message(target: &ReportTarget, note: &str) -> String {
    let note: String = note.trim().chars().take(NOTE_LIMIT).collect();
    let note = note.trim_end();
    let tail = if note.is_empty() { NO_NOTE } else { note };
    format!("{PREAMBLE}\n{}\n\n{tail}", reference(target))
}

/// The core events that raise a report: drop any draft left over, open the
/// support form, fill it in, send it. The core accepts these on any screen,
/// for a member whose role allows support requests, and raising one does not
/// navigate.
///
/// The cancel comes first because the draft carries the idempotency key: a
/// failed report's draft stays open, and filling it with a report on another
/// target would send that under the old key, which the service could answer
/// as already reported. With no draft the cancel does nothing, and while a
/// report is on its way the core refuses all four, so a second press during
/// Sending is ignored.
pub(crate) fn events(target: &ReportTarget, note: &str) -> Vec<Event> {
    vec![
        Event::Support(SupportEvent::CancelRequest),
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

/// Whether `form` is a report's own draft, as [`events`] fills it, rather than
/// one a member wrote in the Support area.
fn is_report(form: &SupportForm) -> bool {
    form.subject == REPORT_SUBJECT && form.message.starts_with(PREAMBLE)
}

/// Whether the support form holds a member's draft with anything typed in it:
/// a subject or a message that is more than spaces. The kind alone is not
/// typed (it has a default), and a report's own draft is not a member's.
fn member_draft_typed(support: &SupportScreen) -> bool {
    support.compose.as_ref().is_some_and(|compose| {
        let form = &compose.form;
        !is_report(form) && !(form.subject.trim().is_empty() && form.message.trim().is_empty())
    })
}

/// Why a Report would be refused now ([`DRAFT_OPEN`]), or `None` when it
/// would be sent.
pub(crate) fn refusal(signed_in: &SignedIn) -> Option<String> {
    member_draft_typed(&signed_in.support).then(|| DRAFT_OPEN.to_owned())
}

/// The core events `action` is, as the actor sends them, given the model they
/// are about to reach; `reporting` is whether this session has a report to
/// show, which a Report sets and its dismissal clears.
///
/// Every event but the two of a report is [`UiEvent::events`]. A Report while
/// [`refusal`] says so is nothing at all: the member's draft is not touched and
/// no report is started (the dialog has kept the note and said why). Putting a
/// report away closes the support form only when it holds the report's own
/// draft, never a member's.
pub fn ui_events(model: &Model, reporting: &mut bool, action: UiEvent) -> Vec<Event> {
    let support = match model.session() {
        SessionState::SignedIn(signed_in) => Some(&signed_in.support),
        _ => None,
    };
    let member_draft = support.is_some_and(|support| {
        support
            .compose
            .as_ref()
            .is_some_and(|compose| !is_report(&compose.form))
    });
    match action {
        UiEvent::Report { .. } if support.is_some_and(member_draft_typed) => Vec::new(),
        UiEvent::Report { target, note } => {
            *reporting = true;
            events(&target, &note)
        }
        UiEvent::DismissReport => {
            *reporting = false;
            if member_draft {
                vec![Event::Support(SupportEvent::DismissSubmitted)]
            } else {
                dismiss()
            }
        }
        action => action.events(),
    }
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
/// the core refused to start one. A draft the member is writing in the Support
/// area is theirs, not the report's: it never reads as a report on its way.
pub(crate) fn report_status(signed_in: &SignedIn, reporting: bool) -> Option<ReportStatus> {
    if !reporting {
        return None;
    }
    let support: &SupportScreen = &signed_in.support;
    let compose = support
        .compose
        .as_ref()
        .filter(|compose| is_report(&compose.form));
    match (compose, &support.submitted) {
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
            "{PREAMBLE}\nConversation event: e-1\nContact: c-1\n\n"
        )));
        assert!(sent.ends_with(&"x".repeat(NOTE_LIMIT)));
        assert!(!sent.ends_with(&"x".repeat(NOTE_LIMIT + 1)));
        // Cut in the middle of a space run: no trailing space survives.
        let spaced = format!("{} tail", "y".repeat(NOTE_LIMIT - 1));
        assert!(message(&call, &spaced).ends_with(&"y".repeat(NOTE_LIMIT - 1)));
    }

    /// A thread with no contact is keyed by an address, and no part of it
    /// reaches the support desk: only the event's id does.
    #[test]
    fn an_address_thread_is_reported_by_its_event_alone() {
        for (key, address) in [
            ("addr:ada@example.com", "ada@example.com"),
            ("addr:+14165550181", "4165550181"),
            ("fax:4165550181", "4165550181"),
        ] {
            let sent = message(
                &ReportTarget::ThreadEvent {
                    thread_key: key.to_owned(),
                    event_id: "e-9".to_owned(),
                },
                "",
            );
            assert_eq!(
                sent,
                format!("{PREAMBLE}\nConversation event: e-9\n\n{NO_NOTE}")
            );
            assert!(!sent.contains(address), "{sent}");
            assert!(!sent.contains("addr:"), "{sent}");
        }
    }

    /// A reply the model wrote is reported by its kind, with the contact of
    /// a contact's thread, and never the address of any other.
    #[test]
    fn an_ai_draft_is_reported_by_its_kind_and_contact() {
        let draft = |key: &str| ReportTarget::AiDraft {
            thread_key: key.to_owned(),
        };
        assert_eq!(
            message(&draft("contact:c-1"), "named the wrong day"),
            format!("{PREAMBLE}\n{AI_DRAFT_REFERENCE}\nContact: c-1\n\nnamed the wrong day")
        );
        for key in [
            "addr:ada@example.com",
            "addr:+14165550181",
            "fax:4165550181",
        ] {
            let sent = message(&draft(key), "");
            assert_eq!(
                sent,
                format!("{PREAMBLE}\n{AI_DRAFT_REFERENCE}\n\n{NO_NOTE}")
            );
            assert!(
                !sent.contains("4165550181") && !sent.contains("ada@"),
                "{sent}"
            );
        }
    }

    /// A District HQ answer is reported by its kind alone.
    #[test]
    fn an_hq_answer_is_reported_by_its_kind() {
        assert_eq!(
            message(&ReportTarget::HqAnswer, " made up a number "),
            format!("{PREAMBLE}\n{HQ_ANSWER_REFERENCE}\n\nmade up a number")
        );
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
