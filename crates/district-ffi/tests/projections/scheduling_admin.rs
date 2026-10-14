//! The booking scheduler's administration (src/scheduling_admin/): the
//! overview, the bookings and one booking; each later section's cases are in
//! its own file under scheduling_admin/.
//!
//! The fixtures' bookings are in September 2026. Where a booking must still
//! be to come (so that it can be acted on) it is moved to 2099, and the move
//! dialog's day is picked, so that no snapshot depends on the day it runs.

use district_core::{SchedulingAdminEvent, SchedulingSection};
use district_ffi::scheduling_admin::bookings::{
    BookingAction, BookingsAction, BookingsTab, SchedulingBookingView, SchedulingBookingsView,
};
use district_ffi::scheduling_admin::overview::{BookingDeskRowState, SchedulingOverviewView};
use district_ffi::scheduling_admin::{
    SchedulingAdminAction, SchedulingAdminPage, SchedulingAdminSection, SchedulingAdminView,
};
use district_model::{SchedulingAdminReply, SchedulingAdminRequest};

use super::*;

pub(crate) mod calendar;
pub(crate) mod developer;
pub(crate) mod event_types;
pub(crate) mod hours;
pub(crate) mod team;

/// This area's snapshot cases.
pub(crate) fn cases() -> Vec<Case> {
    vec![
        (
            "scheduling-admin-overview-loading",
            open(signed_in(), SchedulingAdminSection::Overview),
        ),
        ("scheduling-admin-overview", overview(signed_in())),
        (
            "scheduling-admin-overview-calendar-failed",
            overview_with(signed_in(), Err(server_error())),
        ),
        (
            "scheduling-admin-bookings-loading",
            open(signed_in(), SchedulingAdminSection::Bookings),
        ),
        ("scheduling-admin-bookings", bookings(signed_in())),
        (
            "scheduling-admin-bookings-failed",
            page(context(signed_in()), Err(server_error())),
        ),
        (
            "scheduling-admin-bookings-cancelled-empty",
            page(
                bookings(signed_in()).ui(list(BookingsAction::SelectTab {
                    tab: BookingsTab::Cancelled,
                })),
                Ok(empty_page()),
            ),
        ),
        ("scheduling-admin-bookings-viewer", bookings(viewer())),
        ("scheduling-admin-cancel-open", cancelling(signed_in())),
        (
            "scheduling-admin-cancel-reason-too-long",
            cancelling(signed_in())
                .ui(list(BookingsAction::EditCancelReason {
                    reason: "x".repeat(1001),
                }))
                .ui(list(BookingsAction::Confirm)),
        ),
        (
            "scheduling-admin-cancel-working",
            cancelling(signed_in()).ui(list(BookingsAction::Confirm)),
        ),
        (
            "scheduling-admin-cancel-refused",
            written(
                cancelling(signed_in()).ui(list(BookingsAction::Confirm)),
                Err(server_error()),
            ),
        ),
        ("scheduling-admin-cancel-landed", cancelled(signed_in())),
        (
            "scheduling-admin-reschedule-open",
            rescheduling(signed_in()),
        ),
        (
            "scheduling-admin-reschedule-picked",
            rescheduling(signed_in()).ui(list(BookingsAction::PickSlot {
                start: "2026-09-14T14:00:00Z".to_owned(),
            })),
        ),
        ("scheduling-admin-reassign-open", reassigning(signed_in())),
        ("scheduling-admin-booking", booking(signed_in())),
        (
            "scheduling-admin-booking-cancel-open",
            booking(signed_in()).ui(detail(BookingAction::OpenCancel)),
        ),
        (
            "scheduling-admin-section-not-built",
            signed_in().send(Event::Navigate(Route::SchedulingAdmin(
                SchedulingSection::Settings,
            ))),
        ),
    ]
}

fn action(action: SchedulingAdminAction) -> UiEvent {
    UiEvent::SchedulingAdmin { action }
}

fn list(action: BookingsAction) -> UiEvent {
    UiEvent::SchedulingAdmin {
        action: SchedulingAdminAction::Bookings { action },
    }
}

fn detail(action: BookingAction) -> UiEvent {
    UiEvent::SchedulingAdmin {
        action: SchedulingAdminAction::Booking { action },
    }
}

fn open(session: Session, section: SchedulingAdminSection) -> Session {
    session.ui(action(SchedulingAdminAction::Open { section }))
}

/// Answers the newest scheduling request `pick` finds: with `result`'s
/// `data`, read as the request's operation answers, or the failure.
fn answer_admin(
    mut session: Session,
    pick: fn(&SchedulingAdminRequest) -> bool,
    result: Result<Value, ApiError>,
) -> Session {
    let index = session
        .pending
        .iter()
        .rposition(|e| matches!(e, Effect::SchedulingAdmin { request, .. } if pick(request)))
        .unwrap_or_else(|| panic!("not pending: {:?}", session.pending));
    let Effect::SchedulingAdmin {
        ticket, request, ..
    } = session.pending.remove(index)
    else {
        unreachable!("picked a scheduling request");
    };
    let result = result.map(|value| {
        SchedulingAdminReply::Done(
            request
                .decode(value["data"].clone())
                .unwrap_or_else(|error| panic!("{request:?}: {error}")),
        )
    });
    session.send(Event::SchedulingAdminAnswered { ticket, result })
}

/// The booking pages' status, read: live.
fn status(session: Session) -> Session {
    session.answer(
        |e| matches!(e, Effect::LoadSchedulingStatus { .. }),
        |ticket| Event::SchedulingStatusLoaded {
            ticket,
            result: Ok(contracts::read("district-scheduling-status-ready.json")),
        },
    )
}

fn me(session: Session) -> Session {
    answer_admin(
        session,
        |r| matches!(r, SchedulingAdminRequest::Me),
        Ok(contracts::json("district-scheduling-me.json")),
    )
}

fn event_types(session: Session) -> Session {
    answer_admin(
        session,
        |r| matches!(r, SchedulingAdminRequest::EventTypes),
        Ok(contracts::json("district-scheduling-event-types.json")),
    )
}

/// The overview, every read answered.
fn overview(session: Session) -> Session {
    overview_with(
        session,
        Ok(contracts::json("district-scheduling-calendar-status.json")),
    )
}

/// The overview, its calendars read as `calendar` says and the rest answered.
fn overview_with(session: Session, calendar: Result<Value, ApiError>) -> Session {
    let session = event_types(me(status(open(session, SchedulingAdminSection::Overview))));
    let session = answer_admin(
        session,
        |r| matches!(r, SchedulingAdminRequest::CalendarStatus),
        calendar,
    );
    let session = answer_admin(
        session,
        |r| matches!(r, SchedulingAdminRequest::Rules { .. }),
        Ok(contracts::json("district-scheduling-rules.json")),
    );
    answer_admin(
        session,
        |r| matches!(r, SchedulingAdminRequest::Bookings(_)),
        Ok(contracts::json("district-scheduling-bookings.json")),
    )
}

/// The bookings list with what its first page waits for read: the status, the
/// profile and the event types.
fn context(session: Session) -> Session {
    event_types(me(status(open(session, SchedulingAdminSection::Bookings))))
}

/// The bookings fixture, its first booking moved to 2099 so that it is still
/// to come.
fn upcoming_page() -> Value {
    let mut value = contracts::json("district-scheduling-bookings.json");
    let first = &mut value["data"]["items"][0];
    first["start_at"] = json!("2099-09-14T13:00:00Z");
    first["end_at"] = json!("2099-09-14T13:30:00Z");
    value
}

fn empty_page() -> Value {
    json!({"ok": true, "data": {"items": [], "total": 0}})
}

/// The page being read, answered with `result`.
fn page(session: Session, result: Result<Value, ApiError>) -> Session {
    answer_admin(
        session,
        |r| matches!(r, SchedulingAdminRequest::Bookings(_)),
        result,
    )
}

/// The bookings list, read.
fn bookings(session: Session) -> Session {
    page(context(session), Ok(upcoming_page()))
}

fn cancelling(session: Session) -> Session {
    bookings(session).ui(list(BookingsAction::OpenCancel {
        booking_id: "bk-contract-1".to_owned(),
    }))
}

/// A write on its way, answered with `result`.
fn written(session: Session, result: Result<Value, ApiError>) -> Session {
    answer_admin(
        session,
        |r| {
            matches!(
                r,
                SchedulingAdminRequest::CancelBooking { .. }
                    | SchedulingAdminRequest::RescheduleBooking { .. }
                    | SchedulingAdminRequest::ReassignBooking { .. }
            )
        },
        result,
    )
}

/// A cancel that landed: the list is read again.
fn cancelled(session: Session) -> Session {
    let session = written(
        cancelling(session).ui(list(BookingsAction::Confirm)),
        Ok(contracts::json("district-scheduling-booking.json")),
    );
    page(session, Ok(upcoming_page()))
}

/// The move dialog on 14 September 2099, its times read.
fn rescheduling(session: Session) -> Session {
    let session = bookings(session)
        .ui(list(BookingsAction::OpenReschedule {
            booking_id: "bk-contract-1".to_owned(),
        }))
        .ui(list(BookingsAction::PickDay {
            day: "2099-09-14".to_owned(),
        }));
    answer_admin(
        session,
        |r| matches!(r, SchedulingAdminRequest::Slots { .. }),
        Ok(contracts::json("district-scheduling-slots.json")),
    )
}

/// The host dialog, its candidates read: the fixture's users and one more,
/// of whom the booking's host and the archived one are left out.
fn reassigning(session: Session) -> Session {
    let mut users = contracts::json("district-scheduling-users.json");
    users["data"].as_array_mut().unwrap().push(json!({
        "id": "sched-user-2",
        "email": "sam@example.com",
        "name": "",
        "is_admin": false,
        "is_owner": false,
        "role": "member",
        "archived": false
    }));
    let session = bookings(session).ui(list(BookingsAction::OpenReassign {
        booking_id: "bk-contract-1".to_owned(),
    }));
    answer_admin(
        session,
        |r| matches!(r, SchedulingAdminRequest::Users { .. }),
        Ok(users),
    )
}

/// One booking, opened from the list, its answers read.
fn booking(session: Session) -> Session {
    let session = bookings(session).ui(action(SchedulingAdminAction::OpenBooking {
        booking_id: "bk-contract-1".to_owned(),
    }));
    answer_admin(
        session,
        |r| matches!(r, SchedulingAdminRequest::BookingAnswers { .. }),
        Ok(contracts::json("district-scheduling-booking-answers.json")),
    )
}

fn admin_view(session: &Session) -> SchedulingAdminView {
    let ScreenView::SchedulingAdmin { view } = screen_view(&session.model) else {
        panic!("not the scheduling administration");
    };
    view
}

fn overview_view(session: &Session) -> SchedulingOverviewView {
    let SchedulingAdminPage::Overview { view } = admin_view(session).page else {
        panic!("not the overview");
    };
    view
}

fn bookings_view(session: &Session) -> SchedulingBookingsView {
    let SchedulingAdminPage::Bookings { view } = admin_view(session).page else {
        panic!("not the bookings");
    };
    view
}

fn booking_view(session: &Session) -> SchedulingBookingView {
    let SchedulingAdminPage::Booking { view } = admin_view(session).page else {
        panic!("not a booking");
    };
    view
}

/// The pages are the core's routes, under the booking pages: the pane keeps
/// Booking pages selected, and Back goes there.
#[test]
fn the_sections_are_under_the_booking_pages() {
    let session = overview(signed_in());
    assert_eq!(
        route(&session),
        Route::SchedulingAdmin(SchedulingSection::Overview)
    );
    let view = admin_view(&session);
    assert_eq!(view.title, "Overview");
    let selected: Vec<SchedulingAdminSection> = view
        .sections
        .iter()
        .filter(|entry| entry.selected)
        .map(|entry| entry.section)
        .collect();
    assert_eq!(selected, [SchedulingAdminSection::Overview]);
    assert_eq!(shell_json(&session)["nav_selected"], json!("Scheduling"));
    let back = session.ui(UiEvent::Back);
    assert_eq!(route(&back), Route::Scheduling);
}

/// Every row of the desk stands alone: a calendar read that failed blanks
/// its row and no other.
#[test]
fn a_failed_row_blanks_only_itself() {
    let view = overview_view(&overview_with(signed_in(), Err(server_error())));
    let failed: Vec<&str> = view
        .desk
        .iter()
        .filter(|row| row.state == BookingDeskRowState::Failed)
        .map(|row| row.label.as_str())
        .collect();
    assert_eq!(failed, ["Calendar"]);
    assert_eq!(view.coming_up.len(), 2);
}

/// The booking can be acted on by an agency, as the core allows; a viewer
/// reads the same list with no action, and one opened offers no cancel.
#[test]
fn only_a_member_who_may_change_things_is_offered_the_writes() {
    let agency = bookings_view(&bookings(signed_in()));
    let row = &agency.rows[0];
    assert!(row.can_cancel && row.can_reschedule && row.can_reassign);
    // The second booking is cancelled: nothing to do.
    let done = &agency.rows[1];
    assert!(!done.can_cancel && !done.can_reschedule && !done.can_reassign);
    let read_only = bookings_view(&bookings(viewer()));
    assert!(read_only.rows.iter().all(|row| !row.can_cancel));
    let opened = booking_view(&booking(viewer()));
    assert!(!opened.offers_cancel);
    assert!(booking_view(&booking(signed_in())).offers_cancel);
}

/// A refused cancel keeps the dialog and the reason, and says why in it.
#[test]
fn a_refused_write_stays_in_its_dialog() {
    let session = cancelling(signed_in()).ui(list(BookingsAction::EditCancelReason {
        reason: "Moved away".to_owned(),
    }));
    let working = bookings_view(&session.ui(list(BookingsAction::Confirm)));
    let dialog = working.cancel.expect("still open");
    assert!(dialog.dialog.working && !dialog.dialog.can_confirm);
    let refused = bookings_view(&written(
        cancelling(signed_in())
            .ui(list(BookingsAction::EditCancelReason {
                reason: "Moved away".to_owned(),
            }))
            .ui(list(BookingsAction::Confirm)),
        Err(server_error()),
    ));
    let dialog = refused.cancel.expect("still open");
    assert_eq!(dialog.reason, "Moved away");
    assert!(dialog.dialog.failure.is_some() && dialog.dialog.can_confirm);
    assert!(dialog.dialog.destructive);
    assert_eq!(refused.notice, None);
}

/// A write that landed closes its dialog and says so on the list, until put
/// away; the list is read again.
#[test]
fn a_landed_write_closes_its_dialog_and_says_so() {
    let landed = cancelled(signed_in());
    let view = bookings_view(&landed);
    assert_eq!(view.cancel, None);
    assert_eq!(view.notice.as_deref(), Some("Booking cancelled"));
    let away = bookings_view(&landed.ui(list(BookingsAction::CloseDialog)));
    assert_eq!(away.notice, None);
}

/// The move dialog asks for one day's times, and the confirm waits for one.
#[test]
fn a_move_needs_a_time() {
    let open = bookings_view(&rescheduling(signed_in()));
    let dialog = open.reschedule.expect("open");
    assert_eq!(dialog.day, "2099-09-14");
    assert!(!dialog.dialog.can_confirm);
    assert_eq!(dialog.slots.len(), 2);
    let picked = bookings_view(
        &rescheduling(signed_in()).ui(list(BookingsAction::PickSlot {
            start: dialog.slots[0].value.clone(),
        })),
    );
    let dialog = picked.reschedule.expect("open");
    assert!(dialog.dialog.can_confirm && dialog.slots[0].selected);
    let moved = written(
        rescheduling(signed_in())
            .ui(list(BookingsAction::PickSlot {
                start: dialog.slots[0].value.clone(),
            }))
            .ui(list(BookingsAction::Confirm)),
        Ok(contracts::json("district-scheduling-booking.json")),
    );
    let view = bookings_view(&moved);
    assert_eq!(view.reschedule, None);
    assert!(view.notice.is_some_and(|n| n.starts_with("Booking moved")));
}

/// The host dialog leaves out the host and the archived; a blank name is
/// shown as the address.
#[test]
fn the_host_dialog_offers_the_others() {
    let view = bookings_view(&reassigning(signed_in()));
    let dialog = view.reassign.expect("open");
    let hosts: Vec<(&str, &str)> = dialog
        .hosts
        .iter()
        .map(|h| (h.value.as_str(), h.label.as_str()))
        .collect();
    assert_eq!(hosts, [("sched-user-2", "sam@example.com")]);
    let picked = bookings_view(&reassigning(signed_in()).ui(list(BookingsAction::PickHost {
        host_id: "sched-user-2".to_owned(),
    })));
    assert!(picked.reassign.expect("open").dialog.can_confirm);
}

/// A dialog's read that failed says why, and offers it again.
#[test]
fn a_dialog_read_that_failed_is_offered_again() {
    let session = bookings(signed_in()).ui(list(BookingsAction::OpenReassign {
        booking_id: "bk-contract-1".to_owned(),
    }));
    let failed = answer_admin(
        session,
        |r| matches!(r, SchedulingAdminRequest::Users { .. }),
        Err(server_error()),
    );
    let dialog = bookings_view(&failed).reassign.expect("open");
    assert!(matches!(
        dialog.hosts_status,
        district_ffi::LoadStatus::Failed { .. }
    ));
    let again = failed.ui(list(BookingsAction::RetryDialogRead));
    assert!(again.pending.iter().any(|e| matches!(
        e,
        Effect::SchedulingAdmin { request, .. } if matches!(**request, SchedulingAdminRequest::Users { .. })
    )));
}

/// One booking: its row from the list, its answers, and the cancel; a cancel
/// that landed is said, and offered no more.
#[test]
fn one_booking_shows_its_answers_and_cancels() {
    let session = booking(signed_in());
    let view = booking_view(&session);
    assert_eq!(
        view.summary.as_ref().map(|r| r.id.as_str()),
        Some("bk-contract-1")
    );
    let answers: Vec<&str> = view.answers.iter().map(|a| a.value.as_str()).collect();
    assert_eq!(answers, ["District", "No answer"]);
    let session = session
        .ui(detail(BookingAction::OpenCancel))
        .ui(detail(BookingAction::EditCancelReason {
            reason: "Asked to".to_owned(),
        }))
        .ui(detail(BookingAction::Confirm));
    let landed = written(
        session,
        Ok(contracts::json("district-scheduling-booking.json")),
    );
    let view = booking_view(&landed);
    assert_eq!(view.cancel, None);
    assert_eq!(view.notice.as_deref(), Some("Booking cancelled"));
    assert!(!view.offers_cancel);
    let away = booking_view(&landed.ui(detail(BookingAction::CloseDialog)));
    assert_eq!(away.notice, None);
    // Back to the list, as it was.
    let back = booking(signed_in()).ui(UiEvent::Back);
    assert_eq!(
        route(&back),
        Route::SchedulingAdmin(SchedulingSection::Bookings)
    );
    assert_eq!(bookings_view(&back).rows.len(), 2);
}

/// A full page offers "Load more", which reads from where the rows end.
#[test]
fn a_full_page_offers_more() {
    let mut full = upcoming_page();
    let first = full["data"]["items"][0].clone();
    let items: Vec<Value> = (0..25)
        .map(|n| {
            let mut item = first.clone();
            item["id"] = json!(format!("bk-{n}"));
            item
        })
        .collect();
    full["data"]["items"] = json!(items);
    full["data"]["total"] = json!(60);
    let session = page(context(signed_in()), Ok(full));
    let view = bookings_view(&session);
    assert!(view.paging.can_load_more);
    assert_eq!(view.load_more, "Load more");
    let more = session.ui(list(BookingsAction::LoadMore));
    let view = bookings_view(&more);
    assert!(view.paging.loading_more);
    assert_eq!(view.load_more, "Loading…");
    let failed = page(more, Err(server_error()));
    assert!(bookings_view(&failed).paging.more_failure.is_some());
    assert_eq!(bookings_view(&failed).rows.len(), 25);
}

#[test]
fn each_action_is_its_core_event() {
    use district_core::scheduling_admin::{
        BookingEvent, BookingsEvent, BookingsView, SchedulingDay,
    };
    let bookings = |e| Event::SchedulingAdmin(SchedulingAdminEvent::Bookings(e));
    for (sent, event) in [
        (
            BookingsAction::SelectTab {
                tab: BookingsTab::Past,
            },
            bookings(BookingsEvent::SelectView(BookingsView::Past)),
        ),
        (BookingsAction::LoadMore, bookings(BookingsEvent::LoadMore)),
        (
            BookingsAction::EditCancelReason {
                reason: "r".to_owned(),
            },
            bookings(BookingsEvent::EditCancelReason("r".to_owned())),
        ),
        (
            BookingsAction::PickDay {
                day: "2099-09-14".to_owned(),
            },
            bookings(BookingsEvent::PickDay(
                SchedulingDay::parse("2099-09-14").unwrap(),
            )),
        ),
        (
            BookingsAction::PickSlot {
                start: "s".to_owned(),
            },
            bookings(BookingsEvent::PickSlot("s".to_owned())),
        ),
        (
            BookingsAction::PickHost {
                host_id: "h".to_owned(),
            },
            bookings(BookingsEvent::PickHost("h".to_owned())),
        ),
        (
            BookingsAction::RetryDialogRead,
            bookings(BookingsEvent::RetryDialogRead),
        ),
        (BookingsAction::Confirm, bookings(BookingsEvent::Confirm)),
        (
            BookingsAction::CloseDialog,
            bookings(BookingsEvent::CloseDialog),
        ),
    ] {
        assert_eq!(list(sent).events(), [event]);
    }
    for tab in [
        BookingsTab::Upcoming,
        BookingsTab::Cancelled,
        BookingsTab::All,
    ] {
        assert_eq!(list(BookingsAction::SelectTab { tab }).events().len(), 1);
    }
    // A day that is not one is no event.
    assert_eq!(
        list(BookingsAction::PickDay {
            day: "tomorrow".to_owned()
        })
        .events(),
        []
    );
    // Opening a dialog carries the time it was pressed at.
    for sent in [
        BookingsAction::OpenCancel {
            booking_id: "b".to_owned(),
        },
        BookingsAction::OpenReschedule {
            booking_id: "b".to_owned(),
        },
        BookingsAction::OpenReassign {
            booking_id: "b".to_owned(),
        },
    ] {
        let events: [Event; 1] = list(sent).events().try_into().unwrap();
        let [Event::SchedulingAdmin(SchedulingAdminEvent::Bookings(event))] = events else {
            panic!("not a bookings event");
        };
        assert!(matches!(
            event,
            BookingsEvent::OpenCancel { .. }
                | BookingsEvent::OpenReschedule { .. }
                | BookingsEvent::OpenReassign { .. }
        ));
    }
    let booking = |e| Event::SchedulingAdmin(SchedulingAdminEvent::Booking(e));
    for (sent, event) in [
        (BookingAction::OpenCancel, booking(BookingEvent::OpenCancel)),
        (
            BookingAction::EditCancelReason {
                reason: "r".to_owned(),
            },
            booking(BookingEvent::EditCancelReason("r".to_owned())),
        ),
        (BookingAction::Confirm, booking(BookingEvent::Confirm)),
        (
            BookingAction::CloseDialog,
            booking(BookingEvent::CloseDialog),
        ),
    ] {
        assert_eq!(detail(sent).events(), [event]);
    }
}
