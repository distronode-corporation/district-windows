//! What the window forwards: the user's actions.

use district_core::{
    CallEvent, CallsEvent, ContactsEvent, DevicesEvent, DialerEvent, Event, InboxEvent, RingEvent,
    Route, Tab, ThreadEvent,
};

use crate::nav::NavDestination;
use crate::report;
use crate::shell::TabView;
use crate::views::ReportTarget;
use crate::{
    analytics, billing, blocked, composer, contacts, desk, hq, marketplace, rooms, scheduling,
    settings, support, workflows,
};

/// Something the user did in the window. Most are one core event, and Report
/// is four ([`UiEvent::events`]); the core decides what each means in the
/// state it is in. Each area of 2.0 has one variant carrying its own action,
/// which its module turns into core events.
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
    /// An entry of the navigation pane.
    Navigate {
        /// Which one.
        destination: NavDestination,
    },
    /// One of 1.0's five tabs. Superseded by [`UiEvent::Navigate`], which the
    /// window sends now; kept while the boundary's own tests still send it.
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
    /// "Discard" in answer to "Discard your changes?"
    /// ([`ShellView::discard`](crate::ShellView::discard)): the move that was
    /// held goes to the core as it was. The actor handles it; it is no core
    /// event of its own.
    DiscardChanges,
    /// "Keep editing" in answer to it: the held move is dropped. The actor
    /// handles it; it is no core event of its own.
    KeepEditing,
    /// Open the dialler.
    OpenDialer,
    /// The dialler's number changed: what the box holds now, exactly as typed.
    DialerEdit {
        /// The number.
        number: String,
    },
    /// The dialler's "Call" (or Enter in its box).
    Dial,
    /// Call `number` from anywhere (a call's or a contact's "Call back"): the
    /// dialler is given the number, then dialled. Two core events.
    CallNumber {
        /// The number, as the service wrote it.
        number: String,
    },
    /// Hang up the call under way, or abandon a dial on its way.
    HangUp,
    /// Put an ended call's summary away.
    DismissCall,
    /// Answer the call ringing.
    Answer {
        /// The call, as the ring names it.
        call_id: String,
    },
    /// Decline it. Nothing is sent to the service.
    Decline {
        /// The call.
        call_id: String,
    },
    /// Put an ended ring's line away.
    DismissRing,
    /// Turn the microphone on or off, during a call.
    Microphone {
        /// On, or off.
        on: bool,
    },
    /// The account screen's "Ring on this computer".
    SetRingOnThisComputer {
        /// On, or off.
        on: bool,
    },
    /// Something done on the contacts or a contact: adding, editing,
    /// deleting, research, blocking.
    Contacts {
        /// What.
        action: crate::contacts::ContactsAction,
    },
    /// Something done on the blocked callers.
    Blocked {
        /// What.
        action: crate::blocked::BlockedAction,
    },
    /// Something done on District HQ.
    Hq {
        /// What.
        action: crate::hq::HqAction,
    },
    /// Something done on the analytics.
    Analytics {
        /// What.
        action: crate::analytics::AnalyticsAction,
    },
    /// Something done on the phone numbers.
    Marketplace {
        /// What.
        action: crate::marketplace::MarketplaceAction,
    },
    /// Something done on billing.
    Billing {
        /// What.
        action: crate::billing::BillingAction,
    },
    /// Something done on the workflows.
    Workflows {
        /// What.
        action: crate::workflows::WorkflowsAction,
    },
    /// Something done on the booking pages.
    Scheduling {
        /// What.
        action: crate::scheduling::SchedulingAction,
    },
    /// Something done on the help desk.
    Desk {
        /// What.
        action: crate::desk::DeskAction,
    },
    /// Something done on the support requests.
    Support {
        /// What.
        action: crate::support::SupportAction,
    },
    /// Something done on the meeting rooms.
    Rooms {
        /// What.
        action: crate::rooms::RoomsAction,
    },
    /// Something done on the conversation's reply box.
    Composer {
        /// What.
        action: crate::composer::ComposerAction,
    },
    /// Something done on the workspace settings hub.
    Settings {
        /// What.
        action: crate::settings::SettingsAction,
    },
    /// Something done on the persona section.
    Persona {
        /// What.
        action: crate::settings::persona::PersonaAction,
    },
    /// Something done on the Voice section.
    VoiceStudio {
        /// What.
        action: crate::settings::voice_studio::VoiceStudioAction,
    },
    /// Something done on the call handling section.
    CallHandling {
        /// What.
        action: crate::settings::call_handling::CallHandlingAction,
    },
    /// Something done on the routing rules section.
    Routing {
        /// What.
        action: crate::settings::routing::RoutingAction,
    },
    /// Something done on the transfer directory section.
    Directory {
        /// What.
        action: crate::settings::directory::DirectoryAction,
    },
    /// Something done on the Skills section.
    Tools {
        /// What.
        action: crate::settings::tools::ToolsAction,
    },
    /// Something done on the knowledge base section.
    Knowledge {
        /// What.
        action: crate::settings::knowledge::KnowledgeAction,
    },
    /// Something done on the messaging accounts section.
    Messaging {
        /// What.
        action: crate::settings::messaging::MessagingAction,
    },
    /// Something done on the members section.
    Members {
        /// What.
        action: crate::settings::members::MembersAction,
    },
    /// Something done on the phone numbers section.
    Numbers {
        /// What.
        action: crate::settings::numbers::NumbersAction,
    },
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
            UiEvent::Navigate { destination } => Event::Navigate(destination.route()),
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
            // The actor's (crate::guard::Held): no core event of their own.
            UiEvent::DiscardChanges | UiEvent::KeepEditing => return Vec::new(),
            UiEvent::OpenDialer => Event::Navigate(Route::Dialer),
            UiEvent::DialerEdit { number } => Event::Dialer(DialerEvent::Edit(number)),
            UiEvent::Dial => Event::Dialer(DialerEvent::Dial),
            UiEvent::CallNumber { number } => {
                return vec![
                    Event::Dialer(DialerEvent::Edit(number)),
                    Event::Dialer(DialerEvent::Dial),
                ];
            }
            UiEvent::HangUp => Event::Call(CallEvent::HangUp),
            UiEvent::DismissCall => Event::Call(CallEvent::Dismiss),
            UiEvent::Answer { call_id } => Event::Ring(RingEvent::Answer { call_id }),
            UiEvent::Decline { call_id } => Event::Ring(RingEvent::Decline { call_id }),
            UiEvent::DismissRing => Event::Ring(RingEvent::Dismiss),
            UiEvent::Microphone { on } => Event::Microphone(on),
            UiEvent::SetRingOnThisComputer { on } => Event::SetRingOnThisComputer(on),
            UiEvent::Contacts { action } => return contacts::events(action),
            UiEvent::Blocked { action } => return blocked::events(action),
            UiEvent::Hq { action } => return hq::events(action),
            UiEvent::Analytics { action } => return analytics::events(action),
            UiEvent::Marketplace { action } => return marketplace::events(action),
            UiEvent::Billing { action } => return billing::events(action),
            UiEvent::Workflows { action } => return workflows::events(action),
            UiEvent::Scheduling { action } => return scheduling::events(action),
            UiEvent::Desk { action } => return desk::events(action),
            UiEvent::Support { action } => return support::events(action),
            UiEvent::Rooms { action } => return rooms::events(action),
            UiEvent::Composer { action } => return composer::events(action),
            UiEvent::Settings { action } => return settings::events(action),
            UiEvent::Persona { action } => return settings::persona::events(action),
            UiEvent::VoiceStudio { action } => return settings::voice_studio::events(action),
            UiEvent::CallHandling { action } => return settings::call_handling::events(action),
            UiEvent::Routing { action } => return settings::routing::events(action),
            UiEvent::Directory { action } => return settings::directory::events(action),
            UiEvent::Tools { action } => return settings::tools::events(action),
            UiEvent::Knowledge { action } => return settings::knowledge::events(action),
            UiEvent::Messaging { action } => return settings::messaging::events(action),
            UiEvent::Members { action } => return settings::members::events(action),
            UiEvent::Numbers { action } => return settings::numbers::events(action),
        };
        vec![one]
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
            (
                UiEvent::Navigate {
                    destination: NavDestination::Settings,
                },
                Event::Navigate(Route::Workspace(district_core::WorkspaceSection::Hub)),
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
            (UiEvent::OpenDialer, Event::Navigate(Route::Dialer)),
            (
                UiEvent::DialerEdit {
                    number: "+1 212".to_owned(),
                },
                Event::Dialer(DialerEvent::Edit("+1 212".to_owned())),
            ),
            (UiEvent::Dial, Event::Dialer(DialerEvent::Dial)),
            (UiEvent::HangUp, Event::Call(CallEvent::HangUp)),
            (UiEvent::DismissCall, Event::Call(CallEvent::Dismiss)),
            (
                UiEvent::Answer {
                    call_id: "call-1".to_owned(),
                },
                Event::Ring(RingEvent::Answer {
                    call_id: "call-1".to_owned(),
                }),
            ),
            (
                UiEvent::Decline {
                    call_id: "call-1".to_owned(),
                },
                Event::Ring(RingEvent::Decline {
                    call_id: "call-1".to_owned(),
                }),
            ),
            (UiEvent::DismissRing, Event::Ring(RingEvent::Dismiss)),
            (UiEvent::Microphone { on: false }, Event::Microphone(false)),
            (
                UiEvent::SetRingOnThisComputer { on: true },
                Event::SetRingOnThisComputer(true),
            ),
        ];
        for (action, event) in pairs {
            assert_eq!(action.events(), [event]);
        }
        // Call back: the number into the dialler, then Call, in that order.
        assert_eq!(
            UiEvent::CallNumber {
                number: "+12125550142".to_owned(),
            }
            .events(),
            [
                Event::Dialer(DialerEvent::Edit("+12125550142".to_owned())),
                Event::Dialer(DialerEvent::Dial),
            ]
        );
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

    /// Every area's variant crosses to C# and back intact, through UniFFI's
    /// own converters (the ones the generated bindings call), and is core
    /// events.
    #[test]
    fn every_area_action_survives_the_trip_to_csharp() {
        use uniffi::{Lift, Lower};
        for action in [
            UiEvent::Navigate {
                destination: NavDestination::Hq,
            },
            UiEvent::Blocked {
                action: crate::blocked::BlockedAction::Open,
            },
            UiEvent::Hq {
                action: crate::hq::HqAction::Retry,
            },
            UiEvent::Analytics {
                action: crate::analytics::AnalyticsAction::Open,
            },
            UiEvent::Marketplace {
                action: crate::marketplace::MarketplaceAction::OpenWeb,
            },
            UiEvent::Billing {
                action: crate::billing::BillingAction::ManageOnWeb,
            },
            UiEvent::Workflows {
                action: crate::workflows::WorkflowsAction::DismissToggleFailure,
            },
            UiEvent::Scheduling {
                action: crate::scheduling::SchedulingAction::ManageOnWeb,
            },
            UiEvent::Desk {
                action: crate::desk::DeskAction::OpenSettings,
            },
            UiEvent::Support {
                action: crate::support::SupportAction::Open,
            },
            UiEvent::Rooms {
                action: crate::rooms::RoomsAction::Leave,
            },
            UiEvent::Composer {
                action: crate::composer::ComposerAction::Send,
            },
            UiEvent::Settings {
                action: crate::settings::SettingsAction::Open,
            },
            UiEvent::Persona {
                action: crate::settings::persona::PersonaAction::Open,
            },
            UiEvent::VoiceStudio {
                action: crate::settings::voice_studio::VoiceStudioAction::Open,
            },
            UiEvent::CallHandling {
                action: crate::settings::call_handling::CallHandlingAction::Open,
            },
            UiEvent::Routing {
                action: crate::settings::routing::RoutingAction::Open,
            },
            UiEvent::Directory {
                action: crate::settings::directory::DirectoryAction::Open,
            },
            UiEvent::Tools {
                action: crate::settings::tools::ToolsAction::Open,
            },
            UiEvent::Knowledge {
                action: crate::settings::knowledge::KnowledgeAction::Open,
            },
            UiEvent::Messaging {
                action: crate::settings::messaging::MessagingAction::Open,
            },
            UiEvent::Members {
                action: crate::settings::members::MembersAction::Open,
            },
            UiEvent::Numbers {
                action: crate::settings::numbers::NumbersAction::Open,
            },
        ] {
            let buffer =
                <UiEvent as Lower<crate::UniFfiTag>>::lower_into_rust_buffer(action.clone());
            let back =
                <UiEvent as Lift<crate::UniFfiTag>>::try_lift_from_rust_buffer(buffer).unwrap();
            assert_eq!(back, action);
            // Each is at least one core event; which ones, each area's own
            // tests say (tests/projections/<area>.rs).
            assert!(!action.events().is_empty());
        }
    }
}
