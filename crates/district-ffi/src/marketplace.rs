//! The workspace's phone numbers and the numbers for sale, read only.
//!
//! The scaffold: the view, the actions and the screen's projection are here,
//! and the packet that builds the area fills them in. Until it sets [`BUILT`],
//! the route shows [`ScreenView::Unavailable`] and the navigation leaves Phone
//! numbers out.

use district_core::{Event, MarketplaceEvent, Model, Route, SignedIn};
use serde::Serialize;

use crate::screen::ScreenView;

/// Whether this version has the area's screens. The packet that builds them
/// sets it; until then [`crate::nav::built`] says no for its routes.
pub(crate) const BUILT: bool = false;

/// The phone numbers screen.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct MarketplaceView {
    /// The heading. A placeholder, until the screen is built.
    pub title: String,
}

/// Something the member did on the phone numbers screen.
#[derive(Clone, Debug, PartialEq, Eq, uniffi::Enum)]
pub enum MarketplaceAction {
    /// Open the phone numbers.
    Open,
    /// Open the phone numbers on the web dashboard.
    OpenWeb,
}

/// The core events `action` is, in the order the core is to hear them.
pub(crate) fn events(action: MarketplaceAction) -> Vec<Event> {
    vec![match action {
        MarketplaceAction::Open => Event::Navigate(Route::Marketplace),
        MarketplaceAction::OpenWeb => Event::Marketplace(MarketplaceEvent::OpenWeb),
    }]
}

/// The page of the phone numbers, for a signed-in model:
/// [`ScreenView::unavailable`] until the area is built.
pub(crate) fn screen(_model: &Model, _signed_in: &SignedIn) -> ScreenView {
    ScreenView::unavailable()
}
