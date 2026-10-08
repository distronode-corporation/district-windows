//! The account screen and the devices signed in to the account.

use district_core::{
    AccountView as CoreAccount, Confirmation, DeviceRow, DevicesList, DevicesScreen,
};
use serde::Serialize;

use crate::views::{EmptyView, FailureView, LoadStatus, failure};

/// The account screen: who is signed in, on which installation, with which
/// build.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct AccountView {
    /// This build's version.
    pub app_version: String,
    /// The installation id this session was issued to.
    pub device_id: String,
    /// The signed-in user's id.
    pub user_id: String,
    /// The account's email address. Always `None` with core 1.2.0, whose
    /// session holds only the user and device ids.
    pub email: Option<String>,
    /// The account holder's name. Always `None` with core 1.2.0, for the same
    /// reason.
    pub name: Option<String>,
    /// The sign-out row's caption.
    pub sign_out_caption: String,
    /// The devices row's caption.
    pub devices_caption: String,
    /// The account deletion row's caption.
    pub delete_account_caption: String,
}

impl From<CoreAccount> for AccountView {
    fn from(account: CoreAccount) -> Self {
        Self {
            app_version: account.app_version,
            device_id: account.device_id,
            user_id: account.user_id,
            email: None,
            name: None,
            sign_out_caption: CoreAccount::SIGN_OUT_CAPTION.to_owned(),
            devices_caption: CoreAccount::DEVICES_CAPTION.to_owned(),
            delete_account_caption: CoreAccount::DELETE_ACCOUNT_CAPTION.to_owned(),
        }
    }
}

/// The devices signed in to the account.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct DevicesView {
    /// Where the read stands.
    pub status: LoadStatus,
    /// The devices, this one included.
    pub rows: Vec<DeviceRowView>,
    /// Whether a sign-out is on its way (disable the buttons).
    pub busy: bool,
    /// The question before a sign-out, while it is asked.
    pub confirming: Option<ConfirmView>,
    /// Why the last sign-out failed.
    pub failure: Option<FailureView>,
    /// Whether the device asked about had already gone (say
    /// [`DevicesView::nothing_revoked_note`]).
    pub nothing_revoked: bool,
    /// Whether the list is being read again with its rows still showing.
    pub refreshing: bool,
    /// What to say when the list is read and has nothing in it.
    pub empty: Option<EmptyView>,
    /// The note for `nothing_revoked`, when it is set.
    pub nothing_revoked_note: Option<String>,
}

/// One signed-in device.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct DeviceRowView {
    /// The device, to sign it out by.
    pub device_id: String,
    /// Its name, or "Unnamed device".
    pub name: String,
    /// Its platform ("Windows", "iOS").
    pub platform: String,
    /// "Last active ..." in the service's words, or "Signed in recently".
    pub last_active_label: String,
    /// Whether it is this computer.
    pub is_this_device: bool,
    /// When it last renewed its sign-in, ISO 8601, for the app to say in
    /// local time ("Last active " and the time) instead of the label.
    pub last_active_at: Option<String>,
}

/// A question before a sign-out.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct ConfirmView {
    /// The question.
    pub question: String,
    /// The confirming button's label.
    pub action: String,
}

impl From<&Confirmation> for ConfirmView {
    fn from(confirmation: &Confirmation) -> Self {
        Self {
            question: confirmation.question().to_owned(),
            action: confirmation.action().to_owned(),
        }
    }
}

fn device_row(row: &DeviceRow) -> DeviceRowView {
    DeviceRowView {
        device_id: row.device.device_id.clone(),
        name: row.name().to_owned(),
        platform: row.platform().to_owned(),
        last_active_label: row.last_active(),
        is_this_device: row.is_this_device,
        last_active_at: row.device.last_used_at.clone(),
    }
}

/// The devices screen, as the Linux devices page shows it.
pub(crate) fn devices_view(screen: &DevicesScreen) -> DevicesView {
    let mut view = DevicesView {
        status: LoadStatus::Loading,
        rows: Vec::new(),
        busy: screen.busy,
        confirming: screen.confirming.as_ref().map(ConfirmView::from),
        failure: failure(screen.failure.as_ref()),
        nothing_revoked: screen.nothing_revoked,
        refreshing: screen.refreshing,
        empty: None,
        nothing_revoked_note: screen
            .nothing_revoked
            .then(|| DevicesScreen::NOTHING_REVOKED.to_owned()),
    };
    match &screen.list {
        DevicesList::Loading => {}
        DevicesList::Failed(failure) => {
            view.status = LoadStatus::failed(DevicesList::FAILED_TITLE, failure);
        }
        DevicesList::Ready(rows) => {
            view.status = LoadStatus::Ready;
            view.rows = rows.iter().map(device_row).collect();
            view.empty = rows
                .is_empty()
                .then(|| EmptyView::new(DevicesList::EMPTY_TITLE, DevicesList::EMPTY_BODY));
        }
    }
    view
}
