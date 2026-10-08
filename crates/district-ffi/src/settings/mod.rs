//! The workspace settings hub, and below it one module per section.
//!
//! The scaffold: the hub's view, actions and projection are here, and the
//! packet that builds the hub fills them in; each section's are in its own
//! file, filled by its own packet. Until the hub sets [`BUILT`], its route shows
//! [`ScreenView::Unavailable`] and the navigation leaves Workspace settings out.

use district_core::{Event, Model, Route, SignedIn, WorkspaceSection};
use serde::Serialize;

use crate::screen::ScreenView;

pub mod call_handling;
pub mod directory;
pub mod knowledge;
pub mod members;
pub mod messaging;
pub mod numbers;
pub mod persona;
pub mod routing;
pub mod tools;
pub mod voice_studio;

/// Whether this version has the hub. The packet that builds it sets it; until
/// then [`crate::nav::built`] says no for its route.
pub(crate) const BUILT: bool = false;

/// The workspace settings hub: the list of sections.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct SettingsHubView {
    /// The heading. A placeholder, until the screen is built.
    pub title: String,
}

/// Something the member did on the workspace settings hub.
#[derive(Clone, Debug, PartialEq, Eq, uniffi::Enum)]
pub enum SettingsAction {
    /// Open the hub.
    Open,
}

/// The core events `action` is, in the order the core is to hear them.
pub(crate) fn events(action: SettingsAction) -> Vec<Event> {
    vec![match action {
        SettingsAction::Open => Event::Navigate(Route::Workspace(WorkspaceSection::Hub)),
    }]
}

/// The page of the hub, for a signed-in model: [`ScreenView::unavailable`]
/// until the hub is built.
pub(crate) fn screen(_model: &Model, _signed_in: &SignedIn) -> ScreenView {
    ScreenView::unavailable()
}
