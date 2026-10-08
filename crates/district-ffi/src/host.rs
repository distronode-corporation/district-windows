//! What the core asks of the C# side, and the runner's traits built on it.

use std::future::Future;
use std::sync::Arc;

use district_core::{Notification, Notifier, RingSurface, UrlOpener};

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

/// [`Notifier`] until the window shows notifications (the message toasts and
/// the incoming call's). This build has no calls, so nothing urgent can be
/// lost; a message notification is not shown.
#[derive(Clone, Copy, Debug, Default)]
pub(crate) struct NoNotifier;

impl Notifier for NoNotifier {
    fn notify(&self, _notification: &Notification) {}
    fn withdraw(&self, _id: &str) {}
}

/// [`RingSurface`] for a build without calls: the core never rings one
/// (`calls_available` is false), so there is nothing to sound or raise.
#[derive(Clone, Copy, Debug, Default)]
pub(crate) struct NoRing;

impl RingSurface for NoRing {
    fn start_ringtone(&self) {}
    fn stop_ringtone(&self) {}
    fn present_window(&self) {}
}

#[cfg(test)]
pub(crate) mod tests {
    use std::sync::Mutex;
    use std::sync::atomic::{AtomicU64, Ordering};

    use super::*;

    /// A host that records what it was told and opens every page it is given.
    #[derive(Default)]
    pub(crate) struct RecordingHost {
        pub(crate) revision: AtomicU64,
        pub(crate) opened: Mutex<Vec<String>>,
        pub(crate) refuse: bool,
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

    #[test]
    fn the_stand_ins_do_nothing() {
        NoNotifier.notify(&Notification {
            id: "id".to_owned(),
            title: "Title".to_owned(),
            body: "Body".to_owned(),
            urgency: district_core::Urgency::Normal,
            actions: Vec::new(),
            target: district_core::NotificationTarget::Message {
                workspace_id: "ws-1".to_owned(),
                message_id: "m-1".to_owned(),
            },
        });
        NoNotifier.withdraw("id");
        NoRing.start_ringtone();
        NoRing.stop_ringtone();
        NoRing.present_window();
    }
}
