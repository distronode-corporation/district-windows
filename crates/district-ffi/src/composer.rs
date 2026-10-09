//! The open conversation's reply box: what is typed, what is attached, sending
//! it, and a reply written by AI. Not a screen of its own:
//! [`ThreadView::composer`] carries it.
//!
//! Everything here is the core's: the text and the attachments it holds (and
//! saves on the service as the member's draft), whether each button works
//! ([`ThreadScreen::controls`]), and why the last send, upload or written reply
//! failed, a refused attachment included, in the core's words. The words the
//! core does not have (the busy line, the AI button and its note) are District
//! AI for Linux's (`pages/thread.rs`, `ui/thread-view.ui`).
//!
//! [`ThreadView::composer`]: crate::ThreadView::composer

use district_core::{Capabilities, Composer, Event, ThreadEvent, ThreadScreen};
use district_model::CHANNEL_SMS;
use serde::Serialize;

use crate::files::{FilePickView, attachment_pick, picked_attachment};
use crate::views::{FailureView, ReportAvailability, failure};

/// A picked file, as [`ComposerAction::Attach`] carries it (crate::files).
pub use crate::files::PickedFileView;

/// Whether this version has the reply box.
pub(crate) const BUILT: bool = true;

/// The AI button, as District AI for Linux words it.
pub const DRAFT_REPLY_LABEL: &str = "Draft a reply with AI";
/// What the AI button does and that each press is billed, as District AI for
/// Linux's tooltip on the button says it, word for word: the core (2.0.0
/// included) has no words for this, and nothing in it is Linux-only, so
/// Windows needs no line of its own. It names a charge to the workspace and
/// offers nothing to buy, which the store-copy check holds.
pub const DRAFT_REPLY_NOTE: &str = "Writes a suggested reply into the box for you to read before sending. Each suggestion is billed.";
/// The heading over a reply the model wrote, while it is in the box.
pub const AI_DRAFT: &str = "AI draft";
/// The busy line while a message is on its way.
pub const SENDING: &str = "Sending";
/// The busy line while an image uploads.
pub const UPLOADING: &str = "Uploading the image";
/// The busy line while the model writes a reply.
pub const WRITING: &str = "Writing a reply";

/// The reply box under a conversation, for a member who may reply there.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct ComposerView {
    /// What the box holds, as the core holds it: what was typed, a saved
    /// draft restored, or a reply the model wrote.
    pub text: String,
    /// The images uploaded to go with the next message, in the order picked.
    pub attachments: Vec<AttachmentView>,
    /// Whether to offer Attach at all: a text message thread. An email thread
    /// takes no attachments (the service would drop them).
    pub show_attach: bool,
    /// Whether Attach works now.
    pub can_attach: bool,
    /// Whether Send works now. False while a message or an image is on its
    /// way, and while the box is blank.
    pub can_send: bool,
    /// Whether the AI button works now. False while a reply is being written.
    pub can_draft_reply: bool,
    /// Whether a message is on its way (the box is not to be edited).
    pub sending: bool,
    /// Whether an image is uploading.
    pub attaching: bool,
    /// Whether the model is writing a reply.
    pub generating: bool,
    /// What the box is waiting for, when anything: "Sending", "Uploading the
    /// image", "Writing a reply".
    pub busy: Option<String>,
    /// Why the last send, upload or written reply failed, or why a picked
    /// file was refused, in the core's words. What was typed stays.
    pub failure: Option<FailureView>,
    /// The AI button and what goes with a reply it wrote.
    pub ai_draft: AiDraftOfferView,
}

/// One image waiting to go with the next message.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct AttachmentView {
    /// The address the service answered the upload with, to remove it by.
    pub url: String,
    /// Its name on the chip: "Image 1", "Image 2", in the order picked.
    pub name: String,
}

/// The AI button, and what goes with a reply the model wrote.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct AiDraftOfferView {
    /// The button: "Draft a reply with AI".
    pub label: String,
    /// What it does and that each press is billed, beside the button.
    pub billing_note: String,
    /// The heading over the box while it holds what the model wrote.
    pub draft_label: String,
    /// How to offer Report on what the model wrote (a
    /// [`ReportTarget::AiDraft`](crate::ReportTarget::AiDraft)), once it is in
    /// the box: in the app, or on the web for a member support refuses.
    pub report: ReportAvailability,
}

/// Something the member did in the reply box.
#[derive(Clone, Debug, PartialEq, Eq, uniffi::Enum)]
pub enum ComposerAction {
    /// The box changed: what it holds now.
    Compose {
        /// The text.
        text: String,
    },
    /// Send what the box holds, with the attachments.
    Send,
    /// Attach a picked image. The core checks it (its type sniffed from its
    /// bytes, its size, how many are held) and uploads it, or says why not.
    Attach {
        /// The file, as C# read it.
        file: PickedFileView,
    },
    /// A picked file could not be read at all.
    AttachFailed,
    /// Take an image off the message.
    RemoveAttachment {
        /// Its [`AttachmentView::url`].
        url: String,
    },
    /// Have the model write a reply into the box. Billed, every press.
    DraftReply,
    /// Put away the failure line.
    DismissFailure,
}

/// The core events `action` is, in the order the core is to hear them.
pub(crate) fn events(action: ComposerAction) -> Vec<Event> {
    vec![Event::Thread(match action {
        ComposerAction::Compose { text } => ThreadEvent::Compose(text),
        ComposerAction::Send => ThreadEvent::Send,
        ComposerAction::Attach { file } => ThreadEvent::Attach(picked_attachment(file)),
        ComposerAction::AttachFailed => ThreadEvent::AttachFailed,
        ComposerAction::RemoveAttachment { url } => ThreadEvent::RemoveAttachment(url),
        ComposerAction::DraftReply => ThreadEvent::DraftReply,
        ComposerAction::DismissFailure => ThreadEvent::DismissFailure,
    })]
}

/// The file chooser for an attachment: the core's image types, one file at a
/// time. The core takes one upload at a time and drops an image picked while
/// another uploads, so a pick of several would lose all but the first.
#[uniffi::export]
pub fn composer_pick() -> FilePickView {
    FilePickView {
        max_files: 1,
        ..attachment_pick()
    }
}

/// What the box is waiting for, as District AI for Linux words it.
fn busy(composer: &Composer) -> Option<String> {
    let line = if composer.sending {
        SENDING
    } else if composer.attaching {
        UPLOADING
    } else if composer.generating {
        WRITING
    } else {
        return None;
    };
    Some(line.to_owned())
}

/// The reply box of the thread `screen` shows, to a member with
/// `capabilities`: `None` with no thread, and for a member who may not reply
/// there (the thread's [`read_only_note`] says why).
pub(crate) fn composer(
    screen: Option<&ThreadScreen>,
    capabilities: &Capabilities,
) -> Option<ComposerView> {
    let screen = screen.filter(|_| BUILT)?;
    let controls = screen.controls(capabilities);
    controls.can_reply.then(|| {
        let composer = &screen.composer;
        ComposerView {
            text: composer.text.clone(),
            attachments: composer
                .attachments
                .iter()
                .enumerate()
                .map(|(index, url)| AttachmentView {
                    url: url.clone(),
                    name: format!("Image {}", index + 1),
                })
                .collect(),
            show_attach: screen
                .reply_target
                .as_ref()
                .is_some_and(|target| target.channel == CHANNEL_SMS),
            can_attach: controls.can_attach,
            can_send: controls.can_send,
            can_draft_reply: controls.can_draft_reply,
            sending: composer.sending,
            attaching: composer.attaching,
            generating: composer.generating,
            busy: busy(composer),
            failure: failure(composer.failure.as_ref()),
            ai_draft: AiDraftOfferView {
                label: DRAFT_REPLY_LABEL.to_owned(),
                billing_note: DRAFT_REPLY_NOTE.to_owned(),
                draft_label: AI_DRAFT.to_owned(),
                report: ReportAvailability::for_content(true, capabilities),
            },
        }
    })
}

/// Why the thread `screen` shows has no reply box, in the core's words, or
/// nothing when it has one. A thread still opening says nothing yet.
pub(crate) fn read_only_note(screen: Option<&ThreadScreen>, capabilities: &Capabilities) -> String {
    screen
        .and_then(|screen| screen.read_only_note(capabilities))
        .unwrap_or_default()
        .to_owned()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn the_busy_line_names_what_is_on_its_way_first() {
        let mut composer = Composer::default();
        assert_eq!(busy(&composer), None);
        composer.generating = true;
        assert_eq!(busy(&composer).as_deref(), Some(WRITING));
        composer.attaching = true;
        assert_eq!(busy(&composer).as_deref(), Some(UPLOADING));
        composer.sending = true;
        assert_eq!(busy(&composer).as_deref(), Some(SENDING));
    }

    #[test]
    fn the_pick_is_one_image_of_the_cores_types() {
        let pick = composer_pick();
        assert_eq!(pick.max_files, 1);
        assert_eq!(pick.extensions, attachment_pick().extensions);
        assert_eq!(pick.read_cap, attachment_pick().read_cap);
    }

    #[test]
    fn no_thread_has_no_box_and_no_note() {
        let member = Capabilities::default();
        assert_eq!(composer(None, &member), None);
        assert_eq!(read_only_note(None, &member), "");
    }
}
