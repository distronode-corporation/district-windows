//! Reporting AI-generated content (src/report.rs).

use district_core::SupportForm;
use district_ffi::support::{SupportAction, SupportKind};
use district_ffi::{DRAFT_OPEN, ReportStatus, ReportTarget};

use super::calls::on_call;
use super::support::{composing, typed};
use super::*;

/// This area's snapshot cases.
pub(crate) fn cases() -> Vec<Case> {
    report_cases()
}

pub(crate) fn reported(session: Session) -> Session {
    session.ui(UiEvent::Report {
        target: ReportTarget::Call {
            call_id: "call_contract_answered".to_owned(),
        },
        note: "The summary names the wrong day.".to_owned(),
    })
}

pub(crate) fn report_answer(session: Session, result: Result<Value, ApiError>) -> Session {
    session.answer(
        |e| matches!(e, Effect::CreateSupportRequest { .. }),
        |ticket| Event::SupportRequestCreated {
            ticket,
            result: result.map(|value| contracts::decode("support", value)),
        },
    )
}

pub(crate) fn report_cases() -> Vec<Case> {
    let created = || Ok(contracts::json("district-support-request-create.json"));
    vec![
        ("report-sending", reported(on_call(signed_in()))),
        (
            "report-sent",
            report_answer(reported(on_call(signed_in())), created()),
        ),
        (
            "report-failed",
            report_answer(reported(on_call(signed_in())), Err(server_error())),
        ),
        (
            "report-dismissed",
            report_answer(reported(on_call(signed_in())), created()).ui(UiEvent::DismissReport),
        ),
        ("report-refused-for-a-viewer", reported(on_call(viewer()))),
        (
            "report-refused-support-draft",
            reported(on_call(drafting(signed_in()))),
        ),
    ]
}

/// A support request being written in Support, with something typed.
fn drafting(session: Session) -> Session {
    typed(
        composing(session),
        SupportKind::Problem,
        "Calls drop",
        "Calls drop after a minute.",
    )
}

/// The support form as the core holds it.
fn support_form(session: &Session) -> Option<SupportForm> {
    let SessionState::SignedIn(signed_in) = session.model.session() else {
        panic!("signed out");
    };
    signed_in
        .support
        .compose
        .as_ref()
        .map(|compose| compose.form.clone())
}

fn sends(session: &Session) -> usize {
    session
        .pending
        .iter()
        .filter(|effect| matches!(effect, Effect::CreateSupportRequest { .. }))
        .count()
}

/// While a member's support draft holds anything typed, a Report is refused
/// before it is sent: the frame says why, nothing is sent, and the draft is
/// exactly as typed. Putting the (absent) report away leaves it too.
#[test]
fn a_report_is_refused_while_a_support_draft_is_typed() {
    let drafting = on_call(drafting(signed_in()));
    let draft = support_form(&drafting);
    assert!(draft.is_some());
    let shell = shell_view(&drafting.model, drafting.reporting);
    assert_eq!(shell.report_refusal.as_deref(), Some(DRAFT_OPEN));

    let refused = reported(drafting);
    assert!(!refused.reporting);
    assert_eq!(sends(&refused), 0);
    assert_eq!(support_form(&refused), draft);
    let shell = shell_view(&refused.model, refused.reporting);
    assert_eq!(shell.report, None);
    assert_eq!(shell.report_refusal.as_deref(), Some(DRAFT_OPEN));

    let dismissed = refused.ui(UiEvent::DismissReport);
    assert_eq!(support_form(&dismissed), draft);
}

/// Spaces are not typed, and neither is the kind alone: a form open with
/// nothing in it is closed by a Report, which then goes as before.
#[test]
fn an_untyped_support_form_does_not_hold_a_report_back() {
    for session in [
        composing(signed_in()),
        typed(
            composing(signed_in()),
            SupportKind::Suggestion,
            "  ",
            " \n ",
        ),
    ] {
        let session = on_call(session);
        let shell = shell_view(&session.model, session.reporting);
        assert_eq!(shell.report_refusal, None);
        let sent = reported(session);
        assert_eq!(sends(&sent), 1);
        assert!(matches!(
            shell_view(&sent.model, sent.reporting).report,
            Some(ReportStatus::Sending)
        ));
    }
}

/// Once the draft is sent or discarded, Report works again.
#[test]
fn a_report_goes_once_the_draft_is_discarded() {
    let discarded = on_call(drafting(signed_in()).ui(UiEvent::Support {
        action: SupportAction::CancelRequest,
    }));
    assert_eq!(support_form(&discarded), None);
    assert_eq!(sends(&reported(discarded)), 1);
}

/// A report sent, then a support request started: the draft is the member's.
/// The report still reads as sent, never as on its way, and putting it away
/// does not close the draft.
#[test]
fn a_sent_report_leaves_a_later_draft_alone() {
    let sent = report_answer(
        reported(on_call(signed_in())),
        Ok(contracts::json("district-support-request-create.json")),
    );
    let drafting = typed(
        sent.ui(UiEvent::Support {
            action: SupportAction::StartRequest,
        }),
        SupportKind::Question,
        "Hours",
        "",
    );
    assert!(matches!(
        shell_view(&drafting.model, drafting.reporting).report,
        Some(ReportStatus::Sent { .. })
    ));
    let draft = support_form(&drafting);
    let dismissed = drafting.ui(UiEvent::DismissReport);
    assert_eq!(support_form(&dismissed), draft);
    assert_eq!(
        shell_view(&dismissed.model, dismissed.reporting).report,
        None
    );
}

/// A failed report's draft is the report's, not a member's: it does not hold
/// a Report back, and putting the report away still closes it.
#[test]
fn a_failed_reports_draft_is_put_away_with_it() {
    let failed = report_answer(reported(on_call(signed_in())), Err(server_error()));
    assert_eq!(
        shell_view(&failed.model, failed.reporting).report_refusal,
        None
    );
    let dismissed = failed.ui(UiEvent::DismissReport);
    assert_eq!(support_form(&dismissed), None);
}

/// The three events of a report are taken on the call's own screen, and
/// raising the request leaves the member there.
#[test]
fn a_report_stays_on_the_screen_it_was_made_from() {
    let sent = report_answer(
        reported(on_call(signed_in())),
        Ok(contracts::json("district-support-request-create.json")),
    );
    assert!(matches!(
        screen_view(&sent.model),
        ScreenView::CallDetail { .. }
    ));
    assert!(matches!(
        shell_view(&sent.model, true).report,
        Some(district_ffi::ReportStatus::Sent { .. })
    ));
}

/// The idempotency key of the report the core is sending.
pub(crate) fn report_key(session: &Session) -> String {
    session
        .pending
        .iter()
        .rev()
        .find_map(|effect| match effect {
            Effect::CreateSupportRequest {
                idempotency_key, ..
            } => Some(idempotency_key.clone()),
            _ => None,
        })
        .expect("a report on its way")
}

/// A report that failed leaves its draft, and its key, open. A report on
/// another target drops that draft first, so it goes under a new key and the
/// service cannot take it for a repeat of the first.
#[test]
fn a_report_after_a_failed_one_gets_a_new_key() {
    let first = reported(on_call(signed_in()));
    let first_key = report_key(&first);
    let failed = report_answer(first, Err(server_error()));
    assert!(matches!(
        shell_view(&failed.model, true).report,
        Some(district_ffi::ReportStatus::Failed { .. })
    ));
    let second = failed.ui(UiEvent::Report {
        target: ReportTarget::Contact {
            contact_id: "contact_contract_1".to_owned(),
        },
        note: String::new(),
    });
    let second_key = report_key(&second);
    assert_ne!(first_key, second_key);
    assert!(matches!(
        shell_view(&second.model, true).report,
        Some(district_ffi::ReportStatus::Sending)
    ));
}

/// While a report is on its way, a second press is refused whole: nothing
/// else is sent, and the first draft is not dropped.
#[test]
fn a_second_report_while_sending_is_ignored() {
    let sending = reported(on_call(signed_in()));
    let sent_before = sending.pending.len();
    let again = reported(sending);
    assert_eq!(again.pending.len(), sent_before);
    assert!(matches!(
        shell_view(&again.model, true).report,
        Some(district_ffi::ReportStatus::Sending)
    ));
}
