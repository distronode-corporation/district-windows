//! What the window forwards: the user's actions, and the machine's.

use district_core::{Event, Tab};

use crate::shell::TabView;

/// Something the user did in the window. Each is one core event; the core
/// decides what it means in the state it is in.
#[derive(Clone, Debug, PartialEq, Eq, uniffi::Enum)]
pub enum UiEvent {
    /// "Sign in with your browser".
    SignIn,
    /// "Cancel", while signing in.
    CancelSignIn,
    /// "Try again", after a start-up check that could not finish.
    RetryRestore,
    /// "Sign out again", after a sign-out that left something behind.
    RetrySignOut,
    /// "Sign out", from the account screen.
    SignOut,
    /// A tab of the navigation view.
    OpenTab {
        /// Which one.
        tab: TabView,
    },
    /// The back button.
    Back,
    /// Refresh the screen showing.
    Refresh,
    /// The notice over every screen was dismissed.
    DismissNotice,
    /// The window was shown or hidden (minimised, or closed to the tray).
    WindowVisible {
        /// Whether it is showing now.
        visible: bool,
    },
}

impl From<UiEvent> for Event {
    fn from(event: UiEvent) -> Self {
        match event {
            UiEvent::SignIn => Event::SignIn,
            UiEvent::CancelSignIn => Event::CancelSignIn,
            UiEvent::RetryRestore => Event::RetryRestore,
            UiEvent::RetrySignOut => Event::RetrySignOut,
            UiEvent::SignOut => Event::SignOut,
            UiEvent::OpenTab { tab } => Event::Navigate(Tab::from(tab).route()),
            UiEvent::Back => Event::Back,
            UiEvent::Refresh => Event::Refresh,
            UiEvent::DismissNotice => Event::DismissNotice,
            UiEvent::WindowVisible { visible } => Event::WindowVisible(visible),
        }
    }
}

/// The machine going to sleep or waking, as C# hears it from
/// `PowerRegisterSuspendResumeNotification`.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash, uniffi::Enum)]
pub enum PowerChange {
    /// About to sleep.
    Suspending,
    /// Awake again.
    Resumed,
}

impl From<PowerChange> for Event {
    fn from(change: PowerChange) -> Self {
        match change {
            PowerChange::Suspending => Event::Suspending,
            PowerChange::Resumed => Event::Resumed,
        }
    }
}

#[cfg(test)]
mod tests {
    use district_core::Route;

    use super::*;

    #[test]
    fn each_action_is_its_core_event() {
        let pairs = [
            (UiEvent::SignIn, Event::SignIn),
            (UiEvent::CancelSignIn, Event::CancelSignIn),
            (UiEvent::RetryRestore, Event::RetryRestore),
            (UiEvent::RetrySignOut, Event::RetrySignOut),
            (UiEvent::SignOut, Event::SignOut),
            (
                UiEvent::OpenTab {
                    tab: TabView::Calls,
                },
                Event::Navigate(Route::Calls),
            ),
            (UiEvent::Back, Event::Back),
            (UiEvent::Refresh, Event::Refresh),
            (UiEvent::DismissNotice, Event::DismissNotice),
            (
                UiEvent::WindowVisible { visible: false },
                Event::WindowVisible(false),
            ),
        ];
        for (action, event) in pairs {
            assert_eq!(Event::from(action), event);
        }
        assert_eq!(Event::from(PowerChange::Suspending), Event::Suspending);
        assert_eq!(Event::from(PowerChange::Resumed), Event::Resumed);
    }
}
