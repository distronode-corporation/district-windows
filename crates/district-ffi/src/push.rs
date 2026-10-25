//! Windows push (WNS): ringing and messages when the app is closed.
//!
//! The app asks the Windows App SDK for a channel
//! (`PushNotificationManager.CreateChannelAsync`, with the WNS app's service
//! principal as the remote id) and registers its URI with the service beside
//! this desktop's presence: `POST devices/register` with
//! `{token, platform: "windows", kind: "wns"}`
//! ([`Core::register_push_channel`](crate::Core::register_push_channel)), at
//! sign-in and whenever Windows renews the channel. The core's sign-out
//! unregisters every kind this installation holds, the channel's included; its
//! other unregistrations (sleep, quit, "ring on this computer" off) remove the
//! presence alone (`{"kind": "desktop"}`), so a closed app can still be
//! woken. district-core-rust 4.0.0-beta.3 makes that split; a service that
//! does not yet read the `kind` ignores it and removes every kind.
//!
//! The service sends a raw push holding ids only: `{type: "incoming_call",
//! callId, workspaceId}`, `{type: "call_ring_cancelled", callId, workspaceId,
//! reason}` or `{type: "message", messageId, workspaceId}` ([`decode_push`]).
//! Nothing about the caller or the message crosses Microsoft. The core takes
//! the push as the live socket's own event for that id: a call rings (the
//! toast with Answer and Decline, and the ringtone), a ring the service ended
//! stops (the ringtone, the banner's Answer and the toast), a message is read
//! under the app's own bearer and notified. A push that arrives before the
//! session is ready waits for it, for as long as it can still mean something
//! ([`PushMessage::shelf_life`]); a ring's end that arrives while its ring is
//! still waiting takes the ring away with it ([`PushMessage::waiting`]).
//!
//! Until Microsoft maps the package to the WNS app, asking for a channel
//! fails; the app then works exactly as it does without push.

use std::fmt;
use std::time::{Duration, Instant};

use district_api::{ApiClient, ApiError, Endpoint, TokenSource};
use district_core::RING_DEADLINE;
use district_live::{LiveUpdate, WorkspaceUpdate};
use district_model::{PushRegistrationResponse, TelemetryEnvelope, TelemetryEventType};
use serde::{Deserialize, Serialize};
use serde_json::json;
use url::Url;

/// The domain every WNS channel URI is on. The service refuses any other, and
/// sends our WNS credential to the URI, so the app never registers one.
const CHANNEL_DOMAIN: &str = "notify.windows.com";

/// How long a message push waits for the session: a notification about a
/// message older than this is noise, and the inbox shows it anyway.
const MESSAGE_SHELF_LIFE: Duration = Duration::from_secs(120);

/// What a push says happened. Ids only.
#[derive(Clone, Debug, PartialEq, Eq, uniffi::Enum)]
pub enum PushMessage {
    /// A call is ringing for this member.
    IncomingCall {
        /// The call.
        call_id: String,
        /// Its workspace.
        workspace_id: String,
    },
    /// A ring the service started for this member has ended: answered on
    /// some device, timed out, the caller gone or withdrawn. The core stops
    /// it only where it still rings for that call, never a call answered here.
    RingCancelled {
        /// The call.
        call_id: String,
        /// Its workspace.
        workspace_id: String,
        /// Why, as the service wrote it (`answered`, `timeout`, `call_ended`,
        /// `withdrawn`). The core reads a word it does not know, or none, as
        /// `withdrawn`.
        reason: String,
    },
    /// A message arrived.
    Message {
        /// The message.
        message_id: String,
        /// Its workspace.
        workspace_id: String,
    },
}

/// The raw push's body, as the service writes it. Other keys (`category`)
/// are left alone.
#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct RawPush {
    #[serde(rename = "type")]
    kind: String,
    workspace_id: Option<String>,
    call_id: Option<String>,
    message_id: Option<String>,
    reason: Option<String>,
}

/// What the raw push `payload` says, or `None` for anything else: not JSON, a
/// type this build does not know, or an id missing or blank.
#[uniffi::export]
pub fn decode_push(payload: &str) -> Option<PushMessage> {
    let raw: RawPush = serde_json::from_str(payload).ok()?;
    let id = |value: Option<String>| value.filter(|value| !value.trim().is_empty());
    let workspace_id = id(raw.workspace_id)?;
    match raw.kind.as_str() {
        "incoming_call" => Some(PushMessage::IncomingCall {
            call_id: id(raw.call_id)?,
            workspace_id,
        }),
        "call_ring_cancelled" => Some(PushMessage::RingCancelled {
            call_id: id(raw.call_id)?,
            workspace_id,
            reason: raw.reason.unwrap_or_default(),
        }),
        "message" => Some(PushMessage::Message {
            message_id: id(raw.message_id)?,
            workspace_id,
        }),
        _ => None,
    }
}

impl PushMessage {
    /// How long the push may wait for the session before it means nothing: a
    /// ring past the service's window would ring for a call already handed
    /// back to the receptionist.
    pub(crate) fn shelf_life(&self) -> Duration {
        match self {
            Self::IncomingCall { .. } | Self::RingCancelled { .. } => RING_DEADLINE,
            Self::Message { .. } => MESSAGE_SHELF_LIFE,
        }
    }

    /// Whether it may be handed to the model before "ring on this computer"
    /// has been read: everything but a ring, which the core would refuse as
    /// if the setting were off.
    pub(crate) fn needs_ring_setting(&self) -> bool {
        matches!(self, Self::IncomingCall { .. })
    }

    /// The push left waiting for the session once `push` arrives, beside
    /// `pending`, the one already waiting (one waits at a time). A ring's end
    /// never displaces another push: while one waits nothing can be ringing
    /// here, so the end means something only for the ring waiting, which it
    /// takes away when it is for the same call, so that a call answered
    /// elsewhere before the app was ready never rings. Windows does not keep
    /// pushes in order, so a ring that arrives after its own end does not
    /// replace it either, and never rings.
    pub(crate) fn waiting(
        pending: Option<(PushMessage, Instant)>,
        push: PushMessage,
        now: Instant,
    ) -> Option<(PushMessage, Instant)> {
        match (pending, push) {
            (
                Some((
                    Self::IncomingCall {
                        call_id: ringing, ..
                    },
                    _,
                )),
                Self::RingCancelled { call_id, .. },
            ) if ringing == call_id => None,
            (Some(pending), Self::RingCancelled { .. }) => Some(pending),
            (
                Some((ended @ Self::RingCancelled { .. }, arrived)),
                Self::IncomingCall { call_id, .. },
            ) if ended.call_id() == Some(call_id.as_str()) => Some((ended, arrived)),
            (_, push) => Some((push, now)),
        }
    }

    /// The call it is about, or `None` for a message.
    fn call_id(&self) -> Option<&str> {
        match self {
            Self::IncomingCall { call_id, .. } | Self::RingCancelled { call_id, .. } => {
                Some(call_id)
            }
            Self::Message { .. } => None,
        }
    }

    /// The push as the live socket's own event, for the member `user_id`: a
    /// `call_ringing` naming them, a `call_ring_cancelled` with its reason, or
    /// a `message_received` for the message.
    /// The core then rings, or reads and notifies, as it does for the socket,
    /// with every check it makes there (the role, "ring on this computer", a
    /// ring already live).
    pub(crate) fn live_update(self, user_id: &str) -> WorkspaceUpdate {
        let (workspace_id, id, event_type, data) = match self {
            Self::IncomingCall {
                call_id,
                workspace_id,
            } => {
                let data = json!({ "callId": call_id, "userIds": [user_id] });
                (workspace_id, call_id, TelemetryEventType::CallRinging, data)
            }
            Self::RingCancelled {
                call_id,
                workspace_id,
                reason,
            } => {
                let data = json!({ "callId": call_id, "reason": reason });
                (
                    workspace_id,
                    call_id,
                    TelemetryEventType::CallRingCancelled,
                    data,
                )
            }
            Self::Message {
                message_id,
                workspace_id,
            } => {
                let data = json!({ "messageId": message_id });
                (
                    workspace_id,
                    message_id,
                    TelemetryEventType::MessageReceived,
                    data,
                )
            }
        };
        WorkspaceUpdate {
            workspace_id: workspace_id.clone(),
            update: LiveUpdate::Event(TelemetryEnvelope {
                workspace_id,
                // For the message events, the message's id.
                call_id: id,
                event_type,
                data,
                timestamp: String::new(),
            }),
        }
    }
}

/// Whether `uri` is a WNS channel: https, on `notify.windows.com` or under it.
pub(crate) fn is_channel_uri(uri: &str) -> bool {
    let Ok(url) = Url::parse(uri) else {
        return false;
    };
    url.scheme() == "https"
        && url.host_str().is_some_and(|host| {
            host == CHANNEL_DOMAIN || host.ends_with(&format!(".{CHANNEL_DOMAIN}"))
        })
}

/// The body of `POST devices/register` for the channel. The installation is
/// the bearer's, never the body's.
#[derive(Clone, PartialEq, Eq, Serialize)]
pub(crate) struct WnsRegistration {
    token: String,
    platform: &'static str,
    kind: &'static str,
}

impl WnsRegistration {
    pub(crate) fn new(channel_uri: String) -> Self {
        Self {
            token: channel_uri,
            platform: "windows",
            kind: "wns",
        }
    }
}

/// The channel URI addresses this installation, so `Debug` leaves it out.
impl fmt::Debug for WnsRegistration {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.debug_struct("WnsRegistration")
            .field("token", &"<redacted>")
            .field("platform", &self.platform)
            .field("kind", &self.kind)
            .finish()
    }
}

/// How registering the channel went. The app logs anything but `Registered`
/// and carries on: push is an addition, never a condition.
#[derive(Clone, Debug, PartialEq, Eq, uniffi::Enum)]
pub enum PushRegistration {
    /// The service took it.
    Registered,
    /// Not a WNS channel URI: nothing was sent.
    NotAChannel,
    /// The core is not running, or no one is signed in.
    NotStarted,
    /// The service refused it, or could not be reached.
    Failed {
        /// Why, for the log. Never the channel URI.
        reason: String,
    },
}

/// Registers a channel with the service.
#[async_trait::async_trait]
pub(crate) trait ChannelRegistrar: Send + Sync + 'static {
    async fn register(&self, registration: &WnsRegistration) -> Result<(), ApiError>;
}

#[async_trait::async_trait]
impl<S: TokenSource + Send + Sync + 'static> ChannelRegistrar for ApiClient<S> {
    async fn register(&self, registration: &WnsRegistration) -> Result<(), ApiError> {
        let answer: PushRegistrationResponse = self
            .request(Endpoint::PushRegister)
            .json(registration)
            .send()
            .await?;
        if answer.success {
            Ok(())
        } else {
            Err(ApiError::Unconfirmed {
                endpoint: Endpoint::PushRegister,
            })
        }
    }
}

/// Registers `channel_uri` through `registrar`, after checking it is one.
pub(crate) async fn register_channel(
    registrar: &dyn ChannelRegistrar,
    channel_uri: String,
) -> PushRegistration {
    if !is_channel_uri(&channel_uri) {
        return PushRegistration::NotAChannel;
    }
    match registrar.register(&WnsRegistration::new(channel_uri)).await {
        Ok(()) => PushRegistration::Registered,
        Err(error) => PushRegistration::Failed {
            reason: error.to_string(),
        },
    }
}

#[cfg(test)]
mod tests {
    use std::sync::Mutex;

    use super::*;

    #[test]
    fn a_rings_end_decodes_with_its_reason() {
        assert_eq!(
            decode_push(
                r#"{"type":"call_ring_cancelled","workspaceId":"ws-1","callId":"call-1","reason":"answered"}"#
            ),
            Some(PushMessage::RingCancelled {
                call_id: "call-1".to_owned(),
                workspace_id: "ws-1".to_owned(),
                reason: "answered".to_owned(),
            })
        );
        // No reason is the core's `withdrawn`; it is still a ring's end.
        assert_eq!(
            decode_push(r#"{"type":"call_ring_cancelled","workspaceId":"ws-1","callId":"call-1"}"#),
            Some(PushMessage::RingCancelled {
                call_id: "call-1".to_owned(),
                workspace_id: "ws-1".to_owned(),
                reason: String::new(),
            })
        );
        for payload in [
            r#"{"type":"call_ring_cancelled","workspaceId":"ws-1","reason":"answered"}"#,
            r#"{"type":"call_ring_cancelled","callId":"call-1","reason":"answered"}"#,
            r#"{"type":"call_ring_cancelled","callId":"","workspaceId":"ws-1"}"#,
        ] {
            assert_eq!(decode_push(payload), None, "{payload}");
        }
    }

    #[test]
    fn a_rings_end_is_the_sockets_own_event() {
        let cancelled = PushMessage::RingCancelled {
            call_id: "call-1".to_owned(),
            workspace_id: "ws-1".to_owned(),
            reason: "timeout".to_owned(),
        };
        assert_eq!(cancelled.shelf_life(), RING_DEADLINE);
        assert!(!cancelled.needs_ring_setting());
        let update = cancelled.live_update("user-1");
        assert_eq!(update.workspace_id, "ws-1");
        let LiveUpdate::Event(envelope) = update.update else {
            panic!("not an event");
        };
        assert_eq!(envelope.event_type, TelemetryEventType::CallRingCancelled);
        assert_eq!(envelope.call_id, "call-1");
        assert_eq!(envelope.workspace_id, "ws-1");
        assert_eq!(
            envelope.ring_cancel_reason(),
            Some(district_model::RingCancelReason::Timeout)
        );
    }

    /// Every reason the service sends reaches the core as itself, `declined`
    /// (every member rung declined) included, so a ring declined everywhere
    /// is put away with no missed call; one this build does not know is the
    /// core's `Withdrawn`.
    #[test]
    fn a_rings_end_carries_its_reason_declined_included() {
        use district_model::RingCancelReason;

        for (reason, read) in [
            ("answered", RingCancelReason::Answered),
            ("timeout", RingCancelReason::Timeout),
            ("call_ended", RingCancelReason::CallEnded),
            ("declined", RingCancelReason::Declined),
            ("withdrawn", RingCancelReason::Withdrawn),
            ("parked", RingCancelReason::Withdrawn),
        ] {
            let payload = format!(
                r#"{{"type":"call_ring_cancelled","callId":"call-1","workspaceId":"ws-1","reason":"{reason}"}}"#
            );
            let message = decode_push(&payload).expect("a ring's end");
            let LiveUpdate::Event(envelope) = message.live_update("user-1").update else {
                panic!("not an event");
            };
            assert_eq!(envelope.ring_cancel_reason(), Some(read), "{reason}");
        }
    }

    #[test]
    fn a_rings_end_takes_its_waiting_ring_away_and_displaces_nothing_else() {
        let ring = |call_id: &str| PushMessage::IncomingCall {
            call_id: call_id.to_owned(),
            workspace_id: "ws-1".to_owned(),
        };
        let end = |call_id: &str| PushMessage::RingCancelled {
            call_id: call_id.to_owned(),
            workspace_id: "ws-1".to_owned(),
            reason: "answered".to_owned(),
        };
        let message = PushMessage::Message {
            message_id: "msg-1".to_owned(),
            workspace_id: "ws-1".to_owned(),
        };
        let then = Instant::now();
        let now = then + Duration::from_secs(1);
        assert!(ring("call-1").needs_ring_setting() && !message.needs_ring_setting());

        // The ring it ends goes with it.
        assert_eq!(
            PushMessage::waiting(Some((ring("call-1"), then)), end("call-1"), now),
            None
        );
        // Another call's ring, or a message, stays, as it arrived.
        assert_eq!(
            PushMessage::waiting(Some((ring("call-2"), then)), end("call-1"), now),
            Some((ring("call-2"), then))
        );
        assert_eq!(
            PushMessage::waiting(Some((message.clone(), then)), end("call-1"), now),
            Some((message.clone(), then))
        );
        // Nothing waiting: it waits, as any push does.
        assert_eq!(
            PushMessage::waiting(None, end("call-1"), now),
            Some((end("call-1"), now))
        );
        // A ring that arrives after its own end does not ring; another
        // call's ring replaces the end, which meant nothing for it.
        assert_eq!(
            PushMessage::waiting(Some((end("call-1"), then)), ring("call-1"), now),
            Some((end("call-1"), then))
        );
        assert_eq!(
            PushMessage::waiting(Some((end("call-1"), then)), ring("call-2"), now),
            Some((ring("call-2"), now))
        );
        // Any other push replaces what waited, as before.
        assert_eq!(
            PushMessage::waiting(Some((ring("call-1"), then)), message.clone(), now),
            Some((message.clone(), now))
        );
        assert_eq!(message.call_id(), None);
    }

    #[test]
    fn a_call_and_a_message_decode_and_nothing_else_does() {
        assert_eq!(
            decode_push(
                r#"{"type":"incoming_call","category":"call","callId":"call-1","workspaceId":"ws-1"}"#
            ),
            Some(PushMessage::IncomingCall {
                call_id: "call-1".to_owned(),
                workspace_id: "ws-1".to_owned(),
            })
        );
        assert_eq!(
            decode_push(r#"{"type":"message","messageId":"msg-1","workspaceId":"ws-1"}"#),
            Some(PushMessage::Message {
                message_id: "msg-1".to_owned(),
                workspace_id: "ws-1".to_owned(),
            })
        );
        for payload in [
            "",
            "not json",
            r#"{"type":"voicemail","workspaceId":"ws-1"}"#,
            r#"{"type":"incoming_call","callId":"call-1"}"#,
            r#"{"type":"incoming_call","callId":" ","workspaceId":"ws-1"}"#,
            r#"{"type":"message","workspaceId":"ws-1"}"#,
        ] {
            assert_eq!(decode_push(payload), None, "{payload}");
        }
    }

    #[test]
    fn a_push_is_the_sockets_own_event() {
        let ring = PushMessage::IncomingCall {
            call_id: "call-1".to_owned(),
            workspace_id: "ws-1".to_owned(),
        };
        assert_eq!(ring.shelf_life(), RING_DEADLINE);
        let update = ring.live_update("user-1");
        assert_eq!(update.workspace_id, "ws-1");
        let LiveUpdate::Event(envelope) = update.update else {
            panic!("not an event");
        };
        assert_eq!(envelope.event_type, TelemetryEventType::CallRinging);
        assert_eq!(envelope.call_id, "call-1");
        assert_eq!(envelope.data["userIds"], json!(["user-1"]));

        let message = PushMessage::Message {
            message_id: "msg-1".to_owned(),
            workspace_id: "ws-2".to_owned(),
        };
        assert_eq!(message.shelf_life(), MESSAGE_SHELF_LIFE);
        let LiveUpdate::Event(envelope) = message.live_update("user-1").update else {
            panic!("not an event");
        };
        assert_eq!(envelope.event_type, TelemetryEventType::MessageReceived);
        assert_eq!(envelope.call_id, "msg-1");
        assert_eq!(envelope.workspace_id, "ws-2");
    }

    #[test]
    fn only_a_wns_channel_is_a_channel() {
        assert!(is_channel_uri(
            "https://wns2-by3p.notify.windows.com/w/?token=AwYAAAB"
        ));
        assert!(is_channel_uri("https://notify.windows.com/w/"));
        for uri in [
            "http://wns2-by3p.notify.windows.com/w/",
            "https://notify.windows.com.example.com/w/",
            "https://evilnotify.windows.com/w/",
            "https://example.com/w/",
            "not a uri",
        ] {
            assert!(!is_channel_uri(uri), "{uri}");
        }
    }

    #[test]
    fn the_registration_never_shows_its_channel() {
        let registration =
            WnsRegistration::new("https://a.notify.windows.com/w/?token=secret".to_owned());
        assert!(!format!("{registration:?}").contains("secret"));
        assert_eq!(
            serde_json::to_value(&registration).unwrap(),
            json!({"token": "https://a.notify.windows.com/w/?token=secret", "platform": "windows", "kind": "wns"})
        );
    }

    /// Records what it was asked to register and answers `answer`.
    struct Fake {
        sent: Mutex<Vec<WnsRegistration>>,
        refuse: bool,
    }

    #[async_trait::async_trait]
    impl ChannelRegistrar for Fake {
        async fn register(&self, registration: &WnsRegistration) -> Result<(), ApiError> {
            self.sent.lock().unwrap().push(registration.clone());
            if self.refuse {
                Err(ApiError::Unconfirmed {
                    endpoint: Endpoint::PushRegister,
                })
            } else {
                Ok(())
            }
        }
    }

    #[tokio::test]
    async fn a_channel_is_registered_once_checked() {
        let fake = Fake {
            sent: Mutex::new(Vec::new()),
            refuse: false,
        };
        assert_eq!(
            register_channel(&fake, "https://example.com/w/".to_owned()).await,
            PushRegistration::NotAChannel
        );
        assert!(fake.sent.lock().unwrap().is_empty());
        assert_eq!(
            register_channel(&fake, "https://a.notify.windows.com/w/".to_owned()).await,
            PushRegistration::Registered
        );
        assert_eq!(fake.sent.lock().unwrap().len(), 1);

        let refusing = Fake {
            sent: Mutex::new(Vec::new()),
            refuse: true,
        };
        assert!(matches!(
            register_channel(&refusing, "https://a.notify.windows.com/w/".to_owned()).await,
            PushRegistration::Failed { .. }
        ));
    }
}
