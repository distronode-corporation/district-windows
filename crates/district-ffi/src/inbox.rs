//! The inbox, its search, and one conversation, read-only: what the Linux inbox
//! page and thread view show, without the reply box.

use district_core::{
    Capabilities, ConversationList, Conversations, InboxScreen, SearchState, ThreadHistory,
    ThreadScreen, format_call_duration, format_phone_number,
};
use district_model::{
    ConversationSummary, MESSAGE_SEARCH_MIN_QUERY_LENGTH, MessageSearchHit, ThreadRef,
    TimelineEvent,
};
use serde::Serialize;

use crate::views::{
    AI_SUMMARY, AiTextView, EmptyView, FailureView, LoadStatus, ReportAvailability, ai_summary,
    failure, humanize,
};

/// Why this build shows conversations without a reply box.
pub const READ_ONLY_NOTE: &str =
    "Replies are sent from the web dashboard or the District AI phone apps.";
/// The heading of a conversation that could not be read, as the Linux app words it.
pub const THREAD_FAILED_TITLE: &str = "Could not load this conversation";

/// The inbox: the conversations, and the search over every message.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct InboxView {
    /// Where the first read of the list stands.
    pub status: LoadStatus,
    /// The conversations, newest first.
    pub threads: Vec<ThreadRowView>,
    /// What to say when the list is read and has none.
    pub empty: Option<EmptyView>,
    /// A note under a list that leaves older, quieter threads out.
    pub partial_note: Option<String>,
    /// Whether the list is being read again with its rows still showing.
    pub refreshing: bool,
    /// Why the last read again failed, shown beside the list.
    pub refresh_failure: Option<FailureView>,
    /// The search.
    pub search: SearchView,
    /// Why there is no reply box.
    pub read_only_note: String,
}

/// One conversation in the list.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct ThreadRowView {
    /// The thread, to open it by.
    pub thread_key: String,
    /// The contact's name, or the number or address.
    pub title: String,
    /// The last message, on one line ("You: ..." when it was ours).
    pub preview: String,
    /// The channels it runs on ("Text message", "Email").
    pub channel_label: String,
    /// Whether anything in it is unread.
    pub unread: bool,
    /// When the last message was sent, ISO 8601.
    pub last_at: Option<String>,
    /// Whether it can be opened. False for a key this build cannot read, which
    /// the core refuses to open.
    pub can_open: bool,
}

/// The search over every message.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct SearchView {
    /// What is typed.
    pub query: String,
    /// Whether the query is long enough to search: show the results instead of
    /// the list.
    pub active: bool,
    /// Whether a search is on its way.
    pub running: bool,
    /// The matching messages.
    pub hits: Vec<SearchHitView>,
    /// Whether the service sent only the newest matches.
    pub truncated: bool,
    /// Why the search failed.
    pub failure: Option<FailureView>,
    /// The shortest query that searches.
    pub min_query_length: u32,
    /// What to say when an active search found nothing.
    pub empty: Option<EmptyView>,
    /// The note under a truncated result.
    pub truncated_note: Option<String>,
}

/// One matching message.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct SearchHitView {
    /// The thread it is in, to open it by.
    pub thread_key: String,
    /// The contact's name, or the number or address.
    pub title: String,
    /// The message, on one line.
    pub snippet: String,
    /// When it was sent, ISO 8601.
    pub at: Option<String>,
    /// Whether its thread can be opened (see [`ThreadRowView::can_open`]).
    pub can_open: bool,
}

/// One conversation, read-only.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct ThreadView {
    /// The thread.
    pub thread_key: String,
    /// The contact's name, or the number or address.
    pub title: String,
    /// Where the first read stands.
    pub status: LoadStatus,
    /// What happened in it, oldest first.
    pub items: Vec<TimelineItemView>,
    /// Whether older events can be read (send `UiEvent::LoadOlder`).
    pub has_more: bool,
    /// Whether older events are on their way.
    pub loading_older: bool,
    /// Why the older events failed.
    pub older_failure: Option<FailureView>,
    /// Whether the newest events are being read again.
    pub refreshing: bool,
    /// Why the last read again failed.
    pub refresh_failure: Option<FailureView>,
    /// Why there is no reply box.
    pub read_only_note: String,
}

/// One event of a conversation.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct TimelineItemView {
    /// The event's id.
    pub id: String,
    /// What it is.
    pub kind: TimelineKind,
    /// When it happened, ISO 8601.
    pub at: Option<String>,
    /// How to offer Report: on a call carrying an AI summary.
    pub report: ReportAvailability,
}

/// What a timeline event is.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Enum)]
pub enum TimelineKind {
    /// A message, either way.
    Message {
        /// Whether the workspace sent it.
        outbound: bool,
        /// Who wrote it. Always `None` with core 1.2.0, whose timeline does not
        /// say; the side it is on says whose it is.
        author: Option<String>,
        /// Its text.
        body: String,
        /// The attached files' names.
        attachments: Vec<String>,
        /// An email's subject.
        subject: Option<String>,
        /// The channel ("Text message", "Email", "WhatsApp").
        channel_label: String,
        /// Where an outbound message got to ("Delivered", "Not delivered").
        delivery: Option<String>,
        /// Whether it was not delivered (show `delivery` as an error).
        delivery_failed: bool,
        /// "1 image attached", when anything is.
        attachments_label: Option<String>,
    },
    /// A call.
    CallEvent {
        /// The call, to open it by. Always `None` with core 1.2.0, whose
        /// timeline events do not name their call.
        call_id: Option<String>,
        /// "Incoming call, 01:05", "Missed call".
        title: String,
        /// The call's AI summary, when there is one.
        summary: Option<AiTextView>,
        /// Whether nobody answered it.
        missed: bool,
    },
    /// Anything else, as one readable line. Not produced with core 1.2.0,
    /// whose timeline holds only messages and calls.
    Note {
        /// The line.
        text: String,
    },
}

fn one_line(text: &str) -> String {
    text.split_whitespace().collect::<Vec<_>>().join(" ")
}

fn instant(iso: &str) -> Option<String> {
    Some(iso.to_owned()).filter(|iso| !iso.trim().is_empty())
}

fn can_open(thread_key: &str) -> bool {
    ThreadRef::from_thread_key(thread_key).is_some()
}

/// What a channel is called, as the Linux app names it.
fn channel(event_type: &str) -> &'static str {
    match event_type {
        "email" => "Email",
        "whatsapp" => "WhatsApp",
        _ => "Text message",
    }
}

fn thread_row(conversation: &ConversationSummary) -> ThreadRowView {
    let last = &conversation.last_message;
    let text = one_line(&last.body);
    let mut channels: Vec<&str> = conversation
        .channels
        .iter()
        .map(|kind| channel(kind))
        .collect();
    channels.dedup();
    ThreadRowView {
        thread_key: conversation.thread_key.clone(),
        title: format_phone_number(conversation.display_name()),
        preview: if last.direction == "outbound" {
            format!("You: {text}")
        } else {
            text
        },
        channel_label: channels.join(", "),
        unread: conversation.has_unread(),
        last_at: instant(&last.created_at),
        can_open: can_open(&conversation.thread_key),
    }
}

fn search_hit(hit: &MessageSearchHit) -> SearchHitView {
    SearchHitView {
        thread_key: hit.thread_key.clone(),
        title: format_phone_number(hit.display_name()),
        snippet: one_line(&hit.body),
        at: instant(&hit.created_at),
        can_open: can_open(&hit.thread_key),
    }
}

/// The search, as the Linux inbox page shows it.
fn search_view(search: &SearchState) -> SearchView {
    let active = search.active();
    let none = active && search.failure.is_none() && !search.running && search.hits.is_empty();
    SearchView {
        query: search.query.clone(),
        active,
        running: search.running,
        hits: search.hits.iter().map(search_hit).collect(),
        truncated: search.truncated,
        failure: failure(search.failure.as_ref()),
        min_query_length: u32::try_from(MESSAGE_SEARCH_MIN_QUERY_LENGTH).unwrap_or(u32::MAX),
        empty: none.then(|| EmptyView::new(SearchState::NONE_TITLE, SearchState::NONE_BODY)),
        truncated_note: search
            .truncated
            .then(|| SearchState::TRUNCATED_NOTE.to_owned()),
    }
}

/// The inbox for `inbox`.
pub(crate) fn inbox_view(inbox: &InboxScreen) -> InboxView {
    let mut view = InboxView {
        status: LoadStatus::Loading,
        threads: Vec::new(),
        empty: None,
        partial_note: None,
        refreshing: false,
        refresh_failure: None,
        search: search_view(&inbox.search),
        read_only_note: READ_ONLY_NOTE.to_owned(),
    };
    match &inbox.list {
        ConversationList::NotLoaded | ConversationList::Loading => {}
        ConversationList::Failed(failure) => {
            view.status = LoadStatus::failed(ConversationList::FAILED_TITLE, failure);
        }
        ConversationList::Ready(list) => {
            view.status = LoadStatus::Ready;
            view.threads = list.threads.iter().map(thread_row).collect();
            view.empty = list
                .threads
                .is_empty()
                .then(|| EmptyView::new(Conversations::EMPTY_TITLE, Conversations::EMPTY_BODY));
            view.partial_note = list.partial.then(|| Conversations::PARTIAL_NOTE.to_owned());
            view.refreshing = list.refreshing;
            view.refresh_failure = failure(list.refresh_failure.as_ref());
        }
    }
    view
}

/// Where an outbound message got to, as the Linux app words it.
fn delivery(event: &TimelineEvent) -> Option<String> {
    if event.direction != "outbound" {
        return None;
    }
    Some(
        match event.status.as_str() {
            "" => return None,
            "queued" | "accepted" | "sending" | "scheduled" => "Sending",
            "sent" => "Sent",
            "delivered" => "Delivered",
            "read" => "Read",
            "failed" | "undelivered" | "canceled" => NOT_DELIVERED,
            other => return Some(humanize(other)),
        }
        .to_owned(),
    )
}

const NOT_DELIVERED: &str = "Not delivered";

/// The line for a call in a conversation, as the Linux app words it.
fn call_line(event: &TimelineEvent) -> String {
    if event.is_missed_call() {
        return "Missed call".to_owned();
    }
    let direction = if event.direction == "outbound" {
        "Outgoing call"
    } else {
        "Incoming call"
    };
    match event
        .duration
        .and_then(|seconds| u64::try_from(seconds).ok())
    {
        Some(seconds) if seconds > 0 => format!("{direction}, {}", format_call_duration(seconds)),
        _ => direction.to_owned(),
    }
}

/// The name of an attached file: the last part of its address's path.
fn file_name(url: &str) -> String {
    url::Url::parse(url)
        .ok()
        .and_then(|url| {
            url.path_segments()?
                .rfind(|part| !part.is_empty())
                .map(str::to_owned)
        })
        .unwrap_or_else(|| "image".to_owned())
}

fn images_attached(count: usize) -> Option<String> {
    match count {
        0 => None,
        1 => Some("1 image attached".to_owned()),
        count => Some(format!("{count} images attached")),
    }
}

fn timeline_item(event: &TimelineEvent, capabilities: &Capabilities) -> TimelineItemView {
    let mut report = ReportAvailability::Hidden;
    let kind = if event.is_message() {
        let delivery = delivery(event);
        TimelineKind::Message {
            outbound: event.direction == "outbound",
            author: None,
            body: event.body.clone(),
            attachments: event.media_urls.iter().map(|url| file_name(url)).collect(),
            subject: event
                .subject
                .clone()
                .filter(|subject| !subject.trim().is_empty()),
            channel_label: channel(&event.event_type).to_owned(),
            delivery_failed: delivery.as_deref() == Some(NOT_DELIVERED),
            delivery,
            attachments_label: images_attached(event.media_urls.len()),
        }
    } else {
        let missed = event.is_missed_call();
        // The Linux app shows the summary, or else the body, under a call that
        // was answered.
        let summary = (!missed)
            .then(|| ai_summary(event.summary.as_deref().unwrap_or(event.body.as_str())))
            .flatten();
        report = ReportAvailability::for_content(summary.is_some(), capabilities);
        TimelineKind::CallEvent {
            call_id: None,
            title: call_line(event),
            summary: summary.map(|text| AiTextView::new(AI_SUMMARY, text)),
            missed,
        }
    };
    TimelineItemView {
        id: event.id.clone(),
        kind,
        at: instant(&event.timestamp),
        report,
    }
}

/// A conversation the core has not opened yet, by its key alone.
pub(crate) fn loading_thread(thread_key: &str) -> ThreadView {
    ThreadView {
        thread_key: thread_key.to_owned(),
        title: String::new(),
        status: LoadStatus::Loading,
        items: Vec::new(),
        has_more: false,
        loading_older: false,
        older_failure: None,
        refreshing: false,
        refresh_failure: None,
        read_only_note: READ_ONLY_NOTE.to_owned(),
    }
}

/// The open conversation, read-only.
pub(crate) fn thread_view(screen: &ThreadScreen, capabilities: &Capabilities) -> ThreadView {
    let mut view = ThreadView {
        title: format_phone_number(&screen.title),
        ..loading_thread(&screen.thread_key)
    };
    match &screen.history {
        ThreadHistory::Loading => {}
        ThreadHistory::Failed(failure) => {
            view.status = LoadStatus::failed(THREAD_FAILED_TITLE, failure);
        }
        ThreadHistory::Ready(events) => {
            view.status = LoadStatus::Ready;
            view.items = events
                .events
                .iter()
                .map(|event| timeline_item(event, capabilities))
                .collect();
            view.has_more = events.can_load_older() || events.loading_older;
            view.loading_older = events.loading_older;
            view.older_failure = failure(events.older_failure.as_ref());
            view.refreshing = events.refreshing;
            view.refresh_failure = failure(events.refresh_failure.as_ref());
        }
    }
    view
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn an_attachment_is_named_by_its_path() {
        assert_eq!(
            file_name("https://cdn.example.com/m/roof.png?sig=1"),
            "roof.png"
        );
        assert_eq!(file_name("https://cdn.example.com/"), "image");
        assert_eq!(file_name(""), "image");
        assert_eq!(images_attached(0), None);
        assert_eq!(images_attached(1).as_deref(), Some("1 image attached"));
        assert_eq!(images_attached(3).as_deref(), Some("3 images attached"));
    }
}
