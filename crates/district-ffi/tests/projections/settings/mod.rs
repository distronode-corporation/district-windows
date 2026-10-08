//! The workspace settings hub (src/settings/mod.rs), and one file per section beside this one.

mod call_handling;
mod directory;
mod knowledge;
mod members;
mod messaging;
mod numbers;
mod persona;
mod routing;
mod tools;
mod voice_studio;

use district_core::{Event, Route, WorkspaceSection};
use district_ffi::UiEvent;
use district_ffi::settings::SettingsAction;

use super::{Case, assert_unbuilt, signed_in};

/// The hub's snapshot cases, and every section's.
pub(crate) fn cases() -> Vec<Case> {
    [
        persona::cases(),
        voice_studio::cases(),
        call_handling::cases(),
        routing::cases(),
        directory::cases(),
        tools::cases(),
        knowledge::cases(),
        messaging::cases(),
        members::cases(),
        numbers::cases(),
    ]
    .into_iter()
    .flatten()
    .collect()
}

/// Until its packet builds it, the area's screens are unavailable and the
/// navigation pane does not offer it.
#[test]
fn unbuilt_it_is_unavailable_and_not_offered() {
    assert_unbuilt(
        &signed_in().ui(UiEvent::Settings {
            action: SettingsAction::Open,
        }),
        Route::Workspace(WorkspaceSection::Hub),
    );
}

#[test]
fn each_action_is_its_core_event() {
    assert_eq!(
        UiEvent::Settings {
            action: SettingsAction::Open,
        }
        .events(),
        [Event::Navigate(Route::Workspace(WorkspaceSection::Hub))]
    );
}
