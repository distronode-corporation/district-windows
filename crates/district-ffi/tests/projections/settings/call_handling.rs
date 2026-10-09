//! The CallHandling section (src/settings/call_handling.rs).
//!
//! The core has no contract fixture for call handling or availability, so the
//! answers here are the smallest of the shapes it decodes (district-model's
//! `CallHandlingResponse` and `AvailabilityResponse`), the same as the
//! scripted scene's.

use district_core::{CallHandlingEvent, Effect, Event, Route, SessionState, WorkspaceSection};
use district_ffi::settings::call_handling::{CallHandlingAction, CallHandlingChoice};
use district_ffi::settings::{SettingsAction, SettingsSection};
use district_ffi::{Held, NavDestination, ScreenView, UiEvent, leaves_unsaved, screen_view};
use district_model::CallHandlingMode;
use serde_json::{Value, json};

use super::super::{Case, Session, contracts, offered, route, server_error, signed_in, viewer};

fn stored(mode: &str, ring: i64) -> Value {
    json!({ "success": true, "callHandling": mode, "appRingSeconds": ring })
}

fn availability(available: bool, reason: Option<&str>) -> Value {
    json!({ "success": true, "availableForCalls": available, "reason": reason })
}

fn ui(session: Session, action: CallHandlingAction) -> Session {
    session.ui(UiEvent::CallHandling { action })
}

/// The section opened from the hub, nothing answered yet.
fn opened(session: Session) -> Session {
    session
        .ui(UiEvent::Settings {
            action: SettingsAction::Open,
        })
        .ui(UiEvent::Settings {
            action: SettingsAction::OpenSection {
                section: SettingsSection::CallHandling,
            },
        })
}

fn handling_read(session: Session, answer: Value) -> Session {
    session.answer(
        |e| matches!(e, Effect::LoadCallHandling { .. }),
        |ticket| Event::CallHandlingLoaded {
            ticket,
            result: Ok(contracts::decode("call handling", answer)),
        },
    )
}

fn availability_read(session: Session, answer: Value) -> Session {
    session.answer(
        |e| matches!(e, Effect::LoadAvailability { .. }),
        |ticket| Event::AvailabilityLoaded {
            ticket,
            result: Ok(contracts::decode("availability", answer)),
        },
    )
}

/// Read: the receptionist answers, a 20 second ring, and the member is rung.
fn loaded(session: Session) -> Session {
    availability_read(
        handling_read(opened(session), stored("ai_first", 20)),
        availability(true, None),
    )
}

/// Read, with the mode and the ring changed and not saved.
fn edited() -> Session {
    ui(
        ui(
            loaded(signed_in()),
            CallHandlingAction::SelectMode {
                mode: CallHandlingChoice::AppFirst,
            },
        ),
        CallHandlingAction::SetRingSeconds { seconds: 12 },
    )
}

fn saving() -> Session {
    ui(edited(), CallHandlingAction::Save)
}

fn saved() -> Session {
    saving().answer(
        |e| matches!(e, Effect::SaveCallHandling { .. }),
        |ticket| Event::CallHandlingLoaded {
            ticket,
            result: Ok(contracts::decode("call handling", stored("app_first", 12))),
        },
    )
}

fn save_failed() -> Session {
    saving().answer(
        |e| matches!(e, Effect::SaveCallHandling { .. }),
        |ticket| Event::CallHandlingLoaded {
            ticket,
            result: Err(server_error()),
        },
    )
}

fn changing() -> Session {
    ui(
        loaded(signed_in()),
        CallHandlingAction::SetAvailable { available: false },
    )
}

fn changed(result: Result<Value, ()>) -> Session {
    changing().answer(
        |e| matches!(e, Effect::SetAvailability { .. }),
        |ticket| Event::AvailabilityLoaded {
            ticket,
            result: result
                .map(|answer| contracts::decode("availability", answer))
                .map_err(|()| server_error()),
        },
    )
}

/// This area's snapshot cases.
pub(crate) fn cases() -> Vec<Case> {
    vec![
        ("call-handling-loading", opened(signed_in())),
        ("call-handling-loaded", loaded(signed_in())),
        ("call-handling-edited", edited()),
        ("call-handling-saving", saving()),
        ("call-handling-saved", saved()),
        ("call-handling-save-failed", save_failed()),
        (
            "call-handling-failed",
            availability_read(
                opened(signed_in()).answer(
                    |e| matches!(e, Effect::LoadCallHandling { .. }),
                    |ticket| Event::CallHandlingLoaded {
                        ticket,
                        result: Err(server_error()),
                    },
                ),
                availability(true, None),
            ),
        ),
        (
            "call-handling-availability-failed",
            handling_read(opened(signed_in()), stored("ai_then_app", 30)).answer(
                |e| matches!(e, Effect::LoadAvailability { .. }),
                |ticket| Event::AvailabilityLoaded {
                    ticket,
                    result: Err(server_error()),
                },
            ),
        ),
        (
            "call-handling-unknown-mode",
            availability_read(
                handling_read(opened(signed_in()), stored("ai_overnight", 20)),
                availability(false, None),
            ),
        ),
        (
            "call-handling-owner",
            availability_read(
                handling_read(opened(signed_in()), stored("ai_first", 20)),
                availability(false, Some("no_member_row")),
            ),
        ),
        ("call-handling-availability-changing", changing()),
        (
            "call-handling-availability-changed",
            changed(Ok(availability(false, None))),
        ),
        ("call-handling-availability-change-failed", changed(Err(()))),
        (
            "call-handling-viewer",
            availability_read(
                handling_read(opened(viewer()), stored("ai_then_app", 15)),
                availability(false, Some("role")),
            ),
        ),
    ]
}

fn view(session: &Session) -> district_ffi::settings::call_handling::CallHandlingView {
    match screen_view(&session.model) {
        ScreenView::CallHandling { view } => view,
        other => panic!("not call handling: {other:?}"),
    }
}

fn unsaved(session: &Session) -> bool {
    match session.model.session() {
        SessionState::SignedIn(signed_in) => signed_in.settings_unsaved(),
        _ => false,
    }
}

/// Built, it opens from the hub for every role, under the hub's highlight,
/// with its own page.
#[test]
fn built_it_opens_from_the_hub_for_every_role() {
    for session in [opened(signed_in()), opened(viewer())] {
        assert_eq!(
            route(&session),
            Route::Workspace(WorkspaceSection::CallHandling)
        );
        assert!(offered(&session).contains(&NavDestination::Settings));
        assert_eq!(view(&session).title, "Call handling");
    }
}

/// The section's heading is the hub row's title, which the walk opens it by.
#[test]
fn its_heading_is_its_hub_row() {
    let ScreenView::WorkspaceSettings { view: hub } = screen_view(
        &signed_in()
            .ui(UiEvent::Settings {
                action: SettingsAction::Open,
            })
            .model,
    ) else {
        panic!("the hub shows");
    };
    let row = hub
        .groups
        .iter()
        .flat_map(|group| group.rows.iter())
        .find(|row| row.section == SettingsSection::CallHandling)
        .expect("the hub lists call handling");
    assert_eq!(row.title, view(&opened(signed_in())).title);
}

#[test]
fn each_action_is_its_core_event() {
    let events = |action| UiEvent::CallHandling { action }.events();
    assert_eq!(
        events(CallHandlingAction::Open),
        [Event::Navigate(Route::Workspace(
            WorkspaceSection::CallHandling
        ))]
    );
    for (choice, mode) in [
        (CallHandlingChoice::AiFirst, CallHandlingMode::AiFirst),
        (CallHandlingChoice::AiThenApp, CallHandlingMode::AiThenApp),
        (CallHandlingChoice::AppFirst, CallHandlingMode::AppFirst),
    ] {
        assert_eq!(
            events(CallHandlingAction::SelectMode { mode: choice }),
            [Event::CallHandling(CallHandlingEvent::SelectMode(mode))]
        );
    }
    assert_eq!(
        events(CallHandlingAction::SetRingSeconds { seconds: 12 }),
        [Event::CallHandling(CallHandlingEvent::SetRingSeconds(12))]
    );
    assert_eq!(
        events(CallHandlingAction::Save),
        [Event::CallHandling(CallHandlingEvent::Save)]
    );
    assert_eq!(
        events(CallHandlingAction::SetAvailable { available: true }),
        [Event::CallHandling(CallHandlingEvent::SetAvailable(true))]
    );
    assert_eq!(
        events(CallHandlingAction::DismissNotices),
        [Event::CallHandling(CallHandlingEvent::DismissNotices)]
    );
}

/// The form: who answers and the ring are saved together, with only what
/// changed; the switch sends at once.
#[test]
fn a_save_sends_only_what_changed_and_the_switch_sends_at_once() {
    let read = view(&loaded(signed_in()));
    assert!(read.can_edit);
    assert!(!read.can_save);
    assert_eq!(read.modes.len(), 3);
    assert_eq!(read.ring_seconds, 20);
    assert!(read.availability.show_switch && read.availability.can_toggle);

    let edited = edited();
    assert!(view(&edited).can_save);
    let saving = ui(edited, CallHandlingAction::Save);
    let patch = saving.pending.iter().find_map(|effect| match effect {
        Effect::SaveCallHandling { patch, .. } => Some(*patch),
        _ => None,
    });
    let patch = patch.expect("the save is sent");
    assert_eq!(patch.call_handling, Some(CallHandlingMode::AppFirst));
    assert_eq!(patch.app_ring_seconds, Some(12));
    assert!(view(&saving).saving);
    assert!(!view(&saving).can_edit);

    // A ring outside the range is moved into it by the core.
    let clamped = ui(
        loaded(signed_in()),
        CallHandlingAction::SetRingSeconds { seconds: 90 },
    );
    assert_eq!(view(&clamped).ring_seconds, 30);

    let changing = changing();
    assert!(changing.pending.iter().any(|effect| matches!(
        effect,
        Effect::SetAvailability {
            available: false,
            ..
        }
    )));
    assert!(view(&changing).availability.changing);
    assert!(!view(&changing).availability.can_toggle);

    let dismissed = ui(saved(), CallHandlingAction::DismissNotices);
    assert_eq!(view(&dismissed).notice, None);
}

/// A viewer reads both and changes neither: shown only the mode in force, no
/// control works, and the core ignores what a viewer sends.
#[test]
fn a_viewer_reads_and_changes_nothing() {
    let session = availability_read(
        handling_read(opened(viewer()), stored("ai_then_app", 15)),
        availability(false, Some("role")),
    );
    let read = view(&session);
    assert!(!read.can_change && !read.can_edit && !read.can_save);
    assert!(read.viewer_note.is_some());
    assert_eq!(read.modes.len(), 1);
    assert!(read.modes[0].selected);
    assert!(!read.availability.show_switch);
    assert_eq!(
        read.availability.blocked.as_deref(),
        Some("Viewers are not rung for calls.")
    );
    let tried = ui(
        session,
        CallHandlingAction::SelectMode {
            mode: CallHandlingChoice::AiFirst,
        },
    );
    assert!(!unsaved(&tried));
    let tried = ui(tried, CallHandlingAction::SetAvailable { available: true });
    assert!(
        !tried.pending.iter().any(|effect| matches!(
            effect,
            Effect::SetAvailability { .. } | Effect::SaveCallHandling { .. }
        )),
        "{:?}",
        tried.pending
    );
}

/// A change not saved is what the settings kit's question guards: leaving
/// asks first, Keep editing keeps it, Discard leaves and drops it.
#[test]
fn leaving_with_a_change_not_saved_asks_first() {
    let clean = loaded(signed_in());
    assert!(!leaves_unsaved(&clean.model, &Event::Back));
    let mut session = edited();
    assert!(unsaved(&session));
    let mut held = Held::default();
    assert_eq!(held.pass(&session.model, UiEvent::Back.events()), []);
    assert!(held.question().is_some());
    held.keep();
    assert!(unsaved(&session));
    assert_eq!(held.pass(&session.model, UiEvent::Back.events()), []);
    for event in held.discard() {
        session = session.send(event);
    }
    assert_eq!(route(&session), Route::Workspace(WorkspaceSection::Hub));
    assert!(!unsaved(&session));
}
