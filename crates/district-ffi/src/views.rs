//! What several screens share: a failure, where a read stands, an empty list,
//! a list read a page at a time, a labelled value, AI-generated text, and the
//! Report action that goes with it.

use district_core::{Capabilities, FailureText, Paging};
use serde::Serialize;

/// Why something could not be read or done, in the core's words.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct FailureView {
    /// What went wrong, and what to do about it.
    pub message: String,
    /// "Affected regions: ..." when regions did not answer.
    pub regions_line: Option<String>,
    /// Whether trying again could help (show "Try again").
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

/// `failure` as a [`FailureView`], when there is one.
pub(crate) fn failure(failure: Option<&FailureText>) -> Option<FailureView> {
    failure.map(FailureView::from)
}

/// Where a screen's first read stands.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Enum)]
pub enum LoadStatus {
    /// Being read, with nothing to show yet (show a progress ring).
    Loading,
    /// The read failed, or there is nothing this screen can show (no workspace
    /// open). Shown as a status page.
    Failed {
        /// Why.
        failure: FailureView,
        /// The status page's heading, as the Linux app words it.
        title: String,
    },
    /// Read: show the content.
    Ready,
}

impl LoadStatus {
    /// The status page for `failure`, under `title`.
    pub(crate) fn failed(title: &str, failure: &FailureText) -> Self {
        Self::Failed {
            failure: failure.into(),
            title: title.to_owned(),
        }
    }
}

/// What an empty list says, shown when the read is ready and has no rows.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct EmptyView {
    /// The heading.
    pub title: String,
    /// The text under it.
    pub body: String,
}

impl EmptyView {
    /// An empty list's words.
    pub(crate) fn new(title: &str, body: &str) -> Self {
        Self {
            title: title.to_owned(),
            body: body.to_owned(),
        }
    }
}

/// Where a list read a page at a time stands, from the core's `Paging`.
#[derive(Clone, Debug, Default, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct PagingView {
    /// Whether to ask for the next page when the user nears the end.
    pub can_load_more: bool,
    /// Whether the next page is on its way.
    pub loading_more: bool,
    /// Why the next page failed, shown at the end of the list.
    pub more_failure: Option<FailureView>,
    /// Whether the list is being read again, with its rows still showing.
    pub refreshing: bool,
    /// Why the last read again failed, shown beside the list.
    pub refresh_failure: Option<FailureView>,
}

impl From<&Paging> for PagingView {
    fn from(paging: &Paging) -> Self {
        Self {
            can_load_more: paging.can_load_more(),
            loading_more: paging.loading_more,
            more_failure: failure(paging.more_failure.as_ref()),
            refreshing: paging.refreshing,
            refresh_failure: failure(paging.refresh_failure.as_ref()),
        }
    }
}

/// A labelled value: one row of a details list.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct FactView {
    /// What it is ("Status", "Carrier").
    pub label: String,
    /// Its value, display-ready.
    pub value: String,
}

impl FactView {
    /// A row, made only when `value` says something.
    pub(crate) fn given(label: &str, value: Option<String>) -> Option<Self> {
        value
            .filter(|value| !value.trim().is_empty())
            .map(|value| Self {
                label: label.to_owned(),
                value,
            })
    }
}

/// Every row of `rows` that says something, in order.
pub(crate) fn facts<const N: usize>(rows: [(&str, Option<String>); N]) -> Vec<FactView> {
    rows.into_iter()
        .filter_map(|(label, value)| FactView::given(label, value))
        .collect()
}

/// Text written by AI, under a heading that says so.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct AiTextView {
    /// The heading ("AI summary", "AI dossier").
    pub label: String,
    /// The text.
    pub text: String,
}

/// The heading of a call's AI summary, as the iOS app words it.
pub const AI_SUMMARY: &str = "AI summary";
/// The heading of a contact's AI research, the dossier.
pub const AI_DOSSIER: &str = "AI dossier";

impl AiTextView {
    /// `text` under `label`.
    pub(crate) fn new(label: &str, text: String) -> Self {
        Self {
            label: label.to_owned(),
            text,
        }
    }
}

/// How to offer the Report action beside AI-generated content.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash, Serialize, uniffi::Enum)]
pub enum ReportAvailability {
    /// Nothing AI-generated to report: no action.
    Hidden,
    /// The member's role can send support requests: show "Report".
    InApp,
    /// A viewer, whom support refuses: show "Report on the web", which opens
    /// [`REPORT_WEB_URL`].
    OnWeb,
}

/// Where "Report on the web" goes.
pub const REPORT_WEB_URL: &str = "https://www.distronode.com/support";

impl ReportAvailability {
    /// How to offer Report for content that `ai` says is or is not AI-written,
    /// to a member with `capabilities`.
    pub(crate) fn for_content(ai: bool, capabilities: &Capabilities) -> Self {
        if !ai {
            Self::Hidden
        } else if capabilities.can_use_support {
            Self::InApp
        } else {
            Self::OnWeb
        }
    }
}

/// What a report is about. Only its kind and id reach the support desk, never
/// the content itself.
#[derive(Clone, Debug, PartialEq, Eq, Hash, Serialize, uniffi::Enum)]
pub enum ReportTarget {
    /// A call's AI summary.
    Call {
        /// The call.
        call_id: String,
    },
    /// A contact's AI dossier.
    Contact {
        /// The contact.
        contact_id: String,
    },
    /// A call's AI summary in a conversation's timeline.
    ThreadEvent {
        /// The conversation.
        thread_key: String,
        /// The timeline event.
        event_id: String,
    },
}

/// What the service writes in a call's summary field when there is no summary:
/// a marker or a progress line, never AI text. The iOS app's list
/// (`CallNarrative.placeholders`), matched exactly.
const NOT_A_SUMMARY: [&str; 6] = [
    "No summary available.",
    "direct:softphone",
    "AI Voice session active...",
    "AI Outbound Voice session active...",
    "Voice session completed.",
    // The service's own byte is an em dash, written as an escape here.
    "No answer \u{2014} no conversation took place.",
];

/// What the service writes when a summary was attempted and failed: a call that
/// has a story nobody told, which is not AI text either.
const FAILED_SUMMARY: [&str; 2] = [
    "Error processing audio with AI.",
    "AI analysis failed to execute.",
];

/// The AI summary in `raw`, trimmed, or `None` when it is blank, a marker, or
/// the service's line for a failed summary.
pub(crate) fn ai_summary(raw: &str) -> Option<String> {
    let trimmed = raw.trim();
    let not_text =
        trimmed.is_empty() || NOT_A_SUMMARY.contains(&trimmed) || FAILED_SUMMARY.contains(&trimmed);
    (!not_text).then(|| trimmed.to_owned())
}

/// A status value the service wrote in its own form (`no-answer`,
/// `caller_requested_human`) as a sentence starts it: `No answer`, `Caller
/// requested human`. The Linux app's `humanize`.
pub(crate) fn humanize(raw: &str) -> String {
    let words = raw.trim().replace(['-', '_'], " ");
    let mut chars = words.chars();
    match chars.next() {
        Some(first) => first.to_uppercase().chain(chars).collect(),
        None => String::new(),
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn a_marker_or_a_failure_is_not_an_ai_summary() {
        assert_eq!(
            ai_summary("  Booked for Thursday. "),
            Some("Booked for Thursday.".to_owned())
        );
        for raw in NOT_A_SUMMARY.iter().chain(&FAILED_SUMMARY) {
            assert_eq!(ai_summary(raw), None, "{raw}");
        }
        assert_eq!(ai_summary("   "), None);
    }

    #[test]
    fn humanize_starts_a_sentence() {
        assert_eq!(humanize("no-answer"), "No answer");
        assert_eq!(humanize("caller_requested_human"), "Caller requested human");
        assert_eq!(humanize("  "), "");
    }

    #[test]
    fn report_is_offered_by_role_and_only_for_ai_content() {
        let member = Capabilities::for_role(Some("client"));
        let viewer = Capabilities::for_role(Some("viewer"));
        assert_eq!(
            ReportAvailability::for_content(true, &member),
            ReportAvailability::InApp
        );
        assert_eq!(
            ReportAvailability::for_content(true, &viewer),
            ReportAvailability::OnWeb
        );
        assert_eq!(
            ReportAvailability::for_content(false, &member),
            ReportAvailability::Hidden
        );
    }

    #[test]
    fn a_fact_with_nothing_to_say_is_left_out() {
        let rows = facts([
            ("Status", Some("Completed".to_owned())),
            ("Carrier", Some("  ".to_owned())),
            ("Line type", None),
        ]);
        assert_eq!(
            rows,
            [FactView {
                label: "Status".to_owned(),
                value: "Completed".to_owned()
            }]
        );
    }
}
