//! The working hours (src/scheduling_admin/hours.rs): the week and the date
//! overrides, and their two editors.
//!
//! The fixture's overrides are in September 2026; they are moved to 2099 so
//! that they are still to come whenever the tests run. The add form's day box
//! shows today as its placeholder, so no snapshot holds the form open: the
//! tests below check it.

use district_core::SchedulingAdminEvent;
use district_core::scheduling_admin::{HoursEvent, OverrideForm};
use district_ffi::scheduling_admin::hours::{HoursAction, OverrideFormView, SchedulingHoursView};

use super::*;

/// This area's snapshot cases.
pub(crate) fn cases() -> Vec<Case> {
    vec![
        (
            "scheduling-admin-hours-loading",
            open(signed_in(), SchedulingAdminSection::Hours),
        ),
        ("scheduling-admin-hours", hours(signed_in())),
        (
            "scheduling-admin-hours-failed",
            overrides_read(
                rules_read(context(signed_in()), Err(server_error())),
                Ok(overrides_fixture()),
            ),
        ),
        (
            "scheduling-admin-hours-overrides-failed",
            overrides_read(
                rules_read(
                    context(signed_in()),
                    Ok(contracts::json("district-scheduling-rules.json")),
                ),
                Err(server_error()),
            ),
        ),
        ("scheduling-admin-hours-viewer", hours(viewer())),
        (
            "scheduling-admin-hours-week-loading",
            hours(signed_in()).ui(event(HoursAction::OpenWeek)),
        ),
        ("scheduling-admin-hours-week-open", week(signed_in())),
        (
            "scheduling-admin-hours-week-invalid",
            week(signed_in())
                .ui(event(HoursAction::AddRange { day: 0 }))
                .ui(event(HoursAction::SetRangeEnd {
                    day: 0,
                    key: 2,
                    value: "08:00".to_owned(),
                }))
                .ui(event(HoursAction::AddRange { day: 2 }))
                .ui(event(HoursAction::SetRangeStart {
                    day: 2,
                    key: 3,
                    value: "9".to_owned(),
                })),
        ),
        (
            "scheduling-admin-hours-week-saving",
            week_saving(signed_in()),
        ),
        ("scheduling-admin-hours-week-saved", week_saved(signed_in())),
        (
            "scheduling-admin-hours-week-refused",
            step(week_saving(signed_in()), Err(server_error())),
        ),
        (
            "scheduling-admin-hours-overrides-open",
            overrides_editor(signed_in()),
        ),
        (
            "scheduling-admin-hours-override-delete-asked",
            overrides_editor_with(signed_in(), run_fixture()).ui(event(
                HoursAction::AskDeleteOverride {
                    key: "group:grp-run".to_owned(),
                },
            )),
        ),
        (
            "scheduling-admin-hours-override-added",
            override_added(signed_in()),
        ),
    ]
}

fn event(action: HoursAction) -> UiEvent {
    UiEvent::SchedulingAdmin {
        action: SchedulingAdminAction::Hours { action },
    }
}

/// The overrides fixture, moved to September 2099.
fn overrides_fixture() -> Value {
    let mut value = contracts::json("district-scheduling-overrides.json");
    for item in value["data"]["items"].as_array_mut().unwrap() {
        let date = item["date"].as_str().unwrap().replacen("2026", "2099", 1);
        item["date"] = json!(date);
    }
    value
}

/// The overrides fixture and a run of three days off over Christmas 2099.
fn run_fixture() -> Value {
    let mut value = overrides_fixture();
    let items = value["data"]["items"].as_array_mut().unwrap();
    for (id, date) in [
        ("ovr-run-1", "2099-12-24"),
        ("ovr-run-2", "2099-12-25"),
        ("ovr-run-3", "2099-12-26"),
    ] {
        items.push(json!({
            "id": id,
            "date": date,
            "is_available": false,
            "reason": "out_of_office",
            "start_time": null,
            "end_time": null,
            "group_id": "grp-run"
        }));
    }
    value
}

/// The section opened, with the profile (and so the zone) read.
fn context(session: Session) -> Session {
    me(open(session, SchedulingAdminSection::Hours))
}

/// The newest read of the rules, answered with `result`.
fn rules_read(session: Session, result: Result<Value, ApiError>) -> Session {
    answer_admin(
        session,
        |r| matches!(r, SchedulingAdminRequest::Rules { .. }),
        result,
    )
}

/// The newest read of the overrides, answered with `result`.
fn overrides_read(session: Session, result: Result<Value, ApiError>) -> Session {
    answer_admin(
        session,
        |r| matches!(r, SchedulingAdminRequest::Overrides),
        result,
    )
}

/// The working hours, read.
fn hours(session: Session) -> Session {
    overrides_read(
        rules_read(
            context(session),
            Ok(contracts::json("district-scheduling-rules.json")),
        ),
        Ok(overrides_fixture()),
    )
}

/// The week's editor, its read answered: Wednesday 10:30 to 15:00 is the
/// only rule for every event type, window 1.
fn week(session: Session) -> Session {
    rules_read(
        hours(session).ui(event(HoursAction::OpenWeek)),
        Ok(contracts::json("district-scheduling-rules.json")),
    )
}

/// Monday 9 to 5 added, and the save pressed: one create on its way.
fn week_saving(session: Session) -> Session {
    week(session)
        .ui(event(HoursAction::AddRange { day: 0 }))
        .ui(event(HoursAction::SaveWeek))
}

/// One step of the week's save, answered with `result`.
fn step(session: Session, result: Result<Value, ApiError>) -> Session {
    answer_admin(
        session,
        |r| {
            matches!(
                r,
                SchedulingAdminRequest::CreateRule { .. }
                    | SchedulingAdminRequest::PatchRule { .. }
                    | SchedulingAdminRequest::DeleteRule { .. }
            )
        },
        result,
    )
}

/// The save landed: the week and the section read again.
fn week_saved(session: Session) -> Session {
    let session = step(
        week_saving(session),
        Ok(contracts::json("district-scheduling-rule.json")),
    );
    let session = rules_read(
        session,
        Ok(contracts::json("district-scheduling-rules.json")),
    );
    let session = overrides_read(
        rules_read(
            me(session),
            Ok(contracts::json("district-scheduling-rules.json")),
        ),
        Ok(overrides_fixture()),
    );
    assert!(
        !session
            .pending
            .iter()
            .any(|e| matches!(e, Effect::SchedulingAdmin { .. })),
        "every read answered"
    );
    session
}

/// The overrides' editor, its read answered with `overrides`.
fn overrides_editor_with(session: Session, overrides: Value) -> Session {
    overrides_read(
        hours(session).ui(event(HoursAction::OpenOverrides)),
        Ok(overrides),
    )
}

fn overrides_editor(session: Session) -> Session {
    overrides_editor_with(session, overrides_fixture())
}

/// A day off on 1 October 2099, added: the form closes, the editor says so
/// and reads the overrides again.
fn override_added(session: Session) -> Session {
    let session = overrides_editor(session)
        .ui(event(HoursAction::AddOverride))
        .ui(event(HoursAction::EditOverride {
            form: OverrideFormView {
                date: "2099-10-01".to_owned(),
                ..(&OverrideForm::default()).into()
            },
        }))
        .ui(event(HoursAction::SaveOverride));
    let mut created = contracts::json("district-scheduling-override-created.json");
    created["data"]["date"] = json!("2099-10-01");
    let session = answer_admin(
        session,
        |r| matches!(r, SchedulingAdminRequest::CreateOverride(_)),
        Ok(created),
    );
    overrides_read(session, Ok(overrides_fixture()))
}

fn hours_view(session: &Session) -> SchedulingHoursView {
    let SchedulingAdminPage::Hours { view } = admin_view(session).page else {
        panic!("not the working hours");
    };
    view
}

/// Whether a request `pick` finds is on its way.
fn asked(session: &Session, pick: fn(&SchedulingAdminRequest) -> bool) -> bool {
    session
        .pending
        .iter()
        .any(|e| matches!(e, Effect::SchedulingAdmin { request, .. } if pick(request)))
}

/// Every day of the week shows, an empty one as not bookable, with the
/// summary and the zone; the overrides still to come show as their days and
/// hours; the editors are offered to a member who may change things.
#[test]
fn the_week_shows_every_day_and_the_overrides_to_come() {
    let session = hours(signed_in());
    assert_eq!(
        route(&session),
        Route::SchedulingAdmin(SchedulingSection::Hours)
    );
    let page = admin_view(&session);
    assert_eq!(page.title, "Working hours");
    assert!(
        page.sections
            .iter()
            .any(|s| s.section == SchedulingAdminSection::Hours && s.selected)
    );
    let view = hours_view(&session);
    assert_eq!(view.status, district_ffi::LoadStatus::Ready);
    assert_eq!(view.times_in, "Times in America/Toronto");
    assert_eq!(view.week_heading, "Working hours");
    let days: Vec<(&str, &str)> = view
        .week
        .iter()
        .map(|d| (d.label.as_str(), d.value.as_str()))
        .collect();
    assert_eq!(days.len(), 7);
    assert_eq!(days[0], ("Monday", "Not bookable"));
    assert_eq!(days[2].0, "Wednesday");
    assert_ne!(days[2].1, "Not bookable");
    assert_eq!(days[6], ("Sunday", "Not bookable"));
    assert!(!view.summary.is_empty());
    let overrides: Vec<&str> = view.overrides.iter().map(|o| o.value.as_str()).collect();
    assert_eq!(overrides.len(), 2);
    assert_eq!(overrides[1], "Unavailable");
    assert!(view.offers_writes);
    assert_eq!(
        [view.week_label.as_str(), &view.overrides_label],
        ["Your working hours", "Date overrides"]
    );
    assert_eq!(view.overrides_heading, "Date overrides");
    assert_eq!(view.overrides_empty, "No overrides ahead.");
    assert!(!hours_view(&hours(viewer())).offers_writes);
    // The overrides already over are not listed.
    let past = overrides_read(
        rules_read(
            context(signed_in()),
            Ok(contracts::json("district-scheduling-rules.json")),
        ),
        Ok(json!({"ok": true, "data": {"items": [{
            "id": "ovr-old",
            "date": "2001-01-01",
            "is_available": false,
            "reason": "day_off",
            "start_time": null,
            "end_time": null
        }]}})),
    );
    let view = hours_view(&past);
    assert!(view.overrides.is_empty());
    assert_eq!(view.overrides_status, district_ffi::LoadStatus::Ready);
    // Back to the booking pages.
    assert_eq!(route(&session.ui(UiEvent::Back)), Route::Scheduling);
}

/// A failed read of the week is a status page; the overrides, read on their
/// own, still show, and fail alone in turn.
#[test]
fn each_read_fails_alone() {
    let failed = hours_view(&overrides_read(
        rules_read(context(signed_in()), Err(server_error())),
        Ok(overrides_fixture()),
    ));
    assert!(matches!(
        failed.status,
        district_ffi::LoadStatus::Failed { .. }
    ));
    assert!(failed.week.is_empty());
    assert_eq!(failed.overrides.len(), 2);
    assert!(failed.offers_writes, "each editor reads for itself");
    let overrides_failed = hours_view(&overrides_read(
        rules_read(
            context(signed_in()),
            Ok(contracts::json("district-scheduling-rules.json")),
        ),
        Err(server_error()),
    ));
    assert!(matches!(
        overrides_failed.overrides_status,
        district_ffi::LoadStatus::Failed { .. }
    ));
    assert_eq!(overrides_failed.week.len(), 7);
    let loading = hours_view(&open(signed_in(), SchedulingAdminSection::Hours));
    assert_eq!(loading.status, district_ffi::LoadStatus::Loading);
    assert_eq!(loading.times_in, "Times in UTC");
}

/// The week's editor reads for itself, with no save before it lands; each
/// window's fault shows under it and holds the save; Copy to all weekdays is
/// offered on a weekday with a window.
#[test]
fn the_week_is_edited_window_by_window() {
    let loading = hours_view(&hours(signed_in()).ui(event(HoursAction::OpenWeek)));
    let editor = loading.week_editor.expect("open");
    assert_eq!(editor.status, district_ffi::LoadStatus::Loading);
    assert!(editor.days.is_empty());
    assert!(!editor.editable && !editor.dialog.can_confirm);
    let session = week(signed_in());
    let editor = hours_view(&session).week_editor.expect("open");
    assert_eq!(editor.dialog.title, "Your working hours");
    assert_eq!(editor.dialog.confirm, "Save");
    assert_eq!(editor.dialog.close, "Cancel");
    assert!(editor.dialog.can_confirm && editor.editable);
    assert!(!editor.dialog.destructive);
    assert_eq!(
        [
            editor.not_bookable.as_str(),
            &editor.add_range_label,
            &editor.copy_label,
            &editor.to_label,
            &editor.remove_label,
            &editor.start_placeholder,
            &editor.end_placeholder,
        ],
        [
            "Not bookable",
            "Add range",
            "Copy to all weekdays",
            "to",
            "Remove",
            "09:00",
            "17:00"
        ]
    );
    let names: Vec<&str> = editor.days.iter().map(|d| d.name.as_str()).collect();
    assert_eq!(
        names,
        [
            "Monday",
            "Tuesday",
            "Wednesday",
            "Thursday",
            "Friday",
            "Saturday",
            "Sunday"
        ]
    );
    assert_eq!(editor.days[6].day, 6);
    let wednesday = &editor.days[2];
    assert_eq!(wednesday.ranges.len(), 1);
    assert_eq!(
        (
            wednesday.ranges[0].start.as_str(),
            wednesday.ranges[0].end.as_str()
        ),
        ("10:30", "15:00")
    );
    assert!(wednesday.offers_copy);
    assert!(!editor.days[0].offers_copy);

    // A window added to Monday, then made unsound.
    let session = session.ui(event(HoursAction::AddRange { day: 0 }));
    let editor = hours_view(&session).week_editor.expect("open");
    let monday = &editor.days[0].ranges[0];
    assert_eq!(
        (monday.start.as_str(), monday.end.as_str()),
        ("09:00", "17:00")
    );
    let key = monday.key;
    let session = session.ui(event(HoursAction::SetRangeEnd {
        day: 0,
        key,
        value: "08:00".to_owned(),
    }));
    let editor = hours_view(&session).week_editor.expect("open");
    assert_eq!(
        editor.days[0].ranges[0].error.as_deref(),
        Some("The end has to be after the start.")
    );
    assert!(!editor.dialog.can_confirm);
    let session = session
        .ui(event(HoursAction::SetRangeEnd {
            day: 0,
            key,
            value: "12:00".to_owned(),
        }))
        .ui(event(HoursAction::SetRangeStart {
            day: 0,
            key,
            value: "25:00".to_owned(),
        }));
    // The core reads `8:00` as 08:00 since 5.1.0; a time past 23:59 is still refused.
    let editor = hours_view(&session).week_editor.expect("open");
    assert!(editor.days[0].ranges[0].error.is_some());
    let session = session.ui(event(HoursAction::SetRangeStart {
        day: 0,
        key,
        value: "08:00".to_owned(),
    }));
    let editor = hours_view(&session).week_editor.expect("open");
    assert_eq!(editor.days[0].ranges[0].error, None);
    assert!(editor.dialog.can_confirm);

    // Copied to every other weekday, and taken off again on Friday.
    let session = session.ui(event(HoursAction::CopyToWeekdays { day: 0 }));
    let editor = hours_view(&session).week_editor.expect("open");
    for day in &editor.days[1..5] {
        let windows: Vec<(&str, &str)> = day
            .ranges
            .iter()
            .map(|r| (r.start.as_str(), r.end.as_str()))
            .collect();
        assert_eq!(windows, [("08:00", "12:00")], "{}", day.name);
    }
    assert!(editor.days[5].ranges.is_empty() && editor.days[6].ranges.is_empty());
    let friday = editor.days[4].ranges[0].key;
    let session = session.ui(event(HoursAction::RemoveRange {
        day: 4,
        key: friday,
    }));
    let editor = hours_view(&session).week_editor.expect("open");
    assert!(editor.days[4].ranges.is_empty());
    assert!(!editor.days[4].offers_copy);
    let closed = hours_view(&session.ui(event(HoursAction::CloseDialog)));
    assert_eq!(closed.week_editor, None);
}

/// The save sends the difference one step at a time, the dialog working
/// meanwhile; once it ends the week and the section are read again, and the
/// dialog says what it came to until that is put away. A refusal says why
/// in the dialog; a week with no change saves nothing.
#[test]
fn the_week_saves_its_difference_and_says_so() {
    let saving = week_saving(signed_in());
    assert!(asked(&saving, |r| matches!(
        r,
        SchedulingAdminRequest::CreateRule { .. }
    )));
    let editor = hours_view(&saving).week_editor.expect("open");
    assert!(editor.dialog.working && !editor.dialog.can_confirm);
    assert_eq!(editor.dialog.confirm, "Saving…");
    assert!(!editor.editable);

    let saved = week_saved(signed_in());
    let view = hours_view(&saved);
    let editor = view.week_editor.expect("still open");
    assert_eq!(editor.notice.as_deref(), Some("Working hours saved"));
    assert_eq!(editor.status, district_ffi::LoadStatus::Ready);
    assert_eq!(view.week.len(), 7);
    let away = hours_view(&saved.ui(event(HoursAction::DismissNotice)));
    assert_eq!(away.week_editor.expect("open").notice, None);

    let refused = step(week_saving(signed_in()), Err(server_error()));
    let editor = hours_view(&refused).week_editor.expect("still open");
    assert!(editor.dialog.failure.is_some());
    assert_eq!(editor.notice, None);
    assert!(asked(&refused, |r| matches!(
        r,
        SchedulingAdminRequest::Rules { .. }
    )));

    let unchanged = week(signed_in()).ui(event(HoursAction::SaveWeek));
    let editor = hours_view(&unchanged).week_editor.expect("open");
    assert_eq!(editor.notice.as_deref(), Some("Nothing to save."));
    assert!(!asked(&unchanged, |r| matches!(
        r,
        SchedulingAdminRequest::CreateRule { .. }
            | SchedulingAdminRequest::PatchRule { .. }
            | SchedulingAdminRequest::DeleteRule { .. }
    )));

    // A week whose read failed says why, and offers no save.
    let failed = rules_read(
        hours(signed_in()).ui(event(HoursAction::OpenWeek)),
        Err(server_error()),
    );
    let editor = hours_view(&failed).week_editor.expect("open");
    assert!(matches!(
        editor.status,
        district_ffi::LoadStatus::Failed { .. }
    ));
    assert!(!editor.dialog.can_confirm && editor.days.is_empty());
}

/// "Try again" in an editor whose read failed reads it again: the week's its
/// rules, the overrides' its overrides. Read, it asks for nothing.
#[test]
fn an_editor_whose_read_failed_reads_again() {
    let failed = rules_read(
        hours(signed_in()).ui(event(HoursAction::OpenWeek)),
        Err(server_error()),
    );
    let again = failed.ui(event(HoursAction::RetryRead));
    let editor = hours_view(&again).week_editor.expect("open");
    assert_eq!(editor.status, district_ffi::LoadStatus::Loading);
    let again = rules_read(again, Ok(contracts::json("district-scheduling-rules.json")));
    assert_eq!(
        hours_view(&again).week_editor.expect("open").status,
        district_ffi::LoadStatus::Ready
    );

    let failed = overrides_read(
        hours(signed_in()).ui(event(HoursAction::OpenOverrides)),
        Err(server_error()),
    );
    let editor = hours_view(&failed).overrides_editor.expect("open");
    assert!(matches!(
        editor.status,
        district_ffi::LoadStatus::Failed { .. }
    ));
    let again = failed.ui(event(HoursAction::RetryRead));
    assert_eq!(
        hours_view(&again).overrides_editor.expect("open").status,
        district_ffi::LoadStatus::Loading
    );
    let again = overrides_read(again, Ok(overrides_fixture()));
    assert_eq!(
        hours_view(&again).overrides_editor.expect("open").status,
        district_ffi::LoadStatus::Ready
    );

    // An editor that read its hours asks for nothing.
    let read = week(signed_in());
    let before = read.pending.len();
    assert_eq!(read.ui(event(HoursAction::RetryRead)).pending.len(), before);
}

/// The overrides' editor lists the days still to come, a run as one row;
/// the add form's kind decides its fields, and it says why it cannot be sent
/// before anything is; an override added closes the form and says so.
#[test]
fn an_override_is_added_with_the_fields_its_kind_needs() {
    let loading = hours_view(&hours(signed_in()).ui(event(HoursAction::OpenOverrides)));
    let editor = loading.overrides_editor.expect("open");
    assert_eq!(editor.status, district_ffi::LoadStatus::Loading);
    assert!(!editor.can_change);
    let session = overrides_editor(signed_in());
    let editor = hours_view(&session).overrides_editor.expect("open");
    assert_eq!(editor.dialog.title, "Date overrides");
    assert_eq!(editor.dialog.confirm, "");
    assert!(!editor.dialog.can_confirm);
    assert_eq!(
        editor.hint,
        "Days you are away, or working different hours from usual."
    );
    assert_eq!(editor.add_label, "Add override");
    assert_eq!(editor.delete_label, "Delete");
    assert!(editor.can_change && editor.form.is_none());
    let keys: Vec<&str> = editor.rows.iter().map(|r| r.key.as_str()).collect();
    assert_eq!(keys, ["group:grp-contract-1", "id:ovr-contract-2"]);
    assert_eq!(editor.rows[1].hours, "Unavailable");

    let session = session.ui(event(HoursAction::AddOverride));
    let form = hours_view(&session)
        .overrides_editor
        .and_then(|e| e.form)
        .expect("the form");
    assert_eq!(form.title, "Add an override");
    assert_eq!(form.form.reason, "day_off");
    assert_eq!(form.date_label, "First day");
    assert_eq!(form.date_placeholder.len(), 10, "today, YYYY-MM-DD");
    assert!(form.shows_last_day && !form.shows_hours);
    assert_eq!(form.last_day_label, "Last day");
    assert_eq!(form.last_day_hint, "Leave blank for a single day.");
    assert_eq!(form.kind_label, "What happens that day");
    let kinds: Vec<(&str, bool)> = form
        .kinds
        .iter()
        .map(|k| (k.value.as_str(), k.selected))
        .collect();
    assert_eq!(kinds, [("day_off", true), ("custom_hours", false)]);
    assert_eq!(
        [
            form.save_label.as_str(),
            &form.cancel_label,
            &form.hours_label,
            &form.to_label,
            &form.start_placeholder,
            &form.end_placeholder
        ],
        [
            "Add override",
            "Cancel",
            "Hours that day",
            "to",
            "09:00",
            "17:00"
        ]
    );
    assert!(form.can_save && form.validation.is_none());

    // Custom hours: one day, with its hours.
    let custom = OverrideFormView {
        reason: "custom_hours".to_owned(),
        start: "14:00".to_owned(),
        end: "13:00".to_owned(),
        ..form.form.clone()
    };
    let session = session.ui(event(HoursAction::EditOverride {
        form: custom.clone(),
    }));
    let form = hours_view(&session)
        .overrides_editor
        .and_then(|e| e.form)
        .expect("the form");
    assert!(form.shows_hours && !form.shows_last_day);
    assert_eq!(form.date_label, "Date");
    assert_eq!(form.form, custom);
    let session = session.ui(event(HoursAction::SaveOverride));
    let form = hours_view(&session)
        .overrides_editor
        .and_then(|e| e.form)
        .expect("the form");
    assert_eq!(form.validation.as_deref(), Some("Pick a date."));
    let session = session
        .ui(event(HoursAction::EditOverride {
            form: OverrideFormView {
                date: "2099-10-02".to_owned(),
                ..custom
            },
        }))
        .ui(event(HoursAction::SaveOverride));
    let form = hours_view(&session)
        .overrides_editor
        .and_then(|e| e.form)
        .expect("the form");
    assert_eq!(
        form.validation.as_deref(),
        Some("The end has to be after the start.")
    );
    let closed = hours_view(&session.ui(event(HoursAction::CancelOverride)));
    assert_eq!(closed.overrides_editor.expect("open").form, None);

    let session = override_added(signed_in());
    let editor = hours_view(&session).overrides_editor.expect("open");
    assert_eq!(editor.form, None);
    assert_eq!(editor.notice.as_deref(), Some("Override added"));
    let away = hours_view(&session.ui(event(HoursAction::DismissNotice)));
    assert_eq!(away.overrides_editor.expect("open").notice, None);
}

/// The add's save is pressed once: off while it is on its way, and a
/// refusal says why in the dialog with the form kept.
#[test]
fn an_override_add_is_pressed_once() {
    let session = overrides_editor(signed_in())
        .ui(event(HoursAction::AddOverride))
        .ui(event(HoursAction::EditOverride {
            form: OverrideFormView {
                date: "2099-10-01".to_owned(),
                end_date: "2099-10-03".to_owned(),
                ..(&OverrideForm::default()).into()
            },
        }))
        .ui(event(HoursAction::SaveOverride));
    let editor = hours_view(&session).overrides_editor.expect("open");
    assert!(editor.dialog.working && !editor.can_change);
    assert!(!editor.form.expect("the form").can_save);
    let refused = answer_admin(
        session,
        |r| matches!(r, SchedulingAdminRequest::CreateOverride(_)),
        Err(server_error()),
    );
    let editor = hours_view(&refused).overrides_editor.expect("open");
    assert!(editor.dialog.failure.is_some());
    assert_eq!(
        editor.form.expect("kept").form.end_date,
        "2099-10-03".to_owned()
    );
}

/// A delete is asked once more; a run goes whole and says how many days,
/// one day says so; a delete that lands says so and reads the list again.
#[test]
fn a_delete_is_asked_once_more_and_a_run_goes_whole() {
    let session = overrides_editor_with(signed_in(), run_fixture());
    let editor = hours_view(&session).overrides_editor.expect("open");
    let run = editor.rows.last().expect("the run");
    assert_eq!(run.key, "group:grp-run");
    assert_eq!(run.hours, "Out of office");
    assert!(run.dates.contains(" to "));
    let asked_run = session.ui(event(HoursAction::AskDeleteOverride {
        key: "group:grp-run".to_owned(),
    }));
    let ask = hours_view(&asked_run)
        .overrides_editor
        .and_then(|e| e.delete)
        .expect("asked");
    assert_eq!(ask.title, "Delete this override?");
    assert_eq!(ask.body, "All 3 days go back to your weekly hours.");
    assert_eq!(ask.confirm, "Delete override");
    assert_eq!(ask.cancel, "Cancel");
    let kept = hours_view(&asked_run.ui(event(HoursAction::CancelDeleteOverride)));
    assert!(kept.overrides_editor.expect("open").delete.is_none());

    let deleting = overrides_editor_with(signed_in(), run_fixture())
        .ui(event(HoursAction::AskDeleteOverride {
            key: "group:grp-run".to_owned(),
        }))
        .ui(event(HoursAction::ConfirmDeleteOverride));
    assert!(asked(&deleting, |r| matches!(
        r,
        SchedulingAdminRequest::DeleteOverrideGroup { .. }
    )));
    let deleted = answer_admin(
        deleting,
        |r| matches!(r, SchedulingAdminRequest::DeleteOverrideGroup { .. }),
        Ok(contracts::json("district-scheduling-ok.json")),
    );
    let editor = hours_view(&deleted).overrides_editor.expect("open");
    assert_eq!(editor.notice.as_deref(), Some("Override deleted"));
    assert!(asked(&deleted, |r| matches!(
        r,
        SchedulingAdminRequest::Overrides
    )));

    let asked_day = overrides_editor(signed_in()).ui(event(HoursAction::AskDeleteOverride {
        key: "id:ovr-contract-2".to_owned(),
    }));
    let ask = hours_view(&asked_day)
        .overrides_editor
        .and_then(|e| e.delete)
        .expect("asked");
    assert_eq!(ask.body, "That day goes back to your weekly hours.");
    let failed = answer_admin(
        asked_day.ui(event(HoursAction::ConfirmDeleteOverride)),
        |r| matches!(r, SchedulingAdminRequest::DeleteOverride { .. }),
        Err(server_error()),
    );
    let editor = hours_view(&failed).overrides_editor.expect("open");
    assert!(editor.dialog.failure.is_some());
    assert_eq!(editor.rows.len(), 2);

    // A read of the overrides that failed says why.
    let read_failed = overrides_read(
        hours(signed_in()).ui(event(HoursAction::OpenOverrides)),
        Err(server_error()),
    );
    let editor = hours_view(&read_failed).overrides_editor.expect("open");
    assert!(matches!(
        editor.status,
        district_ffi::LoadStatus::Failed { .. }
    ));
    assert!(!editor.can_change);
}

#[test]
fn each_action_is_its_core_event() {
    use HoursAction as A;
    use HoursEvent as E;
    let s = |v: &str| v.to_owned();
    let form = OverrideFormView {
        reason: s("custom_hours"),
        date: s("2099-10-01"),
        end_date: String::new(),
        start: s("10:00"),
        end: s("12:00"),
    };
    let core_form = OverrideForm {
        reason: s("custom_hours"),
        date: s("2099-10-01"),
        end_date: String::new(),
        start: s("10:00"),
        end: s("12:00"),
    };
    let to_core = |e| Event::SchedulingAdmin(SchedulingAdminEvent::Hours(e));
    for (sent, event_sent) in [
        (A::OpenWeek, E::OpenWeek),
        (A::RetryRead, E::RetryRead),
        (A::AddRange { day: 1 }, E::AddRange { day: 1 }),
        (
            A::RemoveRange { day: 2, key: 7 },
            E::RemoveRange { day: 2, key: 7 },
        ),
        (
            A::SetRangeStart {
                day: 3,
                key: 7,
                value: s("08:00"),
            },
            E::SetRangeStart {
                day: 3,
                key: 7,
                value: s("08:00"),
            },
        ),
        (
            A::SetRangeEnd {
                day: 3,
                key: 7,
                value: s("18:00"),
            },
            E::SetRangeEnd {
                day: 3,
                key: 7,
                value: s("18:00"),
            },
        ),
        (A::CopyToWeekdays { day: 4 }, E::CopyToWeekdays { day: 4 }),
        (A::SaveWeek, E::SaveWeek),
        (A::AddOverride, E::AddOverride),
        (A::EditOverride { form }, E::EditOverride(core_form)),
        (A::CancelOverride, E::CancelOverride),
        (A::SaveOverride, E::SaveOverride),
        (
            A::AskDeleteOverride { key: s("id:o") },
            E::AskDeleteOverride(s("id:o")),
        ),
        (A::CancelDeleteOverride, E::CancelDeleteOverride),
        (A::ConfirmDeleteOverride, E::ConfirmDeleteOverride),
        (A::DismissNotice, E::DismissNotice),
        (A::CloseDialog, E::CloseDialog),
    ] {
        assert_eq!(event(sent).events(), [to_core(event_sent)]);
    }
    // Opening the overrides carries the time it was pressed at.
    let [Event::SchedulingAdmin(SchedulingAdminEvent::Hours(E::OpenOverrides { .. }))] =
        <[Event; 1]>::try_from(event(A::OpenOverrides).events()).unwrap()
    else {
        panic!("not the overrides' opening");
    };
}
