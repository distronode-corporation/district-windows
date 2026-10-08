//! The Routing section (src/settings/routing.rs).

use district_core::{Event, Route, WorkspaceSection};
use district_ffi::UiEvent;
use district_ffi::settings::routing::RoutingAction;

use super::super::{Case, assert_unbuilt, signed_in};

/// This area's snapshot cases: none until it is built.
pub(crate) fn cases() -> Vec<Case> {
    Vec::new()
}

/// Until its packet builds it, the area's screens are unavailable and the
/// navigation pane does not offer it.
#[test]
fn unbuilt_it_is_unavailable_and_not_offered() {
    assert_unbuilt(
        &signed_in().ui(UiEvent::Routing {
            action: RoutingAction::Open,
        }),
        Route::Workspace(WorkspaceSection::Routing),
    );
}

#[test]
fn each_action_is_its_core_event() {
    assert_eq!(
        UiEvent::Routing {
            action: RoutingAction::Open,
        }
        .events(),
        [Event::Navigate(Route::Workspace(WorkspaceSection::Routing))]
    );
}
