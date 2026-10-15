//! Developer: the API keys that let the workspace's own tools reach its
//! bookings, the apps connected with OAuth beside the address an AI
//! assistant reaches the bookings at (MCP), and the webhooks with their
//! deliveries, a tab each, as District AI for iPhone's
//! `SchedulingDeveloperView`, its API key sheets and its webhook editor.
//!
//! Each tab is read the first time it shows, and its own Try again reads it
//! alone (`DeveloperAction::Refresh`, not the window's Refresh, which would
//! enter the section afresh on its first tab). Reading is open to every
//! member, deliveries included; making a key, adding or changing a webhook,
//! and revoking or deleting are offered only to a member who may change the
//! workspace. Revoking a key or an app and deleting a webhook are asked once
//! more, the confirm destructive and never the default. A webhook opens to
//! its deliveries, read the first time.
//!
//! A key, and a webhook's signing secret, are shown once, in the dialog that
//! made them, and the core forgets them as the dialog closes. They cross to
//! the window as UTF-8 bytes in [`SecretView::value`], so that the generated
//! C# record's `ToString` prints the array's type and never the value; the
//! field is left out of the record's `Debug` and of its JSON, so neither a
//! log nor a projection snapshot can hold it.

use district_core::scheduling_admin::{
    DeveloperDialog, DeveloperEvent, DeveloperSection, DeveloperTab, KeyDialog, MintedSecret,
    Removal, SectionLoad, WEBHOOK_EVENTS, WEBHOOK_FIELDS, WebhookEditor, delivery_outcome,
    is_personal_data,
};
use district_core::{Event, SchedulingAdmin, SchedulingAdminEvent};
use district_l10n::scheduling_strings as ss;
use district_model::SchedulingWebhook;
use serde::Serialize;

use super::{DialogView, SchedulingAdminPage, dialog_view, landed, load_status};
use crate::l10n::l;
use crate::views::{FailureView, LoadStatus, failure};
use crate::workflows::RunTone;

/// Whether this version has the section's pages.
pub(crate) const BUILT: bool = true;

/// A tab of the section.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Serialize, uniffi::Enum)]
pub enum DeveloperTabView {
    /// API keys.
    Keys,
    /// Connected apps, and the MCP address.
    Apps,
    /// Webhooks and their deliveries.
    Webhooks,
}

impl From<DeveloperTab> for DeveloperTabView {
    fn from(tab: DeveloperTab) -> Self {
        match tab {
            DeveloperTab::Keys => Self::Keys,
            DeveloperTab::Apps => Self::Apps,
            DeveloperTab::Webhooks => Self::Webhooks,
        }
    }
}

impl From<DeveloperTabView> for DeveloperTab {
    fn from(tab: DeveloperTabView) -> Self {
        match tab {
            DeveloperTabView::Keys => Self::Keys,
            DeveloperTabView::Apps => Self::Apps,
            DeveloperTabView::Webhooks => Self::Webhooks,
        }
    }
}

/// Developer.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct SchedulingDeveloperView {
    /// "Times in America/Toronto".
    pub times_in: String,
    /// The tab shown.
    pub tab: DeveloperTabView,
    /// The tabs, in order, the one shown selected
    /// (`DeveloperAction::SelectTab`).
    pub tabs: Vec<DeveloperTabEntryView>,
    /// Whether the writes are offered: the member may change things.
    pub offers_writes: bool,
    /// The keys' heading.
    pub keys_heading: String,
    /// What a key is for.
    pub keys_subtitle: String,
    /// Where the read of the keys stands; not yet read is still loading.
    pub keys_status: LoadStatus,
    /// The keys, once read; never the key itself.
    pub keys: Vec<ApiKeyRowView>,
    /// Said when the keys are read and there are none.
    pub keys_empty: String,
    /// "Create key" (`DeveloperAction::OpenCreateKey`).
    pub create_key_label: String,
    /// "Revoke key", on each key (`DeveloperAction::AskRevokeKey`).
    pub revoke_key_label: String,
    /// The MCP card's heading.
    pub mcp_heading: String,
    /// The MCP address and the header to send; `None` while the scheduler has
    /// no public address.
    pub mcp: Option<McpView>,
    /// Said while the scheduler has no public address.
    pub mcp_no_host: String,
    /// What the address is for, under it.
    pub mcp_note: String,
    /// The connected apps' heading.
    pub apps_heading: String,
    /// What a connected app is.
    pub apps_subtitle: String,
    /// Where the read of the apps stands.
    pub apps_status: LoadStatus,
    /// The connected apps, once read.
    pub apps: Vec<ConnectedAppRowView>,
    /// Said when the apps are read and there are none.
    pub apps_empty: String,
    /// "Revoke access", on each app (`DeveloperAction::AskRevokeApp`).
    pub revoke_app_label: String,
    /// The webhooks' heading.
    pub webhooks_heading: String,
    /// What a webhook does.
    pub webhooks_subtitle: String,
    /// Where the read of the webhooks stands.
    pub webhooks_status: LoadStatus,
    /// The webhooks, once read.
    pub webhooks: Vec<WebhookRowView>,
    /// Said when the webhooks are read and there are none.
    pub webhooks_empty: String,
    /// "Add webhook" (`DeveloperAction::OpenCreateWebhook`).
    pub add_webhook_label: String,
    /// "Edit", on each webhook (`DeveloperAction::OpenEditWebhook`).
    pub edit_webhook_label: String,
    /// "Delete webhook", on each webhook (`DeveloperAction::AskDeleteWebhook`).
    pub delete_webhook_label: String,
    /// "Deliveries", on each webhook (`DeveloperAction::ToggleDeliveries`).
    pub deliveries_label: String,
    /// The deliveries of the webhook open, if one is.
    pub deliveries: Option<DeliveriesView>,
    /// Whether a removal is on its way: every Revoke and Delete waits.
    pub removing: bool,
    /// Why the last removal failed; put away by the next one asked.
    pub removal_failure: Option<FailureView>,
    /// A removal, while it is asked about: the question naming what goes,
    /// what removing does, the destructive confirm and the way out.
    pub removal: Option<DialogView>,
    /// Making a key, while its dialog is open.
    pub key_dialog: Option<KeyDialogView>,
    /// Adding or changing a webhook, while its dialog is open.
    pub webhook_dialog: Option<WebhookEditorView>,
}

/// One tab, as the tab bar lists it.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct DeveloperTabEntryView {
    /// Which.
    pub tab: DeveloperTabView,
    /// Its label: "API keys".
    pub label: String,
    /// Whether it is shown.
    pub selected: bool,
}

/// One API key: its name, and when it was made and last used.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct ApiKeyRowView {
    /// Its id, which its Revoke names.
    pub id: String,
    /// Its name.
    pub name: String,
    /// "Created … · Last used …", in the member's zone.
    pub line: String,
}

/// The MCP address, and the header an assistant sends with it.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct McpView {
    /// "MCP URL".
    pub url_label: String,
    /// `https://book.example.com/mcp`.
    pub url: String,
    /// "Authorization header".
    pub auth_label: String,
    /// The header's value, with a placeholder where the key goes: never a
    /// key.
    pub auth: String,
}

/// One app connected with OAuth.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct ConnectedAppRowView {
    /// The connection's id, which its Revoke names.
    pub id: String,
    /// The app's name.
    pub name: String,
    /// When it last reached the bookings, or "Never".
    pub last_used: String,
}

/// One webhook.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct WebhookRowView {
    /// Its id, which its actions name.
    pub id: String,
    /// Where its deliveries go.
    pub url: String,
    /// The events it sends, by their wire names, as the scheduler lists them.
    pub events: String,
    /// "On", or "Off" once switched off.
    pub state_label: String,
    /// Its tone.
    pub tone: RunTone,
    /// Whether its deliveries are open.
    pub open: bool,
}

/// The deliveries of the webhook open.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct DeliveriesView {
    /// The webhook's id.
    pub webhook_id: String,
    /// Where their read stands; a failure is said under the webhook, with
    /// "Try again" (`DeveloperAction::RetryDeliveries`).
    pub status: LoadStatus,
    /// The deliveries, once read.
    pub rows: Vec<DeliveryRowView>,
    /// Said when they are read and there are none.
    pub empty: String,
}

/// One delivery.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct DeliveryRowView {
    /// Its id.
    pub id: String,
    /// When it was last tried, in the member's zone, or "Not tried yet".
    pub when: String,
    /// The event it carried, by its wire name.
    pub event: String,
    /// What it came to: "Delivered", "Waiting", "No answer", "Rejected
    /// (500)", or the status as it came.
    pub outcome: String,
    /// Its tone.
    pub tone: RunTone,
}

/// A value shown once: a key just made, or a webhook's signing secret.
#[derive(Clone, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct SecretView {
    /// That it is shown once, so to copy it now.
    pub warning: String,
    /// Where it is used, when there is something to say.
    pub note: Option<String>,
    /// "Copy key", "Copied", or "Copy secret".
    pub copy_label: String,
    /// The value, as UTF-8: shown, and copied when asked, and never kept.
    /// Left out of `Debug` and of the JSON.
    #[serde(skip)]
    pub value: Vec<u8>,
}

impl std::fmt::Debug for SecretView {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("SecretView")
            .field("warning", &self.warning)
            .field("note", &self.note)
            .field("copy_label", &self.copy_label)
            .field("value", &"<redacted>")
            .finish()
    }
}

/// Making an API key: its name, then the key, shown once.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct KeyDialogView {
    /// Its title ("Create API key", then "Your key for …"), the create
    /// ("Create key", "Creating…"; none once made), the way out ("Cancel",
    /// then "Done") and why the create failed.
    pub dialog: DialogView,
    /// "Name".
    pub name_label: String,
    /// What the name is for.
    pub name_hint: String,
    /// The name, as the core holds it.
    pub name: String,
    /// Why the name cannot be sent, before anything was sent.
    pub name_error: Option<String>,
    /// Whether the name can be changed: nothing is on its way.
    pub editable: bool,
    /// The key made, once it is.
    pub minted: Option<SecretView>,
}

/// Adding a webhook, or changing what one sends: where it goes (fixed once
/// added), the events it sends and the parts of a booking each delivery
/// carries; then, for one just added, its signing secret, shown once.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct WebhookEditorView {
    /// Its title ("Add webhook", "Edit webhook", then "Signing secret"), the
    /// save ("Add webhook", "Save changes", "Saving…"; none once the secret
    /// shows), the way out ("Cancel", then "Done") and why the save failed.
    pub dialog: DialogView,
    /// Whether this adds a webhook: its address can be typed.
    pub creating: bool,
    /// "Endpoint URL".
    pub url_label: String,
    /// What the address must be, or, for a webhook added, that it is fixed.
    pub url_hint: String,
    /// The address, as the core holds it.
    pub url: String,
    /// Why the address cannot be sent, before anything was sent.
    pub url_error: Option<String>,
    /// The events this webhook sends that this version does not know, which
    /// a save stops.
    pub dropped: Option<String>,
    /// "Events".
    pub events_label: String,
    /// What they are.
    pub events_hint: String,
    /// Every event a webhook can send, chosen or not.
    pub events: Vec<ToggleRowView>,
    /// Why the events cannot be sent: none chosen.
    pub events_error: Option<String>,
    /// "Fields".
    pub fields_label: String,
    /// What they are.
    pub fields_hint: String,
    /// Every part of a booking a delivery can carry, chosen or not, a
    /// person's own data marked.
    pub fields: Vec<ToggleRowView>,
    /// Whether the form can be changed: nothing is on its way.
    pub editable: bool,
    /// What the last save came to ("Webhook updated"), once it landed.
    pub notice: Option<String>,
    /// The signing secret of the webhook just added, once it is.
    pub minted: Option<SecretView>,
}

/// One event or field of the webhook form.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct ToggleRowView {
    /// Its wire name, which its switch sends and shows.
    pub value: String,
    /// Whether it is chosen.
    pub on: bool,
    /// "Personal data", for a person's own data.
    pub mark: Option<String>,
}

/// Something the member did on Developer.
#[derive(Clone, Debug, PartialEq, Eq, uniffi::Enum)]
pub enum DeveloperAction {
    /// Show a tab, read the first time.
    SelectTab {
        /// Which.
        tab: DeveloperTabView,
    },
    /// Read the tab shown again.
    Refresh,
    /// Open a webhook's deliveries, or close them.
    ToggleDeliveries {
        /// The webhook's id.
        webhook_id: String,
    },
    /// "Try again" under the open webhook: read its deliveries again after
    /// their read failed.
    RetryDeliveries,
    /// Open the key form.
    OpenCreateKey,
    /// The key's name changed.
    EditKeyName {
        /// The name, as typed.
        name: String,
    },
    /// Make the key.
    CreateKey,
    /// The key was copied.
    KeyCopied,
    /// Ask about revoking a key.
    AskRevokeKey {
        /// The key's id.
        key_id: String,
    },
    /// Ask about revoking an app's access.
    AskRevokeApp {
        /// The connection's id.
        connection_id: String,
    },
    /// Ask about deleting a webhook.
    AskDeleteWebhook {
        /// The webhook's id.
        webhook_id: String,
    },
    /// Keep it.
    CancelRemoval,
    /// Remove it.
    ConfirmRemoval,
    /// Open the add form.
    OpenCreateWebhook,
    /// Open a webhook's form.
    OpenEditWebhook {
        /// The webhook's id.
        webhook_id: String,
    },
    /// The address changed (an add only).
    EditWebhookUrl {
        /// The address, as typed.
        url: String,
    },
    /// Choose an event, or take it out.
    ToggleEvent {
        /// Its wire name.
        event: String,
    },
    /// Choose a field, or take it out.
    ToggleField {
        /// Its wire name.
        field: String,
    },
    /// Add the webhook, or save what it sends.
    SaveWebhook,
    /// Close the dialog open.
    CloseDialog,
}

/// The core events `action` is.
pub(crate) fn events(action: DeveloperAction) -> Vec<Event> {
    use DeveloperAction as A;
    use DeveloperEvent as E;
    let event = match action {
        A::SelectTab { tab } => E::SelectTab(tab.into()),
        A::Refresh => E::Refresh,
        A::ToggleDeliveries { webhook_id } => E::ToggleDeliveries(webhook_id),
        A::RetryDeliveries => E::RetryDeliveries,
        A::OpenCreateKey => E::OpenCreateKey,
        A::EditKeyName { name } => E::EditKeyName(name),
        A::CreateKey => E::CreateKey,
        A::KeyCopied => E::KeyCopied,
        A::AskRevokeKey { key_id } => E::AskRevokeKey(key_id),
        A::AskRevokeApp { connection_id } => E::AskRevokeApp(connection_id),
        A::AskDeleteWebhook { webhook_id } => E::AskDeleteWebhook(webhook_id),
        A::CancelRemoval => E::CancelRemoval,
        A::ConfirmRemoval => E::ConfirmRemoval,
        A::OpenCreateWebhook => E::OpenCreateWebhook,
        A::OpenEditWebhook { webhook_id } => E::OpenEditWebhook(webhook_id),
        A::EditWebhookUrl { url } => E::EditWebhookUrl(url),
        A::ToggleEvent { event } => E::ToggleEvent(event),
        A::ToggleField { field } => E::ToggleField(field),
        A::SaveWebhook => E::SaveWebhook,
        A::CloseDialog => E::CloseDialog,
    };
    vec![Event::SchedulingAdmin(SchedulingAdminEvent::Developer(
        event,
    ))]
}

/// Developer's page, for a member who may change things when `can_change`.
/// Before the section is entered, its first tab is being read.
pub(crate) fn page(admin: &SchedulingAdmin, can_change: bool) -> SchedulingAdminPage {
    let fresh = DeveloperSection::default();
    SchedulingAdminPage::Developer {
        view: view(admin.developer.as_ref().unwrap_or(&fresh), can_change),
    }
}

/// The rows of a read, or none until it is in.
fn rows<T>(load: Option<&SectionLoad<Vec<T>>>) -> &[T] {
    load.and_then(SectionLoad::value)
        .map(Vec::as_slice)
        .unwrap_or_default()
}

/// Where a tab's read stands: not read yet is still loading.
fn status_of<T>(load: Option<&SectionLoad<T>>) -> LoadStatus {
    load.map_or(LoadStatus::Loading, load_status)
}

fn view(section: &DeveloperSection, can_change: bool) -> SchedulingDeveloperView {
    let [
        keys_heading,
        keys_subtitle,
        keys_empty,
        apps_heading,
        apps_subtitle,
        apps_empty,
        webhooks_heading,
        webhooks_subtitle,
        webhooks_empty,
        mcp_heading,
        mcp_no_host,
        mcp_url_label,
        mcp_auth_label,
        mcp_note,
        deliveries_label,
        deliveries_empty,
        create_key,
        add_webhook,
    ] = DeveloperSection::labels();
    let keys = rows(section.keys.as_ref());
    let apps = rows(section.apps.as_ref());
    let webhooks: &[SchedulingWebhook] = rows(section.webhooks.as_ref());
    let (mut key_dialog, mut webhook_dialog) = (None, None);
    match &section.dialog {
        Some(DeveloperDialog::Key(dialog)) => key_dialog = Some(key_view(dialog)),
        Some(DeveloperDialog::Webhook(editor)) => webhook_dialog = Some(webhook_view(editor)),
        None => {}
    }
    let removal = &section.removal;
    SchedulingDeveloperView {
        times_in: section.times_in(),
        tab: section.tab.into(),
        tabs: DeveloperTab::ALL
            .into_iter()
            .map(|tab| DeveloperTabEntryView {
                tab: tab.into(),
                label: tab.label().to_owned(),
                selected: tab == section.tab,
            })
            .collect(),
        offers_writes: can_change,
        keys_heading: keys_heading.to_owned(),
        keys_subtitle: keys_subtitle.to_owned(),
        keys_status: status_of(section.keys.as_ref()),
        keys: keys
            .iter()
            .map(|key| ApiKeyRowView {
                id: key.id.clone(),
                name: key.name.clone(),
                line: section.key_line(key),
            })
            .collect(),
        keys_empty: keys_empty.to_owned(),
        create_key_label: create_key.to_owned(),
        revoke_key_label: ss::key_revoke_action(l()).to_owned(),
        mcp_heading: mcp_heading.to_owned(),
        mcp: section.mcp().map(|(url, auth)| McpView {
            url_label: mcp_url_label.to_owned(),
            url,
            auth_label: mcp_auth_label.to_owned(),
            auth,
        }),
        mcp_no_host: mcp_no_host.to_owned(),
        mcp_note: mcp_note.to_owned(),
        apps_heading: apps_heading.to_owned(),
        apps_subtitle: apps_subtitle.to_owned(),
        apps_status: status_of(section.apps.as_ref()),
        apps: apps
            .iter()
            .map(|app| ConnectedAppRowView {
                id: app.id.clone(),
                name: app.client_name.clone(),
                last_used: section.app_line(app),
            })
            .collect(),
        apps_empty: apps_empty.to_owned(),
        revoke_app_label: ss::app_revoke_action(l()).to_owned(),
        webhooks_heading: webhooks_heading.to_owned(),
        webhooks_subtitle: webhooks_subtitle.to_owned(),
        webhooks_status: status_of(section.webhooks.as_ref()),
        webhooks: webhooks
            .iter()
            .map(|webhook| WebhookRowView {
                id: webhook.id.clone(),
                url: webhook.url.clone(),
                events: webhook.events.join(", "),
                state_label: DeveloperSection::webhook_state(webhook).to_owned(),
                tone: if webhook.is_active == Some(false) {
                    RunTone::Neutral
                } else {
                    RunTone::Success
                },
                open: section.open_webhook.as_deref() == Some(webhook.id.as_str()),
            })
            .collect(),
        webhooks_empty: webhooks_empty.to_owned(),
        add_webhook_label: add_webhook.to_owned(),
        edit_webhook_label: ss::common_edit(l()).to_owned(),
        delete_webhook_label: ss::webhook_delete_action(l()).to_owned(),
        deliveries_label: deliveries_label.to_owned(),
        deliveries: section.open_webhook.as_ref().map(|id| {
            let load = section.deliveries.get(id);
            DeliveriesView {
                webhook_id: id.clone(),
                status: status_of(load),
                rows: rows(load)
                    .iter()
                    .map(|delivery| {
                        let (outcome, tone) = delivery_outcome(delivery);
                        DeliveryRowView {
                            id: delivery.id.clone(),
                            when: section.delivery_line(delivery),
                            event: delivery.event.clone(),
                            outcome,
                            tone: RunTone::from(tone),
                        }
                    })
                    .collect(),
                empty: deliveries_empty.to_owned(),
            }
        }),
        removing: removal.busy,
        removal_failure: failure(removal.failure.as_ref()),
        removal: removal.asking.as_ref().map(removal_view),
        key_dialog,
        webhook_dialog,
    }
}

/// A removal, asked once more: never the default, Cancel the way out.
fn removal_view(removal: &Removal) -> DialogView {
    let (question, body, confirm) = removal.prompt();
    dialog_view(
        [question.as_str(), body, confirm, ss::common_cancel(l())],
        &district_core::scheduling_admin::WriteState::Idle,
        true,
        true,
    )
}

/// `value` as the window shows it once.
fn secret(warning: &str, note: Option<String>, copy_label: &str, value: &str) -> SecretView {
    SecretView {
        warning: warning.to_owned(),
        note,
        copy_label: copy_label.to_owned(),
        value: value.as_bytes().to_vec(),
    }
}

/// Making a key.
fn key_view(dialog: &KeyDialog) -> KeyDialogView {
    let [title, name_label, name_hint, warning, done] = KeyDialog::labels();
    let busy = dialog.write.busy();
    let minted = dialog.minted.as_ref();
    let (heading, confirm, close) = match minted {
        Some(key) => (key.title(), "", done),
        None => (
            title.to_owned(),
            dialog.submit_label(),
            ss::common_cancel(l()),
        ),
    };
    KeyDialogView {
        dialog: dialog_view(
            [heading.as_str(), "", confirm, close],
            &dialog.write,
            minted.is_none() && dialog.can_submit(),
            false,
        ),
        name_label: name_label.to_owned(),
        name_hint: name_hint.to_owned(),
        name: dialog.name.clone(),
        name_error: dialog.name_rejected.map(str::to_owned),
        editable: !busy,
        minted: minted.map(|key| secret(warning, None, dialog.copy_label(), key.key.expose())),
    }
}

/// The events or the fields of the form, `chosen` on.
fn toggles(all: &[&str], chosen: &[String]) -> Vec<ToggleRowView> {
    all.iter()
        .map(|value| ToggleRowView {
            value: (*value).to_owned(),
            on: chosen.iter().any(|c| c == value),
            mark: is_personal_data(value).then(|| WebhookEditor::labels()[7].to_owned()),
        })
        .collect()
}

/// Adding or changing a webhook.
fn webhook_view(editor: &WebhookEditor) -> WebhookEditorView {
    let [
        url_label,
        url_hint,
        url_fixed,
        events_label,
        events_hint,
        fields_label,
        fields_hint,
        _,
    ] = WebhookEditor::labels();
    let [secret_title, warning, copy, done] = MintedSecret::labels();
    let busy = editor.write.busy();
    let creating = editor.editing.is_none();
    let minted = editor.minted.as_ref();
    let (title, confirm, close) = match minted {
        Some(_) => (secret_title, "", done),
        None => (
            editor.title(),
            editor.submit_label(),
            ss::common_cancel(l()),
        ),
    };
    WebhookEditorView {
        dialog: dialog_view(
            [title, "", confirm, close],
            &editor.write,
            minted.is_none() && editor.can_submit(),
            false,
        ),
        creating,
        url_label: url_label.to_owned(),
        url_hint: if creating { url_hint } else { url_fixed }.to_owned(),
        url: editor.url.clone(),
        url_error: editor.url_rejected.map(str::to_owned),
        dropped: editor.dropped_line(),
        events_label: events_label.to_owned(),
        events_hint: events_hint.to_owned(),
        events: toggles(&WEBHOOK_EVENTS, &editor.events),
        events_error: editor.events_rejected.map(str::to_owned),
        fields_label: fields_label.to_owned(),
        fields_hint: fields_hint.to_owned(),
        fields: toggles(&WEBHOOK_FIELDS, &editor.fields),
        editable: !busy,
        notice: landed(&editor.write),
        minted: minted.map(|made| secret(warning, Some(made.note()), copy, made.secret.expose())),
    }
}
