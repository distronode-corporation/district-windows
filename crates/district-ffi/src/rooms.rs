//! Meeting rooms: the meetings held, their records, and starting a room.
//!
//! The scaffold: the view, the actions and the screen's projection are here,
//! and the packet that builds the area fills them in. Until it sets [`BUILT`],
//! the route shows [`ScreenView::Unavailable`] and the navigation leaves
//! Meeting rooms out.

use district_core::{Event, Model, RoomsEvent, Route, SignedIn};
use serde::Serialize;

use crate::screen::ScreenView;

/// Whether this version has the area's screens. The packet that builds them
/// sets it; until then [`crate::nav::built`] says no for its routes.
pub(crate) const BUILT: bool = false;

/// The meeting rooms screen.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct RoomsView {
    /// The heading. A placeholder, until the screen is built.
    pub title: String,
}

/// Something the member did in the rooms lobby.
#[derive(Clone, Debug, PartialEq, Eq, uniffi::Enum)]
pub enum RoomsAction {
    /// Open the meeting rooms.
    Open,
    /// Leave the room the member is in.
    Leave,
}

/// The core events `action` is, in the order the core is to hear them.
pub(crate) fn events(action: RoomsAction) -> Vec<Event> {
    vec![match action {
        RoomsAction::Open => Event::Navigate(Route::Rooms),
        RoomsAction::Leave => Event::Rooms(RoomsEvent::LeaveRoom),
    }]
}

/// The page of the meeting rooms, for a signed-in model:
/// [`ScreenView::unavailable`] until the area is built.
pub(crate) fn screen(_model: &Model, _signed_in: &SignedIn) -> ScreenView {
    ScreenView::unavailable()
}
