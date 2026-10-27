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
//!
//! District HQ by voice is the one exception to "no live connection": its
//! session is answered with a made-up credential, and its room (no media
//! engine, no audio anywhere) is played by [`voice_room`], so the walk can see
//! the session's states. [`LANGUAGE_ARG`] starts a scene in another language,
//! for the walk's French pass.

use std::collections::HashMap;
use std::future::Future;
use std::sync::{Mutex, PoisonError};

use district_api::{ApiError, ErrorDetail};
use district_auth::AccessClaims;
use district_core::{
    Effect, Event, Locale, MediaEvent, MediaUpdate, MicrophoneState, Participant, Speaker,
    SpeechSegment, Ticket,
};
use district_live::{LiveUpdate, WorkspaceUpdate};
use serde::de::DeserializeOwned;
use serde_json::{Value, json};
use tokio::sync::mpsc::UnboundedSender;

use super::Effects;

/// The command-line argument that names the scene: `--district-scripted-scene=signed-in`.
pub(crate) const SCENE_ARG: &str = "--district-scripted-scene=";

/// The command-line argument that names the language a scene runs in, as a
/// Windows language tag: `--district-scripted-language=fr-CA`. Read only
/// beside [`SCENE_ARG`], in place of the user's Windows languages, by the
/// window and the core alike.
pub(crate) const LANGUAGE_ARG: &str = "--district-scripted-language=";

/// The media server a scripted voice session's credential names: never
/// dialled, only told apart from a call's.
const VOICE_URL: &str = "wss://media.example.com/scripted-hq-voice";

/// The voice agent's identity in the scripted room.
const VOICE_AGENT: &str = "agent-hq-scripted";

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
    /// As [`Scene::SignedIn`], but each read a dialog offers "Try again" for
    /// fails the first time it is made, and is answered when made again: the
    /// week's and the overrides' editors' own reads, an event type's hosts
    /// dialog's read of its hosts, and a webhook's deliveries.
    FailingReads,
    /// As [`Scene::SignedIn`], but the workspace is in Canada, so District
    /// HQ's residency statement has its region line, and the voice agent
    /// leaves the session as soon as it has spoken and proposed its changes:
    /// the session ends with its cards still waiting, and a call can be placed
    /// beside them.
    VoiceEnds,
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
        "signed-in-failing-reads" => Ok(Some(Scene::FailingReads)),
        "signed-in-voice-ends" => Ok(Some(Scene::VoiceEnds)),
        other => Err(format!("{MARKER}: no scene named {other:?}")),
    }
}

/// The languages a scene's command line names ([`LANGUAGE_ARG`]), in place of
/// the user's own: `None` without one, or without a scene named beside it, so
/// a scripted build started as any other speaks the user's languages.
pub(crate) fn languages(args: impl IntoIterator<Item = String>) -> Option<Vec<String>> {
    let mut scene = false;
    let mut language = None;
    for arg in args {
        scene |= arg.starts_with(SCENE_ARG);
        if let Some(tag) = arg.strip_prefix(LANGUAGE_ARG) {
            language = Some(tag.to_owned());
        }
    }
    language.filter(|_| scene).map(|tag| vec![tag])
}

/// The effects of a scripted scene.
pub(crate) struct Scripted {
    scene: Scene,
    reads: Mutex<FailingReads>,
    voice: Mutex<VoiceSession>,
    /// Where the scripted voice room's reports go: the model's media updates,
    /// as the call engine's go in a build that ships.
    media: UnboundedSender<MediaUpdate>,
}

/// The scripted voice session.
#[derive(Debug, Default)]
struct VoiceSession {
    /// The language the session was asked for in.
    locale: Locale,
    /// The room's session, while it is joined.
    session: Option<Ticket>,
}

impl Scripted {
    pub(crate) fn new(scene: Scene, media: UnboundedSender<MediaUpdate>) -> Self {
        Self {
            scene,
            reads: Mutex::new(FailingReads::default()),
            voice: Mutex::new(VoiceSession::default()),
            media,
        }
    }

    fn voice(&self) -> std::sync::MutexGuard<'_, VoiceSession> {
        self.voice.lock().unwrap_or_else(PoisonError::into_inner)
    }

    /// The scene's answer to a District HQ voice effect (`Some`, holding the
    /// event or none), or `None` for any other effect. The credential is made
    /// up and joinable; the room it names is played by [`voice_room`], its
    /// reports sent on as the engine's are; the microphone does as it is
    /// told. Nothing else of the session is answered: a card's Confirm and
    /// Cancel, and the screen reports, go unanswered, as with no network.
    fn voice_answer(&self, effect: &Effect) -> Option<Option<Event>> {
        match effect {
            Effect::RequestHqVoice { ticket, locale, .. } => {
                self.voice().locale = *locale;
                Some(Some(Event::HqVoiceIssued {
                    ticket: *ticket,
                    result: voice_credential(),
                }))
            }
            Effect::ConnectMedia {
                session,
                credential,
                ..
            } if credential.url() == VOICE_URL => {
                let locale = {
                    let mut voice = self.voice();
                    voice.session = Some(*session);
                    voice.locale
                };
                for event in voice_room(locale, self.scene == Scene::VoiceEnds) {
                    // Closed only once the actor has stopped.
                    self.media
                        .send(MediaUpdate {
                            session: *session,
                            event,
                        })
                        .ok();
                }
                Some(None)
            }
            Effect::SetMicrophone { session, enabled }
                if self.voice().session == Some(*session) =>
            {
                Some(Some(Event::Media(MediaUpdate {
                    session: *session,
                    event: MediaEvent::Microphone(if *enabled {
                        MicrophoneState::On
                    } else {
                        MicrophoneState::Off
                    }),
                })))
            }
            Effect::DisconnectMedia { session } if self.voice().session == Some(*session) => {
                self.voice().session = None;
                Some(None)
            }
            _ => None,
        }
    }

    /// The scene's answer to `effect`, if it has one.
    fn answer(&self, effect: Effect) -> Option<Event> {
        if let Some(answer) = self.voice_answer(&effect) {
            return answer;
        }
        if let (Scene::VoiceEnds, Effect::LoadWorkspaces { ticket }) = (self.scene, &effect) {
            return Some(Event::WorkspacesLoaded {
                ticket: *ticket,
                remembered: None,
                result: serde_json::from_value(workspaces_in("ca")).map_err(|_| broken()),
            });
        }
        if let (
            Scene::FailingReads,
            Effect::SchedulingAdmin {
                ticket, request, ..
            },
        ) = (self.scene, &effect)
            && self
                .reads
                .lock()
                .unwrap_or_else(PoisonError::into_inner)
                .fails(request)
        {
            return Some(Event::SchedulingAdminAnswered {
                ticket: *ticket,
                result: Err(broken()),
            });
        }
        signed_in(effect)
    }
}

impl Effects for Scripted {
    fn run(&self, effect: Effect) -> impl Future<Output = Option<Event>> + Send {
        std::future::ready(self.answer(effect))
    }
}

/// A read [`Scene::FailingReads`] fails once.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash)]
enum Read {
    /// The member's own rules (no event type's).
    Rules,
    Overrides,
    Hosts,
    Deliveries,
}

/// Which read of each [`Read`] kind is a dialog's, counted from the scene's
/// start along the UI walk's path, which never opens the booking pages'
/// overview: the working hours' own reads come first, then an editor's (the
/// second of each); an event type's page reads its hosts, then the hosts
/// dialog (the second); a webhook's deliveries are read only when opened (the
/// first). Counts, not an order of requests, because a section's reads run at
/// once and in no fixed order. Each kind fails once.
#[derive(Debug, Default)]
struct FailingReads {
    /// How many of each kind have been read.
    reads: HashMap<Read, u32>,
}

impl FailingReads {
    /// Whether `request` is answered with a failure.
    fn fails(&mut self, request: &district_model::SchedulingAdminRequest) -> bool {
        use district_model::SchedulingAdminRequest as R;
        let (kind, dialogs) = match request {
            R::Rules {
                event_type_id: None,
            } => (Read::Rules, 2),
            R::Overrides => (Read::Overrides, 2),
            R::Hosts { .. } => (Read::Hosts, 2),
            R::WebhookDeliveries { .. } => (Read::Deliveries, 1),
            _ => return false,
        };
        let count = self.reads.entry(kind).or_default();
        *count += 1;
        *count == dialogs
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
    workspaces_in("us")
}

/// The one workspace, its data in `region`.
fn workspaces_in(region: &str) -> Value {
    json!({
        "success": true,
        "workspaces": [{
            "id": WORKSPACE_ID,
            "name": WORKSPACE_NAME,
            "region": region,
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
        // The first page of a workflow's runs is the recorded one, which says
        // there are more; the page after it is the same runs under ids of
        // their own, and the last.
        Effect::LoadWorkflowRuns { ticket, offset, .. } => Event::WorkflowRunsLoaded {
            ticket,
            result: if offset == 0 {
                decode(fixture!("district-workflow-runs.json"))
            } else {
                later_runs(offset)
            },
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
        // A carrier account's write lands, as the service answers each kind;
        // the accounts are then read again, from the fixture above. The
        // owner's number is not answered.
        Effect::WriteMessaging { ticket, write, .. } => Event::SettingsWritten {
            ticket,
            result: written(messaging_answer(&write)?),
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
        R::Me | R::PatchMe(_) => fixture!("district-scheduling-me.json"),
        R::Branding | R::PatchBranding(_) => fixture!("district-scheduling-branding.json"),
        R::Llm | R::PatchLlm { .. } => fixture!("district-scheduling-llm.json"),
        R::Upload(_) => fixture!("district-scheduling-upload.json"),
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
        R::ApiKeys => fixture!("district-scheduling-api-keys.json"),
        R::CreateApiKey { .. } => fixture!("district-scheduling-api-key-created.json"),
        R::OauthConnections => fixture!("district-scheduling-oauth-connections.json"),
        R::Webhooks => fixture!("district-scheduling-webhooks.json"),
        R::CreateWebhook { .. } => fixture!("district-scheduling-webhook-created.json"),
        R::WebhookDeliveries { .. } => fixture!("district-scheduling-webhook-deliveries.json"),
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
        | R::RemoveTeamMember { .. }
        | R::DeleteApiKey { .. }
        | R::DeleteOauthConnection { .. }
        | R::PatchWebhook { .. }
        | R::DeleteWebhook { .. }
        | R::DeleteAvatar
        | R::DeleteLogo
        | R::DeleteBanner => fixture!("district-scheduling-ok.json"),
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
            } else if let (
                R::Users {
                    include_archived: None,
                },
                Some(users),
            ) = (request, data.as_array_mut())
            {
                // Who could be added as a host, or take a booking (not the
                // team's list of members): one more member, active and no
                // event type's host, so an event type's hosts offer someone.
                if let Some(mut colleague) = users.first().cloned() {
                    colleague["id"] = json!("sched-user-scripted");
                    colleague["email"] = json!("colleague@example.com");
                    colleague["name"] = json!("Scripted Colleague");
                    colleague["is_admin"] = json!(false);
                    colleague["is_owner"] = json!(false);
                    colleague["role"] = json!("member");
                    users.push(colleague);
                }
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

/// The service's answer to a carrier account `write`, or `None` for the
/// owner's number, which goes unanswered.
fn messaging_answer(write: &district_core::MessagingWrite) -> Option<&'static str> {
    use district_core::MessagingWrite as W;
    Some(match write {
        W::SaveAccount(_) => fixture!("district-messaging-upsert.json"),
        W::SetDefault(_) => fixture!("district-messaging-set-default.json"),
        W::SetChannelDefault(_) => fixture!("district-messaging-channel-default.json"),
        W::Delete(_) => fixture!("district-messaging-delete.json"),
        W::CreatorCell(_) => return None,
    })
}

/// The page of a workflow's runs after the first `offset` runs: the recorded
/// runs under ids of their own, and nothing after them.
fn later_runs(offset: u32) -> Result<district_model::WorkflowRunsResponse, ApiError> {
    let mut page: Value =
        serde_json::from_str(fixture!("district-workflow-runs.json")).map_err(|_| broken())?;
    for run in page["runs"].as_array_mut().ok_or_else(broken)? {
        let id = format!("{}-{offset}", run["id"].as_str().unwrap_or("run"));
        run["id"] = json!(id);
    }
    page["offset"] = json!(offset);
    page["hasMore"] = json!(false);
    serde_json::from_value(page).map_err(|_| broken())
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

/// A voice session's credential: an HQ room with a passphrase, so the core
/// joins it, on [`VOICE_URL`], which only this scene answers. The core has no
/// fixture for it: the smallest answer of the shape it decodes.
fn voice_credential() -> Result<district_model::HqVoiceTokenResponse, ApiError> {
    serde_json::from_value(json!({
        "success": true,
        "token": "scripted-voice-token",
        "url": VOICE_URL,
        "roomName": format!("{}{WORKSPACE_ID}_scripted", district_model::HQ_ROOM_PREFIX),
        "processingRegion": "us",
        "e2ee": { "key": "scripted-voice-passphrase" },
    }))
    .map_err(|_| broken())
}

/// The scripted voice room, as the engine reports it once joined, in
/// `locale`: the microphone on, the agent in the room and listening, the
/// member's request and the agent's reply, and the two changes it proposes
/// (one sentence each, long enough to wrap), as the voice agent sends them on
/// [`district_model::HQ_PROPOSAL_TOPIC`]. Then the member still speaking, or,
/// when the session `ends`, the agent leaving instead.
fn voice_room(locale: Locale, ends: bool) -> Vec<MediaEvent> {
    let french = locale == Locale::FrCa;
    let line = |id: &str, speaker, english: &str, francais: &str, is_final| {
        MediaEvent::Speech(SpeechSegment {
            segment_id: id.to_owned(),
            speaker,
            text: (if french { francais } else { english }).to_owned(),
            is_final,
        })
    };
    let proposal =
        |id: &str, tool: &str, args: Value, english: &str, francais: &str| MediaEvent::AgentData {
            topic: district_model::HQ_PROPOSAL_TOPIC.to_owned(),
            payload: json!({
                "type": "HQ_PROPOSAL",
                "proposalId": id,
                "tool": tool,
                "args": args,
                "summary": if french { francais } else { english },
            })
            .to_string()
            .into_bytes(),
        };
    let mut room = vec![
        MediaEvent::Connecting,
        MediaEvent::Connected,
        MediaEvent::Microphone(MicrophoneState::On),
        MediaEvent::ParticipantJoined(Participant::new(
            VOICE_AGENT,
            Some("District HQ".to_owned()),
            true,
        )),
        MediaEvent::AgentState("listening".to_owned()),
        line(
            "scripted-member-1",
            Speaker::Member,
            "Change the greeting to say good afternoon, and let our phones ring longer before the receptionist answers.",
            "Changez l'accueil pour dire bon après-midi, et laissez sonner nos téléphones plus longtemps avant que la réceptionniste réponde.",
            true,
        ),
        line(
            "scripted-agent-1",
            Speaker::Agent,
            "I have prepared both changes. Confirm or cancel each one on its card.",
            "J'ai préparé les deux changements. Confirmez ou annulez chacun d'eux sur sa carte.",
            true,
        ),
        proposal(
            "11111111-1111-4111-8111-111111111111",
            "update_persona",
            json!({ "greeting": "Good afternoon, thanks for calling Example Dental." }),
            "Update the AI receptionist persona (greeting \u{2192} \"Good afternoon, thanks for calling Example Dental.\").",
            "Mettre à jour la persona de la réceptionniste (accueil \u{2192} «\u{a0}Bon après-midi, merci d'appeler Example Dental.\u{a0}»).",
        ),
        proposal(
            "22222222-2222-4222-8222-222222222222",
            "update_call_handling",
            json!({ "appRingSeconds": 30 }),
            "Ring the team's apps for 30 seconds before the AI receptionist answers a call.",
            "Faire sonner les applications de l'équipe pendant 30\u{a0}secondes avant que la réceptionniste réponde à un appel.",
        ),
    ];
    if ends {
        room.push(MediaEvent::ParticipantLeft {
            identity: VOICE_AGENT.to_owned(),
        });
    } else {
        room.push(line(
            "scripted-member-2",
            Speaker::Member,
            "Thanks. Could you also",
            "Merci. Pourriez-vous aussi",
            false,
        ));
    }
    room
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
    use tokio::sync::mpsc::unbounded_channel;

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
        assert_eq!(
            scene(args(&[
                "DistrictAI.exe",
                "--district-scripted-scene=signed-in-voice-ends"
            ])),
            Ok(Some(Scene::VoiceEnds))
        );
        assert!(scene(args(&["DistrictAI.exe", "--district-scripted-scene=nope"])).is_err());
    }

    /// A language is taken from the command line only beside a scene, so a
    /// scripted build started without one speaks the user's languages.
    #[test]
    fn a_language_is_named_only_beside_a_scene() {
        let args = |list: &[&str]| list.iter().map(|arg| (*arg).to_owned()).collect::<Vec<_>>();
        assert_eq!(
            languages(args(&[
                "DistrictAI.exe",
                "--district-scripted-scene=signed-in",
                "--district-scripted-language=fr-CA",
            ])),
            Some(vec!["fr-CA".to_owned()])
        );
        assert_eq!(
            languages(args(&[
                "DistrictAI.exe",
                "--district-scripted-language=fr-CA"
            ])),
            None
        );
        assert_eq!(
            languages(args(&[
                "DistrictAI.exe",
                "--district-scripted-scene=signed-in"
            ])),
            None
        );
        assert_eq!(
            Locale::from_tags(
                &languages(args(&[
                    "--district-scripted-language=fr-CA",
                    "--district-scripted-scene=signed-in-voice-ends",
                ]))
                .unwrap()
            ),
            Locale::FrCa
        );
    }

    /// A scene played as the actor plays it, the voice room's reports
    /// included, in a build that can carry calls.
    struct Played {
        scripted: Scripted,
        reports: tokio::sync::mpsc::UnboundedReceiver<MediaUpdate>,
        model: Model,
    }

    impl Played {
        fn new(scene: Scene) -> Self {
            let (media, reports) = unbounded_channel();
            let (model, first) = Model::new(CoreConfig {
                web_base_url: "https://www.distronode.com".to_owned(),
                app_version: "0.1.0".to_owned(),
                calls_available: true,
                in_app_purchases: true,
                locale: Locale::En,
            });
            let mut played = Self {
                scripted: Scripted::new(scene, media),
                reports,
                model,
            };
            played.settle(first);
            played
        }

        /// Answers every effect, and hands the model each report of the
        /// room, until nothing is left.
        fn settle(&mut self, mut pending: Vec<Effect>) {
            for _ in 0..1000 {
                if let Some(effect) = pending.pop() {
                    if let Some(event) = self.scripted.answer(effect) {
                        assert!(
                            !format!("{event:?}").contains("result: Err("),
                            "an answer did not decode: {event:?}"
                        );
                        pending.extend(self.model.update(event));
                    }
                } else if let Ok(update) = self.reports.try_recv() {
                    pending.extend(self.model.update(Event::Media(update)));
                } else {
                    return;
                }
            }
            panic!("the scene never settles");
        }

        fn ui(&mut self, event: crate::events::UiEvent) {
            let mut pending = Vec::new();
            for event in event.events() {
                pending.extend(self.model.update(event));
            }
            self.settle(pending);
        }

        fn hq(&mut self, action: crate::hq::HqAction) {
            self.ui(crate::events::UiEvent::Hq { action });
        }

        fn open(&mut self, destination: crate::nav::NavDestination) {
            self.ui(crate::events::UiEvent::Navigate { destination });
        }

        fn page(&self) -> crate::hq::HqView {
            match screen_view(&self.model) {
                ScreenView::Hq { view } => view,
                other => panic!("not District HQ: {other:?}"),
            }
        }

        fn dock(&self) -> crate::hq::HqVoiceDockView {
            shell_view(&self.model).voice_dock.expect("the voice dock")
        }
    }

    fn spoken(view: &crate::hq::HqView) -> Vec<(String, bool, bool)> {
        view.messages
            .iter()
            .filter_map(|message| match message {
                crate::hq::HqMessageView::Spoken {
                    text,
                    member,
                    is_final,
                    ..
                } => Some((text.clone(), *member, *is_final)),
                _ => None,
            })
            .collect()
    }

    /// The UI walk's voice states, in the signed-in scene: Start voice shows
    /// the residency statement (no region line: a United States
    /// workspace); Start voice under it joins the scripted room, where the
    /// agent listens, both sides' words show, and two changes wait on their
    /// cards; away from District HQ the dock shows them, and the microphone
    /// turns off and on again as the member asks. Stop voice ends it, the
    /// cards staying.
    #[test]
    fn the_voice_session_listens_and_proposes_two_changes() {
        use crate::hq::{HqAction as H, HqVoiceStage};
        use crate::nav::NavDestination as D;

        let mut played = Played::new(Scene::SignedIn);
        played.open(D::Hq);
        let page = played.page();
        assert!(page.show_voice_start && page.can_start_voice, "{page:?}");

        played.hq(H::StartVoice);
        let disclosure = played
            .page()
            .voice_disclosure
            .expect("the residency statement");
        assert_eq!(disclosure.region_notice, None);

        played.hq(H::AcceptVoiceDisclosure);
        let page = played.page();
        assert_eq!(page.voice.stage, HqVoiceStage::Live);
        assert_eq!(
            page.voice.line.as_deref(),
            Some("Listening. Speak to District HQ.")
        );
        assert!(!page.voice.line_is_problem && !page.voice.busy);
        assert!(page.voice.show_microphone && !page.voice.muted && page.voice.can_stop);
        let lines = spoken(&page);
        assert_eq!(lines.len(), 3, "{lines:?}");
        assert!(lines[0].1 && lines[0].2, "the member's words, final");
        assert!(!lines[1].1 && lines[1].2, "the agent's reply, final");
        assert!(lines[2].1 && !lines[2].2, "the member still speaking");
        assert_eq!(page.voice.cards.len(), 2);
        assert!(page.voice.cards[0].summary.contains("Good afternoon"));
        assert!(
            page.voice
                .cards
                .iter()
                .all(|card| card.can_confirm && card.can_cancel)
        );

        played.open(D::Calls);
        let dock = played.dock();
        assert_eq!(dock.voice.cards.len(), 2);
        assert!(dock.can_review && dock.voice.can_stop && !dock.voice.muted);
        assert_eq!(unsettled(&screen_view(&played.model)), None);

        played.ui(crate::events::UiEvent::Microphone { on: false });
        assert!(played.dock().voice.muted, "the microphone stayed on");
        played.ui(crate::events::UiEvent::Microphone { on: true });
        assert!(!played.dock().voice.muted, "the microphone stayed off");

        played.hq(H::StopVoice);
        let dock = played.dock();
        assert_eq!(dock.voice.stage, HqVoiceStage::Off);
        assert_eq!(dock.voice.cards.len(), 2);
        assert_eq!(played.scripted.voice().session, None);
    }

    /// The same session in French (as the walk's French pass starts it):
    /// the agent speaks the session's language, and its cards say so.
    #[test]
    fn the_voice_room_speaks_the_sessions_language() {
        use crate::hq::HqAction as H;

        district_l10n::with_locale(Locale::FrCa, || {
            let mut played = Played::new(Scene::SignedIn);
            played.open(crate::nav::NavDestination::Hq);
            played.hq(H::StartVoice);
            played.hq(H::AcceptVoiceDisclosure);
            let page = played.page();
            assert_eq!(
                page.voice.line.as_deref(),
                Some("À l'écoute. Parlez à District HQ.")
            );
            assert!(spoken(&page)[1].0.starts_with("J'ai préparé"));
            assert!(page.voice.cards[1].summary.contains("réceptionniste"));
        });
    }

    /// The voice-ends scene: a Canadian workspace, so the statement has its
    /// region line; the agent leaves once it has proposed, so the session
    /// ends, says why and keeps its two cards; and a call placed then shows
    /// its strip and live transcript with the dock and its cards beside it.
    #[test]
    fn in_the_voice_ends_scene_a_call_goes_beside_the_cards_left() {
        use crate::hq::{HqAction as H, HqVoiceStage};
        use crate::nav::NavDestination as D;

        let mut played = Played::new(Scene::VoiceEnds);
        played.open(D::Hq);
        played.hq(H::StartVoice);
        let disclosure = played
            .page()
            .voice_disclosure
            .expect("the residency statement");
        assert!(disclosure.region_notice.is_some(), "{disclosure:?}");

        played.hq(H::AcceptVoiceDisclosure);
        let page = played.page();
        assert_eq!(page.voice.stage, HqVoiceStage::Ended);
        assert!(page.voice.line_is_problem && page.voice.can_dismiss);
        assert_eq!(page.voice.cards.len(), 2);
        assert_eq!(spoken(&page).len(), 2);
        assert_eq!(played.scripted.voice().session, None, "the room was left");

        played.open(D::Calls);
        assert_eq!(unsettled(&screen_view(&played.model)), None);
        played.ui(crate::events::UiEvent::CallNumber {
            number: "+12125550142".to_owned(),
        });
        let shell = shell_view(&played.model);
        let call = serde_json::to_value(shell.call.expect("the call strip")).unwrap();
        assert_eq!(call["transcript"]["phase"], "Live", "{call}");
        let dock = shell.voice_dock.expect("the voice dock");
        assert_eq!(dock.voice.stage, HqVoiceStage::Ended);
        assert_eq!(dock.voice.cards.len(), 2);
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
    fn the_failing_reads_scene_is_named_on_the_command_line() {
        assert_eq!(
            scene([format!("{SCENE_ARG}signed-in-failing-reads")]),
            Ok(Some(Scene::FailingReads))
        );
    }

    /// The failing-reads scene, walked as the UI walk walks it: the working
    /// hours and both their editors, an event
    /// type's hosts, a webhook's deliveries. Each dialog's own read fails once
    /// and offers "Try again", which reads it; every section's own read is
    /// answered.
    #[test]
    fn each_dialogs_read_fails_once_and_try_again_reads_it() {
        use crate::events::UiEvent;
        use crate::scheduling_admin::developer::{DeveloperAction as D, DeveloperTabView};
        use crate::scheduling_admin::event_types::EventTypeAction as E;
        use crate::scheduling_admin::hours::HoursAction as H;
        use crate::scheduling_admin::{
            SchedulingAdminAction as A, SchedulingAdminPage as P, SchedulingAdminSection,
        };
        use crate::views::LoadStatus;

        let scripted = Scripted::new(Scene::FailingReads, unbounded_channel().0);
        let settle = |model: &mut Model, mut pending: Vec<Effect>| {
            while let Some(effect) = pending.pop() {
                if let Some(event) = scripted.answer(effect) {
                    pending.extend(model.update(event));
                }
            }
        };
        let (mut model, first) = Model::new(CoreConfig {
            web_base_url: "https://www.distronode.com".to_owned(),
            app_version: "0.1.0".to_owned(),
            calls_available: false,
            in_app_purchases: true,
            locale: district_core::Locale::En,
        });
        settle(&mut model, first);
        let mut run = |action: A| {
            let mut pending = Vec::new();
            for event in (UiEvent::SchedulingAdmin { action }).events() {
                pending.extend(model.update(event));
            }
            settle(&mut model, pending);
            let ScreenView::SchedulingAdmin { view } = screen_view(&model) else {
                panic!("not the scheduling administration");
            };
            view.page
        };
        let failed = |status: &LoadStatus| matches!(status, LoadStatus::Failed { failure, .. } if failure.retryable);

        // The working hours, from the booking pages: the section's own reads land.
        let hours = |action| A::Hours { action };
        let P::Hours { view } = run(A::Open {
            section: SchedulingAdminSection::Hours,
        }) else {
            panic!("not the working hours");
        };
        assert_eq!(view.status, LoadStatus::Ready);
        assert_eq!(view.overrides_status, LoadStatus::Ready);

        // The week's editor: its read fails, Try again reads it.
        let P::Hours { view } = run(hours(H::OpenWeek)) else {
            panic!("not the working hours");
        };
        assert!(failed(&view.week_editor.expect("open").status));
        let P::Hours { view } = run(hours(H::RetryRead)) else {
            panic!("not the working hours");
        };
        let week = view.week_editor.expect("open");
        assert_eq!(week.status, LoadStatus::Ready);
        assert_eq!(week.days.len(), 7);
        let _ = run(hours(H::CloseDialog));

        // The overrides' editor, the same.
        let P::Hours { view } = run(hours(H::OpenOverrides)) else {
            panic!("not the working hours");
        };
        assert!(failed(&view.overrides_editor.expect("open").status));
        let P::Hours { view } = run(hours(H::RetryRead)) else {
            panic!("not the working hours");
        };
        let editor = view.overrides_editor.expect("open");
        assert_eq!(editor.status, LoadStatus::Ready);
        assert!(!editor.rows.is_empty());
        let _ = run(hours(H::CloseDialog));

        // Opening the editors again reads them at once.
        let P::Hours { view } = run(hours(H::OpenWeek)) else {
            panic!("not the working hours");
        };
        assert_eq!(view.week_editor.expect("open").status, LoadStatus::Ready);
        let _ = run(hours(H::CloseDialog));

        // An event type's hosts: the page's own read lands, the dialog's fails.
        let P::EventTypes { view } = run(A::Open {
            section: SchedulingAdminSection::EventTypes,
        }) else {
            panic!("not the event types");
        };
        let detail = |action| A::EventType { action };
        let P::EventType { view } = run(A::OpenEventType {
            slug: view.rows[0].slug.clone(),
        }) else {
            panic!("not an event type");
        };
        assert_eq!(view.hosts_status, LoadStatus::Ready);
        let P::EventType { view } = run(detail(E::OpenHosts)) else {
            panic!("not an event type");
        };
        assert!(failed(&view.hosts_dialog.expect("open").status));
        let P::EventType { view } = run(detail(E::RetryDialogRead)) else {
            panic!("not an event type");
        };
        let dialog = view.hosts_dialog.expect("open");
        assert_eq!(dialog.status, LoadStatus::Ready);
        assert_eq!(dialog.candidates_status, LoadStatus::Ready);
        assert!(!dialog.hosts.is_empty() && !dialog.candidates.is_empty());
        let _ = run(detail(E::CloseDialog));

        // A webhook's deliveries: the first read fails, Try again reads them.
        let _ = run(A::Open {
            section: SchedulingAdminSection::Developer,
        });
        let developer = |action| A::Developer { action };
        let _ = run(developer(D::SelectTab {
            tab: DeveloperTabView::Webhooks,
        }));
        let P::Developer { view } = run(developer(D::ToggleDeliveries {
            webhook_id: "wh-contract-1".to_owned(),
        })) else {
            panic!("not developer");
        };
        assert!(failed(&view.deliveries.expect("open").status));
        let P::Developer { view } = run(developer(D::RetryDeliveries)) else {
            panic!("not developer");
        };
        let deliveries = view.deliveries.expect("open");
        assert_eq!(deliveries.status, LoadStatus::Ready);
        assert_eq!(deliveries.rows.len(), 2);
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
            Route::SchedulingAdmin(district_core::SchedulingSection::Developer),
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
        // The scene's one other active member can take it.
        let hosts: Vec<String> = view
            .reassign
            .expect("the host dialog")
            .hosts
            .into_iter()
            .map(|host| host.value)
            .collect();
        assert_eq!(hosts, ["sched-user-scripted"]);
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
        // The scene's one active member who is not a host is offered, and
        // added; a priority that is not a whole number is refused at the
        // save, and the host is taken off again.
        let candidates: Vec<&str> = hosts.candidates.iter().map(|c| c.value.as_str()).collect();
        assert_eq!(candidates, ["sched-user-scripted"]);
        let hosts_of = |page: P| match page {
            P::EventType { view } => view.hosts_dialog.expect("the hosts"),
            other => panic!("not an event type: {other:?}"),
        };
        let added = hosts_of(run(detail(E::AddHost {
            user_id: "sched-user-scripted".to_owned(),
        })));
        assert_eq!(added.hosts.len(), 3);
        assert_eq!(added.candidates, []);
        let _ = run(detail(E::SetHostPriority {
            user_id: "sched-user-scripted".to_owned(),
            priority: "first".to_owned(),
        }));
        let refused = hosts_of(run(detail(E::SaveHosts)));
        assert!(refused.validation.is_some());
        let removed = hosts_of(run(detail(E::RemoveHost {
            user_id: "sched-user-scripted".to_owned(),
        })));
        assert_eq!(removed.hosts.len(), 2);
        let P::EventType { view } = run(detail(E::SaveHosts)) else {
            panic!("not an event type");
        };
        assert_eq!(view.hosts_dialog, None);

        let _ = run(detail(E::OpenQuestions));
        // The first question is a choice: its form opens on its options, and
        // one is taken off.
        let options = |page: P| match page {
            P::EventType { view } => {
                view.questions_dialog
                    .and_then(|q| q.form)
                    .expect("the question form")
                    .form
                    .options
            }
            other => panic!("not an event type: {other:?}"),
        };
        let held = options(run(detail(E::EditQuestion {
            id: "q-contract-1".to_owned(),
        })));
        assert_eq!(held.len(), 3);
        let left = options(run(detail(E::RemoveOption {
            option: held[0].clone(),
        })));
        assert_eq!(left, held[1..]);
        let _ = run(detail(E::CancelQuestion));
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
        // What the UI walk does with the rows: Monday has no window, and the
        // one weekday with a window offers to copy it.
        assert_eq!(week.days[0].ranges, []);
        let source = week
            .days
            .iter()
            .find(|day| day.offers_copy)
            .expect("a day offers its windows")
            .clone();
        assert!(source.day > 0 && source.day < 5, "{source:?}");
        let week = run(hours(H::AddRange { day: 0 }))
            .week_editor
            .expect("still open");
        let key = week.days[0].ranges[0].key;
        let start = |value: &str| H::SetRangeStart {
            day: 0,
            key,
            value: value.to_owned(),
        };
        let week = run(hours(start("nine"))).week_editor.expect("still open");
        assert!(week.days[0].ranges[0].error.is_some());
        assert!(!week.dialog.can_confirm);
        let week = run(hours(start("08:30"))).week_editor.expect("still open");
        assert_eq!(week.days[0].ranges[0].error, None);
        let week = run(hours(H::SetRangeEnd {
            day: 0,
            key,
            value: "8".to_owned(),
        }))
        .week_editor
        .expect("still open");
        assert!(week.days[0].ranges[0].error.is_some());
        let week = run(hours(H::RemoveRange { day: 0, key }))
            .week_editor
            .expect("still open");
        assert_eq!(week.days[0].ranges, []);
        assert!(week.dialog.can_confirm);
        let week = run(hours(H::CopyToWeekdays { day: source.day }))
            .week_editor
            .expect("still open");
        assert_eq!(week.days[0].ranges.len(), source.ranges.len());
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

    /// The settings open from the booking pages on the booking page and fill
    /// in; each tab reads and each dialog's save lands: the booking page (and
    /// its logo removed), the assistant, the profile and the notifications.
    #[test]
    fn the_settings_fill_in_and_their_saves_land() {
        use crate::events::UiEvent;
        use crate::scheduling_admin::settings::{
            SchedulingImageTarget, SchedulingSettingsAction as S, SchedulingSettingsTab as Tab,
        };
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
            let P::Settings { view } = view.page else {
                panic!("not the settings");
            };
            view
        };
        let view = send(UiEvent::SchedulingAdmin {
            action: A::Open {
                section: SchedulingAdminSection::Settings,
            },
        });
        let mut run = |action: S| {
            send(UiEvent::SchedulingAdmin {
                action: A::Settings { action },
            })
        };
        assert_eq!(view.status, crate::views::LoadStatus::Ready);
        assert_eq!(view.groups[0].rows[0].value, "Contract Agency");
        assert!(view.offers_edit);

        let view = run(S::OpenEditor {
            tab: Tab::BookingPage,
        });
        assert!(view.branding.expect("open").logo.shows_remove);
        let view = run(S::SaveBranding);
        assert!(view.branding.expect("still open").notice.is_some());
        let view = run(S::RemoveImage {
            target: SchedulingImageTarget::Logo,
        });
        assert!(view.branding.expect("still open").logo.notice.is_some());
        assert_eq!(run(S::CloseDialog).branding, None);

        let view = run(S::SelectTab {
            tab: Tab::Assistant,
        });
        assert_eq!(view.groups[0].rows[0].value, "On");
        let _ = run(S::OpenEditor {
            tab: Tab::Assistant,
        });
        let _ = run(S::EditAssistant {
            enabled: false,
            instructions: "Always list the agreed next steps first.".to_owned(),
        });
        let view = run(S::SaveAssistant);
        assert!(view.assistant.expect("still open").notice.is_some());
        let _ = run(S::CloseDialog);

        let view = run(S::SelectTab { tab: Tab::Profile });
        assert_eq!(view.groups[0].rows[0].value, "Contract Member");
        let _ = run(S::OpenEditor { tab: Tab::Profile });
        let view = run(S::SaveProfile);
        assert!(view.profile.expect("still open").notice.is_some());
        let _ = run(S::CloseDialog);

        let _ = run(S::SelectTab {
            tab: Tab::Notifications,
        });
        let _ = run(S::OpenEditor {
            tab: Tab::Notifications,
        });
        let _ = run(S::SetNotification {
            field: "notify_reschedule".to_owned(),
            on: true,
        });
        let view = run(S::SaveNotifications);
        assert!(view.notifications.expect("still open").notice.is_some());
    }

    /// Developer opens from the booking pages and fills in; a key is made
    /// and shown once, and a key is revoked, asked once more; on the apps
    /// tab the MCP address shows and an app is revoked; on the webhooks tab a
    /// webhook's deliveries open, a webhook's events are saved, one is added
    /// and its signing secret shown once, and one is deleted.
    #[test]
    fn developer_fills_in_and_its_writes_land() {
        use crate::events::UiEvent;
        use crate::scheduling_admin::developer::{DeveloperAction as D, DeveloperTabView};
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
            let P::Developer { view } = view.page else {
                panic!("not Developer");
            };
            view
        };
        let view = send(UiEvent::SchedulingAdmin {
            action: A::Open {
                section: SchedulingAdminSection::Developer,
            },
        });
        let mut run = |action: D| {
            send(UiEvent::SchedulingAdmin {
                action: A::Developer { action },
            })
        };
        assert_eq!(view.keys_status, crate::views::LoadStatus::Ready);
        assert_eq!(view.keys.len(), 2);
        assert!(view.offers_writes);

        let _ = run(D::OpenCreateKey);
        let _ = run(D::EditKeyName {
            name: "Zapier".to_owned(),
        });
        let view = run(D::CreateKey);
        let made = view.key_dialog.expect("still open");
        let shown = made.minted.expect("the key, shown once");
        assert!(!shown.value.is_empty());
        let view = run(D::KeyCopied);
        assert_eq!(
            view.key_dialog
                .expect("open")
                .minted
                .expect("shown")
                .copy_label,
            "Copied"
        );
        assert_eq!(run(D::CloseDialog).key_dialog, None);

        let view = run(D::AskRevokeKey {
            key_id: "key-contract-2".to_owned(),
        });
        assert!(view.removal.is_some());
        let view = run(D::ConfirmRemoval);
        assert!(view.removal.is_none() && !view.removing);
        assert_eq!(view.removal_failure, None);

        let view = run(D::SelectTab {
            tab: DeveloperTabView::Apps,
        });
        assert_eq!(view.apps_status, crate::views::LoadStatus::Ready);
        assert!(
            view.mcp.is_some(),
            "the scene's scheduler has a public address"
        );
        let _ = run(D::AskRevokeApp {
            connection_id: "oauth-contract-1".to_owned(),
        });
        let view = run(D::ConfirmRemoval);
        assert!(view.removal.is_none() && view.removal_failure.is_none());

        let view = run(D::SelectTab {
            tab: DeveloperTabView::Webhooks,
        });
        assert_eq!(view.webhooks.len(), 2);
        let view = run(D::ToggleDeliveries {
            webhook_id: "wh-contract-1".to_owned(),
        });
        let deliveries = view.deliveries.expect("open");
        assert_eq!(deliveries.status, crate::views::LoadStatus::Ready);
        assert_eq!(deliveries.rows.len(), 2);
        // Closed again before the save: a save reads the webhooks again and
        // forgets every webhook's deliveries, but the core leaves an open one
        // open, its deliveries loading with nothing on its way.
        let view = run(D::ToggleDeliveries {
            webhook_id: "wh-contract-1".to_owned(),
        });
        assert_eq!(view.deliveries, None);
        let _ = run(D::OpenEditWebhook {
            webhook_id: "wh-contract-1".to_owned(),
        });
        let _ = run(D::ToggleEvent {
            event: "booking.rescheduled".to_owned(),
        });
        let view = run(D::SaveWebhook);
        assert_eq!(
            view.webhook_dialog.expect("still open").notice.as_deref(),
            Some("Webhook updated")
        );
        let _ = run(D::CloseDialog);
        let _ = run(D::OpenCreateWebhook);
        let _ = run(D::EditWebhookUrl {
            url: "https://hooks.example.com/bookings".to_owned(),
        });
        let _ = run(D::ToggleEvent {
            event: "booking.created".to_owned(),
        });
        let view = run(D::SaveWebhook);
        let added = view.webhook_dialog.expect("still open");
        assert!(added.minted.is_some_and(|secret| !secret.value.is_empty()));
        let _ = run(D::CloseDialog);
        let _ = run(D::AskDeleteWebhook {
            webhook_id: "wh-contract-2".to_owned(),
        });
        let view = run(D::ConfirmRemoval);
        assert!(view.removal.is_none() && view.removal_failure.is_none());
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

    /// The carrier accounts' row controls, as the UI walk presses them: a
    /// channel's sender is changed and a default made, each landing; Edit
    /// opens the form on the account; Remove asks first, and the removal
    /// lands.
    #[test]
    fn the_messaging_writes_land() {
        use crate::settings::messaging::MessagingAction as M;
        let mut model = started();
        let _ = after(
            &mut model,
            Event::Navigate(Route::Workspace(district_core::WorkspaceSection::Messaging)),
        );
        let mut run = |action: M| {
            let mut pending = Vec::new();
            for event in (crate::events::UiEvent::Messaging { action }).events() {
                pending.extend(model.update(event));
            }
            settle(&mut model, pending);
            let screen = screen_view(&model);
            assert_eq!(unsettled(&screen), None, "{screen:?}");
            let ScreenView::Messaging { view } = screen else {
                panic!("not the carrier accounts: {screen:?}");
            };
            view
        };
        let view = run(M::Open);
        let first = view.accounts[0].clone();
        assert!(first.offers_make_default && first.offers_edit && first.offers_remove);
        assert_eq!(first.id, "acct-twilio");
        // The first channel's picker offers another account than its own.
        let channel = view.channels[0].clone();
        assert!(view.channels_editable);
        let other = channel
            .picker
            .choices
            .iter()
            .find(|choice| !choice.value.is_empty() && choice.value != channel.picker.selected)
            .expect("another account to send from")
            .value
            .clone();
        let view = run(M::SetChannelSender {
            channel: channel.channel,
            account_id: other,
        });
        assert!(view.notice.is_some_and(|notice| notice.saved));
        let _ = run(M::DismissNotice);
        let view = run(M::MakeDefault {
            account_id: first.id.clone(),
        });
        assert!(view.notice.is_some_and(|notice| notice.saved));
        let view = run(M::StartEdit {
            account_id: first.id.clone(),
        });
        assert!(view.form.is_some());
        let view = run(M::CloseForm);
        assert!(view.form.is_none());
        let view = run(M::AskRemove {
            account_id: first.id.clone(),
        });
        assert!(view.confirm.is_some());
        let view = run(M::ConfirmRemove);
        assert!(view.confirm.is_none());
        assert!(view.notice.is_some_and(|notice| notice.saved));
    }

    /// A workflow's runs open on the recorded page, which offers more; the
    /// next page adds runs of its own and is the last. A switch's change is
    /// sent and goes unanswered, so the switch waits for it.
    #[test]
    fn a_workflows_runs_page_on_and_its_switch_waits() {
        use crate::workflows::WorkflowsAction as W;
        let mut model = started();
        let mut run = |action: W| {
            let mut pending = Vec::new();
            for event in (crate::events::UiEvent::Workflows { action }).events() {
                pending.extend(model.update(event));
            }
            settle(&mut model, pending);
            let screen = screen_view(&model);
            assert_eq!(unsettled(&screen), None, "{screen:?}");
            let ScreenView::Workflows { view } = screen else {
                panic!("not the workflows: {screen:?}");
            };
            view
        };
        let view = run(W::Open);
        let first = view.rows[0].workflow_id.clone();
        let view = run(W::ToggleExpanded {
            workflow_id: first.clone(),
        });
        let runs = view.rows[0].runs.clone().expect("the runs");
        assert_eq!(runs.runs.len(), 4);
        assert!(runs.can_load_more);
        let view = run(W::LoadMoreRuns {
            workflow_id: first.clone(),
        });
        let runs = view.rows[0].runs.clone().expect("the runs");
        assert_eq!(runs.runs.len(), 8);
        assert!(!runs.can_load_more);
        let second = view.rows[1].clone();
        assert!(second.can_switch);
        let view = run(W::SetActive {
            workflow_id: second.workflow_id.clone(),
            active: !second.active,
        });
        assert_eq!(view.rows[1].active, !second.active);
        assert!(view.rows[1].switching && !view.rows[1].can_switch);
    }

    /// Voice Studio's Voice part has a slider left at the service's default,
    /// behind Advanced, which the UI walk sets: the slider then holds a value.
    #[test]
    fn voice_studio_offers_a_slider_left_at_its_default() {
        use crate::settings::voice_studio::{
            StudioControlView as C, StudioEditorView, VoiceStudioAction,
        };
        let mut model = started();
        let _ = after(
            &mut model,
            Event::Navigate(Route::Workspace(district_core::WorkspaceSection::Hub)),
        );
        let ScreenView::VoiceStudio { view } = after(
            &mut model,
            Event::Navigate(Route::Workspace(
                district_core::WorkspaceSection::VoiceStudio,
            )),
        ) else {
            panic!("Voice Studio opens");
        };
        // The walk opens the Voice part as the chain's last block.
        let blocks = view.studio.expect("read").blocks;
        assert_eq!(blocks.last().map(|block| block.leg.as_str()), Some("tts"));
        let mut run = |action: VoiceStudioAction| -> StudioEditorView {
            let mut pending = Vec::new();
            for event in (crate::events::UiEvent::VoiceStudio { action }).events() {
                pending.extend(model.update(event));
            }
            settle(&mut model, pending);
            let ScreenView::VoiceStudio { view } = screen_view(&model) else {
                panic!("Voice Studio stays open");
            };
            view.studio.expect("read").editor
        };
        let editor = run(VoiceStudioAction::SelectLeg {
            leg: "tts".to_owned(),
        });
        let (key, start) = editor
            .controls
            .iter()
            .find_map(|control| match &control.control {
                C::Slider {
                    value: None,
                    default_label: Some(_),
                    start,
                    ..
                } if control.advanced => Some((control.key.clone(), *start)),
                _ => None,
            })
            .expect("a slider at its default, behind Advanced");
        let editor = run(VoiceStudioAction::SetNumber {
            key: key.clone(),
            value: Some(start),
        });
        let set = editor
            .controls
            .iter()
            .find(|control| control.key == key)
            .expect("the slider");
        assert!(
            matches!(set.control, C::Slider { value: Some(value), .. } if value == start),
            "{set:?}"
        );
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
