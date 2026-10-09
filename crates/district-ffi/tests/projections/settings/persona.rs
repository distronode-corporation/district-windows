//! The Persona section (src/settings/persona.rs).

use district_core::{
    CoreConfig, Effect, Event, PersonaEngineEdit, PersonaEvent, Route, WorkspaceSection,
};
use district_ffi::settings::SectionStatus;
use district_ffi::settings::persona::{PersonaAction, PersonaField, PersonaView};
use district_ffi::{Held, ScreenView, UiEvent, leaves_unsaved, screen_view};
use serde_json::{Value, json};

use super::super::{
    Case, Session, config, contracts, route, server_error, signed_in, signed_in_with, viewer,
};

/// This area's snapshot cases.
pub(crate) fn cases() -> Vec<Case> {
    vec![
        ("persona-loading", open(signed_in())),
        ("persona-loaded", ready(signed_in())),
        (
            "persona-empty",
            read(signed_in(), Ok(empty_config()), Ok(options())),
        ),
        (
            "persona-options-failed",
            read(signed_in(), Ok(stored()), Err(())),
        ),
        ("persona-failed", read(signed_in(), Err(()), Ok(options()))),
        ("persona-editing", edited(ready(signed_in()))),
        (
            "persona-language-refused",
            act(
                ready(signed_in()),
                PersonaAction::ChooseLanguage {
                    value: "xx-NOT-OFFERED".to_owned(),
                },
            ),
        ),
        ("persona-saving", saving(signed_in())),
        ("persona-saved", saved(signed_in(), Ok(()), true)),
        ("persona-save-failed", saved(signed_in(), Err(()), false)),
        (
            "persona-saved-not-read-back",
            saved(signed_in(), Ok(()), false),
        ),
        (
            "persona-audition-open",
            act(ready(signed_in()), PersonaAction::OpenPreview),
        ),
        (
            "persona-audition-no-calls",
            act(
                act(ready(signed_in()), PersonaAction::OpenPreview),
                PersonaAction::StartPreview,
            ),
        ),
        ("persona-audition-starting", starting()),
        ("persona-audition-connecting", issued(starting(), true)),
        ("persona-audition-failed", issued(starting(), false)),
        (
            "persona-audition-ended",
            act(issued(starting(), true), PersonaAction::StopPreview),
        ),
    ]
}

fn act(session: Session, action: PersonaAction) -> Session {
    session.ui(UiEvent::Persona { action })
}

fn open(session: Session) -> Session {
    act(session, PersonaAction::Open)
}

fn stored() -> Value {
    contracts::json("district-workspace-config.json")
}

fn options() -> Value {
    contracts::json("district-persona-options.json")
}

/// A workspace nobody set up: no persona stored.
fn empty_config() -> Value {
    let mut config = stored();
    config["config"]["aiPersona"] = Value::Null;
    config
}

/// The section opened and its two reads answered, each `Ok` with its JSON or
/// failed.
fn read(session: Session, config: Result<Value, ()>, options: Result<Value, ()>) -> Session {
    open(session)
        .answer(
            |e| matches!(e, Effect::LoadWorkspaceConfig { .. }),
            |ticket| Event::WorkspaceConfigLoaded {
                ticket,
                result: config
                    .map(|value| contracts::decode("config", value))
                    .map_err(|()| server_error()),
            },
        )
        .answer(
            |e| matches!(e, Effect::LoadPersonaOptions { .. }),
            |ticket| Event::PersonaOptionsLoaded {
                ticket,
                result: options
                    .map(|value| contracts::decode("options", value))
                    .map_err(|()| server_error()),
            },
        )
}

/// Read, from the contract's settings and options.
fn ready(session: Session) -> Session {
    read(session, Ok(stored()), Ok(options()))
}

fn edited(session: Session) -> Session {
    act(
        session,
        PersonaAction::EditText {
            field: PersonaField::Name,
            value: "Grace".to_owned(),
        },
    )
}

fn saving(session: Session) -> Session {
    act(edited(ready(session)), PersonaAction::Save)
}

/// A save answered `written`; when it landed, the read back after it answered
/// too, `read_back` saying whether that read worked.
fn saved(session: Session, written: Result<(), ()>, read_back: bool) -> Session {
    let landed = written.is_ok();
    let session = saving(session).answer(
        |e| matches!(e, Effect::SavePersona { .. }),
        |ticket| Event::SettingsWritten {
            ticket,
            result: written.map_err(|()| server_error()),
        },
    );
    if !landed {
        return session;
    }
    session.answer(
        |e| matches!(e, Effect::LoadWorkspaceConfig { .. }),
        |ticket| Event::WorkspaceConfigLoaded {
            ticket,
            result: if read_back {
                let mut config = stored();
                config["config"]["aiPersona"]["name"] = json!("Grace");
                Ok(contracts::decode("config", config))
            } else {
                Err(server_error())
            },
        },
    )
}

/// A build that can carry calls, as Windows is.
fn with_calls() -> Session {
    signed_in_with(CoreConfig {
        calls_available: true,
        ..config()
    })
}

/// The audition started: its billed credential asked for.
fn starting() -> Session {
    act(
        act(ready(with_calls()), PersonaAction::OpenPreview),
        PersonaAction::StartPreview,
    )
}

/// The credential answered, `ok` with the contract's or refused.
fn issued(session: Session, ok: bool) -> Session {
    session.answer(
        |e| matches!(e, Effect::RequestPersonaPreview { .. }),
        |ticket| Event::PersonaPreviewIssued {
            ticket,
            result: if ok {
                Ok(contracts::read("district-persona-preview-token.json"))
            } else {
                Err(server_error())
            },
        },
    )
}

fn view(session: &Session) -> PersonaView {
    match screen_view(&session.model) {
        ScreenView::Persona { view } => view,
        other => panic!("not the persona: {other:?}"),
    }
}

/// Built, the section opens from the hub's row and shows its form once read.
#[test]
fn built_it_opens_and_shows_its_form() {
    let session = ready(signed_in());
    assert_eq!(route(&session), Route::Workspace(WorkspaceSection::Persona));
    let shown = view(&session);
    assert_eq!(shown.status, SectionStatus::Ready);
    assert_eq!(shown.title, "Persona");
    assert!(shown.text_editable && shown.engine_editable && shown.can_preview);
    assert!(!shown.can_save, "nothing changed");
}

#[test]
fn each_action_is_its_core_event() {
    let persona = |event| Event::Persona(event);
    for (action, event) in [
        (
            PersonaAction::Open,
            Event::Navigate(Route::Workspace(WorkspaceSection::Persona)),
        ),
        (
            PersonaAction::EditText {
                field: PersonaField::Greeting,
                value: "Hello".to_owned(),
            },
            persona(PersonaEvent::EditText {
                field: district_core::PersonaText::Greeting,
                value: "Hello".to_owned(),
            }),
        ),
        (
            PersonaAction::EditText {
                field: PersonaField::Personality,
                value: "Kind".to_owned(),
            },
            persona(PersonaEvent::EditText {
                field: district_core::PersonaText::Personality,
                value: "Kind".to_owned(),
            }),
        ),
        (
            PersonaAction::ChooseLanguage {
                value: "fr-CA".to_owned(),
            },
            persona(PersonaEvent::Engine(PersonaEngineEdit::Language(
                "fr-CA".to_owned(),
            ))),
        ),
        (
            PersonaAction::ChooseResponseLength {
                value: "detailed".to_owned(),
            },
            persona(PersonaEvent::Engine(PersonaEngineEdit::ResponseLength(
                "detailed".to_owned(),
            ))),
        ),
        (PersonaAction::Save, persona(PersonaEvent::Save)),
        (
            PersonaAction::DismissSaveNotice,
            persona(PersonaEvent::DismissSaveNotice),
        ),
        (
            PersonaAction::OpenVoiceStudio,
            Event::Navigate(Route::Workspace(WorkspaceSection::VoiceStudio)),
        ),
        (
            PersonaAction::OpenPreview,
            persona(PersonaEvent::OpenPreview),
        ),
        (
            PersonaAction::StartPreview,
            persona(PersonaEvent::StartPreview),
        ),
        (
            PersonaAction::StopPreview,
            persona(PersonaEvent::StopPreview),
        ),
        (
            PersonaAction::ClosePreview,
            persona(PersonaEvent::ClosePreview),
        ),
    ] {
        assert_eq!(UiEvent::Persona { action }.events(), [event]);
    }
}

/// With the form edited, every move away is held behind "Discard your
/// changes?"; saved, nothing is.
#[test]
fn leaving_an_edited_persona_asks_first() {
    let leaves = [
        UiEvent::Back,
        UiEvent::Refresh,
        UiEvent::Navigate {
            destination: district_ffi::NavDestination::Overview,
        },
        act_event(PersonaAction::OpenVoiceStudio),
    ];
    let edited = edited(ready(signed_in()));
    for leave in &leaves {
        let mut held = Held::default();
        assert_eq!(held.pass(&edited.model, leave.clone().events()), []);
        assert!(held.question().is_some(), "{leave:?}");
    }
    let clean = saved(signed_in(), Ok(()), true);
    assert!(!view(&clean).can_save);
    for leave in leaves {
        assert!(
            !leave
                .events()
                .iter()
                .any(|event| leaves_unsaved(&clean.model, event)),
            "saved, nothing is lost"
        );
    }
}

fn act_event(action: PersonaAction) -> UiEvent {
    UiEvent::Persona { action }
}

/// The edit shows, Save works, and while saving the form is read only.
#[test]
fn an_edit_is_saved_and_the_form_waits_for_it() {
    let shown = view(&edited(ready(signed_in())));
    assert_eq!(shown.name, "Grace");
    assert!(shown.can_save);
    let saving = view(&saving(signed_in()));
    assert!(saving.saving && !saving.can_save && !saving.text_editable);
    let done = view(&saved(signed_in(), Ok(()), true));
    assert_eq!(done.notice.map(|notice| notice.saved), Some(true));
    let failed = view(&saved(signed_in(), Err(()), false));
    assert_eq!(failed.notice.map(|notice| notice.saved), Some(false));
    assert_eq!(failed.name, "Grace", "a failed save keeps the edit");
    assert!(matches!(
        view(&saved(signed_in(), Ok(()), false)).status,
        SectionStatus::Stale { .. }
    ));
}

/// The audition is said to be billed before it starts, and Start is the only
/// thing that asks for it.
#[test]
fn the_audition_is_billed_and_asked_for_only_by_start() {
    let open = act(ready(with_calls()), PersonaAction::OpenPreview);
    let dialog = view(&open).audition.expect("the dialog opens");
    assert!(
        dialog.billed_note.contains("billed"),
        "{}",
        dialog.billed_note
    );
    assert!(dialog.show_start && dialog.can_start && !dialog.show_stop);
    assert!(
        !open
            .pending
            .iter()
            .any(|e| matches!(e, Effect::RequestPersonaPreview { .. })),
        "opening the dialog asks for nothing"
    );
    let started = act(open, PersonaAction::StartPreview);
    let asked = started
        .pending
        .iter()
        .filter(|e| matches!(e, Effect::RequestPersonaPreview { .. }))
        .count();
    assert_eq!(asked, 1);
    // A second press while it starts asks for nothing more.
    let again = act(started, PersonaAction::StartPreview);
    assert_eq!(
        again
            .pending
            .iter()
            .filter(|e| matches!(e, Effect::RequestPersonaPreview { .. }))
            .count(),
        1
    );
    let ended = view(&act(issued(again, true), PersonaAction::StopPreview))
        .audition
        .expect("still open");
    assert!(ended.cooling.is_some() && !ended.can_start);
    let closed = act(
        act(issued(starting(), true), PersonaAction::ClosePreview),
        PersonaAction::Save,
    );
    assert_eq!(view(&closed).audition, None);
}

/// A viewer is not offered the section, and the core keeps it from them.
#[test]
fn a_viewer_never_reaches_the_persona() {
    let session = open(viewer());
    assert_ne!(route(&session), Route::Workspace(WorkspaceSection::Persona));
    assert!(!matches!(
        screen_view(&session.model),
        ScreenView::Persona { .. }
    ));
}
