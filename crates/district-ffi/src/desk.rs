//! The help desk: the tickets the workspace's own customers raised, one ticket,
//! and the desk's settings.
//!
//! The scaffold: the view, the actions and the screen's projection are here,
//! and the packet that builds the area fills them in. Until it sets [`BUILT`],
//! the route shows [`ScreenView::Unavailable`] and the navigation leaves Help
//! desk out.

use district_core::{Event, Model, Route, SignedIn};
use serde::Serialize;

use crate::screen::ScreenView;

/// Whether this version has the area's screens. The packet that builds them
/// sets it; until then [`crate::nav::built`] says no for its routes.
pub(crate) const BUILT: bool = false;

/// The help desk's list of tickets.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct DeskView {
    /// The heading. A placeholder, until the screen is built.
    pub title: String,
}

/// One help desk ticket.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct DeskTicketView {
    /// The heading. A placeholder, until the screen is built.
    pub title: String,
}

/// The help desk's settings and logo.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct DeskSettingsView {
    /// The heading. A placeholder, until the screen is built.
    pub title: String,
}

/// Something the member did on the help desk's screens.
#[derive(Clone, Debug, PartialEq, Eq, uniffi::Enum)]
pub enum DeskAction {
    /// Open the help desk.
    Open,
    /// Open a ticket.
    OpenTicket {
        /// Which one (its id, not its `T-` reference).
        ticket_id: String,
    },
    /// Open the help desk's settings.
    OpenSettings,
}

/// The core events `action` is, in the order the core is to hear them.
pub(crate) fn events(action: DeskAction) -> Vec<Event> {
    vec![match action {
        DeskAction::Open => Event::Navigate(Route::Desk),
        DeskAction::OpenTicket { ticket_id } => Event::Navigate(Route::DeskTicket { ticket_id }),
        DeskAction::OpenSettings => Event::Navigate(Route::DeskSettings),
    }]
}

/// The page of the help desk, for a signed-in model:
/// [`ScreenView::unavailable`] until the area is built.
pub(crate) fn screen(_model: &Model, _signed_in: &SignedIn) -> ScreenView {
    ScreenView::unavailable()
}

/// The page of the ticket asked for, for a signed-in model:
/// [`ScreenView::unavailable`] until the area is built.
pub(crate) fn ticket_screen(_model: &Model, _signed_in: &SignedIn, _ticket_id: &str) -> ScreenView {
    ScreenView::unavailable()
}

/// The page of the help desk's settings, for a signed-in model:
/// [`ScreenView::unavailable`] until the area is built.
pub(crate) fn settings_screen(_model: &Model, _signed_in: &SignedIn) -> ScreenView {
    ScreenView::unavailable()
}
