//! Workflows, their runs and the outbound campaign's switch.
//!
//! The scaffold: the view, the actions and the screen's projection are here,
//! and the packet that builds the area fills them in. Until it sets [`BUILT`],
//! the route shows [`ScreenView::Unavailable`] and the navigation leaves
//! Workflows out.

use district_core::{Event, Model, Route, SignedIn, WorkflowsEvent};
use serde::Serialize;

use crate::screen::ScreenView;

/// Whether this version has the area's screens. The packet that builds them
/// sets it; until then [`crate::nav::built`] says no for its routes.
pub(crate) const BUILT: bool = false;

/// The workflows screen.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct WorkflowsView {
    /// The heading. A placeholder, until the screen is built.
    pub title: String,
}

/// Something the member did on the workflows screen.
#[derive(Clone, Debug, PartialEq, Eq, uniffi::Enum)]
pub enum WorkflowsAction {
    /// Open the workflows.
    Open,
    /// Put away why turning a workflow on or off failed.
    DismissToggleFailure,
}

/// The core events `action` is, in the order the core is to hear them.
pub(crate) fn events(action: WorkflowsAction) -> Vec<Event> {
    vec![match action {
        WorkflowsAction::Open => Event::Navigate(Route::Workflows),
        WorkflowsAction::DismissToggleFailure => {
            Event::Workflows(WorkflowsEvent::DismissToggleFailure)
        }
    }]
}

/// The page of the workflows, for a signed-in model:
/// [`ScreenView::unavailable`] until the area is built.
pub(crate) fn screen(_model: &Model, _signed_in: &SignedIn) -> ScreenView {
    ScreenView::unavailable()
}
