//! The help desk (src/desk.rs): the queue behind the desk's switch, raising a
//! ticket, one ticket with its reply and status, and the settings with the
//! logo, for a member and for a viewer, to whom the whole desk is closed.

use district_api::ErrorDetail;
use district_core::DeskEvent;
use district_ffi::LoadStatus;
use district_ffi::desk::{
    DESK_TITLE, DeskAction, DeskFilter, DeskSettingsView, DeskStatus, DeskTicketView, DeskView,
    LOGO_TYPE_REFUSED, NO_CUSTOMER, NOTIFIED, PickedFileView, TICKET_FAILED_TITLE,
    desk_logo_problem,
};
use district_model::{DeskLogoRemovalResponse, DeskSettingsResponse, DeskTicketsResponse};

use super::*;

const TICKET: &str = "desk_ticket_open";

/// A PNG's first bytes: a logo the service hosts.
const PNG: &[u8] = b"\x89PNG\r\n\x1a\n\0\0\0\rIHDR";
/// A GIF's first bytes: a logo it does not.
const GIF: &[u8] = b"GIF89a\x01\0\x01\0";

fn act(session: Session, action: DeskAction) -> Session {
    session.ui(UiEvent::Desk { action })
}

fn open(session: Session) -> Session {
    act(session, DeskAction::Open)
}

fn settings_answer(enabled: bool, logo: bool) -> DeskSettingsResponse {
    let mut answer: DeskSettingsResponse = contracts::read("district-desk-settings.json");
    answer.settings.enabled = enabled;
    if !logo {
        answer.settings.public_logo_url = None;
    }
    answer
}

/// The desk's settings read for the queue, as `enabled` says.
fn switched(session: Session, enabled: bool) -> Session {
    session.answer(
        |e| matches!(e, Effect::LoadDeskSettings { .. }),
        |ticket| Event::DeskSettingsLoaded {
            ticket,
            result: Ok(settings_answer(enabled, true)),
        },
    )
}

fn off() -> Session {
    switched(open(signed_in()), false)
}

fn tickets_answer(keep: usize) -> DeskTicketsResponse {
    let mut answer: DeskTicketsResponse = contracts::read("district-desk-tickets.json");
    answer.tickets.truncate(keep);
    answer
}

/// The queue read, with the first `keep` of the fixture's three tickets.
fn queue(session: Session, keep: usize) -> Session {
    switched(session, true).answer(
        |e| matches!(e, Effect::LoadDeskTickets { .. }),
        |ticket| Event::DeskTicketsLoaded {
            ticket,
            result: Ok(tickets_answer(keep)),
        },
    )
}

fn listed() -> Session {
    queue(open(signed_in()), 3)
}

fn edit(session: Session, subject: &str, message: &str) -> Session {
    act(
        session,
        DeskAction::EditTicket {
            subject: subject.to_owned(),
            message: message.to_owned(),
            requester_name: "Ada".to_owned(),
            requester_email: String::new(),
            requester_phone: "+1 416 555 0142".to_owned(),
        },
    )
}

fn composing() -> Session {
    edit(act(listed(), DeskAction::StartTicket), "Hi", "")
}

fn filled() -> Session {
    edit(
        act(listed(), DeskAction::StartTicket),
        "Invoice question",
        "Which card was charged?",
    )
}

fn submitting() -> Session {
    act(filled(), DeskAction::SubmitTicket)
}

fn created(session: Session, result: Result<Value, ApiError>) -> Session {
    session.answer(
        |e| matches!(e, Effect::CreateDeskTicket { .. }),
        |ticket| Event::DeskTicketCreated {
            ticket,
            result: result.map(|value| contracts::decode("create", value)),
        },
    )
}

fn opening() -> Session {
    act(
        listed(),
        DeskAction::OpenTicket {
            ticket_id: TICKET.to_owned(),
        },
    )
}

fn ticket_read(session: Session, result: Result<Value, ApiError>) -> Session {
    session.answer(
        |e| matches!(e, Effect::LoadDeskTicket { .. }),
        |ticket| Event::DeskTicketLoaded {
            ticket,
            result: result.map(|value| contracts::decode("ticket", value)),
        },
    )
}

fn ticket() -> Session {
    ticket_read(opening(), Ok(contracts::json("district-desk-ticket.json")))
}

fn typed() -> Session {
    act(
        ticket(),
        DeskAction::EditReply {
            text: "Moved to Tuesday at 10am. Anything else?".to_owned(),
        },
    )
}

fn sending() -> Session {
    act(typed(), DeskAction::SendReply)
}

fn replied(session: Session, result: Result<Value, ApiError>) -> Session {
    session.answer(
        |e| matches!(e, Effect::ReplyToDeskTicket { .. }),
        |ticket| Event::DeskReplied {
            ticket,
            result: result.map(|value| contracts::decode("reply", value)),
        },
    )
}

fn resolving() -> Session {
    act(
        ticket(),
        DeskAction::SetStatus {
            status: DeskStatus::Resolved,
        },
    )
}

fn status_set(session: Session, result: Result<Value, ApiError>) -> Session {
    session.answer(
        |e| matches!(e, Effect::SetDeskTicketStatus { .. }),
        |ticket| Event::DeskTicketStatusSet {
            ticket,
            result: result.map(|value| contracts::decode("status", value)),
        },
    )
}

fn settings_opening() -> Session {
    act(listed(), DeskAction::OpenSettings)
}

fn settings_read(session: Session, result: Result<DeskSettingsResponse, ApiError>) -> Session {
    session.answer(
        |e| matches!(e, Effect::LoadDeskSettings { .. }),
        |ticket| Event::DeskSettingsLoaded { ticket, result },
    )
}

fn settings() -> Session {
    settings_read(settings_opening(), Ok(settings_answer(true, true)))
}

fn settings_edited() -> Session {
    act(
        act(
            settings(),
            DeskAction::EditBrandName {
                name: "Example Dental Help".to_owned(),
            },
        ),
        DeskAction::SetNotify { on: false },
    )
}

fn saving() -> Session {
    act(settings_edited(), DeskAction::SaveSettings)
}

fn saved(session: Session, result: Result<DeskSettingsResponse, ApiError>) -> Session {
    session.answer(
        |e| matches!(e, Effect::SaveDeskSettings { .. }),
        |ticket| Event::DeskSettingsSaved { ticket, result },
    )
}

fn picked(name: &str, bytes: &[u8]) -> PickedFileView {
    PickedFileView {
        file_name: name.to_owned(),
        size: bytes.len() as u64,
        bytes: bytes.to_vec(),
    }
}

fn uploading() -> Session {
    act(
        settings(),
        DeskAction::UploadLogo {
            file: picked("logo.png", PNG),
        },
    )
}

fn uploaded(session: Session, result: Result<DeskSettingsResponse, ApiError>) -> Session {
    session.answer(
        |e| matches!(e, Effect::UploadDeskLogo { .. }),
        |ticket| Event::DeskLogoUploaded { ticket, result },
    )
}

fn removed(session: Session, object_removed: bool) -> Session {
    let mut answer: DeskLogoRemovalResponse = contracts::read("district-desk-logo-delete.json");
    answer.object_removed = object_removed;
    act(session, DeskAction::DeleteLogo).answer(
        |e| matches!(e, Effect::DeleteDeskLogo { .. }),
        |ticket| Event::DeskLogoDeleted {
            ticket,
            result: Ok(answer),
        },
    )
}

/// The service refusing with its own sentence.
fn refusal(message: &str) -> ApiError {
    ApiError::Rejected {
        status: 413,
        detail: ErrorDetail {
            message: Some(message.to_owned()),
            ..ErrorDetail::default()
        },
    }
}

fn not_found() -> ApiError {
    ApiError::NotFound(ErrorDetail {
        message: Some("Ticket not found.".to_owned()),
        ..ErrorDetail::default()
    })
}

fn sent(session: &Session, wanted: fn(&Effect) -> bool) -> usize {
    session.pending.iter().filter(|e| wanted(e)).count()
}

/// This area's snapshot cases.
pub(crate) fn cases() -> Vec<Case> {
    vec![
        // The queue.
        ("desk-loading", open(signed_in())),
        ("desk-off", off()),
        ("desk-turning-on", act(off(), DeskAction::TurnOn)),
        (
            "desk-turn-on-failed",
            act(off(), DeskAction::TurnOn).answer(
                |e| matches!(e, Effect::SaveDeskSettings { .. }),
                |ticket| Event::DeskSettingsSaved {
                    ticket,
                    result: Err(server_error()),
                },
            ),
        ),
        ("desk-loaded", listed()),
        ("desk-empty", queue(open(signed_in()), 0)),
        (
            "desk-filtered",
            act(
                listed(),
                DeskAction::Filter {
                    filter: DeskFilter::Waiting,
                },
            ),
        ),
        (
            "desk-none-matching",
            act(
                queue(open(signed_in()), 1),
                DeskAction::Filter {
                    filter: DeskFilter::Resolved,
                },
            ),
        ),
        ("desk-refreshing", listed().ui(UiEvent::Refresh)),
        (
            "desk-failed",
            open(signed_in()).answer(
                |e| matches!(e, Effect::LoadDeskSettings { .. }),
                |ticket| Event::DeskSettingsLoaded {
                    ticket,
                    result: Err(server_error()),
                },
            ),
        ),
        ("desk-composing", composing()),
        ("desk-compose-ready", filled()),
        ("desk-submitting", submitting()),
        (
            "desk-submit-failed",
            created(submitting(), Err(refusal("Subject is too short."))),
        ),
        (
            "desk-submitted",
            created(
                submitting(),
                Ok(contracts::json("district-desk-ticket-create.json")),
            ),
        ),
        // One ticket.
        ("desk-ticket-loading", opening()),
        ("desk-ticket-loaded", ticket()),
        (
            "desk-ticket-failed",
            ticket_read(opening(), Err(not_found())),
        ),
        (
            "desk-ticket-refresh-failed",
            ticket_read(ticket().ui(UiEvent::Refresh), Err(server_error())),
        ),
        ("desk-ticket-typing", typed()),
        ("desk-ticket-sending", sending()),
        (
            "desk-ticket-sent",
            replied(
                sending(),
                Ok(contracts::json("district-desk-ticket-reply.json")),
            ),
        ),
        (
            "desk-ticket-send-failed",
            replied(sending(), Err(server_error())),
        ),
        ("desk-ticket-resolving", resolving()),
        (
            "desk-ticket-resolved",
            status_set(
                resolving(),
                Ok(contracts::json("district-desk-ticket-status.json")),
            ),
        ),
        (
            "desk-ticket-status-failed",
            status_set(resolving(), Err(server_error())),
        ),
        // The settings and the logo.
        ("desk-settings-loading", settings_opening()),
        ("desk-settings-loaded", settings()),
        (
            "desk-settings-failed",
            settings_read(settings_opening(), Err(server_error())),
        ),
        ("desk-settings-edited", settings_edited()),
        ("desk-settings-saving", saving()),
        (
            "desk-settings-save-failed",
            saved(saving(), Err(server_error())),
        ),
        ("desk-settings-logo-uploading", uploading()),
        (
            "desk-settings-logo-refused",
            uploaded(uploading(), Err(refusal("That file is too large."))),
        ),
        (
            "desk-settings-logo-unreadable",
            act(settings(), DeskAction::LogoUnreadable),
        ),
        ("desk-settings-logo-file-kept", removed(settings(), false)),
        (
            "desk-settings-no-logo",
            settings_read(settings_opening(), Ok(settings_answer(true, false))),
        ),
        // Closed to a viewer: the desk does not open.
        ("desk-viewer", open(viewer())),
    ]
}

fn queue_view(session: &Session) -> DeskView {
    match screen_view(&session.model) {
        ScreenView::Desk { view } => view,
        other => panic!("not the help desk: {other:?}"),
    }
}

fn ticket_view(session: &Session) -> DeskTicketView {
    match screen_view(&session.model) {
        ScreenView::DeskTicket { view } => view,
        other => panic!("not a ticket: {other:?}"),
    }
}

fn settings_view(session: &Session) -> DeskSettingsView {
    match screen_view(&session.model) {
        ScreenView::DeskSettings { view } => view,
        other => panic!("not the settings: {other:?}"),
    }
}

#[test]
fn each_action_is_its_core_event() {
    use district_core::DeskTicketForm;
    use district_model::DeskTicketStatus;
    let desk = |event: DeskEvent| vec![Event::Desk(event)];
    for (action, events) in [
        (DeskAction::Open, vec![Event::Navigate(Route::Desk)]),
        (
            DeskAction::OpenTicket {
                ticket_id: "ticket-1".to_owned(),
            },
            vec![Event::Navigate(Route::DeskTicket {
                ticket_id: "ticket-1".to_owned(),
            })],
        ),
        (
            DeskAction::OpenSettings,
            vec![Event::Navigate(Route::DeskSettings)],
        ),
        (
            DeskAction::Filter {
                filter: DeskFilter::All,
            },
            desk(DeskEvent::Filter(None)),
        ),
        (
            DeskAction::Filter {
                filter: DeskFilter::Open,
            },
            desk(DeskEvent::Filter(Some(DeskTicketStatus::Open))),
        ),
        (DeskAction::TurnOn, desk(DeskEvent::TurnOn)),
        (DeskAction::StartTicket, desk(DeskEvent::StartTicket)),
        (
            DeskAction::EditTicket {
                subject: "S".to_owned(),
                message: "M".to_owned(),
                requester_name: "N".to_owned(),
                requester_email: "E".to_owned(),
                requester_phone: "P".to_owned(),
            },
            desk(DeskEvent::EditTicket(DeskTicketForm {
                subject: "S".to_owned(),
                message: "M".to_owned(),
                requester_name: "N".to_owned(),
                requester_email: "E".to_owned(),
                requester_phone: "P".to_owned(),
            })),
        ),
        (DeskAction::SubmitTicket, desk(DeskEvent::SubmitTicket)),
        (DeskAction::CancelTicket, desk(DeskEvent::CancelTicket)),
        (
            DeskAction::DismissSubmitted,
            desk(DeskEvent::DismissSubmitted),
        ),
        (
            DeskAction::EditReply {
                text: "Hi".to_owned(),
            },
            desk(DeskEvent::EditReply("Hi".to_owned())),
        ),
        (DeskAction::SendReply, desk(DeskEvent::SendReply)),
        (
            DeskAction::SetStatus {
                status: DeskStatus::Waiting,
            },
            desk(DeskEvent::SetStatus(DeskTicketStatus::Waiting)),
        ),
        (
            DeskAction::DismissTicketFailures,
            desk(DeskEvent::DismissTicketFailures),
        ),
        (
            DeskAction::SetEnabled { on: false },
            desk(DeskEvent::SetEnabled(false)),
        ),
        (
            DeskAction::SetNotify { on: true },
            desk(DeskEvent::SetNotify(true)),
        ),
        (
            DeskAction::EditBrandName {
                name: "Engines".to_owned(),
            },
            desk(DeskEvent::EditBrandName("Engines".to_owned())),
        ),
        (DeskAction::SaveSettings, desk(DeskEvent::SaveSettings)),
        (DeskAction::LogoUnreadable, desk(DeskEvent::LogoUnreadable)),
        (DeskAction::DeleteLogo, desk(DeskEvent::DeleteLogo)),
        (
            DeskAction::DismissSettingsFailures,
            desk(DeskEvent::DismissSettingsFailures),
        ),
        (
            DeskAction::UploadLogo {
                file: picked("logo.gif", GIF),
            },
            Vec::new(),
        ),
    ] {
        assert_eq!(UiEvent::Desk { action }.events(), events);
    }
}

/// Every action crosses to C# and back intact, through UniFFI's own
/// converters (the ones the generated bindings call).
#[test]
fn every_action_survives_the_trip_to_csharp() {
    use uniffi::{Lift, Lower};
    for action in [
        DeskAction::Filter {
            filter: DeskFilter::Resolved,
        },
        DeskAction::EditTicket {
            subject: "S".to_owned(),
            message: "M".to_owned(),
            requester_name: String::new(),
            requester_email: "ada@example.com".to_owned(),
            requester_phone: String::new(),
        },
        DeskAction::SetStatus {
            status: DeskStatus::Open,
        },
        DeskAction::UploadLogo {
            file: picked("logo.png", PNG),
        },
    ] {
        let action = UiEvent::Desk { action };
        let buffer =
            <UiEvent as Lower<district_ffi::UniFfiTag>>::lower_into_rust_buffer(action.clone());
        let back =
            <UiEvent as Lift<district_ffi::UniFfiTag>>::try_lift_from_rust_buffer(buffer).unwrap();
        assert_eq!(back, action);
    }
}

/// Built, the pane offers the help desk to a member, highlighted on each of
/// its screens.
#[test]
fn built_it_is_offered_and_selected() {
    assert!(offered(&signed_in()).contains(&NavDestination::Desk));
    for session in [listed(), ticket(), settings()] {
        assert_eq!(
            shell_json(&session)["nav_selected"],
            json!(NavDestination::Desk)
        );
    }
}

/// The whole desk is closed to a viewer: the pane does not offer it, none of
/// its screens opens, and nothing is sent whatever arrives.
#[test]
fn a_viewer_is_offered_no_desk_and_none_of_it_opens() {
    assert!(!offered(&viewer()).contains(&NavDestination::Desk));
    for action in [
        DeskAction::Open,
        DeskAction::OpenTicket {
            ticket_id: TICKET.to_owned(),
        },
        DeskAction::OpenSettings,
        DeskAction::TurnOn,
        DeskAction::StartTicket,
        DeskAction::SaveSettings,
        DeskAction::SendReply,
        DeskAction::UploadLogo {
            file: picked("logo.png", PNG),
        },
    ] {
        let after = act(viewer(), action.clone());
        assert_eq!(route(&after), Route::Overview, "{action:?}");
        let desk_effects = sent(&after, |effect| {
            matches!(
                effect,
                Effect::LoadDeskSettings { .. }
                    | Effect::SaveDeskSettings { .. }
                    | Effect::UploadDeskLogo { .. }
                    | Effect::LoadDeskTickets { .. }
                    | Effect::LoadDeskTicket { .. }
                    | Effect::CreateDeskTicket { .. }
                    | Effect::ReplyToDeskTicket { .. }
            )
        });
        assert_eq!(desk_effects, 0, "{action:?}");
    }
}

/// A role that narrows to viewer while a ticket is open leaves it, and the
/// customer's details with it.
#[test]
fn a_role_narrowed_to_viewer_leaves_an_open_ticket() {
    let session = ticket().answer(
        |e| matches!(e, Effect::LoadOverview { .. }),
        |ticket| Event::OverviewLoaded {
            ticket,
            result: Ok(contracts::decode(
                "overview as a viewer",
                with(
                    with(
                        contracts::json("district-overview.json"),
                        "workspaceId",
                        "ws-1",
                    ),
                    "role",
                    "viewer",
                ),
            )),
        },
    );
    assert_eq!(route(&session), Route::Overview);
    assert!(!offered(&session).contains(&NavDestination::Desk));
    assert!(matches!(
        screen_view(&session.model),
        ScreenView::Overview { .. }
    ));
}

/// A switched-off desk is not an empty queue: it says so and offers to turn
/// it on, with no "New ticket" and no filter.
#[test]
fn a_desk_that_is_off_says_so_rather_than_empty() {
    let shown = queue_view(&off());
    assert_eq!(shown.title, DESK_TITLE);
    assert_eq!(shown.status, LoadStatus::Ready);
    let switched_off = shown.off.expect("the switched-off state");
    assert_eq!(switched_off.title, "The help desk is off");
    assert_eq!(switched_off.turn_on, "Turn on the help desk");
    assert!(switched_off.can_turn_on && !switched_off.enabling);
    assert!(!shown.can_start && !shown.can_filter && shown.empty.is_none());
    let turning = queue_view(&act(off(), DeskAction::TurnOn));
    assert!(!turning.off.unwrap().can_turn_on);

    let empty = queue_view(&queue(open(signed_in()), 0));
    assert_eq!(empty.off, None);
    assert_eq!(empty.empty.unwrap().title, "No tickets yet");
}

/// The queue counts each status over the whole queue, whatever is picked,
/// and names each ticket's customer as far as it can.
#[test]
fn the_queue_counts_and_filters_the_whole_queue() {
    let shown = queue_view(&listed());
    let labels: Vec<&str> = shown.filters.iter().map(|f| f.label.as_str()).collect();
    assert_eq!(
        labels,
        [
            "Every ticket (3)",
            "Open (1)",
            "Waiting on the customer (1)",
            "Resolved (1)"
        ]
    );
    let rows: Vec<(&str, &str, &str)> = shown
        .rows
        .iter()
        .map(|row| {
            (
                row.reference.as_str(),
                row.requester.as_str(),
                row.status_label.as_str(),
            )
        })
        .collect();
    assert_eq!(
        rows,
        [
            ("T-41", "Contract Test Caller", "Open"),
            ("T-42", NO_CUSTOMER, "Waiting"),
            ("T-43", "billing@example.com", "Resolved"),
        ]
    );
    let waiting = queue_view(&act(
        listed(),
        DeskAction::Filter {
            filter: DeskFilter::Waiting,
        },
    ));
    assert_eq!(waiting.rows.len(), 1);
    assert!(waiting.filters[2].selected && !waiting.filters[0].selected);
    let none = queue_view(&act(
        queue(open(signed_in()), 1),
        DeskAction::Filter {
            filter: DeskFilter::Resolved,
        },
    ));
    assert_eq!(
        none.none_matching.as_deref(),
        Some("No tickets with this status.")
    );
}

/// Raising a ticket: the form says what it needs, sends once, keeps what was
/// typed when it fails, and confirms with the ticket's reference.
#[test]
fn raising_a_ticket_sends_once_and_confirms() {
    let shown = queue_view(&composing());
    assert!(!shown.can_start);
    let compose = shown.compose.unwrap();
    assert!(!compose.can_submit);
    assert_eq!(
        compose.needs.as_deref(),
        Some("A subject of 3 to 200 characters, and a message.")
    );
    assert_eq!((compose.subject_max, compose.message_max), (200, 10_000));
    assert!(queue_view(&filled()).compose.unwrap().can_submit);

    let twice = act(submitting(), DeskAction::SubmitTicket);
    assert_eq!(
        sent(&twice, |e| matches!(e, Effect::CreateDeskTicket { .. })),
        1
    );
    let busy = queue_view(&twice).compose.unwrap();
    assert!(busy.submitting && !busy.can_submit);

    let failed = queue_view(&created(
        submitting(),
        Err(refusal("Subject is too short.")),
    ));
    let compose = failed.compose.unwrap();
    assert_eq!(compose.failure.unwrap().message, "Subject is too short.");
    assert_eq!(compose.subject, "Invoice question");

    let done = created(
        submitting(),
        Ok(contracts::json("district-desk-ticket-create.json")),
    );
    let shown = queue_view(&done);
    assert_eq!(shown.submitted.as_deref(), Some("Ticket T-41 is open."));
    assert_eq!(shown.compose, None);
    assert!(shown.refreshing);
    assert_eq!(
        queue_view(&act(done, DeskAction::DismissSubmitted)).submitted,
        None
    );
}

/// A ticket reads with its customer, its conversation in the core's words,
/// and its status buttons; a reply sends once and says whether the customer
/// was emailed; a status change is one at a time.
#[test]
fn a_ticket_replies_once_and_changes_status_one_at_a_time() {
    let loading = ticket_view(&opening());
    assert_eq!(loading.status, LoadStatus::Loading);
    assert_eq!(loading.ticket_id, TICKET);

    let shown = ticket_view(&ticket());
    assert_eq!(shown.title, "Reschedule Thursday's appointment");
    assert_eq!(shown.reference_line, "T-41 \u{b7} Open");
    let authors: Vec<&str> = shown.messages.iter().map(|m| m.author.as_str()).collect();
    assert_eq!(authors, ["Customer", "Receptionist", "Your team"]);
    assert!(shown.messages[2].from_team && !shown.messages[0].from_team);
    let details: Vec<&str> = shown.details.iter().map(|d| d.label.as_str()).collect();
    assert_eq!(
        details,
        ["Name", "Email address", "Phone number", "Came in by"]
    );
    assert!(shown.statuses[0].selected && shown.can_change_status);
    assert!(shown.can_write_reply && !shown.can_reply);
    assert!(ticket_view(&typed()).can_reply);

    let twice = act(sending(), DeskAction::SendReply);
    assert_eq!(
        sent(&twice, |e| matches!(e, Effect::ReplyToDeskTicket { .. })),
        1
    );
    let busy = ticket_view(&twice);
    assert!(busy.sending && !busy.can_write_reply && !busy.can_reply);

    let done = ticket_view(&replied(
        sending(),
        Ok(contracts::json("district-desk-ticket-reply.json")),
    ));
    assert_eq!(done.notified_note.as_deref(), Some(NOTIFIED));
    assert_eq!(done.reply, "");
    assert_eq!(done.reference_line, "T-41 \u{b7} Waiting on the customer");

    let failed = ticket_view(&replied(sending(), Err(server_error())));
    assert!(failed.send_failure.is_some());
    assert_eq!(failed.reply, "Moved to Tuesday at 10am. Anything else?");

    let changing = ticket_view(&resolving());
    assert!(changing.status_changing && !changing.can_change_status);
    let resolved = ticket_view(&status_set(
        resolving(),
        Ok(contracts::json("district-desk-ticket-status.json")),
    ));
    assert!(resolved.statuses[2].selected);
    assert_eq!(
        resolved.resolved_at.as_deref(),
        Some("2026-09-06T16:40:00.000Z")
    );
    assert_eq!(shown.created_at, "2026-09-04T09:15:00.000Z");
    assert_eq!(shown.resolved_at, None);
    let dismissed = ticket_view(&act(
        status_set(resolving(), Err(server_error())),
        DeskAction::DismissTicketFailures,
    ));
    assert_eq!(dismissed.status_failure, None);

    let missing = ticket_view(&ticket_read(opening(), Err(not_found())));
    let LoadStatus::Failed { title, failure } = missing.status else {
        panic!("not failed");
    };
    assert_eq!(title, TICKET_FAILED_TITLE);
    assert_eq!(failure.message, "Ticket not found.");
}

/// The settings: no form before they are read, a save sends only what
/// changed and waits for a logo change, and the logo's notes are the core's.
#[test]
fn the_settings_save_once_and_the_logo_is_one_change_at_a_time() {
    let loading = settings_view(&settings_opening());
    assert_eq!(loading.status, LoadStatus::Loading);
    assert!(!loading.can_edit && !loading.can_save && !loading.can_choose_logo);

    let shown = settings_view(&settings());
    assert!(shown.enabled && shown.notify_customers_by_email);
    assert_eq!(shown.brand_name, "Contract Test Desk");
    assert!(shown.can_edit && !shown.can_save && shown.can_choose_logo);
    assert!(shown.show_remove_logo && shown.can_remove_logo);
    assert_eq!(shown.logo_line, "A logo is published.");

    assert!(settings_view(&settings_edited()).can_save);
    let busy = settings_view(&saving());
    assert!(busy.saving && !busy.can_edit && !busy.can_save && !busy.can_choose_logo);
    let failed = settings_view(&saved(saving(), Err(server_error())));
    assert!(failed.save_failure.is_some() && failed.can_save);

    let busy_logo = settings_view(&uploading());
    assert!(busy_logo.logo_busy && !busy_logo.can_choose_logo && !busy_logo.can_remove_logo);
    assert!(busy_logo.can_edit && !busy_logo.can_save);
    let refused = settings_view(&uploaded(
        uploading(),
        Err(refusal("That file is too large.")),
    ));
    assert_eq!(
        refused.logo_failure.unwrap().message,
        "That file is too large."
    );

    let kept = settings_view(&removed(settings(), false));
    assert!(kept.logo_file_kept.is_some() && !kept.show_remove_logo);
    assert_eq!(kept.logo_line, "No logo yet.");
    assert_eq!(
        settings_view(&removed(settings(), true)).logo_file_kept,
        None
    );
}

/// A GIF picked as the logo, even one named `.png`, is refused with the
/// sentence the page shows, and the core never hears of it: nothing is sent,
/// and the settings stay as they were. So is a logo over the size limit.
#[test]
fn a_gif_logo_and_one_too_large_are_refused_before_the_core() {
    assert_eq!(
        desk_logo_problem(picked("logo.gif", GIF)).as_deref(),
        Some(LOGO_TYPE_REFUSED)
    );
    let mut large = PNG.to_vec();
    large.resize(5 * 1024 * 1024 + 1, 0);
    assert_eq!(
        desk_logo_problem(picked("large.png", &large)).as_deref(),
        Some("The logo must be 5 MB or smaller.")
    );
    assert_eq!(
        desk_logo_problem(picked("empty.png", b"")).as_deref(),
        Some("This file is empty.")
    );
    for file in [
        picked("logo.gif", GIF),
        picked("logo.png", GIF),
        picked("large.png", &large),
        picked("empty.png", b""),
    ] {
        let after = act(settings(), DeskAction::UploadLogo { file });
        assert_eq!(
            sent(&after, |e| matches!(e, Effect::UploadDeskLogo { .. })),
            0
        );
        assert_eq!(settings_view(&after), settings_view(&settings()));
    }
}
