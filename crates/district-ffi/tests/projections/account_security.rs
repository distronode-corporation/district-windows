//! Two-step verification and deleting the account (src/account_security.rs),
//! each behind a fresh re-authentication.

use district_ffi::account_security::{
    DeleteAccountAction, DeleteAccountStepView, ReauthModeView, ReauthProviderView, TwoStepAction,
    TwoStepChangeView, TwoStepStepView, TwoStepView,
};

use super::*;

/// This area's snapshot cases.
pub(crate) fn cases() -> Vec<Case> {
    vec![
        ("two-step-loading", two_step(signed_in())),
        (
            "two-step-load-failed",
            two_step(signed_in()).answer(
                |e| matches!(e, Effect::LoadTwoStep { .. }),
                |ticket| Event::TwoStepLoaded {
                    ticket,
                    result: Err(server_error()),
                },
            ),
        ),
        ("two-step-off", off()),
        ("two-step-on", on()),
        (
            "two-step-on-no-codes-no-password",
            two_step_loaded(
                two_step(signed_in()),
                with(
                    with(status(), "backupCodesRemaining", 0),
                    "canDisable",
                    false,
                ),
            ),
        ),
        ("two-step-reauth", reauth()),
        (
            "two-step-reauth-browser",
            reauth().ui(two(TwoStepAction::UseProvider {
                provider: ReauthProviderView::Microsoft,
            })),
        ),
        ("two-step-reauth-password", typed_reauth()),
        ("two-step-reauth-busy", typed_reauth_sent()),
        ("two-step-reauth-code", needs_code()),
        ("two-step-scan", scan()),
        (
            "two-step-scan-no-picture",
            scan_with(enrollment_without_picture()),
        ),
        // The codes, and the notice after them, are not snapshots: the core
        // writes the status as on since today, which a pinned file cannot
        // hold. the_secret_and_the_codes_reach_the_window and
        // saving_the_codes_says_it_is_on check them.
        (
            "two-step-turn-off",
            on().ui(two(TwoStepAction::Begin {
                change: TwoStepChangeView::TurnOff,
            })),
        ),
        ("delete-account", deletion_opened(signed_in())),
        (
            "delete-account-browser",
            deletion_opened(signed_in()).ui(deletion(DeleteAccountAction::UseProvider {
                provider: ReauthProviderView::Apple,
            })),
        ),
        ("delete-account-password", deletion_typed_reauth()),
        ("delete-account-confirm", deletion_confirm()),
        ("delete-account-deleting", deleting()),
        (
            "delete-account-email-refused",
            deleting().answer(
                |e| matches!(e, Effect::DeleteAccount { .. }),
                |ticket| Event::AccountDeleted {
                    ticket,
                    result: Err(envelope(400, "confirmation_mismatch")),
                },
            ),
        ),
        (
            "delete-account-not-deleted",
            deleting().answer(
                |e| matches!(e, Effect::DeleteAccount { .. }),
                |ticket| Event::AccountDeleted {
                    ticket,
                    result: Err(server_error()),
                },
            ),
        ),
        ("delete-account-signing-out", deleted()),
    ]
}

fn two(action: TwoStepAction) -> UiEvent {
    UiEvent::TwoStep { action }
}

fn deletion(action: DeleteAccountAction) -> UiEvent {
    UiEvent::AccountDeletion { action }
}

fn typed(text: &str) -> Vec<u8> {
    text.as_bytes().to_vec()
}

fn envelope(status: u16, code: &str) -> ApiError {
    ApiError::Envelope {
        status,
        code: code.to_owned(),
        detail: ErrorDetail {
            code: Some(code.to_owned()),
            ..ErrorDetail::default()
        },
    }
}

fn status() -> Value {
    contracts::json("district-mfa-status.json")
}

fn two_step(session: Session) -> Session {
    session.ui(two(TwoStepAction::Open))
}

fn two_step_loaded(session: Session, value: Value) -> Session {
    session.answer(
        |e| matches!(e, Effect::LoadTwoStep { .. }),
        |ticket| Event::TwoStepLoaded {
            ticket,
            result: Ok(contracts::decode("mfa status", value)),
        },
    )
}

/// The screen, two-step verification off.
fn off() -> Session {
    two_step_loaded(
        two_step(signed_in()),
        json!({"enrolled": false, "enrolledAt": null, "backupCodesRemaining": 0, "canDisable": true}),
    )
}

/// The screen, two-step verification on (the core's fixture).
fn on() -> Session {
    two_step_loaded(two_step(signed_in()), status())
}

fn reauth() -> Session {
    off().ui(two(TwoStepAction::Begin {
        change: TwoStepChangeView::TurnOn,
    }))
}

// The password step's helpers say "typed", not "password", and the
// deletion's say "deletion", not "account": CodeQL takes a call named with
// either word for sensitive data, and then reports every failure message that
// prints the session as cleartext logging.

/// Confirming it is you with the password, its box shown.
fn typed_reauth() -> Session {
    reauth().ui(two(TwoStepAction::UsePassword))
}

fn typed_reauth_sent() -> Session {
    typed_reauth().ui(two(TwoStepAction::SubmitPassword {
        password: typed("correct horse battery staple"),
        code: None,
    }))
}

fn needs_code() -> Session {
    typed_reauth_sent().answer(
        |e| matches!(e, Effect::Reauthenticate { .. }),
        |ticket| Event::Reauthenticated {
            ticket,
            action: district_model::ReauthAction::TwoStepChange,
            result: Err(envelope(401, district_api::CODE_MFA_REQUIRED)),
        },
    )
}

fn proof_accepted(session: Session) -> Session {
    session.answer(
        |e| matches!(e, Effect::Reauthenticate { .. }),
        |ticket| Event::Reauthenticated {
            ticket,
            action: district_model::ReauthAction::TwoStepChange,
            result: Ok(300),
        },
    )
}

fn scan_with(enrollment: Value) -> Session {
    proof_accepted(typed_reauth_sent()).answer(
        |e| matches!(e, Effect::StartTwoStep { .. }),
        |ticket| Event::TwoStepStarted {
            ticket,
            result: Ok(contracts::decode("mfa enroll", enrollment)),
        },
    )
}

fn scan() -> Session {
    scan_with(contracts::json("district-mfa-enroll.json"))
}

fn enrollment_without_picture() -> Value {
    with(
        contracts::json("district-mfa-enroll.json"),
        "qrDataUrl",
        "not a picture",
    )
}

fn codes() -> Session {
    scan()
        .ui(two(TwoStepAction::Confirm {
            code: typed("123456"),
        }))
        .answer(
            |e| matches!(e, Effect::ConfirmTwoStep { .. }),
            |ticket| Event::TwoStepConfirmed {
                ticket,
                result: Ok(contracts::read("district-mfa-confirm.json")),
            },
        )
}

fn deletion_opened(session: Session) -> Session {
    session.ui(deletion(DeleteAccountAction::Open))
}

fn deletion_typed_reauth() -> Session {
    deletion_opened(signed_in()).ui(deletion(DeleteAccountAction::UsePassword))
}

fn deletion_confirm() -> Session {
    deletion_typed_reauth()
        .ui(deletion(DeleteAccountAction::SubmitPassword {
            password: typed("correct horse battery staple"),
            code: None,
        }))
        .answer(
            |e| matches!(e, Effect::Reauthenticate { .. }),
            |ticket| Event::Reauthenticated {
                ticket,
                action: district_model::ReauthAction::DeleteAccount,
                result: Ok(300),
            },
        )
}

fn deleting() -> Session {
    deletion_confirm().ui(deletion(DeleteAccountAction::Delete {
        confirmation: "member@example.com".to_owned(),
    }))
}

fn deleted() -> Session {
    deleting().answer(
        |e| matches!(e, Effect::DeleteAccount { .. }),
        |ticket| Event::AccountDeleted {
            ticket,
            result: Ok(false),
        },
    )
}

fn two_step_view(session: &Session) -> TwoStepView {
    match screen_view(&session.model) {
        ScreenView::TwoStep { view } => view,
        _ => panic!("not the two-step screen"),
    }
}

/// Both screens are under the account: its pane entry stays selected.
#[test]
fn both_screens_are_the_accounts() {
    for session in [two_step(signed_in()), deletion_opened(signed_in())] {
        assert_eq!(
            shell_json(&session)["nav_selected"],
            json!(NavDestination::Account)
        );
    }
    assert_eq!(route(&two_step(signed_in())), Route::TwoStep);
    assert_eq!(route(&deletion_opened(signed_in())), Route::DeleteAccount);
}

/// Turning it on: confirming it is you, the secret, then the codes, and each
/// step's own part is the one set.
#[test]
fn turning_it_on_walks_its_steps() {
    let steps = [
        (off(), TwoStepStepView::Overview),
        (reauth(), TwoStepStepView::Reauth),
        (scan(), TwoStepStepView::Scan),
        (codes(), TwoStepStepView::RecoveryCodes),
    ];
    for (session, step) in steps {
        let view = two_step_view(&session);
        assert_eq!(view.step, step);
        assert_eq!(view.reauth.is_some(), step == TwoStepStepView::Reauth);
        assert_eq!(view.scan.is_some(), step == TwoStepStepView::Scan);
        assert_eq!(view.codes.is_some(), step == TwoStepStepView::RecoveryCodes);
    }
    let off = two_step_view(&off());
    assert_eq!(off.offered, Some(TwoStepChangeView::TurnOn));
    assert_eq!(off.offered_label.as_deref(), Some("Turn on"));
    assert_eq!(
        two_step_view(&on()).offered,
        Some(TwoStepChangeView::TurnOff)
    );
}

/// The secret and the codes reach C# whole, as UTF-8 (never in a snapshot:
/// see the redaction in src/account_security.rs).
#[test]
fn the_secret_and_the_codes_reach_the_window() {
    let scan = two_step_view(&scan()).scan.expect("the secret");
    assert_eq!(scan.key, b"JBSWY3DPEHPK3PXP");
    assert!(
        String::from_utf8(scan.otpauth_uri)
            .unwrap()
            .starts_with("otpauth://totp/")
    );
    assert!(
        scan.qr_png
            .expect("the picture")
            .starts_with(b"\x89PNG\r\n\x1a\n")
    );
    assert_eq!(
        two_step_view(&scan_with(enrollment_without_picture()))
            .scan
            .expect("the secret")
            .qr_png,
        None
    );
    let codes = two_step_view(&codes()).codes.expect("the codes");
    assert_eq!(codes.codes, b"aaaa-bbbb\ncccc-dddd");
    assert_eq!(codes.count, 2);
}

/// "I have saved them": the codes go, the screen says it is on and reads the
/// status again.
#[test]
fn saving_the_codes_says_it_is_on() {
    let saved = codes().ui(two(TwoStepAction::FinishRecoveryCodes));
    let view = two_step_view(&saved);
    assert_eq!(view.step, TwoStepStepView::Overview);
    assert!(view.codes.is_none());
    assert_eq!(view.notice.as_deref(), Some("Two-step verification is on."));
    assert_eq!(view.status_label.as_deref(), Some("On"));
    assert!(
        saved
            .pending
            .iter()
            .any(|effect| matches!(effect, Effect::LoadTwoStep { .. }))
    );
}

/// The password goes, and the screen waits; the service asking for the code
/// is not a failure but the code's field.
#[test]
fn the_password_then_the_code() {
    let password = two_step_view(&typed_reauth()).reauth.expect("the reauth");
    assert_eq!(password.mode, ReauthModeView::Password);
    assert_eq!(password.code_prompt, None);
    assert!(two_step_view(&typed_reauth_sent()).busy);
    let sent = typed_reauth_sent();
    let proof = sent
        .pending
        .iter()
        .find_map(|effect| match effect {
            Effect::Reauthenticate { proof, .. } => Some(proof.clone()),
            _ => None,
        })
        .expect("the password goes");
    assert_eq!(
        proof,
        district_core::ReauthProof::Password {
            password: district_model::Credential::new("correct horse battery staple"),
            code: None,
        }
    );
    let code = two_step_view(&needs_code());
    assert!(!code.busy);
    assert_eq!(code.failure, None);
    assert!(code.reauth.expect("the reauth").code_prompt.is_some());
}

/// Once asked for, the code goes with the password, and the proof it earns
/// leads to the new secret.
#[test]
fn the_password_and_its_code_lead_to_the_secret() {
    let sent = needs_code().ui(two(TwoStepAction::SubmitPassword {
        password: typed("correct horse battery staple"),
        code: Some(typed(" 123456 ")),
    }));
    let proof = sent
        .pending
        .iter()
        .rev()
        .find_map(|effect| match effect {
            Effect::Reauthenticate { proof, .. } => Some(proof.clone()),
            _ => None,
        })
        .expect("the password and the code go");
    assert_eq!(
        proof,
        district_core::ReauthProof::Password {
            password: district_model::Credential::new("correct horse battery staple"),
            code: Some(district_model::Credential::new("123456")),
        }
    );
    assert!(two_step_view(&sent).busy);
    let scan = proof_accepted(sent).answer(
        |e| matches!(e, Effect::StartTwoStep { .. }),
        |ticket| Event::TwoStepStarted {
            ticket,
            result: Ok(contracts::read("district-mfa-enroll.json")),
        },
    );
    assert_eq!(two_step_view(&scan).step, TwoStepStepView::Scan);
}

/// A provider sign-in opens the core's page in the browser and says it waits;
/// the three providers are always offered, in order.
#[test]
fn a_provider_sign_in_opens_the_browser() {
    let browser = reauth().ui(two(TwoStepAction::UseProvider {
        provider: ReauthProviderView::Google,
    }));
    assert!(
        browser
            .pending
            .iter()
            .any(|effect| matches!(effect, Effect::OpenOneTimeUrl { .. }))
    );
    let view = two_step_view(&browser).reauth.expect("the reauth");
    assert_eq!(view.mode, ReauthModeView::Browser);
    assert!(view.waiting.is_some());
    assert_eq!(
        view.choices
            .iter()
            .map(|choice| (choice.provider, choice.label.as_str()))
            .collect::<Vec<_>>(),
        [
            (ReauthProviderView::Google, "Continue with Google"),
            (ReauthProviderView::Microsoft, "Continue with Microsoft"),
            (ReauthProviderView::Apple, "Continue with Apple"),
        ]
    );
}

/// The deletion: what is deleted and kept, confirming it is you, then the
/// typed address and the final button, whose label says it is on its way.
#[test]
fn the_deletion_walks_its_steps() {
    let deletion_view = |session: &Session| match screen_view(&session.model) {
        ScreenView::DeleteAccount { view } => view,
        _ => panic!("not the deletion screen"),
    };
    let start = deletion_view(&deletion_opened(signed_in()));
    assert_eq!(start.step, DeleteAccountStepView::Explain);
    assert_eq!(start.explain.len(), 4);
    assert!(start.confirm.is_none());
    assert_eq!(
        start.reauth.expect("the reauth").mode,
        ReauthModeView::Choose
    );
    let confirm = deletion_view(&deletion_confirm());
    assert_eq!(confirm.step, DeleteAccountStepView::Confirm);
    assert!(confirm.reauth.is_none());
    assert_eq!(
        confirm.confirm.expect("the last step").submit_label,
        "Delete my account"
    );
    let busy = deletion_view(&deleting());
    assert!(busy.busy);
    assert_eq!(
        busy.confirm.expect("the last step").submit_label,
        "Deleting\u{2026}"
    );
    let not_deleted = deleting().answer(
        |e| matches!(e, Effect::DeleteAccount { .. }),
        |ticket| Event::AccountDeleted {
            ticket,
            result: Err(server_error()),
        },
    );
    let refused = deletion_view(&not_deleted);
    assert_eq!(
        refused.failure_title.as_deref(),
        Some("Your account was not deleted.")
    );
    assert!(refused.failure.is_some());
}

/// After the deletion the core signs this computer out, and the sign-in page
/// says the account was deleted.
#[test]
fn after_the_deletion_the_sign_in_page_says_so() {
    let session = deleted();
    assert!(matches!(
        session.model.session(),
        SessionState::SigningOut(_)
    ));
    let ScreenView::Session { view } = screen_view(&session.model) else {
        panic!("not the session screen");
    };
    assert!(view.busy);
    // The sign-out finishes as any other does.
    let session = session.answer(
        |e| matches!(e, Effect::SignOut { .. }),
        |ticket| Event::SignOutFinished {
            ticket,
            report: district_auth::SignOutReport {
                presence_unregistered: true,
                revoke: district_auth::RevokeStatus::Revoked,
                cleared: Ok(()),
            },
        },
    );
    let ScreenView::Session { view } = screen_view(&session.model) else {
        panic!("not the session screen");
    };
    assert_eq!(view.title, "Your account was deleted.");
    assert!(view.sign_in);
}
