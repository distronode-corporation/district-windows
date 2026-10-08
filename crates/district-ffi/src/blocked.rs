//! The callers the workspace has blocked, below contacts.
//!
//! The scaffold: the view, the actions and the screen's projection are here,
//! and the packet that builds the area fills them in. Until it sets [`BUILT`],
//! the route shows [`ScreenView::Unavailable`] and nothing links to it.

use district_core::{Event, Model, Route, SignedIn};
use serde::Serialize;

use crate::screen::ScreenView;

/// Whether this version has the area's screens. The packet that builds them
/// sets it; until then [`crate::nav::built`] says no for its routes.
pub(crate) const BUILT: bool = false;

/// The blocked callers screen.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct BlockedView {
    /// The heading. A placeholder, until the screen is built.
    pub title: String,
}

/// Something the member did on the blocked callers screen.
#[derive(Clone, Debug, PartialEq, Eq, uniffi::Enum)]
pub enum BlockedAction {
    /// Open the blocked callers.
    Open,
}

/// The core events `action` is, in the order the core is to hear them.
pub(crate) fn events(action: BlockedAction) -> Vec<Event> {
    vec![match action {
        BlockedAction::Open => Event::Navigate(Route::BlockedContacts),
    }]
}

/// The page of the blocked callers, for a signed-in model:
/// [`ScreenView::unavailable`] until the area is built.
pub(crate) fn screen(_model: &Model, _signed_in: &SignedIn) -> ScreenView {
    ScreenView::unavailable()
}
