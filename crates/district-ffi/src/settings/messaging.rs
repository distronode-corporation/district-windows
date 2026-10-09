//! The workspace's messaging accounts (carrier accounts), as District AI for
//! Linux shows them (`pages/messaging.rs`, `pages/messaging_form.rs`): the
//! accounts and the default sender, each channel's sender, the numbers held by
//! Distronode, the owner's mobile number, and the form that adds or edits an
//! account, with its check of the keys.
//!
//! # Secrets
//!
//! The core never reads a credential back, so the form never starts with one,
//! and what is typed stays in the core's `SecretText` while the form is open.
//! This projection carries no secret: a secret box shows only whether
//! something is typed in it (`filled`), so the app can empty its box when the
//! core drops what was typed (a change of carrier, a save that landed, the
//! form closing), and never writes a secret back into one. The owner's mobile
//! number is never shown either: no read returns it. The action that carries
//! a typed key prints it as redacted in `Debug`.
//!
//! A viewer reads the accounts and changes nothing; the core refuses their
//! changes too.

use std::fmt;

use district_core::{
    Capabilities, CredentialField, CredentialTest, Event, MESSAGING_CHANNELS, MessagingAccounts,
    MessagingDeleteConfirm, MessagingEvent, MessagingForm, MessagingFormEdit, MessagingSection,
    Model, Route, SecretText, SignedIn, WorkspaceSection, channel_label, credential_source_label,
    format_phone_number, provider_label,
};
use district_model::{
    MessagingAccount, MessagingChannel, MessagingCredentialSource, MessagingProvider,
    MessagingResponse,
};
use serde::Serialize;

use super::{ChoiceView, PickerView, SaveNoticeView, SectionStatus, save_notice};
use crate::screen::ScreenView;
use crate::views::{EmptyView, FailureView, humanize};

/// Whether this version has the section.
pub(crate) const BUILT: bool = true;

/// The page's heading, as the settings hub names it.
pub const MESSAGING_TITLE: &str = "Messaging accounts";
/// The heading when the accounts could not be read.
pub const FAILED_TITLE: &str = "Could not load the carrier accounts";
/// What a channel's sender reads when it has none of its own.
pub const DEFAULT_SENDER: &str = "The default account";
/// What a channel's sender reads when it names an account not listed.
pub const UNLISTED_SENDER: &str = "An account that is not listed";
/// The line under the key boxes when every key must be typed.
pub const KEYS_NEEDED: &str = "Every key the carrier needs, typed in full.";
/// The heading over the numbers Distronode holds for the workspace.
pub const MANAGED_HEADING: &str = "Numbers held by Distronode";
/// What those numbers are.
pub const MANAGED_BODY: &str = "Bought for this workspace on Distronode's own carrier account. \
    They are not an account of this workspace's, so they cannot be chosen as a sender.";
/// The heading over the channels' senders.
pub const CHANNELS_HEADING: &str = "Sender for each channel";
/// What a channel without a sender does.
pub const CHANNELS_BODY: &str = "A channel without a sender of its own sends from the default \
    account.";
/// The heading over the owner's mobile number.
pub const CREATOR_HEADING: &str = "The owner's mobile number";

const PROVIDERS: [MessagingProvider; 3] = [
    MessagingProvider::Twilio,
    MessagingProvider::Sinch,
    MessagingProvider::Telnyx,
];
const SOURCES: [MessagingCredentialSource; 2] = [
    MessagingCredentialSource::Byok,
    MessagingCredentialSource::Managed,
];

/// A carrier, as the form offers it.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Serialize, uniffi::Enum)]
pub enum MessagingCarrier {
    /// Twilio.
    Twilio,
    /// Sinch.
    Sinch,
    /// Telnyx.
    Telnyx,
}

impl From<MessagingProvider> for MessagingCarrier {
    fn from(provider: MessagingProvider) -> Self {
        match provider {
            MessagingProvider::Twilio => Self::Twilio,
            MessagingProvider::Sinch => Self::Sinch,
            MessagingProvider::Telnyx => Self::Telnyx,
        }
    }
}

impl From<MessagingCarrier> for MessagingProvider {
    fn from(carrier: MessagingCarrier) -> Self {
        match carrier {
            MessagingCarrier::Twilio => Self::Twilio,
            MessagingCarrier::Sinch => Self::Sinch,
            MessagingCarrier::Telnyx => Self::Telnyx,
        }
    }
}

/// Whose carrier account an account is.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Serialize, uniffi::Enum)]
pub enum MessagingSource {
    /// The workspace's own carrier account.
    Own,
    /// Managed by Distronode.
    Managed,
}

impl From<MessagingCredentialSource> for MessagingSource {
    fn from(source: MessagingCredentialSource) -> Self {
        match source {
            MessagingCredentialSource::Byok => Self::Own,
            MessagingCredentialSource::Managed => Self::Managed,
        }
    }
}

impl From<MessagingSource> for MessagingCredentialSource {
    fn from(source: MessagingSource) -> Self {
        match source {
            MessagingSource::Own => Self::Byok,
            MessagingSource::Managed => Self::Managed,
        }
    }
}

/// A channel a sender is set for.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Serialize, uniffi::Enum)]
pub enum SenderChannel {
    /// Texts.
    Sms,
    /// Calls.
    Voice,
    /// WhatsApp.
    Whatsapp,
}

impl From<MessagingChannel> for SenderChannel {
    fn from(channel: MessagingChannel) -> Self {
        match channel {
            MessagingChannel::Sms => Self::Sms,
            MessagingChannel::Voice => Self::Voice,
            MessagingChannel::Whatsapp => Self::Whatsapp,
        }
    }
}

impl From<SenderChannel> for MessagingChannel {
    fn from(channel: SenderChannel) -> Self {
        match channel {
            SenderChannel::Sms => Self::Sms,
            SenderChannel::Voice => Self::Voice,
            SenderChannel::Whatsapp => Self::Whatsapp,
        }
    }
}

/// A key box of the form.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Serialize, uniffi::Enum)]
pub enum KeyField {
    /// Twilio's account SID.
    AccountSid,
    /// Twilio's auth token.
    AuthToken,
    /// Sinch's project id (not a secret).
    ProjectId,
    /// Sinch's access key id.
    KeyId,
    /// Sinch's access key secret.
    KeySecret,
    /// Sinch's voice application key.
    ApplicationKey,
    /// Sinch's voice application secret.
    ApplicationSecret,
    /// Telnyx's API key.
    ApiKey,
}

impl From<CredentialField> for KeyField {
    fn from(field: CredentialField) -> Self {
        match field {
            CredentialField::AccountSid => Self::AccountSid,
            CredentialField::AuthToken => Self::AuthToken,
            CredentialField::ProjectId => Self::ProjectId,
            CredentialField::KeyId => Self::KeyId,
            CredentialField::KeySecret => Self::KeySecret,
            CredentialField::ApplicationKey => Self::ApplicationKey,
            CredentialField::ApplicationSecret => Self::ApplicationSecret,
            CredentialField::ApiKey => Self::ApiKey,
        }
    }
}

impl From<KeyField> for CredentialField {
    fn from(field: KeyField) -> Self {
        match field {
            KeyField::AccountSid => Self::AccountSid,
            KeyField::AuthToken => Self::AuthToken,
            KeyField::ProjectId => Self::ProjectId,
            KeyField::KeyId => Self::KeyId,
            KeyField::KeySecret => Self::KeySecret,
            KeyField::ApplicationKey => Self::ApplicationKey,
            KeyField::ApplicationSecret => Self::ApplicationSecret,
            KeyField::ApiKey => Self::ApiKey,
        }
    }
}

/// The messaging accounts section.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct MessagingView {
    /// The heading, [`MESSAGING_TITLE`].
    pub title: String,
    /// Being read, failed, or read.
    pub status: SectionStatus,
    /// For a viewer: that the accounts are shown and not changed.
    pub viewer_note: Option<String>,
    /// How the last change ended, until it is dismissed.
    pub notice: Option<SaveNoticeView>,
    /// Whether a change or a check of keys is on its way.
    pub busy: bool,
    /// Whether changes can be made now: read, and nothing on its way.
    pub editable: bool,
    /// Whether "Add an account" shows (a member who may change them).
    pub offers_add: bool,
    /// What to say when no carrier is connected.
    pub empty: Option<EmptyView>,
    /// The accounts.
    pub accounts: Vec<MessagingAccountView>,
    /// The numbers Distronode holds for the workspace, when it holds any.
    pub managed: Option<ManagedNumbersView>,
    /// Each channel's sender, while there are accounts.
    pub channels: Vec<ChannelSenderView>,
    /// Whether the senders can be chosen (else they are read only).
    pub channels_editable: bool,
    /// The owner's mobile number box, for a member who may change it.
    pub creator: Option<CreatorCellView>,
    /// The question before an account is removed.
    pub confirm: Option<MessagingConfirmView>,
    /// The form adding or editing an account, while it is open.
    pub form: Option<MessagingFormView>,
}

impl Default for MessagingView {
    fn default() -> Self {
        Self {
            title: MESSAGING_TITLE.to_owned(),
            status: SectionStatus::Loading,
            viewer_note: None,
            notice: None,
            busy: false,
            editable: false,
            offers_add: false,
            empty: None,
            accounts: Vec::new(),
            managed: None,
            channels: Vec::new(),
            channels_editable: false,
            creator: None,
            confirm: None,
            form: None,
        }
    }
}

/// One carrier account.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct MessagingAccountView {
    /// The account, to act on.
    pub id: String,
    /// Its name.
    pub label: String,
    /// Its carrier, whose account it is, and its numbers.
    pub line: String,
    /// Whether it is the default sender.
    pub is_default: bool,
    /// Whether "Make default" shows.
    pub offers_make_default: bool,
    /// Whether "Edit" shows: a carrier and a source this build knows.
    pub offers_edit: bool,
    /// Whether "Remove" shows.
    pub offers_remove: bool,
}

/// The numbers Distronode holds for the workspace.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct ManagedNumbersView {
    /// [`MANAGED_HEADING`].
    pub heading: String,
    /// [`MANAGED_BODY`].
    pub body: String,
    /// The carrier, or "Distronode".
    pub carrier: String,
    /// The numbers, grouped.
    pub numbers: String,
}

/// One channel's sender.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct ChannelSenderView {
    /// The channel.
    pub channel: SenderChannel,
    /// Its name ("SMS").
    pub label: String,
    /// The accounts to choose from, and the one chosen ("" for none of its
    /// own, which reads [`DEFAULT_SENDER`]).
    pub picker: PickerView,
}

/// The owner's mobile number box.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct CreatorCellView {
    /// [`CREATOR_HEADING`].
    pub heading: String,
    /// What it is for; the saved number is never shown.
    pub help: String,
    /// What is typed.
    pub number: String,
    /// Whether "Save number" works.
    pub can_save: bool,
    /// Whether it is being saved.
    pub saving: bool,
}

/// The question before removing an account.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct MessagingConfirmView {
    /// The heading.
    pub title: String,
    /// What removing does.
    pub body: String,
    /// The confirming button's label.
    pub action: String,
}

/// The form adding or editing an account.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct MessagingFormView {
    /// "Add a carrier account" or "Edit the carrier account".
    pub title: String,
    /// The carriers.
    pub carriers: Vec<CarrierChoiceView>,
    /// The carrier chosen.
    pub carrier: MessagingCarrier,
    /// Whose account it can be.
    pub sources: Vec<SourceChoiceView>,
    /// Whose account it is.
    pub source: MessagingSource,
    /// The account's name.
    pub label: String,
    /// The carrier's key boxes, in order.
    pub keys: Vec<KeyFieldView>,
    /// The line under the boxes: blank keeps the saved value, or every key is
    /// needed.
    pub keys_note: String,
    /// The warning after changing an existing account's carrier.
    pub carrier_switch: Option<String>,
    /// The numbers, one per line.
    pub phone_numbers: String,
    /// What the numbers box does.
    pub numbers_help: String,
    /// Whether to make it the default sender.
    pub make_default: bool,
    /// Whether the form can be changed (not while a change or check is on
    /// its way).
    pub editable: bool,
    /// Whether "Save" works.
    pub can_save: bool,
    /// Whether "Check these keys" works: every box typed.
    pub can_test: bool,
    /// Why the check is not offered yet.
    pub test_hint: Option<String>,
    /// What the last check came to.
    pub test: Option<KeyCheckView>,
    /// Whether the save is on its way.
    pub saving: bool,
}

/// One carrier, to choose.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct CarrierChoiceView {
    /// The carrier.
    pub carrier: MessagingCarrier,
    /// Its name.
    pub label: String,
}

/// One source, to choose.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct SourceChoiceView {
    /// The source.
    pub source: MessagingSource,
    /// What it reads.
    pub label: String,
}

/// One key box. A secret box never carries what is typed in it.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct KeyFieldView {
    /// The box.
    pub field: KeyField,
    /// Its label.
    pub label: String,
    /// Whether it is a secret (a password box).
    pub secret: bool,
    /// What is typed, for a box that is not a secret; always empty for one
    /// that is.
    pub value: String,
    /// Whether anything is typed in it. A secret box the core has emptied
    /// (a change of carrier, the form closed) is emptied in the app too.
    pub filled: bool,
}

/// What checking the keys came to.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct KeyCheckView {
    /// What it says.
    pub message: String,
    /// How it went.
    pub outcome: KeyCheckOutcome,
}

/// How checking the keys went.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Serialize, uniffi::Enum)]
pub enum KeyCheckOutcome {
    /// Asking the carrier.
    Running,
    /// The carrier accepted them.
    Passed,
    /// The carrier refused them: an answer about what was typed.
    Refused,
    /// The carrier could not be asked.
    Unreachable,
}

/// Something the member did on the messaging accounts section.
#[derive(Clone, PartialEq, Eq, uniffi::Enum)]
pub enum MessagingAction {
    /// Open the section.
    Open,
    /// Open the form for a new account.
    StartAdd,
    /// Open the form for a listed account.
    StartEdit {
        /// The account.
        account_id: String,
    },
    /// Choose the carrier.
    SetCarrier {
        /// The carrier.
        carrier: MessagingCarrier,
    },
    /// Choose whose account it is.
    SetSource {
        /// The source.
        source: MessagingSource,
    },
    /// The account's name changed.
    EditLabel {
        /// As typed.
        label: String,
    },
    /// A key box changed. The value is never printed.
    EditKey {
        /// The box.
        field: KeyField,
        /// As typed.
        value: String,
    },
    /// The numbers box changed.
    EditNumbers {
        /// As typed, one per line.
        numbers: String,
    },
    /// Make it the default sender, or not.
    SetMakeDefault {
        /// Whether to.
        on: bool,
    },
    /// Close the form, dropping what was typed.
    CloseForm,
    /// Save the form's account.
    Save,
    /// Ask the carrier whether the typed keys work.
    CheckKeys,
    /// Make a listed account the default sender.
    MakeDefault {
        /// The account.
        account_id: String,
    },
    /// Send one channel from a listed account.
    SetChannelSender {
        /// The channel.
        channel: SenderChannel,
        /// The account.
        account_id: String,
    },
    /// Ask before removing a listed account.
    AskRemove {
        /// The account.
        account_id: String,
    },
    /// Remove it, after the question.
    ConfirmRemove,
    /// Do not remove it.
    CancelRemove,
    /// The owner's mobile number changed.
    EditOwnerNumber {
        /// As typed.
        number: String,
    },
    /// Save the owner's mobile number.
    SaveOwnerNumber,
    /// Put the notice away.
    DismissNotice,
}

/// `Debug` without a typed key: the one place a secret crosses the boundary.
impl fmt::Debug for MessagingAction {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            Self::EditKey { field, .. } => f
                .debug_struct("EditKey")
                .field("field", field)
                .field("value", &"<redacted>")
                .finish(),
            other => f.write_str(action_name(other)),
        }
    }
}

/// An action's name, for `Debug`.
fn action_name(action: &MessagingAction) -> &'static str {
    match action {
        MessagingAction::Open => "Open",
        MessagingAction::StartAdd => "StartAdd",
        MessagingAction::StartEdit { .. } => "StartEdit",
        MessagingAction::SetCarrier { .. } => "SetCarrier",
        MessagingAction::SetSource { .. } => "SetSource",
        MessagingAction::EditLabel { .. } => "EditLabel",
        MessagingAction::EditKey { .. } => "EditKey",
        MessagingAction::EditNumbers { .. } => "EditNumbers",
        MessagingAction::SetMakeDefault { .. } => "SetMakeDefault",
        MessagingAction::CloseForm => "CloseForm",
        MessagingAction::Save => "Save",
        MessagingAction::CheckKeys => "CheckKeys",
        MessagingAction::MakeDefault { .. } => "MakeDefault",
        MessagingAction::SetChannelSender { .. } => "SetChannelSender",
        MessagingAction::AskRemove { .. } => "AskRemove",
        MessagingAction::ConfirmRemove => "ConfirmRemove",
        MessagingAction::CancelRemove => "CancelRemove",
        MessagingAction::EditOwnerNumber { .. } => "EditOwnerNumber",
        MessagingAction::SaveOwnerNumber => "SaveOwnerNumber",
        MessagingAction::DismissNotice => "DismissNotice",
    }
}

/// The core events `action` is, in the order the core is to hear them.
pub(crate) fn events(action: MessagingAction) -> Vec<Event> {
    let event = |event| Event::Messaging(event);
    let form = |edit| Event::Messaging(MessagingEvent::Form(edit));
    vec![match action {
        MessagingAction::Open => Event::Navigate(Route::Workspace(WorkspaceSection::Messaging)),
        MessagingAction::StartAdd => event(MessagingEvent::StartAdd),
        MessagingAction::StartEdit { account_id } => {
            event(MessagingEvent::StartEdit { account_id })
        }
        MessagingAction::SetCarrier { carrier } => {
            form(MessagingFormEdit::Provider(carrier.into()))
        }
        MessagingAction::SetSource { source } => {
            form(MessagingFormEdit::CredentialSource(source.into()))
        }
        MessagingAction::EditLabel { label } => form(MessagingFormEdit::Label(label)),
        MessagingAction::EditKey { field, value } => form(MessagingFormEdit::Credential {
            field: field.into(),
            value: SecretText::new(value),
        }),
        MessagingAction::EditNumbers { numbers } => form(MessagingFormEdit::PhoneNumbers(numbers)),
        MessagingAction::SetMakeDefault { on } => form(MessagingFormEdit::MakeDefault(on)),
        MessagingAction::CloseForm => event(MessagingEvent::CloseForm),
        MessagingAction::Save => event(MessagingEvent::SaveAccount),
        MessagingAction::CheckKeys => event(MessagingEvent::TestCredentials),
        MessagingAction::MakeDefault { account_id } => {
            event(MessagingEvent::SetDefault { account_id })
        }
        MessagingAction::SetChannelSender {
            channel,
            account_id,
        } => event(MessagingEvent::SetChannelDefault {
            channel: channel.into(),
            account_id,
        }),
        MessagingAction::AskRemove { account_id } => {
            event(MessagingEvent::AskDelete { account_id })
        }
        MessagingAction::ConfirmRemove => event(MessagingEvent::ConfirmDelete),
        MessagingAction::CancelRemove => event(MessagingEvent::CancelDelete),
        MessagingAction::EditOwnerNumber { number } => {
            event(MessagingEvent::EditCreatorCell(number))
        }
        MessagingAction::SaveOwnerNumber => event(MessagingEvent::SaveCreatorCell),
        MessagingAction::DismissNotice => event(MessagingEvent::DismissNotice),
    }]
}

/// The page of the messaging accounts, for a signed-in model.
pub(crate) fn screen(_model: &Model, signed_in: &SignedIn) -> ScreenView {
    ScreenView::Messaging {
        view: signed_in
            .messaging
            .as_ref()
            .map_or_else(MessagingView::default, |section| {
                messaging_view(section, &signed_in.capabilities())
            }),
    }
}

fn messaging_view(section: &MessagingSection, capabilities: &Capabilities) -> MessagingView {
    let can_change = capabilities.can_change;
    let editable = section.can_edit_now();
    let mut view = MessagingView {
        viewer_note: (!can_change).then(|| MessagingSection::VIEWER.to_owned()),
        notice: save_notice(&section.write),
        busy: section.busy(),
        editable,
        confirm: section.confirming.as_ref().map(confirm_view),
        form: section
            .form
            .as_ref()
            .map(|form| form_view(form, section.busy())),
        ..MessagingView::default()
    };
    match &section.accounts {
        MessagingAccounts::Loading => {}
        MessagingAccounts::Failed(failure) => {
            view.status = SectionStatus::Failed {
                title: FAILED_TITLE.to_owned(),
                failure: failure.into(),
            };
        }
        MessagingAccounts::Ready(response) => {
            view.status = SectionStatus::Ready;
            view.offers_add = can_change;
            view.empty = response.accounts.is_empty().then(|| {
                EmptyView::new(MessagingSection::EMPTY_TITLE, MessagingSection::EMPTY_BODY)
            });
            view.accounts = response
                .accounts
                .iter()
                .map(|account| account_view(account, response, can_change))
                .collect();
            view.managed = response
                .managed_account
                .as_ref()
                .map(|managed| ManagedNumbersView {
                    heading: MANAGED_HEADING.to_owned(),
                    body: MANAGED_BODY.to_owned(),
                    carrier: managed
                        .provider
                        .as_deref()
                        .map_or_else(|| "Distronode".to_owned(), carrier_words),
                    numbers: numbers_words(&managed.phone_numbers),
                });
            if !response.accounts.is_empty() {
                view.channels = MESSAGING_CHANNELS
                    .iter()
                    .map(|channel| channel_view(*channel, response))
                    .collect();
            }
            view.channels_editable = can_change;
            view.creator = can_change.then(|| CreatorCellView {
                heading: CREATOR_HEADING.to_owned(),
                help: MessagingSection::CREATOR_CELL_HELP.to_owned(),
                number: section.creator_cell.clone(),
                can_save: editable && !section.creator_cell.trim().is_empty(),
                saving: section.busy()
                    && section.last_write == Some(district_core::MessagingAction::CreatorCell),
            });
        }
    }
    view
}

/// A stored carrier's name: its own when this build knows it, else as stored.
fn carrier_words(stored: &str) -> String {
    PROVIDERS
        .iter()
        .find(|provider| provider.as_str() == stored)
        .map_or_else(
            || humanize(stored),
            |provider| provider_label(*provider).to_owned(),
        )
}

/// Whose account a stored source says it is.
fn source_words(stored: &str) -> String {
    SOURCES
        .iter()
        .find(|source| source_wire(**source) == stored)
        .map_or_else(
            || humanize(stored),
            |source| credential_source_label(*source).to_owned(),
        )
}

/// A source as it is stored.
fn source_wire(source: MessagingCredentialSource) -> &'static str {
    match source {
        MessagingCredentialSource::Byok => "byok",
        MessagingCredentialSource::Managed => "managed",
    }
}

/// Numbers, grouped for reading.
fn numbers_words(numbers: &[String]) -> String {
    if numbers.is_empty() {
        return "No numbers".to_owned();
    }
    numbers
        .iter()
        .map(|number| format_phone_number(number))
        .collect::<Vec<_>>()
        .join(", ")
}

fn account_view(
    account: &MessagingAccount,
    response: &MessagingResponse,
    can_change: bool,
) -> MessagingAccountView {
    let is_default = response.default_account_id.as_deref() == Some(account.id.as_str());
    let known = PROVIDERS
        .iter()
        .any(|provider| provider.as_str() == account.provider)
        && SOURCES
            .iter()
            .any(|source| source_wire(*source) == account.credential_source);
    MessagingAccountView {
        id: account.id.clone(),
        label: account.label.clone(),
        line: format!(
            "{} \u{b7} {} \u{b7} {}",
            carrier_words(&account.provider),
            source_words(&account.credential_source),
            numbers_words(&account.phone_numbers)
        ),
        is_default,
        offers_make_default: can_change && !is_default,
        offers_edit: can_change && known,
        offers_remove: can_change,
    }
}

/// A channel as its sender is stored: its name on the wire.
fn channel_key(channel: MessagingChannel) -> &'static str {
    match channel {
        MessagingChannel::Sms => "sms",
        MessagingChannel::Voice => "voice",
        MessagingChannel::Whatsapp => "whatsapp",
    }
}

fn channel_view(channel: MessagingChannel, response: &MessagingResponse) -> ChannelSenderView {
    let stored = response
        .channel_defaults
        .get(channel_key(channel))
        .cloned()
        .unwrap_or_default();
    let selected_label = if stored.is_empty() {
        DEFAULT_SENDER.to_owned()
    } else {
        response
            .accounts
            .iter()
            .find(|account| account.id == stored)
            .map_or_else(
                || UNLISTED_SENDER.to_owned(),
                |account| account.label.clone(),
            )
    };
    ChannelSenderView {
        channel: channel.into(),
        label: channel_label(channel).to_owned(),
        picker: PickerView {
            choices: response
                .accounts
                .iter()
                .map(|account| ChoiceView {
                    value: account.id.clone(),
                    label: account.label.clone(),
                })
                .collect(),
            selected: stored,
            selected_label,
        },
    }
}

fn confirm_view(confirm: &MessagingDeleteConfirm) -> MessagingConfirmView {
    MessagingConfirmView {
        title: MessagingDeleteConfirm::TITLE.to_owned(),
        body: confirm.body(),
        action: MessagingDeleteConfirm::ACTION.to_owned(),
    }
}

fn form_view(form: &MessagingForm, busy: bool) -> MessagingFormView {
    let saving = busy && form.test != CredentialTest::Running;
    MessagingFormView {
        title: if form.account_id.is_some() {
            "Edit the carrier account"
        } else {
            "Add a carrier account"
        }
        .to_owned(),
        carriers: PROVIDERS
            .iter()
            .map(|provider| CarrierChoiceView {
                carrier: (*provider).into(),
                label: provider_label(*provider).to_owned(),
            })
            .collect(),
        carrier: form.provider.into(),
        sources: SOURCES
            .iter()
            .map(|source| SourceChoiceView {
                source: (*source).into(),
                label: credential_source_label(*source).to_owned(),
            })
            .collect(),
        source: form.credential_source.into(),
        label: form.label.clone(),
        keys: CredentialField::for_provider(form.provider)
            .iter()
            .map(|field| {
                let typed = form.credential(*field);
                KeyFieldView {
                    field: (*field).into(),
                    label: field.label().to_owned(),
                    secret: field.is_secret(),
                    value: if field.is_secret() {
                        String::new()
                    } else {
                        typed.to_owned()
                    },
                    filled: !typed.is_empty(),
                }
            })
            .collect(),
        keys_note: if form.secrets_required() {
            KEYS_NEEDED
        } else {
            MessagingForm::SECRET_KEEP
        }
        .to_owned(),
        carrier_switch: (form.account_id.is_some() && form.secrets_required())
            .then(|| MessagingForm::PROVIDER_SWITCH.to_owned()),
        phone_numbers: form.phone_numbers.clone(),
        numbers_help: MessagingForm::NUMBERS_HELP.to_owned(),
        make_default: form.make_default,
        editable: !busy,
        can_save: !busy && form.can_save(),
        can_test: !busy && form.can_test(),
        test_hint: (!form.can_test()).then(|| MessagingForm::TEST_NEEDS_EVERY_FIELD.to_owned()),
        test: key_check(&form.test),
        saving,
    }
}

/// What checking the keys came to, in the Linux app's words.
fn key_check(test: &CredentialTest) -> Option<KeyCheckView> {
    let view = |message: String, outcome| Some(KeyCheckView { message, outcome });
    match test {
        CredentialTest::Idle => None,
        CredentialTest::Running => view("Asking the carrier.".to_owned(), KeyCheckOutcome::Running),
        CredentialTest::Passed(Some(account)) => view(
            format!("The carrier accepted these keys, for {account}."),
            KeyCheckOutcome::Passed,
        ),
        CredentialTest::Passed(None) => view(
            "The carrier accepted these keys.".to_owned(),
            KeyCheckOutcome::Passed,
        ),
        CredentialTest::Rejected(reason) => view(
            format!("The carrier refused these keys: {reason}"),
            KeyCheckOutcome::Refused,
        ),
        CredentialTest::Unreachable(failure) => view(
            format!(
                "The carrier could not be asked, so nothing is known about these keys. {}",
                FailureView::from(failure).message
            ),
            KeyCheckOutcome::Unreachable,
        ),
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn the_kinds_cross_both_ways() {
        for carrier in [
            MessagingCarrier::Twilio,
            MessagingCarrier::Sinch,
            MessagingCarrier::Telnyx,
        ] {
            assert_eq!(
                MessagingCarrier::from(MessagingProvider::from(carrier)),
                carrier
            );
        }
        for source in [MessagingSource::Own, MessagingSource::Managed] {
            assert_eq!(
                MessagingSource::from(MessagingCredentialSource::from(source)),
                source
            );
        }
        for channel in [
            SenderChannel::Sms,
            SenderChannel::Voice,
            SenderChannel::Whatsapp,
        ] {
            assert_eq!(
                SenderChannel::from(MessagingChannel::from(channel)),
                channel
            );
        }
        for field in [
            KeyField::AccountSid,
            KeyField::AuthToken,
            KeyField::ProjectId,
            KeyField::KeyId,
            KeyField::KeySecret,
            KeyField::ApplicationKey,
            KeyField::ApplicationSecret,
            KeyField::ApiKey,
        ] {
            assert_eq!(KeyField::from(CredentialField::from(field)), field);
        }
    }

    #[test]
    fn words_read_as_the_linux_app_puts_them() {
        assert_eq!(carrier_words("twilio"), "Twilio");
        assert_eq!(carrier_words("vonage"), "Vonage");
        assert_eq!(source_words("managed"), "Managed by Distronode");
        assert_eq!(source_words("partner"), "Partner");
        assert_eq!(numbers_words(&[]), "No numbers");
    }

    /// A typed key never reaches `Debug`; every action has a name there.
    #[test]
    fn a_typed_key_is_never_printed() {
        let printed = format!(
            "{:?}",
            MessagingAction::EditKey {
                field: KeyField::AuthToken,
                value: "tok-very-secret".to_owned(),
            }
        );
        assert!(!printed.contains("tok-very-secret"), "{printed}");
        assert!(printed.contains("AuthToken"));
        for action in [
            MessagingAction::Open,
            MessagingAction::StartAdd,
            MessagingAction::StartEdit {
                account_id: "a".to_owned(),
            },
            MessagingAction::SetCarrier {
                carrier: MessagingCarrier::Sinch,
            },
            MessagingAction::SetSource {
                source: MessagingSource::Own,
            },
            MessagingAction::EditLabel {
                label: "x".to_owned(),
            },
            MessagingAction::EditNumbers {
                numbers: "x".to_owned(),
            },
            MessagingAction::SetMakeDefault { on: true },
            MessagingAction::CloseForm,
            MessagingAction::Save,
            MessagingAction::CheckKeys,
            MessagingAction::MakeDefault {
                account_id: "a".to_owned(),
            },
            MessagingAction::SetChannelSender {
                channel: SenderChannel::Sms,
                account_id: "a".to_owned(),
            },
            MessagingAction::AskRemove {
                account_id: "a".to_owned(),
            },
            MessagingAction::ConfirmRemove,
            MessagingAction::CancelRemove,
            MessagingAction::EditOwnerNumber {
                number: "x".to_owned(),
            },
            MessagingAction::SaveOwnerNumber,
            MessagingAction::DismissNotice,
        ] {
            let printed = format!("{action:?}");
            assert_eq!(printed, action_name(&action));
        }
    }
}
