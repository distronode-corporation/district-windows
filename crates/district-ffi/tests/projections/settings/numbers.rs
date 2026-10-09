//! The Numbers section (src/settings/numbers.rs).

use district_core::{Event, Route, WorkspaceSection};
use district_ffi::UiEvent;
use district_ffi::settings::numbers::NumbersAction;

use district_ffi::{ScreenView, screen_view};

use super::super::{Case, route, signed_in};

/// This area's snapshot cases: the phone numbers screen's (marketplace.rs).
pub(crate) fn cases() -> Vec<Case> {
    Vec::new()
}

/// The core shows the phone numbers screen for this section, built with it.
#[test]
fn it_is_the_phone_numbers_screen() {
    let session = signed_in().ui(UiEvent::Numbers {
        action: NumbersAction::Open,
    });
    assert_eq!(route(&session), Route::Marketplace);
    assert!(matches!(
        screen_view(&session.model),
        ScreenView::Marketplace { .. }
    ));
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
