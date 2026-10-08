//! Phone numbers (src/marketplace.rs).

use district_core::{Event, MarketplaceEvent, Route};
use district_ffi::UiEvent;
use district_ffi::marketplace::MarketplaceAction;

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
        &signed_in().ui(UiEvent::Marketplace {
            action: MarketplaceAction::Open,
        }),
        Route::Marketplace,
    );
}

#[test]
fn each_action_is_its_core_event() {
    assert_eq!(
        UiEvent::Marketplace {
            action: MarketplaceAction::Open,
        }
        .events(),
        [Event::Navigate(Route::Marketplace)]
    );
    assert_eq!(
        UiEvent::Marketplace {
            action: MarketplaceAction::OpenWeb,
        }
        .events(),
        [Event::Marketplace(MarketplaceEvent::OpenWeb)]
    );
}
