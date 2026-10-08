//! What the core asks of the C# side, and the runner's traits built on it.

use std::collections::VecDeque;
use std::future::Future;
use std::sync::{Arc, Mutex, PoisonError};

use district_core::{
    Event, Notification, NotificationAction, Notifier, RingSurface, Urgency, UrlOpener,
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
    /// Its buttons, in order.
    pub actions: Vec<NotificationActionView>,
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
        }
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

    /// The event activating the notification `id` sends: opening its target
    /// when `action_id` is `None` (the toast itself was clicked), or the event
    /// of the action named. `None` for a notification or an action this table
    /// does not know.
    pub(crate) fn activate(&self, id: &str, action_id: Option<&str>) -> Option<Event> {
        let shown = self.lock();
        let notification = shown.iter().find(|shown| shown.id == id)?;
        match action_id {
            None => Some(Event::OpenNotification(notification.target.clone())),
            Some(wanted) => notification
                .actions
                .iter()
                .find(|action| self::action_id(action) == wanted)
                .map(NotificationAction::event),
        }
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
}

/// [`Notifier`] over the host: each notification goes to C# as a
/// [`NotificationView`], and is remembered in the table that
/// [`Core::activate_notification`](crate::Core::activate_notification) reads.
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

    use district_core::{NotificationTarget, RingEvent};

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
            }
        );
        let view = NotificationView::from(&message("m-1"));
        assert!(!view.urgent);
        assert!(view.actions.is_empty());
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
