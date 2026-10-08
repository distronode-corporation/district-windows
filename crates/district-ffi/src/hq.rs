//! District HQ, the workspace assistant.
//!
//! The scaffold: the view, the actions and the screen's projection are here,
//! and the packet that builds the area fills them in. Until it sets [`BUILT`],
//! the route shows [`ScreenView::Unavailable`] and the navigation leaves
//! District HQ out.

use district_core::{Event, HqEvent, Model, Route, SignedIn};
use serde::Serialize;

use crate::screen::ScreenView;

/// Whether this version has the area's screens. The packet that builds them
/// sets it; until then [`crate::nav::built`] says no for its routes.
pub(crate) const BUILT: bool = false;

/// The District HQ screen.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct HqView {
    /// The heading. A placeholder, until the screen is built.
    pub title: String,
}

/// Something the member did on District HQ.
#[derive(Clone, Debug, PartialEq, Eq, uniffi::Enum)]
pub enum HqAction {
    /// Open District HQ.
    Open,
    /// Ask again, after a question that failed.
    Retry,
}

/// The core events `action` is, in the order the core is to hear them.
pub(crate) fn events(action: HqAction) -> Vec<Event> {
    vec![match action {
        HqAction::Open => Event::Navigate(Route::Hq),
        HqAction::Retry => Event::Hq(HqEvent::Retry),
    }]
}

/// The page of District HQ, for a signed-in model: [`ScreenView::unavailable`]
/// until the area is built.
pub(crate) fn screen(_model: &Model, _signed_in: &SignedIn) -> ScreenView {
    ScreenView::unavailable()
}
