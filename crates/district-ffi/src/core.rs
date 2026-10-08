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

use crate::events::{PowerChange, UiEvent};
use crate::host::{HostOpener, NoNotifier, NoRing, UiHost};
use crate::identity::client_identity;
use crate::link::{LinkKind, link_kind};
use crate::screen::{ScreenView, screen_view, session_view};
use crate::shell::{ShellView, shell_for, shell_view};

/// How long the work the model asks for at shutdown (saving the session,
/// unregistering this desktop's presence) may take before the runtime stops
/// anyway. The window is already gone by then, so this is time the user waits
/// for the process to end.
pub const QUIT_BUDGET: Duration = Duration::from_secs(2);

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
/// [`send`](Self::send), [`open_link`](Self::open_link) and
/// [`power`](Self::power) hand it events; [`shell`](Self::shell) and
/// [`screen`](Self::screen) read the latest snapshot; and
/// [`shutdown`](Self::shutdown) gives the model its last word and stops the
/// runtime.
#[derive(uniffi::Object)]
pub struct Core {
    lifecycle: Mutex<Lifecycle>,
    snapshot: Arc<SnapshotCell>,
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
            shell: shell_for(&session, false),
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
        let sender = match start_real(&runtime, config, host, &self.snapshot) {
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

    /// The machine is about to sleep, or has woken.
    pub fn power(&self, change: PowerChange) {
        self.deliver(Message::event(change));
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
        NoNotifier,
        presence,
        engine,
        NoRing,
    );
    let core_config = CoreConfig {
        web_base_url: api_config.base_url.to_string(),
        app_version: config.app_version,
        calls_available: district_call::CALLS_AVAILABLE,
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
                    match &action {
                        UiEvent::Report { .. } => self.reporting = true,
                        UiEvent::DismissReport => self.reporting = false,
                        _ => {}
                    }
                    let mut next = Vec::new();
                    for event in action.events() {
                        next.extend(self.model.update(event));
                    }
                    self.publish();
                    self.dispatch(next);
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

    /// Runs each effect on its own task and feeds its event back. Effects are
    /// independent of each other; the model's tickets sort out an answer that
    /// arrives after it stopped mattering.
    fn dispatch(&self, effects: Vec<Effect>) {
        for effect in effects {
            let runner = Arc::clone(&self.effects);
            let sender = self.sender.clone();
            tokio::spawn(async move {
                if let Some(event) = runner.run(effect).await {
                    // Closed only once the actor has stopped.
                    sender.send(Message::event(event)).ok();
                }
            });
        }
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
    use std::sync::atomic::Ordering;

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
    }

    impl Effects for Arc<FakeEffects> {
        fn run(&self, effect: Effect) -> impl Future<Output = Option<Event>> + Send {
            self.ran.lock().unwrap().push(effect.clone());
            let event = match effect {
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
                _ => None,
            };
            std::future::ready(event)
        }
    }

    impl FakeEffects {
        fn ran(&self, matches: impl Fn(&Effect) -> bool) -> bool {
            self.ran.lock().unwrap().iter().any(matches)
        }
    }

    fn config() -> CoreConfig {
        CoreConfig {
            web_base_url: "https://www.distronode.com".to_owned(),
            app_version: "0.1.0".to_owned(),
            calls_available: false,
        }
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
        core.power(PowerChange::Suspending);
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
        core.power(PowerChange::Resumed);
        core.send(UiEvent::Refresh);
        tokio::time::sleep(Duration::from_millis(50)).await;
        assert_eq!(core.revision(), before);

        core.shutdown().await;
        core.shutdown().await;
        core.send(UiEvent::SignIn);
        assert_eq!(core.revision(), before);
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
