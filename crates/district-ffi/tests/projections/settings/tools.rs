//! The Skills section (src/settings/tools.rs).

use district_core::{Effect, Event, Route, ToolsEvent, WorkspaceSection};
use district_ffi::settings::SectionStatus;
use district_ffi::settings::tools::{ToolsAction, ToolsView};
use district_ffi::{Held, NavDestination, ScreenView, UiEvent, leaves_unsaved, screen_view};
use serde_json::{Value, json};

use super::super::{Case, Session, contracts, offered, route, server_error, signed_in, viewer};

/// This area's snapshot cases.
pub(crate) fn cases() -> Vec<Case> {
    vec![
        ("tools-loading", open(signed_in())),
        ("tools-loaded", ready(signed_in())),
        ("tools-never-chosen", read(signed_in(), Ok(never_chosen()))),
        (
            "tools-no-support-number",
            read(signed_in(), Ok(no_support_number())),
        ),
        ("tools-failed", read(signed_in(), Err(()))),
        ("tools-editing", toggled(ready(signed_in()))),
        ("tools-saving", saving(signed_in())),
        ("tools-saved", saved(signed_in(), Ok(()), true)),
        ("tools-save-failed", saved(signed_in(), Err(()), false)),
        (
            "tools-saved-not-read-back",
            saved(signed_in(), Ok(()), false),
        ),
        ("tools-research-editing", research_off(ready(signed_in()))),
        ("tools-research-saving", research_saving()),
        ("tools-research-saved", research_saved(Ok(()))),
        ("tools-research-save-failed", research_saved(Err(()))),
    ]
}

fn act(session: Session, action: ToolsAction) -> Session {
    session.ui(UiEvent::Tools { action })
}

fn open(session: Session) -> Session {
    act(session, ToolsAction::Open)
}

/// The contract's settings: six tools stored, one of them
/// (`transfer_to_creator`) an id the core has no name for; research on.
fn stored() -> Value {
    contracts::json("district-workspace-config.json")
}

/// A workspace that never stored a list: the core's defaults are on.
fn never_chosen() -> Value {
    let mut config = stored();
    config["config"]["toolConfig"] = Value::Null;
    config
}

/// No support number stored: the fallback transfer says it needs one.
fn no_support_number() -> Value {
    let mut config = stored();
    config["config"]["toolConfig"]["supportPhoneNumber"] = Value::Null;
    config
}

/// The section opened and its read answered, `Ok` with its JSON or failed.
fn read(session: Session, config: Result<Value, ()>) -> Session {
    open(session).answer(
        |e| matches!(e, Effect::LoadWorkspaceConfig { .. }),
        |ticket| Event::WorkspaceConfigLoaded {
            ticket,
            result: config
                .map(|value| contracts::decode("config", value))
                .map_err(|()| server_error()),
        },
    )
}

fn ready(session: Session) -> Session {
    read(session, Ok(stored()))
}

/// Texting the caller turned on, and taking a message turned off.
fn toggled(session: Session) -> Session {
    let on = act(
        session,
        ToolsAction::Toggle {
            id: "send_sms".to_owned(),
            enabled: true,
        },
    );
    act(
        on,
        ToolsAction::Toggle {
            id: "leave_message".to_owned(),
            enabled: false,
        },
    )
}

fn saving(session: Session) -> Session {
    act(toggled(ready(session)), ToolsAction::SaveTools)
}

/// The tools save answered `written`; when it landed, the read back after it
/// answered too, `read_back` saying whether that read worked.
fn saved(session: Session, written: Result<(), ()>, read_back: bool) -> Session {
    let landed = written.is_ok();
    let session = saving(session).answer(
        |e| matches!(e, Effect::SaveTools { .. }),
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
                config["config"]["toolConfig"]["allowedTools"] = json!([
                    "search_knowledge_base",
                    "transfer_to_agent",
                    "book_appointment",
                    "transfer_to_creator",
                    "dispatch_email",
                    "send_sms"
                ]);
                Ok(contracts::decode("config", config))
            } else {
                Err(server_error())
            },
        },
    )
}

/// Research, stored on, switched off.
fn research_off(session: Session) -> Session {
    act(session, ToolsAction::SetResearch { enabled: false })
}

fn research_saving() -> Session {
    act(research_off(ready(signed_in())), ToolsAction::SaveResearch)
}

/// The research save answered `written`, and when it landed, read back.
fn research_saved(written: Result<(), ()>) -> Session {
    let landed = written.is_ok();
    let session = research_saving().answer(
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
            result: {
                let mut config = stored();
                config["config"]["aiPersona"]["dgiEnabled"] = json!(false);
                Ok(contracts::decode("config", config))
            },
        },
    )
}

fn view(session: &Session) -> ToolsView {
    match screen_view(&session.model) {
        ScreenView::Tools { view } => view,
        other => panic!("not the Skills section: {other:?}"),
    }
}

fn enabled(view: &ToolsView) -> Vec<&str> {
    view.tools
        .iter()
        .filter(|row| row.enabled)
        .map(|row| row.id.as_str())
        .collect()
}

/// Built, the section opens from the hub's row, the pane offers the hub, and
/// it shows a switch per tool once read.
#[test]
fn built_it_opens_and_shows_its_switches() {
    let session = ready(signed_in());
    assert_eq!(route(&session), Route::Workspace(WorkspaceSection::Tools));
    assert!(offered(&session).contains(&NavDestination::Settings));
    let shown = view(&session);
    assert_eq!(shown.title, "Skills");
    assert_eq!(shown.status, SectionStatus::Ready);
    assert!(shown.editable && shown.research_enabled);
    assert!(!shown.can_save_tools && !shown.can_save_research);
    // Every tool the core names, then the stored id it does not, kept on.
    assert_eq!(shown.tools.len(), 14);
    let unknown = shown.tools.last().unwrap();
    assert_eq!(
        (unknown.id.as_str(), unknown.known, unknown.enabled),
        ("transfer_to_creator", false, true)
    );
    assert!(unknown.note.is_some());
    assert_eq!(
        enabled(&shown),
        [
            "transfer_to_agent",
            "dispatch_email",
            "book_appointment",
            "leave_message",
            "search_knowledge_base",
            "transfer_to_creator"
        ]
    );
}

/// A workspace that never chose has the core's defaults on: every tool but
/// the booking pages' four.
#[test]
fn a_workspace_that_never_chose_has_the_defaults_on() {
    let shown = view(&read(signed_in(), Ok(never_chosen())));
    assert_eq!(enabled(&shown).len(), 9);
    assert!(!enabled(&shown).contains(&"list_appointments"));
    assert!(shown.tools.iter().all(|row| row.known));
}

#[test]
fn each_action_is_its_core_event() {
    let tools = Event::Tools;
    for (action, event) in [
        (
            ToolsAction::Open,
            Event::Navigate(Route::Workspace(WorkspaceSection::Tools)),
        ),
        (
            ToolsAction::Toggle {
                id: "send_sms".to_owned(),
                enabled: true,
            },
            tools(ToolsEvent::Toggle {
                id: "send_sms".to_owned(),
                enabled: true,
            }),
        ),
        (
            ToolsAction::SetResearch { enabled: false },
            tools(ToolsEvent::SetEnrichment(false)),
        ),
        (ToolsAction::SaveTools, tools(ToolsEvent::SaveTools)),
        (ToolsAction::SaveResearch, tools(ToolsEvent::SaveEnrichment)),
        (
            ToolsAction::DismissNotices,
            tools(ToolsEvent::DismissNotices),
        ),
    ] {
        assert_eq!(UiEvent::Tools { action }.events(), [event]);
    }
}

/// Save sends the list read with the switches applied, in its order, the
/// unnamed id kept, once; while it is on its way the switches are read only.
#[test]
fn the_tools_save_sends_the_list_read_with_the_switches_applied() {
    let session = saving(signed_in());
    let sent: Vec<&Vec<String>> = session
        .pending
        .iter()
        .filter_map(|e| match e {
            Effect::SaveTools { allowed_tools, .. } => Some(allowed_tools),
            _ => None,
        })
        .collect();
    assert_eq!(
        sent,
        [&vec![
            "search_knowledge_base".to_owned(),
            "transfer_to_agent".to_owned(),
            "book_appointment".to_owned(),
            "transfer_to_creator".to_owned(),
            "dispatch_email".to_owned(),
            "send_sms".to_owned(),
        ]]
    );
    let shown = view(&session);
    assert!(shown.tools_saving && !shown.editable && !shown.can_save_tools);
    let again = act(session, ToolsAction::SaveTools);
    assert_eq!(
        again
            .pending
            .iter()
            .filter(|e| matches!(e, Effect::SaveTools { .. }))
            .count(),
        1
    );
    let done = view(&saved(signed_in(), Ok(()), true));
    assert_eq!(done.tools_notice.map(|n| n.saved), Some(true));
    assert!(!done.can_save_tools, "saved, nothing differs");
    let failed = view(&saved(signed_in(), Err(()), false));
    assert_eq!(failed.tools_notice.map(|n| n.saved), Some(false));
    assert!(failed.can_save_tools, "a failed save keeps the switches");
    assert!(matches!(
        view(&saved(signed_in(), Ok(()), false)).status,
        SectionStatus::Stale { .. }
    ));
}

/// Research is saved alone, through the persona save, carrying only itself.
#[test]
fn research_is_saved_alone() {
    let session = research_saving();
    let patches: Vec<_> = session
        .pending
        .iter()
        .filter_map(|e| match e {
            Effect::SavePersona { patch, .. } => Some(serde_json::to_value(patch).unwrap()),
            _ => None,
        })
        .collect();
    assert_eq!(patches.len(), 1);
    assert_eq!(patches[0]["dgiEnabled"], json!(false));
    assert!(
        !session
            .pending
            .iter()
            .any(|e| matches!(e, Effect::SaveTools { .. }))
    );
    let shown = view(&session);
    assert!(shown.research_saving && !shown.research_enabled);
    let done = view(&research_saved(Ok(())));
    assert_eq!(done.research_notice.map(|n| n.saved), Some(true));
    assert_eq!(done.tools_notice, None);
    let dismissed = view(&act(research_saved(Ok(())), ToolsAction::DismissNotices));
    assert_eq!(dismissed.research_notice, None);
}

/// With a switch moved, every move away is held behind "Discard your
/// changes?"; saved, nothing is.
#[test]
fn leaving_changed_skills_asks_first() {
    let leaves = [
        UiEvent::Back,
        UiEvent::Refresh,
        UiEvent::Navigate {
            destination: NavDestination::Overview,
        },
    ];
    for edited in [
        toggled(ready(signed_in())),
        research_off(ready(signed_in())),
    ] {
        for leave in &leaves {
            let mut held = Held::default();
            assert_eq!(held.pass(&edited.model, leave.clone().events()), []);
            assert!(held.question().is_some(), "{leave:?}");
        }
    }
    // Switched back, nothing differs from what is stored.
    let back = act(
        toggled(ready(signed_in())),
        ToolsAction::Toggle {
            id: "send_sms".to_owned(),
            enabled: false,
        },
    );
    let back = act(
        back,
        ToolsAction::Toggle {
            id: "leave_message".to_owned(),
            enabled: true,
        },
    );
    for clean in [back, saved(signed_in(), Ok(()), true)] {
        for leave in &leaves {
            assert!(
                !leave
                    .clone()
                    .events()
                    .iter()
                    .any(|event| leaves_unsaved(&clean.model, event)),
                "nothing is lost"
            );
        }
    }
}

/// A viewer is not offered the section, and the core keeps it from them.
#[test]
fn a_viewer_never_reaches_the_skills() {
    let session = open(viewer());
    assert_ne!(route(&session), Route::Workspace(WorkspaceSection::Tools));
    assert!(!matches!(
        screen_view(&session.model),
        ScreenView::Tools { .. }
    ));
}
