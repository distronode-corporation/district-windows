//! Reporting AI-generated content (src/report.rs).

use district_ffi::ReportTarget;

use super::calls::on_call;
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
    ]
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
