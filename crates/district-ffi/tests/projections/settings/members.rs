//! The Members section (src/settings/members.rs).

use district_core::{Event, Route, WorkspaceSection};
use district_ffi::UiEvent;
use district_ffi::settings::members::MembersAction;

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
        &signed_in().ui(UiEvent::Members {
            action: MembersAction::Open,
        }),
        Route::Workspace(WorkspaceSection::Members),
    );
}

#[test]
fn each_action_is_its_core_event() {
    assert_eq!(
        UiEvent::Members {
            action: MembersAction::Open,
        }
        .events(),
        [Event::Navigate(Route::Workspace(WorkspaceSection::Members))]
    );
}
