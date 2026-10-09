//! Meeting rooms (src/rooms.rs).

use district_core::{
    DisconnectReason, MediaEvent, MediaUpdate, MicrophoneState, Participant, RoomsEvent,
};
use district_ffi::rooms::{RoomsAction, RoomsView};
use district_ffi::{LoadStatus, ReportAvailability};

use super::calls_live::{placed, with_calls};
use super::*;

fn rooms(action: RoomsAction) -> UiEvent {
    UiEvent::Rooms { action }
}

/// The lobby, opened: the meetings on their way.
fn opened(session: Session) -> Session {
    session.ui(rooms(RoomsAction::Open))
}

fn meetings(session: Session, list: Value) -> Session {
    session.answer(
        |e| matches!(e, Effect::LoadMeetings { .. }),
        |ticket| Event::MeetingsLoaded {
            ticket,
            result: Ok(contracts::decode("meetings", list)),
        },
    )
}

/// The recorded meetings, held in `workspace_id`'s rooms.
fn meetings_of(workspace_id: &str) -> Value {
    let text = contracts::json("district-meetings.json")
        .to_string()
        .replace("ws-contract-test", workspace_id);
    serde_json::from_str(&text).unwrap()
}

/// The lobby with the recorded meetings: one running, one finished.
fn lobby(session: Session) -> Session {
    meetings(opened(session), meetings_of("ws-1"))
}

/// The room named "Weekly review" asked for.
fn joining(session: Session) -> Session {
    lobby(session)
        .ui(rooms(RoomsAction::EditRoomName {
            name: "Weekly review".to_owned(),
        }))
        .ui(rooms(RoomsAction::Join))
}

/// The room's credential issued (`fixture`): the engine is asked to join.
fn joined(session: Session, fixture: &str) -> Session {
    let credential = contracts::read(fixture);
    joining(session).answer(
        |e| matches!(e, Effect::RequestRoomToken { .. }),
        |ticket| Event::RoomTokenIssued {
            ticket,
            result: Ok(credential),
        },
    )
}

/// The engine reports `event` about the room's session.
fn media(session: Session, event: MediaEvent) -> Session {
    let ticket = session
        .pending
        .iter()
        .rev()
        .find_map(|effect| match effect {
            Effect::ConnectMedia { session, .. } => Some(*session),
            _ => None,
        })
        .expect("the engine was asked to join");
    session.send(Event::Media(MediaUpdate {
        session: ticket,
        event,
    }))
}

/// In the room, with Ada, a guest with no name, and the Companion, the
/// microphone on.
fn in_room(session: Session) -> Session {
    let session = media(
        joined(session, "district-room-token.json"),
        MediaEvent::Connected,
    );
    let session = media(
        session,
        MediaEvent::ParticipantJoined(Participant::new("user-4f2a", Some("Ada".to_owned()), false)),
    );
    let session = media(
        session,
        MediaEvent::ParticipantJoined(Participant::new("guest-1", None, false)),
    );
    let session = media(
        session,
        MediaEvent::ParticipantJoined(Participant::new(
            "companion",
            Some("Companion".to_owned()),
            true,
        )),
    );
    media(session, MediaEvent::Microphone(MicrophoneState::On))
}

/// Signed in as a viewer, in a build that can carry calls.
fn viewer_with_calls() -> Session {
    signed_in_as(
        CoreConfig {
            calls_available: true,
            ..config()
        },
        Ok(contracts::read("district-workspace-list.json")),
    )
}

/// The record of the finished meeting, opened.
fn record_open(session: Session) -> Session {
    lobby(session).ui(rooms(RoomsAction::OpenRecord {
        meeting_id: "meeting_contract_completed".to_owned(),
    }))
}

fn record_read(session: Session) -> Session {
    let detail = contracts::read("district-meeting-detail.json");
    record_open(session).answer(
        |e| matches!(e, Effect::LoadMeeting { .. }),
        |ticket| Event::MeetingLoaded {
            ticket,
            result: Ok(detail),
        },
    )
}

/// This area's snapshot cases.
pub(crate) fn cases() -> Vec<Case> {
    vec![
        ("rooms-loading", opened(with_calls())),
        ("rooms-loaded", lobby(with_calls())),
        ("rooms-empty", meetings(opened(with_calls()), json!([]))),
        (
            "rooms-failed",
            opened(with_calls()).answer(
                |e| matches!(e, Effect::LoadMeetings { .. }),
                |ticket| Event::MeetingsLoaded {
                    ticket,
                    result: Err(server_error()),
                },
            ),
        ),
        (
            "rooms-name-typed",
            lobby(with_calls()).ui(rooms(RoomsAction::EditRoomName {
                name: "Weekly review".to_owned(),
            })),
        ),
        ("rooms-joining", joining(with_calls())),
        (
            "rooms-join-failed",
            joining(with_calls()).answer(
                |e| matches!(e, Effect::RequestRoomToken { .. }),
                |ticket| Event::RoomTokenIssued {
                    ticket,
                    result: Err(server_error()),
                },
            ),
        ),
        (
            "rooms-room-connecting",
            joined(with_calls(), "district-room-token.json"),
        ),
        ("rooms-in-room", in_room(with_calls())),
        (
            "rooms-in-room-reconnecting-muted",
            media(
                media(in_room(with_calls()), MediaEvent::Reconnecting),
                MediaEvent::Microphone(MicrophoneState::Off),
            ),
        ),
        (
            "rooms-in-room-microphone-unavailable",
            media(
                in_room(with_calls()),
                MediaEvent::Microphone(MicrophoneState::Unavailable),
            ),
        ),
        (
            "rooms-in-room-encryption-failed",
            media(in_room(with_calls()), MediaEvent::EncryptionFailed),
        ),
        (
            "rooms-in-room-viewer",
            media(
                joined(viewer_with_calls(), "district-room-token-viewer.json"),
                MediaEvent::Connected,
            ),
        ),
        (
            "rooms-room-ended",
            media(
                in_room(with_calls()),
                MediaEvent::Disconnected(DisconnectReason::ConnectionLost),
            ),
        ),
        (
            "rooms-left",
            in_room(with_calls()).ui(rooms(RoomsAction::Leave)),
        ),
        ("rooms-busy-on-a-call", opened(placed())),
        ("rooms-record-loading", record_open(with_calls())),
        ("rooms-record", record_read(with_calls())),
        ("rooms-record-viewer", record_read(viewer_with_calls())),
        (
            "rooms-record-failed",
            record_open(with_calls()).answer(
                |e| matches!(e, Effect::LoadMeeting { .. }),
                |ticket| Event::MeetingLoaded {
                    ticket,
                    result: Err(server_error()),
                },
            ),
        ),
    ]
}

/// The lobby `session` shows.
fn page(session: &Session) -> RoomsView {
    match screen_view(&session.model) {
        ScreenView::Rooms { view } => view,
        other => panic!("not the rooms: {other:?}"),
    }
}

#[test]
fn built_it_is_offered_to_every_role_and_shows_its_page() {
    for session in [opened(signed_in()), opened(viewer())] {
        assert_eq!(route(&session), Route::Rooms);
        assert!(offered(&session).contains(&NavDestination::Rooms));
        assert_eq!(page(&session).title, "Meeting rooms");
    }
}

#[test]
fn a_name_leads_to_its_room_and_join_asks_for_it() {
    let lobby = page(&lobby(with_calls()));
    assert!(!lobby.can_join);
    assert!(lobby.name_note.starts_with("Name the room."));
    let typed = page(&lobby_typed());
    assert!(typed.can_join);
    assert_eq!(
        typed.name_note,
        "Everyone who joins \"weekly-review\" meets in the same room."
    );
    let asked = joining(with_calls());
    assert!(
        asked
            .pending
            .iter()
            .any(|e| matches!(e, Effect::RequestRoomToken { .. }))
    );
    let view = page(&asked);
    assert!(view.joining && !view.can_join);
}

fn lobby_typed() -> Session {
    lobby(with_calls()).ui(rooms(RoomsAction::EditRoomName {
        name: "Weekly review".to_owned(),
    }))
}

#[test]
fn in_the_room_the_people_mute_and_the_guest_link_show() {
    let view = page(&in_room(with_calls()));
    let room = view.room.expect("in a room");
    assert_eq!(room.title, "Room \"weekly-review\"");
    assert_eq!(room.state, "In the room.");
    assert_eq!(room.people, ["Ada", "A guest"]);
    assert_eq!(
        room.people_line,
        "With Ada, A guest. The Companion joins every room and writes up the minutes."
    );
    assert!(room.can_speak && room.can_mute && !room.muted);
    let link = room.guest_link.expect("a member who may speak gets a link");
    assert!(
        link.starts_with("https://www.distronode.com/meet/"),
        "{link}"
    );
    assert_eq!(room.notice, None);
}

#[test]
fn a_viewer_listens_and_gets_no_guest_link() {
    let view = page(&media(
        joined(viewer_with_calls(), "district-room-token-viewer.json"),
        MediaEvent::Connected,
    ));
    assert!(view.role_note.contains("to listen"));
    let room = view.room.expect("in a room");
    assert!(!room.can_speak && !room.can_mute);
    assert_eq!(room.guest_link, None);
    assert_eq!(room.people_line, "Nobody else is here yet.");
}

#[test]
fn a_room_that_ended_says_why_and_can_be_put_away() {
    let ended = media(
        in_room(with_calls()),
        MediaEvent::Disconnected(DisconnectReason::ConnectionLost),
    );
    let view = page(&ended);
    assert_eq!(view.room, None);
    assert_eq!(
        view.failure
            .as_ref()
            .map(|failure| failure.message.as_str()),
        Some("The connection was lost and could not be resumed.")
    );
    let dismissed = page(&ended.ui(rooms(RoomsAction::DismissFailure)));
    assert_eq!(dismissed.failure, None);
}

#[test]
fn a_call_holds_the_microphone_so_no_room_is_joined() {
    let view = page(&opened(placed()).ui(rooms(RoomsAction::EditRoomName {
        name: "standup".to_owned(),
    })));
    assert_eq!(
        view.busy_note.as_deref(),
        Some("Finish the call you are on to join a room.")
    );
    assert!(!view.can_join);
}

#[test]
fn a_running_meeting_can_be_rejoined_and_a_finished_one_cannot() {
    let view = page(&lobby(with_calls()));
    let rows: Vec<(&str, bool, Option<&str>)> = view
        .meetings
        .iter()
        .map(|row| {
            (
                row.title.as_str(),
                row.can_rejoin,
                row.in_progress.as_deref(),
            )
        })
        .collect();
    assert_eq!(
        rows,
        [
            ("standup", true, Some("In progress")),
            ("Weekly review", false, None)
        ]
    );
    assert_eq!(
        view.meetings[0].minutes.as_deref(),
        Some("Minutes are written when the meeting ends.")
    );
    // The service cut the preview short: it says so.
    assert!(
        view.meetings[1]
            .minutes
            .as_deref()
            .unwrap()
            .ends_with('\u{2026}')
    );
    assert_eq!(view.meetings[1].detail, "42m 0s, 2 people");
    let rejoined = lobby(with_calls()).ui(rooms(RoomsAction::Rejoin {
        meeting_id: "meeting_contract_live".to_owned(),
    }));
    assert!(
        rejoined
            .pending
            .iter()
            .any(|e| matches!(e, Effect::RequestRoomToken { .. }))
    );
    // A running meeting whose room is not this workspace's is not offered.
    let elsewhere = page(&meetings(opened(with_calls()), meetings_of("ws-other")));
    assert!(!elsewhere.meetings[0].can_rejoin);
}

#[test]
fn the_record_offers_report_on_its_minutes_and_action_items() {
    let view = page(&record_read(with_calls()));
    let record = view.record.expect("a record");
    assert_eq!(record.status, LoadStatus::Ready);
    assert_eq!(record.title, "Weekly review");
    assert!(record.minutes.starts_with("The team reviewed"));
    assert_eq!(record.minutes_report, ReportAvailability::InApp);
    assert_eq!(
        record.action_items,
        [
            "Move after-hours overflow to the second attendant (Ada)",
            "Rewrite the greeting (Grace)"
        ]
    );
    assert_eq!(record.action_items_report, ReportAvailability::InApp);
    assert!(record.transcript.is_some());
    // A viewer, whom support refuses, reports on the web.
    let viewer = page(&record_read(viewer_with_calls())).record.unwrap();
    assert_eq!(viewer.minutes_report, ReportAvailability::OnWeb);
    // Closed, it is gone.
    let closed = page(&record_read(with_calls()).ui(rooms(RoomsAction::CloseRecord)));
    assert_eq!(closed.record, None);
}

#[test]
fn a_record_with_no_minutes_offers_no_report() {
    let mut detail = contracts::json("district-meeting-detail.json");
    detail["summary"] = Value::Null;
    detail["actionItems"] = json!([]);
    let session = record_open(with_calls()).answer(
        |e| matches!(e, Effect::LoadMeeting { .. }),
        |ticket| Event::MeetingLoaded {
            ticket,
            result: Ok(contracts::decode("meeting", detail)),
        },
    );
    let record = page(&session).record.unwrap();
    assert_eq!(record.minutes, "No minutes were saved for this meeting.");
    assert_eq!(record.minutes_report, ReportAvailability::Hidden);
    assert_eq!(record.action_items_report, ReportAvailability::Hidden);

    // Not over yet: the minutes come when it ends.
    let mut running = contracts::json("district-meeting-detail.json");
    running["summary"] = Value::Null;
    running["endedAt"] = Value::Null;
    let session = record_open(with_calls()).answer(
        |e| matches!(e, Effect::LoadMeeting { .. }),
        |ticket| Event::MeetingLoaded {
            ticket,
            result: Ok(contracts::decode("meeting", running)),
        },
    );
    assert_eq!(
        page(&session).record.unwrap().minutes,
        "Minutes are written when the meeting ends."
    );
}

/// Every text value under `value`.
fn texts(value: &Value, out: &mut Vec<String>) {
    match value {
        Value::String(text) => out.push(text.to_lowercase()),
        Value::Array(items) => items.iter().for_each(|item| texts(item, out)),
        Value::Object(fields) => fields.values().for_each(|field| texts(field, out)),
        _ => {}
    }
}

/// Nothing on the page claims a room is end-to-end encrypted, and nothing
/// mentions recording.
#[test]
fn the_rooms_say_nothing_about_encryption_or_recording() {
    for (name, session) in cases() {
        let mut found = Vec::new();
        texts(&serde_json::to_value(page(&session)).unwrap(), &mut found);
        for text in found {
            for word in ["encrypt", "record", "end-to-end"] {
                assert!(!text.contains(word), "{name}: {text}");
            }
        }
    }
}

#[test]
fn each_action_is_its_core_event() {
    let id = || "meeting_1".to_owned();
    for (action, event) in [
        (RoomsAction::Open, Event::Navigate(Route::Rooms)),
        (RoomsAction::Leave, Event::Rooms(RoomsEvent::LeaveRoom)),
        (
            RoomsAction::EditRoomName {
                name: "standup".to_owned(),
            },
            Event::Rooms(RoomsEvent::EditRoomName("standup".to_owned())),
        ),
        (RoomsAction::Join, Event::Rooms(RoomsEvent::Start)),
        (
            RoomsAction::Rejoin { meeting_id: id() },
            Event::Rooms(RoomsEvent::Rejoin { meeting_id: id() }),
        ),
        (
            RoomsAction::Microphone { on: true },
            Event::Microphone(true),
        ),
        (
            RoomsAction::OpenRecord { meeting_id: id() },
            Event::Rooms(RoomsEvent::OpenRecord { meeting_id: id() }),
        ),
        (
            RoomsAction::CloseRecord,
            Event::Rooms(RoomsEvent::CloseRecord),
        ),
        (
            RoomsAction::DismissFailure,
            Event::Rooms(RoomsEvent::DismissJoinFailure),
        ),
        (RoomsAction::Retry, Event::Refresh),
    ] {
        assert_eq!(rooms(action).events(), [event]);
    }
}
