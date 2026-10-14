//! The calendars (src/scheduling_admin/calendar.rs): the connected accounts,
//! the connect card and its hand-off to the browser, an account's calendars,
//! the CalDAV form and the disconnect.
//!
//! The first account's calendars are read from the fixture; the second's read
//! fails, so its card falls back to what the connection itself says.

use district_core::SchedulingAdminEvent;
use district_core::scheduling_admin::{CaldavForm, CaldavPreset, CalendarEvent};
use district_ffi::scheduling_admin::calendar::{
    CaldavFormView, CalendarAction, SchedulingCalendarView,
};
use std::hash::{BuildHasher, Hasher, RandomState};
use std::sync::LazyLock;

use district_model::SchedulingSecret;

use super::*;

/// The app password the CalDAV cases type, which no snapshot may hold. It is
/// made as the tests run (a random number), never written here: code scanning
/// reads any literal that reaches a password as one kept in the source.
static PASSWORD: LazyLock<String> =
    LazyLock::new(|| RandomState::new().build_hasher().finish().to_string());

/// This area's snapshot cases.
pub(crate) fn cases() -> Vec<Case> {
    vec![
        (
            "scheduling-admin-calendar-loading",
            open(signed_in(), SchedulingAdminSection::Calendar),
        ),
        ("scheduling-admin-calendar", calendar(signed_in())),
        (
            "scheduling-admin-calendar-failed",
            status_read(
                open(signed_in(), SchedulingAdminSection::Calendar),
                Err(server_error()),
            ),
        ),
        ("scheduling-admin-calendar-viewer", calendar(viewer())),
        (
            "scheduling-admin-calendar-not-configured",
            zoom_read(
                status_read(
                    open(signed_in(), SchedulingAdminSection::Calendar),
                    Ok(not_configured()),
                ),
                Err(server_error()),
            ),
        ),
        (
            "scheduling-admin-calendar-connecting",
            connecting(signed_in()),
        ),
        (
            "scheduling-admin-calendar-awaiting-browser",
            signed_in_ready(connecting(signed_in()), Ok(SchedulingSecret::new(LINK))),
        ),
        (
            "scheduling-admin-calendar-connect-refused",
            signed_in_ready(
                connecting(signed_in()),
                Err(ApiError::Conflict(ErrorDetail::default())),
            ),
        ),
        (
            "scheduling-admin-calendar-picker-loading",
            calendar(signed_in()).ui(event(CalendarAction::OpenCalendars {
                connection_id: FIRST.to_owned(),
            })),
        ),
        ("scheduling-admin-calendar-picker-open", picker(signed_in())),
        (
            "scheduling-admin-calendar-picker-saving",
            picker_saving(signed_in()),
        ),
        (
            "scheduling-admin-calendar-picker-saved",
            picker_saved(signed_in()),
        ),
        (
            "scheduling-admin-calendar-picker-refused",
            set_calendars(picker_saving(signed_in()), Err(server_error())),
        ),
        ("scheduling-admin-calendar-caldav-open", caldav(signed_in())),
        (
            "scheduling-admin-calendar-caldav-server-missing",
            caldav(signed_in())
                .ui(event(CalendarAction::EditCaldav {
                    form: form("nextcloud", "", "ada@example.com", PASSWORD.clone()),
                }))
                .ui(event(CalendarAction::ConnectCaldav)),
        ),
        (
            "scheduling-admin-calendar-caldav-connecting",
            caldav_connecting(signed_in()),
        ),
        (
            "scheduling-admin-calendar-caldav-connected",
            caldav_connected(signed_in()),
        ),
        (
            "scheduling-admin-calendar-caldav-refused",
            caldav_answer(caldav_connecting(signed_in()), Err(server_error())),
        ),
        (
            "scheduling-admin-calendar-disconnect-asked",
            asked(signed_in(), FIRST),
        ),
        (
            "scheduling-admin-calendar-disconnected-destination",
            disconnected(signed_in()),
        ),
        (
            "scheduling-admin-calendar-disconnect-failed",
            delete_connection(
                asked(signed_in(), FIRST).ui(event(CalendarAction::ConfirmDisconnect)),
                Err(server_error()),
            ),
        ),
    ]
}

/// The fixture's account that receives bookings, Google.
const FIRST: &str = "cal-conn-contract-1";

/// The fixture's CalDAV account.
const SECOND: &str = "cal-conn-contract-2";

/// The single-use sign-in the scheduler answers a connect with.
const LINK: &str = "https://book.example.com/v1/auth/sso?token=single-use";

fn event(action: CalendarAction) -> UiEvent {
    UiEvent::SchedulingAdmin {
        action: SchedulingAdminAction::Calendar { action },
    }
}

fn form(preset: &str, server_url: &str, username: &str, password: String) -> CaldavFormView {
    CaldavFormView {
        preset: preset.to_owned(),
        server_url: server_url.to_owned(),
        username: username.to_owned(),
        app_password: password.into_bytes(),
    }
}

/// The newest read of the connections, answered with `result`.
fn status_read(session: Session, result: Result<Value, ApiError>) -> Session {
    answer_admin(
        session,
        |r| matches!(r, SchedulingAdminRequest::CalendarStatus),
        result,
    )
}

/// The newest read of Zoom, answered with `result`.
fn zoom_read(session: Session, result: Result<Value, ApiError>) -> Session {
    answer_admin(
        session,
        |r| matches!(r, SchedulingAdminRequest::ZoomStatus),
        result,
    )
}

/// The newest read of the first account's calendars, answered with `result`.
fn first_calendars(session: Session, result: Result<Value, ApiError>) -> Session {
    answer_admin(
        session,
        |r| matches!(r, SchedulingAdminRequest::Calendars { id, .. } if id == FIRST),
        result,
    )
}

/// The connections as no scheduler set up for calendars has them.
fn not_configured() -> Value {
    let mut value = contracts::json("district-scheduling-calendar-status.json");
    let data = &mut value["data"];
    data["configured"] = json!(false);
    data["connected"] = json!(false);
    data["connections"] = json!([]);
    value
}

/// The section's reads answered: the connections, Zoom, the first account's
/// calendars, and the second's, which fail.
fn read(session: Session) -> Session {
    let session = zoom_read(
        status_read(
            session,
            Ok(contracts::json("district-scheduling-calendar-status.json")),
        ),
        Ok(contracts::json("district-scheduling-zoom-status.json")),
    );
    let session = first_calendars(
        session,
        Ok(contracts::json("district-scheduling-calendars.json")),
    );
    answer_admin(
        session,
        |r| matches!(r, SchedulingAdminRequest::Calendars { id, .. } if id == SECOND),
        Err(server_error()),
    )
}

/// The calendars, read.
fn calendar(session: Session) -> Session {
    read(open(session, SchedulingAdminSection::Calendar))
}

/// Connect Google pressed: the sign-in on its way.
fn connecting(session: Session) -> Session {
    calendar(session).ui(event(CalendarAction::Connect {
        provider: "google".to_owned(),
    }))
}

/// The sign-in answered with `result`.
fn signed_in_ready(session: Session, result: Result<SchedulingSecret, ApiError>) -> Session {
    session.answer(
        |e| matches!(e, Effect::SchedulingSignIn { .. }),
        |ticket| Event::SchedulingSignInReady { ticket, result },
    )
}

/// The first account's calendars, read.
fn picker(session: Session) -> Session {
    first_calendars(
        calendar(session).ui(event(CalendarAction::OpenCalendars {
            connection_id: FIRST.to_owned(),
        })),
        Ok(contracts::json("district-scheduling-calendars.json")),
    )
}

/// The holidays checked for conflicts, and the save pressed.
fn picker_saving(session: Session) -> Session {
    picker(session)
        .ui(event(CalendarAction::SetCheckConflicts {
            calendar_id: "holidays@example.com".to_owned(),
            on: true,
        }))
        .ui(event(CalendarAction::SaveCalendars))
}

/// The save of the calendars, answered with `result`.
fn set_calendars(session: Session, result: Result<Value, ApiError>) -> Session {
    answer_admin(
        session,
        |r| matches!(r, SchedulingAdminRequest::SetCalendars { .. }),
        result,
    )
}

/// The calendars saved, then the account receiving bookings, then the
/// section read again.
fn picker_saved(session: Session) -> Session {
    let session = set_calendars(
        picker_saving(session),
        Ok(contracts::json("district-scheduling-ok.json")),
    );
    let session = answer_admin(
        session,
        |r| matches!(r, SchedulingAdminRequest::SetDestination { .. }),
        Ok(contracts::json("district-scheduling-ok.json")),
    );
    read(session)
}

/// The CalDAV form, open.
fn caldav(session: Session) -> Session {
    calendar(session).ui(event(CalendarAction::OpenCaldav))
}

/// An iCloud account typed in, and its connect pressed.
fn caldav_connecting(session: Session) -> Session {
    caldav(session)
        .ui(event(CalendarAction::EditCaldav {
            form: form("icloud", "", "ada@example.com", PASSWORD.clone()),
        }))
        .ui(event(CalendarAction::ConnectCaldav))
}

/// The CalDAV connect, answered with `result`.
fn caldav_answer(session: Session, result: Result<Value, ApiError>) -> Session {
    answer_admin(
        session,
        |r| matches!(r, SchedulingAdminRequest::ConnectCaldav { .. }),
        result,
    )
}

/// The CalDAV account connected, and the section read again.
fn caldav_connected(session: Session) -> Session {
    read(caldav_answer(
        caldav_connecting(session),
        Ok(contracts::json("district-scheduling-caldav-connect.json")),
    ))
}

/// The disconnect of `id`, asked about.
fn asked(session: Session, id: &str) -> Session {
    calendar(session).ui(event(CalendarAction::AskDisconnect {
        connection_id: id.to_owned(),
    }))
}

/// The disconnect, answered with `result`.
fn delete_connection(session: Session, result: Result<Value, ApiError>) -> Session {
    answer_admin(
        session,
        |r| matches!(r, SchedulingAdminRequest::DeleteConnection { .. }),
        result,
    )
}

/// The account receiving bookings disconnected, and the section read again.
fn disconnected(session: Session) -> Session {
    read(delete_connection(
        asked(session, FIRST).ui(event(CalendarAction::ConfirmDisconnect)),
        Ok(contracts::json("district-scheduling-ok.json")),
    ))
}

fn calendar_view(session: &Session) -> SchedulingCalendarView {
    let SchedulingAdminPage::Calendar { view } = admin_view(session).page else {
        panic!("not the calendars");
    };
    view
}

/// Whether a request `pick` finds is on its way.
fn sent(session: &Session, pick: fn(&SchedulingAdminRequest) -> bool) -> bool {
    session
        .pending
        .iter()
        .any(|e| matches!(e, Effect::SchedulingAdmin { request, .. } if pick(request)))
}

/// Each account shows what it checks and what receives bookings, a card
/// whose calendars could not be read falling back to the connection's own
/// flags; the connect card offers the providers the scheduler can connect,
/// Zoom's line and CalDAV; a viewer is offered the same.
#[test]
fn each_account_says_what_it_checks_and_what_receives_bookings() {
    let session = calendar(signed_in());
    assert_eq!(
        route(&session),
        Route::SchedulingAdmin(SchedulingSection::Calendar)
    );
    let page = admin_view(&session);
    assert_eq!(page.title, "Calendar");
    assert!(
        page.sections
            .iter()
            .any(|s| s.section == SchedulingAdminSection::Calendar && s.selected)
    );
    let view = calendar_view(&session);
    assert_eq!(view.status, district_ffi::LoadStatus::Ready);
    let cards: Vec<[&str; 5]> = view
        .connections
        .iter()
        .map(|c| {
            [
                c.id.as_str(),
                &c.provider,
                &c.account,
                &c.conflicts,
                &c.destination,
            ]
        })
        .collect();
    assert_eq!(
        cards,
        [
            [
                FIRST,
                "Google Calendar",
                "contract@example.com",
                "Contract Member",
                "Contract Member"
            ],
            [
                SECOND,
                "CalDAV",
                "caldav@example.com",
                "Not checked",
                "No calendar in this account"
            ],
        ]
    );
    assert_eq!(
        [
            view.account_label.as_str(),
            &view.conflicts_label,
            &view.destination_label,
            &view.choose_label,
            &view.disconnect_label,
            &view.connect_heading,
            &view.caldav_label,
        ],
        [
            "Account",
            "Checked for conflicts",
            "Receives bookings",
            "Choose calendars",
            "Disconnect",
            "Connecting a calendar",
            "Connect a CalDAV calendar"
        ]
    );
    assert!(view.can_disconnect && !view.disconnecting);
    assert_eq!(
        view.sentence,
        "Another account can be connected to check it for conflicts as well."
    );
    let zoom = view.zoom.expect("Zoom can be connected");
    assert_eq!(
        (zoom.label.as_str(), zoom.value.as_str()),
        ("Zoom", "Not connected")
    );
    // Microsoft has no credentials on this scheduler.
    let providers: Vec<(&str, &str)> = view
        .providers
        .iter()
        .map(|p| (p.value.as_str(), p.label.as_str()))
        .collect();
    assert_eq!(providers, [("google", "Connect Google Calendar")]);
    assert_eq!(
        view.connect_note,
        "Finish in the browser, then come back and refresh this page."
    );
    assert!(view.shows_connect_note);
    assert!(!view.connecting && !view.awaiting_browser);
    assert_eq!(view.connect_failure, None);
    assert_eq!(view.disconnect, None);
    assert_eq!((view.picker, view.caldav), (None, None));
    // A viewer may connect and choose their own calendars.
    let viewer = calendar_view(&calendar(viewer()));
    assert_eq!(viewer.providers.len(), 1);
    assert_eq!(viewer.connections.len(), 2);
    // Back to the booking pages.
    assert_eq!(route(&session.ui(UiEvent::Back)), Route::Scheduling);
}

/// A read of the connections that failed is a status page, never "none
/// connected"; Zoom's failure is silent; a scheduler with no calendar set up
/// says so once, and offers CalDAV only.
#[test]
fn a_failed_read_is_a_status_page_and_zoom_fails_silently() {
    let loading = calendar_view(&open(signed_in(), SchedulingAdminSection::Calendar));
    assert_eq!(loading.status, district_ffi::LoadStatus::Loading);
    assert!(loading.connections.is_empty() && loading.sentence.is_empty());
    let failed = calendar_view(&status_read(
        open(signed_in(), SchedulingAdminSection::Calendar),
        Err(server_error()),
    ));
    assert!(matches!(
        failed.status,
        district_ffi::LoadStatus::Failed { .. }
    ));
    assert!(failed.connections.is_empty() && failed.providers.is_empty());
    let bare = calendar_view(&zoom_read(
        status_read(
            open(signed_in(), SchedulingAdminSection::Calendar),
            Ok(not_configured()),
        ),
        Err(server_error()),
    ));
    assert_eq!(bare.status, district_ffi::LoadStatus::Ready);
    assert_eq!(bare.zoom, None);
    assert_eq!(
        bare.sentence,
        "Calendar connections are not set up on this scheduler yet."
    );
    assert!(bare.providers.is_empty());
    assert!(!bare.shows_connect_note, "said once, in the sentence");
    assert_eq!(bare.caldav_label, "Connect a CalDAV calendar");
    // Configured, with nothing connected yet.
    let mut none = contracts::json("district-scheduling-calendar-status.json");
    none["data"]["connections"] = json!([]);
    let none = calendar_view(&status_read(
        open(signed_in(), SchedulingAdminSection::Calendar),
        Ok(none),
    ));
    assert!(none.sentence.starts_with("No calendar is connected yet"));
    assert!(none.shows_connect_note);
}

/// Connecting Google asks for the single-use sign-in, the buttons waiting
/// meanwhile; the sign-in is opened in the browser at once and reaches no
/// view, and the section offers Refresh until it is read again. A sign-in
/// refused says why until it is put away.
#[test]
fn connecting_hands_off_to_the_browser_and_shows_no_link() {
    let session = connecting(signed_in());
    assert!(
        session
            .pending
            .iter()
            .any(|e| matches!(e, Effect::SchedulingSignIn { .. }))
    );
    let view = calendar_view(&session);
    assert!(view.connecting && !view.awaiting_browser);
    // A provider the scheduler cannot connect is not asked for.
    let microsoft = calendar(signed_in()).ui(event(CalendarAction::Connect {
        provider: "microsoft".to_owned(),
    }));
    assert!(!calendar_view(&microsoft).connecting);

    let opened = signed_in_ready(session, Ok(SchedulingSecret::new(LINK)));
    assert!(
        opened
            .pending
            .iter()
            .any(|e| matches!(e, Effect::OpenOneTimeUrl { .. }))
    );
    let view = calendar_view(&opened);
    assert!(view.awaiting_browser && !view.connecting);
    let json = serde_json::to_string(&screen_view(&opened.model)).unwrap();
    assert!(!json.contains("single-use"), "{json}");
    // Refresh reads what is connected now.
    let refreshed = opened.ui(UiEvent::Refresh);
    assert!(sent(&refreshed, |r| matches!(
        r,
        SchedulingAdminRequest::CalendarStatus
    )));
    assert!(!calendar_view(&refreshed).awaiting_browser);

    let refused = signed_in_ready(
        connecting(signed_in()),
        Err(ApiError::Conflict(ErrorDetail::default())),
    );
    let view = calendar_view(&refused);
    assert_eq!(
        view.connect_failure.map(|f| f.message).as_deref(),
        Some("Scheduling is still being set up for this workspace. Try again shortly.")
    );
    assert!(!view.awaiting_browser && !view.connecting);
    let away = calendar_view(&refused.ui(event(CalendarAction::DismissConnectFailure)));
    assert_eq!(away.connect_failure, None);
}

/// An account's calendars are read for the dialog, which offers its save
/// once they are; each has its switch, and those not read-only can receive
/// bookings. The save goes whole, then the account receiving bookings, and
/// the dialog says so; a refusal says why with the choices kept.
#[test]
fn an_accounts_calendars_are_chosen_and_saved() {
    let loading = calendar_view(
        &calendar(signed_in()).ui(event(CalendarAction::OpenCalendars {
            connection_id: FIRST.to_owned(),
        })),
    );
    let open = loading.picker.expect("open");
    assert_eq!(open.status, district_ffi::LoadStatus::Loading);
    assert!(open.calendars.is_empty() && !open.editable && !open.dialog.can_confirm);
    assert_eq!(open.loading_label, "Loading calendars…");

    let session = picker(signed_in());
    let open = calendar_view(&session).picker.expect("open");
    assert_eq!(open.connection_id, FIRST);
    assert_eq!(open.dialog.title, "Calendars in contract@example.com");
    assert_eq!(open.dialog.confirm, "Save changes");
    assert_eq!(open.dialog.close, "Cancel");
    assert!(open.dialog.can_confirm && open.editable && !open.dialog.destructive);
    assert_eq!(
        [
            open.conflicts_label.as_str(),
            &open.conflicts_hint,
            &open.destination_label,
            &open.destination_hint,
        ],
        [
            "Checked for conflicts",
            "A booking is refused when it clashes with an event on a checked calendar.",
            "Receives bookings",
            "New bookings are written to this calendar."
        ]
    );
    let rows: Vec<(&str, bool)> = open
        .calendars
        .iter()
        .map(|c| (c.name.as_str(), c.checks_conflicts))
        .collect();
    assert_eq!(
        rows,
        [("Contract Member", true), ("Statutory holidays", false)]
    );
    let destinations: Vec<(&str, bool)> = open
        .destinations
        .iter()
        .map(|c| (c.value.as_str(), c.selected))
        .collect();
    assert_eq!(
        destinations,
        [
            ("contract@example.com", true),
            ("holidays@example.com", false)
        ]
    );

    let chosen = session
        .ui(event(CalendarAction::ChooseDestination {
            calendar_id: "holidays@example.com".to_owned(),
        }))
        .ui(event(CalendarAction::SetCheckConflicts {
            calendar_id: "contract@example.com".to_owned(),
            on: false,
        }));
    let open = calendar_view(&chosen).picker.expect("open");
    assert!(open.destinations[1].selected && !open.destinations[0].selected);
    assert!(!open.calendars[0].checks_conflicts);

    let saving = picker_saving(signed_in());
    assert!(sent(&saving, |r| matches!(
        r,
        SchedulingAdminRequest::SetCalendars { .. }
    )));
    let open = calendar_view(&saving).picker.expect("open");
    assert!(open.dialog.working && !open.dialog.can_confirm && !open.editable);
    assert_eq!(open.dialog.confirm, "Saving…");

    let saved = picker_saved(signed_in());
    let view = calendar_view(&saved);
    let open = view.picker.expect("still open");
    assert_eq!(open.notice.as_deref(), Some("Calendars saved"));
    assert_eq!(view.status, district_ffi::LoadStatus::Ready);
    assert_eq!(
        view.connections[0].conflicts, "Contract Member",
        "the section read again"
    );
    let closed = calendar_view(&saved.ui(event(CalendarAction::CloseDialog)));
    assert_eq!(closed.picker, None);

    let refused = set_calendars(picker_saving(signed_in()), Err(server_error()));
    let open = calendar_view(&refused).picker.expect("still open");
    assert!(open.dialog.failure.is_some() && open.dialog.can_confirm);
    assert!(open.calendars[1].checks_conflicts, "the choice is kept");
    assert_eq!(open.notice, None);

    // A read that failed says why, offers no save, and is asked for again.
    let failed = first_calendars(
        calendar(signed_in()).ui(event(CalendarAction::OpenCalendars {
            connection_id: FIRST.to_owned(),
        })),
        Err(server_error()),
    );
    let open = calendar_view(&failed).picker.expect("open");
    assert!(matches!(
        open.status,
        district_ffi::LoadStatus::Failed { .. }
    ));
    assert!(!open.dialog.can_confirm);
    let again = failed.ui(event(CalendarAction::OpenCalendars {
        connection_id: open.connection_id,
    }));
    assert!(sent(&again, |r| matches!(
        r,
        SchedulingAdminRequest::Calendars { id, .. } if id == FIRST
    )));
}

/// The CalDAV form: where the calendar lives decides whether its server's
/// address shows; each field is refused before anything is sent; the connect
/// sends the app password and no view holds it; once connected the form is
/// emptied and says which account connected; a refusal says what to check.
#[test]
fn a_caldav_account_is_checked_then_connected_and_its_password_never_shows() {
    let session = caldav(signed_in());
    let form_view = calendar_view(&session).caldav.expect("open");
    assert_eq!(form_view.dialog.title, "Connect a CalDAV calendar");
    assert_eq!(form_view.dialog.confirm, "Connect calendar");
    assert_eq!(form_view.dialog.close, "Cancel");
    assert!(form_view.dialog.can_confirm && form_view.editable);
    let presets: Vec<(&str, &str, bool)> = form_view
        .presets
        .iter()
        .map(|p| (p.value.as_str(), p.label.as_str(), p.selected))
        .collect();
    assert_eq!(
        presets,
        [
            ("icloud", "iCloud", true),
            ("fastmail", "Fastmail", false),
            ("nextcloud", "Nextcloud", false),
            ("custom", "Other server URL", false)
        ]
    );
    assert_eq!(
        form_view.preset_note,
        "Apple's calendar. Create an app-specific password at appleid.apple.com."
    );
    assert!(!form_view.shows_server_url);
    assert_eq!(
        [
            form_view.preset_label.as_str(),
            &form_view.server_url_label,
            &form_view.server_url_hint,
            &form_view.username_label,
            &form_view.password_label,
            &form_view.password_hint,
        ],
        [
            "Where the calendar lives",
            "Server URL",
            "The base address of the CalDAV server.",
            "Username",
            "App password",
            "An app-specific password from the provider, not the account password."
        ]
    );

    // Field by field, before anything is sent.
    let refused = |typed: CaldavFormView| {
        let session = caldav(signed_in())
            .ui(event(CalendarAction::EditCaldav { form: typed }))
            .ui(event(CalendarAction::ConnectCaldav));
        assert!(!sent(&session, |r| matches!(
            r,
            SchedulingAdminRequest::ConnectCaldav { .. }
        )));
        calendar_view(&session).caldav.expect("open")
    };
    let missing = refused(form("nextcloud", "", "ada", PASSWORD.clone()));
    assert!(missing.shows_server_url);
    assert_eq!(
        missing.server_url_error.as_deref(),
        Some("Enter the server URL.")
    );
    assert_eq!(
        (missing.username_error, missing.password_error),
        (None, None)
    );
    assert_eq!(
        refused(form(
            "custom",
            "https://10.0.0.4/dav",
            "ada",
            PASSWORD.clone()
        ))
        .server_url_error
        .as_deref(),
        Some("Enter the server's hostname rather than an IP address.")
    );
    assert_eq!(
        refused(form("fastmail", "", " ", PASSWORD.clone()))
            .username_error
            .as_deref(),
        Some("Enter the username.")
    );
    let no_password = refused(form("icloud", "", "ada", String::new()));
    assert_eq!(
        no_password.password_error.as_deref(),
        Some("Enter the app password.")
    );
    assert_eq!(no_password.username, "ada");
    // A change clears the refusal.
    let edited = caldav(signed_in())
        .ui(event(CalendarAction::EditCaldav {
            form: form("icloud", "", "ada", String::new()),
        }))
        .ui(event(CalendarAction::ConnectCaldav))
        .ui(event(CalendarAction::EditCaldav {
            form: form("icloud", "", "ada", PASSWORD.clone()),
        }));
    assert_eq!(
        calendar_view(&edited).caldav.expect("open").password_error,
        None
    );

    let connecting = caldav_connecting(signed_in());
    assert!(sent(&connecting, |r| matches!(
        r,
        SchedulingAdminRequest::ConnectCaldav { .. }
    )));
    let open = calendar_view(&connecting).caldav.expect("open");
    assert!(open.dialog.working && !open.dialog.can_confirm && !open.editable);
    assert_eq!(open.dialog.confirm, "Connecting…");
    assert_eq!(open.username, "ada@example.com");
    let json = serde_json::to_string(&screen_view(&connecting.model)).unwrap();
    assert!(!json.contains(PASSWORD.as_str()), "{json}");
    assert!(!format!("{open:?}").contains(PASSWORD.as_str()));

    let connected = caldav_connected(signed_in());
    let view = calendar_view(&connected);
    let open = view.caldav.expect("still open");
    assert_eq!(
        open.notice.as_deref(),
        Some("Calendar connected: caldav@example.com")
    );
    assert_eq!((open.username.as_str(), open.server_url.as_str()), ("", ""));
    assert!(open.presets[0].selected, "the place is kept");
    assert_eq!(view.status, district_ffi::LoadStatus::Ready);
    let closed = calendar_view(&connected.ui(event(CalendarAction::CloseDialog)));
    assert_eq!(closed.caldav, None);

    let failed = caldav_answer(caldav_connecting(signed_in()), Err(server_error()));
    let open = calendar_view(&failed).caldav.expect("still open");
    assert!(open.dialog.failure.is_some() && open.editable);
    assert_eq!(open.notice, None);
    assert_eq!(open.username, "ada@example.com", "the form is kept");
}

/// A disconnect is asked once more, naming the account, its confirm never
/// the default; once it lands the section is read again, and an account
/// that received bookings leaves a warning that none does now. A refusal
/// says why; both are put away.
#[test]
fn a_disconnect_is_asked_once_more_and_warns_when_nothing_receives_bookings() {
    let session = asked(signed_in(), FIRST);
    let ask = calendar_view(&session).disconnect.expect("asked");
    assert_eq!(ask.title, "Disconnect contract@example.com?");
    assert_eq!(
        ask.body,
        "Bookings stop syncing to it. Existing events stay."
    );
    assert_eq!(ask.confirm, "Disconnect");
    assert_eq!(ask.close, "Keep it connected");
    assert!(ask.destructive && ask.can_confirm && !ask.working);
    let kept = calendar_view(&session.ui(event(CalendarAction::CancelDisconnect)));
    assert_eq!(kept.disconnect, None);

    let disconnecting = asked(signed_in(), FIRST).ui(event(CalendarAction::ConfirmDisconnect));
    assert!(sent(&disconnecting, |r| matches!(
        r,
        SchedulingAdminRequest::DeleteConnection { .. }
    )));
    let view = calendar_view(&disconnecting);
    assert_eq!(view.disconnect, None);
    assert!(view.disconnecting && !view.can_disconnect);

    let gone = disconnected(signed_in());
    let view = calendar_view(&gone);
    assert_eq!(
        view.disconnect_notice.as_deref(),
        Some(
            "No calendar receives new bookings now. Connect one, or choose a calendar in another account."
        )
    );
    assert!(view.can_disconnect && !view.disconnecting);
    let away = calendar_view(&gone.ui(event(CalendarAction::DismissDisconnect)));
    assert_eq!(away.disconnect_notice, None);

    // The CalDAV account did not receive bookings: nothing to warn of.
    let other = read(delete_connection(
        asked(signed_in(), SECOND).ui(event(CalendarAction::ConfirmDisconnect)),
        Ok(contracts::json("district-scheduling-ok.json")),
    ));
    assert_eq!(calendar_view(&other).disconnect_notice, None);

    let failed = delete_connection(
        asked(signed_in(), FIRST).ui(event(CalendarAction::ConfirmDisconnect)),
        Err(server_error()),
    );
    let view = calendar_view(&failed);
    assert!(view.disconnect_failure.is_some() && view.can_disconnect);
    let away = calendar_view(&failed.ui(event(CalendarAction::DismissDisconnect)));
    assert_eq!(away.disconnect_failure, None);
}

#[test]
fn each_action_is_its_core_event() {
    use CalendarAction as A;
    use CalendarEvent as E;
    let s = |v: &str| v.to_owned();
    let to_core = |e| Event::SchedulingAdmin(SchedulingAdminEvent::Calendar(e));
    let typed = form(
        "nextcloud",
        "https://dav.example.com",
        "ada",
        PASSWORD.clone(),
    );
    let core_form = CaldavForm {
        preset: CaldavPreset::Nextcloud,
        server_url: s("https://dav.example.com"),
        username: s("ada"),
        app_password: SchedulingSecret::new(PASSWORD.as_str()),
    };
    for (sent, event_sent) in [
        (
            A::Connect {
                provider: s("google"),
            },
            E::Connect(s("google")),
        ),
        (A::DismissConnectFailure, E::DismissConnectFailure),
        (
            A::AskDisconnect {
                connection_id: s("c"),
            },
            E::AskDisconnect(s("c")),
        ),
        (A::CancelDisconnect, E::CancelDisconnect),
        (A::ConfirmDisconnect, E::ConfirmDisconnect),
        (A::DismissDisconnect, E::DismissDisconnect),
        (
            A::OpenCalendars {
                connection_id: s("c"),
            },
            E::OpenCalendars(s("c")),
        ),
        (
            A::SetCheckConflicts {
                calendar_id: s("k"),
                on: true,
            },
            E::SetCheckConflicts {
                calendar_id: s("k"),
                on: true,
            },
        ),
        (
            A::ChooseDestination {
                calendar_id: s("k"),
            },
            E::ChooseDestination(s("k")),
        ),
        (A::SaveCalendars, E::SaveCalendars),
        (A::OpenCaldav, E::OpenCaldav),
        (
            A::EditCaldav {
                form: typed.clone(),
            },
            E::EditCaldav(core_form),
        ),
        (A::ConnectCaldav, E::ConnectCaldav),
        (A::CloseDialog, E::CloseDialog),
    ] {
        assert_eq!(event(sent).events(), [to_core(event_sent)]);
    }
    // The password never prints; a place this version does not list is the
    // first.
    let printed = format!("{:?}", A::EditCaldav { form: typed });
    assert!(!printed.contains(PASSWORD.as_str()) && printed.contains("<redacted>"));
    assert!(printed.contains("nextcloud"));
    let unknown: CaldavForm = form("exchange", "", "ada", String::new()).into();
    assert_eq!(unknown.preset, CaldavPreset::Icloud);
    // The password travels as UTF-8, whatever it holds.
    let outside_ascii: String = PASSWORD
        .chars()
        .chain([char::REPLACEMENT_CHARACTER, char::MAX])
        .collect();
    let accented: CaldavForm = form("icloud", "", "ada", outside_ascii.clone()).into();
    assert_eq!(accented.app_password.expose(), outside_ascii);
}

/// No projection ever holds the app password: every snapshot case's screen
/// and shell, the CalDAV ones that typed it among them, serialised whole.
#[test]
fn no_projection_holds_the_app_password() {
    let cases = crate::cases();
    assert!(
        cases
            .iter()
            .any(|(name, _)| *name == "scheduling-admin-calendar-caldav-connecting"),
        "a case with the password typed and on its way"
    );
    for (name, session) in cases {
        let screen = serde_json::to_string(&screen_view(&session.model)).unwrap();
        let shell = serde_json::to_string(&district_ffi::shell_view(&session.model)).unwrap();
        assert!(!screen.contains(PASSWORD.as_str()), "{name}: {screen}");
        assert!(!shell.contains(PASSWORD.as_str()), "{name}: {shell}");
    }
}
