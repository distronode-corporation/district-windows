//! Billing (src/billing.rs).

use district_core::{BillingEvent, Event, Route};
use district_ffi::UiEvent;
use district_ffi::billing::BillingAction;

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
        &signed_in().ui(UiEvent::Billing {
            action: BillingAction::Open,
        }),
        Route::Billing,
    );
}

#[test]
fn each_action_is_its_core_event() {
    assert_eq!(
        UiEvent::Billing {
            action: BillingAction::Open,
        }
        .events(),
        [Event::Navigate(Route::Billing)]
    );
    assert_eq!(
        UiEvent::Billing {
            action: BillingAction::ManageOnWeb,
        }
        .events(),
        [Event::Billing(BillingEvent::ManageOnWeb)]
    );
}
