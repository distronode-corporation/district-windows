//! The welcome screen's way to a new account (packet K4).
//!
//! Creating an account happens in the browser, on the same sign-in the app
//! already starts: the app's authorize request carries `platform=windows`
//! (district-auth), and for it the service's sign-in page offers "Create an
//! account" (Google, Microsoft, or an email address and a password), whose
//! register page returns to the app's own sign-in. The handshake keeps only
//! the code challenge, the state, the redirect address and the platform, so
//! there is no way to ask for the register page directly: the button starts
//! the ordinary browser sign-in, and says the page there offers the account.
//! The code exchange is the one every sign-in uses.
//!
//! A new account has no workspace yet. The core says so in its own words on
//! the overview; making the first one is the billing and checkout areas'.

use serde::Serialize;

use crate::l10n::{l, s};

/// The way to a new account, on a signed-out sign-in page.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct CreateAccountView {
    /// The button's words.
    pub label: String,
    /// What it does.
    pub note: String,
}

/// The way to a new account, offered wherever browser sign-in is.
pub(crate) fn create_account(sign_in: bool) -> Option<CreateAccountView> {
    sign_in.then(|| CreateAccountView {
        label: s::welcome_create_account_label(l()).to_owned(),
        note: s::welcome_create_account_note(l()).to_owned(),
    })
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn it_is_offered_only_beside_sign_in() {
        assert_eq!(create_account(false), None);
        let view = create_account(true).expect("offered");
        assert_eq!(view.label, "Create an account");
        assert!(view.note.contains("sign-in page"));
    }
}
