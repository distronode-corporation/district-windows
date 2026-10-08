//! The workspace's phone numbers, in its settings.
//!
//! The core shows the phone numbers screen (`Route::Marketplace`) for this
//! section rather than a second copy of it, so the section has no screen of
//! its own: only the way to it, and [`BUILT`], which the packet that builds
//! the phone numbers screen sets with the marketplace's.

use district_core::{Event, Route, WorkspaceSection};

/// Whether this version has the section, which is the phone numbers screen.
/// Until it is set, [`crate::nav::built`] says no for its route.
pub(crate) const BUILT: bool = false;

/// Something the member did towards the phone numbers section.
#[derive(Clone, Debug, PartialEq, Eq, uniffi::Enum)]
pub enum NumbersAction {
    /// Open the section (the core opens the phone numbers screen).
    Open,
}

/// The core events `action` is, in the order the core is to hear them.
pub(crate) fn events(action: NumbersAction) -> Vec<Event> {
    vec![match action {
        NumbersAction::Open => Event::Navigate(Route::Workspace(WorkspaceSection::Numbers)),
    }]
}
