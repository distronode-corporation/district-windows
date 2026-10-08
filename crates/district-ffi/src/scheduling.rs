//! Booking pages.
//!
//! The scaffold: the view, the actions and the screen's projection are here,
//! and the packet that builds the area fills them in. Until it sets [`BUILT`],
//! the route shows [`ScreenView::Unavailable`] and the navigation leaves
//! Booking pages out.

use district_core::{Event, Model, Route, SchedulingEvent, SignedIn};
use serde::Serialize;

use crate::screen::ScreenView;

/// Whether this version has the area's screens. The packet that builds them
/// sets it; until then [`crate::nav::built`] says no for its routes.
pub(crate) const BUILT: bool = false;

/// The booking pages screen.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct SchedulingView {
    /// The heading. A placeholder, until the screen is built.
    pub title: String,
}

/// Something the member did on the booking pages screen.
#[derive(Clone, Debug, PartialEq, Eq, uniffi::Enum)]
pub enum SchedulingAction {
    /// Open the booking pages.
    Open,
    /// Manage the booking pages on the web dashboard.
    ManageOnWeb,
}

/// The core events `action` is, in the order the core is to hear them.
pub(crate) fn events(action: SchedulingAction) -> Vec<Event> {
    vec![match action {
        SchedulingAction::Open => Event::Navigate(Route::Scheduling),
        SchedulingAction::ManageOnWeb => Event::Scheduling(SchedulingEvent::ManageOnWeb),
    }]
}

/// The page of the booking pages, for a signed-in model:
/// [`ScreenView::unavailable`] until the area is built.
pub(crate) fn screen(_model: &Model, _signed_in: &SignedIn) -> ScreenView {
    ScreenView::unavailable()
}
