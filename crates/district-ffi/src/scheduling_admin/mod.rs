//! The booking scheduler's administration, natively: one section at a time
//! under the booking pages screen, each the core's
//! `Route::SchedulingAdmin(SchedulingSection)` and its state in
//! `SignedIn::scheduling_admin`, as District AI for iPhone's
//! `SchedulingHubView` lists them.
//!
//! # How a section is built
//!
//! Each section is one file here, built by its own pull request, and the
//! sections share the rest of this module. A section's pull request:
//!
//! - adds `pub mod <section>;` below, with `pub(crate) const BUILT: bool =
//!   true;`, its view records, its `<Section>Action` and the core events each
//!   one is (`events`), and the projection of its page (`page`);
//! - replaces its `false` in [`SchedulingAdminSection::built`] with its
//!   `BUILT`, which puts it in the section navigation and the booking pages
//!   screen's list;
//! - replaces its arm of `page` in this file (now
//!   [`ScreenView::unavailable`]) with its page, as a variant of
//!   [`SchedulingAdminPage`], and adds a variant of [`SchedulingAdminAction`]
//!   carrying its action, dispatched in [`events_at`];
//! - on the C# side, a `UserControl` under `Views/Scheduling/` and a view
//!   model under `Presentation/Scheduling/`, which `SchedulingAdminPage`
//!   shows for its variant.
//!
//! # What the sections share
//!
//! - Where a read stands ([`load_status`]): a section's read, and a dialog's,
//!   is the core's `SectionLoad`, and a failed one is never an empty list.
//! - The confirm-once dialog ([`DialogView`], [`dialog_view`]): the core owns
//!   each dialog and its write. The confirm is pressed once (the core refuses a
//!   second while the write is on its way, and the button is off meanwhile);
//!   a refusal stays in the dialog with the edits kept; a write that landed
//!   closes the dialog, and its sentence ([`landed`]) shows on the section
//!   until it is put away, which is the dialog's own "close" event. A
//!   destructive confirm is never the default button.
//! - Failure text: every refusal is the core's sentence, one of the web
//!   dashboard's five, and a dialog's or a row's failure says it where it
//!   happened.
//! - Paging, for a list read a page at a time: the core's `Paging`, as
//!   [`PagingView`](crate::views::PagingView).
//! - Time: every time is the core's, already in the zone of the member's
//!   scheduler profile, which each section names ("Times in ...").

pub mod bookings;
pub mod overview;

use std::time::SystemTime;

use district_core::scheduling_admin::{SectionLoad, WriteState};
use district_core::{Event, FailureText, Model, Route, SchedulingSection, SignedIn};
use district_l10n::scheduling_strings as ss;
use serde::Serialize;

use crate::l10n::{l, s};
use crate::screen::ScreenView;
use crate::views::{FailureView, LoadStatus, failure};

/// A section of the administration, as the section navigation lists it. A
/// detail (one booking, one event type) is listed as its list.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash, Serialize, uniffi::Enum)]
pub enum SchedulingAdminSection {
    /// The booking desk at a glance, and what is coming up.
    Overview,
    /// The event types.
    EventTypes,
    /// The working hours.
    Hours,
    /// The bookings.
    Bookings,
    /// The member's calendars.
    Calendar,
    /// The scheduler's users and teams.
    Team,
    /// The scheduler's settings.
    Settings,
    /// API keys, connected apps and webhooks.
    Developer,
}

impl SchedulingAdminSection {
    /// The core's section, and its route.
    pub(crate) fn section(self) -> SchedulingSection {
        match self {
            Self::Overview => SchedulingSection::Overview,
            Self::EventTypes => SchedulingSection::EventTypes,
            Self::Hours => SchedulingSection::Hours,
            Self::Bookings => SchedulingSection::Bookings,
            Self::Calendar => SchedulingSection::Calendar,
            Self::Team => SchedulingSection::Team,
            Self::Settings => SchedulingSection::Settings,
            Self::Developer => SchedulingSection::Developer,
        }
    }

    /// The entry `section` is listed under: a detail's list.
    pub(crate) fn of(section: &SchedulingSection) -> Self {
        match section {
            SchedulingSection::Overview => Self::Overview,
            SchedulingSection::EventTypes | SchedulingSection::EventType { .. } => Self::EventTypes,
            SchedulingSection::Hours => Self::Hours,
            SchedulingSection::Bookings | SchedulingSection::Booking { .. } => Self::Bookings,
            SchedulingSection::Calendar => Self::Calendar,
            SchedulingSection::Team => Self::Team,
            SchedulingSection::Settings => Self::Settings,
            SchedulingSection::Developer => Self::Developer,
        }
    }

    /// Whether this version has the section's pages. A section's pull request
    /// replaces its `false` with its module's `BUILT`.
    pub(crate) fn built(self) -> bool {
        match self {
            Self::Overview => overview::BUILT,
            Self::Bookings => bookings::BUILT,
            Self::EventTypes
            | Self::Hours
            | Self::Calendar
            | Self::Team
            | Self::Settings
            | Self::Developer => false,
        }
    }
}

/// Whether this version has the page of `section` (crate::nav::built).
pub(crate) fn built(section: &SchedulingSection) -> bool {
    SchedulingAdminSection::of(section).built()
}

/// One entry of the section navigation, and one row of the booking pages
/// screen's list.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct SchedulingSectionEntryView {
    /// The section it opens (`SchedulingAdminAction::Open`).
    pub section: SchedulingAdminSection,
    /// Its title, the web dashboard's name for it.
    pub title: String,
    /// What it holds, the row's second line.
    pub subtitle: String,
    /// Whether it is the section showing.
    pub selected: bool,
}

/// The built sections, in the web dashboard's order, with `selected` the one
/// showing (none on the booking pages screen).
pub(crate) fn entries(selected: Option<SchedulingAdminSection>) -> Vec<SchedulingSectionEntryView> {
    SchedulingSection::listed()
        .into_iter()
        .map(|section| (SchedulingAdminSection::of(&section), section))
        .filter(|(entry, _)| entry.built())
        .map(|(entry, section)| SchedulingSectionEntryView {
            section: entry,
            title: section.title().to_owned(),
            // A listed section always has one; only a detail has none.
            subtitle: section.subtitle().unwrap_or_default().to_owned(),
            selected: selected == Some(entry),
        })
        .collect()
}

/// A page of the administration: the section showing, under its title and
/// the section navigation.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct SchedulingAdminView {
    /// The page's heading: the section's title ("Bookings"), or a detail's
    /// ("Booking").
    pub title: String,
    /// The section navigation: every built section, the one showing selected.
    pub sections: Vec<SchedulingSectionEntryView>,
    /// The section's own page.
    pub page: SchedulingAdminPage,
}

/// The section's own page under the navigation. A section's pull request adds
/// its variant.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Enum)]
#[allow(
    clippy::large_enum_variant,
    reason = "one page is made per state change and lowered to C# at once; UniFFI cannot carry a Box"
)]
pub enum SchedulingAdminPage {
    /// The booking desk and what is coming up.
    Overview {
        /// The page.
        view: overview::SchedulingOverviewView,
    },
    /// The bookings list.
    Bookings {
        /// The page.
        view: bookings::SchedulingBookingsView,
    },
    /// One booking.
    Booking {
        /// The page.
        view: bookings::SchedulingBookingView,
    },
}

/// Something the member did in the administration. A section's pull request
/// adds its variant.
#[derive(Clone, Debug, PartialEq, Eq, uniffi::Enum)]
pub enum SchedulingAdminAction {
    /// Open a section, from the booking pages screen's list or the section
    /// navigation.
    Open {
        /// Which.
        section: SchedulingAdminSection,
    },
    /// Open one booking from the list.
    OpenBooking {
        /// The booking's id.
        booking_id: String,
    },
    /// On the bookings list.
    Bookings {
        /// What.
        action: bookings::BookingsAction,
    },
    /// On one booking.
    Booking {
        /// What.
        action: bookings::BookingAction,
    },
}

/// The core events `action` is, as of now.
pub(crate) fn events(action: SchedulingAdminAction) -> Vec<Event> {
    events_at(action, SystemTime::now())
}

/// The core events `action` is, as of `now`: whether a booking can still be
/// acted on, and which day is today, are the core's to judge against the time
/// the member pressed.
pub(crate) fn events_at(action: SchedulingAdminAction, now: SystemTime) -> Vec<Event> {
    match action {
        SchedulingAdminAction::Open { section } => {
            vec![Event::Navigate(Route::SchedulingAdmin(section.section()))]
        }
        SchedulingAdminAction::OpenBooking { booking_id } => vec![Event::Navigate(
            Route::SchedulingAdmin(SchedulingSection::Booking { id: booking_id }),
        )],
        SchedulingAdminAction::Bookings { action } => bookings::events(action, now),
        SchedulingAdminAction::Booking { action } => bookings::detail_events(action),
    }
}

/// The page of `section`, for a signed-in model.
pub(crate) fn screen(
    _model: &Model,
    signed_in: &SignedIn,
    section: &SchedulingSection,
) -> ScreenView {
    screen_at(signed_in, section, SystemTime::now())
}

/// The page of `section` as of `now`, which decides "Today" and which
/// bookings can still be acted on.
pub(crate) fn screen_at(
    signed_in: &SignedIn,
    section: &SchedulingSection,
    now: SystemTime,
) -> ScreenView {
    let admin = &signed_in.scheduling_admin;
    let can_change = signed_in.capabilities().can_change;
    let page = match section {
        SchedulingSection::Overview => overview::page(admin, now),
        SchedulingSection::Bookings => bookings::list_page(admin, can_change, now),
        SchedulingSection::Booking { id } => bookings::detail_page(admin, id, can_change, now),
        // Not built yet: each section's pull request replaces its arm.
        SchedulingSection::EventTypes
        | SchedulingSection::EventType { .. }
        | SchedulingSection::Hours
        | SchedulingSection::Calendar
        | SchedulingSection::Team
        | SchedulingSection::Settings
        | SchedulingSection::Developer => return ScreenView::unavailable(),
    };
    ScreenView::SchedulingAdmin {
        view: SchedulingAdminView {
            title: section.title().to_owned(),
            sections: entries(Some(SchedulingAdminSection::of(section))),
            page,
        },
    }
}

/// Where a section's read (or a dialog's) stands: a failed one is a status
/// page with the core's sentence, never an empty list.
pub(crate) fn load_status<T>(load: &SectionLoad<T>) -> LoadStatus {
    match load {
        SectionLoad::Loading => LoadStatus::Loading,
        SectionLoad::Failed(failure) => {
            LoadStatus::failed(s::scheduling_failed_title(l()), failure)
        }
        SectionLoad::Ready(_) => LoadStatus::Ready,
    }
}

/// What every confirm-once dialog shows besides its own fields.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct DialogView {
    /// The question: "Cancel this booking?".
    pub title: String,
    /// What saying yes does.
    pub body: String,
    /// The confirm button.
    pub confirm: String,
    /// The way out.
    pub close: String,
    /// Whether the confirm can be pressed now: what it needs is chosen, and
    /// nothing is on its way.
    pub can_confirm: bool,
    /// Whether the write is on its way (show progress; the way out still
    /// works, and the write still lands).
    pub working: bool,
    /// Whether the confirm undoes something for a customer, so that it is
    /// never the default button.
    pub destructive: bool,
    /// Why the write was refused, in the dialog; the edits are kept.
    pub failure: Option<FailureView>,
}

/// The chrome of a dialog whose write stands at `write`.
pub(crate) fn dialog_view(
    words: [&str; 4],
    write: &WriteState,
    ready: bool,
    destructive: bool,
) -> DialogView {
    let [title, body, confirm, close] = words;
    DialogView {
        title: title.to_owned(),
        body: body.to_owned(),
        confirm: confirm.to_owned(),
        close: close.to_owned(),
        can_confirm: ready && !write.busy(),
        working: write.busy(),
        destructive,
        failure: failure(write.failure()),
    }
}

/// The sentence of a write that landed, which the section shows once its
/// dialog has closed; `None` while the dialog still shows.
pub(crate) fn landed(write: &WriteState) -> Option<String> {
    write.notice().map(str::to_owned)
}

/// "Times in America/Toronto", for a section that shows times.
pub(crate) fn times_in(zone: &str) -> String {
    ss::times_in(l(), zone)
}

/// A row's failure, or a section's, as the view carries it.
pub(crate) fn failure_view(failure: &FailureText) -> FailureView {
    failure.into()
}

/// A page with every part filled in, for the boundary's own tests.
#[cfg(test)]
pub(crate) fn sample() -> SchedulingAdminView {
    SchedulingAdminView {
        title: "Overview".to_owned(),
        sections: entries(Some(SchedulingAdminSection::Overview)),
        page: overview::page(
            &district_core::SchedulingAdmin::default(),
            SystemTime::now(),
        ),
    }
}

/// A failure for the boundary's own tests.
#[cfg(test)]
pub(crate) fn sample_failure(message: &str, retryable: bool) -> FailureText {
    FailureText {
        message: message.to_owned(),
        degraded_regions: Vec::new(),
        session_ended: None,
        retryable,
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    const ALL: [SchedulingAdminSection; 8] = [
        SchedulingAdminSection::Overview,
        SchedulingAdminSection::EventTypes,
        SchedulingAdminSection::Hours,
        SchedulingAdminSection::Bookings,
        SchedulingAdminSection::Calendar,
        SchedulingAdminSection::Team,
        SchedulingAdminSection::Settings,
        SchedulingAdminSection::Developer,
    ];

    /// Every entry opens its own section, and a detail is listed as its list.
    #[test]
    fn each_entry_opens_its_own_section() {
        for entry in ALL {
            assert_eq!(SchedulingAdminSection::of(&entry.section()), entry);
        }
        assert_eq!(
            SchedulingAdminSection::of(&SchedulingSection::Booking { id: "b".to_owned() }),
            SchedulingAdminSection::Bookings
        );
        assert_eq!(
            SchedulingAdminSection::of(&SchedulingSection::EventType {
                slug: "s".to_owned()
            }),
            SchedulingAdminSection::EventTypes
        );
    }

    /// The navigation lists the built sections in the web's order, and only
    /// those; a detail is built when its list is.
    #[test]
    fn the_navigation_lists_the_built_sections_in_order() {
        let listed: Vec<SchedulingAdminSection> =
            entries(None).iter().map(|entry| entry.section).collect();
        assert_eq!(
            listed,
            [
                SchedulingAdminSection::Overview,
                SchedulingAdminSection::Bookings
            ]
        );
        let selected = entries(Some(SchedulingAdminSection::Bookings));
        assert!(!selected[0].selected);
        assert!(selected[1].selected);
        assert_eq!(selected[1].title, "Bookings");
        assert!(!selected[1].subtitle.is_empty());
        assert!(built(&SchedulingSection::Booking { id: "b".to_owned() }));
        for entry in ALL {
            assert_eq!(built(&entry.section()), listed.contains(&entry));
        }
    }

    #[test]
    fn opening_a_section_or_a_booking_navigates() {
        assert_eq!(
            events(SchedulingAdminAction::Open {
                section: SchedulingAdminSection::Bookings
            }),
            [Event::Navigate(Route::SchedulingAdmin(
                SchedulingSection::Bookings
            ))]
        );
        assert_eq!(
            events(SchedulingAdminAction::OpenBooking {
                booking_id: "bk-1".to_owned()
            }),
            [Event::Navigate(Route::SchedulingAdmin(
                SchedulingSection::Booking {
                    id: "bk-1".to_owned()
                }
            ))]
        );
    }

    #[test]
    fn a_dialog_is_pressed_once_and_its_landing_closes_it() {
        let words = ["Title", "Body", "Confirm", "Close"];
        let idle = dialog_view(words, &WriteState::Idle, true, true);
        assert!(idle.can_confirm);
        assert!(!idle.working);
        assert!(idle.destructive);
        assert_eq!(idle.title, "Title");
        let unready = dialog_view(words, &WriteState::Idle, false, false);
        assert!(!unready.can_confirm);
        let working = dialog_view(words, &WriteState::Working, true, false);
        assert!(!working.can_confirm);
        assert!(working.working);
        let failed = dialog_view(
            words,
            &WriteState::Failed(sample_failure("No.", true)),
            true,
            false,
        );
        assert_eq!(failed.failure.map(|f| f.message), Some("No.".to_owned()));
        assert!(failed.can_confirm);
        assert_eq!(landed(&WriteState::Working), None);
        assert_eq!(
            landed(&WriteState::Done("Booking cancelled".to_owned())),
            Some("Booking cancelled".to_owned())
        );
    }

    #[test]
    fn a_failed_read_is_a_status_page_never_an_empty_list() {
        assert_eq!(
            load_status(&SectionLoad::<()>::Loading),
            LoadStatus::Loading
        );
        assert_eq!(load_status(&SectionLoad::Ready(())), LoadStatus::Ready);
        let failed = load_status(&SectionLoad::<()>::Failed(sample_failure("Offline.", true)));
        let LoadStatus::Failed { failure, title } = failed else {
            panic!("not failed");
        };
        assert_eq!(failure.message, "Offline.");
        assert!(failure.retryable);
        assert_eq!(title, s::scheduling_failed_title(l()));
        assert_eq!(times_in("America/Toronto"), "Times in America/Toronto");
        assert_eq!(failure_view(&sample_failure("x", false)).message, "x");
    }
}
