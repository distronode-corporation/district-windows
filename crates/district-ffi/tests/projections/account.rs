//! The account and the devices signed in to it (src/account.rs).

use super::*;

/// This area's snapshot cases.
pub(crate) fn cases() -> Vec<Case> {
    account_cases()
}

pub(crate) fn devices(session: Session) -> Session {
    session.ui(UiEvent::OpenDevices)
}

pub(crate) fn devices_loaded(session: Session, value: Value) -> Session {
    session.answer(
        |e| matches!(e, Effect::LoadDevices { .. }),
        |ticket| Event::DevicesLoaded {
            ticket,
            result: Ok(contracts::decode("devices", value)),
        },
    )
}

pub(crate) fn account_cases() -> Vec<Case> {
    let loaded = || {
        devices_loaded(
            devices(signed_in()),
            contracts::json("district-devices.json"),
        )
    };
    vec![
        (
            "account",
            signed_in().ui(UiEvent::Navigate {
                destination: NavDestination::Account,
            }),
        ),
        ("account-purchases-on", account_with_purchases(None)),
        (
            "account-purchases-off",
            account_with_purchases(Some(district_core::PurchaseSetting::Off)),
        ),
        ("devices-loading", devices(signed_in())),
        ("devices-loaded", loaded()),
        (
            "devices-empty",
            devices_loaded(
                devices(signed_in()),
                json!({"success": true, "devices": []}),
            ),
        ),
        (
            "devices-failed",
            devices(signed_in()).answer(
                |e| matches!(e, Effect::LoadDevices { .. }),
                |ticket| Event::DevicesLoaded {
                    ticket,
                    result: Err(server_error()),
                },
            ),
        ),
        (
            "devices-confirming",
            loaded().ui(UiEvent::AskSignOutDevice {
                device_id: "device-contract-android-1".to_owned(),
            }),
        ),
        ("devices-refreshing", loaded().ui(UiEvent::Refresh)),
    ]
}

/// The account page with "Purchases on this computer" read as `setting`.
fn account_with_purchases(setting: Option<district_core::PurchaseSetting>) -> Session {
    signed_in()
        .answer(
            |e| matches!(e, Effect::ReadPurchaseSetting { .. }),
            |ticket| Event::PurchaseSettingRead { ticket, setting },
        )
        .ui(UiEvent::Navigate {
            destination: NavDestination::Account,
        })
}

/// The row shows once the setting is read, on or off, and not before.
#[test]
fn the_purchases_row_shows_the_setting_once_read() {
    let on_of = |session: &Session| match screen_view(&session.model) {
        ScreenView::Account { view } => view.purchases_on,
        other => panic!("not the account: {other:?}"),
    };
    let unread = signed_in().ui(UiEvent::Navigate {
        destination: NavDestination::Account,
    });
    assert_eq!(on_of(&unread), None);
    assert_eq!(on_of(&account_with_purchases(None)), Some(true));
    assert_eq!(
        on_of(&account_with_purchases(Some(
            district_core::PurchaseSetting::Off
        ))),
        Some(false)
    );
}
