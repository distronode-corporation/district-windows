//! Who answers an inbound call, and whether this member is rung (District
//! Studio's Call handling).
//!
//! The scaffold: the view, the actions and the screen's projection are here,
//! and the packet that builds the area fills them in. Until it sets [`BUILT`],
//! the route shows [`ScreenView::Unavailable`] and the settings hub does not
//! list it.

use district_core::{Event, Model, Route, SignedIn, WorkspaceSection};
use serde::Serialize;

use crate::screen::ScreenView;

/// Whether this version has the area's screens. The packet that builds them
/// sets it; until then [`crate::nav::built`] says no for its routes.
pub(crate) const BUILT: bool = false;

/// The call handling section.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct CallHandlingView {
    /// The heading. A placeholder, until the screen is built.
    pub title: String,
}

/// Something the member did on the call handling section.
#[derive(Clone, Debug, PartialEq, Eq, uniffi::Enum)]
pub enum CallHandlingAction {
    /// Open the call handling section.
    Open,
}

/// The core events `action` is, in the order the core is to hear them.
pub(crate) fn events(action: CallHandlingAction) -> Vec<Event> {
    vec![match action {
        CallHandlingAction::Open => {
            Event::Navigate(Route::Workspace(WorkspaceSection::CallHandling))
        }
    }]
}

/// The page of the call handling section, for a signed-in model:
/// [`ScreenView::unavailable`] until the area is built.
pub(crate) fn screen(_model: &Model, _signed_in: &SignedIn) -> ScreenView {
    ScreenView::unavailable()
}
