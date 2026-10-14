//! The bookings: every booking a page at a time, by view (upcoming, past,
//! cancelled, all), and one booking's answers; cancelling a booking, moving
//! it to another free time, and handing it to another host, as District AI
//! for iPhone's `SchedulingBookingsView`, `SchedulingBookingDetailView` and
//! their sheets.
//!
//! The core decides what is offered, and this projects it: a booking can be
//! acted on while it is confirmed and has not ended, by a member who may
//! change things; a move needs the booking's event type; a hand-over needs
//! the scheduler's own administrator flag, never the workspace role. One
//! booking's page offers the cancel alone, as on iPhone: the move and the
//! hand-over need what only the list has read. Each write is a confirm-once
//! dialog (see the module above).

use std::time::SystemTime;

use district_core::scheduling_admin::{
    BookingDetailSection, BookingDialog, BookingEvent, BookingsEvent, BookingsSection,
    BookingsView, CancelDialog, ReassignDialog, RescheduleDialog, SchedulingDay,
};
use district_core::{Event, SchedulingAdmin, SchedulingAdminEvent};
use serde::Serialize;

use super::overview::tone;
use super::{DialogView, SchedulingAdminPage, dialog_view, landed, load_status, times_in};
use crate::views::{EmptyView, FactView, LoadStatus, PagingView};
use crate::workflows::RunTone;

/// Whether this version has the section's pages.
pub(crate) const BUILT: bool = true;

/// Which bookings the list shows.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash, Serialize, uniffi::Enum)]
pub enum BookingsTab {
    /// Confirmed and still to come, soonest first.
    Upcoming,
    /// Confirmed and over, newest first.
    Past,
    /// Cancelled, newest first.
    Cancelled,
    /// Every booking, newest first.
    All,
}

impl BookingsTab {
    fn view(self) -> BookingsView {
        match self {
            Self::Upcoming => BookingsView::Upcoming,
            Self::Past => BookingsView::Past,
            Self::Cancelled => BookingsView::Cancelled,
            Self::All => BookingsView::All,
        }
    }

    fn of(view: BookingsView) -> Self {
        match view {
            BookingsView::Upcoming => Self::Upcoming,
            BookingsView::Past => Self::Past,
            BookingsView::Cancelled => Self::Cancelled,
            BookingsView::All => Self::All,
        }
    }
}

/// One view of the list, as a tab.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct BookingsTabView {
    /// Which (`BookingsAction::SelectTab`).
    pub tab: BookingsTab,
    /// Its label.
    pub label: String,
    /// Whether it is the view showing.
    pub selected: bool,
}

/// One booking in the list.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct BookingRowView {
    /// The booking's id, which opens it (`SchedulingAdminAction::OpenBooking`).
    pub id: String,
    /// When it starts, a date and a time in the profile's zone.
    pub when: String,
    /// Who, what and with whom.
    pub subtitle: String,
    /// The first attendee's address, or empty.
    pub email: String,
    /// The status badge's label.
    pub status_label: String,
    /// Its tone.
    pub tone: RunTone,
    /// Whether "Cancel booking" is offered on it.
    pub can_cancel: bool,
    /// Whether "Move booking" is offered on it.
    pub can_reschedule: bool,
    /// Whether "Change host" is offered on it.
    pub can_reassign: bool,
}

/// The bookings list.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct SchedulingBookingsView {
    /// "Times in America/Toronto".
    pub times_in: String,
    /// The views, the one showing selected.
    pub tabs: Vec<BookingsTabView>,
    /// Where the first page's read stands.
    pub status: LoadStatus,
    /// The bookings read so far.
    pub rows: Vec<BookingRowView>,
    /// What the list says when the view has nothing.
    pub empty: EmptyView,
    /// "Load more" at the end of the list, and why the last page failed.
    pub paging: PagingView,
    /// "Load more", or "Loading…" while the next page is on its way.
    pub load_more: String,
    /// The row action that opens the cancel dialog, "Cancel booking".
    pub cancel_label: String,
    /// The row action that opens the move dialog, "Move booking".
    pub reschedule_label: String,
    /// The row action that opens the host dialog, "Change host".
    pub reassign_label: String,
    /// What the last write came to ("Booking cancelled"), once its dialog has
    /// closed, until it is put away (`BookingsAction::CloseDialog`).
    pub notice: Option<String>,
    /// The cancel dialog, while it is open.
    pub cancel: Option<CancelDialogView>,
    /// The move dialog, while it is open.
    pub reschedule: Option<RescheduleDialogView>,
    /// The host dialog, while it is open.
    pub reassign: Option<ReassignDialogView>,
}

/// Cancelling a booking, with an optional reason for the attendee's email.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct CancelDialogView {
    /// Its title, body, buttons and write.
    pub dialog: DialogView,
    /// The reason field's label.
    pub reason_label: String,
    /// Under it: optional, and it goes in the email.
    pub reason_hint: String,
    /// The reason, as typed.
    pub reason: String,
    /// Why the reason cannot be sent (too long), before anything was sent.
    pub reason_rejected: Option<String>,
}

/// Moving a booking to another free time, on a day the member picks.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct RescheduleDialogView {
    /// Its title, body, buttons and write.
    pub dialog: DialogView,
    /// "Times in America/Toronto": the zone the day and its times are in.
    pub times_in: String,
    /// The day picker's label.
    pub day_label: String,
    /// The day whose times show, `YYYY-MM-DD` (`BookingsAction::PickDay`).
    pub day: String,
    /// The day as a reader names it.
    pub day_text: String,
    /// The times' label.
    pub time_label: String,
    /// Where the read of the day's times stands; a failed one offers "Try
    /// again" (`BookingsAction::RetryDialogRead`).
    pub slots_status: LoadStatus,
    /// The day's free times.
    pub slots: Vec<ChoiceRowView>,
    /// Said when the day has no free time.
    pub no_slots: String,
    /// "Pick a time.", for a confirm with none chosen.
    pub selection_rejected: Option<String>,
}

/// Handing a booking to another host.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct ReassignDialogView {
    /// Its title, body, buttons and write.
    pub dialog: DialogView,
    /// The host list's label.
    pub host_label: String,
    /// Where the read of the candidates stands; a failed one offers "Try
    /// again" (`BookingsAction::RetryDialogRead`).
    pub hosts_status: LoadStatus,
    /// The scheduler's users who are not archived and not its host now.
    pub hosts: Vec<ChoiceRowView>,
    /// Said when nobody else can take it: a fact, not a failure.
    pub no_hosts: String,
}

/// One choice in a dialog's list: a time, or a host.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct ChoiceRowView {
    /// What is sent when it is chosen: the time's start, the host's id.
    pub value: String,
    /// What it reads.
    pub label: String,
    /// Whether it is chosen.
    pub selected: bool,
}

/// One booking: what the booker answered, and the cancel.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct SchedulingBookingView {
    /// The booking's id.
    pub id: String,
    /// Its row from the list (when, who, status), when the list has it.
    pub summary: Option<BookingRowView>,
    /// Where the read of the answers stands.
    pub status: LoadStatus,
    /// The answers' heading.
    pub answers_heading: String,
    /// The answers to the event type's questions, a blank one said as such.
    pub answers: Vec<FactView>,
    /// Said when the event type asks nothing.
    pub no_questions: String,
    /// The heading over the cancel, "Manage".
    pub manage_heading: String,
    /// Whether "Cancel booking" is offered: the member may change things and
    /// the booking can still be cancelled.
    pub offers_cancel: bool,
    /// "Cancel booking".
    pub cancel_label: String,
    /// "Booking cancelled", once the cancel has landed, until it is put away
    /// (`BookingAction::CloseDialog`).
    pub notice: Option<String>,
    /// The cancel dialog, while it is open.
    pub cancel: Option<CancelDialogView>,
}

/// Something the member did on the bookings list.
#[derive(Clone, Debug, PartialEq, Eq, uniffi::Enum)]
pub enum BookingsAction {
    /// Show another view, read from its first page.
    SelectTab {
        /// Which.
        tab: BookingsTab,
    },
    /// Read the next page.
    LoadMore,
    /// Open the cancel dialog for a booking.
    OpenCancel {
        /// The booking's id.
        booking_id: String,
    },
    /// Open the move dialog for a booking, on today in the profile's zone.
    OpenReschedule {
        /// The booking's id.
        booking_id: String,
    },
    /// Open the host dialog for a booking.
    OpenReassign {
        /// The booking's id.
        booking_id: String,
    },
    /// The cancel dialog's reason changed.
    EditCancelReason {
        /// What the field holds now.
        reason: String,
    },
    /// The move dialog's day changed, `YYYY-MM-DD`. A value that is not a
    /// day is no event.
    PickDay {
        /// The day.
        day: String,
    },
    /// A time was chosen, by its start.
    PickSlot {
        /// The time's start, as the dialog listed it.
        start: String,
    },
    /// A host was chosen.
    PickHost {
        /// Their scheduler user id.
        host_id: String,
    },
    /// Read the dialog's times or hosts again, after a failure.
    RetryDialogRead,
    /// The dialog's confirm.
    Confirm,
    /// Close the dialog, or put away what its write came to.
    CloseDialog,
}

/// Something the member did on one booking.
#[derive(Clone, Debug, PartialEq, Eq, uniffi::Enum)]
pub enum BookingAction {
    /// Open the cancel dialog.
    OpenCancel,
    /// Its reason changed.
    EditCancelReason {
        /// What the field holds now.
        reason: String,
    },
    /// Its confirm.
    Confirm,
    /// Close it, or put away what its write came to.
    CloseDialog,
}

/// The core events `action` on the list is, as of `now`.
pub(crate) fn events(action: BookingsAction, now: SystemTime) -> Vec<Event> {
    let event = match action {
        BookingsAction::SelectTab { tab } => BookingsEvent::SelectView(tab.view()),
        BookingsAction::LoadMore => BookingsEvent::LoadMore,
        BookingsAction::OpenCancel { booking_id } => BookingsEvent::OpenCancel { booking_id, now },
        BookingsAction::OpenReschedule { booking_id } => {
            BookingsEvent::OpenReschedule { booking_id, now }
        }
        BookingsAction::OpenReassign { booking_id } => {
            BookingsEvent::OpenReassign { booking_id, now }
        }
        BookingsAction::EditCancelReason { reason } => BookingsEvent::EditCancelReason(reason),
        BookingsAction::PickDay { day } => match SchedulingDay::parse(&day) {
            Some(day) => BookingsEvent::PickDay(day),
            None => return Vec::new(),
        },
        BookingsAction::PickSlot { start } => BookingsEvent::PickSlot(start),
        BookingsAction::PickHost { host_id } => BookingsEvent::PickHost(host_id),
        BookingsAction::RetryDialogRead => BookingsEvent::RetryDialogRead,
        BookingsAction::Confirm => BookingsEvent::Confirm,
        BookingsAction::CloseDialog => BookingsEvent::CloseDialog,
    };
    vec![Event::SchedulingAdmin(SchedulingAdminEvent::Bookings(
        event,
    ))]
}

/// The core events `action` on one booking is.
pub(crate) fn detail_events(action: BookingAction) -> Vec<Event> {
    let event = match action {
        BookingAction::OpenCancel => BookingEvent::OpenCancel,
        BookingAction::EditCancelReason { reason } => BookingEvent::EditCancelReason(reason),
        BookingAction::Confirm => BookingEvent::Confirm,
        BookingAction::CloseDialog => BookingEvent::CloseDialog,
    };
    vec![Event::SchedulingAdmin(SchedulingAdminEvent::Booking(event))]
}

/// The list's page, as of `now`, for a member who may change things when
/// `can_change`. Before the list is entered, its first page is on its way.
pub(crate) fn list_page(
    admin: &SchedulingAdmin,
    can_change: bool,
    now: SystemTime,
) -> SchedulingAdminPage {
    let mut fresh = BookingsSection::default();
    fresh.timezone = "UTC".to_owned();
    SchedulingAdminPage::Bookings {
        view: list_view(admin.bookings.as_ref().unwrap_or(&fresh), can_change, now),
    }
}

fn list_view(
    section: &BookingsSection,
    can_change: bool,
    now: SystemTime,
) -> SchedulingBookingsView {
    let mut paging = PagingView::from(&section.paging);
    // "Load more" is offered under rows read, never before the first page.
    paging.can_load_more &= section.list.value().is_some();
    let (mut cancel, mut reschedule, mut reassign, mut notice) = (None, None, None, None);
    match &section.dialog {
        Some(BookingDialog::Cancel(dialog)) => {
            notice = landed(&dialog.write);
            cancel = cancel_view(dialog);
        }
        Some(BookingDialog::Reschedule(dialog)) => {
            notice = landed(&dialog.write);
            reschedule = reschedule_view(dialog);
        }
        Some(BookingDialog::Reassign(dialog)) => {
            notice = landed(&dialog.write);
            reassign = reassign_view(dialog);
        }
        None => {}
    }
    SchedulingBookingsView {
        times_in: section.times_in(),
        tabs: BookingsView::ALL
            .into_iter()
            .map(|view| BookingsTabView {
                tab: BookingsTab::of(view),
                label: view.label().to_owned(),
                selected: view == section.view,
            })
            .collect(),
        status: load_status(&section.list),
        rows: rows(section, can_change, now),
        empty: EmptyView::new(BookingsSection::empty_title(), section.view.empty_body()),
        paging,
        load_more: section.load_more_label().to_owned(),
        cancel_label: CancelDialog::confirm_label().to_owned(),
        reschedule_label: RescheduleDialog::confirm_label().to_owned(),
        reassign_label: ReassignDialog::confirm_label().to_owned(),
        notice,
        cancel,
        reschedule,
        reassign,
    }
}

fn rows(section: &BookingsSection, can_change: bool, now: SystemTime) -> Vec<BookingRowView> {
    section
        .rows(can_change, now)
        .into_iter()
        .map(|row| BookingRowView {
            id: row.id,
            when: row.when,
            subtitle: row.subtitle,
            email: row.email,
            status_label: row.status,
            tone: tone(row.tone),
            can_cancel: row.can_cancel,
            can_reschedule: row.can_reschedule,
            can_reassign: row.can_reassign,
        })
        .collect()
}

/// The cancel dialog, until its cancel lands.
fn cancel_view(dialog: &CancelDialog) -> Option<CancelDialogView> {
    landed(&dialog.write).is_none().then(|| CancelDialogView {
        dialog: dialog_view(
            [
                CancelDialog::title(),
                CancelDialog::body(),
                CancelDialog::confirm_label(),
                CancelDialog::keep_label(),
            ],
            &dialog.write,
            true,
            true,
        ),
        reason_label: CancelDialog::reason_label().to_owned(),
        reason_hint: CancelDialog::reason_hint().to_owned(),
        reason: dialog.reason.clone(),
        reason_rejected: dialog.reason_rejected.map(str::to_owned),
    })
}

/// The move dialog, until its move lands.
fn reschedule_view(dialog: &RescheduleDialog) -> Option<RescheduleDialogView> {
    landed(&dialog.write)
        .is_none()
        .then(|| RescheduleDialogView {
            dialog: dialog_view(
                [
                    RescheduleDialog::title(),
                    RescheduleDialog::body(),
                    RescheduleDialog::confirm_label(),
                    RescheduleDialog::cancel_label(),
                ],
                &dialog.write,
                dialog.selected.is_some(),
                false,
            ),
            times_in: times_in(&dialog.timezone),
            day_label: RescheduleDialog::day_label().to_owned(),
            day: dialog.day.key(),
            day_text: dialog.day.label(),
            time_label: RescheduleDialog::time_label().to_owned(),
            slots_status: load_status(&dialog.slots),
            slots: dialog
                .slots
                .value()
                .map(|slots| {
                    slots
                        .iter()
                        .map(|slot| ChoiceRowView {
                            value: slot.start.clone(),
                            label: dialog.slot_label(&slot.start),
                            selected: dialog.selected.as_deref() == Some(slot.start.as_str()),
                        })
                        .collect()
                })
                .unwrap_or_default(),
            no_slots: RescheduleDialog::no_slots().to_owned(),
            selection_rejected: dialog.selection_rejected.map(str::to_owned),
        })
}

/// The host dialog, until its hand-over lands.
fn reassign_view(dialog: &ReassignDialog) -> Option<ReassignDialogView> {
    landed(&dialog.write).is_none().then(|| ReassignDialogView {
        dialog: dialog_view(
            [
                ReassignDialog::title(),
                ReassignDialog::body(),
                ReassignDialog::confirm_label(),
                ReassignDialog::cancel_label(),
            ],
            &dialog.write,
            dialog.selected.is_some(),
            false,
        ),
        host_label: ReassignDialog::host_label().to_owned(),
        hosts_status: load_status(&dialog.hosts),
        hosts: dialog
            .hosts
            .value()
            .map(|hosts| {
                hosts
                    .iter()
                    .map(|host| ChoiceRowView {
                        value: host.id.clone(),
                        label: ReassignDialog::host_name(host).to_owned(),
                        selected: dialog.selected.as_deref() == Some(host.id.as_str()),
                    })
                    .collect()
            })
            .unwrap_or_default(),
        no_hosts: ReassignDialog::no_hosts().to_owned(),
    })
}

/// One booking's page, as of `now`. Its row comes from the list, which stays
/// while the booking shows.
pub(crate) fn detail_page(
    admin: &SchedulingAdmin,
    id: &str,
    can_change: bool,
    now: SystemTime,
) -> SchedulingAdminPage {
    let fresh = BookingDetailSection {
        id: id.to_owned(),
        answers: district_core::scheduling_admin::SectionLoad::Loading,
        cancel: None,
    };
    let section = admin
        .booking
        .as_ref()
        .filter(|section| section.id == id)
        .unwrap_or(&fresh);
    let summary = admin.bookings.as_ref().and_then(|list| {
        rows(list, can_change, now)
            .into_iter()
            .find(|row| row.id == id)
    });
    SchedulingAdminPage::Booking {
        view: detail_view(section, summary, can_change),
    }
}

fn detail_view(
    section: &BookingDetailSection,
    summary: Option<BookingRowView>,
    can_change: bool,
) -> SchedulingBookingView {
    let notice = section
        .cancel
        .as_ref()
        .and_then(|dialog| landed(&dialog.write));
    // A booking the list says is over, or already cancelled, offers nothing;
    // one opened without the list is the core's to judge, as on iPhone.
    let cancellable = summary.as_ref().is_none_or(|row| row.can_cancel);
    SchedulingBookingView {
        id: section.id.clone(),
        offers_cancel: can_change && cancellable && notice.is_none(),
        summary,
        status: load_status(&section.answers),
        answers_heading: BookingDetailSection::answers_heading().to_owned(),
        answers: section
            .answers
            .value()
            .map(|answers| {
                answers
                    .iter()
                    .map(|answer| FactView {
                        label: answer.label.clone(),
                        value: BookingDetailSection::answer_value(answer).to_owned(),
                    })
                    .collect()
            })
            .unwrap_or_default(),
        no_questions: BookingDetailSection::no_questions().to_owned(),
        manage_heading: BookingDetailSection::manage_heading().to_owned(),
        cancel_label: CancelDialog::confirm_label().to_owned(),
        notice,
        cancel: section.cancel.as_ref().and_then(cancel_view),
    }
}
