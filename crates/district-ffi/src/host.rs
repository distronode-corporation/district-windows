//! What the core asks of the C# side, and the runner's traits built on it.

use std::collections::VecDeque;
use std::future::Future;
use std::sync::{Arc, Mutex, PoisonError};

use district_core::{
    EmbeddedView, Event, Notification, NotificationAction, NotificationTarget, Notifier,
    RingSurface, Urgency, UrlOpener,
};

/// The C# side of the boundary, implemented by `CoreHost`.
///
/// Called from the core's own threads, never the UI thread: an implementation
/// that touches the window marshals onto its `DispatcherQueue` first.
#[uniffi::export(with_foreign)]
#[async_trait::async_trait]
pub trait UiHost: Send + Sync {
    /// The snapshot behind [`Core::shell`](crate::Core::shell) and
    /// [`Core::screen`](crate::Core::screen) is now at `revision`. Revisions
    /// only grow. Many calls may arrive for one frame; the window reads the
    /// snapshot once, after the last.
    fn state_changed(&self, revision: u64);

    /// Opens `url` in the user's own browser (never in a view inside the app),
    /// and answers whether a browser took it.
    async fn open_url(&self, url: String) -> bool;

    /// Opens `url` in the checkout window, a view inside the app, and answers
    /// whether it did. Asked only for the service's hand-off start page and
    /// the one-time link after it (district-core's `UrlOpener::open_embedded`).
    ///
    /// [`EmbeddedViewKind::NewPrivate`] opens a new private view, replacing
    /// any earlier one, with nothing from it or from the browser and nothing
    /// kept once it closes; [`EmbeddedViewKind::Same`] navigates that view,
    /// answering false when it is gone. While it shows, a navigation to
    /// `districtai://handoff` is cancelled in the view and handed to
    /// [`Core::open_link`](crate::Core::open_link), and the window closing is
    /// `BillingAction::CheckoutClosed`. Answer false when the view cannot be
    /// shown at all (no WebView2 runtime): the core then opens the pages in
    /// the browser instead, and says so.
    async fn open_embedded(&self, url: String, view: EmbeddedViewKind) -> bool;

    /// Shows `notification` as a toast, replacing any shown with the same id.
    /// An [`urgent`](NotificationView::urgent) one (a call ringing now) stays
    /// on screen until it is dealt with. Clicking it, or one of its actions,
    /// calls [`Core::activate_notification`](crate::Core::activate_notification)
    /// with its id and the action's id.
    fn notify(&self, notification: NotificationView);

    /// Takes away the notification `id`, if it is showing.
    fn withdraw(&self, id: String);

    /// Starts the ringtone, looping, until [`stop_ringtone`](Self::stop_ringtone).
    fn start_ringtone(&self);

    /// Stops the ringtone, if it is sounding.
    fn stop_ringtone(&self);

    /// Brings the main window forward: shown, restored, raised and focused.
    fn present_window(&self);
}

/// Which view [`UiHost::open_embedded`] opens a page in: the core's
/// `EmbeddedView`.
#[derive(Clone, Copy, Debug, PartialEq, Eq, uniffi::Enum)]
pub enum EmbeddedViewKind {
    /// A new private view.
    NewPrivate,
    /// The view the last new one opened.
    Same,
}

impl From<EmbeddedView> for EmbeddedViewKind {
    fn from(view: EmbeddedView) -> Self {
        match view {
            EmbeddedView::NewPrivate => Self::NewPrivate,
            EmbeddedView::Same => Self::Same,
        }
    }
}

/// A notification, as the host shows it: the core's `Notification` with its
/// target left in Rust (see [`NotificationTable`]).
#[derive(Clone, Debug, PartialEq, Eq, uniffi::Record)]
pub struct NotificationView {
    /// The same for every notification about the same message or call, so a
    /// later one replaces the earlier.
    pub id: String,
    /// The heading.
    pub title: String,
    /// The body.
    pub body: String,
    /// A call ringing now: over everything, with its sound and its buttons,
    /// and kept until it is dealt with.
    pub urgent: bool,
    /// Its buttons, in order. Always none for a message.
    pub actions: Vec<NotificationActionView>,
    /// What it is about, which picks the toast's style.
    pub kind: NotificationKind,
    /// What clicking the toast opens, written by [`encode_open_target`] for
    /// the host to carry in the toast's arguments and hand back to
    /// [`Core::activate_notification`](crate::Core::activate_notification).
    /// Ids only (the workspace, the call or message), never a secret or what
    /// a message says. Empty when the ids cannot be carried; such a toast,
    /// once forgotten, only shows the window.
    pub open_target: String,
}

/// What a notification is about, from the core's target.
#[derive(Clone, Copy, Debug, PartialEq, Eq, uniffi::Enum)]
pub enum NotificationKind {
    /// A call ringing here now, or one waiting behind another.
    IncomingCall,
    /// A call over, such as one missed.
    Call,
    /// A new message: no buttons, and its body opens the conversation.
    Message,
}

impl From<&NotificationTarget> for NotificationKind {
    fn from(target: &NotificationTarget) -> Self {
        match target {
            NotificationTarget::IncomingCall { .. } => Self::IncomingCall,
            NotificationTarget::Call { .. } => Self::Call,
            NotificationTarget::Message { .. } => Self::Message,
        }
    }
}

/// A button on a notification.
#[derive(Clone, Debug, PartialEq, Eq, uniffi::Record)]
pub struct NotificationActionView {
    /// What the button says ("Answer", "Decline").
    pub label: String,
    /// What the host hands back to
    /// [`Core::activate_notification`](crate::Core::activate_notification)
    /// when it is pressed.
    pub action_id: String,
}

/// The id of a notification action, as the host hands it back.
fn action_id(action: &NotificationAction) -> &'static str {
    match action {
        NotificationAction::Answer { .. } => "answer",
        NotificationAction::Decline { .. } => "decline",
    }
}

impl From<&Notification> for NotificationView {
    fn from(notification: &Notification) -> Self {
        Self {
            id: notification.id.clone(),
            title: notification.title.clone(),
            body: notification.body.clone(),
            urgent: match notification.urgency {
                Urgency::Normal => false,
                Urgency::Urgent => true,
            },
            actions: notification
                .actions
                .iter()
                .map(|action| NotificationActionView {
                    label: action.label().to_owned(),
                    action_id: action_id(action).to_owned(),
                })
                .collect(),
            kind: NotificationKind::from(&notification.target),
            open_target: encode_open_target(&notification.target).unwrap_or_default(),
        }
    }
}

/// The version of the open-target format, its first field. A toast written by
/// another version is refused, as garbled.
const OPEN_TARGET_VERSION: &str = "1";

/// The longest id an open target carries.
const OPEN_TARGET_ID_MAX: usize = 128;

/// Whether `id` can be carried in an open target: 1 to [`OPEN_TARGET_ID_MAX`]
/// ASCII letters, digits, `_`, `-`, `.` or `:`, so it never holds the
/// separator or anything the toast's own argument encoding would change.
fn carriable(id: &str) -> bool {
    (1..=OPEN_TARGET_ID_MAX).contains(&id.len())
        && id
            .bytes()
            .all(|b| b.is_ascii_alphanumeric() || matches!(b, b'_' | b'-' | b'.' | b':'))
}

/// `target` as a toast carries it: `1|<kind>|<workspace id>|<id>`, where the
/// kind is `incoming-call`, `call` or `message`. `None` when an id is not
/// [`carriable`].
///
/// A toast outlives the process that showed it (it waits in Action Center
/// across a Quit or a restart), while the [`NotificationTable`] does not, so
/// the toast carries what opening it leads to: the same ids the core's
/// [`NotificationTarget`] holds, nothing more.
pub(crate) fn encode_open_target(target: &NotificationTarget) -> Option<String> {
    let (kind, workspace_id, id) = match target {
        NotificationTarget::IncomingCall {
            workspace_id,
            call_id,
        } => ("incoming-call", workspace_id, call_id),
        NotificationTarget::Call {
            workspace_id,
            call_id,
        } => ("call", workspace_id, call_id),
        NotificationTarget::Message {
            workspace_id,
            message_id,
        } => ("message", workspace_id, message_id),
    };
    (carriable(workspace_id) && carriable(id))
        .then(|| format!("{OPEN_TARGET_VERSION}|{kind}|{workspace_id}|{id}"))
}

/// The [`NotificationTarget`] an [`encode_open_target`] string names. `None`
/// for anything else: another version, an unknown kind, a missing or extra
/// field, or an id that could not have been written.
pub(crate) fn parse_open_target(text: &str) -> Option<NotificationTarget> {
    let mut fields = text.split('|');
    let (Some(version), Some(kind), Some(workspace_id), Some(id), None) = (
        fields.next(),
        fields.next(),
        fields.next(),
        fields.next(),
        fields.next(),
    ) else {
        return None;
    };
    if version != OPEN_TARGET_VERSION || !carriable(workspace_id) || !carriable(id) {
        return None;
    }
    let (workspace_id, id) = (workspace_id.to_owned(), id.to_owned());
    match kind {
        "incoming-call" => Some(NotificationTarget::IncomingCall {
            workspace_id,
            call_id: id,
        }),
        "call" => Some(NotificationTarget::Call {
            workspace_id,
            call_id: id,
        }),
        "message" => Some(NotificationTarget::Message {
            workspace_id,
            message_id: id,
        }),
        _ => None,
    }
}

/// The notifications shown, by id, so that activating one maps back to what
/// the core asked for: its target, and its actions with the calls they name.
/// Only ids cross to C#; what opening a toast does stays here.
///
/// It keeps the last [`NotificationTable::CAPACITY`] notifications shown. A
/// toast older than that (left in the Action Center for days) does nothing
/// when clicked, rather than letting the table grow without end.
#[derive(Debug, Default)]
pub(crate) struct NotificationTable(Mutex<VecDeque<Notification>>);

impl NotificationTable {
    /// How many notifications are remembered.
    pub(crate) const CAPACITY: usize = 64;

    fn lock(&self) -> std::sync::MutexGuard<'_, VecDeque<Notification>> {
        // Each entry is pushed or removed whole, so a poisoned lock holds a
        // usable value.
        self.0.lock().unwrap_or_else(PoisonError::into_inner)
    }

    /// Remembers `notification`, replacing any with the same id.
    pub(crate) fn shown(&self, notification: &Notification) {
        let mut shown = self.lock();
        shown.retain(|old| old.id != notification.id);
        if shown.len() == Self::CAPACITY {
            shown.pop_front();
        }
        shown.push_back(notification.clone());
    }

    /// Forgets the notification `id`.
    pub(crate) fn withdrawn(&self, id: &str) {
        self.lock().retain(|old| old.id != id);
    }

    /// [`known`](Self::known) with a notification it does not know read as
    /// nothing, which is how the table alone answers (its tests ask it so).
    #[cfg(test)]
    pub(crate) fn activate(&self, id: &str, action_id: Option<&str>) -> Option<Event> {
        self.known(id, action_id).flatten()
    }

    /// The event activating the notification `id` sends: opening its target
    /// when `action_id` is `None` (the toast itself was clicked), or the event
    /// of the action named, `None` for an action it does not have. The outer
    /// `None` when this table does not know the notification `id` at all.
    fn known(&self, id: &str, action_id: Option<&str>) -> Option<Option<Event>> {
        let shown = self.lock();
        let notification = shown.iter().find(|shown| shown.id == id)?;
        Some(match action_id {
            None => Some(Event::OpenNotification(notification.target.clone())),
            Some(wanted) => notification
                .actions
                .iter()
                .find(|action| self::action_id(action) == wanted)
                .map(NotificationAction::event),
        })
    }

    /// The event a toast's activation sends: what [`known`](Self::known)
    /// answers when this table knows the notification `id`, which is always
    /// the first choice. Otherwise (a toast left in Action Center by an
    /// earlier process, or older than the table's memory) the toast's body
    /// opens the `open_target` it carries, when that is one
    /// [`encode_open_target`] wrote; anything else does nothing, and the host
    /// only shows the window.
    pub(crate) fn activate_toast(
        &self,
        id: &str,
        action_id: Option<&str>,
        open_target: Option<&str>,
    ) -> Option<Event> {
        if let Some(event) = self.known(id, action_id) {
            return event;
        }
        if action_id.is_some() {
            // Answer or Decline on a toast this table no longer knows: the
            // ring it was for ended with the process that showed it (or long
            // ago), so there is nothing to answer or decline. Ignored on
            // purpose, never guessed from the open target.
            return None;
        }
        open_target
            .and_then(parse_open_target)
            .map(Event::OpenNotification)
    }
}

/// [`UrlOpener`] over the host.
#[derive(Clone)]
pub(crate) struct HostOpener(pub(crate) Arc<dyn UiHost>);

impl UrlOpener for HostOpener {
    fn open(&self, url: &str) -> impl Future<Output = bool> + Send {
        let host = Arc::clone(&self.0);
        let url = url.to_owned();
        async move { host.open_url(url).await }
    }

    fn open_embedded(&self, url: &str, view: EmbeddedView) -> impl Future<Output = bool> + Send {
        let host = Arc::clone(&self.0);
        let url = url.to_owned();
        async move { host.open_embedded(url, view.into()).await }
    }
}

/// [`Notifier`] over the host: each notification the core decides to give (a
/// call ringing here, one waiting behind another, a missed call, a new
/// message) goes to C# as a [`NotificationView`], and is remembered in the
/// table that [`Core::activate_notification`](crate::Core::activate_notification)
/// reads. Whether to notify at all (a message in the thread already open and
/// visible, say) is the core's to decide, so nothing is filtered here.
#[derive(Clone)]
pub(crate) struct HostNotifier {
    pub(crate) host: Arc<dyn UiHost>,
    pub(crate) table: Arc<NotificationTable>,
}

impl Notifier for HostNotifier {
    fn notify(&self, notification: &Notification) {
        // Remembered first, so a click that comes back at once finds it.
        self.table.shown(notification);
        self.host.notify(NotificationView::from(notification));
    }

    fn withdraw(&self, id: &str) {
        self.table.withdrawn(id);
        self.host.withdraw(id.to_owned());
    }
}

/// [`RingSurface`] over the host: the ringtone and the window are C#'s.
#[derive(Clone)]
pub(crate) struct HostRing(pub(crate) Arc<dyn UiHost>);

impl RingSurface for HostRing {
    fn start_ringtone(&self) {
        self.0.start_ringtone();
    }

    fn stop_ringtone(&self) {
        self.0.stop_ringtone();
    }

    fn present_window(&self) {
        self.0.present_window();
    }
}

#[cfg(test)]
pub(crate) mod tests {
    use std::sync::atomic::{AtomicU64, Ordering};

    use district_core::RingEvent;

    use super::*;

    /// What a [`RecordingHost`] was asked to do, besides opening pages, in
    /// order.
    #[derive(Clone, Debug, PartialEq, Eq)]
    pub(crate) enum Told {
        Notify(NotificationView),
        Withdraw(String),
        StartRingtone,
        StopRingtone,
        PresentWindow,
    }

    /// A host that records what it was told and opens every page it is given.
    #[derive(Default)]
    pub(crate) struct RecordingHost {
        pub(crate) revision: AtomicU64,
        pub(crate) opened: Mutex<Vec<String>>,
        pub(crate) told: Mutex<Vec<Told>>,
        pub(crate) refuse: bool,
    }

    impl RecordingHost {
        fn tell(&self, told: Told) {
            self.told.lock().unwrap().push(told);
        }
    }

    #[async_trait::async_trait]
    impl UiHost for RecordingHost {
        fn state_changed(&self, revision: u64) {
            self.revision.fetch_max(revision, Ordering::SeqCst);
        }

        async fn open_url(&self, url: String) -> bool {
            self.opened.lock().unwrap().push(url);
            !self.refuse
        }

        async fn open_embedded(&self, url: String, view: EmbeddedViewKind) -> bool {
            self.opened.lock().unwrap().push(format!("{view:?} {url}"));
            !self.refuse
        }

        fn notify(&self, notification: NotificationView) {
            self.tell(Told::Notify(notification));
        }

        fn withdraw(&self, id: String) {
            self.tell(Told::Withdraw(id));
        }

        fn start_ringtone(&self) {
            self.tell(Told::StartRingtone);
        }

        fn stop_ringtone(&self) {
            self.tell(Told::StopRingtone);
        }

        fn present_window(&self) {
            self.tell(Told::PresentWindow);
        }
    }

    #[tokio::test]
    async fn the_opener_asks_the_host() {
        let host = Arc::new(RecordingHost::default());
        let opener = HostOpener(host.clone());
        assert!(opener.open("https://www.distronode.com/x").await);
        assert_eq!(
            *host.opened.lock().unwrap(),
            ["https://www.distronode.com/x"]
        );
        let refusing = HostOpener(Arc::new(RecordingHost {
            refuse: true,
            ..RecordingHost::default()
        }));
        assert!(!refusing.open("https://www.distronode.com/y").await);
    }

    #[tokio::test]
    async fn a_page_inside_the_app_is_the_hosts_checkout_window() {
        let host = Arc::new(RecordingHost::default());
        let opener = HostOpener(host.clone());
        assert!(
            opener
                .open_embedded("https://www.distronode.com/a", EmbeddedView::NewPrivate)
                .await
        );
        assert!(
            opener
                .open_embedded("https://www.distronode.com/b", EmbeddedView::Same)
                .await
        );
        assert_eq!(
            *host.opened.lock().unwrap(),
            [
                "NewPrivate https://www.distronode.com/a",
                "Same https://www.distronode.com/b"
            ]
        );
        let refusing = HostOpener(Arc::new(RecordingHost {
            refuse: true,
            ..RecordingHost::default()
        }));
        assert!(
            !refusing
                .open_embedded("https://www.distronode.com/c", EmbeddedView::Same)
                .await
        );
    }

    /// A ringing call's notification, as the core makes it.
    pub(crate) fn ringing(call_id: &str) -> Notification {
        Notification {
            id: format!("call:{call_id}"),
            title: "Incoming call".to_owned(),
            body: "Transferred from your AI receptionist.".to_owned(),
            urgency: Urgency::Urgent,
            actions: vec![
                NotificationAction::Answer {
                    call_id: call_id.to_owned(),
                },
                NotificationAction::Decline {
                    call_id: call_id.to_owned(),
                },
            ],
            target: NotificationTarget::IncomingCall {
                workspace_id: "ws-1".to_owned(),
                call_id: call_id.to_owned(),
            },
        }
    }

    fn message(message_id: &str) -> Notification {
        Notification {
            id: format!("message:{message_id}"),
            title: "New message".to_owned(),
            body: "Open District AI to read it.".to_owned(),
            urgency: Urgency::Normal,
            actions: Vec::new(),
            target: NotificationTarget::Message {
                workspace_id: "ws-1".to_owned(),
                message_id: message_id.to_owned(),
            },
        }
    }

    #[test]
    fn a_notification_crosses_as_ids_and_words() {
        assert_eq!(
            NotificationView::from(&ringing("call-1")),
            NotificationView {
                id: "call:call-1".to_owned(),
                title: "Incoming call".to_owned(),
                body: "Transferred from your AI receptionist.".to_owned(),
                urgent: true,
                actions: vec![
                    NotificationActionView {
                        label: "Answer".to_owned(),
                        action_id: "answer".to_owned(),
                    },
                    NotificationActionView {
                        label: "Decline".to_owned(),
                        action_id: "decline".to_owned(),
                    },
                ],
                kind: NotificationKind::IncomingCall,
                open_target: "1|incoming-call|ws-1|call-1".to_owned(),
            }
        );
        assert_eq!(
            NotificationView::from(&message("m-1")),
            NotificationView {
                id: "message:m-1".to_owned(),
                title: "New message".to_owned(),
                body: "Open District AI to read it.".to_owned(),
                urgent: false,
                actions: Vec::new(),
                kind: NotificationKind::Message,
                open_target: "1|message|ws-1|m-1".to_owned(),
            }
        );
        let mut missed = ringing("call-1");
        missed.target = NotificationTarget::Call {
            workspace_id: "ws-1".to_owned(),
            call_id: "call-1".to_owned(),
        };
        assert_eq!(NotificationView::from(&missed).kind, NotificationKind::Call);
        assert_eq!(
            NotificationView::from(&missed).open_target,
            "1|call|ws-1|call-1"
        );
        // An id that cannot be carried leaves the target empty.
        missed.target = NotificationTarget::Call {
            workspace_id: "ws-1".to_owned(),
            call_id: "call|1".to_owned(),
        };
        assert_eq!(NotificationView::from(&missed).open_target, "");
    }

    #[test]
    fn an_open_target_reads_back_as_written() {
        let targets = [
            NotificationTarget::IncomingCall {
                workspace_id: "ws_1".to_owned(),
                call_id: "CA0123456789abcdef".to_owned(),
            },
            NotificationTarget::Call {
                workspace_id: "ws-1".to_owned(),
                call_id: "call_contract_answered".to_owned(),
            },
            NotificationTarget::Message {
                workspace_id: "cmg2x.9:a".to_owned(),
                message_id: "m".repeat(OPEN_TARGET_ID_MAX),
            },
        ];
        for target in targets {
            let text = encode_open_target(&target).expect("carriable");
            assert_eq!(parse_open_target(&text), Some(target));
        }
    }

    #[test]
    fn an_open_target_with_ids_it_cannot_carry_is_not_written() {
        let long = "x".repeat(OPEN_TARGET_ID_MAX + 1);
        for (workspace_id, call_id) in [
            ("", "call-1"),
            ("ws-1", ""),
            ("ws|1", "call-1"),
            ("ws-1", "call;1"),
            ("ws-1", "call=1"),
            ("ws 1", "call-1"),
            ("ws-1", "caf\u{e9}"),
            ("ws-1", long.as_str()),
        ] {
            let target = NotificationTarget::Call {
                workspace_id: workspace_id.to_owned(),
                call_id: call_id.to_owned(),
            };
            assert_eq!(
                encode_open_target(&target),
                None,
                "{workspace_id:?} {call_id:?}"
            );
        }
    }

    #[test]
    fn a_garbled_open_target_is_refused() {
        let long = format!("1|call|ws-1|{}", "x".repeat(OPEN_TARGET_ID_MAX + 1));
        for text in [
            "",
            "|",
            "1",
            "1|message",
            "1|message|ws-1",
            "1|message|ws-1|",
            "1|message||m-1",
            "1|message|ws-1|m-1|",
            "1|message|ws-1|m-1|extra",
            "2|message|ws-1|m-1",
            "01|message|ws-1|m-1",
            " 1|message|ws-1|m-1",
            "1|Message|ws-1|m-1",
            "1|thread|ws-1|m-1",
            "1|voicemail|ws-1|m-1",
            "1||ws-1|m-1",
            "1|message|ws 1|m-1",
            "1|message|ws-1|m-1\n",
            "1|message|ws-1|m=1",
            "1;message;ws-1;m-1",
            long.as_str(),
        ] {
            assert_eq!(parse_open_target(text), None, "{text:?}");
        }
    }

    /// A toast an earlier process showed, so the table does not know it: its
    /// body opens the target it carries, and its buttons do nothing.
    #[test]
    fn a_toast_the_table_forgot_opens_what_it_carries() {
        let table = NotificationTable::default();
        let carried = encode_open_target(&ringing("call-1").target).unwrap();
        assert_eq!(
            table.activate_toast("call:call-1", None, Some(&carried)),
            Some(Event::OpenNotification(NotificationTarget::IncomingCall {
                workspace_id: "ws-1".to_owned(),
                call_id: "call-1".to_owned(),
            }))
        );
        assert_eq!(
            table.activate_toast("message:m-1", None, Some("1|message|ws-2|m-1")),
            Some(Event::OpenNotification(NotificationTarget::Message {
                workspace_id: "ws-2".to_owned(),
                message_id: "m-1".to_owned(),
            }))
        );
        // The ring ended with the process: a stale Answer or Decline is ignored.
        assert_eq!(
            table.activate_toast("call:call-1", Some("answer"), Some(&carried)),
            None
        );
        assert_eq!(
            table.activate_toast("call:call-1", Some("decline"), Some(&carried)),
            None
        );
        // No target, or a garbled one: only the window shows, as before.
        assert_eq!(table.activate_toast("call:call-1", None, None), None);
        assert_eq!(
            table.activate_toast("call:call-1", None, Some("1|ring|ws-1|call-1")),
            None
        );
        assert_eq!(table.activate_toast("call:call-1", None, Some("")), None);
    }

    /// While the table knows the toast, it answers, whatever the toast carries.
    #[test]
    fn a_toast_the_table_knows_is_answered_by_the_table() {
        let table = NotificationTable::default();
        table.shown(&ringing("call-1"));
        let elsewhere = Some("1|message|ws-9|m-9");
        assert_eq!(
            table.activate_toast("call:call-1", None, elsewhere),
            table.activate("call:call-1", None)
        );
        assert_eq!(
            table.activate_toast("call:call-1", Some("answer"), elsewhere),
            Some(Event::Ring(RingEvent::Answer {
                call_id: "call-1".to_owned(),
            }))
        );
        // An action the known notification does not have stays nothing, and
        // is not read as a click on the body.
        assert_eq!(
            table.activate_toast("call:call-1", Some("snooze"), elsewhere),
            None
        );
    }

    #[test]
    fn the_notifier_tells_the_host_and_remembers_what_it_showed() {
        let host = Arc::new(RecordingHost::default());
        let table = Arc::new(NotificationTable::default());
        let notifier = HostNotifier {
            host: host.clone(),
            table: Arc::clone(&table),
        };
        notifier.notify(&ringing("call-1"));
        notifier.withdraw("call:call-1");
        assert_eq!(
            *host.told.lock().unwrap(),
            [
                Told::Notify(NotificationView::from(&ringing("call-1"))),
                Told::Withdraw("call:call-1".to_owned()),
            ]
        );
        // Withdrawn, so a late click on it does nothing.
        assert_eq!(table.activate("call:call-1", None), None);
    }

    /// A new message's notification is shown, and remembered so its toast
    /// opens the conversation through the core; the core's withdrawal takes
    /// it away again.
    #[test]
    fn a_message_notification_is_shown_opened_and_withdrawn() {
        let host = Arc::new(RecordingHost::default());
        let table = Arc::new(NotificationTable::default());
        let notifier = HostNotifier {
            host: host.clone(),
            table: Arc::clone(&table),
        };
        notifier.notify(&message("m-1"));
        assert_eq!(
            *host.told.lock().unwrap(),
            [Told::Notify(NotificationView::from(&message("m-1")))]
        );
        assert_eq!(
            table.activate("message:m-1", None),
            Some(Event::OpenNotification(NotificationTarget::Message {
                workspace_id: "ws-1".to_owned(),
                message_id: "m-1".to_owned(),
            }))
        );
        // A message has no buttons, so no action id means anything.
        assert_eq!(table.activate("message:m-1", Some("answer")), None);
        notifier.withdraw("message:m-1");
        assert_eq!(
            host.told.lock().unwrap().last(),
            Some(&Told::Withdraw("message:m-1".to_owned()))
        );
        assert_eq!(table.activate("message:m-1", None), None);
    }

    #[test]
    fn activating_maps_back_to_the_target_or_the_action() {
        let table = NotificationTable::default();
        table.shown(&ringing("call-1"));
        table.shown(&message("m-1"));
        assert_eq!(
            table.activate("call:call-1", None),
            Some(Event::OpenNotification(NotificationTarget::IncomingCall {
                workspace_id: "ws-1".to_owned(),
                call_id: "call-1".to_owned(),
            }))
        );
        assert_eq!(
            table.activate("call:call-1", Some("answer")),
            Some(Event::Ring(RingEvent::Answer {
                call_id: "call-1".to_owned(),
            }))
        );
        assert_eq!(
            table.activate("call:call-1", Some("decline")),
            Some(Event::Ring(RingEvent::Decline {
                call_id: "call-1".to_owned(),
            }))
        );
        assert_eq!(
            table.activate("message:m-1", None),
            Some(Event::OpenNotification(NotificationTarget::Message {
                workspace_id: "ws-1".to_owned(),
                message_id: "m-1".to_owned(),
            }))
        );
        // An action the notification does not have, and one never shown.
        assert_eq!(table.activate("message:m-1", Some("answer")), None);
        assert_eq!(table.activate("call:call-1", Some("snooze")), None);
        assert_eq!(table.activate("call:other", None), None);
    }

    #[test]
    fn a_notification_with_the_same_id_replaces_the_last() {
        let table = NotificationTable::default();
        table.shown(&ringing("call-1"));
        let mut missed = ringing("call-1");
        missed.actions.clear();
        missed.target = NotificationTarget::Call {
            workspace_id: "ws-1".to_owned(),
            call_id: "call-1".to_owned(),
        };
        table.shown(&missed);
        assert_eq!(table.lock().len(), 1);
        assert_eq!(table.activate("call:call-1", Some("answer")), None);
        assert_eq!(
            table.activate("call:call-1", None),
            Some(Event::OpenNotification(missed.target))
        );
    }

    #[test]
    fn the_table_keeps_the_most_recent_and_forgets_the_oldest() {
        let table = NotificationTable::default();
        for n in 0..=NotificationTable::CAPACITY {
            table.shown(&message(&format!("m-{n}")));
        }
        assert_eq!(table.lock().len(), NotificationTable::CAPACITY);
        assert_eq!(table.activate("message:m-0", None), None);
        let last = format!("message:m-{}", NotificationTable::CAPACITY);
        assert!(table.activate(&last, None).is_some());
    }

    #[test]
    fn a_poisoned_table_still_answers() {
        let table = Arc::new(NotificationTable::default());
        table.shown(&message("m-1"));
        let poisoner = Arc::clone(&table);
        std::thread::spawn(move || {
            let _held = poisoner.0.lock().unwrap();
            panic!("poison the lock");
        })
        .join()
        .unwrap_err();
        assert!(table.activate("message:m-1", None).is_some());
    }

    #[test]
    fn the_ring_surface_asks_the_host() {
        let host = Arc::new(RecordingHost::default());
        let ring = HostRing(host.clone());
        ring.start_ringtone();
        ring.stop_ringtone();
        ring.present_window();
        assert_eq!(
            *host.told.lock().unwrap(),
            [Told::StartRingtone, Told::StopRingtone, Told::PresentWindow]
        );
    }
}
