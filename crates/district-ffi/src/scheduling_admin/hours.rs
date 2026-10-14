//! The working hours: the week, every day Monday first and an empty one too
//! (a day that is not bookable is never mistaken for a day that did not
//! load), with the summary the overview shows and the zone the times are in;
//! and the dated overrides still to come, a run of days as one row; with
//! their two editors, as District AI for iPhone's `SchedulingHoursView`,
//! `SchedulingAvailabilityRulesSheet` and `SchedulingAvailabilityOverridesSheet`.
//!
//! The core decides what is offered, and this projects it: both editors need
//! a member who may change things, and are offered whatever the section's two
//! reads came to, because each editor reads for itself. The week's editor
//! saves the difference from what it read, one step at a time, and stays
//! open saying what the save came to until it is put away. The overrides'
//! editor lists the days still to come, each with Delete (asked once more in
//! the dialog, a run of several days saying how many), and an add form in
//! place of the list while it is open, whose kind decides its fields.

use std::time::SystemTime;

use district_core::scheduling_admin::{
    DEFAULT_END, DEFAULT_START, HoursDialog, HoursEvent, HoursSection, OverrideForm, OverrideRow,
    OverridesEditor, SchedulingDay, SectionLoad, WeekEditor, weekday_name,
};
use district_core::{Event, SchedulingAdmin, SchedulingAdminEvent, SchedulingSection};
use district_l10n::scheduling_strings as ss;
use serde::Serialize;

use super::bookings::ChoiceRowView;
use super::event_types::SchedulingAskView;
use super::{DialogView, SchedulingAdminPage, dialog_view, landed, load_status};
use crate::l10n::l;
use crate::views::{FactView, LoadStatus};

/// Whether this version has the section's pages.
pub(crate) const BUILT: bool = true;

/// The days of the week the editor shows, Monday first.
const DAYS: usize = 7;

/// The working hours.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct SchedulingHoursView {
    /// "Times in America/Toronto": the zone of the member's scheduler profile.
    pub times_in: String,
    /// Whether the two editors are offered: the member may change things.
    pub offers_writes: bool,
    /// "Your working hours", which opens the week's editor
    /// (`HoursAction::OpenWeek`).
    pub week_label: String,
    /// "Date overrides", which opens the overrides' editor
    /// (`HoursAction::OpenOverrides`).
    pub overrides_label: String,
    /// The week's heading.
    pub week_heading: String,
    /// Where the read of the week stands: a failed one is a status page.
    pub status: LoadStatus,
    /// "Mon to Fri, 9:00 a.m. to 5:00 p.m.", or that no day is bookable yet;
    /// shown once the week is read.
    pub summary: String,
    /// The week, Monday first, every day: its name, and its hours or "Not
    /// bookable". Empty until read.
    pub week: Vec<FactView>,
    /// The overrides' heading.
    pub overrides_heading: String,
    /// What they are.
    pub overrides_subtitle: String,
    /// Where the read of the overrides stands; a failure is said on its own
    /// line, under the week.
    pub overrides_status: LoadStatus,
    /// The overrides still to come, a run as one row: its days, and its hours
    /// or why it has none.
    pub overrides: Vec<FactView>,
    /// Said when the overrides are read and none is to come.
    pub overrides_empty: String,
    /// The week's editor, while it is open.
    pub week_editor: Option<HoursWeekView>,
    /// The overrides' editor, while it is open.
    pub overrides_editor: Option<HoursOverridesView>,
}

/// The week's editor: every day's windows, saved as the difference from what
/// it read. Its save is the dialog's confirm, offered once the week is read
/// and every window is sound.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct HoursWeekView {
    /// Its title ("Your working hours"), the save ("Save", "Saving…"), the
    /// way out and why the save failed.
    pub dialog: DialogView,
    /// Where the editor's own read of the week stands.
    pub status: LoadStatus,
    /// Whether the windows can be changed: the week is read and no save is on
    /// its way.
    pub editable: bool,
    /// The days, Monday first; none until the week is read.
    pub days: Vec<HoursDayView>,
    /// "Not bookable", on a day with no window.
    pub not_bookable: String,
    /// "Add range", on each day.
    pub add_range_label: String,
    /// "Copy to all weekdays", on a weekday with a window to copy.
    pub copy_label: String,
    /// "to", between a window's two times.
    pub to_label: String,
    /// "Remove", on each window.
    pub remove_label: String,
    /// A new window's opening, the time box's placeholder.
    pub start_placeholder: String,
    /// A new window's closing.
    pub end_placeholder: String,
    /// What the last save came to ("Working hours saved", "Nothing to
    /// save."), until it is put away (`HoursAction::DismissNotice`).
    pub notice: Option<String>,
}

/// One day of the week's editor.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct HoursDayView {
    /// Which day, 0 for Monday, as the day's actions name it.
    pub day: u32,
    /// "Monday".
    pub name: String,
    /// Its windows, as edited.
    pub ranges: Vec<HoursRangeView>,
    /// Whether it offers "Copy to all weekdays": Monday to Friday, with a
    /// window to copy.
    pub offers_copy: bool,
}

/// One window of a day, as typed.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct HoursRangeView {
    /// Which window, as the window's actions name it. A window keeps its key
    /// while it is edited, and the week read again gives every window a new
    /// one.
    pub key: u64,
    /// When it opens, `HH:MM`, as typed.
    pub start: String,
    /// When it closes.
    pub end: String,
    /// Its fault: a time not `HH:MM`, an end not after its start, or an
    /// overlap with the window before it.
    pub error: Option<String>,
}

/// The overrides' editor: the days still to come, each with Delete, asked
/// once more; Add override; and the add form in place of the list while it
/// is open. It has no confirm: the form has its own.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct HoursOverridesView {
    /// Its title ("Date overrides"), the way out, and why the last write
    /// failed.
    pub dialog: DialogView,
    /// What it is for.
    pub hint: String,
    /// Where the editor's own read of the overrides stands.
    pub status: LoadStatus,
    /// The days still to come, a run as one row.
    pub rows: Vec<OverrideRowView>,
    /// Said when the overrides are read and none is to come.
    pub empty: String,
    /// "Add override", which opens the form.
    pub add_label: String,
    /// Whether an override can be added or deleted: the overrides are read
    /// and nothing is on its way.
    pub can_change: bool,
    /// "Delete", on each row.
    pub delete_label: String,
    /// What the last write came to ("Override added"), until it is put away
    /// (`HoursAction::DismissNotice`).
    pub notice: Option<String>,
    /// The add form, while it is open.
    pub form: Option<OverrideEditorView>,
    /// A row's delete, while it is being asked about: a run goes whole, and
    /// says how many days.
    pub delete: Option<SchedulingAskView>,
}

/// A day, or a run of days, still to come.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct OverrideRowView {
    /// The row, as its delete names it (`HoursAction::AskDeleteOverride`).
    pub key: String,
    /// "Sep 24, 2026", or "Dec 24, 2026 to Dec 31, 2026".
    pub dates: String,
    /// "12:00 p.m. to 4:00 p.m.", "Out of office" or "Unavailable".
    pub hours: String,
}

/// An override's fields as the form holds them, what a change to the form
/// sends back whole (`HoursAction::EditOverride`).
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct OverrideFormView {
    /// The kind's wire value: `day_off` or `custom_hours`.
    pub reason: String,
    /// The (first) day, `YYYY-MM-DD`, as typed.
    pub date: String,
    /// A day off's last day, or empty for one day.
    pub end_date: String,
    /// Custom hours' opening, `HH:MM`.
    pub start: String,
    /// Custom hours' closing.
    pub end: String,
}

impl From<&OverrideForm> for OverrideFormView {
    fn from(form: &OverrideForm) -> Self {
        Self {
            reason: form.reason.clone(),
            date: form.date.clone(),
            end_date: form.end_date.clone(),
            start: form.start.clone(),
            end: form.end.clone(),
        }
    }
}

impl From<OverrideFormView> for OverrideForm {
    fn from(form: OverrideFormView) -> Self {
        Self {
            reason: form.reason,
            date: form.date,
            end_date: form.end_date,
            start: form.start,
            end: form.end,
        }
    }
}

/// The add form.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct OverrideEditorView {
    /// "Add an override".
    pub title: String,
    /// The fields, as the core holds them.
    pub form: OverrideFormView,
    /// The kinds of day, the one chosen selected.
    pub kinds: Vec<ChoiceRowView>,
    /// "What happens that day".
    pub kind_label: String,
    /// "First day", or "Date" for custom hours.
    pub date_label: String,
    /// The day box's placeholder: today, in the profile's zone.
    pub date_placeholder: String,
    /// Whether the last day shows: a day off, which may run on.
    pub shows_last_day: bool,
    /// "Last day".
    pub last_day_label: String,
    /// "Leave blank for a single day."
    pub last_day_hint: String,
    /// Whether the hours show: custom hours, one day only.
    pub shows_hours: bool,
    /// "Hours that day".
    pub hours_label: String,
    /// "to", between the two times.
    pub to_label: String,
    /// The opening's placeholder.
    pub start_placeholder: String,
    /// The closing's placeholder.
    pub end_placeholder: String,
    /// "Add override".
    pub save_label: String,
    /// "Cancel", which closes the form and keeps the dialog.
    pub cancel_label: String,
    /// Whether the save can be pressed: nothing is on its way.
    pub can_save: bool,
    /// Why the override cannot be sent, before anything was sent.
    pub validation: Option<String>,
}

/// Something the member did on the working hours.
#[derive(Clone, Debug, PartialEq, Eq, uniffi::Enum)]
pub enum HoursAction {
    /// Open the week's editor.
    OpenWeek,
    /// Add a window to a day, 9 to 5.
    AddRange {
        /// The day, 0 for Monday.
        day: u32,
    },
    /// Take a window off.
    RemoveRange {
        /// The day.
        day: u32,
        /// The window.
        key: u64,
    },
    /// A window's opening changed.
    SetRangeStart {
        /// The day.
        day: u32,
        /// The window.
        key: u64,
        /// `HH:MM`, as typed.
        value: String,
    },
    /// A window's closing changed.
    SetRangeEnd {
        /// The day.
        day: u32,
        /// The window.
        key: u64,
        /// `HH:MM`, as typed.
        value: String,
    },
    /// Give every other weekday this day's windows.
    CopyToWeekdays {
        /// The day.
        day: u32,
    },
    /// Save the week.
    SaveWeek,
    /// Open the overrides' editor, the days still to come as of the press.
    OpenOverrides,
    /// Open the add form.
    AddOverride,
    /// The add form's fields changed.
    EditOverride {
        /// The whole form, as it holds now.
        form: OverrideFormView,
    },
    /// Close the add form.
    CancelOverride,
    /// Add the override.
    SaveOverride,
    /// Ask about deleting a row.
    AskDeleteOverride {
        /// The row's key.
        key: String,
    },
    /// Do not delete it.
    CancelDeleteOverride,
    /// Delete it (a run whole).
    ConfirmDeleteOverride,
    /// Put away what the last write came to.
    DismissNotice,
    /// Close the editor.
    CloseDialog,
}

/// The core events `action` is, as of `now`: the overrides' editor lists the
/// days still to come from the day the member pressed.
pub(crate) fn events(action: HoursAction, now: SystemTime) -> Vec<Event> {
    use HoursAction as A;
    use HoursEvent as E;
    let day = |day: u32| day as usize;
    let event = match action {
        A::OpenWeek => E::OpenWeek,
        A::AddRange { day: d } => E::AddRange { day: day(d) },
        A::RemoveRange { day: d, key } => E::RemoveRange { day: day(d), key },
        A::SetRangeStart { day: d, key, value } => E::SetRangeStart {
            day: day(d),
            key,
            value,
        },
        A::SetRangeEnd { day: d, key, value } => E::SetRangeEnd {
            day: day(d),
            key,
            value,
        },
        A::CopyToWeekdays { day: d } => E::CopyToWeekdays { day: day(d) },
        A::SaveWeek => E::SaveWeek,
        A::OpenOverrides => E::OpenOverrides { now },
        A::AddOverride => E::AddOverride,
        A::EditOverride { form } => E::EditOverride(form.into()),
        A::CancelOverride => E::CancelOverride,
        A::SaveOverride => E::SaveOverride,
        A::AskDeleteOverride { key } => E::AskDeleteOverride(key),
        A::CancelDeleteOverride => E::CancelDeleteOverride,
        A::ConfirmDeleteOverride => E::ConfirmDeleteOverride,
        A::DismissNotice => E::DismissNotice,
        A::CloseDialog => E::CloseDialog,
    };
    vec![Event::SchedulingAdmin(SchedulingAdminEvent::Hours(event))]
}

/// The working hours' page as of `now`, which decides which overrides are
/// still to come, for a member who may change things when `can_change`.
/// Before the section is entered, its reads are on their way.
pub(crate) fn page(
    admin: &SchedulingAdmin,
    can_change: bool,
    now: SystemTime,
) -> SchedulingAdminPage {
    let fresh = HoursSection {
        timezone: "UTC".to_owned(),
        rules: SectionLoad::Loading,
        overrides: SectionLoad::Loading,
        dialog: None,
    };
    SchedulingAdminPage::Hours {
        view: view(admin.hours.as_ref().unwrap_or(&fresh), can_change, now),
    }
}

fn view(section: &HoursSection, can_change: bool, now: SystemTime) -> SchedulingHoursView {
    let [eyebrow, subtitle, empty, week_label, overrides_label] = HoursSection::labels();
    let (mut week_editor, mut overrides_editor) = (None, None);
    match &section.dialog {
        Some(HoursDialog::Week(week)) => week_editor = Some(week_view(week)),
        Some(HoursDialog::Overrides(editor)) => {
            let today = SchedulingDay::today(now, &section.timezone).key();
            overrides_editor = Some(overrides_view(editor, &today));
        }
        None => {}
    }
    SchedulingHoursView {
        times_in: section.times_in(),
        offers_writes: can_change,
        week_label: week_label.to_owned(),
        overrides_label: overrides_label.to_owned(),
        week_heading: SchedulingSection::Hours.title().to_owned(),
        status: load_status(&section.rules),
        summary: section.summary(),
        week: section
            .week()
            .into_iter()
            .map(|(day, hours)| FactView {
                label: day.to_owned(),
                value: hours,
            })
            .collect(),
        overrides_heading: eyebrow.to_owned(),
        overrides_subtitle: subtitle.to_owned(),
        overrides_status: load_status(&section.overrides),
        overrides: section
            .override_rows(now)
            .into_iter()
            .map(|row| FactView {
                label: row.dates,
                value: row.hours,
            })
            .collect(),
        overrides_empty: empty.to_owned(),
        week_editor,
        overrides_editor,
    }
}

/// The week's editor.
fn week_view(week: &WeekEditor) -> HoursWeekView {
    let [
        title,
        not_bookable,
        add_range,
        copy,
        to,
        remove,
        save,
        cancel,
    ] = WeekEditor::labels();
    let busy = week.write.busy();
    let read = week.load.value().is_some();
    HoursWeekView {
        dialog: dialog_view(
            [
                title,
                "",
                if busy { ss::common_saving(l()) } else { save },
                cancel,
            ],
            &week.write,
            week.can_save(),
            false,
        ),
        status: load_status(&week.load),
        editable: read && !busy,
        days: if read {
            (0..DAYS).map(|day| day_view(week, day)).collect()
        } else {
            Vec::new()
        },
        not_bookable: not_bookable.to_owned(),
        add_range_label: add_range.to_owned(),
        copy_label: copy.to_owned(),
        to_label: to.to_owned(),
        remove_label: remove.to_owned(),
        start_placeholder: DEFAULT_START.to_owned(),
        end_placeholder: DEFAULT_END.to_owned(),
        notice: landed(&week.write),
    }
}

/// One day of the week's editor, its windows each with its fault.
fn day_view(week: &WeekEditor, day: usize) -> HoursDayView {
    let errors = week.errors(day);
    HoursDayView {
        // Below 7.
        day: day as u32,
        name: weekday_name(day).to_owned(),
        ranges: week.days[day]
            .iter()
            .zip(errors)
            .map(|(range, error)| HoursRangeView {
                key: range.key,
                start: range.start.clone(),
                end: range.end.clone(),
                error: error.map(str::to_owned),
            })
            .collect(),
        offers_copy: week.offers_copy(day),
    }
}

/// The overrides' editor; a day is typed like `today`.
fn overrides_view(editor: &OverridesEditor, today: &str) -> HoursOverridesView {
    let [
        title,
        hint,
        empty,
        add_label,
        add_title,
        kind_label,
        last_day_label,
        last_day_hint,
        hours_label,
        delete_title,
        delete_confirm,
    ] = OverridesEditor::labels();
    let busy = editor.write.busy();
    let to = WeekEditor::labels()[4];
    HoursOverridesView {
        dialog: dialog_view(
            [title, "", "", ss::common_cancel(l())],
            &editor.write,
            false,
            false,
        ),
        hint: hint.to_owned(),
        status: load_status(&editor.load),
        rows: editor.rows().into_iter().map(row_view).collect(),
        empty: empty.to_owned(),
        add_label: add_label.to_owned(),
        can_change: editor.load.value().is_some() && !busy,
        delete_label: ss::common_delete(l()).to_owned(),
        notice: landed(&editor.write),
        form: editor.adding.as_ref().map(|form| {
            let custom = form.is_custom_hours();
            OverrideEditorView {
                title: add_title.to_owned(),
                form: form.into(),
                kinds: OverrideForm::kinds()
                    .iter()
                    .map(|(value, label)| ChoiceRowView {
                        value: (*value).to_owned(),
                        label: (*label).to_owned(),
                        selected: *value == form.reason,
                    })
                    .collect(),
                kind_label: kind_label.to_owned(),
                date_label: form.date_label().to_owned(),
                date_placeholder: today.to_owned(),
                shows_last_day: !custom,
                last_day_label: last_day_label.to_owned(),
                last_day_hint: last_day_hint.to_owned(),
                shows_hours: custom,
                hours_label: hours_label.to_owned(),
                to_label: to.to_owned(),
                start_placeholder: DEFAULT_START.to_owned(),
                end_placeholder: DEFAULT_END.to_owned(),
                save_label: add_label.to_owned(),
                cancel_label: ss::common_cancel(l()).to_owned(),
                can_save: !busy,
                validation: editor.validation.map(str::to_owned),
            }
        }),
        delete: editor
            .confirming_delete
            .as_ref()
            .map(|_| SchedulingAskView {
                title: delete_title.to_owned(),
                body: editor.delete_body(),
                confirm: delete_confirm.to_owned(),
                cancel: ss::common_cancel(l()).to_owned(),
            }),
    }
}

fn row_view(row: OverrideRow) -> OverrideRowView {
    OverrideRowView {
        key: row.key,
        dates: row.dates,
        hours: row.hours,
    }
}
