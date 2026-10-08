//! The open conversation's reply box: what is typed, what is attached, and
//! sending it. Not a screen of its own: [`ThreadView::composer`] carries it.
//!
//! The scaffold: the view, the actions and the projection are here, and the
//! packet that builds the reply box fills them in. Until it sets [`BUILT`],
//! [`ThreadView::composer`] is `None`, and the thread shows its read-only note.
//!
//! [`ThreadView::composer`]: crate::ThreadView::composer

use district_core::{Capabilities, Event, ThreadEvent, ThreadScreen};
use serde::Serialize;

/// Whether this version has the reply box. The packet that builds it sets it
/// (and drops the `expect`, which fails the build once anything reads it).
#[expect(dead_code, reason = "read by the packet that builds the reply box")]
pub(crate) const BUILT: bool = false;

/// The reply box under a conversation.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct ComposerView {
    /// What the box holds. A placeholder, until the reply box is built.
    pub text: String,
}

/// Something the member did in the reply box.
#[derive(Clone, Debug, PartialEq, Eq, uniffi::Enum)]
pub enum ComposerAction {
    /// The box changed: what it holds now.
    Edit {
        /// The text.
        text: String,
    },
    /// Send what the box holds.
    Send,
}

/// The core events `action` is, in the order the core is to hear them.
pub(crate) fn events(action: ComposerAction) -> Vec<Event> {
    vec![match action {
        ComposerAction::Edit { text } => Event::Thread(ThreadEvent::Compose(text)),
        ComposerAction::Send => Event::Thread(ThreadEvent::Send),
    }]
}

/// The reply box of the thread `_screen` shows, to a member with
/// `_capabilities`: `None` until the reply box is built.
pub(crate) fn composer(
    _screen: Option<&ThreadScreen>,
    _capabilities: &Capabilities,
) -> Option<ComposerView> {
    None
}
