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
