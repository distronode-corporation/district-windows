//! What the window forwards: the user's actions, and the machine's.

use district_core::{
    CallsEvent, ContactsEvent, DevicesEvent, Event, InboxEvent, Route, Tab, ThreadEvent,
};

use crate::report;
use crate::shell::TabView;
use crate::views::ReportTarget;

/// Something the user did in the window. Most are one core event, and Report
/// is four ([`UiEvent::events`]); the core decides what each means in the
/// state it is in.
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
    /// Open another workspace from the switcher.
    SelectWorkspace {
        /// Which one.
        workspace_id: String,
    },
    /// The overview's "Open the web dashboard" card.
    OpenFinishSetup,
    /// Open a conversation. The core marks it read as it opens it.
    OpenThread {
        /// Which one.
        thread_key: String,
    },
    /// Read the open conversation's older events.
    LoadOlder,
    /// The inbox's search box changed.
    Search {
        /// What it holds now.
        query: String,
    },
    /// The inbox's search box was cleared.
    ClearSearch,
    /// Open a call.
    OpenCall {
        /// Which one.
        call_id: String,
    },
    /// Read the next page of the call log.
    LoadMoreCalls,
    /// Open a contact.
    OpenContact {
        /// Which one.
        contact_id: String,
    },
    /// Read the next page of contacts.
    LoadMoreContacts,
    /// Open the devices list, from the account screen.
    OpenDevices,
    /// Ask before signing a device out.
    AskSignOutDevice {
        /// Which one.
        device_id: String,
    },
    /// Ask before signing out every device.
    AskSignOutEverywhere,
    /// Answer the devices screen's question yes.
    ConfirmDevices,
    /// Answer it no.
    CancelDevices,
    /// Put away the devices screen's failure and notes.
    DismissDevicesNotices,
    /// Open the account deletion page in the browser.
    DeleteAccount,
    /// Report AI-generated content: raises a support request without leaving
    /// the screen. Its progress is [`ShellView::report`](crate::ShellView::report).
    Report {
        /// What is reported.
        target: ReportTarget,
        /// The member's note, which may be empty. Trimmed, and cut to
        /// [`NOTE_LIMIT`](crate::NOTE_LIMIT) characters.
        note: String,
    },
    /// Put away a report's outcome.
    DismissReport,
}

impl UiEvent {
    /// The core events this action is, in the order the core is to hear them.
    pub fn events(self) -> Vec<Event> {
        let one = match self {
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
            UiEvent::SelectWorkspace { workspace_id } => Event::SelectWorkspace(workspace_id),
            UiEvent::OpenFinishSetup => Event::OpenFinishSetup,
            UiEvent::OpenThread { thread_key } => Event::Navigate(Route::Thread { thread_key }),
            UiEvent::LoadOlder => Event::Thread(ThreadEvent::LoadOlder),
            UiEvent::Search { query } => Event::Inbox(InboxEvent::Search(query)),
            UiEvent::ClearSearch => Event::Inbox(InboxEvent::ClearSearch),
            UiEvent::OpenCall { call_id } => Event::Navigate(Route::CallDetail { call_id }),
            UiEvent::LoadMoreCalls => Event::Calls(CallsEvent::LoadMore),
            UiEvent::OpenContact { contact_id } => {
                Event::Navigate(Route::ContactDetail { contact_id })
            }
            UiEvent::LoadMoreContacts => Event::Contacts(ContactsEvent::LoadMore),
            UiEvent::OpenDevices => Event::Navigate(Route::Devices),
            UiEvent::AskSignOutDevice { device_id } => {
                Event::Devices(DevicesEvent::AskSignOut { device_id })
            }
            UiEvent::AskSignOutEverywhere => Event::Devices(DevicesEvent::AskSignOutEverywhere),
            UiEvent::ConfirmDevices => Event::Devices(DevicesEvent::Confirm),
            UiEvent::CancelDevices => Event::Devices(DevicesEvent::Cancel),
            UiEvent::DismissDevicesNotices => Event::Devices(DevicesEvent::DismissNotices),
            UiEvent::DeleteAccount => Event::DeleteAccount,
            UiEvent::Report { target, note } => return report::events(&target, &note),
            UiEvent::DismissReport => return report::dismiss(),
        };
        vec![one]
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
    use district_core::{SupportEvent, SupportForm};
    use district_model::SupportRequestKind;

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
            (
                UiEvent::SelectWorkspace {
                    workspace_id: "ws-2".to_owned(),
                },
                Event::SelectWorkspace("ws-2".to_owned()),
            ),
            (UiEvent::OpenFinishSetup, Event::OpenFinishSetup),
            (
                UiEvent::OpenThread {
                    thread_key: "contact:c-1".to_owned(),
                },
                Event::Navigate(Route::Thread {
                    thread_key: "contact:c-1".to_owned(),
                }),
            ),
            (UiEvent::LoadOlder, Event::Thread(ThreadEvent::LoadOlder)),
            (
                UiEvent::Search {
                    query: "roof".to_owned(),
                },
                Event::Inbox(InboxEvent::Search("roof".to_owned())),
            ),
            (UiEvent::ClearSearch, Event::Inbox(InboxEvent::ClearSearch)),
            (
                UiEvent::OpenCall {
                    call_id: "call-1".to_owned(),
                },
                Event::Navigate(Route::CallDetail {
                    call_id: "call-1".to_owned(),
                }),
            ),
            (UiEvent::LoadMoreCalls, Event::Calls(CallsEvent::LoadMore)),
            (
                UiEvent::OpenContact {
                    contact_id: "c-1".to_owned(),
                },
                Event::Navigate(Route::ContactDetail {
                    contact_id: "c-1".to_owned(),
                }),
            ),
            (
                UiEvent::LoadMoreContacts,
                Event::Contacts(ContactsEvent::LoadMore),
            ),
            (UiEvent::OpenDevices, Event::Navigate(Route::Devices)),
            (
                UiEvent::AskSignOutDevice {
                    device_id: "d-1".to_owned(),
                },
                Event::Devices(DevicesEvent::AskSignOut {
                    device_id: "d-1".to_owned(),
                }),
            ),
            (
                UiEvent::AskSignOutEverywhere,
                Event::Devices(DevicesEvent::AskSignOutEverywhere),
            ),
            (
                UiEvent::ConfirmDevices,
                Event::Devices(DevicesEvent::Confirm),
            ),
            (UiEvent::CancelDevices, Event::Devices(DevicesEvent::Cancel)),
            (
                UiEvent::DismissDevicesNotices,
                Event::Devices(DevicesEvent::DismissNotices),
            ),
            (UiEvent::DeleteAccount, Event::DeleteAccount),
            (UiEvent::Refresh, Event::Refresh),
            (UiEvent::DismissNotice, Event::DismissNotice),
            (
                UiEvent::WindowVisible { visible: false },
                Event::WindowVisible(false),
            ),
        ];
        for (action, event) in pairs {
            assert_eq!(action.events(), [event]);
        }
        assert_eq!(Event::from(PowerChange::Suspending), Event::Suspending);
        assert_eq!(Event::from(PowerChange::Resumed), Event::Resumed);
    }

    /// Report is four events, in order: drop a leftover draft, open the form,
    /// fill it in, send it.
    #[test]
    fn a_report_opens_fills_and_sends_the_support_form() {
        let events = UiEvent::Report {
            target: ReportTarget::Call {
                call_id: "call-1".to_owned(),
            },
            note: "  Wrong caller name.  ".to_owned(),
        }
        .events();
        assert_eq!(
            events,
            [
                Event::Support(SupportEvent::CancelRequest),
                Event::Support(SupportEvent::StartRequest),
                Event::Support(SupportEvent::EditRequest(SupportForm {
                    kind: SupportRequestKind::Problem,
                    subject: "Report: AI-generated content".to_owned(),
                    message: format!("{}\nCall: call-1\n\nWrong caller name.", report::PREAMBLE),
                })),
                Event::Support(SupportEvent::SubmitRequest),
            ]
        );
        assert_eq!(
            UiEvent::DismissReport.events(),
            [
                Event::Support(SupportEvent::DismissSubmitted),
                Event::Support(SupportEvent::CancelRequest),
            ]
        );
    }
}
