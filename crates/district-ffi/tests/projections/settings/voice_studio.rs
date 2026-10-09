//! The VoiceStudio section (src/settings/voice_studio.rs).

use district_core::studio::PickerKind;
use district_core::{
    Effect, Event, Route, SessionState, StudioEdit, VoiceStudioEvent, WorkspaceSection,
};
use district_ffi::settings::voice_studio::{
    StudioControlView, StudioFormView, StudioPicker, VoiceStudioAction, VoiceStudioView,
};
use district_ffi::settings::{SectionStatus, SettingsAction, SettingsSection};
use district_ffi::{Held, NavDestination, ScreenView, UiEvent, leaves_unsaved, screen_view};
use district_model::VoiceStudioResponse;
use serde_json::{Value, json};

use super::super::{Case, Session, contracts, offered, route, server_error, signed_in, viewer};

/// The recorded read.
const FIXTURE: &str = "district-voice-studio.json";
/// A voice of the fixture's voice model other than the saved one.
const OTHER_VOICE: &str = "aura-2-luna-en";

/// This area's snapshot cases.
pub(crate) fn cases() -> Vec<Case> {
    vec![
        ("voice-studio-loading", opened()),
        ("voice-studio-failed", failed()),
        ("voice-studio-ready", ready()),
        ("voice-studio-turn-taking", ready().ui(leg("turn"))),
        ("voice-studio-edited", edited()),
        (
            "voice-studio-realtime",
            ready().ui(VoiceStudioAction::ApplyRecipe {
                recipe_id: "realtime".to_owned(),
            }
            .into_ui()),
        ),
        (
            "voice-studio-latest",
            ready().ui(VoiceStudioAction::SelectTier {
                tier: "latest".to_owned(),
            }
            .into_ui()),
        ),
        ("voice-studio-saving", saving()),
        ("voice-studio-saved", saved()),
        (
            "voice-studio-not-held",
            written().answer(is_read, |ticket| read_as(ticket, json(FIXTURE))),
        ),
        (
            "voice-studio-save-failed",
            saving().answer(is_save, |ticket| Event::SettingsWritten {
                ticket,
                result: Err(server_error()),
            }),
        ),
        (
            "voice-studio-saved-stale",
            written().answer(is_read, |ticket| Event::VoiceStudioLoaded {
                ticket,
                result: Err(server_error()),
            }),
        ),
        (
            "voice-studio-viewer",
            viewer().ui(VoiceStudioAction::Open.into_ui()),
        ),
    ]
}

trait IntoUi {
    fn into_ui(self) -> UiEvent;
}

impl IntoUi for VoiceStudioAction {
    fn into_ui(self) -> UiEvent {
        UiEvent::VoiceStudio { action: self }
    }
}

fn leg(leg: &str) -> UiEvent {
    VoiceStudioAction::SelectLeg {
        leg: leg.to_owned(),
    }
    .into_ui()
}

fn is_read(effect: &Effect) -> bool {
    matches!(effect, Effect::LoadVoiceStudio { .. })
}

fn is_save(effect: &Effect) -> bool {
    matches!(effect, Effect::SavePersona { .. })
}

fn json(name: &str) -> Value {
    contracts::json(name)
}

fn read_as(ticket: district_core::Ticket, value: Value) -> Event {
    Event::VoiceStudioLoaded {
        ticket,
        result: Ok(Box::new(contracts::decode::<VoiceStudioResponse>(
            "the voice studio read",
            value,
        ))),
    }
}

/// The section opened from the hub, its read on its way.
fn opened() -> Session {
    signed_in()
        .ui(UiEvent::Settings {
            action: SettingsAction::Open,
        })
        .ui(UiEvent::Settings {
            action: SettingsAction::OpenSection {
                section: SettingsSection::VoiceStudio,
            },
        })
}

fn failed() -> Session {
    opened().answer(is_read, |ticket| Event::VoiceStudioLoaded {
        ticket,
        result: Err(server_error()),
    })
}

fn ready() -> Session {
    opened().answer(is_read, |ticket| read_as(ticket, json(FIXTURE)))
}

/// The voice leg open, and another voice chosen.
fn edited() -> Session {
    ready().ui(leg("tts")).ui(VoiceStudioAction::Pick {
        picker: StudioPicker::Voice,
        value: OTHER_VOICE.to_owned(),
    }
    .into_ui())
}

fn saving() -> Session {
    edited().ui(VoiceStudioAction::Save.into_ui())
}

/// The save landed; the read after it is on its way.
fn written() -> Session {
    saving().answer(is_save, |ticket| Event::SettingsWritten {
        ticket,
        result: Ok(()),
    })
}

/// The read after the save holds the voice sent.
fn saved() -> Session {
    let mut value = json(FIXTURE);
    value["current"]["fields"]["voice"] = json!(OTHER_VOICE);
    value["current"]["chain"]["voice"] = json!(OTHER_VOICE);
    value["current"]["chain"]["engineMix"]["tts"]["voice"] = json!(OTHER_VOICE);
    written().answer(is_read, |ticket| read_as(ticket, value))
}

fn view(session: &Session) -> VoiceStudioView {
    match screen_view(&session.model) {
        ScreenView::VoiceStudio { view } => view,
        other => panic!("not Voice Studio: {other:?}"),
    }
}

fn form(session: &Session) -> StudioFormView {
    view(session).studio.expect("the Studio is read")
}

fn unsaved(session: &Session) -> bool {
    match session.model.session() {
        SessionState::SignedIn(signed_in) => signed_in.settings_unsaved(),
        _ => false,
    }
}

/// Built, the section is a row of the hub for an agency, opens as Voice
/// Studio under the hub's entry, and reads the Studio.
#[test]
fn built_it_opens_from_the_hub_and_reads_the_studio() {
    let session = opened();
    assert_eq!(
        route(&session),
        Route::Workspace(WorkspaceSection::VoiceStudio)
    );
    assert!(offered(&session).contains(&NavDestination::Settings));
    let loading = view(&session);
    assert_eq!(loading.title, "Voice");
    assert_eq!(loading.status, SectionStatus::Loading);

    let form = form(&ready());
    assert!(form.editable && form.can_change && !form.dirty && !form.can_save);
    assert_eq!(form.tier, "stable");
    assert_eq!(form.tiers.len(), 2);
    assert!(form.recipes.iter().any(|recipe| recipe.selected));
    assert_eq!(form.blocks.len(), 4);
    assert!(form.blocks[0].open);
    assert_eq!(form.notice, None);
    assert!(form.meter.headline.contains("970"));
    assert!(form.residency.in_region);

    let ScreenView::VoiceStudio { view } = screen_view(&failed().model) else {
        panic!("Voice Studio shows its failure");
    };
    assert!(
        matches!(view.status, SectionStatus::Failed { ref title, .. } if title == "Could not load Voice Studio")
    );
    assert_eq!(view.studio, None);
}

/// A viewer is never offered Voice Studio, and cannot open it.
#[test]
fn a_viewer_cannot_open_it() {
    let session = viewer().ui(UiEvent::Settings {
        action: SettingsAction::Open,
    });
    let ScreenView::WorkspaceSettings { view } = screen_view(&session.model) else {
        panic!("the hub shows");
    };
    assert!(
        view.groups
            .iter()
            .flat_map(|group| group.rows.iter())
            .all(|row| row.section != SettingsSection::VoiceStudio)
    );
    let session = session.ui(VoiceStudioAction::Open.into_ui());
    assert_eq!(route(&session), Route::Workspace(WorkspaceSection::Hub));
}

/// Each leg's editor: the ear's pickers, turn-taking's tuning with the
/// advanced part and its interruptions heading, the voice's picker.
#[test]
fn the_editor_follows_the_open_leg() {
    let ear = form(&ready()).editor;
    let pickers: Vec<StudioPicker> = ear.pickers.iter().map(|p| p.picker).collect();
    assert_eq!(pickers, [StudioPicker::Vendor, StudioPicker::Model]);
    let brain = form(&ready().ui(leg("llm"))).editor;
    let pickers: Vec<StudioPicker> = brain.pickers.iter().map(|p| p.picker).collect();
    assert_eq!(pickers, [StudioPicker::Model, StudioPicker::Location]);
    assert!(ear.pickers.iter().all(|p| {
        p.choices
            .choices
            .iter()
            .any(|o| o.value == p.choices.selected)
    }));

    let turn = form(&ready().ui(leg("turn"))).editor;
    assert!(turn.pickers.is_empty());
    assert!(turn.has_advanced);
    assert!(
        turn.controls
            .iter()
            .any(|c| !c.advanced && matches!(c.control, StudioControlView::Switch { on: false }))
    );
    let headed: Vec<&str> = turn
        .controls
        .iter()
        .filter_map(|c| c.heading.as_deref())
        .collect();
    assert_eq!(headed, ["Interruptions"]);
    let slider = turn
        .controls
        .iter()
        .find(|c| c.key == "engineMix.turn.minDelay")
        .expect("the shortest wait");
    assert_eq!(
        slider.control,
        StudioControlView::Slider {
            min: 0,
            max: 1000,
            step: 50,
            value: None,
            start: 300,
            value_text: "0.30".to_owned(),
            default_label: Some("Use the default (0.30)".to_owned()),
        }
    );

    let voice = form(&ready().ui(leg("tts"))).editor;
    let last = voice.pickers.last().expect("the voice picker");
    assert_eq!(last.picker, StudioPicker::Voice);
    assert_eq!(last.choices.selected, "aura-2-asteria-en");
    assert!(
        last.choices
            .choices
            .iter()
            .any(|o| o.label.ends_with("(English (Feminine))"))
    );
}

/// Every control the core offers changes what it holds, through the
/// projection's actions: a slider in thousandths and back to the default, a
/// select, the switch, key terms, a picker, a recipe and Reset.
#[test]
fn each_control_edits_the_studio() {
    let turn = ready().ui(leg("turn"));
    let moved = turn.ui(VoiceStudioAction::SetNumber {
        key: "engineMix.turn.minDelay".to_owned(),
        value: Some(512),
    }
    .into_ui());
    let slider = |session: &Session| {
        form(session)
            .editor
            .controls
            .into_iter()
            .find(|c| c.key == "engineMix.turn.minDelay")
            .unwrap()
            .control
    };
    // Snapped by the core to its step.
    assert!(
        matches!(slider(&moved), StudioControlView::Slider { value: Some(500), ref value_text, .. } if value_text == "0.50")
    );
    assert!(form(&moved).dirty && form(&moved).can_save);
    let back = moved.ui(VoiceStudioAction::SetNumber {
        key: "engineMix.turn.minDelay".to_owned(),
        value: None,
    }
    .into_ui());
    assert!(matches!(
        slider(&back),
        StudioControlView::Slider { value: None, .. }
    ));

    let switched = back.ui(VoiceStudioAction::SetFlag {
        key: "engineMix.preemptiveTts".to_owned(),
        on: true,
    }
    .into_ui());
    assert!(
        form(&switched)
            .editor
            .controls
            .iter()
            .any(|c| c.control == StudioControlView::Switch { on: true })
    );

    let ear = ready().ui(VoiceStudioAction::SetLines {
        key: "engineMix.stt.keyterms".to_owned(),
        text: "Ada\n  Ada \nGrace\n".to_owned(),
    }
    .into_ui());
    assert!(form(&ear).editor.controls.iter().any(|c| c.control
        == StudioControlView::Lines {
            text: "Ada\nGrace".to_owned()
        }));

    let brain = ready().ui(leg("llm"));
    let thinking = form(&brain)
        .editor
        .controls
        .into_iter()
        .find(|c| c.key == "engineMix.llm.thinking")
        .expect("thinking");
    let StudioControlView::Select { options, selected } = thinking.control else {
        panic!("thinking is a select");
    };
    let other = options
        .iter()
        .find(|o| o.value != selected)
        .expect("another choice");
    let thought = brain.ui(VoiceStudioAction::SetChoice {
        key: "engineMix.llm.thinking".to_owned(),
        value: other.value.clone(),
    }
    .into_ui());
    assert!(form(&thought).dirty);

    let ear = form(&ready()).editor;
    let model = ear
        .pickers
        .iter()
        .find(|p| p.picker == StudioPicker::Model)
        .unwrap();
    if let Some(other) = model
        .choices
        .choices
        .iter()
        .find(|o| o.value != model.choices.selected)
    {
        let picked = ready().ui(VoiceStudioAction::Pick {
            picker: StudioPicker::Model,
            value: other.value.clone(),
        }
        .into_ui());
        assert!(form(&picked).dirty);
    }

    let edited = form(&edited());
    assert!(edited.dirty && edited.can_save);
    assert_eq!(
        edited.based_on.as_deref(),
        Some("Based on Fastest, 1 change.")
    );
    assert_eq!(edited.pending, "Unsaved changes");
    let reset = self::edited().ui(VoiceStudioAction::Reset.into_ui());
    assert!(!form(&reset).dirty);

    let natural = ready().ui(VoiceStudioAction::ApplyRecipe {
        recipe_id: "natural".to_owned(),
    }
    .into_ui());
    assert!(
        form(&natural)
            .recipes
            .iter()
            .any(|r| r.selected && r.recipe_id == "natural")
    );

    let realtime = form(
        &ready().ui(VoiceStudioAction::ApplyRecipe {
            recipe_id: "realtime".to_owned(),
        }
        .into_ui()),
    );
    assert_eq!(realtime.blocks.len(), 1);
    assert!(realtime.blocks[0].open);
}

/// Saving: on its way nothing works; saved, not held, refused, or saved and
/// not read back, each says so in the read's words; Dismiss puts it away.
#[test]
fn a_save_says_how_it_went() {
    let on_its_way = form(&saving());
    assert!(on_its_way.saving && !on_its_way.editable && !on_its_way.can_save);
    assert_eq!(on_its_way.notice, None);

    let done = form(&saved());
    let notice = done.notice.expect("a notice");
    assert_eq!(notice.message, "Voice settings saved.");
    assert!(notice.saved);
    assert!(!done.dirty);
    assert_eq!(done.pending, "All changes saved");

    let not_held = written().answer(is_read, |ticket| read_as(ticket, json(FIXTURE)));
    let notice = form(&not_held).notice.expect("a notice");
    assert_eq!(notice.message, "Voice settings were not saved.");
    assert!(!notice.saved);
    let dismissed = not_held.ui(VoiceStudioAction::DismissNotice.into_ui());
    assert_eq!(form(&dismissed).notice, None);

    let refused = saving().answer(is_save, |ticket| Event::SettingsWritten {
        ticket,
        result: Err(server_error()),
    });
    let refused = form(&refused);
    let notice = refused.notice.expect("a notice");
    assert!(
        notice
            .message
            .starts_with("Voice settings were not saved. ")
    );
    assert!(!notice.saved);
    assert!(refused.dirty && refused.can_save, "the edits are kept");

    let stale = written().answer(is_read, |ticket| Event::VoiceStudioLoaded {
        ticket,
        result: Err(server_error()),
    });
    // Saved and not read back: the kit's page, a read and never a save.
    let view = view(&stale);
    assert_eq!(view.studio, None);
    assert!(matches!(
        view.status,
        SectionStatus::Stale { ref body, ref action, .. }
            if body.starts_with("Saved, but Voice Studio could not be read back.")
                && action == "Read them again"
    ));
}

/// A voice the list lacks is shown under the read's placeholder.
#[test]
fn a_held_voice_the_list_lacks_shows_the_placeholder() {
    let mut value = json(FIXTURE);
    value["current"]["fields"]["voice"] = json!("aura-2-nobody-en");
    value["current"]["chain"]["voice"] = json!("aura-2-nobody-en");
    value["current"]["chain"]["engineMix"]["tts"]["voice"] = json!("aura-2-nobody-en");
    let session = opened()
        .answer(is_read, |ticket| read_as(ticket, value))
        .ui(leg("tts"));
    let voice = form(&session).editor.pickers.pop().unwrap();
    assert_eq!(voice.choices.selected, "aura-2-nobody-en");
    assert_eq!(voice.choices.selected_label, "Choose a voice");
    assert!(
        voice
            .choices
            .choices
            .iter()
            .all(|choice| choice.value != "aura-2-nobody-en")
    );
}

/// "Discard your changes?" on this form: nothing is asked while it holds no
/// edit; with one, every move away is held, Keep editing keeps it, and
/// Discard leaves with the edits gone.
#[test]
fn leaving_with_an_edit_asks_first() {
    let clean = ready();
    assert!(!unsaved(&clean));
    assert!(!leaves_unsaved(&clean.model, &Event::Back));

    let mut session = edited();
    assert!(unsaved(&session));
    for event in [
        Event::Back,
        Event::Refresh,
        Event::Navigate(Route::Overview),
        Event::Navigate(Route::Workspace(WorkspaceSection::Hub)),
    ] {
        assert!(leaves_unsaved(&session.model, &event), "{event:?}");
    }
    assert!(!leaves_unsaved(
        &session.model,
        &Event::VoiceStudio(VoiceStudioEvent::Save)
    ));

    let mut held = Held::default();
    assert_eq!(held.pass(&session.model, UiEvent::Back.events()), []);
    assert!(held.question().is_some());
    held.keep();
    assert!(unsaved(&session));
    assert_eq!(
        route(&session),
        Route::Workspace(WorkspaceSection::VoiceStudio)
    );

    assert_eq!(held.pass(&session.model, UiEvent::Back.events()), []);
    for event in held.discard() {
        session = session.send(event);
    }
    assert_eq!(route(&session), Route::Workspace(WorkspaceSection::Hub));
    assert!(!unsaved(&session));
}

#[test]
fn each_action_is_its_core_event() {
    let edit = |edit| Event::VoiceStudio(VoiceStudioEvent::Edit(edit));
    let text = |s: &str| s.to_owned();
    for (action, event) in [
        (
            VoiceStudioAction::Open,
            Event::Navigate(Route::Workspace(WorkspaceSection::VoiceStudio)),
        ),
        (
            VoiceStudioAction::SelectTier {
                tier: text("latest"),
            },
            edit(StudioEdit::SelectTier(text("latest"))),
        ),
        (
            VoiceStudioAction::ApplyRecipe {
                recipe_id: text("fastest"),
            },
            edit(StudioEdit::ApplyRecipe(text("fastest"))),
        ),
        (VoiceStudioAction::Reset, edit(StudioEdit::Reset)),
        (
            VoiceStudioAction::SelectLeg { leg: text("tts") },
            Event::VoiceStudio(VoiceStudioEvent::SelectLeg(text("tts"))),
        ),
        (
            VoiceStudioAction::Pick {
                picker: StudioPicker::Vendor,
                value: text("deepgram"),
            },
            edit(StudioEdit::Pick {
                kind: PickerKind::Vendor,
                value: text("deepgram"),
            }),
        ),
        (
            VoiceStudioAction::Pick {
                picker: StudioPicker::Model,
                value: text("aura-2"),
            },
            edit(StudioEdit::Pick {
                kind: PickerKind::Model,
                value: text("aura-2"),
            }),
        ),
        (
            VoiceStudioAction::Pick {
                picker: StudioPicker::Location,
                value: text("us"),
            },
            edit(StudioEdit::Pick {
                kind: PickerKind::Location,
                value: text("us"),
            }),
        ),
        (
            VoiceStudioAction::Pick {
                picker: StudioPicker::Voice,
                value: text(OTHER_VOICE),
            },
            edit(StudioEdit::Voice(text(OTHER_VOICE))),
        ),
        (
            VoiceStudioAction::SetNumber {
                key: text("temperature"),
                value: Some(700),
            },
            edit(StudioEdit::Number {
                key: text("temperature"),
                value: Some(0.7),
            }),
        ),
        (
            VoiceStudioAction::SetNumber {
                key: text("temperature"),
                value: None,
            },
            edit(StudioEdit::Number {
                key: text("temperature"),
                value: None,
            }),
        ),
        (
            VoiceStudioAction::SetChoice {
                key: text("voiceStyle"),
                value: text("warm"),
            },
            edit(StudioEdit::Choice {
                key: text("voiceStyle"),
                value: text("warm"),
            }),
        ),
        (
            VoiceStudioAction::SetFlag {
                key: text("engineMix.preemptiveTts"),
                on: true,
            },
            edit(StudioEdit::Flag {
                key: text("engineMix.preemptiveTts"),
                on: true,
            }),
        ),
        (
            VoiceStudioAction::SetLines {
                key: text("engineMix.stt.keyterms"),
                text: text("Ada"),
            },
            edit(StudioEdit::Lines {
                key: text("engineMix.stt.keyterms"),
                text: text("Ada"),
            }),
        ),
        (
            VoiceStudioAction::Save,
            Event::VoiceStudio(VoiceStudioEvent::Save),
        ),
        (
            VoiceStudioAction::DismissNotice,
            Event::VoiceStudio(VoiceStudioEvent::DismissSaveNotice),
        ),
    ] {
        assert_eq!(action.into_ui().events(), [event]);
    }
}

/// Persona's "Open Voice Studio" lands here and reads the Studio; with an
/// edit of the persona not saved, the move is held and asked about first.
#[test]
fn persona_s_link_lands_here_and_asks_first() {
    use district_ffi::settings::persona::PersonaAction;
    let link = || UiEvent::Persona {
        action: PersonaAction::OpenVoiceStudio,
    };

    let clean = super::on_persona(false);
    assert!(
        link()
            .events()
            .iter()
            .all(|e| !leaves_unsaved(&clean.model, e))
    );
    let landed = clean
        .ui(link())
        .answer(is_read, |ticket| read_as(ticket, json(FIXTURE)));
    assert_eq!(
        route(&landed),
        Route::Workspace(WorkspaceSection::VoiceStudio)
    );
    assert_eq!(view(&landed).status, SectionStatus::Ready);

    let mut session = super::on_persona(true);
    let mut held = Held::default();
    assert_eq!(held.pass(&session.model, link().events()), []);
    assert!(held.question().is_some());
    for event in held.discard() {
        session = session.send(event);
    }
    assert_eq!(
        route(&session),
        Route::Workspace(WorkspaceSection::VoiceStudio)
    );
    assert!(matches!(
        screen_view(&session.model),
        ScreenView::VoiceStudio { .. }
    ));
}
