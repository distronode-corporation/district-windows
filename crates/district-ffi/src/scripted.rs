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

/// The scheduler sign-in a calendar connection starts with: never opened.
const SIGN_IN_LINK: &str = "https://scheduler.example.com/v1/auth/sso?scripted";

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

/// A write's answer `text`, whose answer holds nothing worth keeping: landed
/// when it says `success`, else a server error.
fn written(text: &str) -> Result<(), ApiError> {
    let answer: Value = decode(text)?;
    if answer["success"] == json!(true) {
        Ok(())
    } else {
        Err(broken())
    }
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
        // The booking scheduler's administration: the reads of the sections
        // built so far, and the bookings' writes.
        Effect::SchedulingAdmin {
            ticket, request, ..
        } => Event::SchedulingAdminAnswered {
            ticket,
            result: scheduling_admin(&request)?,
        },
        // Connecting a calendar: the scheduler's single-use sign-in arrives,
        // and the core asks for it to be opened in the browser, which goes
        // unanswered below, so no browser opens and the section waits for it.
        Effect::SchedulingSignIn { ticket, .. } => Event::SchedulingSignInReady {
            ticket,
            result: Ok(district_model::SchedulingSecret::new(SIGN_IN_LINK)),
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
        // The writes of the Skills and Knowledge sections land, as the
        // service answers them: each settings write is followed by the read
        // the core asks for, answered above.
        Effect::SaveTools { ticket, .. } => Event::SettingsWritten {
            ticket,
            result: written(fixture!("district-tools-patch.json")),
        },
        // A persona save (Voice Studio's too) is accepted. The scene keeps
        // nothing, so the read after it is the recorded one again, and Voice
        // Studio says the workspace does not hold what was sent, as the core
        // does when the service stores something else.
        Effect::SavePersona { ticket, .. } => Event::SettingsWritten {
            ticket,
            result: written(fixture!("district-persona-patch.json")),
        },
        Effect::AddKnowledgeDocument { ticket, .. } => Event::SettingsWritten {
            ticket,
            result: written(fixture!("district-knowledge-create.json")),
        },
        Effect::DeleteKnowledgeDocument { ticket, .. } => Event::SettingsWritten {
            ticket,
            result: written(fixture!("district-knowledge-delete.json")),
        },
        Effect::SetKnowledgeMode { ticket, .. } => Event::KnowledgeModeLoaded {
            ticket,
            result: decode(fixture!("district-knowledge-mode-patch.json")),
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
        // Never set on this computer: "Sign in every time", the default.
        Effect::ReadPurchaseSetting { ticket } => Event::PurchaseSettingRead {
            ticket,
            setting: None,
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

/// The scheduling administration's answer to `request`, from its fixture.
/// The bookings, and the date overrides, are moved to the days after the
/// scene runs, so that they are still to come and can be acted on whenever
/// the walk runs; a team's write is answered with that team as the teams
/// fixture lists it, so that its dialog stays about it; and a host's upcoming
/// bookings are none, the fixture's one being in September 2026 and so past,
/// which lets the walk archive their account. `None` for a request of a
/// section not built yet, which goes unanswered.
fn scheduling_admin(
    request: &district_model::SchedulingAdminRequest,
) -> Option<Result<district_model::SchedulingAdminReply, ApiError>> {
    use district_model::SchedulingAdminRequest as R;
    let text = match request {
        R::Me => fixture!("district-scheduling-me.json"),
        R::EventTypes => fixture!("district-scheduling-event-types.json"),
        R::CalendarStatus => fixture!("district-scheduling-calendar-status.json"),
        R::ZoomStatus => fixture!("district-scheduling-zoom-status.json"),
        R::Calendars { .. } => fixture!("district-scheduling-calendars.json"),
        R::ConnectCaldav { .. } => fixture!("district-scheduling-caldav-connect.json"),
        R::Rules { .. } => fixture!("district-scheduling-rules.json"),
        R::Bookings(_) => fixture!("district-scheduling-bookings.json"),
        R::BookingAnswers { .. } => fixture!("district-scheduling-booking-answers.json"),
        R::Slots { .. } => fixture!("district-scheduling-slots.json"),
        R::Users { .. } => fixture!("district-scheduling-users.json"),
        R::Teams => fixture!("district-scheduling-teams.json"),
        R::Team { .. }
        | R::CreateTeam { .. }
        | R::PatchTeam { .. }
        | R::AddTeamMember { .. }
        | R::PatchTeamMember { .. } => fixture!("district-scheduling-team.json"),
        R::UpcomingBookings { .. } => fixture!("district-scheduling-user-upcoming.json"),
        R::ArchiveUser { .. } => fixture!("district-scheduling-user-archive.json"),
        R::CancelBooking { .. } | R::RescheduleBooking { .. } | R::ReassignBooking { .. } => {
            fixture!("district-scheduling-booking.json")
        }
        R::EventType { .. } | R::CreateEventType { .. } | R::PatchEventType { .. } => {
            fixture!("district-scheduling-event-type.json")
        }
        R::Hosts { .. } | R::SetHosts { .. } => fixture!("district-scheduling-hosts.json"),
        R::Questions { .. } => fixture!("district-scheduling-questions.json"),
        R::CreateQuestion { .. } | R::PatchQuestion { .. } => {
            fixture!("district-scheduling-question.json")
        }
        R::SendTestEmail { .. } => fixture!("district-scheduling-test-email.json"),
        R::CreateRule { .. } | R::PatchRule { .. } => fixture!("district-scheduling-rule.json"),
        R::Overrides => fixture!("district-scheduling-overrides.json"),
        R::CreateOverride(_) => fixture!("district-scheduling-override-created.json"),
        R::DeleteEventType { .. }
        | R::DeleteQuestion { .. }
        | R::DeleteRule { .. }
        | R::DeleteOverride { .. }
        | R::DeleteOverrideGroup { .. }
        | R::SetCalendars { .. }
        | R::SetDestination { .. }
        | R::DeleteConnection { .. }
        | R::DeleteTeam { .. }
        | R::RemoveTeamMember { .. } => fixture!("district-scheduling-ok.json"),
        _ => return None,
    };
    let data = serde_json::from_str::<Value>(text)
        .ok()
        .map(|mut answer| answer["data"].take())
        .map(|mut data| {
            if data.get("start_at").is_some() {
                soon(&mut data, 2);
            } else if data.get("date").is_some() {
                soon_day(&mut data, 2);
            } else if let (R::Bookings(_) | R::Overrides, Some(items)) =
                (request, data.get_mut("items").and_then(Value::as_array_mut))
            {
                for (day, item) in (2..).zip(items.iter_mut()) {
                    if matches!(request, R::Overrides) {
                        soon_day(item, day);
                    } else {
                        soon(item, day);
                    }
                }
            } else if let R::UpcomingBookings { .. } = request {
                data["items"] = json!([]);
            }
            listed_team(request).unwrap_or(data)
        });
    Some(
        data.and_then(|data| request.decode(data).ok())
            .map(district_model::SchedulingAdminReply::Done)
            .ok_or_else(broken),
    )
}

/// The team `request` reads or writes, as the teams fixture lists it.
fn listed_team(request: &district_model::SchedulingAdminRequest) -> Option<Value> {
    use district_model::SchedulingAdminRequest as R;
    let (R::Team { id }
    | R::PatchTeam { id, .. }
    | R::AddTeamMember { id, .. }
    | R::PatchTeamMember { id, .. }) = request
    else {
        return None;
    };
    let teams = serde_json::from_str::<Value>(fixture!("district-scheduling-teams.json")).ok()?;
    teams["data"]["items"]
        .as_array()?
        .iter()
        .find(|team| team["id"].as_str() == Some(id.as_str()))
        .cloned()
}

/// `booking` moved to 13:00 UTC `days` days from now, for half an hour.
fn soon(booking: &mut Value, days: u64) {
    use std::time::{Duration, SystemTime, UNIX_EPOCH};
    let today = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map_or(0, |since| since.as_secs() / 86_400);
    let start = UNIX_EPOCH + Duration::from_secs((today + days) * 86_400 + 13 * 3600);
    booking["start_at"] = json!(crate::calls_live::iso_utc(start));
    booking["end_at"] = json!(crate::calls_live::iso_utc(
        start + Duration::from_secs(1800)
    ));
}

/// The override `item` moved to the day `days` days from today, UTC.
fn soon_day(item: &mut Value, days: u64) {
    use std::time::{Duration, SystemTime, UNIX_EPOCH};
    let today = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map_or(0, |since| since.as_secs() / 86_400);
    let day = crate::calls_live::iso_utc(UNIX_EPOCH + Duration::from_secs((today + days) * 86_400));
    item["date"] = json!(day[..10]);
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
            in_app_purchases: true,
            locale: district_core::Locale::En,
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
        let offered: Vec<_> = shell_view(&model)
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
        let shell = serde_json::to_value(shell_view(&model)).unwrap();
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
            Route::SchedulingAdmin(district_core::SchedulingSection::Overview),
            Route::SchedulingAdmin(district_core::SchedulingSection::EventTypes),
            Route::SchedulingAdmin(district_core::SchedulingSection::Hours),
            Route::SchedulingAdmin(district_core::SchedulingSection::Bookings),
            Route::SchedulingAdmin(district_core::SchedulingSection::Calendar),
            Route::SchedulingAdmin(district_core::SchedulingSection::Team),
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

    /// The booking scheduler's sections open from the booking pages, as the
    /// walk opens them, and fill in; a booking opens, and each of the
    /// bookings' writes is answered: the dialog closes and says so.
    #[test]
    fn the_scheduling_sections_fill_in_and_the_bookings_writes_land() {
        use crate::events::UiEvent;
        use crate::scheduling_admin::bookings::{BookingAction, BookingsAction};
        use crate::scheduling_admin::{
            SchedulingAdminAction as A, SchedulingAdminPage as P, SchedulingAdminSection,
        };

        let mut model = started();
        let mut run = |action: A| {
            let mut pending = Vec::new();
            for event in (UiEvent::SchedulingAdmin { action }).events() {
                pending.extend(model.update(event));
            }
            settle(&mut model, pending);
            let screen = screen_view(&model);
            assert_eq!(unsettled(&screen), None, "{screen:?}");
            let ScreenView::SchedulingAdmin { view } = screen else {
                panic!("not the scheduling administration: {screen:?}");
            };
            view.page
        };
        let P::Overview { view } = run(A::Open {
            section: SchedulingAdminSection::Overview,
        }) else {
            panic!("not the overview");
        };
        assert!(view.desk.iter().all(|row| row.failure.is_none()));
        assert_eq!(view.coming_up.len(), 2);

        let bookings = |action| A::Bookings { action };
        let P::Bookings { view } = run(A::Open {
            section: SchedulingAdminSection::Bookings,
        }) else {
            panic!("not the bookings");
        };
        let first = view.rows[0].clone();
        assert!(
            first.can_cancel && first.can_reschedule && first.can_reassign,
            "{first:?}"
        );

        let P::Bookings { view } = run(bookings(BookingsAction::OpenCancel {
            booking_id: first.id.clone(),
        })) else {
            panic!("not the bookings");
        };
        assert!(view.cancel.is_some());
        let P::Bookings { view } = run(bookings(BookingsAction::Confirm)) else {
            panic!("not the bookings");
        };
        assert_eq!(view.cancel, None);
        assert_eq!(view.notice.as_deref(), Some("Booking cancelled"));

        let P::Bookings { view } = run(bookings(BookingsAction::OpenReschedule {
            booking_id: first.id.clone(),
        })) else {
            panic!("not the bookings");
        };
        let dialog = view.reschedule.expect("the move dialog");
        assert_eq!(dialog.slots.len(), 2);
        let _ = run(bookings(BookingsAction::PickSlot {
            start: dialog.slots[1].value.clone(),
        }));
        let P::Bookings { view } = run(bookings(BookingsAction::Confirm)) else {
            panic!("not the bookings");
        };
        assert!(
            view.notice
                .is_some_and(|notice| notice.starts_with("Booking moved"))
        );

        let P::Bookings { view } = run(bookings(BookingsAction::OpenReassign {
            booking_id: first.id.clone(),
        })) else {
            panic!("not the bookings");
        };
        // The scene's one other user is archived: nobody else can take it.
        assert_eq!(view.reassign.expect("the host dialog").hosts, []);
        let _ = run(bookings(BookingsAction::CloseDialog));

        let P::Booking { view } = run(A::OpenBooking {
            booking_id: first.id.clone(),
        }) else {
            panic!("not the booking");
        };
        assert_eq!(view.answers.len(), 2);
        assert!(view.offers_cancel);
        let _ = run(A::Booking {
            action: BookingAction::OpenCancel,
        });
        let P::Booking { view } = run(A::Booking {
            action: BookingAction::Confirm,
        }) else {
            panic!("not the booking");
        };
        assert_eq!(view.notice.as_deref(), Some("Booking cancelled"));
        assert!(!view.offers_cancel);
    }

    /// The event types open from the booking pages and fill in; a create
    /// lands, one event type opens with its hosts and questions, and each of
    /// its writes is answered: the editor and the hosts close on their save,
    /// the state actions and the questions say what they came to, and a
    /// delete goes back to the list.
    #[test]
    fn the_event_types_fill_in_and_their_writes_land() {
        use crate::events::UiEvent;
        use crate::scheduling_admin::event_types::{EventTypeAction as E, EventTypesAction};
        use crate::scheduling_admin::{
            SchedulingAdminAction as A, SchedulingAdminPage as P, SchedulingAdminSection,
        };

        let mut model = started();
        let mut run = |action: A| {
            let mut pending = Vec::new();
            for event in (UiEvent::SchedulingAdmin { action }).events() {
                pending.extend(model.update(event));
            }
            settle(&mut model, pending);
            let screen = screen_view(&model);
            assert_eq!(unsettled(&screen), None, "{screen:?}");
            let ScreenView::SchedulingAdmin { view } = screen else {
                panic!("not the scheduling administration: {screen:?}");
            };
            view.page
        };
        let P::EventTypes { view } = run(A::Open {
            section: SchedulingAdminSection::EventTypes,
        }) else {
            panic!("not the event types");
        };
        assert_eq!(view.rows.len(), 2);
        assert!(view.offers_create);
        let list = |action| A::EventTypes { action };
        let P::EventTypes { view } = run(list(EventTypesAction::OpenCreate)) else {
            panic!("not the event types");
        };
        let mut form = view.create.expect("the create form").form;
        form.name = "Intro call".to_owned();
        let _ = run(list(EventTypesAction::EditForm { form }));
        let P::EventTypes { view } = run(list(EventTypesAction::Save)) else {
            panic!("not the event types");
        };
        assert_eq!(view.create, None);

        let detail = |action| A::EventType { action };
        let P::EventType { view } = run(A::OpenEventType {
            slug: view.rows[0].slug.clone(),
        }) else {
            panic!("not an event type");
        };
        assert!(view.offers_writes);
        assert_eq!(view.hosts.len(), 2);
        assert_eq!(view.questions.len(), 2);
        assert_eq!(view.hosts_status, crate::views::LoadStatus::Ready);

        let _ = run(detail(E::OpenEditor));
        let P::EventType { view } = run(detail(E::Save)) else {
            panic!("not an event type");
        };
        assert_eq!(view.editor, None);

        let _ = run(detail(E::OpenActions));
        let P::EventType { view } = run(detail(E::ToggleActive)) else {
            panic!("not an event type");
        };
        let actions = view.actions.expect("the state actions");
        assert_eq!(actions.notice.as_deref(), Some("Event type turned off"));
        let P::EventType { view } = run(detail(E::SendTestEmail {
            kind: "confirmation".to_owned(),
        })) else {
            panic!("not an event type");
        };
        assert!(
            view.actions
                .and_then(|a| a.notice)
                .is_some_and(|n| n.contains("contract@example.com"))
        );
        let _ = run(detail(E::CloseDialog));

        let P::EventType { view } = run(detail(E::OpenHosts)) else {
            panic!("not an event type");
        };
        let hosts = view.hosts_dialog.expect("the hosts");
        assert_eq!(hosts.hosts.len(), 2);
        assert!(hosts.dialog.can_confirm);
        let P::EventType { view } = run(detail(E::SaveHosts)) else {
            panic!("not an event type");
        };
        assert_eq!(view.hosts_dialog, None);

        let _ = run(detail(E::OpenQuestions));
        let P::EventType { view } = run(detail(E::AddQuestion)) else {
            panic!("not an event type");
        };
        let mut question = view
            .questions_dialog
            .and_then(|q| q.form)
            .expect("the question form")
            .form;
        question.label = "Which day suits you?".to_owned();
        let _ = run(detail(E::EditQuestionForm { form: question }));
        let P::EventType { view } = run(detail(E::SaveQuestion)) else {
            panic!("not an event type");
        };
        let questions = view.questions_dialog.expect("the questions");
        assert_eq!(questions.form, None);
        assert_eq!(questions.notice.as_deref(), Some("Question added"));
        let _ = run(detail(E::AskDeleteQuestion {
            id: questions.questions[0].id.clone(),
        }));
        let P::EventType { view } = run(detail(E::ConfirmDeleteQuestion)) else {
            panic!("not an event type");
        };
        assert_eq!(
            view.questions_dialog.and_then(|q| q.notice).as_deref(),
            Some("Question deleted")
        );
        let _ = run(detail(E::CloseDialog));

        let _ = run(detail(E::OpenActions));
        let P::EventType { view } = run(detail(E::AskDelete)) else {
            panic!("not an event type");
        };
        assert!(view.actions.is_some_and(|a| a.delete.is_some()));
        let P::EventTypes { view } = run(detail(E::ConfirmDelete)) else {
            panic!("not back on the list");
        };
        assert_eq!(view.rows.len(), 2);
    }

    /// The working hours open from the booking pages and fill in, the
    /// overrides still to come; the week's editor reads, a window is added
    /// and the save lands; the overrides' editor adds a day off and deletes
    /// one, each landing.
    #[test]
    fn the_working_hours_fill_in_and_their_writes_land() {
        use crate::events::UiEvent;
        use crate::scheduling_admin::hours::{HoursAction as H, OverrideFormView};
        use crate::scheduling_admin::{
            SchedulingAdminAction as A, SchedulingAdminPage as P, SchedulingAdminSection,
        };

        let mut model = started();
        let mut run = |action: A| {
            let mut pending = Vec::new();
            for event in (UiEvent::SchedulingAdmin { action }).events() {
                pending.extend(model.update(event));
            }
            settle(&mut model, pending);
            let screen = screen_view(&model);
            assert_eq!(unsettled(&screen), None, "{screen:?}");
            let ScreenView::SchedulingAdmin { view } = screen else {
                panic!("not the scheduling administration: {screen:?}");
            };
            let P::Hours { view } = view.page else {
                panic!("not the working hours");
            };
            view
        };
        let view = run(A::Open {
            section: SchedulingAdminSection::Hours,
        });
        assert_eq!(view.status, crate::views::LoadStatus::Ready);
        assert_eq!(view.week.len(), 7);
        assert_eq!(view.overrides.len(), 2);
        assert!(view.offers_writes);

        let hours = |action| A::Hours { action };
        let view = run(hours(H::OpenWeek));
        let week = view.week_editor.expect("the week's editor");
        assert_eq!(week.days.len(), 7);
        assert!(week.dialog.can_confirm);
        let _ = run(hours(H::AddRange { day: 0 }));
        let view = run(hours(H::SaveWeek));
        let week = view.week_editor.expect("still open");
        assert_eq!(week.notice.as_deref(), Some("Working hours saved"));
        assert_eq!(week.dialog.failure, None);
        let _ = run(hours(H::CloseDialog));

        let view = run(hours(H::OpenOverrides));
        let editor = view.overrides_editor.expect("the overrides' editor");
        assert_eq!(editor.rows.len(), 2);
        let view = run(hours(H::AddOverride));
        let form = view
            .overrides_editor
            .and_then(|e| e.form)
            .expect("the add form")
            .form;
        let _ = run(hours(H::EditOverride {
            form: OverrideFormView {
                date: "2099-12-24".to_owned(),
                ..form
            },
        }));
        let view = run(hours(H::SaveOverride));
        let editor = view.overrides_editor.expect("still open");
        assert_eq!(editor.form, None);
        assert_eq!(editor.notice.as_deref(), Some("Override added"));
        let _ = run(hours(H::AskDeleteOverride {
            key: editor.rows[1].key.clone(),
        }));
        let view = run(hours(H::ConfirmDeleteOverride));
        assert_eq!(
            view.overrides_editor.and_then(|e| e.notice).as_deref(),
            Some("Override deleted")
        );
        let view = run(hours(H::CloseDialog));
        assert_eq!(view.overrides_editor, None);
    }

    /// The calendars open from the booking pages and fill in; connecting
    /// Google hands off to the browser, which the scene never opens, and the
    /// section waits for it until it is read again; an account's calendars
    /// are chosen and the save lands; a CalDAV account connects; and an
    /// account is disconnected, asked once more.
    #[test]
    fn the_calendars_fill_in_and_their_writes_land() {
        use crate::events::UiEvent;
        use crate::scheduling_admin::calendar::{CaldavFormView, CalendarAction as C};
        use crate::scheduling_admin::{
            SchedulingAdminAction as A, SchedulingAdminPage as P, SchedulingAdminSection,
        };

        let mut model = started();
        let mut send = |action: UiEvent| {
            let mut pending = Vec::new();
            for event in action.events() {
                pending.extend(model.update(event));
            }
            settle(&mut model, pending);
            let screen = screen_view(&model);
            assert_eq!(unsettled(&screen), None, "{screen:?}");
            let ScreenView::SchedulingAdmin { view } = screen else {
                panic!("not the scheduling administration: {screen:?}");
            };
            let P::Calendar { view } = view.page else {
                panic!("not the calendars");
            };
            view
        };
        let view = send(UiEvent::SchedulingAdmin {
            action: A::Open {
                section: SchedulingAdminSection::Calendar,
            },
        });
        let mut run = |action: C| {
            send(UiEvent::SchedulingAdmin {
                action: A::Calendar { action },
            })
        };
        assert_eq!(view.status, crate::views::LoadStatus::Ready);
        assert_eq!(view.connections.len(), 2);
        assert_eq!(view.connections[0].destination, "Contract Member");
        let providers: Vec<&str> = view.providers.iter().map(|p| p.value.as_str()).collect();
        assert_eq!(providers, ["google"]);
        assert!(view.zoom.is_some());

        let view = run(C::Connect {
            provider: "google".to_owned(),
        });
        assert!(view.awaiting_browser && !view.connecting);
        assert_eq!(view.connect_failure, None);

        let view = run(C::OpenCalendars {
            connection_id: "cal-conn-contract-1".to_owned(),
        });
        let picker = view.picker.expect("the calendars");
        assert_eq!(picker.calendars.len(), 2);
        assert!(picker.dialog.can_confirm);
        let _ = run(C::SetCheckConflicts {
            calendar_id: "holidays@example.com".to_owned(),
            on: true,
        });
        let view = run(C::SaveCalendars);
        let picker = view.picker.expect("still open");
        assert_eq!(picker.notice.as_deref(), Some("Calendars saved"));
        let view = run(C::CloseDialog);
        assert_eq!(view.picker, None);

        let _ = run(C::OpenCaldav);
        let _ = run(C::EditCaldav {
            form: CaldavFormView {
                preset: "icloud".to_owned(),
                server_url: String::new(),
                username: "ada@example.com".to_owned(),
                app_password: b"scripted-app-password".to_vec(),
            },
        });
        let view = run(C::ConnectCaldav);
        let caldav = view.caldav.expect("still open");
        assert_eq!(
            caldav.notice.as_deref(),
            Some("Calendar connected: caldav@example.com")
        );
        assert_eq!(caldav.username, "");
        let _ = run(C::CloseDialog);

        let view = run(C::AskDisconnect {
            connection_id: "cal-conn-contract-2".to_owned(),
        });
        assert!(view.disconnect.is_some());
        let view = run(C::ConfirmDisconnect);
        assert_eq!(view.disconnect, None);
        assert_eq!(view.disconnect_failure, None);
        assert_eq!(view.disconnect_notice, None);
    }

    /// The team opens from the booking pages and fills in, naming who left
    /// while still a host; a team is created; Sales's hosts open, a routing
    /// priority is saved and a host's removal, asked once more, lands; a host
    /// is added to Support; an account is archived, asked once more; and
    /// Support is deleted, asked once more.
    #[test]
    fn the_team_fills_in_and_its_writes_land() {
        use crate::events::UiEvent;
        use crate::scheduling_admin::team::TeamAction as T;
        use crate::scheduling_admin::{
            SchedulingAdminAction as A, SchedulingAdminPage as P, SchedulingAdminSection,
        };

        let mut model = started();
        let mut send = |action: UiEvent| {
            let mut pending = Vec::new();
            for event in action.events() {
                pending.extend(model.update(event));
            }
            settle(&mut model, pending);
            let screen = screen_view(&model);
            assert_eq!(unsettled(&screen), None, "{screen:?}");
            let ScreenView::SchedulingAdmin { view } = screen else {
                panic!("not the scheduling administration: {screen:?}");
            };
            let P::Team { view } = view.page else {
                panic!("not the team");
            };
            view
        };
        let view = send(UiEvent::SchedulingAdmin {
            action: A::Open {
                section: SchedulingAdminSection::Team,
            },
        });
        let mut run = |action: T| {
            send(UiEvent::SchedulingAdmin {
                action: A::Team { action },
            })
        };
        assert_eq!(view.status, crate::views::LoadStatus::Ready);
        assert_eq!(view.members.len(), 5);
        assert!(view.stranded.is_some(), "the scene's host is not a member");
        assert_eq!(view.teams.len(), 2);
        assert!(view.offers_create);

        let _ = run(T::OpenCreate);
        let _ = run(T::EditName {
            name: "Front desk".to_owned(),
        });
        let view = run(T::SaveName);
        let editor = view.editor.expect("still open");
        assert_eq!(editor.notice.as_deref(), Some("Team created"));
        assert_eq!(run(T::CloseDialog).editor, None);

        let view = run(T::OpenMembers {
            team_id: "team-contract-1".to_owned(),
        });
        assert_eq!(view.hosts.expect("open").hosts.len(), 2);
        let _ = run(T::EditPriority {
            user_id: "sched-user-rotation".to_owned(),
            value: "2".to_owned(),
        });
        let view = run(T::SavePriority {
            user_id: "sched-user-rotation".to_owned(),
        });
        let hosts = view.hosts.expect("still open");
        assert_eq!(hosts.notice.as_deref(), Some("Routing priority saved"));
        assert_eq!(hosts.dialog.title, "Sales");
        let view = run(T::AskRemove {
            user_id: "sched-user-rotation".to_owned(),
        });
        assert!(view.hosts.expect("open").remove.is_some());
        let view = run(T::ConfirmRemove);
        let hosts = view.hosts.expect("still open");
        assert_eq!(hosts.notice.as_deref(), Some("Host removed from the team"));
        assert_eq!(hosts.remove, None);
        let _ = run(T::CloseDialog);

        let view = run(T::OpenMembers {
            team_id: "team-contract-2".to_owned(),
        });
        let candidate = view.hosts.expect("open").candidates[0].value.clone();
        let _ = run(T::Pick { user_id: candidate });
        let view = run(T::AddMember);
        assert_eq!(
            view.hosts.expect("still open").notice.as_deref(),
            Some("Host added to the team")
        );
        let _ = run(T::CloseDialog);

        let view = run(T::OpenArchive {
            user_id: "sched-user-contract".to_owned(),
        });
        let archive = view.archive.expect("open");
        assert!(archive.bookings.is_empty() && archive.blocked.is_none());
        assert!(archive.dialog.can_confirm);
        let view = run(T::AskArchive);
        assert!(view.archive.expect("open").ask.is_some());
        let view = run(T::ConfirmArchive);
        assert_eq!(
            view.archive.expect("still open").notice.as_deref(),
            Some("Contract Member's scheduler account is closed")
        );
        let _ = run(T::CloseDialog);

        let _ = run(T::OpenEditor {
            team_id: "team-contract-2".to_owned(),
        });
        let view = run(T::AskDelete);
        assert!(view.editor.expect("open").delete.is_some());
        let view = run(T::ConfirmDelete);
        assert_eq!(
            view.editor.expect("still open").notice.as_deref(),
            Some("Team deleted")
        );
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
        assert!(view.notice.as_ref().is_some_and(|n| n.saved), "{view:?}");
        assert_eq!(view.ring_seconds, 12);
        assert!(
            view.modes
                .iter()
                .any(|m| m.selected && m.mode == CallHandlingChoice::AppFirst)
        );
        let ScreenView::CallHandling { view } = run(
            &mut model,
            call_handling(CallHandlingAction::SetAvailable { available: false }),
        ) else {
            panic!("call handling shows");
        };
        assert!(!view.availability.available);
        assert!(view.availability.notice.as_ref().is_some_and(|n| n.saved));

        let routing = |action| UiEvent::Routing { action };
        let _ = run(&mut model, routing(RoutingAction::Open));
        let _ = run(&mut model, routing(RoutingAction::Add));
        let _ = run(&mut model, routing(RoutingAction::Save));
        let ScreenView::Routing { view } = run(&mut model, routing(RoutingAction::ConfirmSave))
        else {
            panic!("the routing rules show");
        };
        assert!(view.notice.as_ref().is_some_and(|n| n.saved), "{view:?}");

        let directory = |action| UiEvent::Directory { action };
        let _ = run(&mut model, directory(DirectoryAction::Open));
        let _ = run(&mut model, directory(DirectoryAction::Remove { index: 0 }));
        let _ = run(&mut model, directory(DirectoryAction::Save));
        let ScreenView::Directory { view } =
            run(&mut model, directory(DirectoryAction::ConfirmSave))
        else {
            panic!("the directory shows");
        };
        assert!(view.notice.as_ref().is_some_and(|n| n.saved), "{view:?}");
    }

    /// The Skills and Knowledge sections' writes are answered: each save
    /// lands and is read back, an added document is listed again, and a
    /// change of mode is the mode stored. Nothing is left on its way.
    #[test]
    fn the_skills_and_knowledge_writes_land() {
        use district_core::{KnowledgeEvent, ToolsEvent, WorkspaceSection as S};
        use district_model::KnowledgeMode;
        let mut model = started();
        let _ = after(&mut model, Event::Navigate(Route::Workspace(S::Tools)));
        for event in [
            Event::Tools(ToolsEvent::Toggle {
                id: "send_sms".to_owned(),
                enabled: true,
            }),
            Event::Tools(ToolsEvent::SaveTools),
            Event::Tools(ToolsEvent::SetEnrichment(false)),
            Event::Tools(ToolsEvent::SaveEnrichment),
        ] {
            let screen = after(&mut model, event);
            assert_eq!(unsettled(&screen), None, "{screen:?}");
            let json = serde_json::to_value(&screen).unwrap();
            assert_eq!(json["Tools"]["view"]["tools_saving"], json!(false));
            assert_eq!(json["Tools"]["view"]["research_saving"], json!(false));
        }
        let _ = after(&mut model, Event::Navigate(Route::Workspace(S::Knowledge)));
        for event in [
            Event::Knowledge(KnowledgeEvent::EditTitle("Parking".to_owned())),
            Event::Knowledge(KnowledgeEvent::EditContent("Behind the clinic.".to_owned())),
            Event::Knowledge(KnowledgeEvent::Add),
            Event::Knowledge(KnowledgeEvent::AskDelete {
                document_id: "doc_contract_ready".to_owned(),
            }),
            Event::Knowledge(KnowledgeEvent::Confirm),
            Event::Knowledge(KnowledgeEvent::SelectMode(KnowledgeMode::Internal)),
        ] {
            let screen = after(&mut model, event);
            assert_eq!(unsettled(&screen), None, "{screen:?}");
            let view = &serde_json::to_value(&screen).unwrap()["Knowledge"]["view"];
            assert_eq!(view["adding"], json!(false), "{view}");
            assert_eq!(view["add_enabled"], json!(true), "{view}");
        }
        let screen = serde_json::to_value(screen_view(&model)).unwrap();
        let view = &screen["Knowledge"]["view"];
        assert_eq!(view["notice"]["saved"], json!(true), "{view}");
        assert_eq!(view["modes"][0]["selected"], json!(true), "{view}");
    }

    /// Voice Studio opens from the hub filled in, and a save in the scene is
    /// answered: written, read back, and the notice shown.
    #[test]
    fn voice_studio_opens_and_its_save_is_answered() {
        use crate::settings::voice_studio::{StudioPicker, VoiceStudioAction};
        let mut model = started();
        let _ = after(
            &mut model,
            Event::Navigate(Route::Workspace(district_core::WorkspaceSection::Hub)),
        );
        let screen = after(
            &mut model,
            Event::Navigate(Route::Workspace(
                district_core::WorkspaceSection::VoiceStudio,
            )),
        );
        assert_eq!(unsettled(&screen), None, "{screen:?}");
        for action in [
            VoiceStudioAction::SelectLeg {
                leg: "tts".to_owned(),
            },
            VoiceStudioAction::Pick {
                picker: StudioPicker::Voice,
                value: "aura-2-luna-en".to_owned(),
            },
            VoiceStudioAction::Save,
        ] {
            let mut pending = Vec::new();
            for event in (crate::events::UiEvent::VoiceStudio { action }).events() {
                pending.extend(model.update(event));
            }
            settle(&mut model, pending);
        }
        let ScreenView::VoiceStudio { view } = screen_view(&model) else {
            panic!("Voice Studio stays open");
        };
        let studio = view.studio.expect("read back");
        assert!(!studio.saving);
        assert!(studio.notice.is_some(), "{studio:?}");
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
