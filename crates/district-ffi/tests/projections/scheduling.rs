//! Booking pages (src/scheduling.rs).

use district_core::{Event, Route, SchedulingEvent};
use district_ffi::UiEvent;
use district_ffi::scheduling::SchedulingAction;

use super::{Case, assert_unbuilt, signed_in};

/// This area's snapshot cases: none until it is built.
pub(crate) fn cases() -> Vec<Case> {
    Vec::new()
}

/// Until its packet builds it, the area's screens are unavailable and the
/// navigation pane does not offer it.
#[test]
fn unbuilt_it_is_unavailable_and_not_offered() {
    assert_unbuilt(
        &signed_in().ui(UiEvent::Scheduling {
            action: SchedulingAction::Open,
        }),
        Route::Scheduling,
    );
}

#[test]
fn each_action_is_its_core_event() {
    assert_eq!(
        UiEvent::Scheduling {
            action: SchedulingAction::Open,
        }
        .events(),
        [Event::Navigate(Route::Scheduling)]
    );
    assert_eq!(
        UiEvent::Scheduling {
            action: SchedulingAction::ManageOnWeb,
        }
        .events(),
        [Event::Scheduling(SchedulingEvent::ManageOnWeb)]
    );
}
