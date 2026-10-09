//! The question before leaving a settings section with changes that are not
//! saved: "Discard your changes?", as District AI for Linux asks it
//! (`district-app/src/guard.rs`).
//!
//! The core says when leaving would lose edits
//! ([`SignedIn::settings_unsaved`]); this decides which of the member's moves
//! leave, so the actor holds one before handing it to the core. The answer
//! [`UiEvent::DiscardChanges`](crate::UiEvent::DiscardChanges) hands it over
//! as it was; [`UiEvent::KeepEditing`](crate::UiEvent::KeepEditing) drops it,
//! and the window is drawn again from the core, which never moved.
//!
//! The moves that leave are Linux's: another screen, the way back, a fresh
//! read of the section (which starts it again from what is stored), another
//! workspace, and a notification's target. Signing out is not one: it is on
//! the account screen, and reaching that screen from a section is a
//! navigation, which is asked about. Quitting is not one either: the window is
//! gone by then, so there is nobody to ask, and as on Linux the edits go.

use district_core::{Event, Model, SessionState, SignedIn, WorkspacesState};
use serde::Serialize;

/// The question's heading.
pub const DISCARD_TITLE: &str = "Discard your changes?";
/// What discarding does.
pub const DISCARD_BODY: &str =
    "What you changed in this section has not been saved, and leaving it drops those changes.";
/// The button that discards them.
pub const DISCARD_ACTION: &str = "Discard";
/// The button that stays.
pub const KEEP_EDITING: &str = "Keep editing";

/// The question, while one is asked: a move away from a settings section with
/// unsaved changes is held until it is answered.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct DiscardView {
    /// The heading, [`DISCARD_TITLE`].
    pub title: String,
    /// The text under it, [`DISCARD_BODY`].
    pub body: String,
    /// The button that sends `UiEvent::DiscardChanges`, [`DISCARD_ACTION`].
    pub discard_label: String,
    /// The button that sends `UiEvent::KeepEditing`, [`KEEP_EDITING`]: the
    /// default, and what closing the question means.
    pub keep_label: String,
}

impl DiscardView {
    /// The question in the Linux app's words.
    pub(crate) fn new() -> Self {
        Self {
            title: DISCARD_TITLE.to_owned(),
            body: DISCARD_BODY.to_owned(),
            discard_label: DISCARD_ACTION.to_owned(),
            keep_label: KEEP_EDITING.to_owned(),
        }
    }
}

/// Whether handling `event` would leave the settings section showing while it
/// holds changes that are not saved, so the member is asked first.
pub fn leaves_unsaved(model: &Model, event: &Event) -> bool {
    let SessionState::SignedIn(signed_in) = model.session() else {
        return false;
    };
    signed_in.settings_unsaved() && leaves(signed_in, event)
}

/// Whether `event` leaves the screen showing: another screen, the way back, a
/// fresh read of this one, another workspace, or a notification's target.
fn leaves(signed_in: &SignedIn, event: &Event) -> bool {
    match event {
        Event::Navigate(route) => *route != signed_in.route,
        Event::SelectWorkspace(id) => match &signed_in.workspaces {
            WorkspacesState::Ready(workspaces) => workspaces.active().id != *id,
            _ => false,
        },
        Event::Back | Event::Refresh | Event::OpenNotification(_) => true,
        _ => false,
    }
}

/// The move held while the question is asked, as the actor keeps it.
#[derive(Debug, Default)]
pub struct Held(Option<Vec<Event>>);

impl Held {
    /// What of `events` (one action's, in order) goes to `model` now: all of
    /// them, or none when one would leave a section with unsaved changes. Those
    /// are held, and the question asked; while it is, a second move away is
    /// dropped, as Linux asks one question at a time.
    pub fn pass(&mut self, model: &Model, events: Vec<Event>) -> Vec<Event> {
        if !events.iter().any(|event| leaves_unsaved(model, event)) {
            return events;
        }
        if self.0.is_none() {
            self.0 = Some(events);
        }
        Vec::new()
    }

    /// "Discard": the held move, to hand to the model as it was. Nothing when
    /// nothing is held.
    pub fn discard(&mut self) -> Vec<Event> {
        self.0.take().unwrap_or_default()
    }

    /// "Keep editing": the held move is dropped.
    pub fn keep(&mut self) {
        self.0 = None;
    }

    /// The question, while a move is held.
    pub fn question(&self) -> Option<DiscardView> {
        self.0.as_ref().map(|_| DiscardView::new())
    }
}

#[cfg(test)]
mod tests {
    use district_core::CoreConfig;

    use super::*;

    #[test]
    fn nothing_is_asked_without_a_session() {
        let (model, _) = Model::new(CoreConfig {
            web_base_url: "https://www.distronode.com".to_owned(),
            app_version: "0.1.0".to_owned(),
            calls_available: false,
        });
        assert!(!leaves_unsaved(&model, &Event::Back));
        let mut held = Held::default();
        assert_eq!(held.pass(&model, vec![Event::Back]), [Event::Back]);
        assert_eq!(held.question(), None);
        assert_eq!(held.discard(), []);
        held.keep();
        assert_eq!(held.question(), None);
    }
}
