//! Meeting rooms (src/rooms.rs).

use district_core::{Event, RoomsEvent, Route};
use district_ffi::UiEvent;
use district_ffi::rooms::RoomsAction;

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
        &signed_in().ui(UiEvent::Rooms {
            action: RoomsAction::Open,
        }),
        Route::Rooms,
    );
}

#[test]
fn each_action_is_its_core_event() {
    assert_eq!(
        UiEvent::Rooms {
            action: RoomsAction::Open,
        }
        .events(),
        [Event::Navigate(Route::Rooms)]
    );
    assert_eq!(
        UiEvent::Rooms {
            action: RoomsAction::Leave,
        }
        .events(),
        [Event::Rooms(RoomsEvent::LeaveRoom)]
    );
}
