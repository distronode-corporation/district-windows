//! The Numbers section (src/settings/numbers.rs).

use district_core::{Event, Route, WorkspaceSection};
use district_ffi::UiEvent;
use district_ffi::settings::numbers::NumbersAction;

use super::super::{Case, assert_unbuilt, signed_in};

/// This area's snapshot cases: none until it is built.
pub(crate) fn cases() -> Vec<Case> {
    Vec::new()
}

/// The core shows the phone numbers screen for this section, which until its
/// packet builds it is unavailable, and the navigation pane does not offer it.
#[test]
fn unbuilt_it_is_unavailable_and_not_offered() {
    assert_unbuilt(
        &signed_in().ui(UiEvent::Numbers {
            action: NumbersAction::Open,
        }),
        Route::Marketplace,
    );
}

#[test]
fn each_action_is_its_core_event() {
    assert_eq!(
        UiEvent::Numbers {
            action: NumbersAction::Open,
        }
        .events(),
        [Event::Navigate(Route::Workspace(WorkspaceSection::Numbers))]
    );
}
