//! Scripted scenes: the core against made-up data, for the UI walk and the
//! Store screenshots. Only in a build with the `scripted` feature, which the
//! Store and sideload builds never have (CI and the release check the DLL for
//! [`MARKER`]); CI builds a separate scripted test package for the UI tests.
//!
//! A scripted build runs a scene only when the process was started with
//! [`SCENE_ARG`] and a scene's name on its command line (the UI tests launch
//! the package by its application user model ID with that argument). Without
//! it, the build runs as any other.
//!
//! The scene's answers are the core's own contract fixtures, the recorded
//! server responses its contract tests decode (copied in by build.rs from the
//! checkout Cargo.lock pins), so the screens show what the service really
//! sends. Nothing reaches the network: every effect is answered here or not at
//! all. An effect with no fixture (a write, a live connection, a call) gets no
//! answer, and its screen stays as the model leaves it.

use std::future::Future;

use district_api::{ApiError, ErrorDetail};
use district_auth::AccessClaims;
use district_core::{Effect, Event};
use district_live::{LiveUpdate, WorkspaceUpdate};
use serde::de::DeserializeOwned;
use serde_json::{Value, json};

use super::Effects;

/// The command-line argument that names the scene: `--district-scripted-scene=signed-in`.
pub(crate) const SCENE_ARG: &str = "--district-scripted-scene=";

/// What `scripts/check-scripted.py` looks for in a DLL: present in a scripted
/// build, absent from every build that ships.
pub(crate) const MARKER: &str = "district-ffi scripted scenes: not for release";

/// The scripted workspace's id.
pub(crate) const WORKSPACE_ID: &str = "ws-scripted";

/// The scripted workspace's name, the overview's heading.
pub(crate) const WORKSPACE_NAME: &str = "Example Dental";

/// A scene the core can run.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub(crate) enum Scene {
    /// Signed in, with one workspace (an agency's), every screen filled from
    /// the fixtures.
    SignedIn,
}

/// The scene the command line asks for: none without [`SCENE_ARG`], an error
/// for a scene that does not exist.
pub(crate) fn scene(args: impl IntoIterator<Item = String>) -> Result<Option<Scene>, String> {
    let Some(name) = args
        .into_iter()
        .find_map(|arg| arg.strip_prefix(SCENE_ARG).map(str::to_owned))
    else {
        return Ok(None);
    };
    match name.as_str() {
        "signed-in" => Ok(Some(Scene::SignedIn)),
        other => Err(format!("{MARKER}: no scene named {other:?}")),
    }
}

/// The effects of a scripted scene.
pub(crate) struct Scripted {
    scene: Scene,
}

impl Scripted {
    pub(crate) fn new(scene: Scene) -> Self {
        Self { scene }
    }
}

impl Effects for Scripted {
    fn run(&self, effect: Effect) -> impl Future<Output = Option<Event>> + Send {
        std::future::ready(match self.scene {
            Scene::SignedIn => signed_in(effect),
        })
    }
}

macro_rules! fixture {
    ($name:literal) => {
        include_str!(concat!(env!("OUT_DIR"), "/fixtures/", $name))
    };
}

/// The fixture `text`, with its `workspaceId` (where it has one) set to the
/// scripted workspace's, decoded. A fixture that no longer decodes is a
/// server error, so the walk sees a failed screen rather than a crash; the
/// tests below catch it first.
fn decode<T: DeserializeOwned>(text: &str) -> Result<T, ApiError> {
    let mut value: Value = serde_json::from_str(text).map_err(|_| broken())?;
    if let Some(id) = value.get_mut("workspaceId") {
        *id = json!(WORKSPACE_ID);
    }
    serde_json::from_value(value).map_err(|_| broken())
}

fn broken() -> ApiError {
    ApiError::Server {
        status: 500,
        detail: ErrorDetail::default(),
    }
}

/// The one workspace: an agency's, so every area its role allows is offered.
fn workspaces() -> Value {
    json!({
        "success": true,
        "workspaces": [{
            "id": WORKSPACE_ID,
            "name": WORKSPACE_NAME,
            "region": "us",
            "role": "agency",
            "subscriptionTier": "VoicePro"
        }],
        "total": 1,
        "limit": 100,
        "offset": 0,
        "degradedRegions": [],
        "inactiveCount": 0,
        "defaultWorkspaceId": WORKSPACE_ID
    })
}

/// Each effect of the signed-in scene, answered from the fixtures.
#[allow(clippy::too_many_lines)] // One arm per effect, by design.
fn signed_in(effect: Effect) -> Option<Event> {
    Some(match effect {
        Effect::RestoreSession { ticket } => Event::SessionRestored {
            ticket,
            result: Ok(AccessClaims {
                user_id: "user-scripted".to_owned(),
                device_id: "device-scripted".to_owned(),
                expires_at_secs: 4_000_000_000,
            }),
        },
        Effect::LoadWorkspaces { ticket } => Event::WorkspacesLoaded {
            ticket,
            remembered: None,
            result: serde_json::from_value(workspaces()).map_err(|_| broken()),
        },
        Effect::LoadOverview { ticket, .. } => Event::OverviewLoaded {
            ticket,
            result: decode(fixture!("district-overview.json")),
        },
        Effect::LoadSetupStatus { ticket, .. } => Event::SetupStatusLoaded {
            ticket,
            result: Ok(false),
        },
        Effect::LoadDevices { ticket } => Event::DevicesLoaded {
            ticket,
            result: decode(fixture!("district-devices.json")),
        },
        Effect::LoadConversations { ticket, .. } => Event::ConversationsLoaded {
            ticket,
            result: decode(fixture!("district-conversations.json")),
        },
        Effect::LoadUnreadCount { ticket, .. } => Event::UnreadCountLoaded {
            ticket,
            result: decode(fixture!("district-messages-unread-count.json")),
        },
        Effect::LoadDraftKeys { ticket, .. } => Event::DraftKeysLoaded {
            ticket,
            result: decode(fixture!("district-drafts-list.json")),
        },
        Effect::LoadTimeline { ticket, .. } => Event::TimelineLoaded {
            ticket,
            result: decode(fixture!("district-timeline.json")),
        },
        Effect::LoadDraft { ticket, .. } => Event::DraftLoaded {
            ticket,
            result: decode(fixture!("district-draft-null.json")),
        },
        Effect::MarkRead { ticket, .. } => Event::MarkedRead {
            ticket,
            result: decode(fixture!("district-message-mark-read.json")),
        },
        Effect::LoadCalls { ticket, .. } => Event::CallsLoaded {
            ticket,
            result: decode(fixture!("district-calls.json")),
        },
        Effect::LoadCall { ticket, .. } => Event::CallLoaded {
            ticket,
            result: decode(fixture!("district-call-detail.json")),
        },
        Effect::LoadTranscript { ticket, .. } => Event::TranscriptLoaded {
            ticket,
            result: decode(fixture!("district-call-transcript.json")),
        },
        Effect::LoadContacts { ticket, .. } => Event::ContactsLoaded {
            ticket,
            result: decode(fixture!("district-contacts.json")),
        },
        Effect::LoadContact { ticket, .. } => Event::ContactLoaded {
            ticket,
            result: decode(fixture!("district-contact-detail.json")),
        },
        // The core has no fixture for the blocked list: an empty one, the
        // usual answer.
        Effect::LoadBlocked { ticket, .. } => Event::BlockedLoaded {
            ticket,
            result: serde_json::from_value(json!({ "success": true, "blocked": [] }))
                .map_err(|_| broken()),
        },
        Effect::LoadAnalytics { ticket, .. } => Event::AnalyticsLoaded {
            ticket,
            result: decode(fixture!("district-analytics.json")),
        },
        Effect::LoadUsage { ticket, .. } => Event::UsageLoaded {
            ticket,
            result: decode(fixture!("district-usage.json")),
        },
        Effect::LoadUsageHistory { ticket, .. } => Event::UsageHistoryLoaded {
            ticket,
            result: decode(fixture!("district-usage-history.json")),
        },
        Effect::SearchNumbers { ticket, .. } => Event::NumbersFound {
            ticket,
            result: decode(fixture!("district-numbers-search.json")),
        },
        Effect::LoadOwnedNumbers { ticket, .. } => Event::OwnedNumbersLoaded {
            ticket,
            result: decode(fixture!("district-provider-numbers.json")),
        },
        Effect::LoadWorkspaceBilling { ticket, .. } => Event::WorkspaceBillingLoaded {
            ticket,
            result: decode(fixture!("district-workspace-billing.json")),
        },
        Effect::LoadAccountBilling { ticket } => Event::AccountBillingLoaded {
            ticket,
            result: decode(fixture!("district-billing.json")),
        },
        Effect::LoadWorkflows { ticket, .. } => Event::WorkflowsLoaded {
            ticket,
            result: decode(fixture!("district-workflows.json")),
        },
        Effect::LoadWorkflowRuns { ticket, .. } => Event::WorkflowRunsLoaded {
            ticket,
            result: decode(fixture!("district-workflow-runs.json")),
        },
        Effect::LoadCampaign { ticket, .. } => Event::CampaignLoaded {
            ticket,
            result: decode(fixture!("district-campaign-status.json")),
        },
        Effect::LoadSchedulingStatus { ticket, .. } => Event::SchedulingStatusLoaded {
            ticket,
            result: decode(fixture!("district-scheduling-status-ready.json")),
        },
        Effect::LoadDeskSettings { ticket, .. } => Event::DeskSettingsLoaded {
            ticket,
            result: decode(fixture!("district-desk-settings.json")),
        },
        Effect::LoadDeskTickets { ticket, .. } => Event::DeskTicketsLoaded {
            ticket,
            result: decode(fixture!("district-desk-tickets.json")),
        },
        Effect::LoadDeskTicket { ticket, .. } => Event::DeskTicketLoaded {
            ticket,
            result: decode(fixture!("district-desk-ticket.json")),
        },
        Effect::LoadSupportRequests { ticket, .. } => Event::SupportRequestsLoaded {
            ticket,
            result: decode(fixture!("district-support-requests.json")),
        },
        Effect::LoadSupportRequest { ticket, .. } => Event::SupportRequestLoaded {
            ticket,
            result: decode(fixture!("district-support-request.json")),
        },
        Effect::LoadMeetings { ticket, .. } => Event::MeetingsLoaded {
            ticket,
            result: decode(fixture!("district-meetings.json")),
        },
        Effect::LoadMeeting { ticket, .. } => Event::MeetingLoaded {
            ticket,
            result: decode(fixture!("district-meeting-detail.json")),
        },
        Effect::LoadWorkspaceConfig { ticket, .. } => Event::WorkspaceConfigLoaded {
            ticket,
            result: decode(fixture!("district-workspace-config.json")),
        },
        Effect::LoadPersonaOptions { ticket, .. } => Event::PersonaOptionsLoaded {
            ticket,
            result: decode(fixture!("district-persona-options.json")),
        },
        Effect::LoadVoiceStudio { ticket, .. } => Event::VoiceStudioLoaded {
            ticket,
            result: decode(fixture!("district-voice-studio.json")),
        },
        Effect::LoadKnowledge { ticket, .. } => Event::KnowledgeLoaded {
            ticket,
            result: decode(fixture!("district-knowledge.json")),
        },
        Effect::LoadKnowledgeMode { ticket, .. } => Event::KnowledgeModeLoaded {
            ticket,
            result: decode(fixture!("district-knowledge-mode.json")),
        },
        Effect::LoadMessaging { ticket, .. } => Event::MessagingLoaded {
            ticket,
            result: decode(fixture!("district-messaging.json")),
        },
        Effect::LoadMembers { ticket, .. } => Event::MembersLoaded {
            ticket,
            result: decode(fixture!("district-members.json")),
        },
        // The core has no fixture for call handling or availability: the
        // smallest answers of the shapes it decodes (district-model's
        // CallHandlingResponse and AvailabilityResponse), a workspace that
        // never chose (the receptionist answers, a 20 second ring) and a
        // member who is rung. A save is answered as stored; the scene keeps
        // nothing, so the next read is the first answer again.
        Effect::LoadCallHandling { ticket, .. } => Event::CallHandlingLoaded {
            ticket,
            result: call_handling(None, None),
        },
        Effect::SaveCallHandling { ticket, patch, .. } => Event::CallHandlingLoaded {
            ticket,
            result: call_handling(patch.call_handling, patch.app_ring_seconds),
        },
        Effect::LoadAvailability { ticket, .. } => Event::AvailabilityLoaded {
            ticket,
            result: availability(true),
        },
        Effect::SetAvailability {
            ticket, available, ..
        } => Event::AvailabilityLoaded {
            ticket,
            result: availability(available),
        },
        // The transfer directory's and the routing rules' saves land; the
        // read that follows is answered with the settings fixture above.
        Effect::SaveDirectory { ticket, .. } | Effect::SaveRoutingRules { ticket, .. } => {
            Event::SettingsWritten {
                ticket,
                result: Ok(()),
            }
        }
        Effect::ReadRingSetting { ticket } => Event::RingSettingRead {
            ticket,
            ring_here: true,
        },
        // A call placed from the dialler is accepted by the service, so it
        // has an id and its live transcript is asked for. Its media is never
        // connected: the call stays "Connecting.", with no audio anywhere.
        Effect::Dial { ticket, .. } => Event::Dialled {
            ticket,
            result: decode(fixture!("district-dial.json")),
        },
        // The socket answers the call's transcript with the recorded
        // snapshot, and a reply still being heard.
        Effect::WatchTranscript {
            transcript: Some(watch),
            ..
        } => transcript_snapshot(&watch.call_id)?,
        // Everything else: writes, the browser, live updates, presence, calls,
        // notifications and the settings sections the core has no fixture
        // for. Unanswered, as with no network.
        _ => return None,
    })
}

/// Call handling as stored: `mode` and `ring` where a save names them, else
/// what a workspace that never chose has.
fn call_handling(
    mode: Option<district_model::CallHandlingMode>,
    ring: Option<i64>,
) -> Result<district_model::CallHandlingResponse, ApiError> {
    serde_json::from_value(json!({
        "success": true,
        "callHandling": mode.map_or("ai_first", district_model::CallHandlingMode::as_str),
        "appRingSeconds": ring.unwrap_or(district_model::DEFAULT_APP_RING_SECONDS),
    }))
    .map_err(|_| broken())
}

/// The member's availability as stored: `available`, with nothing against it.
fn availability(available: bool) -> Result<district_model::AvailabilityResponse, ApiError> {
    serde_json::from_value(json!({
        "success": true,
        "availableForCalls": available,
        "reason": null,
    }))
    .map_err(|_| broken())
}

/// The recorded transcript snapshot, for `call_id` in the scripted
/// workspace, with the assistant's reply to it still being heard.
fn transcript_snapshot(call_id: &str) -> Option<Event> {
    let mut envelope: Value =
        serde_json::from_str(fixture!("telemetry-event-transcript-snapshot.json")).ok()?;
    envelope["workspaceId"] = json!(WORKSPACE_ID);
    envelope["callId"] = json!(call_id);
    let data = &mut envelope["data"];
    data["callId"] = json!(call_id);
    let mut reply = data["segments"][0].clone();
    reply["segmentId"] = json!("item_c3");
    reply["index"] = json!(2);
    reply["seq"] = json!(3);
    reply["text"] = json!("Of course. Would morning or afternoon suit you");
    reply["final"] = json!(false);
    reply["endedAt"] = json!(null);
    data["segments"].as_array_mut()?.push(reply);
    data["lastSeq"] = json!(3);
    Some(Event::Live(WorkspaceUpdate {
        workspace_id: WORKSPACE_ID.to_owned(),
        update: LiveUpdate::Event(serde_json::from_value(envelope).ok()?),
    }))
}

#[cfg(test)]
mod tests {
    use district_core::{CoreConfig, Model, Route, SessionState};

    use super::*;
    use crate::screen::{ScreenView, screen_view};
    use crate::shell::shell_view;

    #[test]
    fn the_scene_comes_from_the_command_line() {
        let args = |list: &[&str]| list.iter().map(|arg| (*arg).to_owned()).collect::<Vec<_>>();
        assert_eq!(scene(args(&["DistrictAI.exe"])), Ok(None));
        assert_eq!(
            scene(args(&[
                "DistrictAI.exe",
                "--district-scripted-scene=signed-in"
            ])),
            Ok(Some(Scene::SignedIn))
        );
        assert!(scene(args(&["DistrictAI.exe", "--district-scripted-scene=nope"])).is_err());
    }

    /// Runs `model` as the actor does, answering every effect the scene
    /// answers, until none is left.
    fn settle(model: &mut Model, mut pending: Vec<Effect>) {
        for _ in 0..1000 {
            let Some(effect) = pending.pop() else {
                return;
            };
            if let Some(event) = signed_in(effect) {
                // A fixture that no longer decodes is answered as a failure.
                assert!(
                    !format!("{event:?}").contains("result: Err("),
                    "a fixture did not decode: {event:?}"
                );
                pending.extend(model.update(event));
            }
        }
        panic!("the scene never settles");
    }

    fn started() -> Model {
        started_with(false)
    }

    /// The scene in a build that can carry calls, or not.
    fn started_with(calls_available: bool) -> Model {
        let (mut model, first) = Model::new(CoreConfig {
            web_base_url: "https://www.distronode.com".to_owned(),
            app_version: "0.1.0".to_owned(),
            calls_available,
        });
        settle(&mut model, first);
        model
    }

    #[test]
    fn the_scene_signs_in_to_its_workspace() {
        let model = started();
        assert!(matches!(model.session(), SessionState::SignedIn(_)));
        match screen_view(&model) {
            ScreenView::Overview { view } => {
                assert!(
                    serde_json::to_string(&view)
                        .unwrap()
                        .contains(WORKSPACE_NAME),
                    "{view:?}"
                );
            }
            other => panic!("not the overview: {other:?}"),
        }
    }

    /// The screen after `event`, with every answer in.
    fn after(model: &mut Model, event: Event) -> ScreenView {
        let pending = model.update(event);
        settle(model, pending);
        screen_view(model)
    }

    /// Whether a projected screen is still loading or shows a failure.
    fn unsettled(screen: &ScreenView) -> Option<String> {
        let json = serde_json::to_value(screen).unwrap();
        let mut found = None;
        walk(&json, &mut |key, value| {
            // A load state is "Loading", "Ready" or {"Failed": ...}; a
            // failure anywhere (a refresh's, a search's) is not null.
            let bad = match key {
                "status" => value == &json!("Loading") || value.get("Failed").is_some(),
                "failure" | "refresh_failure" => !value.is_null(),
                _ => false,
            };
            if bad && found.is_none() {
                found = Some(format!("{key} = {value}"));
            }
        });
        found
    }

    fn walk(value: &Value, visit: &mut impl FnMut(&str, &Value)) {
        match value {
            Value::Object(fields) => {
                for (key, field) in fields {
                    visit(key, field);
                    walk(field, visit);
                }
            }
            Value::Array(items) => items.iter().for_each(|item| walk(item, visit)),
            _ => {}
        }
    }

    /// Every entry the pane offers opens a screen that has finished loading
    /// and has not failed: what the UI walk checks, here first.
    #[test]
    fn every_offered_screen_fills_in() {
        let mut model = started();
        let offered: Vec<_> = shell_view(&model, false)
            .nav
            .groups
            .iter()
            .flat_map(|group| group.entries.iter().map(|entry| entry.destination))
            .collect();
        assert!(offered.len() >= 5, "{offered:?}");
        for destination in offered {
            let mut pending = Vec::new();
            for event in (crate::events::UiEvent::Navigate { destination }).events() {
                pending.extend(model.update(event));
            }
            settle(&mut model, pending);
            let screen = screen_view(&model);
            assert!(
                !matches!(screen, ScreenView::Unavailable { .. }),
                "{destination:?} is offered but unavailable"
            );
            assert_eq!(unsettled(&screen), None, "{destination:?}: {screen:?}");
        }
    }

    /// A call placed in the scene shows its live transcript: the recorded
    /// lines, and the reply still being heard.
    #[test]
    fn a_placed_call_shows_its_live_transcript() {
        let mut model = started_with(true);
        let mut pending = Vec::new();
        for event in (crate::events::UiEvent::CallNumber {
            number: "+12125550142".to_owned(),
        })
        .events()
        {
            pending.extend(model.update(event));
        }
        settle(&mut model, pending);
        let shell = serde_json::to_value(shell_view(&model, false)).unwrap();
        let transcript = &shell["call"]["transcript"];
        assert_eq!(transcript["phase"], "Live", "{shell}");
        let lines = transcript["lines"].as_array().unwrap();
        assert_eq!(lines.len(), 3);
        assert_eq!(lines[1]["speaker"], "Caller");
        assert_eq!(lines[2]["is_final"], false);
    }

    /// Every list screen of 1.0 and 2.0, built or not: each fixture its
    /// effects are answered with decodes (settle checks), so an area that is
    /// built later finds its answers working.
    #[test]
    fn every_route_is_answered_with_fixtures_that_decode() {
        use district_core::WorkspaceSection as S;
        let mut model = started();
        let routes = [
            Route::Overview,
            Route::Inbox,
            Route::Calls,
            Route::Contacts,
            Route::Account,
            Route::Devices,
            Route::BlockedContacts,
            Route::Hq,
            Route::Analytics,
            Route::Marketplace,
            Route::Billing,
            Route::Workflows,
            Route::Scheduling,
            Route::Desk,
            Route::DeskSettings,
            Route::Support,
            Route::Rooms,
            Route::Workspace(S::Hub),
            Route::Workspace(S::Persona),
            Route::Workspace(S::VoiceStudio),
            Route::Workspace(S::CallHandling),
            Route::Workspace(S::Routing),
            Route::Workspace(S::Directory),
            Route::Workspace(S::Tools),
            Route::Workspace(S::Knowledge),
            Route::Workspace(S::Messaging),
            Route::Workspace(S::Members),
            Route::Workspace(S::Numbers),
        ];
        for route in routes.clone() {
            let _ = after(&mut model, Event::Navigate(route.clone()));
            // Some routes lead on (the numbers section to the marketplace).
            assert!(
                matches!(model.session(), SessionState::SignedIn(_)),
                "signed out at {route:?}"
            );
        }
        // And every built screen among them (1.0's, and each area whose
        // packet has set BUILT) is filled in, neither loading nor failed.
        for route in routes.into_iter().filter(crate::nav::built) {
            let screen = after(&mut model, Event::Navigate(route.clone()));
            assert_eq!(unsettled(&screen), None, "{route:?}: {screen:?}");
        }
    }

    /// Each settings section the walk opens from the hub fills in, and each
    /// of their saves is answered: the screen is left saved, not saving.
    #[test]
    fn each_call_handling_section_opens_from_the_hub_and_saves() {
        use crate::events::UiEvent;
        use crate::settings::call_handling::{CallHandlingAction, CallHandlingChoice};
        use crate::settings::directory::DirectoryAction;
        use crate::settings::routing::RoutingAction;
        use crate::settings::{SettingsAction, SettingsSection};

        let mut model = started();
        let run = |model: &mut Model, action: UiEvent| {
            let mut pending = Vec::new();
            for event in action.events() {
                pending.extend(model.update(event));
            }
            settle(model, pending);
            screen_view(model)
        };
        for (section, title) in [
            (SettingsSection::CallHandling, "Call handling"),
            (SettingsSection::Routing, "Call routing rules"),
            (SettingsSection::Directory, "Transfer directory"),
        ] {
            let _ = run(
                &mut model,
                UiEvent::Settings {
                    action: SettingsAction::Open,
                },
            );
            let screen = run(
                &mut model,
                UiEvent::Settings {
                    action: SettingsAction::OpenSection { section },
                },
            );
            assert_eq!(unsettled(&screen), None, "{section:?}: {screen:?}");
            let json = serde_json::to_value(&screen).unwrap();
            let page = json.as_object().unwrap().values().next().unwrap();
            assert_eq!(page["view"]["title"], title);
        }

        let call_handling = |action| UiEvent::CallHandling { action };
        let _ = run(&mut model, call_handling(CallHandlingAction::Open));
        let _ = run(
            &mut model,
            call_handling(CallHandlingAction::SelectMode {
                mode: CallHandlingChoice::AppFirst,
            }),
        );
        let _ = run(
            &mut model,
            call_handling(CallHandlingAction::SetRingSeconds { seconds: 12 }),
        );
        let ScreenView::CallHandling { view } =
            run(&mut model, call_handling(CallHandlingAction::Save))
        else {
            panic!("call handling shows");
        };
        assert!(view.save.saved.is_some(), "{view:?}");
        assert_eq!(view.ring_seconds, 12);
        assert!(view.modes.iter().any(|m| m.selected && m.mode == CallHandlingChoice::AppFirst));
        let ScreenView::CallHandling { view } = run(
            &mut model,
            call_handling(CallHandlingAction::SetAvailable { available: false }),
        ) else {
            panic!("call handling shows");
        };
        assert!(!view.availability.available);
        assert!(view.availability.save.saved.is_some());

        let routing = |action| UiEvent::Routing { action };
        let _ = run(&mut model, routing(RoutingAction::Open));
        let _ = run(&mut model, routing(RoutingAction::Add));
        let _ = run(&mut model, routing(RoutingAction::Save));
        let ScreenView::Routing { view } = run(&mut model, routing(RoutingAction::ConfirmSave))
        else {
            panic!("the routing rules show");
        };
        assert!(view.save.saved.is_some(), "{view:?}");

        let directory = |action| UiEvent::Directory { action };
        let _ = run(&mut model, directory(DirectoryAction::Open));
        let _ = run(&mut model, directory(DirectoryAction::Remove { index: 0 }));
        let _ = run(&mut model, directory(DirectoryAction::Save));
        let ScreenView::Directory { view } =
            run(&mut model, directory(DirectoryAction::ConfirmSave))
        else {
            panic!("the directory shows");
        };
        assert!(view.save.saved.is_some(), "{view:?}");
    }

    /// The walk's one step past the pane: the inbox's first conversation
    /// opens from the fixtures with its reply box, settled.
    #[test]
    fn the_first_conversation_opens_with_its_reply_box() {
        let mut model = started();
        let ScreenView::Inbox { view } = after(&mut model, Event::Navigate(Route::Inbox)) else {
            panic!("the inbox opens");
        };
        let first = view
            .threads
            .first()
            .expect("the fixtures hold a conversation");
        let screen = after(
            &mut model,
            Event::Navigate(Route::Thread {
                thread_key: first.thread_key.clone(),
            }),
        );
        let ScreenView::Thread { view } = &screen else {
            panic!("the conversation opens: {screen:?}");
        };
        assert!(view.composer.is_some(), "no reply box: {view:?}");
        assert_eq!(unsettled(&screen), None, "{screen:?}");
    }
}
