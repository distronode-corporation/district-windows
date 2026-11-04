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

use district_core::{
    AssigneeList, Capabilities, Composer, Event, TemplateList, ThreadAssignment, ThreadEvent,
    ThreadScreen, WhatsAppComposer, reply_channel_label,
};
use district_model::CHANNEL_SMS;
use serde::Serialize;

use crate::files::{FilePickView, attachment_pick, picked_attachment};
use crate::l10n::{cs, l, s};
use crate::views::{FailureView, ReportAvailability, failure};

/// A picked file, as [`ComposerAction::Attach`] carries it (crate::files).
pub use crate::files::PickedFileView;

/// Whether this version has the reply box.
pub(crate) const BUILT: bool = true;

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
    /// The reply channel chooser's label: "Reply channel".
    pub channel_label: String,
    /// The ways to reply the service lists for this thread, in its order, the
    /// one in use marked. Offer the chooser only with `can_choose_channel`.
    pub channels: Vec<ReplyChannelView>,
    /// Whether to offer the chooser now: more than one channel, and nothing on
    /// its way.
    pub can_choose_channel: bool,
    /// Said beside the images when the channel chosen cannot carry them
    /// (Send waits until they are removed), or nothing.
    pub attachments_note: Option<String>,
    /// The WhatsApp window and its templates, while the reply goes by WhatsApp.
    pub whatsapp: Option<WhatsAppView>,
}

/// One way to reply.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct ReplyChannelView {
    /// The channel as the service names it (`sms`, `email`, `whatsapp`,
    /// `messenger`, `instagram`), to choose it by.
    pub channel: String,
    /// Its name: "Text message", "Email", "WhatsApp", "Messenger".
    pub label: String,
    /// Where the reply goes on it.
    pub to: String,
    /// Whether the reply goes on it now.
    pub selected: bool,
}

/// The WhatsApp side of the reply box.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct WhatsAppView {
    /// "Window closed: WhatsApp only allows a template now", once the service
    /// says so; nothing while the window is open or not known. Free text is off
    /// while it shows.
    pub window_closed: Option<String>,
    /// The template picker, while the window is closed.
    pub templates: Option<TemplatePickerView>,
}

/// The template picker a closed window offers.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct TemplatePickerView {
    /// Instead of the picker: "Loading templates", the sentence for a number
    /// with no approved template, or for templates that could not be read.
    pub message: Option<String>,
    /// The picker's label: "Template".
    pub label: String,
    /// The picker's placeholder: "Choose an approved template".
    pub placeholder: String,
    /// The approved templates, each by its id with its name and language.
    pub options: Vec<TemplateOptionView>,
    /// The id of the template chosen.
    pub chosen: Option<String>,
    /// One field per `{{n}}` of the chosen template, in order.
    pub variables: Vec<TemplateVariableView>,
    /// The preview's label: "Preview".
    pub preview_label: String,
    /// The chosen template with what is typed filled in, as the service will
    /// store it; an empty variable shows as its `{{n}}`.
    pub preview: Option<String>,
    /// The send button: "Send template".
    pub send_label: String,
    /// Whether the send button works now.
    pub can_send: bool,
    /// Whether a template is on its way.
    pub sending: bool,
}

/// One approved template.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct TemplateOptionView {
    /// Its id, to choose it by.
    pub id: String,
    /// Its line in the picker: "appointment_reminder (en_US)".
    pub label: String,
}

/// One variable of the chosen template.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct TemplateVariableView {
    /// Its place, from zero, to change it by.
    pub index: u32,
    /// "Variable {{1}}".
    pub label: String,
    /// What is typed.
    pub value: String,
    /// "Fill in this variable" while it is empty.
    pub required: Option<String>,
}

/// Who owns the conversation, and the picker that changes it.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct AssignmentView {
    /// The picker's label: "Assigned to".
    pub label: String,
    /// The owner in one line, for a member who sees it without the picker:
    /// "Assigned to ada@example.com", or "Unassigned".
    pub line: String,
    /// Whether to show the picker: the member may assign (a Viewer sees
    /// `line` only).
    pub show_picker: bool,
    /// The picker's choices: "Unassigned" first, then the Agency and Client
    /// members, and the owner when they are not among them.
    pub options: Vec<AssigneeOptionView>,
    /// The choice showing: the owner's, or "Unassigned".
    pub selected: u32,
    /// Whether the picker works now (members read, no change on its way).
    pub can_assign: bool,
    /// Whether a change is on its way.
    pub saving: bool,
    /// Why the last change failed, in the core's words.
    pub failure: Option<FailureView>,
}

/// One choice of the owner picker.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct AssigneeOptionView {
    /// The member's address, or nothing for "Unassigned".
    pub email: Option<String>,
    /// What the choice reads.
    pub label: String,
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

/// Something the member did in the reply box, or with the conversation's owner
/// picker in its header.
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
    /// Reply on another channel.
    ChooseChannel {
        /// Its [`ReplyChannelView::channel`].
        channel: String,
    },
    /// Give the conversation to a member, or take it back.
    Assign {
        /// The member's address, or nothing to unassign.
        assignee: Option<String>,
    },
    /// Put away the owner picker's failure line.
    DismissAssignFailure,
    /// Choose a WhatsApp template.
    ChooseTemplate {
        /// Its [`TemplateOptionView::id`].
        id: String,
    },
    /// A template variable changed.
    TemplateVariable {
        /// Its [`TemplateVariableView::index`].
        index: u32,
        /// What it holds now.
        value: String,
    },
    /// Send the chosen template, filled. Billed.
    SendTemplate,
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
        ComposerAction::ChooseChannel { channel } => ThreadEvent::ChooseChannel(channel),
        ComposerAction::Assign { assignee } => ThreadEvent::Assign(assignee),
        ComposerAction::DismissAssignFailure => ThreadEvent::DismissAssignFailure,
        ComposerAction::ChooseTemplate { id } => ThreadEvent::ChooseTemplate(id),
        ComposerAction::TemplateVariable { index, value } => {
            ThreadEvent::TemplateVariable(usize::try_from(index).unwrap_or(usize::MAX), value)
        }
        ComposerAction::SendTemplate => ThreadEvent::SendTemplate,
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
        s::composer_sending(l())
    } else if composer.attaching {
        s::composer_uploading(l())
    } else if composer.generating {
        s::composer_writing(l())
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
                    name: s::composer_attachment_name(
                        l(),
                        i64::try_from(index + 1).unwrap_or(i64::MAX),
                    ),
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
                label: s::composer_draft_reply_label(l()).to_owned(),
                billing_note: s::composer_draft_reply_note(l()).to_owned(),
                draft_label: s::composer_ai_draft(l()).to_owned(),
                report: ReportAvailability::for_content(true, capabilities),
            },
            channel_label: cs::thread_channel_label(l()).to_owned(),
            channels: screen
                .reply_choices
                .iter()
                .map(|choice| ReplyChannelView {
                    channel: choice.channel.to_owned(),
                    label: reply_channel_label(choice.channel)
                        .unwrap_or(choice.channel)
                        .to_owned(),
                    to: choice.to.clone(),
                    selected: screen.reply_target.as_ref() == Some(choice),
                })
                .collect(),
            can_choose_channel: controls.can_choose_channel,
            attachments_note: screen
                .attachments_blocked()
                .then(|| cs::thread_attachments_sms_only(l()).to_owned()),
            whatsapp: screen
                .whatsapp
                .as_ref()
                .map(|whatsapp| whatsapp_view(whatsapp, controls.can_send_template)),
        }
    })
}

/// The WhatsApp window and, once it is closed, its templates.
fn whatsapp_view(whatsapp: &WhatsAppComposer, can_send: bool) -> WhatsAppView {
    let closed = whatsapp.window == district_core::WhatsAppWindowState::Closed;
    WhatsAppView {
        window_closed: closed.then(|| cs::thread_whatsapp_window_closed(l()).to_owned()),
        templates: closed.then(|| template_picker(whatsapp, can_send)),
    }
}

fn template_picker(whatsapp: &WhatsAppComposer, can_send: bool) -> TemplatePickerView {
    let (message, options) = match &whatsapp.templates {
        TemplateList::NotLoaded | TemplateList::Loading => {
            (Some(cs::thread_templates_loading(l())), Vec::new())
        }
        TemplateList::Failed(_) => (Some(cs::thread_templates_failed(l())), Vec::new()),
        TemplateList::Ready(templates) if templates.is_empty() => {
            (Some(cs::thread_templates_none(l())), Vec::new())
        }
        TemplateList::Ready(templates) => (
            None,
            templates
                .iter()
                .map(|template| TemplateOptionView {
                    id: template.id.clone(),
                    label: template.label(),
                })
                .collect(),
        ),
    };
    let count = whatsapp
        .template()
        .map_or(0, |template| template.variable_count);
    TemplatePickerView {
        message: message.map(str::to_owned),
        label: cs::thread_template_label(l()).to_owned(),
        placeholder: cs::thread_template_placeholder(l()).to_owned(),
        options,
        chosen: whatsapp.template().map(|template| template.id.clone()),
        variables: (0..count)
            .map(|index| {
                let value = whatsapp.variables.get(index).cloned().unwrap_or_default();
                TemplateVariableView {
                    index: u32::try_from(index).unwrap_or(u32::MAX),
                    label: WhatsAppComposer::variable_label(index),
                    required: value
                        .trim()
                        .is_empty()
                        .then(|| cs::thread_template_variable_required(l()).to_owned()),
                    value,
                }
            })
            .collect(),
        preview_label: cs::thread_template_preview(l()).to_owned(),
        preview: whatsapp.preview(),
        send_label: cs::thread_template_send(l()).to_owned(),
        can_send,
        sending: whatsapp.sending,
    }
}

/// Who owns the conversation `screen` shows, for a member with
/// `capabilities`: `None` with no thread.
pub(crate) fn assignment(
    screen: Option<&ThreadScreen>,
    capabilities: &Capabilities,
) -> Option<AssignmentView> {
    let screen = screen?;
    let assignment: &ThreadAssignment = &screen.assignment;
    let mut options = vec![AssigneeOptionView {
        email: None,
        label: cs::thread_unassigned(l()).to_owned(),
    }];
    options.extend(
        assignment
            .options()
            .into_iter()
            .map(|email| AssigneeOptionView {
                label: email.clone(),
                email: Some(email),
            }),
    );
    let selected = options
        .iter()
        .position(|option| option.email == assignment.assignee)
        .unwrap_or(0);
    Some(AssignmentView {
        label: cs::thread_assign_label(l()).to_owned(),
        line: assignment.assignee.as_deref().map_or_else(
            || cs::thread_unassigned(l()).to_owned(),
            |email| cs::inbox_assigned_to(l(), email),
        ),
        show_picker: assignment.members != AssigneeList::NotLoaded,
        options,
        selected: u32::try_from(selected).unwrap_or(0),
        can_assign: screen.controls(capabilities).can_assign,
        saving: assignment.saving,
        failure: failure(assignment.failure.as_ref()),
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
        assert_eq!(busy(&composer).as_deref(), Some(s::composer_writing(l())));
        composer.attaching = true;
        assert_eq!(busy(&composer).as_deref(), Some(s::composer_uploading(l())));
        composer.sending = true;
        assert_eq!(busy(&composer).as_deref(), Some(s::composer_sending(l())));
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
