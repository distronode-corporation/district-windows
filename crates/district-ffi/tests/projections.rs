//! The JSON of every projection, pinned.
//!
//! Each case drives the real core model through the events that reach a state,
//! the way the actor does, answering its effects with the core's own contract
//! fixtures, and compares what C# would read ([`shell_view`] and
//! [`screen_view`]) with a file under `tests/snapshots/`. A change to the core's
//! words, or to a projection, shows up here as a diff to review.
//!
//! To write the files again after a deliberate change:
//!
//! ```text
//! UPDATE_SNAPSHOTS=1 cargo test -p district-ffi --test projections
//! ```

mod contracts;

use std::collections::BTreeSet;
use std::path::PathBuf;

use district_api::{ApiError, ErrorDetail, ReauthReason, RetryReason, TokenError};
use district_auth::AccessClaims;
use district_core::{
    CoreConfig, Effect, Event, Model, RestoreError, Route, Ticket, WorkspaceSection,
};
use district_ffi::{ReportTarget, ScreenView, TabView, UiEvent, screen_view, shell_view};
use district_live::{LiveError, LiveUpdate, WorkspaceUpdate};
use district_model::WorkspaceListResponse;
use serde_json::{Value, json};

fn dir() -> PathBuf {
    PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("tests/snapshots")
}

/// A model, the effects it has asked for and not yet had answered, and
/// whether a report was started, as the actor holds them.
struct Session {
    model: Model,
    pending: Vec<Effect>,
    reporting: bool,
}

impl Session {
    /// Applies `event`, keeping the effects it asks for.
    fn send(mut self, event: Event) -> Self {
        let effects = self.model.update(event);
        self.pending.extend(effects);
        self
    }

    /// Applies what the user did, as the actor does.
    fn ui(mut self, action: UiEvent) -> Self {
        match &action {
            UiEvent::Report { .. } => self.reporting = true,
            UiEvent::DismissReport => self.reporting = false,
            _ => {}
        }
        for event in action.events() {
            self = self.send(event);
        }
        self
    }

    /// Answers the newest pending effect `pick` finds with `answer`.
    fn answer(mut self, pick: fn(&Effect) -> bool, answer: impl FnOnce(Ticket) -> Event) -> Self {
        let index = self
            .pending
            .iter()
            .rposition(pick)
            .unwrap_or_else(|| panic!("not pending: {:?}", self.pending));
        let effect = self.pending.remove(index);
        let ticket = effect
            .ticket()
            .expect("an effect with an answer has a ticket");
        self.send(answer(ticket))
    }
}

fn server_error() -> ApiError {
    ApiError::Server {
        status: 503,
        detail: ErrorDetail::default(),
    }
}

/// Every case: its file name and the session in that state.
fn cases() -> Vec<(&'static str, Session)> {
    let mut cases = vec![("restoring", plain(start().0))];

    let (mut model, ticket) = start();
    model.update(restored(
        ticket,
        Err(RestoreError::Token(TokenError::RetryLater(
            RetryReason::Offline,
        ))),
    ));
    cases.push(("restoring-offline", plain(model)));

    let (mut model, ticket) = start();
    model.update(restored(
        ticket,
        Err(RestoreError::Token(TokenError::RetryLater(
            RetryReason::SecretStoreLocked,
        ))),
    ));
    cases.push(("restoring-locked", plain(model)));

    cases.push(("signed-out-first-run", plain(signed_out())));

    let (mut model, ticket) = start();
    model.update(restored(
        ticket,
        Err(RestoreError::Token(TokenError::SignInRequired(
            ReauthReason::InterruptedRefresh,
        ))),
    ));
    cases.push(("signed-out-session-ended", plain(model)));

    let mut model = signed_out();
    model.update(Event::SignIn);
    cases.push(("signing-in-opening-browser", plain(model)));

    let mut model = signed_out();
    let ticket = sign_in_ticket(model.update(Event::SignIn));
    model.update(Event::SignInBrowser {
        ticket,
        opened: true,
    });
    cases.push(("signing-in-waiting", plain(model)));

    let mut model = signed_out();
    let ticket = sign_in_ticket(model.update(Event::SignIn));
    model.update(Event::SignInBrowser {
        ticket,
        opened: false,
    });
    cases.push(("signed-out-no-browser", plain(model)));

    cases.extend(overview_cases());
    cases.extend(inbox_cases());
    cases.extend(thread_cases());
    cases.extend(calls_cases());
    cases.extend(call_cases());
    cases.extend(contacts_cases());
    cases.extend(contact_cases());
    cases.extend(account_cases());
    cases.extend(report_cases());

    cases.push((
        "signed-in-voice-studio",
        signed_in().send(Event::Navigate(Route::Workspace(
            WorkspaceSection::VoiceStudio,
        ))),
    ));
    cases.extend(dialer_cases());
    cases.push(("signing-out", signed_in().send(Event::SignOut)));
    cases
}

fn overview_loaded(session: Session) -> Session {
    session
        .answer(
            |e| matches!(e, Effect::LoadOverview { .. }),
            |ticket| Event::OverviewLoaded {
                ticket,
                result: Ok(contracts::decode(
                    "district-overview.json",
                    with(
                        contracts::json("district-overview.json"),
                        "workspaceId",
                        "ws-1",
                    ),
                )),
            },
        )
        .answer(
            |e| matches!(e, Effect::LoadUnreadCount { .. }),
            |ticket| Event::UnreadCountLoaded {
                ticket,
                result: Ok(unread_count()),
            },
        )
}

fn no_workspaces(degraded: &[&str]) -> WorkspaceListResponse {
    contracts::decode(
        "no workspaces",
        json!({
            "success": true, "workspaces": [], "total": 0, "limit": 100, "offset": 0,
            "degradedRegions": degraded, "inactiveCount": 0, "defaultWorkspaceId": null
        }),
    )
}

fn overview_cases() -> Vec<(&'static str, Session)> {
    vec![
        ("signed-in-overview", signed_in()),
        (
            "overview-loaded-finish-setup",
            overview_loaded(signed_in()).answer(
                |e| matches!(e, Effect::LoadSetupStatus { .. }),
                |ticket| Event::SetupStatusLoaded {
                    ticket,
                    result: Ok(true),
                },
            ),
        ),
        (
            "overview-refreshing",
            overview_loaded(signed_in()).ui(UiEvent::Refresh),
        ),
        (
            "overview-live-stopped",
            overview_loaded(signed_in()).send(Event::Live(WorkspaceUpdate {
                workspace_id: "ws-1".to_owned(),
                update: LiveUpdate::Ended(Some(LiveError::Protocol)),
            })),
        ),
        (
            "overview-failed",
            signed_in().answer(
                |e| matches!(e, Effect::LoadOverview { .. }),
                |ticket| Event::OverviewLoaded {
                    ticket,
                    result: Err(server_error()),
                },
            ),
        ),
        (
            "overview-no-workspace",
            signed_in_to(Ok(no_workspaces(&[]))),
        ),
        (
            "overview-regions-unreachable",
            signed_in_to(Ok(contracts::read("district-workspace-list-degraded.json"))),
        ),
        (
            "overview-workspaces-failed",
            signed_in_to(Err(server_error())),
        ),
        (
            "overview-switcher-partial",
            signed_in_to(Ok(contracts::read("district-workspace-list-partial.json"))),
        ),
    ]
}

fn inbox(session: Session) -> Session {
    session.ui(UiEvent::OpenTab {
        tab: TabView::Inbox,
    })
}

fn conversations(session: Session, value: Value) -> Session {
    session.answer(
        |e| matches!(e, Effect::LoadConversations { .. }),
        |ticket| Event::ConversationsLoaded {
            ticket,
            result: Ok(contracts::decode("conversations", value)),
        },
    )
}

fn search_hits() -> Value {
    json!({
        "success": true,
        "limit": 1,
        "results": [{
            "messageId": "msg_1",
            "key": "contact:contact_contract_1",
            "threadKey": "contact:contact_contract_1",
            "counterpart": "+14165550142",
            "kind": "contact",
            "contactId": "contact_contract_1",
            "contactName": "Contract Test Caller",
            "contactEmail": null,
            "body": "Here is the photo\n of the roof.",
            "subject": null,
            "direction": "inbound",
            "messageType": "sms",
            "createdAt": "2026-08-15T14:10:00.000Z"
        }]
    })
}

fn searched(session: Session, query: &str, answer: Result<Value, ApiError>) -> Session {
    session
        .ui(UiEvent::Search {
            query: query.to_owned(),
        })
        .answer(
            |e| matches!(e, Effect::Wait { .. }),
            |ticket| Event::WaitOver { ticket },
        )
        .answer(
            |e| matches!(e, Effect::SearchMessages { .. }),
            |ticket| Event::SearchLoaded {
                ticket,
                result: answer.map(|value| contracts::decode("search", value)),
            },
        )
}

fn inbox_loaded() -> Session {
    conversations(
        inbox(signed_in()),
        contracts::json("district-conversations.json"),
    )
}

fn inbox_cases() -> Vec<(&'static str, Session)> {
    let mut partial = contracts::json("district-conversations.json");
    partial["scanned"] = json!(500);
    let none = with(search_hits(), "results", json!([]));
    vec![
        ("inbox-loading", inbox(signed_in())),
        ("inbox-loaded", inbox_loaded()),
        (
            "inbox-empty-partial",
            conversations(
                inbox(signed_in()),
                with(partial, "conversations", json!([])),
            ),
        ),
        (
            "inbox-failed",
            inbox(signed_in()).answer(
                |e| matches!(e, Effect::LoadConversations { .. }),
                |ticket| Event::ConversationsLoaded {
                    ticket,
                    result: Err(server_error()),
                },
            ),
        ),
        ("inbox-refreshing", inbox_loaded().ui(UiEvent::Refresh)),
        (
            "inbox-refresh-failed",
            inbox_loaded().ui(UiEvent::Refresh).answer(
                |e| matches!(e, Effect::LoadConversations { .. }),
                |ticket| Event::ConversationsLoaded {
                    ticket,
                    result: Err(server_error()),
                },
            ),
        ),
        (
            "inbox-search-hits",
            searched(inbox_loaded(), "roof", Ok(search_hits())),
        ),
        (
            "inbox-search-none",
            searched(inbox_loaded(), "gutter", Ok(none)),
        ),
        (
            "inbox-search-failed",
            searched(inbox_loaded(), "roof", Err(server_error())),
        ),
        (
            "inbox-search-running",
            inbox_loaded().ui(UiEvent::Search {
                query: "roof".to_owned(),
            }),
        ),
        (
            "inbox-search-cleared",
            searched(inbox_loaded(), "roof", Ok(search_hits())).ui(UiEvent::ClearSearch),
        ),
    ]
}

fn with_unread(session: Session) -> Session {
    session.answer(
        |e| matches!(e, Effect::LoadUnreadCount { .. }),
        |ticket| Event::UnreadCountLoaded {
            ticket,
            result: Ok(unread_count()),
        },
    )
}

fn open_thread(session: Session) -> Session {
    with_unread(conversations(
        inbox(session),
        contracts::json("district-conversations.json"),
    ))
    .ui(UiEvent::OpenThread {
        thread_key: "contact:contact_contract_1".to_owned(),
    })
}

fn timeline(session: Session, name: &str) -> Session {
    session.answer(
        |e| matches!(e, Effect::LoadTimeline { .. }),
        |ticket| Event::TimelineLoaded {
            ticket,
            result: Ok(contracts::read(name)),
        },
    )
}

fn timeline_failed(session: Session) -> Session {
    session.answer(
        |e| matches!(e, Effect::LoadTimeline { .. }),
        |ticket| Event::TimelineLoaded {
            ticket,
            result: Err(server_error()),
        },
    )
}

fn thread_cases() -> Vec<(&'static str, Session)> {
    let paged = || timeline(open_thread(signed_in()), "district-timeline-page.json");
    vec![
        ("thread-loading-marked-read", open_thread(signed_in())),
        (
            "thread-loaded",
            timeline(open_thread(signed_in()), "district-timeline.json"),
        ),
        (
            "thread-viewer",
            timeline(open_thread(viewer()), "district-timeline.json"),
        ),
        ("thread-failed", timeline_failed(open_thread(signed_in()))),
        ("thread-loading-older", paged().ui(UiEvent::LoadOlder)),
        (
            "thread-older-failed",
            timeline_failed(paged().ui(UiEvent::LoadOlder)),
        ),
        (
            "thread-refresh-failed",
            timeline_failed(paged().ui(UiEvent::Refresh)),
        ),
        (
            "thread-unreadable-key-refused",
            open_thread(signed_in()).ui(UiEvent::OpenThread {
                thread_key: "fax:123".to_owned(),
            }),
        ),
    ]
}

fn calls(session: Session) -> Session {
    session.ui(UiEvent::OpenTab {
        tab: TabView::Calls,
    })
}

fn calls_page(session: Session, value: Value) -> Session {
    session.answer(
        |e| matches!(e, Effect::LoadCalls { .. }),
        |ticket| Event::CallsLoaded {
            ticket,
            result: Ok(contracts::decode("calls", value)),
        },
    )
}

fn calls_failed(session: Session) -> Session {
    session.answer(
        |e| matches!(e, Effect::LoadCalls { .. }),
        |ticket| Event::CallsLoaded {
            ticket,
            result: Err(server_error()),
        },
    )
}

/// A full first page: the fixture's calls, repeated under new ids.
fn full_page() -> Value {
    let calls = contracts::json("district-calls.json");
    let rows = calls.as_array().expect("the call log is a list");
    let page: Vec<Value> = (0..25)
        .map(|n| with(rows[n % rows.len()].clone(), "id", format!("call-{n}")))
        .collect();
    Value::Array(page)
}

fn calls_cases() -> Vec<(&'static str, Session)> {
    let full = || calls_page(calls(signed_in()), full_page());
    vec![
        ("calls-loading", calls(signed_in())),
        (
            "calls-loaded",
            calls_page(calls(signed_in()), contracts::json("district-calls.json")),
        ),
        ("calls-empty", calls_page(calls(signed_in()), json!([]))),
        ("calls-failed", calls_failed(calls(signed_in()))),
        ("calls-loading-more", full().ui(UiEvent::LoadMoreCalls)),
        (
            "calls-more-failed",
            calls_failed(full().ui(UiEvent::LoadMoreCalls)),
        ),
    ]
}

fn open_call(session: Session, call_id: &str) -> Session {
    session.ui(UiEvent::OpenCall {
        call_id: call_id.to_owned(),
    })
}

fn call_loaded(session: Session, call: Value) -> Session {
    session.answer(
        |e| matches!(e, Effect::LoadCall { .. }),
        |ticket| Event::CallLoaded {
            ticket,
            result: Ok(contracts::decode("call", call)),
        },
    )
}

fn call_failed(session: Session) -> Session {
    session.answer(
        |e| matches!(e, Effect::LoadCall { .. }),
        |ticket| Event::CallLoaded {
            ticket,
            result: Err(server_error()),
        },
    )
}

fn transcript(session: Session, text: &str) -> Session {
    session.answer(
        |e| matches!(e, Effect::LoadTranscript { .. }),
        |ticket| Event::TranscriptLoaded {
            ticket,
            result: Ok(contracts::decode(
                "transcript",
                with(
                    contracts::json("district-call-transcript.json"),
                    "transcript",
                    text,
                ),
            )),
        },
    )
}

/// An outbound call from the log, to a number with nothing known about it,
/// transferred, with a follow-up sent.
fn outbound_call() -> Value {
    let calls = contracts::json("district-calls.json");
    let mut call = calls[2].clone();
    call["number"] = json!("+1 416 555 0171");
    call["followUp"] = json!({"email": "ada@example.com", "sms": null, "sentAt": null});
    call["transferStatus"] = json!("completed");
    call["transferReason"] = json!("caller_requested_human");
    json!({"success": true, "call": call})
}

fn on_call(session: Session) -> Session {
    call_loaded(
        open_call(session, "call_contract_answered"),
        contracts::json("district-call-detail.json"),
    )
}

fn call_read(session: Session) -> Session {
    let text = contracts::json("district-call-transcript.json")["transcript"]
        .as_str()
        .expect("a transcript")
        .to_owned();
    transcript(on_call(session), &text)
}

fn call_cases() -> Vec<(&'static str, Session)> {
    vec![
        (
            "call-loading",
            open_call(signed_in(), "call_contract_answered"),
        ),
        ("call-loaded", call_read(signed_in())),
        ("call-viewer", call_read(viewer())),
        (
            "call-outbound-no-transcript",
            transcript(
                call_loaded(
                    open_call(signed_in(), "call_contract_outbound"),
                    outbound_call(),
                ),
                "",
            ),
        ),
        (
            "call-failed",
            call_failed(open_call(signed_in(), "call_gone")).answer(
                |e| matches!(e, Effect::LoadTranscript { .. }),
                |ticket| Event::TranscriptLoaded {
                    ticket,
                    result: Err(server_error()),
                },
            ),
        ),
        (
            "call-refresh-failed",
            call_failed(call_read(signed_in()).ui(UiEvent::Refresh)),
        ),
    ]
}

fn contacts(session: Session) -> Session {
    session.ui(UiEvent::OpenTab {
        tab: TabView::Contacts,
    })
}

fn contacts_page(session: Session, value: Value) -> Session {
    session.answer(
        |e| matches!(e, Effect::LoadContacts { .. }),
        |ticket| Event::ContactsLoaded {
            ticket,
            result: Ok(contracts::decode("contacts", value)),
        },
    )
}

/// The fixture's two contacts as the first page of ten.
fn first_of_ten() -> Value {
    let mut page = contracts::json("district-contacts.json");
    page["total"] = json!(10);
    page["limit"] = json!(2);
    page
}

fn contacts_cases() -> Vec<(&'static str, Session)> {
    vec![
        ("contacts-loading", contacts(signed_in())),
        (
            "contacts-loaded",
            contacts_page(
                contacts(signed_in()),
                contracts::json("district-contacts.json"),
            ),
        ),
        (
            "contacts-empty",
            contacts_page(
                contacts(signed_in()),
                json!({"success": true, "contacts": [], "total": 0, "limit": 25, "offset": 0}),
            ),
        ),
        (
            "contacts-failed",
            contacts(signed_in()).answer(
                |e| matches!(e, Effect::LoadContacts { .. }),
                |ticket| Event::ContactsLoaded {
                    ticket,
                    result: Err(server_error()),
                },
            ),
        ),
        (
            "contacts-loading-more",
            contacts_page(contacts(signed_in()), first_of_ten()).ui(UiEvent::LoadMoreContacts),
        ),
    ]
}

fn open_contact(session: Session) -> Session {
    session.ui(UiEvent::OpenContact {
        contact_id: "contact_contract_1".to_owned(),
    })
}

fn contact_loaded(session: Session, value: Value) -> Session {
    session.answer(
        |e| matches!(e, Effect::LoadContact { .. }),
        |ticket| Event::ContactLoaded {
            ticket,
            result: Ok(contracts::decode("contact", value)),
        },
    )
}

fn contact_cases() -> Vec<(&'static str, Session)> {
    let detail = || contracts::json("district-contact-detail.json");
    let mut bare = detail();
    bare["contact"]["intelligence"] = Value::Null;
    bare["contact"]["dgiStatus"] = json!("crawling");
    bare["contact"]["company"] = Value::Null;
    bare["contact"]["phoneNumber"] = json!("4165550142");
    bare["phoneIntel"] = Value::Null;
    vec![
        ("contact-loading", open_contact(signed_in())),
        (
            "contact-loaded",
            contact_loaded(open_contact(signed_in()), detail()),
        ),
        (
            "contact-viewer",
            contact_loaded(open_contact(viewer()), detail()),
        ),
        (
            "contact-no-research",
            contact_loaded(open_contact(signed_in()), bare),
        ),
        (
            "contact-failed",
            open_contact(signed_in()).answer(
                |e| matches!(e, Effect::LoadContact { .. }),
                |ticket| Event::ContactLoaded {
                    ticket,
                    result: Err(server_error()),
                },
            ),
        ),
    ]
}

fn devices(session: Session) -> Session {
    session.ui(UiEvent::OpenDevices)
}

fn devices_loaded(session: Session, value: Value) -> Session {
    session.answer(
        |e| matches!(e, Effect::LoadDevices { .. }),
        |ticket| Event::DevicesLoaded {
            ticket,
            result: Ok(contracts::decode("devices", value)),
        },
    )
}

/// Signed in to a build that can carry calls.
fn with_calls() -> Session {
    signed_in_with(CoreConfig {
        calls_available: true,
        ..config()
    })
}

/// The dialler and a placed call, in a build with calls; the dialler in one
/// without. No case reaches a call in progress, whose start time is the clock's.
fn dialer_cases() -> Vec<(&'static str, Session)> {
    vec![
        ("dialer-without-calls", signed_in().ui(UiEvent::OpenDialer)),
        ("dialer", with_calls().ui(UiEvent::OpenDialer)),
        (
            "dialer-number",
            with_calls()
                .ui(UiEvent::OpenDialer)
                .ui(UiEvent::DialerEdit {
                    number: "+12125550142".to_owned(),
                }),
        ),
        (
            "call-dialing",
            with_calls().ui(UiEvent::CallNumber {
                number: "+12125550142".to_owned(),
            }),
        ),
        (
            "call-not-placed",
            with_calls()
                .ui(UiEvent::CallNumber {
                    number: "+12125550142".to_owned(),
                })
                .answer(
                    |e| matches!(e, Effect::Dial { .. }),
                    |ticket| Event::Dialled {
                        ticket,
                        result: Err(server_error()),
                    },
                ),
        ),
        (
            "account-ring-here",
            with_calls()
                .answer(
                    |e| matches!(e, Effect::ReadRingSetting { .. }),
                    |ticket| Event::RingSettingRead {
                        ticket,
                        ring_here: true,
                    },
                )
                .ui(UiEvent::OpenTab {
                    tab: TabView::Account,
                }),
        ),
    ]
}

fn account_cases() -> Vec<(&'static str, Session)> {
    let loaded = || {
        devices_loaded(
            devices(signed_in()),
            contracts::json("district-devices.json"),
        )
    };
    vec![
        (
            "account",
            signed_in().ui(UiEvent::OpenTab {
                tab: TabView::Account,
            }),
        ),
        ("devices-loading", devices(signed_in())),
        ("devices-loaded", loaded()),
        (
            "devices-empty",
            devices_loaded(
                devices(signed_in()),
                json!({"success": true, "devices": []}),
            ),
        ),
        (
            "devices-failed",
            devices(signed_in()).answer(
                |e| matches!(e, Effect::LoadDevices { .. }),
                |ticket| Event::DevicesLoaded {
                    ticket,
                    result: Err(server_error()),
                },
            ),
        ),
        (
            "devices-confirming",
            loaded().ui(UiEvent::AskSignOutDevice {
                device_id: "device-contract-android-1".to_owned(),
            }),
        ),
        ("devices-refreshing", loaded().ui(UiEvent::Refresh)),
    ]
}

fn reported(session: Session) -> Session {
    session.ui(UiEvent::Report {
        target: ReportTarget::Call {
            call_id: "call_contract_answered".to_owned(),
        },
        note: "The summary names the wrong day.".to_owned(),
    })
}

fn report_answer(session: Session, result: Result<Value, ApiError>) -> Session {
    session.answer(
        |e| matches!(e, Effect::CreateSupportRequest { .. }),
        |ticket| Event::SupportRequestCreated {
            ticket,
            result: result.map(|value| contracts::decode("support", value)),
        },
    )
}

fn report_cases() -> Vec<(&'static str, Session)> {
    let created = || Ok(contracts::json("district-support-request-create.json"));
    vec![
        ("report-sending", reported(on_call(signed_in()))),
        (
            "report-sent",
            report_answer(reported(on_call(signed_in())), created()),
        ),
        (
            "report-failed",
            report_answer(reported(on_call(signed_in())), Err(server_error())),
        ),
        (
            "report-dismissed",
            report_answer(reported(on_call(signed_in())), created()).ui(UiEvent::DismissReport),
        ),
        ("report-refused-for-a-viewer", reported(on_call(viewer()))),
    ]
}

/// The unread count fixture, for the open workspace.
fn unread_count() -> district_model::UnreadCountResponse {
    contracts::decode(
        "unread count",
        with(
            contracts::json("district-messages-unread-count.json"),
            "workspaceId",
            "ws-1",
        ),
    )
}

/// `value` with `key` set to `to`.
fn with(mut value: Value, key: &str, to: impl Into<Value>) -> Value {
    value[key] = to.into();
    value
}

fn config() -> CoreConfig {
    CoreConfig {
        web_base_url: "https://www.distronode.com".to_owned(),
        app_version: "0.1.0".to_owned(),
        calls_available: false,
    }
}

fn plain(model: Model) -> Session {
    Session {
        model,
        pending: Vec::new(),
        reporting: false,
    }
}

/// A model at start-up, and the ticket of its search for a stored session.
fn start() -> (Model, Ticket) {
    let (model, effects) = Model::new(config());
    let ticket = effects
        .into_iter()
        .find_map(|effect| match effect {
            Effect::RestoreSession { ticket } => Some(ticket),
            _ => None,
        })
        .expect("the model looks for a stored session at start-up");
    (model, ticket)
}

fn restored(ticket: Ticket, result: Result<AccessClaims, RestoreError>) -> Event {
    Event::SessionRestored { ticket, result }
}

fn signed_out() -> Model {
    let (mut model, ticket) = start();
    model.update(restored(
        ticket,
        Err(RestoreError::Token(TokenError::SignInRequired(
            ReauthReason::NoSession,
        ))),
    ));
    model
}

/// Signed in, with the workspace list read as `list` says.
fn signed_in_to(list: Result<WorkspaceListResponse, ApiError>) -> Session {
    signed_in_as(config(), list)
}

/// Signed in to a model of `config`, with the workspace list read as `list`
/// says.
fn signed_in_as(config: CoreConfig, list: Result<WorkspaceListResponse, ApiError>) -> Session {
    let (model, effects) = Model::new(config);
    let ticket = effects
        .into_iter()
        .find_map(|effect| match effect {
            Effect::RestoreSession { ticket } => Some(ticket),
            _ => None,
        })
        .expect("the model looks for a stored session at start-up");
    plain(model)
        .send(restored(
            ticket,
            Ok(AccessClaims {
                user_id: "user-1".to_owned(),
                device_id: "device-windows-1".to_owned(),
                expires_at_secs: 4_000_000_000,
            }),
        ))
        .answer(
            |e| matches!(e, Effect::LoadWorkspaces { .. }),
            |ticket| Event::WorkspacesLoaded {
                ticket,
                remembered: None,
                result: list,
            },
        )
}

/// Signed in, with one workspace open, as its agency: a workspace's screens
/// open only once the list has been read.
fn signed_in() -> Session {
    signed_in_with(config())
}

/// [`signed_in`], in a model of `config`.
fn signed_in_with(config: CoreConfig) -> Session {
    signed_in_as(
        config,
        Ok(contracts::decode(
            "one workspace",
            json!({
                "success": true,
                "workspaces": [{
                    "id": "ws-1",
                    "name": "Example Dental",
                    "region": "us",
                    "role": "agency",
                    "subscriptionTier": "VoicePro"
                }],
                "total": 1,
                "limit": 100,
                "offset": 0,
                "degradedRegions": [],
                "inactiveCount": 0,
                "defaultWorkspaceId": "ws-1"
            }),
        )),
    )
}

/// Signed in to the contract list's default workspace, where the member is a
/// viewer: read-only, and refused by support.
fn viewer() -> Session {
    signed_in_to(Ok(contracts::read("district-workspace-list.json")))
}

fn sign_in_ticket(effects: Vec<Effect>) -> Ticket {
    effects
        .into_iter()
        .find_map(|effect| match effect {
            Effect::BeginSignIn { ticket } => Some(ticket),
            _ => None,
        })
        .expect("signing in opens the browser")
}

#[test]
fn every_projection_matches_its_snapshot() {
    let update = std::env::var_os("UPDATE_SNAPSHOTS").is_some();
    let mut failures = Vec::new();
    for (name, session) in cases() {
        let actual = serde_json::to_string_pretty(&json!({
            "shell": shell_view(&session.model, session.reporting),
            "screen": screen_view(&session.model),
        }))
        .unwrap()
            + "\n";
        let path = dir().join(format!("{name}.json"));
        if update {
            std::fs::create_dir_all(dir()).unwrap();
            std::fs::write(&path, &actual).unwrap();
            continue;
        }
        match std::fs::read_to_string(&path) {
            Ok(expected) if expected == actual => {}
            Ok(expected) => failures.push(format!(
                "{name}: the projection changed\n--- {}\n{expected}\n+++ now\n{actual}",
                path.display()
            )),
            Err(_) => failures.push(format!("{name}: {} is missing", path.display())),
        }
    }
    assert!(
        failures.is_empty(),
        "{}\n\nIf the change is deliberate: UPDATE_SNAPSHOTS=1 cargo test -p district-ffi --test projections",
        failures.join("\n\n")
    );
}

/// A snapshot no case writes any more is a stale file, not a pinned state.
#[test]
fn every_snapshot_file_belongs_to_a_case() {
    let names: BTreeSet<String> = cases()
        .into_iter()
        .map(|(name, _)| format!("{name}.json"))
        .collect();
    assert_eq!(names.len(), cases().len(), "two cases share a file name");
    let files: BTreeSet<String> = std::fs::read_dir(dir())
        .unwrap()
        .map(|entry| entry.unwrap().file_name().into_string().unwrap())
        .collect();
    assert_eq!(files, names);
}

/// Opening a thread marks it read, and that is the core's own doing: the
/// unread count drops by the thread's two at once, with no event from C#.
#[test]
fn opening_a_thread_marks_it_read_in_the_core() {
    let before = with_unread(inbox_loaded());
    assert_eq!(shell_view(&before.model, false).unread, 3);
    let after = before.ui(UiEvent::OpenThread {
        thread_key: "contact:contact_contract_1".to_owned(),
    });
    assert_eq!(shell_view(&after.model, false).unread, 1);
    assert!(
        after
            .pending
            .iter()
            .any(|effect| matches!(effect, Effect::MarkRead { .. }))
    );
    assert!(matches!(
        screen_view(&after.model),
        ScreenView::Thread { .. }
    ));
}

/// The three events of a report are taken on the call's own screen, and
/// raising the request leaves the member there.
#[test]
fn a_report_stays_on_the_screen_it_was_made_from() {
    let sent = report_answer(
        reported(on_call(signed_in())),
        Ok(contracts::json("district-support-request-create.json")),
    );
    assert!(matches!(
        screen_view(&sent.model),
        ScreenView::CallDetail { .. }
    ));
    assert!(matches!(
        shell_view(&sent.model, true).report,
        Some(district_ffi::ReportStatus::Sent { .. })
    ));
}

/// The idempotency key of the report the core is sending.
fn report_key(session: &Session) -> String {
    session
        .pending
        .iter()
        .rev()
        .find_map(|effect| match effect {
            Effect::CreateSupportRequest {
                idempotency_key, ..
            } => Some(idempotency_key.clone()),
            _ => None,
        })
        .expect("a report on its way")
}

/// A report that failed leaves its draft, and its key, open. A report on
/// another target drops that draft first, so it goes under a new key and the
/// service cannot take it for a repeat of the first.
#[test]
fn a_report_after_a_failed_one_gets_a_new_key() {
    let first = reported(on_call(signed_in()));
    let first_key = report_key(&first);
    let failed = report_answer(first, Err(server_error()));
    assert!(matches!(
        shell_view(&failed.model, true).report,
        Some(district_ffi::ReportStatus::Failed { .. })
    ));
    let second = failed.ui(UiEvent::Report {
        target: ReportTarget::Contact {
            contact_id: "contact_contract_1".to_owned(),
        },
        note: String::new(),
    });
    let second_key = report_key(&second);
    assert_ne!(first_key, second_key);
    assert!(matches!(
        shell_view(&second.model, true).report,
        Some(district_ffi::ReportStatus::Sending)
    ));
}

/// While a report is on its way, a second press is refused whole: nothing
/// else is sent, and the first draft is not dropped.
#[test]
fn a_second_report_while_sending_is_ignored() {
    let sending = reported(on_call(signed_in()));
    let sent_before = sending.pending.len();
    let again = reported(sending);
    assert_eq!(again.pending.len(), sent_before);
    assert!(matches!(
        shell_view(&again.model, true).report,
        Some(district_ffi::ReportStatus::Sending)
    ));
}
