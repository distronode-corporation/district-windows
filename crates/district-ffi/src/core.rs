//! The [`Core`] object: the model's actor, its snapshot, and the runner built
//! over the core's own adapters.

use std::future::Future;
use std::path::{Path, PathBuf};
use std::sync::{Arc, Mutex, MutexGuard, PoisonError};
use std::time::Duration;

use district_api::{ApiClient, ApiConfig};
use district_auth::{NativeAuthApi, TokenRefreshCoordinator};
use district_core::{
    CallEngine, Clock, CoreConfig, DesktopPresence, DistrictApi, Effect, EffectRunner, Event,
    LiveHub, LiveUpdates, Model, NativeAuth, Notifier, Presence, Restoring, RingSurface,
    SessionState, Settings, TokioClock, UrlOpener,
};
use district_host::{DeviceIdentity, RefreshMarkerFile, SettingsFile};
use district_live::LiveConfig;
use tokio::runtime::Runtime;
use tokio::sync::mpsc::{UnboundedReceiver, UnboundedSender, unbounded_channel};
use tokio::sync::oneshot;
use tokio::task::JoinHandle;

use crate::events::UiEvent;
use crate::host::{HostNotifier, HostOpener, HostRing, NotificationTable, UiHost};
use crate::identity::client_identity;
use crate::link::{LinkKind, link_kind};
use crate::report::ui_events;
use crate::screen::{ScreenView, screen_view, session_view};
use crate::shell::{ShellView, shell_for, shell_view};

// Declared here, not in lib.rs: the scenes are this file's backend.
#[cfg(feature = "scripted")]
#[path = "scripted.rs"]
mod scripted;

/// How long the work the model asks for at shutdown (saving the session,
/// unregistering this desktop's presence) may take before the runtime stops
/// anyway. The window is already gone by then, so this is time the user waits
/// for the process to end.
pub const QUIT_BUDGET: Duration = Duration::from_secs(2);

/// How long [`Core::suspend`] waits for the work the model asks for before
/// the machine sleeps (unregistering this desktop's presence, ending a call
/// under way). Windows allows an app about two seconds to handle the
/// notification that the machine is about to sleep, and the app's own
/// handling has to fit inside that too.
pub const SUSPEND_BUDGET: Duration = Duration::from_millis(1500);

/// What the core needs from the app to start.
#[derive(Clone, Debug, PartialEq, Eq, uniffi::Record)]
pub struct StartConfig {
    /// Where the app's own files go: the device id, the settings and the
    /// refresh-pending marker. The MSIX package's `LocalState`.
    pub data_dir: String,
    /// The app's version, from its package manifest. It names the app in the
    /// `User-Agent` and on the account screen.
    pub app_version: String,
    /// This computer's name, for the devices list.
    pub device_name: Option<String>,
    /// What keeps this copy's credentials apart from another copy's: the
    /// package family name. Credential Manager is per user, not per package,
    /// so the Store copy and the GitHub copy installed side by side would
    /// otherwise read each other's session.
    pub credential_namespace: String,
}

/// Why the core could not start.
#[derive(Debug, PartialEq, Eq, thiserror::Error, uniffi::Error)]
pub enum StartError {
    /// [`Core::start`] was called twice.
    #[error("the core has already started")]
    AlreadyStarted,
    /// [`Core::start`] was called after [`Core::shutdown`].
    #[error("the core has shut down")]
    ShutDown,
    /// Something the core runs on could not be set up.
    #[error("{detail}")]
    Setup {
        /// What, for a diagnostic line. Never a credential.
        detail: String,
    },
}

fn setup(what: &str) -> impl FnOnce(String) -> StartError + '_ {
    move |error| StartError::Setup {
        detail: format!("{what}: {error}"),
    }
}

/// The District AI core, as the Windows app holds it: one per process.
///
/// [`start`](Self::start) builds everything and starts the actor;
/// [`send`](Self::send), [`open_link`](Self::open_link),
/// [`suspend`](Self::suspend) and [`resume`](Self::resume) hand it events;
/// [`shell`](Self::shell) and
/// [`screen`](Self::screen) read the latest snapshot; and
/// [`shutdown`](Self::shutdown) gives the model its last word and stops the
/// runtime.
#[derive(uniffi::Object)]
pub struct Core {
    lifecycle: Mutex<Lifecycle>,
    snapshot: Arc<SnapshotCell>,
    /// The notifications shown, for [`activate_notification`](Self::activate_notification).
    notifications: Arc<NotificationTable>,
}

enum Lifecycle {
    Idle,
    Running {
        runtime: Runtime,
        sender: UnboundedSender<Message>,
    },
    Stopped,
}

/// What the actor hears.
pub(crate) enum Message {
    /// An event for the model.
    Event(Box<Event>),
    /// Something the user did: one or more events for the model, delivered in
    /// order before the window is told.
    Ui(UiEvent),
    /// The machine is about to sleep: tell the model, run what it asks for,
    /// and answer once that has settled or [`SUSPEND_BUDGET`] has passed. The
    /// actor keeps running.
    Suspend(oneshot::Sender<()>),
    /// Run the model's last effects and stop, then answer.
    Shutdown(oneshot::Sender<()>),
}

impl Message {
    fn event(event: impl Into<Event>) -> Self {
        Self::Event(Box::new(event.into()))
    }
}

/// The latest snapshot, which [`Core::shell`] and [`Core::screen`] read.
pub(crate) struct SnapshotCell(Mutex<Snapshot>);

#[derive(Clone)]
struct Snapshot {
    revision: u64,
    shell: ShellView,
    screen: ScreenView,
}

impl SnapshotCell {
    fn new() -> Self {
        // What the model's own first state looks like, so a window that reads
        // before the actor has run shows "Resuming your session."
        let session = SessionState::Restoring(Restoring {
            problem: None,
            checking: true,
            retry_in: None,
        });
        Self(Mutex::new(Snapshot {
            revision: 0,
            shell: shell_for(
                &session,
                false,
                district_call::CALLS_AVAILABLE,
                std::time::SystemTime::now(),
            ),
            screen: session_view(&session, |_| ScreenView::unavailable()),
        }))
    }

    fn lock(&self) -> MutexGuard<'_, Snapshot> {
        // Each field is replaced whole, so a poisoned lock holds a usable value.
        self.0.lock().unwrap_or_else(PoisonError::into_inner)
    }

    /// Projects `model`, and when anything the window shows changed, bumps the
    /// revision and tells the host. `reporting` says whether this session
    /// started a report that has not been dismissed.
    fn publish(&self, model: &Model, reporting: bool, host: &dyn UiHost) {
        let shell = shell_view(model, reporting);
        let screen = screen_view(model);
        let revision = {
            let mut snapshot = self.lock();
            if snapshot.shell == shell && snapshot.screen == screen {
                return;
            }
            snapshot.revision += 1;
            snapshot.shell = shell;
            snapshot.screen = screen;
            snapshot.revision
        };
        // Outside the lock: the host may read the snapshot straight away.
        host.state_changed(revision);
    }
}

/// Runs the model's effects: the core's `EffectRunner`, or a test's fake.
pub(crate) trait Effects: Send + Sync + 'static {
    fn run(&self, effect: Effect) -> impl Future<Output = Option<Event>> + Send;
}

impl<A, U, S, O, C, L, N, P, E, R> Effects for EffectRunner<A, U, S, O, C, L, N, P, E, R>
where
    A: DistrictApi + 'static,
    U: district_core::Auth + 'static,
    S: Settings + 'static,
    O: UrlOpener + 'static,
    C: Clock + 'static,
    L: LiveUpdates + 'static,
    N: Notifier + 'static,
    P: Presence + 'static,
    E: CallEngine + 'static,
    R: RingSurface + 'static,
{
    fn run(&self, effect: Effect) -> impl Future<Output = Option<Event>> + Send {
        EffectRunner::run(self, effect)
    }
}

#[uniffi::export]
impl Core {
    /// A core that has not started. The window can read its snapshot (the
    /// start-up state) before [`start`](Self::start).
    #[uniffi::constructor]
    pub fn new() -> Arc<Self> {
        Arc::new(Self {
            lifecycle: Mutex::new(Lifecycle::Idle),
            snapshot: Arc::new(SnapshotCell::new()),
            notifications: Arc::new(NotificationTable::default()),
        })
    }

    /// Builds the runtime and every adapter, and starts the model: it drains
    /// the revoke outbox and looks for a stored session.
    pub fn start(&self, config: StartConfig, host: Arc<dyn UiHost>) -> Result<(), StartError> {
        let mut lifecycle = self.lifecycle();
        match *lifecycle {
            Lifecycle::Idle => {}
            Lifecycle::Running { .. } => return Err(StartError::AlreadyStarted),
            Lifecycle::Stopped => return Err(StartError::ShutDown),
        }
        let runtime = tokio::runtime::Builder::new_multi_thread()
            .enable_all()
            .thread_name("district-core")
            .build()
            .map_err(|error| setup("the runtime could not start")(error.to_string()))?;
        // A scripted build runs the scene its command line names, if any.
        #[cfg(feature = "scripted")]
        if let Some(scene) =
            scripted::scene(std::env::args()).map_err(setup("the scripted scene"))?
        {
            let sender = start_scripted(&runtime, scene, &config, host, &self.snapshot);
            *lifecycle = Lifecycle::Running { runtime, sender };
            return Ok(());
        }
        let sender = match start_real(
            &runtime,
            config,
            host,
            &self.snapshot,
            Arc::clone(&self.notifications),
        ) {
            Ok(sender) => sender,
            Err(error) => {
                // Not a plain drop, which blocks on the runtime's threads.
                runtime.shutdown_background();
                return Err(error);
            }
        };
        *lifecycle = Lifecycle::Running { runtime, sender };
        Ok(())
    }

    /// Hands the model something the user did: each of its core events, in
    /// order. Ignored before [`start`](Self::start) and after
    /// [`shutdown`](Self::shutdown).
    pub fn send(&self, event: UiEvent) {
        self.deliver(Message::Ui(event));
    }

    /// Hands the model a `districtai:` link Windows activated the app with,
    /// and says what kind it was. A sign-in's answer goes to the sign-in, a
    /// hand-off's to the hand-off (the core checks each against what it is
    /// waiting for); anything else is ignored.
    pub fn open_link(&self, uri: String) -> LinkKind {
        let kind = link_kind(&uri);
        match kind {
            LinkKind::Auth | LinkKind::Handoff => {
                if let Some(event) = Event::from_link(&uri) {
                    self.deliver(Message::event(event));
                }
            }
            LinkKind::Unknown => {}
        }
        kind
    }

    /// A notification the host showed was activated: its toast clicked
    /// (`action_id` `None`), or one of its buttons, named by its
    /// `NotificationActionView::action_id`. The model then opens what the
    /// notification is about, or answers or declines the call it names. A
    /// notification this core did not show, or no longer remembers, does
    /// nothing.
    pub fn activate_notification(&self, id: String, action_id: Option<String>) {
        if let Some(event) = self.notifications.activate(&id, action_id.as_deref()) {
            self.deliver(Message::event(event));
        }
    }

    /// The machine is about to sleep. Tells the model, which unregisters this
    /// desktop's presence and ends any call or ring, and returns once the work
    /// that asks for has run, or after [`SUSPEND_BUDGET`], whichever is first.
    /// The caller holds the sleep for that long and no longer. Returns at
    /// once before [`start`](Self::start) and after
    /// [`shutdown`](Self::shutdown).
    pub async fn suspend(&self) {
        let (done, settled) = oneshot::channel();
        // Not running, or the actor gone: the message is dropped with `done`,
        // and the wait ends at once.
        self.deliver(Message::Suspend(done));
        settled.await.ok();
    }

    /// The machine has woken: the model registers this desktop's presence
    /// again when it should ring. Nothing waits for that. Ignored before
    /// [`start`](Self::start) and after [`shutdown`](Self::shutdown).
    pub fn resume(&self) {
        self.deliver(Message::event(Event::Resumed));
    }

    /// The window's frame, as of [`revision`](Self::revision).
    pub fn shell(&self) -> ShellView {
        self.snapshot.lock().shell.clone()
    }

    /// The screen inside the frame, as of [`revision`](Self::revision).
    pub fn screen(&self) -> ScreenView {
        self.snapshot.lock().screen.clone()
    }

    /// The snapshot's revision: 0 until the model first changes what the
    /// window shows, then one more for each change.
    pub fn revision(&self) -> u64 {
        self.snapshot.lock().revision
    }

    /// Tells the model the app is quitting, gives the work it asks for (saving
    /// the session) up to [`QUIT_BUDGET`], and stops the runtime. Safe to call
    /// more than once, and before [`start`](Self::start).
    pub async fn shutdown(&self) {
        let previous = std::mem::replace(&mut *self.lifecycle(), Lifecycle::Stopped);
        let Lifecycle::Running { runtime, sender } = previous else {
            return;
        };
        let (done, finished) = oneshot::channel();
        // The actor answers after its last effects, or at the budget. Should
        // it have stopped already, the message comes back undelivered, `done`
        // is dropped with it, and the wait ends at once.
        sender.send(Message::Shutdown(done)).ok();
        finished.await.ok();
        // Not `shutdown_timeout`, which blocks: this runs on whatever thread
        // polls the C# task. Anything still running is dropped at its next
        // await.
        runtime.shutdown_background();
    }
}

impl Core {
    fn lifecycle(&self) -> MutexGuard<'_, Lifecycle> {
        // Each arm is replaced whole, so a poisoned lock holds a usable value.
        self.lifecycle
            .lock()
            .unwrap_or_else(PoisonError::into_inner)
    }

    fn deliver(&self, message: Message) {
        if let Lifecycle::Running { sender, .. } = &*self.lifecycle() {
            // A closed channel means the actor has stopped, which only
            // shutdown does; nothing is left to tell.
            sender.send(message).ok();
        }
    }

    /// Starts the actor over `effects` instead of the real runner. For tests.
    #[cfg(test)]
    pub(crate) fn start_with<X: Effects>(
        &self,
        config: CoreConfig,
        effects: X,
        host: Arc<dyn UiHost>,
    ) -> UnboundedSender<Message> {
        let runtime = tokio::runtime::Builder::new_multi_thread()
            .worker_threads(2)
            .enable_all()
            .build()
            .unwrap();
        let (model, first) = Model::new(config);
        let sender = launch(
            &runtime,
            model,
            first,
            Arc::new(effects),
            host,
            Arc::clone(&self.snapshot),
        );
        *self.lifecycle() = Lifecycle::Running {
            runtime,
            sender: sender.clone(),
        };
        sender
    }
}

impl Drop for Core {
    fn drop(&mut self) {
        // A core dropped without `shutdown` (the process is ending) must not
        // block on its tasks, and must not drop a runtime inside one.
        let lifecycle = std::mem::replace(&mut *self.lifecycle(), Lifecycle::Stopped);
        if let Lifecycle::Running { runtime, .. } = lifecycle {
            runtime.shutdown_background();
        }
    }
}

/// Everything the real app runs on, built inside `runtime`, and the actor
/// started over it.
fn start_real(
    runtime: &Runtime,
    config: StartConfig,
    host: Arc<dyn UiHost>,
    snapshot: &Arc<SnapshotCell>,
    notifications: Arc<NotificationTable>,
) -> Result<UnboundedSender<Message>, StartError> {
    let data_dir = PathBuf::from(&config.data_dir);
    std::fs::create_dir_all(&data_dir)
        .map_err(|error| setup("the data directory could not be made")(error.to_string()))?;
    let device_id = DeviceIdentity::new(&data_dir)
        .load_or_create()
        .map_err(|error| setup("this installation's id could not be kept")(error.to_string()))?;
    let identity = client_identity(&config.app_version);
    let platform = identity.platform;
    let api_config = ApiConfig::new(identity);
    let settings = SettingsFile::load(&data_dir, platform);
    let config_error =
        |error: district_api::ConfigError| setup("the API client")(error.to_string());

    // The coordinator and the live hub take the runtime they are built in.
    let _entered = runtime.enter();
    let sign_in = NativeAuthApi::new(&api_config).map_err(config_error)?;
    // One coordinator: every client and the sign-in share its refresh lock,
    // which is what keeps a refresh token from being sent twice.
    let coordinator = TokenRefreshCoordinator::new(
        platform_store(&data_dir, &config.credential_namespace),
        sign_in.clone(),
    );
    let client = || ApiClient::new(api_config.clone(), coordinator.clone());
    let api = client().map_err(config_error)?;
    let presence = DesktopPresence::new(Arc::new(client().map_err(config_error)?), platform);
    // The live updates' credential states its expiry by the service's clock.
    let live_config = LiveConfig {
        clock: Arc::new(coordinator.server_clock()),
        ..LiveConfig::network().map_err(|error| setup("live updates")(error.to_string()))?
    };
    let (live, updates) = LiveHub::new(Arc::new(client().map_err(config_error)?), live_config);
    let auth = NativeAuth::new(
        &api_config,
        sign_in.clone(),
        coordinator.clone(),
        sign_in,
        presence.clone(),
        device_id,
        config.device_name.clone(),
    );
    let (engine, media) = district_call::engine();
    let runner = EffectRunner::new(
        api,
        auth,
        settings,
        HostOpener(Arc::clone(&host)),
        TokioClock,
        live,
        HostNotifier {
            host: Arc::clone(&host),
            table: notifications,
        },
        presence,
        engine,
        HostRing(Arc::clone(&host)),
    );
    let core_config = CoreConfig {
        web_base_url: api_config.base_url.to_string(),
        app_version: config.app_version,
        calls_available: district_call::CALLS_AVAILABLE,
        // District AI for Windows buys in the app: checkout in a view inside
        // the window (district-core's `purchase`, src/billing.rs).
        in_app_purchases: true,
    };
    let (model, first) = Model::new(core_config);
    let sender = launch(
        runtime,
        model,
        first,
        Arc::new(runner),
        host,
        Arc::clone(snapshot),
    );
    forward(runtime, updates, sender.clone(), Event::Live);
    forward(runtime, media, sender.clone(), Event::Media);
    Ok(sender)
}

/// A scripted scene (src/scripted.rs): the model over made-up answers, with no
/// network, no credential store and nothing written to the data directory.
#[cfg(feature = "scripted")]
fn start_scripted(
    runtime: &Runtime,
    scene: scripted::Scene,
    config: &StartConfig,
    host: Arc<dyn UiHost>,
    snapshot: &Arc<SnapshotCell>,
) -> UnboundedSender<Message> {
    let (model, first) = Model::new(CoreConfig {
        web_base_url: "https://www.distronode.com".to_owned(),
        app_version: config.app_version.clone(),
        calls_available: district_call::CALLS_AVAILABLE,
        in_app_purchases: true,
    });
    launch(
        runtime,
        model,
        first,
        Arc::new(scripted::Scripted::new(scene)),
        host,
        Arc::clone(snapshot),
    )
}

/// The session store: Credential Manager on Windows.
#[cfg(windows)]
fn platform_store(
    data_dir: &Path,
    namespace: &str,
) -> crate::store::CredentialStore<crate::store::WindowsVault> {
    crate::store::CredentialStore::new(
        crate::store::WindowsVault::for_package(namespace),
        RefreshMarkerFile::new(data_dir),
    )
}

/// The session store everywhere else: memory. The Linux build of this crate
/// exists for DistrictAI.Core.Tests, never as an app, so nothing is written
/// to a plain file.
#[cfg(not(windows))]
fn platform_store(
    data_dir: &Path,
    _namespace: &str,
) -> crate::store::CredentialStore<crate::store::MemoryVault> {
    crate::store::CredentialStore::new(
        crate::store::MemoryVault::new(),
        RefreshMarkerFile::new(data_dir),
    )
}

/// Starts the actor that owns `model`, and runs `first`, the model's opening
/// effects. Returns the actor's inbox.
fn launch<X: Effects>(
    runtime: &Runtime,
    model: Model,
    first: Vec<Effect>,
    effects: Arc<X>,
    host: Arc<dyn UiHost>,
    snapshot: Arc<SnapshotCell>,
) -> UnboundedSender<Message> {
    let (sender, inbox) = unbounded_channel();
    let actor = Actor {
        model,
        effects,
        host,
        snapshot,
        sender: sender.clone(),
        reporting: false,
    };
    runtime.spawn(actor.run(first, inbox));
    sender
}

struct Actor<X> {
    model: Model,
    effects: Arc<X>,
    host: Arc<dyn UiHost>,
    snapshot: Arc<SnapshotCell>,
    sender: UnboundedSender<Message>,
    /// Whether this session started a report that has not been dismissed. The
    /// support form's state says how it went; this says it was ours.
    reporting: bool,
}

impl<X: Effects> Actor<X> {
    fn publish(&self) {
        self.snapshot
            .publish(&self.model, self.reporting, &*self.host);
    }

    async fn run(mut self, first: Vec<Effect>, mut inbox: UnboundedReceiver<Message>) {
        self.publish();
        self.dispatch(first);
        while let Some(message) = inbox.recv().await {
            match message {
                Message::Event(event) => {
                    let next = self.model.update(*event);
                    self.publish();
                    self.dispatch(next);
                }
                Message::Ui(action) => {
                    let mut next = Vec::new();
                    let events = ui_events(&self.model, &mut self.reporting, action);
                    for event in events {
                        next.extend(self.model.update(event));
                    }
                    self.publish();
                    self.dispatch(next);
                }
                Message::Suspend(done) => {
                    let next = self.model.update(Event::Suspending);
                    self.publish();
                    // Run as any other effects are, their events fed back, and
                    // watched apart from the actor, which goes on handling
                    // those events (the unregistration's answer among them).
                    let running = self.dispatch(next);
                    tokio::spawn(async move {
                        let all = async {
                            for task in running {
                                task.await.ok();
                            }
                        };
                        tokio::time::timeout(SUSPEND_BUDGET, all).await.ok();
                        // Nobody waiting any more is the sleep gone ahead.
                        done.send(()).ok();
                    });
                }
                Message::Shutdown(done) => {
                    let last = self.model.update(Event::Quitting);
                    self.publish();
                    let running: Vec<_> = last
                        .into_iter()
                        .map(|effect| {
                            let effects = Arc::clone(&self.effects);
                            tokio::spawn(async move { effects.run(effect).await })
                        })
                        .collect();
                    let all = async {
                        for task in running {
                            task.await.ok();
                        }
                    };
                    tokio::time::timeout(QUIT_BUDGET, all).await.ok();
                    done.send(()).ok();
                    return;
                }
            }
        }
    }

    /// Runs each effect on its own task and feeds its event back, and returns
    /// the tasks, for a caller that waits on them. Effects are independent of
    /// each other; the model's tickets sort out an answer that arrives after
    /// it stopped mattering.
    fn dispatch(&self, effects: Vec<Effect>) -> Vec<JoinHandle<()>> {
        effects
            .into_iter()
            .map(|effect| {
                let runner = Arc::clone(&self.effects);
                let sender = self.sender.clone();
                tokio::spawn(async move {
                    if let Some(event) = runner.run(effect).await {
                        // Closed only once the actor has stopped.
                        sender.send(Message::event(event)).ok();
                    }
                })
            })
            .collect()
    }
}

/// Forwards everything `from` receives to the actor as events, until the
/// actor stops.
fn forward<T: Send + 'static>(
    runtime: &Runtime,
    mut from: UnboundedReceiver<T>,
    to: UnboundedSender<Message>,
    event: fn(T) -> Event,
) {
    runtime.spawn(async move {
        while let Some(item) = from.recv().await {
            if to.send(Message::event(event(item))).is_err() {
                break;
            }
        }
    });
}

#[cfg(test)]
mod tests {
    use std::sync::atomic::{AtomicBool, Ordering};
    use std::time::Instant;

    use district_api::{ReauthReason, TokenError};
    use district_auth::AccessClaims;
    use district_core::RestoreError;

    use super::*;
    use crate::host::tests::RecordingHost;
    use crate::screen::SessionScreen;
    use crate::screen::WELCOME_TITLE;
    use crate::shell::SessionPhase;

    /// Answers the effects of signing in and out the way the real runner
    /// would with no stored session and a browser that opens, and records
    /// every effect it is asked to run.
    #[derive(Default)]
    struct FakeEffects {
        ran: Mutex<Vec<Effect>>,
        /// Whether the start-up check finds a session.
        stored: bool,
        /// The "ring on this computer" setting, as read at sign-in.
        ring_here: bool,
        /// How long unregistering this desktop's presence takes.
        unregister_takes: Duration,
        /// Set once an unregistration has finished.
        unregistered: AtomicBool,
        /// How long saving the session takes, at quitting.
        save_takes: Duration,
    }

    impl Effects for Arc<FakeEffects> {
        fn run(&self, effect: Effect) -> impl Future<Output = Option<Event>> + Send {
            self.ran.lock().unwrap().push(effect.clone());
            async move { self.answer(effect).await }
        }
    }

    impl FakeEffects {
        async fn answer(&self, effect: Effect) -> Option<Event> {
            match effect {
                Effect::RestoreSession { ticket } => Some(Event::SessionRestored {
                    ticket,
                    result: if self.stored {
                        Ok(AccessClaims {
                            user_id: "user-1".to_owned(),
                            device_id: "device-windows-1".to_owned(),
                            expires_at_secs: 4_000_000_000,
                        })
                    } else {
                        Err(RestoreError::Token(TokenError::SignInRequired(
                            ReauthReason::NoSession,
                        )))
                    },
                }),
                Effect::BeginSignIn { ticket } => Some(Event::SignInBrowser {
                    ticket,
                    opened: true,
                }),
                Effect::ReadRingSetting { ticket } => Some(Event::RingSettingRead {
                    ticket,
                    ring_here: self.ring_here,
                }),
                Effect::SetPresence { ticket, registered } => {
                    if !registered {
                        tokio::time::sleep(self.unregister_takes).await;
                        self.unregistered.store(true, Ordering::SeqCst);
                    }
                    Some(Event::PresenceSet {
                        ticket,
                        result: Ok(()),
                    })
                }
                Effect::SaveSession => {
                    tokio::time::sleep(self.save_takes).await;
                    None
                }
                _ => None,
            }
        }

        /// How many times the presence was registered.
        fn registrations(&self) -> usize {
            self.ran
                .lock()
                .unwrap()
                .iter()
                .filter(|effect| {
                    matches!(
                        effect,
                        Effect::SetPresence {
                            registered: true,
                            ..
                        }
                    )
                })
                .count()
        }

        fn ran(&self, matches: impl Fn(&Effect) -> bool) -> bool {
            self.ran.lock().unwrap().iter().any(matches)
        }
    }

    fn config() -> CoreConfig {
        CoreConfig {
            web_base_url: "https://www.distronode.com".to_owned(),
            app_version: "0.1.0".to_owned(),
            calls_available: false,
            in_app_purchases: true,
        }
    }

    /// A build that can take calls: the only kind that registers a presence.
    fn calls_config() -> CoreConfig {
        CoreConfig {
            calls_available: true,
            ..config()
        }
    }

    /// Fails the test if `suspend` waits for anything: nothing is running for
    /// it to wait on.
    async fn suspends_at_once(core: &Core) {
        tokio::time::timeout(Duration::from_millis(250), core.suspend())
            .await
            .expect("suspend returns at once when nothing runs");
    }

    /// Signed in, with "ring on this computer" on, a build with calls, and an
    /// unregistration that takes `unregister_takes`: started, and registered.
    async fn registered(unregister_takes: Duration) -> (Arc<Core>, Arc<FakeEffects>) {
        let core = Core::new();
        let effects = Arc::new(FakeEffects {
            stored: true,
            ring_here: true,
            unregister_takes,
            ..FakeEffects::default()
        });
        core.start_with(
            calls_config(),
            Arc::clone(&effects),
            Arc::new(RecordingHost::default()),
        );
        eventually("registered", || effects.registrations() == 1).await;
        (core, effects)
    }

    /// Waits, for real, until `done` holds; the actor runs on its own runtime.
    async fn eventually(what: &str, done: impl Fn() -> bool) {
        for _ in 0..500 {
            if done() {
                break;
            }
            tokio::time::sleep(Duration::from_millis(10)).await;
        }
        assert!(done(), "timed out waiting for {what}");
    }

    fn phase(core: &Core) -> SessionPhase {
        core.shell().phase
    }

    /// The sign-in page showing, or `None` while signed in.
    fn session_screen(core: &Core) -> Option<SessionScreen> {
        match core.screen() {
            ScreenView::Session { view } => Some(view),
            _ => None,
        }
    }

    #[tokio::test]
    async fn before_starting_the_window_reads_the_start_up_state() {
        let core = Core::new();
        assert_eq!(core.revision(), 0);
        assert_eq!(phase(&core), SessionPhase::Restoring);
        let view = session_screen(&core).expect("the start-up state is a session screen");
        assert_eq!(view.body, "Resuming your session.");
        assert!(view.busy);
        // Nothing to deliver to, and nothing breaks.
        core.send(UiEvent::SignIn);
        suspends_at_once(&core).await;
        core.resume();
        assert_eq!(
            core.open_link("districtai://auth?code=c".to_owned()),
            LinkKind::Auth
        );
        core.shutdown().await;
        let refused = core.start(
            StartConfig {
                data_dir: String::new(),
                app_version: "0.1.0".to_owned(),
                device_name: None,
                credential_namespace: "DistrictAI.Tests_0".to_owned(),
            },
            Arc::new(RecordingHost::default()),
        );
        assert_eq!(refused, Err(StartError::ShutDown));
    }

    #[tokio::test]
    async fn the_actor_runs_the_model_and_tells_the_host_each_revision() {
        let core = Core::new();
        let host = Arc::new(RecordingHost::default());
        let effects = Arc::new(FakeEffects::default());
        core.start_with(config(), Arc::clone(&effects), host.clone());

        eventually("signed out", || phase(&core) == SessionPhase::SignedOut).await;
        let view = session_screen(&core).expect("signed out is a session screen");
        assert_eq!(view.title, WELCOME_TITLE);
        assert!(view.sign_in);
        assert!(effects.ran(|effect| matches!(effect, Effect::DrainRevokeOutbox)));
        eventually("the host told", || {
            host.revision.load(Ordering::SeqCst) == core.revision()
        })
        .await;

        core.send(UiEvent::SignIn);
        eventually(
            "waiting for the browser",
            || matches!(core.screen(), ScreenView::Session { view } if view.cancel),
        )
        .await;
        assert_eq!(phase(&core), SessionPhase::SigningIn);

        // The browser's answer goes to the sign-in, and only that link does.
        let answer = "districtai://auth?code=c&state=s".to_owned();
        assert_eq!(core.open_link(answer.clone()), LinkKind::Auth);
        eventually("the exchange", || {
            effects.ran(|effect| {
                matches!(effect, Effect::CompleteSignIn { callback, .. } if *callback == answer)
            })
        })
        .await;
        assert_eq!(
            core.open_link("districtai://handoff?n=1".to_owned()),
            LinkKind::Handoff
        );
        assert_eq!(
            core.open_link("https://example.com".to_owned()),
            LinkKind::Unknown
        );

        // An event that changes nothing on screen is not a new revision.
        let before = core.revision();
        core.resume();
        core.send(UiEvent::Refresh);
        tokio::time::sleep(Duration::from_millis(50)).await;
        assert_eq!(core.revision(), before);

        // Signed out, the model asks for nothing before the machine sleeps.
        suspends_at_once(&core).await;

        core.shutdown().await;
        core.shutdown().await;
        core.send(UiEvent::SignIn);
        suspends_at_once(&core).await;
        core.resume();
        assert_eq!(core.revision(), before);
    }

    /// Before the machine sleeps, the presence is unregistered, and `suspend`
    /// returns only once that has run. Awake again, it is registered again.
    #[tokio::test]
    async fn suspending_waits_for_the_unregistration_and_resuming_registers_again() {
        let takes = Duration::from_millis(300);
        let (core, effects) = registered(takes).await;

        let started = Instant::now();
        core.suspend().await;
        assert!(effects.unregistered.load(Ordering::SeqCst));
        assert!(started.elapsed() >= takes);
        assert!(started.elapsed() < SUSPEND_BUDGET);

        core.resume();
        eventually("registered again", || effects.registrations() == 2).await;
        core.shutdown().await;
    }

    /// An unregistration that never finishes holds the sleep for
    /// [`SUSPEND_BUDGET`] and no longer.
    #[tokio::test]
    async fn a_suspend_that_hangs_returns_at_the_budget() {
        let (core, effects) = registered(Duration::from_secs(3600)).await;

        let started = Instant::now();
        core.suspend().await;
        let waited = started.elapsed();
        assert!(waited >= SUSPEND_BUDGET, "{waited:?}");
        assert!(
            waited < SUSPEND_BUDGET + Duration::from_secs(1),
            "{waited:?}"
        );
        assert!(!effects.unregistered.load(Ordering::SeqCst));
        assert!(effects.ran(|effect| matches!(
            effect,
            Effect::SetPresence {
                registered: false,
                ..
            }
        )));
        // Asleep, nothing is left to unregister at quitting, so this does not
        // wait on the hung one.
        core.shutdown().await;
    }

    /// Signed in, quitting saves the session (should a refresh have left it
    /// unsaved) before the runtime stops.
    #[tokio::test]
    async fn shutting_down_signed_in_runs_the_last_effects() {
        let core = Core::new();
        let effects = Arc::new(FakeEffects {
            stored: true,
            ..FakeEffects::default()
        });
        core.start_with(
            config(),
            Arc::clone(&effects),
            Arc::new(RecordingHost::default()),
        );
        eventually("signed in", || phase(&core) == SessionPhase::SignedIn).await;
        assert_eq!(session_screen(&core), None);
        core.shutdown().await;
        assert!(effects.ran(|effect| matches!(effect, Effect::SaveSession)));
    }

    /// The work Quitting asks for (saving the session, and from
    /// district-core-rust 2.0.0 a reply still waiting to be saved) gets
    /// [`QUIT_BUDGET`] and no longer: quitting waits for it, and returns at
    /// the budget when it hangs.
    #[tokio::test]
    async fn shutting_down_waits_for_the_last_effects_up_to_the_budget() {
        let core = Core::new();
        let effects = Arc::new(FakeEffects {
            stored: true,
            save_takes: Duration::from_secs(30),
            ..FakeEffects::default()
        });
        core.start_with(
            config(),
            Arc::clone(&effects),
            Arc::new(RecordingHost::default()),
        );
        eventually("signed in", || phase(&core) == SessionPhase::SignedIn).await;
        let started = std::time::Instant::now();
        core.shutdown().await;
        let waited = started.elapsed();
        assert!(effects.ran(|effect| matches!(effect, Effect::SaveSession)));
        assert!(waited >= QUIT_BUDGET, "{waited:?}");
        assert!(waited < QUIT_BUDGET + Duration::from_secs(1), "{waited:?}");
    }

    /// A report's three events reach the model, and a dismissal its two; with
    /// no workspace open the core refuses both, so nothing is under way.
    #[tokio::test]
    async fn a_report_and_its_dismissal_reach_the_model() {
        let core = Core::new();
        let host = Arc::new(RecordingHost::default());
        let effects = Arc::new(FakeEffects {
            stored: true,
            ..FakeEffects::default()
        });
        core.start_with(config(), Arc::clone(&effects), host);
        eventually("signed in", || phase(&core) == SessionPhase::SignedIn).await;
        core.send(UiEvent::Report {
            target: crate::views::ReportTarget::Call {
                call_id: "call-1".to_owned(),
            },
            note: String::new(),
        });
        core.send(UiEvent::DismissReport);
        core.send(UiEvent::OpenTab {
            tab: crate::shell::TabView::Account,
        });
        eventually("the account screen", || {
            matches!(core.screen(), ScreenView::Account { .. })
        })
        .await;
        assert_eq!(core.shell().report, None);
        assert!(!effects.ran(|effect| matches!(effect, Effect::CreateSupportRequest { .. })));
        core.shutdown().await;
    }

    #[tokio::test]
    async fn forwarding_stops_when_the_actor_has() {
        let runtime = tokio::runtime::Builder::new_multi_thread()
            .worker_threads(1)
            .enable_all()
            .build()
            .unwrap();
        let (source, from) = unbounded_channel();
        let (to, mut inbox) = unbounded_channel();
        forward(&runtime, from, to, Event::WindowVisible);
        source.send(false).unwrap();
        let first = inbox.recv().await;
        assert!(
            matches!(&first, Some(Message::Event(event)) if **event == Event::WindowVisible(false))
        );
        drop(inbox);
        source.send(true).unwrap();
        eventually("the forwarder to stop", || source.is_closed()).await;
        runtime.shutdown_background();
    }

    #[tokio::test]
    async fn a_core_dropped_while_running_stops_without_blocking() {
        let core = Core::new();
        core.start_with(
            config(),
            Arc::new(FakeEffects::default()),
            Arc::new(RecordingHost::default()),
        );
        drop(core);
    }

    /// A toast clicked, or one of its buttons, reaches the model as the event
    /// the core's notification names; one this core never showed does not.
    #[test]
    fn activating_a_notification_sends_its_event() {
        let core = Core::new();
        let runtime = tokio::runtime::Builder::new_current_thread()
            .build()
            .unwrap();
        let (sender, mut inbox) = unbounded_channel();
        *core.lifecycle() = Lifecycle::Running { runtime, sender };
        core.notifications
            .shown(&crate::host::tests::ringing("call-1"));

        core.activate_notification("call:call-1".to_owned(), Some("answer".to_owned()));
        core.activate_notification("call:unknown".to_owned(), None);
        core.activate_notification("call:call-1".to_owned(), None);
        let mut events = Vec::new();
        while let Ok(Message::Event(event)) = inbox.try_recv() {
            events.push(*event);
        }
        assert_eq!(
            events,
            [
                Event::Ring(district_core::RingEvent::Answer {
                    call_id: "call-1".to_owned(),
                }),
                Event::OpenNotification(district_core::NotificationTarget::IncomingCall {
                    workspace_id: "ws-1".to_owned(),
                    call_id: "call-1".to_owned(),
                }),
            ]
        );
    }

    /// The real runner, offline: with nothing in the store, the start-up check
    /// signs out without a request, so this runs anywhere.
    #[tokio::test]
    async fn the_real_core_starts_signs_out_and_shuts_down() {
        let dir = tempfile::tempdir().unwrap();
        let data = dir.path().join("LocalState");
        let core = Core::new();
        let host = Arc::new(RecordingHost::default());
        let config = StartConfig {
            data_dir: data.to_string_lossy().into_owned(),
            app_version: "0.1.0".to_owned(),
            device_name: Some("Test PC".to_owned()),
            credential_namespace: "DistrictAI.Tests_0".to_owned(),
        };
        core.start(config.clone(), host.clone()).unwrap();
        assert_eq!(
            core.start(config, host.clone()),
            Err(StartError::AlreadyStarted)
        );
        eventually("signed out", || phase(&core) == SessionPhase::SignedOut).await;
        assert!(data.join(district_host::DEVICE_ID_FILE).is_file());
        core.shutdown().await;
    }

    #[tokio::test]
    async fn a_data_directory_that_cannot_be_made_is_a_setup_error() {
        let dir = tempfile::tempdir().unwrap();
        let file = dir.path().join("a-file");
        std::fs::write(&file, "").unwrap();
        let error = Core::new()
            .start(
                StartConfig {
                    data_dir: file.join("below").to_string_lossy().into_owned(),
                    app_version: "0.1.0".to_owned(),
                    device_name: None,
                    credential_namespace: "DistrictAI.Tests_0".to_owned(),
                },
                Arc::new(RecordingHost::default()),
            )
            .unwrap_err();
        assert!(
            matches!(&error, StartError::Setup { detail } if detail.starts_with("the data directory")),
            "{error:?}"
        );
    }
}
