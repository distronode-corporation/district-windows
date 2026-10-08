//! The workspace's plan and the account's invoices, read only.
//!
//! The scaffold: the view, the actions and the screen's projection are here,
//! and the packet that builds the area fills them in. Until it sets [`BUILT`],
//! the route shows [`ScreenView::Unavailable`] and the navigation leaves
//! Billing out.

use district_core::{BillingEvent, Event, Model, Route, SignedIn};
use serde::Serialize;

use crate::screen::ScreenView;

/// Whether this version has the area's screens. The packet that builds them
/// sets it; until then [`crate::nav::built`] says no for its routes.
pub(crate) const BUILT: bool = false;

/// The billing screen.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct BillingView {
    /// The heading. A placeholder, until the screen is built.
    pub title: String,
}

/// Something the member did on the billing screen.
#[derive(Clone, Debug, PartialEq, Eq, uniffi::Enum)]
pub enum BillingAction {
    /// Open billing.
    Open,
    /// Manage billing on the web dashboard.
    ManageOnWeb,
}

/// The core events `action` is, in the order the core is to hear them.
pub(crate) fn events(action: BillingAction) -> Vec<Event> {
    vec![match action {
        BillingAction::Open => Event::Navigate(Route::Billing),
        BillingAction::ManageOnWeb => Event::Billing(BillingEvent::ManageOnWeb),
    }]
}

/// The page of billing, for a signed-in model: [`ScreenView::unavailable`]
/// until the area is built.
pub(crate) fn screen(_model: &Model, _signed_in: &SignedIn) -> ScreenView {
    ScreenView::unavailable()
}
