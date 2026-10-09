//! Meeting rooms, audio only, as District AI for Linux's rooms lobby shows
//! them (`pages/rooms.rs`, `pages/meeting_record.rs`): starting or joining a
//! room by its name, the room joined (who is there, mute, the guest link,
//! leave), the meetings held, and a meeting's record over the lobby, with
//! Report on its minutes and on its action items.
//!
//! A room is joined through the call engine, as a call is ([`MediaOwner::Room`]
//! in the core): this screen shows where that stands and who is there, and
//! never the room's credential. The guest link is the one the service minted,
//! which it does not for a viewer. Leaving the lobby leaves the room.
//!
//! Nothing here says a room is end-to-end encrypted: the core has no words
//! for it, and a phone call is not. Nothing says anything is recorded.
//!
//! [`MediaOwner::Room`]: district_core::MediaOwner::Room

use district_core::{
    Capabilities, CoreConfig, DisconnectReason, Event, FailureText, MediaConnection, MediaSession,
    MeetingList, MeetingRecord, MicrophoneState, Model, RoomsEvent, RoomsScreen, Route, SignedIn,
    WorkspacesState, format_duration, is_in_progress,
};
use district_model::{MeetRoomName, MeetingDetail, MeetingSummary};
use serde::Serialize;
use serde_json::Value;

use crate::screen::ScreenView;
use crate::views::{EmptyView, FactView, FailureView, LoadStatus, ReportAvailability, humanize};

/// Whether this version has the area's screens.
pub(crate) const BUILT: bool = true;

/// The page's heading, the navigation entry's.
pub const ROOMS_TITLE: &str = "Meeting rooms";
/// The room name box's label.
pub const ROOM_NAME_LABEL: &str = "Room name";
/// The button that joins the room named.
pub const JOIN_LABEL: &str = "Join";
/// The button that copies the guest link.
pub const COPY_GUEST_LINK: &str = "Copy guest link";
/// What the guest link is for, beside its button.
pub const GUEST_LINK_HINT: &str = "Copy a link that lets someone without an account join this room";
/// What shows once the guest link is on the clipboard.
pub const GUEST_LINK_COPIED: &str = "Guest link copied.";
/// The button that leaves the room.
pub const LEAVE_LABEL: &str = "Leave the room";
/// The meetings' heading.
pub const MEETINGS_TITLE: &str = "Meetings";
/// What the meetings are.
pub const MEETINGS_DESCRIPTION: &str = "The most recent meetings held in this workspace's rooms. \
    Open one to read its minutes and transcript.";
/// The badge on a meeting still running.
pub const IN_PROGRESS: &str = "In progress";
/// The button that joins a meeting still running.
pub const REJOIN_LABEL: &str = "Rejoin";
/// The record's heading before it is read.
pub const RECORD_TITLE: &str = "Meeting";
/// The heading of a record that could not be read.
pub const RECORD_FAILED_TITLE: &str = "Could not load this meeting";
/// The record's minutes heading.
pub const MINUTES_TITLE: &str = "Minutes";
/// The record's action items heading.
pub const ACTION_ITEMS_TITLE: &str = "Action items";
/// The record's transcript heading.
pub const TRANSCRIPT_TITLE: &str = "Transcript";
/// How a person in the room with no name is shown.
pub const A_GUEST: &str = "A guest";
/// The line when nobody else has joined.
pub const NOBODY_ELSE: &str = "Nobody else is here yet.";

/// The meeting rooms screen.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct RoomsView {
    /// The heading: "Meeting rooms".
    pub title: String,
    /// The form's heading: "Start or join a room".
    pub start_title: String,
    /// The name box's label: "Room name".
    pub room_name_label: String,
    /// The name as the core holds it, which C# writes into the box only when
    /// it is not what the box sent (a name cleared, say).
    pub room_name: String,
    /// The line under the box: which room the name leads to, or what to type.
    pub name_note: String,
    /// The note under the form: the Companion takes the minutes; or, for a
    /// viewer, that they join to listen.
    pub role_note: String,
    /// Why Join does not work while a call holds the microphone, when it does.
    pub busy_note: Option<String>,
    /// The Join button's words.
    pub join_label: String,
    /// Whether Join works.
    pub can_join: bool,
    /// Whether the credential for a room is being asked for.
    pub joining: bool,
    /// Why the last join failed, or why the last room ended under the member.
    /// Dismissible ([`RoomsAction::DismissFailure`]).
    pub failure: Option<FailureView>,
    /// The room joined.
    pub room: Option<RoomView>,
    /// The meetings' heading.
    pub meetings_title: String,
    /// What the meetings are.
    pub meetings_description: String,
    /// Where the meetings' read stands.
    pub meetings_status: LoadStatus,
    /// What to say when no meeting has been held yet.
    pub meetings_empty: Option<EmptyView>,
    /// The meetings, newest first.
    pub meetings: Vec<MeetingRowView>,
    /// Whether the meetings are being read again, with these still showing.
    pub meetings_refreshing: bool,
    /// The meeting record open over the lobby.
    pub record: Option<MeetingRecordView>,
}

/// The room joined.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct RoomView {
    /// "Room \"weekly-review\"".
    pub title: String,
    /// Where the connection stands, in words: joining, in the room,
    /// reconnecting.
    pub state: String,
    /// The people in the room, by name ("A guest" for one with none): never
    /// the services, which are a line of their own.
    pub people: Vec<String>,
    /// Who else is here, in a sentence, and that the Companion is when it is.
    pub people_line: String,
    /// The room's notice: the microphone could not be used, or someone's audio
    /// could not be decrypted.
    pub notice: Option<String>,
    /// Whether the member may speak: Mute is offered. A viewer listens.
    pub can_speak: bool,
    /// Whether the microphone is off.
    pub muted: bool,
    /// Whether Mute works: the room's audio is up.
    pub can_mute: bool,
    /// The link that lets someone without an account join, when the service
    /// minted one (never for a viewer).
    pub guest_link: Option<String>,
    /// The copy button's words.
    pub copy_link_label: String,
    /// What the guest link is for.
    pub copy_link_hint: String,
    /// What shows once it is copied.
    pub copied_note: String,
    /// The leave button's words.
    pub leave_label: String,
}

/// One meeting in the list.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct MeetingRowView {
    /// The meeting, for its record and for Rejoin.
    pub meeting_id: String,
    /// Its title, or the name its room was joined by.
    pub title: String,
    /// When it started, ISO 8601, for C# to show in local time.
    pub started_at: String,
    /// How long it was ("42m 0s") and how many were there ("2 people").
    pub detail: String,
    /// The start of its minutes, or that they come when it ends.
    pub minutes: Option<String>,
    /// "In progress", for a meeting still running.
    pub in_progress: Option<String>,
    /// Whether Rejoin is offered: running, and nothing holds the microphone.
    pub can_rejoin: bool,
    /// Rejoin's words.
    pub rejoin_label: String,
}

/// A meeting's record, over the lobby.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct MeetingRecordView {
    /// The meeting, for Report.
    pub meeting_id: String,
    /// Its title, once read.
    pub title: String,
    /// Where the read stands.
    pub status: LoadStatus,
    /// Its facts: status, length, people.
    pub facts: Vec<FactView>,
    /// When it started and ended, ISO 8601, for C# to show in local time.
    pub started_at: Option<String>,
    /// When it ended.
    pub ended_at: Option<String>,
    /// "Minutes".
    pub minutes_title: String,
    /// The minutes, or why there are none.
    pub minutes: String,
    /// How to offer Report on the minutes: only on minutes the Companion
    /// wrote.
    pub minutes_report: ReportAvailability,
    /// "Action items".
    pub action_items_title: String,
    /// The action items, each with whose it is where the record says.
    pub action_items: Vec<String>,
    /// How to offer Report on the action items.
    pub action_items_report: ReportAvailability,
    /// "Transcript".
    pub transcript_title: String,
    /// The meeting's whole transcript, when it has one.
    pub transcript: Option<String>,
}

/// Something the member did in the rooms lobby.
#[derive(Clone, Debug, PartialEq, Eq, uniffi::Enum)]
pub enum RoomsAction {
    /// Open the meeting rooms.
    Open,
    /// Leave the room the member is in.
    Leave,
    /// The room name box changed.
    EditRoomName {
        /// What it holds now.
        name: String,
    },
    /// Join the room named.
    Join,
    /// Join the room of a meeting still running.
    Rejoin {
        /// The meeting.
        meeting_id: String,
    },
    /// Turn the microphone on or off in the room.
    Microphone {
        /// On.
        on: bool,
    },
    /// Open a meeting's record.
    OpenRecord {
        /// The meeting.
        meeting_id: String,
    },
    /// Close the record.
    CloseRecord,
    /// Put away the last join's failure.
    DismissFailure,
    /// Read the meetings again: Try again.
    Retry,
}

/// The core events `action` is, in the order the core is to hear them.
pub(crate) fn events(action: RoomsAction) -> Vec<Event> {
    vec![match action {
        RoomsAction::Open => Event::Navigate(Route::Rooms),
        RoomsAction::Leave => Event::Rooms(RoomsEvent::LeaveRoom),
        RoomsAction::EditRoomName { name } => Event::Rooms(RoomsEvent::EditRoomName(name)),
        RoomsAction::Join => Event::Rooms(RoomsEvent::Start),
        RoomsAction::Rejoin { meeting_id } => Event::Rooms(RoomsEvent::Rejoin { meeting_id }),
        RoomsAction::Microphone { on } => Event::Microphone(on),
        RoomsAction::OpenRecord { meeting_id } => {
            Event::Rooms(RoomsEvent::OpenRecord { meeting_id })
        }
        RoomsAction::CloseRecord => Event::Rooms(RoomsEvent::CloseRecord),
        RoomsAction::DismissFailure => Event::Rooms(RoomsEvent::DismissJoinFailure),
        RoomsAction::Retry => Event::Refresh,
    }]
}

/// The page of the meeting rooms, for a signed-in model.
pub(crate) fn screen(model: &Model, signed_in: &SignedIn) -> ScreenView {
    ScreenView::Rooms {
        view: rooms_view(signed_in, model.config()),
    }
}

/// The lobby of `signed_in`'s workspace, in a build configured by `config`,
/// which says where a guest link leads.
pub(crate) fn rooms_view(signed_in: &SignedIn, config: &CoreConfig) -> RoomsView {
    let rooms = &signed_in.rooms;
    let capabilities = signed_in.capabilities();
    let speaker = capabilities.can_publish_in_rooms;
    let busy = signed_in.media_busy();
    let workspace_id = match &signed_in.workspaces {
        WorkspacesState::Ready(workspaces) => Some(workspaces.active().id.clone()),
        _ => None,
    };
    let failure = rooms
        .join_failure
        .as_ref()
        .map(FailureView::from)
        .or_else(|| {
            rooms
                .ended
                .and_then(DisconnectReason::message)
                .map(|message| FailureView {
                    message: message.to_owned(),
                    regions_line: None,
                    retryable: false,
                })
        });
    let (meetings_status, meetings_empty, meetings, meetings_refreshing) = match &rooms.meetings {
        MeetingList::NotLoaded | MeetingList::Loading => {
            (LoadStatus::Loading, None, Vec::new(), false)
        }
        MeetingList::Failed(failure) => (
            LoadStatus::failed(MeetingList::FAILED_TITLE, failure),
            None,
            Vec::new(),
            false,
        ),
        MeetingList::Ready {
            meetings,
            refreshing,
        } => (
            LoadStatus::Ready,
            meetings
                .is_empty()
                .then(|| EmptyView::new(MeetingList::EMPTY_TITLE, MeetingList::EMPTY_BODY)),
            meetings
                .iter()
                .map(|meeting| meeting_row(meeting, !busy, workspace_id.as_deref()))
                .collect(),
            *refreshing,
        ),
    };
    RoomsView {
        title: ROOMS_TITLE.to_owned(),
        start_title: RoomsScreen::START_TITLE.to_owned(),
        room_name_label: ROOM_NAME_LABEL.to_owned(),
        room_name: rooms.room_name.clone(),
        name_note: rooms
            .name_preview()
            .unwrap_or_else(|| RoomsScreen::NAME_HINT.to_owned()),
        role_note: if speaker {
            RoomsScreen::COMPANION_NOTE
        } else {
            RoomsScreen::LISTENER_NOTE
        }
        .to_owned(),
        busy_note: (busy && rooms.joining.is_none() && rooms.room.is_none())
            .then(|| RoomsScreen::BUSY_NOTE.to_owned()),
        join_label: JOIN_LABEL.to_owned(),
        can_join: rooms.can_start() && !busy,
        joining: rooms.joining.is_some(),
        failure,
        room: rooms.room.as_ref().map(|room| RoomView {
            title: format!(
                "Room \"{}\"",
                MeetRoomName::display_name(room.room.as_str())
            ),
            ..room_view(signed_in.room_session(), speaker, room.guest_link(config))
        }),
        meetings_title: MEETINGS_TITLE.to_owned(),
        meetings_description: MEETINGS_DESCRIPTION.to_owned(),
        meetings_status,
        meetings_empty,
        meetings,
        meetings_refreshing,
        record: rooms
            .record
            .as_ref()
            .map(|record| record_view(record, &capabilities)),
    }
}

/// The room joined, as far as its session goes: no session yet is joining.
fn room_view(
    session: Option<&MediaSession>,
    speaker: bool,
    guest_link: Option<String>,
) -> RoomView {
    let connection = session.map_or(MediaConnection::Connecting, |session| session.connection);
    let people: Vec<String> = session
        .map(|session| {
            session
                .people()
                .iter()
                .map(|person| person.name.clone().unwrap_or_else(|| A_GUEST.to_owned()))
                .collect()
        })
        .unwrap_or_default();
    let mut people_line = if people.is_empty() {
        NOBODY_ELSE.to_owned()
    } else {
        format!("With {}.", people.join(", "))
    };
    if session.is_some_and(MediaSession::service_present) {
        people_line = format!("{people_line} {}", RoomsScreen::COMPANION_NOTE);
    }
    let microphone = session.map_or(MicrophoneState::Off, |session| session.microphone);
    RoomView {
        title: String::new(),
        state: connection_words(connection).to_owned(),
        people,
        people_line,
        notice: session
            .and_then(|session| {
                if session.microphone == MicrophoneState::Unavailable {
                    Some(MediaSession::MICROPHONE_UNAVAILABLE)
                } else if session.encryption_failed {
                    Some(MediaSession::ENCRYPTION_FAILED)
                } else {
                    None
                }
            })
            .map(str::to_owned),
        can_speak: speaker,
        muted: microphone != MicrophoneState::On,
        can_mute: speaker
            && session.is_some_and(|session| {
                session.connection != MediaConnection::Connecting
                    && session.microphone != MicrophoneState::Unavailable
            }),
        guest_link,
        copy_link_label: COPY_GUEST_LINK.to_owned(),
        copy_link_hint: GUEST_LINK_HINT.to_owned(),
        copied_note: GUEST_LINK_COPIED.to_owned(),
        leave_label: LEAVE_LABEL.to_owned(),
    }
}

/// Where the room's connection stands, in words.
fn connection_words(connection: MediaConnection) -> &'static str {
    match connection {
        MediaConnection::Connecting => "Joining the room.",
        MediaConnection::Connected => "In the room.",
        MediaConnection::Reconnecting => MediaSession::RECONNECTING,
    }
}

/// A meeting's name: its title, or the name its room was joined by.
fn meeting_title(title: Option<&str>, room_name: &str) -> String {
    title
        .map(str::trim)
        .filter(|title| !title.is_empty())
        .map_or_else(
            || MeetRoomName::display_name(room_name).to_owned(),
            str::to_owned,
        )
}

/// "42m 0s" and "2 people", whichever there are.
fn length_and_people(duration_sec: i64, people: usize) -> String {
    let mut parts = Vec::new();
    if duration_sec > 0 {
        parts.push(format_duration(duration_sec));
    }
    match people {
        0 => {}
        1 => parts.push("1 person".to_owned()),
        count => parts.push(format!("{count} people")),
    }
    parts.join(", ")
}

/// Whether the core would join `meeting`'s room: it is running, and its room
/// is a meeting room of the workspace open, named exactly as listed.
fn rejoinable(meeting: &MeetingSummary, workspace_id: Option<&str>) -> bool {
    is_in_progress(meeting)
        && workspace_id
            .and_then(|id| MeetRoomName::new(id, MeetRoomName::display_name(&meeting.room_name)))
            .is_some_and(|room| room.as_str() == meeting.room_name)
}

fn meeting_row(
    meeting: &MeetingSummary,
    can_join: bool,
    workspace_id: Option<&str>,
) -> MeetingRowView {
    let running = is_in_progress(meeting);
    let minutes = if running {
        Some(MeetingList::NO_MINUTES_YET)
    } else {
        meeting.summary_preview.as_deref()
    }
    .map(str::trim)
    .filter(|text| !text.is_empty())
    // The service cuts the preview short, often mid-sentence: say so.
    .map(|text| {
        if text.ends_with(['.', '!', '?']) {
            text.to_owned()
        } else {
            format!("{text}\u{2026}")
        }
    });
    MeetingRowView {
        meeting_id: meeting.id.clone(),
        title: meeting_title(meeting.title.as_deref(), &meeting.room_name),
        started_at: meeting
            .started_at
            .clone()
            .unwrap_or_else(|| meeting.created_at.clone()),
        detail: length_and_people(
            meeting.duration_sec,
            usize::try_from(meeting.participant_count.max(0)).unwrap_or(usize::MAX),
        ),
        minutes,
        in_progress: running.then(|| IN_PROGRESS.to_owned()),
        can_rejoin: can_join && rejoinable(meeting, workspace_id),
        rejoin_label: REJOIN_LABEL.to_owned(),
    }
}

fn record_view(record: &MeetingRecord, capabilities: &Capabilities) -> MeetingRecordView {
    let mut view = MeetingRecordView {
        meeting_id: String::new(),
        title: RECORD_TITLE.to_owned(),
        status: LoadStatus::Loading,
        facts: Vec::new(),
        started_at: None,
        ended_at: None,
        minutes_title: MINUTES_TITLE.to_owned(),
        minutes: String::new(),
        minutes_report: ReportAvailability::Hidden,
        action_items_title: ACTION_ITEMS_TITLE.to_owned(),
        action_items: Vec::new(),
        action_items_report: ReportAvailability::Hidden,
        transcript_title: TRANSCRIPT_TITLE.to_owned(),
        transcript: None,
    };
    match record {
        MeetingRecord::Loading => view,
        MeetingRecord::Failed(failure) => MeetingRecordView {
            status: failed(failure),
            ..view
        },
        MeetingRecord::Ready(meeting) => {
            let (minutes, written) = minutes(meeting);
            let items = action_items(meeting.action_items.as_ref());
            let people = participant_names(meeting.participants.as_ref());
            view = MeetingRecordView {
                meeting_id: meeting.id.clone(),
                title: meeting_title(meeting.title.as_deref(), &meeting.room_name),
                status: LoadStatus::Ready,
                facts: crate::views::facts([
                    ("Status", Some(humanize(&meeting.status))),
                    (
                        "Length",
                        (meeting.duration_sec > 0).then(|| format_duration(meeting.duration_sec)),
                    ),
                    ("People", (!people.is_empty()).then(|| people.join(", "))),
                ]),
                started_at: meeting.started_at.clone(),
                ended_at: meeting.ended_at.clone(),
                minutes,
                minutes_report: ReportAvailability::for_content(written, capabilities),
                action_items_report: ReportAvailability::for_content(
                    !items.is_empty(),
                    capabilities,
                ),
                action_items: items,
                transcript: meeting
                    .transcript
                    .clone()
                    .filter(|text| !text.trim().is_empty()),
                ..view
            };
            view
        }
    }
}

fn failed(failure: &FailureText) -> LoadStatus {
    LoadStatus::failed(RECORD_FAILED_TITLE, failure)
}

/// The minutes, or why there are none (a meeting not over yet has not had
/// them written), and whether they are the Companion's writing.
fn minutes(meeting: &MeetingDetail) -> (String, bool) {
    match meeting
        .summary
        .as_deref()
        .filter(|text| !text.trim().is_empty())
    {
        Some(summary) => (summary.to_owned(), true),
        None if meeting.ended_at.is_none() => (MeetingList::NO_MINUTES_YET.to_owned(), false),
        None => (MeetingRecord::NO_MINUTES.to_owned(), false),
    }
}

/// The text of a record's free-form entry: itself when it is text, or the
/// first of `keys` it holds as text.
fn text_of(value: &Value, keys: &[&str]) -> Option<String> {
    match value {
        Value::String(text) => Some(text.clone()),
        Value::Object(fields) => keys
            .iter()
            .find_map(|key| fields.get(*key).and_then(Value::as_str))
            .map(str::to_owned),
        _ => None,
    }
    .filter(|text| !text.trim().is_empty())
}

/// The action items, each with whose it is where the record says. The record
/// is written by a model and its shape is not fixed, so only what reads as
/// text is shown.
fn action_items(items: Option<&Value>) -> Vec<String> {
    let Some(Value::Array(items)) = items else {
        return Vec::new();
    };
    items
        .iter()
        .filter_map(|item| {
            let text = text_of(item, &["text", "task", "title", "description"])?;
            let owner = item
                .is_object()
                .then(|| text_of(item, &["owner", "assignee"]))
                .flatten();
            Some(match owner {
                Some(owner) => format!("{text} ({owner})"),
                None => text,
            })
        })
        .collect()
}

/// The names of the people who were there.
fn participant_names(people: Option<&Value>) -> Vec<String> {
    let Some(Value::Array(people)) = people else {
        return Vec::new();
    };
    people
        .iter()
        .filter_map(|person| text_of(person, &["name"]))
        .collect()
}

/// A page for tests that only need one (the trip to C# and back).
#[cfg(test)]
pub(crate) fn sample() -> RoomsView {
    let room = room_view(None, true, Some("https://example.com/meet".to_owned()));
    RoomsView {
        title: ROOMS_TITLE.to_owned(),
        start_title: String::new(),
        room_name_label: String::new(),
        room_name: String::new(),
        name_note: String::new(),
        role_note: String::new(),
        busy_note: None,
        join_label: String::new(),
        can_join: false,
        joining: false,
        failure: None,
        room: Some(room),
        meetings_title: String::new(),
        meetings_description: String::new(),
        meetings_status: LoadStatus::Loading,
        meetings_empty: None,
        meetings: Vec::new(),
        meetings_refreshing: false,
        record: Some(record_view(
            &MeetingRecord::Loading,
            &Capabilities::default(),
        )),
    }
}

#[cfg(test)]
mod tests {
    use district_auth::AccessClaims;
    use district_core::{Effect, SessionState};
    use serde_json::json;

    use super::*;

    /// Before the workspace list is read, no meeting can be rejoined: no room
    /// is this workspace's yet.
    #[test]
    fn with_no_workspace_open_nothing_is_rejoinable() {
        let config = CoreConfig {
            web_base_url: "https://www.distronode.com".to_owned(),
            app_version: "0.1.0".to_owned(),
            calls_available: true,
            in_app_purchases: true,
        };
        let (mut model, effects) = Model::new(config.clone());
        let ticket = effects
            .iter()
            .find_map(Effect::ticket)
            .expect("the model looks for a session");
        model.update(Event::SessionRestored {
            ticket,
            result: Ok(AccessClaims {
                user_id: "user-1".to_owned(),
                device_id: "device-windows-1".to_owned(),
                expires_at_secs: 4_000_000_000,
            }),
        });
        let SessionState::SignedIn(signed_in) = model.session() else {
            panic!("not signed in");
        };
        let view = rooms_view(signed_in, &config);
        assert_eq!(view.meetings_status, LoadStatus::Loading);
        let running: MeetingSummary = serde_json::from_value(json!({
            "id": "m1", "roomName": "meet_ws-1_standup", "title": null,
            "status": "in-progress", "startedAt": null, "endedAt": null,
            "createdAt": "2026-08-15T14:30:00.000Z", "durationSec": 0,
            "summaryPreview": null, "participantCount": 0
        }))
        .unwrap();
        assert!(!rejoinable(&running, None));
        assert!(rejoinable(&running, Some("ws-1")));
        assert_eq!(
            meeting_row(&running, true, None).started_at,
            running.created_at
        );
    }

    #[test]
    fn a_record_shows_what_reads_as_text() {
        let odd = json!(["Call the carrier", {"task": "Send notes", "assignee": "Ada"}, {"nested": {}}, 3, " "]);
        assert_eq!(
            action_items(Some(&odd)),
            ["Call the carrier", "Send notes (Ada)"]
        );
        assert!(action_items(Some(&json!({"not": "a list"}))).is_empty());
        assert!(action_items(None).is_empty());
        assert_eq!(
            participant_names(Some(&json!([{"name": "Ada"}, "Grace", {"id": 1}]))),
            ["Ada", "Grace"]
        );
        assert!(participant_names(Some(&json!("Ada"))).is_empty());
    }

    #[test]
    fn a_meeting_is_named_by_its_title_or_its_room() {
        assert_eq!(meeting_title(Some("Review"), "meet_ws-1_standup"), "Review");
        assert_eq!(meeting_title(Some("  "), "meet_ws-1_standup"), "standup");
        assert_eq!(meeting_title(None, "meet_ws-1_standup"), "standup");
    }

    #[test]
    fn length_and_people_say_what_there_is() {
        assert_eq!(length_and_people(0, 0), "");
        assert_eq!(length_and_people(0, 1), "1 person");
        assert_eq!(length_and_people(2520, 2), "42m 0s, 2 people");
    }

    #[test]
    fn the_connection_in_words() {
        assert_eq!(
            connection_words(MediaConnection::Connecting),
            "Joining the room."
        );
        assert_eq!(connection_words(MediaConnection::Connected), "In the room.");
        assert_eq!(
            connection_words(MediaConnection::Reconnecting),
            MediaSession::RECONNECTING
        );
    }
}
