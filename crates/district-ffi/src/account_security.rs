//! The account's own security, below the account screen: two-step
//! verification (whether it is on, since when, how many recovery codes are
//! left, and turning it on or off) and deleting the account, each after a
//! fresh re-authentication. What District AI for Linux shows, through
//! district-core's `TwoStepScreen` and `DeleteAccountScreen`; every sentence
//! is the core's.
//!
//! Confirming it is you is a fresh sign-in at Google, Microsoft or Apple in
//! the browser (the core opens the page; its answer comes back through the
//! app's own `districtai://auth` link), or the password with, once the service
//! asks for it, the authenticator or recovery code.
//!
//! What the person types (the password, a code) crosses from C# as UTF-8
//! bytes, as the CalDAV form's app password does: a generated C# record's
//! `ToString` prints a byte array as its type's name, never its contents. The
//! core gets each wrapped in a `Credential` and sends it once. The new
//! authenticator key, its `otpauth:` link and the recovery codes cross to C#
//! as bytes for the same reason. None of them reaches a `Debug` output or a
//! snapshot.

use district_core::{
    DeleteAccountEvent, DeleteAccountScreen, DeleteAccountStep, Event, ReauthProvider, ReauthStep,
    Route, SignedIn, TwoStepChange, TwoStepEvent, TwoStepScreen, TwoStepStep,
};
use district_model::Credential;
use serde::{Serialize, Serializer};

use crate::l10n::{cs, l};
use crate::screen::ScreenView;

/// Writes a secret into a snapshot as `<redacted>`: the JSON of a screen is
/// pinned in this public repository, and a secret has no place in it.
fn redacted<S: Serializer, T>(_: &T, serializer: S) -> Result<S::Ok, S::Error> {
    serializer.serialize_str("<redacted>")
}

/// Writes an optional secret as `null` when there is none and `<redacted>`
/// when there is one, so a snapshot still tells the two states apart.
fn redacted_option<S: Serializer, T>(value: &Option<T>, serializer: S) -> Result<S::Ok, S::Error> {
    match value {
        Some(_) => serializer.serialize_str("<redacted>"),
        None => serializer.serialize_none(),
    }
}

/// The UTF-8 of `text`.
fn bytes(text: &str) -> Vec<u8> {
    text.as_bytes().to_vec()
}

/// What C# sent as a typed secret, as a credential. C# encodes a string, so
/// the bytes are UTF-8; anything else is kept as near as it can be, and the
/// service refuses it.
fn credential(typed: &[u8]) -> Credential {
    Credential::new(String::from_utf8_lossy(typed))
}

// ── Confirming it is you ───────────────────────────────────────────────────

/// A provider to confirm it is you at.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash, Serialize, uniffi::Enum)]
pub enum ReauthProviderView {
    /// Google.
    Google,
    /// Microsoft.
    Microsoft,
    /// Apple.
    Apple,
}

impl From<ReauthProvider> for ReauthProviderView {
    fn from(provider: ReauthProvider) -> Self {
        match provider {
            ReauthProvider::Google => Self::Google,
            ReauthProvider::Microsoft => Self::Microsoft,
            ReauthProvider::Apple => Self::Apple,
        }
    }
}

impl From<ReauthProviderView> for ReauthProvider {
    fn from(provider: ReauthProviderView) -> Self {
        match provider {
            ReauthProviderView::Google => Self::Google,
            ReauthProviderView::Microsoft => Self::Microsoft,
            ReauthProviderView::Apple => Self::Apple,
        }
    }
}

/// One way to confirm it is you at a provider: its button.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct ReauthChoiceView {
    /// Which provider.
    pub provider: ReauthProviderView,
    /// "Continue with Google".
    pub label: String,
}

/// How far confirming it is you has got.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash, Serialize, uniffi::Enum)]
pub enum ReauthModeView {
    /// Choosing a provider or the password.
    Choose,
    /// The browser is open at a provider's sign-in; the providers are still
    /// offered, and choosing one again starts a new attempt.
    Browser,
    /// The password, and the code when [`ReauthView::code_prompt`] is set.
    Password,
}

/// Confirming it is you, on either screen.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct ReauthView {
    /// "Confirm it is you".
    pub heading: String,
    /// What happens once it is confirmed, over the ways to confirm.
    pub intro: String,
    /// Where it is.
    pub mode: ReauthModeView,
    /// "Waiting for sign-in to finish in your browser.", while the browser is
    /// open.
    pub waiting: Option<String>,
    /// The providers, in the order the core offers them. Shown while
    /// choosing and while the browser is open.
    pub choices: Vec<ReauthChoiceView>,
    /// "Use my password".
    pub use_password_label: String,
    /// "Your password".
    pub password_label: String,
    /// Once the service has asked for it, why the code goes with the
    /// password; `None` while the password goes alone.
    pub code_prompt: Option<String>,
    /// "Authenticator code or recovery code".
    pub code_label: String,
    /// "Continue", which sends the password.
    pub continue_label: String,
    /// "Back".
    pub back_label: String,
}

fn reauth_view(step: ReauthStep, intro: &str) -> ReauthView {
    let (mode, needs_code) = match step {
        ReauthStep::Choose => (ReauthModeView::Choose, false),
        ReauthStep::Browser(_) => (ReauthModeView::Browser, false),
        ReauthStep::Password { needs_code } => (ReauthModeView::Password, needs_code),
    };
    ReauthView {
        heading: cs::reauth_confirm_it_is_you(l()).to_owned(),
        intro: intro.to_owned(),
        mode,
        waiting: (mode == ReauthModeView::Browser)
            .then(|| cs::session_waiting_for_browser(l()).to_owned()),
        choices: ReauthProvider::ALL
            .into_iter()
            .map(|provider| ReauthChoiceView {
                provider: provider.into(),
                label: cs::reauth_continue_with(l(), provider.display()),
            })
            .collect(),
        use_password_label: cs::reauth_use_password(l()).to_owned(),
        password_label: cs::reauth_password_label(l()).to_owned(),
        code_prompt: needs_code.then(|| cs::reauth_code_prompt(l()).to_owned()),
        code_label: cs::reauth_code_label(l()).to_owned(),
        continue_label: cs::reauth_continue(l()).to_owned(),
        back_label: cs::reauth_back(l()).to_owned(),
    }
}

// ── Two-step verification ──────────────────────────────────────────────────

/// Which change the two-step screen offers or is making.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash, Serialize, uniffi::Enum)]
pub enum TwoStepChangeView {
    /// Turning it on.
    TurnOn,
    /// Turning it off.
    TurnOff,
}

impl From<TwoStepChange> for TwoStepChangeView {
    fn from(change: TwoStepChange) -> Self {
        match change {
            TwoStepChange::TurnOn => Self::TurnOn,
            TwoStepChange::TurnOff => Self::TurnOff,
        }
    }
}

impl From<TwoStepChangeView> for TwoStepChange {
    fn from(change: TwoStepChangeView) -> Self {
        match change {
            TwoStepChangeView::TurnOn => Self::TurnOn,
            TwoStepChangeView::TurnOff => Self::TurnOff,
        }
    }
}

/// Where the two-step screen is.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash, Serialize, uniffi::Enum)]
pub enum TwoStepStepView {
    /// The status is being read for the first time.
    Loading,
    /// The first read failed: [`TwoStepView::failure`] and Try again.
    LoadFailed,
    /// The status, and the change it offers.
    Overview,
    /// Confirming it is you before the change ([`TwoStepView::reauth`]).
    Reauth,
    /// The new secret and the field for its first code
    /// ([`TwoStepView::scan`]).
    Scan,
    /// It is on: the recovery codes, shown this once
    /// ([`TwoStepView::codes`]).
    RecoveryCodes,
}

/// The new authenticator secret, and the field for its first code. The key,
/// the link and the picture are the secret; its `Debug` output and its
/// snapshot name none of them.
#[derive(Clone, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct TwoStepScanView {
    /// "Add District AI to your authenticator app".
    pub title: String,
    /// "Scan this QR code with your authenticator app, or enter the key
    /// below by hand."
    pub intro: String,
    /// The QR code of the link, as PNG bytes, or `None` when the service's
    /// picture could not be read (say [`TwoStepScanView::qr_unreadable`]).
    #[serde(serialize_with = "redacted_option")]
    pub qr_png: Option<Vec<u8>>,
    /// The picture's name, for screen readers.
    pub qr_label: String,
    /// "The QR code could not be shown. Enter the key by hand instead."
    pub qr_unreadable: String,
    /// "Key".
    pub key_label: String,
    /// The base32 key, as UTF-8.
    #[serde(serialize_with = "redacted")]
    pub key: Vec<u8>,
    /// "Copy key".
    pub copy_key_label: String,
    /// "Copied", on a copy button once it has copied.
    pub copied_label: String,
    /// The `otpauth:` link an authenticator app opens, as UTF-8.
    #[serde(serialize_with = "redacted")]
    pub otpauth_uri: Vec<u8>,
    /// "Open in an authenticator app".
    pub open_label: String,
    /// What to do when no app opened the link.
    pub open_failed: String,
    /// "Then enter the 6-digit code your authenticator app shows for
    /// District AI."
    pub code_prompt: String,
    /// "6-digit code".
    pub code_label: String,
    /// "Turn on two-step verification", which sends the code.
    pub confirm_label: String,
    /// "Back".
    pub back_label: String,
}

impl std::fmt::Debug for TwoStepScanView {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.write_str("TwoStepScanView(<redacted>)")
    }
}

/// The recovery codes, shown once. Its `Debug` output and its snapshot give
/// their count only.
#[derive(Clone, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct TwoStepCodesView {
    /// "Save your recovery codes".
    pub title: String,
    /// That each signs in once, and they will not be shown again.
    pub warning: String,
    /// Every code, one per line (what "Copy all" copies), as UTF-8.
    #[serde(serialize_with = "redacted")]
    pub codes: Vec<u8>,
    /// How many there are.
    pub count: u32,
    /// "Copy all".
    pub copy_all_label: String,
    /// "Copied".
    pub copied_label: String,
    /// "I have saved them".
    pub done_label: String,
}

impl std::fmt::Debug for TwoStepCodesView {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        write!(f, "TwoStepCodesView(<{} redacted>)", self.count)
    }
}

/// The two-step verification screen.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct TwoStepView {
    /// The heading, "Two-step verification".
    pub title: String,
    /// Where it is; the step's own part is the field of the same name.
    pub step: TwoStepStepView,
    /// Whether a read or a change is on its way: every control is disabled.
    pub busy: bool,
    /// What went wrong last, as a sentence that says what to do.
    pub failure: Option<String>,
    /// What the last finished change did: "Two-step verification is on."
    pub notice: Option<String>,
    /// "On" or "Off", once the status is known.
    pub status_label: Option<String>,
    /// Since when it is on and how many recovery codes are left, or what it
    /// does while it is off.
    pub status_lines: Vec<String>,
    /// The change the overview offers, once the status is known.
    pub offered: Option<TwoStepChangeView>,
    /// Its button, "Turn on" or "Turn off".
    pub offered_label: Option<String>,
    /// "Try again", after a first read that failed.
    pub retry_label: String,
    /// Confirming it is you, on [`TwoStepStepView::Reauth`].
    pub reauth: Option<ReauthView>,
    /// The new secret, on [`TwoStepStepView::Scan`].
    pub scan: Option<TwoStepScanView>,
    /// The recovery codes, on [`TwoStepStepView::RecoveryCodes`].
    pub codes: Option<TwoStepCodesView>,
}

/// The two-step screen for `screen`.
pub(crate) fn two_step_view(screen: &TwoStepScreen) -> TwoStepView {
    let mut view = TwoStepView {
        title: cs::two_step_title(l()).to_owned(),
        step: TwoStepStepView::Loading,
        busy: screen.busy,
        failure: screen.failure.clone(),
        notice: screen.notice.map(|notice| notice.message().to_owned()),
        status_label: screen.status_label().map(str::to_owned),
        status_lines: screen.status_lines(),
        offered: screen.offered().map(TwoStepChangeView::from),
        offered_label: screen.offered().map(|change| {
            match change {
                TwoStepChange::TurnOn => cs::two_step_turn_on(l()),
                TwoStepChange::TurnOff => cs::two_step_turn_off(l()),
            }
            .to_owned()
        }),
        retry_label: cs::two_step_retry(l()).to_owned(),
        reauth: None,
        scan: None,
        codes: None,
    };
    match &screen.step {
        TwoStepStep::Loading => {}
        TwoStepStep::LoadFailed => view.step = TwoStepStepView::LoadFailed,
        TwoStepStep::Overview => view.step = TwoStepStepView::Overview,
        TwoStepStep::Reauth { change, step } => {
            view.step = TwoStepStepView::Reauth;
            view.reauth = Some(reauth_view(*step, TwoStepScreen::reauth_intro(*change)));
        }
        TwoStepStep::Scan(secret) => {
            view.step = TwoStepStepView::Scan;
            view.scan = Some(TwoStepScanView {
                title: cs::two_step_scan_title(l()).to_owned(),
                intro: cs::two_step_scan_intro(l()).to_owned(),
                qr_png: secret.qr_png.clone(),
                qr_label: cs::two_step_qr_label(l()).to_owned(),
                qr_unreadable: cs::two_step_qr_unreadable(l()).to_owned(),
                key_label: cs::two_step_key_label(l()).to_owned(),
                key: bytes(&secret.key),
                copy_key_label: cs::two_step_copy_key(l()).to_owned(),
                copied_label: cs::two_step_copied(l()).to_owned(),
                otpauth_uri: bytes(&secret.otpauth_uri),
                open_label: cs::two_step_open_authenticator(l()).to_owned(),
                open_failed: cs::two_step_open_failed(l()).to_owned(),
                code_prompt: cs::two_step_code_prompt(l()).to_owned(),
                code_label: cs::two_step_code_label(l()).to_owned(),
                confirm_label: cs::two_step_confirm_button(l()).to_owned(),
                back_label: cs::reauth_back(l()).to_owned(),
            });
        }
        TwoStepStep::RecoveryCodes(codes) => {
            view.step = TwoStepStepView::RecoveryCodes;
            view.codes = Some(TwoStepCodesView {
                title: cs::two_step_codes_title(l()).to_owned(),
                warning: cs::two_step_codes_warning(l()).to_owned(),
                codes: bytes(&codes.joined()),
                count: u32::try_from(codes.0.len()).unwrap_or(u32::MAX),
                copy_all_label: cs::two_step_copy_all(l()).to_owned(),
                copied_label: cs::two_step_copied(l()).to_owned(),
                done_label: cs::two_step_codes_done(l()).to_owned(),
            });
        }
    }
    view
}

/// Something done on the two-step screen. The password and the codes are
/// UTF-8 bytes (see the module documentation), left out of its `Debug`.
#[derive(Clone, PartialEq, Eq, uniffi::Enum)]
pub enum TwoStepAction {
    /// Open the screen, from the account screen.
    Open,
    /// Read the status again after a first read that failed.
    Retry,
    /// Start the change the overview offers.
    Begin {
        /// Which: [`TwoStepView::offered`].
        change: TwoStepChangeView,
    },
    /// Confirm it is you at `provider`, in the browser.
    UseProvider {
        /// Which.
        provider: ReauthProviderView,
    },
    /// Confirm it is you with the password instead.
    UsePassword,
    /// Send the password, and the code once the screen asks for it.
    SubmitPassword {
        /// The password, as UTF-8.
        password: Vec<u8>,
        /// The authenticator or recovery code, as UTF-8, when the screen
        /// asks for one ([`ReauthView::code_prompt`]).
        code: Option<Vec<u8>>,
    },
    /// Turn it on with the authenticator's first code.
    Confirm {
        /// The code, as UTF-8.
        code: Vec<u8>,
    },
    /// "I have saved them": the recovery codes go, and the status shows.
    FinishRecoveryCodes,
    /// One step back inside the screen.
    Back,
    /// Put away the notice and the failure.
    DismissNotices,
}

impl std::fmt::Debug for TwoStepAction {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        match self {
            Self::Open => f.write_str("Open"),
            Self::Retry => f.write_str("Retry"),
            Self::Begin { change } => f.debug_struct("Begin").field("change", change).finish(),
            Self::UseProvider { provider } => f
                .debug_struct("UseProvider")
                .field("provider", provider)
                .finish(),
            Self::UsePassword => f.write_str("UsePassword"),
            Self::SubmitPassword { .. } => f.write_str("SubmitPassword(<redacted>)"),
            Self::Confirm { .. } => f.write_str("Confirm(<redacted>)"),
            Self::FinishRecoveryCodes => f.write_str("FinishRecoveryCodes"),
            Self::Back => f.write_str("Back"),
            Self::DismissNotices => f.write_str("DismissNotices"),
        }
    }
}

/// The core events `action` is.
pub(crate) fn two_step_events(action: TwoStepAction) -> Vec<Event> {
    let event = match action {
        TwoStepAction::Open => return vec![Event::Navigate(Route::TwoStep)],
        TwoStepAction::Retry => TwoStepEvent::Retry,
        TwoStepAction::Begin { change } => TwoStepEvent::Begin(change.into()),
        TwoStepAction::UseProvider { provider } => TwoStepEvent::UseProvider(provider.into()),
        TwoStepAction::UsePassword => TwoStepEvent::UsePassword,
        TwoStepAction::SubmitPassword { password, code } => TwoStepEvent::SubmitPassword {
            password: credential(&password),
            code: code.as_deref().map(credential),
        },
        TwoStepAction::Confirm { code } => TwoStepEvent::Confirm(credential(&code)),
        TwoStepAction::FinishRecoveryCodes => TwoStepEvent::FinishRecoveryCodes,
        TwoStepAction::Back => TwoStepEvent::Back,
        TwoStepAction::DismissNotices => TwoStepEvent::DismissNotices,
    };
    vec![Event::TwoStep(event)]
}

// ── Deleting the account ───────────────────────────────────────────────────

/// Where the deletion screen is.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash, Serialize, uniffi::Enum)]
pub enum DeleteAccountStepView {
    /// What is deleted and kept, and confirming it is you
    /// ([`DeleteAccountView::reauth`]).
    Explain,
    /// The typed email address and the final button
    /// ([`DeleteAccountView::confirm`]).
    Confirm,
}

/// The last step: the email address, typed, and Delete my account.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct DeleteConfirmView {
    /// "Last step".
    pub title: String,
    /// "Type your account's email address to confirm. Nothing is deleted
    /// until you click Delete my account."
    pub type_email: String,
    /// "Your email address".
    pub email_label: String,
    /// "Delete my account", or "Deleting…" while it is on its way.
    pub submit_label: String,
}

/// The account deletion screen.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct DeleteAccountView {
    /// The heading, "Delete your account".
    pub title: String,
    /// What is deleted, what is cancelled, what is kept, and that it cannot
    /// be undone: one paragraph each.
    pub explain: Vec<String>,
    /// Where it is.
    pub step: DeleteAccountStepView,
    /// Whether a re-authentication or the deletion is on its way: every
    /// control is disabled.
    pub busy: bool,
    /// "Your account was not deleted.", over a refusal when that is certain.
    pub failure_title: Option<String>,
    /// What went wrong last, and what to do.
    pub failure: Option<String>,
    /// Confirming it is you, on [`DeleteAccountStepView::Explain`].
    pub reauth: Option<ReauthView>,
    /// The last step, on [`DeleteAccountStepView::Confirm`].
    pub confirm: Option<DeleteConfirmView>,
}

/// The deletion screen for `screen`.
pub(crate) fn delete_account_view(screen: &DeleteAccountScreen) -> DeleteAccountView {
    let (step, reauth, confirm) = match screen.step {
        DeleteAccountStep::Explain(step) => (
            DeleteAccountStepView::Explain,
            Some(reauth_view(step, cs::delete_account_confirm_intro(l()))),
            None,
        ),
        DeleteAccountStep::Confirm => (
            DeleteAccountStepView::Confirm,
            None,
            Some(DeleteConfirmView {
                title: cs::delete_account_last_step(l()).to_owned(),
                type_email: cs::delete_account_type_email(l()).to_owned(),
                email_label: cs::delete_account_email_label(l()).to_owned(),
                submit_label: if screen.busy {
                    cs::delete_account_deleting(l())
                } else {
                    cs::delete_account_submit(l())
                }
                .to_owned(),
            }),
        ),
    };
    DeleteAccountView {
        title: cs::delete_account_title(l()).to_owned(),
        explain: [
            cs::delete_account_what_is_deleted(l()),
            cs::delete_account_subscriptions_cancelled(l()),
            cs::delete_account_what_is_kept(l()),
            cs::delete_account_cannot_be_undone(l()),
        ]
        .map(str::to_owned)
        .to_vec(),
        step,
        busy: screen.busy,
        failure_title: screen
            .failure
            .as_ref()
            .and_then(|failure| failure.title)
            .map(str::to_owned),
        failure: screen
            .failure
            .as_ref()
            .map(|failure| failure.message.clone()),
        reauth,
        confirm,
    }
}

/// Something done on the deletion screen. The password and the code are
/// UTF-8 bytes (see the module documentation), left out of its `Debug`.
#[derive(Clone, PartialEq, Eq, uniffi::Enum)]
pub enum DeleteAccountAction {
    /// Open the screen, from the account screen's "Delete account".
    Open,
    /// Confirm it is you at `provider`, in the browser.
    UseProvider {
        /// Which.
        provider: ReauthProviderView,
    },
    /// Confirm it is you with the password instead.
    UsePassword,
    /// Send the password, and the code once the screen asks for it.
    SubmitPassword {
        /// The password, as UTF-8.
        password: Vec<u8>,
        /// The authenticator or recovery code, as UTF-8, when the screen
        /// asks for one.
        code: Option<Vec<u8>>,
    },
    /// Delete the account. `confirmation` is its email address, as typed.
    Delete {
        /// The email address.
        confirmation: String,
    },
    /// Back to the ways to confirm it is you.
    Back,
}

impl std::fmt::Debug for DeleteAccountAction {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        match self {
            Self::Open => f.write_str("Open"),
            Self::UseProvider { provider } => f
                .debug_struct("UseProvider")
                .field("provider", provider)
                .finish(),
            Self::UsePassword => f.write_str("UsePassword"),
            Self::SubmitPassword { .. } => f.write_str("SubmitPassword(<redacted>)"),
            Self::Delete { .. } => f.write_str("Delete(<redacted>)"),
            Self::Back => f.write_str("Back"),
        }
    }
}

/// The core events `action` is.
pub(crate) fn delete_account_events(action: DeleteAccountAction) -> Vec<Event> {
    let event = match action {
        DeleteAccountAction::Open => return vec![Event::Navigate(Route::DeleteAccount)],
        DeleteAccountAction::UseProvider { provider } => {
            DeleteAccountEvent::UseProvider(provider.into())
        }
        DeleteAccountAction::UsePassword => DeleteAccountEvent::UsePassword,
        DeleteAccountAction::SubmitPassword { password, code } => {
            DeleteAccountEvent::SubmitPassword {
                password: credential(&password),
                code: code.as_deref().map(credential),
            }
        }
        DeleteAccountAction::Delete { confirmation } => DeleteAccountEvent::Delete { confirmation },
        DeleteAccountAction::Back => DeleteAccountEvent::Back,
    };
    vec![Event::AccountDeletion(event)]
}

/// The two-step screen of `signed_in`.
pub(crate) fn two_step_screen(signed_in: &SignedIn) -> ScreenView {
    ScreenView::TwoStep {
        view: two_step_view(&signed_in.two_step),
    }
}

/// The deletion screen of `signed_in`.
pub(crate) fn delete_account_screen(signed_in: &SignedIn) -> ScreenView {
    ScreenView::DeleteAccount {
        view: delete_account_view(&signed_in.delete_account),
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn typed(text: &str) -> Vec<u8> {
        text.as_bytes().to_vec()
    }

    #[test]
    fn each_two_step_action_is_its_core_event() {
        assert_eq!(
            two_step_events(TwoStepAction::Open),
            [Event::Navigate(Route::TwoStep)]
        );
        for (action, event) in [
            (TwoStepAction::Retry, TwoStepEvent::Retry),
            (
                TwoStepAction::Begin {
                    change: TwoStepChangeView::TurnOn,
                },
                TwoStepEvent::Begin(TwoStepChange::TurnOn),
            ),
            (
                TwoStepAction::Begin {
                    change: TwoStepChangeView::TurnOff,
                },
                TwoStepEvent::Begin(TwoStepChange::TurnOff),
            ),
            (TwoStepAction::UsePassword, TwoStepEvent::UsePassword),
            (
                TwoStepAction::FinishRecoveryCodes,
                TwoStepEvent::FinishRecoveryCodes,
            ),
            (TwoStepAction::Back, TwoStepEvent::Back),
            (TwoStepAction::DismissNotices, TwoStepEvent::DismissNotices),
        ] {
            assert_eq!(two_step_events(action), [Event::TwoStep(event)]);
        }
        for provider in ReauthProvider::ALL {
            assert_eq!(
                two_step_events(TwoStepAction::UseProvider {
                    provider: provider.into(),
                }),
                [Event::TwoStep(TwoStepEvent::UseProvider(provider))]
            );
        }
    }

    #[test]
    fn typed_secrets_become_credentials() {
        assert_eq!(
            two_step_events(TwoStepAction::SubmitPassword {
                password: typed("correct horse"),
                code: Some(typed("123456")),
            }),
            [Event::TwoStep(TwoStepEvent::SubmitPassword {
                password: Credential::new("correct horse"),
                code: Some(Credential::new("123456")),
            })]
        );
        assert_eq!(
            two_step_events(TwoStepAction::SubmitPassword {
                password: typed("p"),
                code: None,
            }),
            [Event::TwoStep(TwoStepEvent::SubmitPassword {
                password: Credential::new("p"),
                code: None,
            })]
        );
        assert_eq!(
            two_step_events(TwoStepAction::Confirm {
                code: typed("654321"),
            }),
            [Event::TwoStep(TwoStepEvent::Confirm(Credential::new(
                "654321"
            )))]
        );
        assert_eq!(
            delete_account_events(DeleteAccountAction::SubmitPassword {
                password: typed("p\u{e4}ssword"),
                code: Some(typed("aaaa-bbbb")),
            }),
            [Event::AccountDeletion(DeleteAccountEvent::SubmitPassword {
                password: Credential::new("p\u{e4}ssword"),
                code: Some(Credential::new("aaaa-bbbb")),
            })]
        );
        // Bytes that are not UTF-8 are kept as near as they can be.
        assert_eq!(credential(&[0x61, 0xff]).expose(), "a\u{fffd}");
    }

    #[test]
    fn each_deletion_action_is_its_core_event() {
        assert_eq!(
            delete_account_events(DeleteAccountAction::Open),
            [Event::Navigate(Route::DeleteAccount)]
        );
        for (action, event) in [
            (
                DeleteAccountAction::UsePassword,
                DeleteAccountEvent::UsePassword,
            ),
            (
                DeleteAccountAction::Delete {
                    confirmation: "ada@example.com".to_owned(),
                },
                DeleteAccountEvent::Delete {
                    confirmation: "ada@example.com".to_owned(),
                },
            ),
            (DeleteAccountAction::Back, DeleteAccountEvent::Back),
        ] {
            assert_eq!(
                delete_account_events(action),
                [Event::AccountDeletion(event)]
            );
        }
        for provider in ReauthProvider::ALL {
            assert_eq!(
                delete_account_events(DeleteAccountAction::UseProvider {
                    provider: provider.into(),
                }),
                [Event::AccountDeletion(DeleteAccountEvent::UseProvider(
                    provider
                ))]
            );
        }
    }

    /// Nothing typed or shown once reaches a `Debug` output.
    #[test]
    fn no_secret_reaches_a_debug_output() {
        let actions = [
            format!(
                "{:?}",
                TwoStepAction::SubmitPassword {
                    password: typed("hunter2"),
                    code: Some(typed("314159")),
                }
            ),
            format!(
                "{:?}",
                TwoStepAction::Confirm {
                    code: typed("314159"),
                }
            ),
            format!(
                "{:?}",
                DeleteAccountAction::SubmitPassword {
                    password: typed("hunter2"),
                    code: None,
                }
            ),
            format!(
                "{:?}",
                DeleteAccountAction::Delete {
                    confirmation: "ada@example.com".to_owned(),
                }
            ),
        ];
        for printed in &actions {
            assert!(printed.contains("<redacted>"), "{printed}");
            for secret in ["hunter2", "314159", "ada@example.com", "104, 117"] {
                assert!(!printed.contains(secret), "{printed}");
            }
        }
        // The rest print as they are.
        let plain: Vec<String> = [
            TwoStepAction::Open,
            TwoStepAction::Retry,
            TwoStepAction::Begin {
                change: TwoStepChangeView::TurnOn,
            },
            TwoStepAction::UseProvider {
                provider: ReauthProviderView::Apple,
            },
            TwoStepAction::UsePassword,
            TwoStepAction::FinishRecoveryCodes,
            TwoStepAction::Back,
            TwoStepAction::DismissNotices,
        ]
        .iter()
        .map(|action| format!("{action:?}"))
        .chain(
            [
                DeleteAccountAction::Open,
                DeleteAccountAction::UseProvider {
                    provider: ReauthProviderView::Google,
                },
                DeleteAccountAction::UsePassword,
                DeleteAccountAction::Back,
            ]
            .iter()
            .map(|action| format!("{action:?}")),
        )
        .collect();
        assert_eq!(
            plain,
            [
                "Open",
                "Retry",
                "Begin { change: TurnOn }",
                "UseProvider { provider: Apple }",
                "UsePassword",
                "FinishRecoveryCodes",
                "Back",
                "DismissNotices",
                "Open",
                "UseProvider { provider: Google }",
                "UsePassword",
                "Back",
            ]
        );

        let scan = TwoStepScanView {
            title: String::new(),
            intro: String::new(),
            qr_png: Some(vec![1, 2, 3]),
            qr_label: String::new(),
            qr_unreadable: String::new(),
            key_label: String::new(),
            key: typed("JBSWY3DPEHPK3PXP"),
            copy_key_label: String::new(),
            copied_label: String::new(),
            otpauth_uri: typed("otpauth://totp/x?secret=JBSWY3DPEHPK3PXP"),
            open_label: String::new(),
            open_failed: String::new(),
            code_prompt: String::new(),
            code_label: String::new(),
            confirm_label: String::new(),
            back_label: String::new(),
        };
        assert_eq!(format!("{scan:?}"), "TwoStepScanView(<redacted>)");
        let without = serde_json::to_value(TwoStepScanView {
            qr_png: None,
            ..scan.clone()
        })
        .unwrap();
        assert_eq!(without["qr_png"], serde_json::Value::Null);
        let json = serde_json::to_string(&scan).unwrap();
        assert!(!json.contains("JBSWY3DPEHPK3PXP"), "{json}");
        assert!(json.contains(r#""key":"<redacted>""#), "{json}");
        let codes = TwoStepCodesView {
            title: String::new(),
            warning: String::new(),
            codes: typed("aaaa-bbbb\ncccc-dddd"),
            count: 2,
            copy_all_label: String::new(),
            copied_label: String::new(),
            done_label: String::new(),
        };
        assert_eq!(format!("{codes:?}"), "TwoStepCodesView(<2 redacted>)");
        let json = serde_json::to_string(&codes).unwrap();
        assert!(!json.contains("aaaa-bbbb"), "{json}");
    }

    #[test]
    fn providers_cross_both_ways() {
        for provider in ReauthProvider::ALL {
            assert_eq!(
                ReauthProvider::from(ReauthProviderView::from(provider)),
                provider
            );
        }
        for change in [TwoStepChange::TurnOn, TwoStepChange::TurnOff] {
            assert_eq!(TwoStepChange::from(TwoStepChangeView::from(change)), change);
        }
    }

    /// Every action crosses to C# and back intact, through UniFFI's own
    /// converters.
    #[test]
    fn every_action_survives_the_trip_to_csharp() {
        use uniffi::{Lift, Lower};
        for action in [
            crate::UiEvent::TwoStep {
                action: TwoStepAction::SubmitPassword {
                    password: typed("p"),
                    code: Some(typed("c")),
                },
            },
            crate::UiEvent::TwoStep {
                action: TwoStepAction::Confirm { code: typed("1") },
            },
            crate::UiEvent::AccountDeletion {
                action: DeleteAccountAction::Delete {
                    confirmation: "ada@example.com".to_owned(),
                },
            },
        ] {
            let buffer =
                <crate::UiEvent as Lower<crate::UniFfiTag>>::lower_into_rust_buffer(action.clone());
            let back =
                <crate::UiEvent as Lift<crate::UniFfiTag>>::try_lift_from_rust_buffer(buffer)
                    .unwrap();
            assert_eq!(back, action);
        }
    }
}
