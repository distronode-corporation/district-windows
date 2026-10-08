//! The call log and one call: what the Linux app's calls page and call view
//! show, row for row.

use district_core::{
    CallDetailScreen, CallLog, CallView, Capabilities, TranscriptView, format_phone_number,
};
use district_model::{CallAnalysis, CallSummary, PhoneIntel};
use serde::Serialize;

use crate::views::{
    AI_SUMMARY, AiTextView, EmptyView, FactView, FailureView, LoadStatus, PagingView,
    ReportAvailability, ai_summary, facts, failure, humanize,
};

/// The heading of a call that could not be read, as the Linux app words it.
pub const CALL_FAILED_TITLE: &str = "Could not load this call";

/// The separator between the parts of one line.
const DOT: &str = " \u{b7} ";

/// One call in a list: the call log, or the overview's recent calls.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct CallRowView {
    /// The call, to open it by.
    pub call_id: String,
    /// Who called, or who was called: the contact's name, or the number.
    pub title: String,
    /// Direction, outcome and length, as one line ("Inbound \u{b7} Booked \u{b7} 1m 5s").
    pub detail: String,
    /// The call's short AI summary, when it has one.
    pub summary: Option<String>,
    /// When the call started, ISO 8601.
    pub started_at: String,
    /// Whether the call came in (false for a call placed).
    pub inbound: bool,
    /// Whether nobody answered it.
    pub missed: bool,
}

/// The call log.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct CallsView {
    /// Where the first read stands.
    pub status: LoadStatus,
    /// The calls, newest first.
    pub rows: Vec<CallRowView>,
    /// What to say when the log is read and has no calls.
    pub empty: Option<EmptyView>,
    /// The next page, and the newest page read again.
    pub paging: PagingView,
}

/// One call.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct CallDetailView {
    /// The call's id, as the route carries it.
    pub call_id: String,
    /// Where the read of the call stands.
    pub status: LoadStatus,
    /// Who called, or who was called. Empty until the call is read.
    pub title: String,
    /// When it started, ISO 8601, once read.
    pub started_at: Option<String>,
    /// How long it lasted ("1m 5s"), for a call somebody took.
    pub duration_label: Option<String>,
    /// Status, direction, numbers, carrier, sentiment, outcome and transfer,
    /// each only when known, as the Linux app lists them.
    pub facts: Vec<FactView>,
    /// The AI summary, when there is one.
    pub summary: Option<AiTextView>,
    /// Key points, action items, objections and topics, one bullet per line.
    pub analysis: Vec<FactView>,
    /// The follow-up sent after the call ("Email sent to", "Text message").
    pub follow_up: Vec<FactView>,
    /// The transcript.
    pub transcript: TranscriptState,
    /// Why the last read again failed, shown beside the call.
    pub refresh_failure: Option<FailureView>,
    /// How to offer Report for the AI summary.
    pub report: ReportAvailability,
    /// The number to call back, in E.164, when the call carries one.
    pub callback_number: Option<String>,
}

/// A call's transcript.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Enum)]
pub enum TranscriptState {
    /// Being read.
    Loading,
    /// The transcript.
    Ready {
        /// Its text.
        text: String,
    },
    /// The call has none.
    Absent {
        /// What to say instead.
        message: String,
    },
    /// The read failed.
    Failed {
        /// Why.
        failure: FailureView,
    },
}

impl From<&TranscriptView> for TranscriptState {
    fn from(transcript: &TranscriptView) -> Self {
        match transcript {
            TranscriptView::Loading => Self::Loading,
            TranscriptView::Ready(text) => Self::Ready { text: text.clone() },
            TranscriptView::Absent => Self::Absent {
                message: TranscriptView::ABSENT.to_owned(),
            },
            TranscriptView::Failed(failure) => Self::Failed {
                failure: failure.into(),
            },
        }
    }
}

/// The call log as the Linux calls page shows it.
pub(crate) fn calls_view(log: &CallLog) -> CallsView {
    let nothing = CallsView {
        status: LoadStatus::Loading,
        rows: Vec::new(),
        empty: None,
        paging: PagingView::default(),
    };
    match log {
        CallLog::NotLoaded | CallLog::Loading => nothing,
        CallLog::Failed(failure) => CallsView {
            status: LoadStatus::failed(CallLog::FAILED_TITLE, failure),
            ..nothing
        },
        CallLog::Ready(rows) => CallsView {
            status: LoadStatus::Ready,
            rows: rows.calls.iter().map(call_row).collect(),
            empty: rows
                .calls
                .is_empty()
                .then(|| EmptyView::new(CallLog::EMPTY_TITLE, CallLog::EMPTY_BODY)),
            paging: (&rows.paging).into(),
        },
    }
}

/// Whether `call` was placed from the workspace rather than taken.
fn outbound(call: &CallSummary) -> bool {
    call.direction.as_deref() == Some("outbound") || call.call_type == "outbound"
}

/// The length of a call somebody took, or `None` for one nobody did.
fn duration(call: &CallSummary) -> Option<String> {
    (call.duration_raw.unwrap_or(0) > 0)
        .then(|| call.duration.clone())
        .filter(|duration| !duration.trim().is_empty())
}

/// The title of a call, as the Linux app gives it.
fn call_title(call: &CallSummary) -> String {
    format_phone_number(&call.number)
}

/// One row of a call list.
pub(crate) fn call_row(call: &CallSummary) -> CallRowView {
    let missed = call.call_type == "missed";
    let direction = if missed {
        "Missed".to_owned()
    } else if outbound(call) {
        "Outbound".to_owned()
    } else {
        "Inbound".to_owned()
    };
    let outcome = call.disposition.as_deref().map(humanize);
    let detail = [Some(direction), outcome, duration(call)]
        .into_iter()
        .flatten()
        .filter(|part| !part.trim().is_empty())
        .collect::<Vec<_>>()
        .join(DOT);
    CallRowView {
        call_id: call.id.clone(),
        title: call_title(call),
        detail,
        summary: ai_summary(&call.ai_summary),
        started_at: call.created_at.clone(),
        inbound: !outbound(call),
        missed,
    }
}

/// Where a phone number is, from what is known about it.
fn location(intel: &PhoneIntel) -> Option<String> {
    let region = intel.region.as_ref();
    let parts: Vec<&str> = [
        region.and_then(|region| region.city.as_deref()),
        region.map(|region| region.name.as_str()),
        intel.country_name.as_deref(),
    ]
    .into_iter()
    .flatten()
    .filter(|part| !part.trim().is_empty())
    .collect();
    (!parts.is_empty()).then(|| parts.join(", "))
}

/// What is known about a phone number: where, what kind of line, whose.
pub(crate) fn number_facts(intel: Option<&PhoneIntel>) -> [(&'static str, Option<String>); 3] {
    [
        ("Location", intel.and_then(location)),
        (
            "Line type",
            intel
                .and_then(|intel| intel.line_type.as_deref())
                .map(humanize),
        ),
        ("Carrier", intel.and_then(|intel| intel.carrier.clone())),
    ]
}

fn call_facts(call: &CallSummary) -> Vec<FactView> {
    let [location, line_type, carrier] = number_facts(call.phone_intel.as_ref());
    facts([
        ("Status", Some(humanize(&call.status))),
        ("Direction", call.direction.as_deref().map(humanize)),
        (
            if outbound(call) {
                "Placed from"
            } else {
                "Caller's number"
            },
            call.from.as_deref().map(format_phone_number),
        ),
        location,
        line_type,
        carrier,
        ("Sentiment", call.sentiment.as_deref().map(humanize)),
        ("Outcome", call.disposition.as_deref().map(humanize)),
        ("Transfer", call.transfer_status.as_deref().map(humanize)),
        (
            "Why it was transferred",
            call.transfer_reason.as_deref().map(humanize),
        ),
    ])
}

fn analysis(analysis: Option<&CallAnalysis>) -> Vec<FactView> {
    let Some(analysis) = analysis else {
        return Vec::new();
    };
    let bullets = |items: &[String]| {
        Some(
            items
                .iter()
                .map(|item| format!("\u{2022} {item}"))
                .collect::<Vec<_>>()
                .join("\n"),
        )
    };
    facts([
        ("Key points", bullets(&analysis.key_points)),
        ("Action items", bullets(&analysis.action_items)),
        ("Objections", bullets(&analysis.objections)),
        ("Topics", bullets(&analysis.topics)),
    ])
}

/// `number` in E.164, when it can be said without a guess: written with its
/// `+` (spaces, brackets, dashes and dots dropped), or eleven digits starting
/// with North America's `1`, which the service stores without the `+`. Ten
/// digits could be any country's, so they are not given one.
pub(crate) fn dialable(number: Option<&str>) -> Option<String> {
    let number = number?.trim();
    let written = number
        .chars()
        .all(|c| c.is_ascii_digit() || matches!(c, '+' | ' ' | '(' | ')' | '-' | '.'));
    let digits: String = number.chars().filter(char::is_ascii_digit).collect();
    let plus = number.starts_with('+') && number.matches('+').count() == 1;
    let e164 = |digits: &str| {
        (8..=15)
            .contains(&digits.len())
            .then(|| format!("+{digits}"))
    };
    match (written, plus) {
        (false, _) => None,
        (true, true) => e164(&digits),
        (true, false) if digits.len() == 11 && digits.starts_with('1') => e164(&digits),
        (true, false) => None,
    }
}

/// One call as the Linux call view shows it.
pub(crate) fn call_detail_view(
    screen: &CallDetailScreen,
    capabilities: &Capabilities,
) -> CallDetailView {
    let mut view = CallDetailView {
        call_id: screen.call_id.clone(),
        status: LoadStatus::Loading,
        title: String::new(),
        started_at: None,
        duration_label: None,
        facts: Vec::new(),
        summary: None,
        analysis: Vec::new(),
        follow_up: Vec::new(),
        transcript: (&screen.transcript).into(),
        refresh_failure: failure(screen.refresh_failure.as_ref()),
        report: ReportAvailability::Hidden,
        callback_number: None,
    };
    match &screen.call {
        CallView::Loading => {}
        CallView::Failed(failure) => view.status = LoadStatus::failed(CALL_FAILED_TITLE, failure),
        CallView::Ready(call) => {
            let summary = ai_summary(&call.summary).or_else(|| ai_summary(&call.ai_summary));
            let follow_up = call.follow_up.as_ref();
            view = CallDetailView {
                status: LoadStatus::Ready,
                title: call_title(call),
                started_at: Some(call.created_at.clone()),
                duration_label: duration(call),
                facts: call_facts(call),
                report: ReportAvailability::for_content(summary.is_some(), capabilities),
                summary: summary.map(|text| AiTextView::new(AI_SUMMARY, text)),
                analysis: analysis(call.analysis.as_ref()),
                follow_up: facts([
                    (
                        "Email sent to",
                        follow_up.and_then(|sent| sent.email.clone()),
                    ),
                    ("Text message", follow_up.and_then(|sent| sent.sms.clone())),
                ]),
                // The caller's own number on a call that came in; on one placed,
                // the number dialled when the row names it rather than a contact.
                callback_number: if outbound(call) {
                    dialable(Some(&call.number))
                } else {
                    dialable(call.from.as_deref())
                },
                ..view
            };
        }
    }
    view
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn only_a_number_whose_country_is_known_is_dialable() {
        let dial = |number| dialable(Some(number));
        assert_eq!(dial(" +1 (416) 555-0142 ").as_deref(), Some("+14165550142"));
        assert_eq!(dial("14165550142").as_deref(), Some("+14165550142"));
        assert_eq!(dial("4165550142"), None, "ten digits, no country");
        assert_eq!(dial("+"), None);
        assert_eq!(dial("+1+416"), None);
        assert_eq!(dial("Direct Dial"), None);
        assert_eq!(dialable(None), None);
    }
}
