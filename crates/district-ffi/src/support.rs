//! Support requests from the workspace to Distronode, and one request.
//!
//! The scaffold: the view, the actions and the screen's projection are here,
//! and the packet that builds the area fills them in. Until it sets [`BUILT`],
//! the route shows [`ScreenView::Unavailable`] and the navigation leaves
//! Support out.

use district_core::{Event, Model, Route, SignedIn};
use serde::Serialize;

use crate::screen::ScreenView;

/// Whether this version has the area's screens. The packet that builds them
/// sets it; until then [`crate::nav::built`] says no for its routes.
pub(crate) const BUILT: bool = false;

/// The support requests.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct SupportView {
    /// The heading. A placeholder, until the screen is built.
    pub title: String,
}

/// One support request.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct SupportRequestView {
    /// The heading. A placeholder, until the screen is built.
    pub title: String,
}

/// Something the member did on the support screens.
#[derive(Clone, Debug, PartialEq, Eq, uniffi::Enum)]
pub enum SupportAction {
    /// Open the support requests.
    Open,
    /// Open a request.
    OpenRequest {
        /// Its support desk key (such as `DA-42`), or the service's own id.
        key: String,
    },
}

/// The core events `action` is, in the order the core is to hear them.
pub(crate) fn events(action: SupportAction) -> Vec<Event> {
    vec![match action {
        SupportAction::Open => Event::Navigate(Route::Support),
        SupportAction::OpenRequest { key } => Event::Navigate(Route::SupportRequest { key }),
    }]
}

/// The page of the support requests, for a signed-in model:
/// [`ScreenView::unavailable`] until the area is built.
pub(crate) fn screen(_model: &Model, _signed_in: &SignedIn) -> ScreenView {
    ScreenView::unavailable()
}

/// The page of the request asked for, for a signed-in model:
/// [`ScreenView::unavailable`] until the area is built.
pub(crate) fn request_screen(_model: &Model, _signed_in: &SignedIn, _key: &str) -> ScreenView {
    ScreenView::unavailable()
}
