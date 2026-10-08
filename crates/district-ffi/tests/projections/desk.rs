//! The help desk, a ticket and the desk's settings (src/desk.rs).

use district_core::{Event, Route};
use district_ffi::UiEvent;
use district_ffi::desk::DeskAction;

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
        &signed_in().ui(UiEvent::Desk {
            action: DeskAction::Open,
        }),
        Route::Desk,
    );
    assert_unbuilt(
        &signed_in().ui(UiEvent::Desk {
            action: DeskAction::OpenTicket {
                ticket_id: "ticket-1".to_owned(),
            },
        }),
        Route::DeskTicket {
            ticket_id: "ticket-1".to_owned(),
        },
    );
    assert_unbuilt(
        &signed_in().ui(UiEvent::Desk {
            action: DeskAction::OpenSettings,
        }),
        Route::DeskSettings,
    );
}

#[test]
fn each_action_is_its_core_event() {
    assert_eq!(
        UiEvent::Desk {
            action: DeskAction::Open,
        }
        .events(),
        [Event::Navigate(Route::Desk)]
    );
    assert_eq!(
        UiEvent::Desk {
            action: DeskAction::OpenTicket {
                ticket_id: "ticket-1".to_owned(),
            },
        }
        .events(),
        [Event::Navigate(Route::DeskTicket {
            ticket_id: "ticket-1".to_owned(),
        })]
    );
    assert_eq!(
        UiEvent::Desk {
            action: DeskAction::OpenSettings,
        }
        .events(),
        [Event::Navigate(Route::DeskSettings)]
    );
}
