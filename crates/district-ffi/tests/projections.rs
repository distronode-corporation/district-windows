//! The JSON of every projection, pinned.
//!
//! Each case drives the real core model through the events that reach a state,
//! the way the actor does, and compares what C# would read ([`shell_view`] and
//! [`screen_view`]) with a file under `tests/snapshots/`. A change to the core's
//! words, or to a projection, shows up here as a diff to review.
//!
//! To write the files again after a deliberate change:
//!
//! ```text
//! UPDATE_SNAPSHOTS=1 cargo test -p district-ffi --test projections
//! ```

use std::collections::BTreeSet;
use std::path::PathBuf;

use district_api::{ReauthReason, RetryReason, TokenError};
use district_auth::AccessClaims;
use district_core::{
    CoreConfig, Effect, Event, Model, RestoreError, Route, Ticket, WorkspaceSection,
};
use district_ffi::{screen_view, shell_view};
use district_model::WorkspaceListResponse;
use serde_json::json;

fn dir() -> PathBuf {
    PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("tests/snapshots")
}

/// Every case: its file name and the model in that state.
fn cases() -> Vec<(&'static str, Model)> {
    let mut cases = vec![("restoring", start().0)];

    let (mut model, ticket) = start();
    model.update(restored(
        ticket,
        Err(RestoreError::Token(TokenError::RetryLater(
            RetryReason::Offline,
        ))),
    ));
    cases.push(("restoring-offline", model));

    let (mut model, ticket) = start();
    model.update(restored(
        ticket,
        Err(RestoreError::Token(TokenError::RetryLater(
            RetryReason::SecretStoreLocked,
        ))),
    ));
    cases.push(("restoring-locked", model));

    cases.push(("signed-out-first-run", signed_out()));

    let (mut model, ticket) = start();
    model.update(restored(
        ticket,
        Err(RestoreError::Token(TokenError::SignInRequired(
            ReauthReason::InterruptedRefresh,
        ))),
    ));
    cases.push(("signed-out-session-ended", model));

    let mut model = signed_out();
    model.update(Event::SignIn);
    cases.push(("signing-in-opening-browser", model));

    let mut model = signed_out();
    let ticket = sign_in_ticket(model.update(Event::SignIn));
    model.update(Event::SignInBrowser {
        ticket,
        opened: true,
    });
    cases.push(("signing-in-waiting", model));

    let mut model = signed_out();
    let ticket = sign_in_ticket(model.update(Event::SignIn));
    model.update(Event::SignInBrowser {
        ticket,
        opened: false,
    });
    cases.push(("signed-out-no-browser", model));

    cases.push(("signed-in-overview", signed_in()));

    let mut model = signed_in();
    model.update(Event::Navigate(Route::Calls));
    cases.push(("signed-in-calls", model));

    let mut model = signed_in();
    model.update(Event::Navigate(Route::CallDetail {
        call_id: "call-1".to_owned(),
    }));
    cases.push(("signed-in-call-detail", model));

    let mut model = signed_in();
    model.update(Event::Navigate(Route::Workspace(
        WorkspaceSection::VoiceStudio,
    )));
    cases.push(("signed-in-voice-studio", model));

    let mut model = signed_in();
    model.update(Event::SignOut);
    cases.push(("signing-out", model));

    cases
}

fn config() -> CoreConfig {
    CoreConfig {
        web_base_url: "https://www.distronode.com".to_owned(),
        app_version: "0.1.0".to_owned(),
        calls_available: false,
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

/// Signed in, with one workspace open: a workspace's screens open only once
/// the list has been read.
fn signed_in() -> Model {
    let (mut model, ticket) = start();
    let effects = model.update(restored(
        ticket,
        Ok(AccessClaims {
            user_id: "user-1".to_owned(),
            device_id: "device-windows-1".to_owned(),
            expires_at_secs: 4_000_000_000,
        }),
    ));
    let ticket = effects
        .into_iter()
        .find_map(|effect| match effect {
            Effect::LoadWorkspaces { ticket } => Some(ticket),
            _ => None,
        })
        .expect("signing in reads the workspace list");
    let list: WorkspaceListResponse = serde_json::from_value(json!({
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
    }))
    .unwrap();
    model.update(Event::WorkspacesLoaded {
        ticket,
        remembered: None,
        result: Ok(list),
    });
    model
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
    for (name, model) in cases() {
        let actual = serde_json::to_string_pretty(&json!({
            "shell": shell_view(model.session()),
            "screen": screen_view(model.session()),
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
