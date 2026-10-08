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
