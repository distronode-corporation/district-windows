//! The dialler, a placed call and ringing here (src/calls_live.rs).

use super::*;

/// This area's snapshot cases.
pub(crate) fn cases() -> Vec<Case> {
    dialer_cases()
}

/// Signed in to a build that can carry calls.
pub(crate) fn with_calls() -> Session {
    signed_in_with(CoreConfig {
        calls_available: true,
        ..config()
    })
}

/// The dialler and a placed call, in a build with calls; the dialler in one
/// without. No case reaches a call in progress, whose start time is the clock's.
pub(crate) fn dialer_cases() -> Vec<Case> {
    vec![
        ("dialer-without-calls", signed_in().ui(UiEvent::OpenDialer)),
        ("dialer", with_calls().ui(UiEvent::OpenDialer)),
        (
            "dialer-number",
            with_calls()
                .ui(UiEvent::OpenDialer)
                .ui(UiEvent::DialerEdit {
                    number: "+12125550142".to_owned(),
                }),
        ),
        (
            "call-dialing",
            with_calls().ui(UiEvent::CallNumber {
                number: "+12125550142".to_owned(),
            }),
        ),
        (
            "call-not-placed",
            with_calls()
                .ui(UiEvent::CallNumber {
                    number: "+12125550142".to_owned(),
                })
                .answer(
                    |e| matches!(e, Effect::Dial { .. }),
                    |ticket| Event::Dialled {
                        ticket,
                        result: Err(server_error()),
                    },
                ),
        ),
        (
            "account-ring-here",
            with_calls()
                .answer(
                    |e| matches!(e, Effect::ReadRingSetting { .. }),
                    |ticket| Event::RingSettingRead {
                        ticket,
                        ring_here: true,
                    },
                )
                .ui(UiEvent::Navigate {
                    destination: NavDestination::Account,
                }),
        ),
    ]
}
