//! The overview: the booking desk at a glance (the booking page's links, the
//! calendars, the working hours, the event types, the next booking) and what
//! is coming up, as District AI for iPhone's `SchedulingOverviewView`.
//!
//! Every row stands or falls alone: a row whose read failed says why in its
//! own place and leaves the other four, so the page never fails as a whole.
//! The booking links are shown as text to select, not links: they address the
//! workspace's public booking page, where an operator's click should not land
//! by accident. Nothing here writes.

use std::time::SystemTime;

use district_core::scheduling_admin::{DeskContent, DeskRow, OverviewSection};
use district_core::{SchedulingAdmin, Tone};
use district_l10n::scheduling_strings as ss;
use serde::Serialize;

use super::{SchedulingAdminPage, failure_view, load_status, times_in};
use crate::l10n::l;
use crate::views::{FailureView, LoadStatus};
use crate::workflows::RunTone;

/// Whether this version has the section's page.
pub(crate) const BUILT: bool = true;

/// The overview's page.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct SchedulingOverviewView {
    /// "Times in America/Toronto": the zone every time here is in.
    pub times_in: String,
    /// The desk card's heading, "Your booking desk".
    pub desk_heading: String,
    /// The desk's five rows, each labelled in every state.
    pub desk: Vec<BookingDeskRowView>,
    /// The second card's heading, "Coming up".
    pub coming_up_heading: String,
    /// Where the read of the next bookings stands. A failure is shown in the
    /// card, in the danger tone; the desk above still shows.
    pub coming_up_status: LoadStatus,
    /// The next bookings, soonest first, once read.
    pub coming_up: Vec<ComingUpRowView>,
    /// Said when nothing is coming up.
    pub coming_up_empty: String,
}

/// What a row of the desk shows.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Serialize, uniffi::Enum)]
pub enum BookingDeskRowState {
    /// Its read is on its way (a placeholder under the label).
    Loading,
    /// Its read failed: `failure`, in the danger tone.
    Failed,
    /// Its value: the first line, and any after it.
    Lines,
    /// Nothing set up yet: the one line, in the muted tone.
    Hint,
}

/// One row of the booking desk.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct BookingDeskRowView {
    /// The row's label ("Booking page", "Calendar").
    pub label: String,
    /// What it shows.
    pub state: BookingDeskRowState,
    /// The value's lines, or the hint alone. Empty while loading or failed.
    pub lines: Vec<String>,
    /// Why its read failed.
    pub failure: Option<FailureView>,
}

impl From<DeskRow> for BookingDeskRowView {
    fn from(row: DeskRow) -> Self {
        let (state, lines, failure) = match row.content {
            DeskContent::Loading => (BookingDeskRowState::Loading, Vec::new(), None),
            DeskContent::Failed(failure) => (
                BookingDeskRowState::Failed,
                Vec::new(),
                Some(failure_view(&failure)),
            ),
            DeskContent::Lines(lines) => (BookingDeskRowState::Lines, lines, None),
            DeskContent::Hint(hint) => (BookingDeskRowState::Hint, vec![hint.to_owned()], None),
        };
        Self {
            label: row.label.to_owned(),
            state,
            lines,
            failure,
        }
    }
}

/// One booking under "Coming up".
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct ComingUpRowView {
    /// The booking's id.
    pub id: String,
    /// When: "Today 2:30 p.m.", "Tomorrow 10:00 a.m." or the date and time.
    pub when: String,
    /// Who booked, and what.
    pub subtitle: String,
    /// The status badge's label.
    pub status_label: String,
    /// The badge's tone.
    pub tone: RunTone,
}

/// The overview, as of `now`, for the section `admin` holds (read on entry;
/// before that, every read is on its way).
pub(crate) fn page(admin: &SchedulingAdmin, now: SystemTime) -> SchedulingAdminPage {
    let fresh = OverviewSection::default();
    SchedulingAdminPage::Overview {
        view: overview_view(admin.overview.as_ref().unwrap_or(&fresh), now),
    }
}

fn overview_view(section: &OverviewSection, now: SystemTime) -> SchedulingOverviewView {
    SchedulingOverviewView {
        times_in: times_in(&section.timezone),
        desk_heading: ss::desk_eyebrow(l()).to_owned(),
        desk: section
            .desk(now)
            .into_iter()
            .map(BookingDeskRowView::from)
            .collect(),
        coming_up_heading: ss::coming_up_eyebrow(l()).to_owned(),
        coming_up_status: load_status(&section.bookings),
        coming_up: section
            .coming_up(now)
            .into_iter()
            .map(|row| ComingUpRowView {
                id: row.id,
                when: row.when,
                subtitle: row.subtitle,
                status_label: row.status,
                tone: RunTone::from(row.tone),
            })
            .collect(),
        coming_up_empty: ss::coming_up_empty(l()).to_owned(),
    }
}

/// A booking status's tone, as a run's (the badge colours are the same).
pub(crate) fn tone(tone: Tone) -> RunTone {
    RunTone::from(tone)
}

#[cfg(test)]
mod tests {
    use std::time::{Duration, UNIX_EPOCH};

    use district_core::scheduling_admin::SectionLoad;
    use serde_json::json;

    use super::super::sample_failure;
    use super::*;

    /// 2026-09-14 12:00 UTC: the fixture's booking is at 13:00 that day.
    fn now() -> SystemTime {
        UNIX_EPOCH + Duration::from_secs(1_789_387_200)
    }

    fn read<T: serde::de::DeserializeOwned>(value: serde_json::Value) -> T {
        serde_json::from_value(value).unwrap()
    }

    fn overview() -> OverviewSection {
        let mut section = OverviewSection::default();
        section.event_types = SectionLoad::Ready(read(json!([{
            "id": "et-1", "slug": "phone-consultation", "name": "Phone consultation",
            "duration_minutes": 30, "is_active": true
        }])));
        section.calendar =
            SectionLoad::Failed(sample_failure("The booking system did not answer.", true));
        section.rules = SectionLoad::Ready(Vec::new());
        section.bookings = SectionLoad::Ready(read(json!([{
            "id": "bk-1", "event_type_slug": "phone-consultation",
            "start_at": "2026-09-14T13:00:00Z", "end_at": "2026-09-14T13:30:00Z",
            "status": "confirmed",
            "attendees": [{"name": "Dana Booker", "email": "dana@example.com"}]
        }])));
        section.public_host = Some("book.example.com".to_owned());
        section.can_manage = true;
        section
    }

    #[test]
    fn each_row_stands_or_falls_alone() {
        let view = overview_view(&overview(), now());
        assert_eq!(view.times_in, "Times in UTC");
        let states: Vec<BookingDeskRowState> = view.desk.iter().map(|row| row.state).collect();
        assert_eq!(
            states,
            [
                BookingDeskRowState::Lines,
                BookingDeskRowState::Failed,
                BookingDeskRowState::Hint,
                BookingDeskRowState::Lines,
                BookingDeskRowState::Lines,
            ]
        );
        assert_eq!(
            view.desk[0].lines,
            ["https://book.example.com/book/phone-consultation"]
        );
        assert_eq!(
            view.desk[1].failure.as_ref().map(|f| f.message.as_str()),
            Some("The booking system did not answer.")
        );
        assert!(view.desk[1].lines.is_empty());
        assert_eq!(view.desk[2].lines.len(), 1);
        assert_eq!(view.coming_up_status, LoadStatus::Ready);
        assert_eq!(view.coming_up.len(), 1);
        let row = &view.coming_up[0];
        assert_eq!(row.id, "bk-1");
        assert!(row.when.starts_with("Today"), "{}", row.when);
        assert_eq!(row.status_label, "Confirmed");
        assert_eq!(row.tone, RunTone::Success);
        assert!(row.subtitle.contains("Dana Booker"));
    }

    #[test]
    fn before_its_reads_every_row_is_loading() {
        let page = page(&SchedulingAdmin::default(), now());
        let SchedulingAdminPage::Overview { view } = page else {
            panic!("not the overview");
        };
        assert!(
            view.desk
                .iter()
                .all(|row| row.state == BookingDeskRowState::Loading)
        );
        assert_eq!(view.desk.len(), 5);
        assert_eq!(view.coming_up_status, LoadStatus::Loading);
        assert!(view.coming_up.is_empty());
        assert_eq!(view.desk_heading, "Your booking desk");
        assert_eq!(view.coming_up_heading, "Coming up");
        assert!(!view.coming_up_empty.is_empty());
        assert_eq!(tone(Tone::Danger), RunTone::Danger);
    }
}
