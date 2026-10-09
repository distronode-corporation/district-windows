//! Outside a session: the sign-in page in each phase (src/screen.rs).

use district_api::{ReauthReason, RetryReason, TokenError};
use district_core::{Event, RestoreError};

use super::{Case, plain, restored, sign_in_ticket, signed_in, signed_out, start};

/// Starting up, signed out, signing in and signing out.
pub(crate) fn cases() -> Vec<Case> {
    let mut cases = vec![("restoring", plain(start().0))];

    let (mut model, ticket) = start();
    model.update(restored(
        ticket,
        Err(RestoreError::Token(TokenError::RetryLater(
            RetryReason::Offline,
        ))),
    ));
    cases.push(("restoring-offline", plain(model)));

    let (mut model, ticket) = start();
    model.update(restored(
        ticket,
        Err(RestoreError::Token(TokenError::RetryLater(
            RetryReason::SecretStoreLocked,
        ))),
    ));
    cases.push(("restoring-locked", plain(model)));

    cases.push(("signed-out-first-run", plain(signed_out())));

    let (mut model, ticket) = start();
    model.update(restored(
        ticket,
        Err(RestoreError::Token(TokenError::SignInRequired(
            ReauthReason::InterruptedRefresh,
        ))),
    ));
    cases.push(("signed-out-session-ended", plain(model)));

    let mut model = signed_out();
    model.update(Event::SignIn);
    cases.push(("signing-in-opening-browser", plain(model)));

    let mut model = signed_out();
    let ticket = sign_in_ticket(model.update(Event::SignIn));
    model.update(Event::SignInBrowser {
        ticket,
        opened: true,
    });
    cases.push(("signing-in-waiting", plain(model)));

    let mut model = signed_out();
    let ticket = sign_in_ticket(model.update(Event::SignIn));
    model.update(Event::SignInBrowser {
        ticket,
        opened: false,
    });
    cases.push(("signed-out-no-browser", plain(model)));

    cases.push(("signing-out", signed_in().send(Event::SignOut)));
    cases
}

/// The welcome screen offers "Create an account" beside browser sign-in, and
/// only there: not while signing in. Whether both are held (another copy of
/// the app installed) is the sign-in page's, from `copies_view`.
#[test]
fn the_welcome_screen_offers_an_account_beside_sign_in() {
    use district_ffi::{ScreenView, screen_view};

    let ScreenView::Session { view } = screen_view(&signed_out()) else {
        panic!("the sign-in page");
    };
    assert!(view.sign_in);
    let offer = view.create_account.expect("offered beside sign-in");
    assert_eq!(offer.label, "Create an account");
    assert!(offer.note.contains("sign-in page"), "{}", offer.note);

    let (model, _) = start();
    let ScreenView::Session { view } = screen_view(&model) else {
        panic!("the sign-in page");
    };
    assert!(!view.sign_in);
    assert_eq!(view.create_account, None);
}
