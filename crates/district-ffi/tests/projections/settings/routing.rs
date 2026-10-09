//! The Routing section (src/settings/routing.rs).

use district_core::{Effect, Event, Route, RoutingRulesEvent, SessionState, WorkspaceSection};
use district_ffi::settings::routing::{RoutingAction, RoutingField, RoutingView};
use district_ffi::settings::{SectionStatus, SettingsAction, SettingsSection};
use district_ffi::{Held, NavDestination, ScreenView, UiEvent, leaves_unsaved, screen_view};
use district_model::RoutingRuleField;
use serde_json::{Value, json};

use super::super::{Case, Session, contracts, offered, route, server_error, signed_in, viewer};

fn ui(session: Session, action: RoutingAction) -> Session {
    session.ui(UiEvent::Routing { action })
}

/// The section opened from the hub, nothing answered yet.
fn opened() -> Session {
    signed_in()
        .ui(UiEvent::Settings {
            action: SettingsAction::Open,
        })
        .ui(UiEvent::Settings {
            action: SettingsAction::OpenSection {
                section: SettingsSection::Routing,
            },
        })
}

/// The settings fixture with its routing rules replaced by `rules`.
fn config_with(rules: Value) -> Value {
    let mut config = contracts::json("district-workspace-config.json");
    config["config"]["routingRules"] = rules;
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

/// Read from the core's fixture: three rules, two in the older stored shape.
fn loaded() -> Session {
    read(opened(), contracts::json("district-workspace-config.json"))
}

/// A rule added and its value typed, not saved.
fn edited() -> Session {
    ui(
        ui(loaded(), RoutingAction::Add),
        RoutingAction::Edit {
            index: 3,
            field: RoutingField::Value,
            value: "dental".to_owned(),
        },
    )
}

fn confirming() -> Session {
    ui(edited(), RoutingAction::Save)
}

fn saving() -> Session {
    ui(confirming(), RoutingAction::ConfirmSave)
}

fn written(result: Result<(), ()>) -> Session {
    saving().answer(
        |e| matches!(e, Effect::SaveRoutingRules { .. }),
        |ticket| Event::SettingsWritten {
            ticket,
            result: result.map_err(|()| server_error()),
        },
    )
}

fn removed_all() -> Session {
    let mut session = loaded();
    for _ in 0..3 {
        session = ui(session, RoutingAction::Remove { index: 0 });
    }
    session
}

/// This area's snapshot cases.
pub(crate) fn cases() -> Vec<Case> {
    vec![
        ("routing-loading", opened()),
        ("routing-loaded", loaded()),
        ("routing-failed", read_failed(opened())),
        ("routing-empty", read(opened(), config_with(Value::Null))),
        (
            "routing-unmodellable",
            read(opened(), config_with(json!({ "billing": "transfer" }))),
        ),
        ("routing-edited", edited()),
        ("routing-confirming", confirming()),
        (
            "routing-confirming-remove-all",
            ui(removed_all(), RoutingAction::Save),
        ),
        ("routing-saving", saving()),
        (
            "routing-saved",
            read(
                written(Ok(())),
                contracts::json("district-workspace-config.json"),
            ),
        ),
        ("routing-save-failed", written(Err(()))),
        ("routing-saved-not-read-back", read_failed(written(Ok(())))),
    ]
}

fn view(session: &Session) -> RoutingView {
    match screen_view(&session.model) {
        ScreenView::Routing { view } => view,
        other => panic!("not the routing rules: {other:?}"),
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
    assert_eq!(route(&session), Route::Workspace(WorkspaceSection::Routing));
    assert!(offered(&session).contains(&NavDestination::Settings));
    assert_eq!(view(&session).title, "Call routing rules");
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
            .all(|row| row.section != SettingsSection::Routing)
    );
}

#[test]
fn each_action_is_its_core_event() {
    let events = |action| UiEvent::Routing { action }.events();
    assert_eq!(
        events(RoutingAction::Open),
        [Event::Navigate(Route::Workspace(WorkspaceSection::Routing))]
    );
    assert_eq!(
        events(RoutingAction::Add),
        [Event::RoutingRules(RoutingRulesEvent::Add)]
    );
    for (field, core) in [
        (RoutingField::Field, RoutingRuleField::Field),
        (RoutingField::Operator, RoutingRuleField::Operator),
        (RoutingField::Value, RoutingRuleField::Value),
        (RoutingField::Voice, RoutingRuleField::Voice),
        (RoutingField::Instruction, RoutingRuleField::Instruction),
    ] {
        assert_eq!(
            events(RoutingAction::Edit {
                index: 2,
                field,
                value: "x".to_owned(),
            }),
            [Event::RoutingRules(RoutingRulesEvent::Edit {
                index: 2,
                field: core,
                value: "x".to_owned(),
            })]
        );
    }
    assert_eq!(
        events(RoutingAction::Remove { index: 1 }),
        [Event::RoutingRules(RoutingRulesEvent::Remove(1))]
    );
    for (action, event) in [
        (RoutingAction::Save, RoutingRulesEvent::Save),
        (RoutingAction::ConfirmSave, RoutingRulesEvent::ConfirmSave),
        (RoutingAction::CancelSave, RoutingRulesEvent::CancelSave),
        (
            RoutingAction::DismissNotice,
            RoutingRulesEvent::DismissNotice,
        ),
    ] {
        assert_eq!(events(action), [Event::RoutingRules(event)]);
    }
}

/// Each stored rule is shown with every key kept, a stored value the choices
/// lack as it is stored, and the engine shown, not offered.
#[test]
fn the_rules_read_as_stored() {
    let read = view(&loaded());
    assert_eq!(read.status, SectionStatus::Ready);
    assert_eq!(read.rules.len(), 3);
    assert!(read.can_edit && !read.can_save);
    assert_eq!(read.rules[0].summary, "No condition set here");
    assert!(read.rules[0].kept.as_deref().unwrap().contains("Match"));
    assert_eq!(read.rules[2].voice.picker.selected, "Fenrir");
    assert_eq!(read.rules[2].engine, "The persona's own");
    assert_eq!(read.rules[0].field.picker.selected_label, "Not chosen");
}

/// Saving asks first, in the core's words, and only "Replace" saves; the list
/// sent is the one on screen, every stored key going back.
#[test]
fn a_save_asks_first_and_sends_the_list_on_screen() {
    let asked = confirming();
    let question = view(&asked).confirming.expect("the core asks");
    assert_eq!(question.title, "Replace the routing rules?");
    assert_eq!(question.action, "Replace");
    assert!(!question.destructive);
    assert!(
        asked
            .pending
            .iter()
            .all(|e| !matches!(e, Effect::SaveRoutingRules { .. }))
    );
    let cancelled = ui(asked, RoutingAction::CancelSave);
    assert_eq!(view(&cancelled).confirming, None);
    assert!(unsaved(&cancelled));

    let saving = saving();
    let rules = saving
        .pending
        .iter()
        .find_map(|e| match e {
            Effect::SaveRoutingRules { rules, .. } => Some(rules.clone()),
            _ => None,
        })
        .expect("the save is sent");
    assert_eq!(rules.len(), 4);
    assert_eq!(rules[0].as_json()["target"], "+14165550188");
    assert_eq!(rules[3].get(RoutingRuleField::Value), "dental");
    assert!(view(&saving).saving);
    assert!(!view(&saving).can_edit);

    let all = view(&ui(removed_all(), RoutingAction::Save));
    let question = all.confirming.expect("the core asks");
    assert_eq!(question.title, "Remove every routing rule?");
    assert!(question.destructive);

    let dismissed = ui(written(Err(())), RoutingAction::DismissNotice);
    assert_eq!(view(&dismissed).notice, None);
}

/// A change not saved is what the settings kit's question guards.
#[test]
fn leaving_with_a_change_not_saved_asks_first() {
    assert!(!leaves_unsaved(&loaded().model, &Event::Back));
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
