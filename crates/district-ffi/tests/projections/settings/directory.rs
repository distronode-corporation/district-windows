//! The Directory section (src/settings/directory.rs).

use district_core::{
    DirectoryEvent, DirectoryField, Effect, Event, Route, SessionState, WorkspaceSection,
};
use district_ffi::settings::directory::{DirectoryAction, DirectoryEntryField, DirectoryView};
use district_ffi::settings::{SectionStatus, SettingsAction, SettingsSection};
use district_ffi::{Held, NavDestination, ScreenView, UiEvent, leaves_unsaved, screen_view};
use serde_json::{Value, json};

use super::super::{Case, Session, contracts, offered, route, server_error, signed_in, viewer};

fn ui(session: Session, action: DirectoryAction) -> Session {
    session.ui(UiEvent::Directory { action })
}

/// The section opened from the hub, nothing answered yet.
fn opened() -> Session {
    signed_in()
        .ui(UiEvent::Settings {
            action: SettingsAction::Open,
        })
        .ui(UiEvent::Settings {
            action: SettingsAction::OpenSection {
                section: SettingsSection::Directory,
            },
        })
}

/// The settings fixture with its directory replaced by `directory`.
fn config_with(directory: Value) -> Value {
    let mut config = contracts::json("district-workspace-config.json");
    config["config"]["callDirectory"] = directory;
    config
}

fn read(session: Session, config: Value) -> Session {
    session.answer(
        |e| matches!(e, Effect::LoadWorkspaceConfig { .. }),
        |ticket| Event::WorkspaceConfigLoaded {
            ticket,
            result: Ok(contracts::decode("workspace config", config)),
        },
    )
}

fn read_failed(session: Session) -> Session {
    session.answer(
        |e| matches!(e, Effect::LoadWorkspaceConfig { .. }),
        |ticket| Event::WorkspaceConfigLoaded {
            ticket,
            result: Err(server_error()),
        },
    )
}

/// Read from the core's fixture: two entries, one with a key this app does
/// not edit.
fn loaded() -> Session {
    read(opened(), contracts::json("district-workspace-config.json"))
}

/// A new entry typed, not added yet.
fn typed() -> Session {
    ui(
        ui(
            loaded(),
            DirectoryAction::EditNewName {
                name: "Front desk".to_owned(),
            },
        ),
        DirectoryAction::EditNewPhoneNumber {
            number: "+14165550155".to_owned(),
        },
    )
}

/// The new entry added and the first entry renamed, not saved.
fn edited() -> Session {
    ui(
        ui(typed(), DirectoryAction::Add),
        DirectoryAction::Edit {
            index: 0,
            field: DirectoryEntryField::Name,
            value: "Operations".to_owned(),
        },
    )
}

fn confirming() -> Session {
    ui(edited(), DirectoryAction::Save)
}

fn saving() -> Session {
    ui(confirming(), DirectoryAction::ConfirmSave)
}

fn written(result: Result<(), ()>) -> Session {
    saving().answer(
        |e| matches!(e, Effect::SaveDirectory { .. }),
        |ticket| Event::SettingsWritten {
            ticket,
            result: result.map_err(|()| server_error()),
        },
    )
}

fn removed_all() -> Session {
    ui(
        ui(loaded(), DirectoryAction::Remove { index: 1 }),
        DirectoryAction::Remove { index: 0 },
    )
}

/// This area's snapshot cases.
pub(crate) fn cases() -> Vec<Case> {
    vec![
        ("directory-loading", opened()),
        ("directory-loaded", loaded()),
        ("directory-failed", read_failed(opened())),
        ("directory-empty", read(opened(), config_with(Value::Null))),
        (
            "directory-unmodellable",
            read(opened(), config_with(json!(["Ops desk"]))),
        ),
        (
            "directory-incomplete",
            read(
                opened(),
                config_with(json!([
                    { "name": "Ops desk", "phoneNumber": "+14165550177" },
                    { "name": "", "phoneNumber": "+14165550166" },
                    { "name": "Night line" }
                ])),
            ),
        ),
        ("directory-typed", typed()),
        (
            "directory-add-rejected",
            ui(
                ui(
                    loaded(),
                    DirectoryAction::EditNewName {
                        name: "Front desk".to_owned(),
                    },
                ),
                DirectoryAction::Add,
            ),
        ),
        ("directory-edited", edited()),
        ("directory-confirming", confirming()),
        (
            "directory-confirming-remove-all",
            ui(removed_all(), DirectoryAction::Save),
        ),
        ("directory-saving", saving()),
        (
            "directory-saved",
            read(
                written(Ok(())),
                contracts::json("district-workspace-config.json"),
            ),
        ),
        ("directory-save-failed", written(Err(()))),
        (
            "directory-saved-not-read-back",
            read_failed(written(Ok(()))),
        ),
    ]
}

fn view(session: &Session) -> DirectoryView {
    match screen_view(&session.model) {
        ScreenView::Directory { view } => view,
        other => panic!("not the transfer directory: {other:?}"),
    }
}

fn unsaved(session: &Session) -> bool {
    match session.model.session() {
        SessionState::SignedIn(signed_in) => signed_in.settings_unsaved(),
        _ => false,
    }
}

/// Built, it opens from the hub for a member who may read the settings, with
/// its own page, under the hub's row title. A viewer is not offered it.
#[test]
fn built_it_opens_from_the_hub_and_a_viewer_is_not_offered_it() {
    let session = opened();
    assert_eq!(
        route(&session),
        Route::Workspace(WorkspaceSection::Directory)
    );
    assert!(offered(&session).contains(&NavDestination::Settings));
    assert_eq!(view(&session).title, "Transfer directory");
    let ScreenView::WorkspaceSettings { view: hub } = screen_view(
        &viewer()
            .ui(UiEvent::Settings {
                action: SettingsAction::Open,
            })
            .model,
    ) else {
        panic!("the hub shows");
    };
    assert!(
        hub.groups
            .iter()
            .flat_map(|group| group.rows.iter())
            .all(|row| row.section != SettingsSection::Directory)
    );
}

#[test]
fn each_action_is_its_core_event() {
    let events = |action| UiEvent::Directory { action }.events();
    assert_eq!(
        events(DirectoryAction::Open),
        [Event::Navigate(Route::Workspace(
            WorkspaceSection::Directory
        ))]
    );
    assert_eq!(
        events(DirectoryAction::EditNewName {
            name: "A".to_owned()
        }),
        [Event::Directory(DirectoryEvent::EditNewName(
            "A".to_owned()
        ))]
    );
    assert_eq!(
        events(DirectoryAction::EditNewPhoneNumber {
            number: "1".to_owned()
        }),
        [Event::Directory(DirectoryEvent::EditNewPhoneNumber(
            "1".to_owned()
        ))]
    );
    for (field, core) in [
        (DirectoryEntryField::Name, DirectoryField::Name),
        (
            DirectoryEntryField::PhoneNumber,
            DirectoryField::PhoneNumber,
        ),
    ] {
        assert_eq!(
            events(DirectoryAction::Edit {
                index: 1,
                field,
                value: "x".to_owned(),
            }),
            [Event::Directory(DirectoryEvent::Edit {
                index: 1,
                field: core,
                value: "x".to_owned(),
            })]
        );
    }
    assert_eq!(
        events(DirectoryAction::Remove { index: 0 }),
        [Event::Directory(DirectoryEvent::Remove(0))]
    );
    for (action, event) in [
        (DirectoryAction::Add, DirectoryEvent::Add),
        (DirectoryAction::Save, DirectoryEvent::Save),
        (DirectoryAction::ConfirmSave, DirectoryEvent::ConfirmSave),
        (DirectoryAction::CancelSave, DirectoryEvent::CancelSave),
        (
            DirectoryAction::DismissNotice,
            DirectoryEvent::DismissNotice,
        ),
    ] {
        assert_eq!(events(action), [Event::Directory(event)]);
    }
}

/// The entries read as the core gives them: the number grouped for reading in
/// the line under each, and the stored value in its box.
#[test]
fn the_entries_read_as_stored() {
    let read = view(&loaded());
    assert_eq!(read.status, SectionStatus::Ready);
    assert_eq!(read.entries.len(), 2);
    assert_eq!(read.entries[0].title, "Ops desk");
    assert_eq!(read.entries[0].line, "+1 416 555 0177");
    assert_eq!(read.entries[0].phone_number, "+14165550177");
    assert!(read.can_edit && !read.can_save);
    assert_eq!(read.incomplete, None);
}

/// "Add" needs a name and a number; the save asks first, and sends the list
/// on screen with every stored key kept.
#[test]
fn an_add_needs_both_and_a_save_asks_first() {
    let rejected = view(&ui(
        ui(
            loaded(),
            DirectoryAction::EditNewName {
                name: "Front desk".to_owned(),
            },
        ),
        DirectoryAction::Add,
    ));
    assert!(rejected.add_rejected.is_some());
    assert!(!rejected.can_save);

    let edited = view(&edited());
    assert_eq!(edited.entries.len(), 3);
    assert_eq!(edited.new_name, "");
    assert!(edited.can_save);

    let asked = confirming();
    let question = view(&asked).confirming.expect("the core asks");
    assert_eq!(question.title, "Replace the transfer directory?");
    assert!(!question.destructive);
    let cancelled = ui(asked, DirectoryAction::CancelSave);
    assert_eq!(view(&cancelled).confirming, None);

    let saving = saving();
    let entries = saving
        .pending
        .iter()
        .find_map(|e| match e {
            Effect::SaveDirectory { entries, .. } => Some(entries.clone()),
            _ => None,
        })
        .expect("the save is sent");
    assert_eq!(entries.len(), 3);
    assert_eq!(entries[0].name(), "Operations");
    assert_eq!(
        serde_json::to_value(&entries[1]).unwrap()["extension"],
        "402"
    );
    assert!(view(&saving).saving);

    let all = view(&ui(removed_all(), DirectoryAction::Save));
    let question = all.confirming.expect("the core asks");
    assert_eq!(question.title, "Remove every transfer target?");
    assert!(question.destructive);

    let dismissed = ui(written(Err(())), DirectoryAction::DismissNotice);
    assert_eq!(view(&dismissed).notice, None);
}

/// A change not saved is what the settings kit's question guards.
#[test]
fn leaving_with_a_change_not_saved_asks_first() {
    assert!(!leaves_unsaved(&typed().model, &Event::Back));
    let mut session = edited();
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
