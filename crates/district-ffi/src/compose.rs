//! Starting a conversation from the inbox: the core's new message form
//! (`ComposeScreen`), as District AI for iOS offers it. A recipient is one of
//! the most recent contacts or a number or address typed in; a text message
//! comes first among its channels, and an email needs a subject. The message
//! is sent once, and the core then opens its thread.

use district_core::{
    Capabilities, ComposeContacts, ComposeEvent, ComposeRecipient, ComposeScreen, Event,
    InboxEvent, contact_label, format_phone_number,
};
use district_model::CHANNEL_EMAIL;
use serde::Serialize;

use crate::contacts::{ContactRowView, contact_row};
use crate::views::{FailureView, failure};

/// The inbox's "New" control: whether to offer it, and its words. A viewer
/// cannot send, so it is not offered to one.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct ComposeEntryView {
    /// Whether to offer it.
    pub offered: bool,
    /// Its words.
    pub label: String,
}

/// The new message form, while it is open.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct ComposeView {
    /// The dialog's title.
    pub title: String,
    /// The recipient field's label.
    pub to_label: String,
    /// What is typed in the recipient field, the filter over the contacts.
    pub query: String,
    /// Whether the contacts are being read.
    pub contacts_loading: bool,
    /// The contacts matching what is typed, newest first.
    pub suggestions: Vec<ContactRowView>,
    /// The note under them: only the most recent are offered.
    pub recent_note: String,
    /// Why the contacts could not be read; the form still works with an
    /// address typed in.
    pub suggestions_failed: Option<String>,
    /// The button that sends to what is typed, or `None` while nothing is.
    pub typed_action: Option<String>,
    /// Who the message goes to, once chosen: a contact's name, or what was
    /// typed.
    pub recipient: Option<String>,
    /// The button beside the recipient that goes back to choosing.
    pub change_label: String,
    /// Why a chosen contact cannot be sent to, or `None`.
    pub no_address_note: Option<String>,
    /// The label above the choice of channel.
    pub send_on_label: String,
    /// Each way the recipient can be reached, a text message first.
    pub channels: Vec<ComposeChannelView>,
    /// Which of [`Self::channels`] is chosen.
    pub channel: u32,
    /// Whether the message goes as an email, which needs a subject.
    pub requires_subject: bool,
    /// The subject field's label.
    pub subject_label: String,
    /// The subject, as typed.
    pub subject: String,
    /// The message field's label.
    pub message_label: String,
    /// The message, as typed.
    pub body: String,
    /// The send button's words, which say when the message is on its way.
    pub send_label: String,
    /// The cancel button's words.
    pub cancel_label: String,
    /// Whether "Send" works now.
    pub can_send: bool,
    /// Whether the message is on its way: nothing in the form takes input.
    pub sending: bool,
    /// Why the last send failed. What was typed stays.
    pub failure: Option<FailureView>,
}

/// One way to reach the recipient.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct ComposeChannelView {
    /// The channel's name: "Text message" or "Email".
    pub name: String,
    /// The number, grouped to read, or the address.
    pub address: String,
}

/// Something the member did in the new message form.
#[derive(Clone, Debug, PartialEq, Eq, uniffi::Enum)]
pub enum ComposeAction {
    /// "New", in the inbox's header: open the form, empty.
    Open,
    /// The recipient field changed.
    Query {
        /// What it holds now.
        text: String,
    },
    /// Send to an offered contact.
    ChooseContact {
        /// The contact.
        contact_id: String,
    },
    /// Send to what is typed in the recipient field.
    UseTyped,
    /// "Change": go back to choosing a recipient.
    ClearRecipient,
    /// Send on the channel at this index of [`ComposeView::channels`].
    SelectChannel {
        /// The channel's index.
        index: u32,
    },
    /// The subject changed.
    EditSubject {
        /// What it holds now.
        text: String,
    },
    /// The message changed.
    EditBody {
        /// What it holds now.
        text: String,
    },
    /// "Send". Billed, and sent once.
    Send,
    /// Close the form. The core refuses while the message is on its way.
    Cancel,
}

impl ComposeAction {
    /// The core event this is.
    pub(crate) fn event(self) -> Event {
        Event::Inbox(InboxEvent::Compose(match self {
            Self::Open => ComposeEvent::Open,
            Self::Query { text } => ComposeEvent::Query(text),
            Self::ChooseContact { contact_id } => ComposeEvent::ChooseContact(contact_id),
            Self::UseTyped => ComposeEvent::UseTyped,
            Self::ClearRecipient => ComposeEvent::ClearRecipient,
            Self::SelectChannel { index } => {
                ComposeEvent::SelectChannel(usize::try_from(index).unwrap_or(usize::MAX))
            }
            Self::EditSubject { text } => ComposeEvent::EditSubject(text),
            Self::EditBody { text } => ComposeEvent::EditBody(text),
            Self::Send => ComposeEvent::Send,
            Self::Cancel => ComposeEvent::Cancel,
        }))
    }
}

/// The inbox's "New" control for a member with `capabilities`.
pub(crate) fn compose_entry(capabilities: &Capabilities) -> ComposeEntryView {
    ComposeEntryView {
        offered: capabilities.can_change,
        label: ComposeScreen::action().to_owned(),
    }
}

/// The form, as the core holds it.
pub(crate) fn compose_view(compose: &ComposeScreen) -> ComposeView {
    let recipient = compose.recipient.as_ref().map(|recipient| match recipient {
        ComposeRecipient::Contact(contact) => contact_label(contact),
        ComposeRecipient::Typed(typed) => typed.clone(),
    });
    let channels = compose
        .channel_choices()
        .iter()
        .map(|target| ComposeChannelView {
            name: ComposeScreen::channel_label(target.channel).to_owned(),
            address: if target.channel == CHANNEL_EMAIL {
                target.to.clone()
            } else {
                format_phone_number(&target.to)
            },
        })
        .collect();
    ComposeView {
        title: ComposeScreen::title().to_owned(),
        to_label: ComposeScreen::to_label().to_owned(),
        query: compose.query.clone(),
        contacts_loading: matches!(compose.contacts, ComposeContacts::Loading),
        suggestions: compose.matches().into_iter().map(contact_row).collect(),
        recent_note: ComposeScreen::recent_contacts_note().to_owned(),
        suggestions_failed: compose.suggestions_failed(),
        typed_action: compose.typed_action(),
        recipient,
        change_label: ComposeScreen::change_label().to_owned(),
        no_address_note: compose.no_address_note().map(str::to_owned),
        send_on_label: ComposeScreen::send_on_label().to_owned(),
        channels,
        channel: u32::try_from(compose.channel).unwrap_or(u32::MAX),
        requires_subject: compose.requires_subject(),
        subject_label: ComposeScreen::subject_label().to_owned(),
        subject: compose.subject.clone(),
        message_label: ComposeScreen::message_label().to_owned(),
        body: compose.body.clone(),
        send_label: compose.send_label().to_owned(),
        cancel_label: ComposeScreen::cancel_label().to_owned(),
        can_send: compose.can_send(),
        sending: compose.sending,
        failure: failure(compose.failure.as_ref()),
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn each_action_is_its_compose_event() {
        let text = || "x".to_owned();
        let pairs = [
            (ComposeAction::Open, ComposeEvent::Open),
            (
                ComposeAction::Query { text: text() },
                ComposeEvent::Query(text()),
            ),
            (
                ComposeAction::ChooseContact { contact_id: text() },
                ComposeEvent::ChooseContact(text()),
            ),
            (ComposeAction::UseTyped, ComposeEvent::UseTyped),
            (ComposeAction::ClearRecipient, ComposeEvent::ClearRecipient),
            (
                ComposeAction::SelectChannel { index: 1 },
                ComposeEvent::SelectChannel(1),
            ),
            (
                ComposeAction::EditSubject { text: text() },
                ComposeEvent::EditSubject(text()),
            ),
            (
                ComposeAction::EditBody { text: text() },
                ComposeEvent::EditBody(text()),
            ),
            (ComposeAction::Send, ComposeEvent::Send),
            (ComposeAction::Cancel, ComposeEvent::Cancel),
        ];
        for (action, event) in pairs {
            assert_eq!(action.event(), Event::Inbox(InboxEvent::Compose(event)));
        }
    }
}
