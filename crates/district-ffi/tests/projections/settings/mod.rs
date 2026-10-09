//! The workspace settings hub (src/settings/mod.rs), and one file per section beside this one.

mod call_handling;
mod directory;
mod knowledge;
mod members;
mod messaging;
mod numbers;
mod persona;
mod routing;
mod tools;
mod voice_studio;

use district_core::{
    Effect, Event, NotificationTarget, PersonaEvent, PersonaText, Route, SessionState,
    WorkspaceSection,
};
use district_ffi::settings::{SettingsAction, SettingsSection};
use district_ffi::{Held, NavDestination, ScreenView, UiEvent, leaves_unsaved, screen_view};

use super::{Case, Session, contracts, offered, route, signed_in, signed_in_to, viewer};

/// The hub's snapshot cases, and every section's.
pub(crate) fn cases() -> Vec<Case> {
    [
        vec![
            ("settings-hub", hub(signed_in())),
            ("settings-hub-viewer", hub(viewer())),
        ],
        persona::cases(),
        voice_studio::cases(),
        call_handling::cases(),
        routing::cases(),
        directory::cases(),
        tools::cases(),
        knowledge::cases(),
        messaging::cases(),
        members::cases(),
        numbers::cases(),
    ]
    .into_iter()
    .flatten()
    .collect()
}

fn hub(session: Session) -> Session {
    session.ui(UiEvent::Settings {
        action: SettingsAction::Open,
    })
}

/// Built, the hub is offered to every role and shows the core's rows for it;
/// each row opens its section, which stays "Not in this version yet" until its
/// own packet.
#[test]
fn built_the_hub_is_offered_and_its_rows_open_their_sections() {
    for session in [hub(signed_in()), hub(viewer())] {
        assert_eq!(route(&session), Route::Workspace(WorkspaceSection::Hub));
        assert!(offered(&session).contains(&NavDestination::Settings));
        let ScreenView::WorkspaceSettings { view } = screen_view(&session.model) else {
            panic!("the hub shows");
        };
        assert!(!view.groups.is_empty());
        assert!(!view.note.is_empty());
    }
    // Tools is still unbuilt; a built section shows its own page (persona.rs).
    let tools = hub(signed_in()).ui(UiEvent::Settings {
        action: SettingsAction::OpenSection {
            section: SettingsSection::Tools,
        },
    });
    assert_eq!(route(&tools), Route::Workspace(WorkspaceSection::Tools));
    assert!(matches!(
        screen_view(&tools.model),
        ScreenView::Unavailable { .. }
    ));
}

#[test]
fn each_action_is_its_core_event() {
    assert_eq!(
        UiEvent::Settings {
            action: SettingsAction::Open,
        }
        .events(),
        [Event::Navigate(Route::Workspace(WorkspaceSection::Hub))]
    );
    assert_eq!(
        UiEvent::Settings {
            action: SettingsAction::OpenSection {
                section: SettingsSection::Numbers,
            },
        }
        .events(),
        [Event::Navigate(Route::Workspace(WorkspaceSection::Numbers))]
    );
    assert_eq!(UiEvent::DiscardChanges.events(), []);
    assert_eq!(UiEvent::KeepEditing.events(), []);
}

/// The agency workspace of the contract list, the default being a viewer's.
const AGENCY: &str = "ws-contract-active";

/// On the persona section of the agency workspace, read, with or without an
/// edit not saved.
fn on_persona(edited: bool) -> Session {
    let session = signed_in_to(Ok(contracts::read("district-workspace-list.json")))
        .send(Event::SelectWorkspace(AGENCY.to_owned()))
        .send(Event::Navigate(Route::Workspace(WorkspaceSection::Persona)))
        .answer(
            |e| matches!(e, Effect::LoadWorkspaceConfig { .. }),
            |ticket| Event::WorkspaceConfigLoaded {
                ticket,
                result: Ok(contracts::read("district-workspace-config.json")),
            },
        );
    if edited {
        session.send(Event::Persona(PersonaEvent::EditText {
            field: PersonaText::Name,
            value: "Grace".to_owned(),
        }))
    } else {
        session
    }
}

fn unsaved(session: &Session) -> bool {
    match session.model.session() {
        SessionState::SignedIn(signed_in) => signed_in.settings_unsaved(),
        _ => false,
    }
}

/// Every move that leaves the section is held while it has unsaved changes,
/// as on Linux: another screen, Back, a refresh, another workspace, and a
/// notification's target. Staying, saving and the rest are not.
#[test]
fn each_leave_kind_is_held_only_with_unsaved_changes() {
    let leaving = [
        Event::Back,
        Event::Refresh,
        Event::Navigate(Route::Overview),
        Event::Navigate(Route::Workspace(WorkspaceSection::Tools)),
        Event::Navigate(Route::Workspace(WorkspaceSection::Hub)),
        Event::SelectWorkspace("ws-contract-viewer".to_owned()),
        Event::OpenNotification(NotificationTarget::Message {
            workspace_id: AGENCY.to_owned(),
            message_id: "m-1".to_owned(),
        }),
    ];
    let clean = on_persona(false);
    assert!(!unsaved(&clean));
    for event in &leaving {
        assert!(
            !leaves_unsaved(&clean.model, event),
            "nothing to lose: {event:?}"
        );
    }
    let edited = on_persona(true);
    assert!(unsaved(&edited));
    for event in &leaving {
        assert!(leaves_unsaved(&edited.model, event), "{event:?}");
    }
    for staying in [
        Event::Navigate(Route::Workspace(WorkspaceSection::Persona)),
        Event::SelectWorkspace(AGENCY.to_owned()),
        Event::Persona(PersonaEvent::Save),
        Event::DismissNotice,
        Event::SignOut,
    ] {
        assert!(!leaves_unsaved(&edited.model, &staying), "{staying:?}");
    }
}

/// The actor's rule, on the real model: a held move changes nothing; Keep
/// editing drops it and the edit stays; Discard hands it over and the section
/// is left, its edits gone.
#[test]
fn discard_applies_the_held_move_and_keep_editing_drops_it() {
    for (leave, to) in [
        (UiEvent::Back, Route::Workspace(WorkspaceSection::Hub)),
        (
            UiEvent::Navigate {
                destination: NavDestination::Overview,
            },
            Route::Overview,
        ),
    ] {
        let mut held = Held::default();
        let mut session = on_persona(true);
        let passed = held.pass(&session.model, leave.clone().events());
        assert_eq!(passed, []);
        assert!(held.question().is_some());
        // A second move while asked is dropped, not queued.
        assert_eq!(held.pass(&session.model, UiEvent::Refresh.events()), []);
        held.keep();
        assert!(held.question().is_none());
        assert_eq!(held.discard(), []);
        assert_eq!(route(&session), Route::Workspace(WorkspaceSection::Persona));
        assert!(unsaved(&session));

        assert_eq!(held.pass(&session.model, leave.events()), []);
        for event in held.discard() {
            session = session.send(event);
        }
        assert!(held.question().is_none());
        assert_eq!(route(&session), to);
        assert!(!unsaved(&session));
    }
}
