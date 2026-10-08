//! Support requests and one request (src/support.rs).

use district_core::{Event, Route};
use district_ffi::UiEvent;
use district_ffi::support::SupportAction;

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
        &signed_in().ui(UiEvent::Support {
            action: SupportAction::Open,
        }),
        Route::Support,
    );
    assert_unbuilt(
        &signed_in().ui(UiEvent::Support {
            action: SupportAction::OpenRequest {
                key: "DA-42".to_owned(),
            },
        }),
        Route::SupportRequest {
            key: "DA-42".to_owned(),
        },
    );
}

#[test]
fn each_action_is_its_core_event() {
    assert_eq!(
        UiEvent::Support {
            action: SupportAction::Open,
        }
        .events(),
        [Event::Navigate(Route::Support)]
    );
    assert_eq!(
        UiEvent::Support {
            action: SupportAction::OpenRequest {
                key: "DA-42".to_owned(),
            },
        }
        .events(),
        [Event::Navigate(Route::SupportRequest {
            key: "DA-42".to_owned(),
        })]
    );
}
