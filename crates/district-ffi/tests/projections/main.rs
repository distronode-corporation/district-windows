//! The JSON of every projection, pinned, and each area's own checks.
//!
//! Each case drives the real core model through the events that reach a state,
//! the way the actor does, answering its effects with the core's own contract
//! fixtures, and compares what C# would read ([`shell_view`], its navigation
//! pane reduced to the selected entry, and [`screen_view`]) with a file under
//! `tests/snapshots/`. A change to the core's
//! words, or to a projection, shows up here as a diff to review.
//!
//! This file is the harness; each area's cases and tests are in its own file
//! beside it (CONTRIBUTING.md, "Areas"), and [`cases`] gathers them.
//!
//! To write the files again after a deliberate change:
//!
//! ```text
//! UPDATE_SNAPSHOTS=1 cargo test -p district-ffi --test projections
//! ```

#[path = "../contracts/mod.rs"]
mod contracts;

// The areas of 1.0.
mod account;
mod calls;
mod calls_live;
mod contacts;
mod inbox;
mod overview;
mod report;
mod session;

// The areas of 2.0.
mod analytics;
mod billing;
mod blocked;
mod composer;
mod desk;
mod hq;
mod marketplace;
mod rooms;
mod scheduling;
mod settings;
mod support;
mod workflows;

use std::collections::BTreeSet;
use std::path::PathBuf;

use district_api::{ApiError, ErrorDetail, ReauthReason, TokenError};
use district_auth::AccessClaims;
use district_core::{CoreConfig, Effect, Event, Model, RestoreError, Route, SessionState, Ticket};
use district_ffi::{NavDestination, ScreenView, UiEvent, screen_view, shell_view, ui_events};
use district_model::WorkspaceListResponse;
use serde_json::{Value, json};

/// A snapshot case: its file name and the session in that state.
type Case = (&'static str, Session);

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
        for event in ui_events(&self.model, &mut self.reporting, action) {
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

/// Every case, from every area.
fn cases() -> Vec<Case> {
    [
        session::cases(),
        overview::cases(),
        inbox::cases(),
        calls::cases(),
        contacts::cases(),
        account::cases(),
        report::cases(),
        calls_live::cases(),
        hq::cases(),
        analytics::cases(),
        marketplace::cases(),
        billing::cases(),
        workflows::cases(),
        scheduling::cases(),
        desk::cases(),
        support::cases(),
        rooms::cases(),
        blocked::cases(),
        composer::cases(),
        settings::cases(),
    ]
    .into_iter()
    .flatten()
    .collect()
}

/// The frame as a snapshot holds it: everything but the navigation pane's
/// entries, of which it keeps the selected one (`nav_selected`). The entries
/// change for every signed-in case whenever an area is built, so they are
/// pinned once, in nav.rs's own tests, rather than in every area's files.
fn shell_json(session: &Session) -> Value {
    let shell = shell_view(&session.model, session.reporting);
    let selected = shell
        .nav
        .groups
        .iter()
        .flat_map(|group| group.entries.iter())
        .find(|entry| entry.selected)
        .map(|entry| entry.destination);
    let mut value = serde_json::to_value(&shell).unwrap();
    let fields = value.as_object_mut().unwrap();
    fields.remove("nav");
    fields.insert("nav_selected".to_owned(), json!(selected));
    value
}

/// The route showing in a signed-in `session`.
fn route(session: &Session) -> Route {
    match session.model.session() {
        SessionState::SignedIn(signed_in) => signed_in.route.clone(),
        other => panic!("not signed in: {other:?}"),
    }
}

/// The destinations the navigation pane offers in `session`.
fn offered(session: &Session) -> Vec<NavDestination> {
    shell_view(&session.model, session.reporting)
        .nav
        .groups
        .iter()
        .flat_map(|group| group.entries.iter().map(|entry| entry.destination))
        .collect()
}

/// An area whose packet has not built it: `session` shows `expected`, as
/// [`ScreenView::Unavailable`], the navigation pane offers 1.0's five
/// destinations, and no entry of 2.0's is highlighted for it. Which of 2.0's
/// the pane offers is nav.rs's to pin (only those built), so building one
/// area changes no other area's checks.
fn assert_unbuilt(session: &Session, expected: Route) {
    const FIRST_FIVE: [NavDestination; 5] = [
        NavDestination::Overview,
        NavDestination::Inbox,
        NavDestination::Calls,
        NavDestination::Contacts,
        NavDestination::Account,
    ];
    assert_eq!(route(session), expected);
    assert!(
        matches!(screen_view(&session.model), ScreenView::Unavailable { .. }),
        "{expected:?}"
    );
    let offered = offered(session);
    assert!(
        FIRST_FIVE.iter().all(|five| offered.contains(five)),
        "{expected:?}: {offered:?}"
    );
    let selected = shell_json(session)["nav_selected"].clone();
    assert!(
        selected.is_null() || FIRST_FIVE.iter().any(|five| json!(five) == selected),
        "{expected:?}: {selected}"
    );
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
        // As the app builds it: Windows buys in the app.
        in_app_purchases: true,
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
            "shell": shell_json(&session),
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
