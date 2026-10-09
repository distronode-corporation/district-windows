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

/// The button's words, as the service's own sign-in page puts them.
pub const CREATE_ACCOUNT_LABEL: &str = "Create an account";
/// The line under it: what the button does, since it cannot open the
/// register page itself.
pub const CREATE_ACCOUNT_NOTE: &str = "Your browser opens the District AI sign-in page, which \
    offers Create an account. When the account is made, you come back here signed in.";

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
        label: CREATE_ACCOUNT_LABEL.to_owned(),
        note: CREATE_ACCOUNT_NOTE.to_owned(),
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
