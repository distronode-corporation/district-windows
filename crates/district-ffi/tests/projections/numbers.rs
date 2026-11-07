//! Managing the numbers held and the carrier account (src/numbers.rs).
//!
//! No contract fixture records these ten routes yet (the core's 4.0.0-beta.2
//! says so), so the answers here are the core's typed answers, not JSON.

use std::collections::BTreeMap;

use district_core::{NumberAnswer, NumberCall};
use district_ffi::marketplace::{MarketplaceAction, NumbersTab};
use district_ffi::{
    A2pFormInput, NumberAdminAction, RegistrationFormInput, SipTrunkFormInput, TollFreeFormInput,
};
use district_model::{
    A2pRegistrationResponse, NumberLookupInfo, NumberLookupResponse, NumberReleaseResponse,
    ProviderStatusEntry, ProviderStatusResponse, SipTrunk, SipTrunkCreateResponse,
    SipTrunksResponse, TollFreeVerificationResponse, VerifyServiceResponse,
};

use super::marketplace::{action, held, view};
use super::*;

/// The workspace's own number in the fixture; the other two are Distronode's.
const OWN: &str = "+14165550100";
const MANAGED: &str = "+14165550199";

/// This area's snapshot cases.
pub(crate) fn cases() -> Vec<Case> {
    vec![
        ("numbers-release-asked", ask_release()),
        ("numbers-released", released(Ok(()))),
        ("numbers-release-unconfirmed", released(Err(server_error()))),
        ("numbers-lookup-asked", ask_lookup()),
        ("numbers-looked-up", looked_up()),
        ("numbers-carrier-loading", carrier_tab(held(signed_in()))),
        ("numbers-carrier", carrier_ready()),
        ("numbers-carrier-none", carrier_none()),
        ("numbers-a2p-asked", a2p_asked()),
        ("numbers-carrier-viewer", carrier_tab(held(viewer()))),
        ("numbers-registrations", registrations_ready()),
        (
            "numbers-registrations-viewer",
            registrations_tab(held(viewer())),
        ),
    ]
}

fn numbers(session: Session, action: NumberAdminAction) -> Session {
    session.ui(super::marketplace::action(MarketplaceAction::Numbers {
        action,
    }))
}

fn done(
    session: Session,
    pick: fn(&NumberCall) -> bool,
    answer: Result<NumberAnswer, ApiError>,
) -> Session {
    let index = session
        .pending
        .iter()
        .rposition(|e| matches!(e, Effect::NumberAdmin { call, .. } if pick(call)))
        .expect("no such number call pending");
    let mut session = session;
    let Effect::NumberAdmin { ticket, .. } = session.pending.remove(index) else {
        unreachable!()
    };
    session.send(Event::NumberAdminDone {
        ticket,
        result: answer,
    })
}

fn calls(session: &Session, pick: fn(&NumberCall) -> bool) -> usize {
    session
        .pending
        .iter()
        .filter(|e| matches!(e, Effect::NumberAdmin { call, .. } if pick(call)))
        .count()
}

fn ask_release() -> Session {
    numbers(
        held(signed_in()),
        NumberAdminAction::AskRelease {
            phone_number: OWN.to_owned(),
        },
    )
}

fn released(result: Result<(), ApiError>) -> Session {
    done(
        numbers(ask_release(), NumberAdminAction::Confirm),
        |call| matches!(call, NumberCall::Release(_)),
        result.map(|()| {
            NumberAnswer::Released(NumberReleaseResponse {
                success: true,
                warnings: Vec::new(),
            })
        }),
    )
}

fn ask_lookup() -> Session {
    let typed = numbers(
        held(signed_in()),
        NumberAdminAction::EditLookup {
            phone_number: "+14165550142".to_owned(),
        },
    );
    numbers(typed, NumberAdminAction::AskLookup)
}

fn looked_up() -> Session {
    done(
        numbers(ask_lookup(), NumberAdminAction::Confirm),
        |call| matches!(call, NumberCall::Lookup(_)),
        Ok(NumberAnswer::LookedUp(NumberLookupResponse {
            success: true,
            info: NumberLookupInfo {
                valid: true,
                phone_number: "+14165550142".to_owned(),
                country: Some("CA".to_owned()),
                line_type: Some("mobile".to_owned()),
                carrier: Some("Rogers".to_owned()),
            },
        })),
    )
}

fn carrier_tab(session: Session) -> Session {
    session.ui(action(MarketplaceAction::SelectTab {
        tab: NumbersTab::Carrier,
    }))
}

fn trunk() -> SipTrunk {
    SipTrunk {
        id: "trunk_1".to_owned(),
        name: "Office PBX".to_owned(),
        domain: "office".to_owned(),
        ip_access_control_list: vec!["203.0.113.10".to_owned()],
        domain_sid: Some("SD1".to_owned()),
        ip_acl_sid: Some("AL1".to_owned()),
        status: "active".to_owned(),
    }
}

fn carrier_ready() -> Session {
    let session = carrier_tab(held(signed_in()));
    let session = done(
        session,
        |call| matches!(call, NumberCall::CarrierStatus),
        Ok(NumberAnswer::CarrierStatus(ProviderStatusResponse {
            connected: true,
            providers: Some(BTreeMap::from([(
                "twilio".to_owned(),
                ProviderStatusEntry {
                    connected: true,
                    managed: Some(false),
                    account_name: Some("Acme".to_owned()),
                    balance: Some("12.50".to_owned()),
                    currency: Some("USD".to_owned()),
                    number_count: Some(1),
                    ..ProviderStatusEntry::default()
                },
            )])),
            provider: Some(Some("twilio".to_owned())),
        })),
    );
    let session = done(
        session,
        |call| matches!(call, NumberCall::SipTrunks),
        Ok(NumberAnswer::SipTrunks(SipTrunksResponse {
            success: true,
            trunks: vec![trunk()],
        })),
    );
    done(
        session,
        |call| matches!(call, NumberCall::VerifyService),
        Ok(NumberAnswer::VerifyService(VerifyServiceResponse {
            success: true,
            enabled: false,
            verify_service_sid: None,
        })),
    )
}

fn carrier_none() -> Session {
    done(
        carrier_tab(held(signed_in())),
        |call| matches!(call, NumberCall::CarrierStatus),
        Ok(NumberAnswer::CarrierStatus(ProviderStatusResponse {
            connected: false,
            providers: None,
            provider: None,
        })),
    )
}

fn a2p_form() -> A2pFormInput {
    A2pFormInput {
        business_name: "Acme Roofing".to_owned(),
        ein: String::new(),
        website: String::new(),
        vertical: String::new(),
        campaign_type: "CUSTOMER_CARE".to_owned(),
        campaign_description: "Appointment reminders.".to_owned(),
        sample_one: "Your roof inspection is tomorrow at 9.".to_owned(),
        sample_two: "Reply STOP to opt out.".to_owned(),
    }
}

fn a2p_asked() -> Session {
    let filled = numbers(
        carrier_ready(),
        NumberAdminAction::EditA2p { form: a2p_form() },
    );
    numbers(filled, NumberAdminAction::AskA2p)
}

/// The workspace's own number can be reapplied and released; a number
/// Distronode holds says why it cannot, and a viewer is told who can.
#[test]
fn only_the_workspaces_own_numbers_are_managed() {
    let owned = view(&held(signed_in())).owned;
    let own = owned
        .iter()
        .find_map(|row| row.manage.clone().filter(|m| m.phone_number == OWN))
        .unwrap();
    assert!(own.note.is_none() && own.can_configure && own.can_release);
    let managed = owned
        .iter()
        .find_map(|row| row.manage.clone().filter(|m| m.phone_number == MANAGED))
        .unwrap();
    assert!(managed.note.is_some() && !managed.can_configure && !managed.can_release);

    let seen = view(&held(viewer()));
    assert!(seen.owned.iter().all(|row| {
        row.manage.as_ref().is_none_or(|manage| {
            manage.note.is_some() && !manage.can_configure && !manage.can_release
        })
    }));
    let tried = numbers(
        held(viewer()),
        NumberAdminAction::Configure {
            phone_number: OWN.to_owned(),
        },
    );
    assert_eq!(calls(&tried, |_| true), 0);
}

/// Reapplying routing is not confirmed, and goes once until it lands.
#[test]
fn reapplying_routing_goes_once() {
    let configure = || NumberAdminAction::Configure {
        phone_number: OWN.to_owned(),
    };
    let session = numbers(held(signed_in()), configure());
    let session = numbers(session, configure());
    assert_eq!(
        calls(&session, |c| matches!(c, NumberCall::Configure(_))),
        1
    );
    let row = |session: &Session| {
        view(session)
            .owned
            .into_iter()
            .find_map(|row| row.manage.filter(|m| m.phone_number == OWN))
            .unwrap()
    };
    assert!(row(&session).busy && !row(&session).can_configure);
    let done = done(
        session,
        |c| matches!(c, NumberCall::Configure(_)),
        Ok(NumberAnswer::Configured),
    );
    assert!(row(&done).can_configure);
    assert!(view(&done).admin.number_notice.is_some());
}

/// Releasing asks first, drawn as destructive, and nothing is sent until
/// the member goes ahead; a failure that proves nothing is not offered again.
#[test]
fn a_release_is_confirmed_and_never_repeated_blind() {
    let asked = ask_release();
    assert_eq!(calls(&asked, |_| true), 0);
    let confirmation = view(&asked).admin.confirmation.unwrap();
    assert!(confirmation.destructive && confirmation.can_confirm);
    assert!(confirmation.body.contains(OWN));

    let cancelled = numbers(asked, NumberAdminAction::CancelConfirmation);
    assert_eq!(view(&cancelled).admin.confirmation, None);

    let sending = numbers(ask_release(), NumberAdminAction::Confirm);
    let running = view(&sending).admin.confirmation.unwrap();
    assert!(running.running && !running.can_confirm);
    assert_eq!(calls(&sending, |c| matches!(c, NumberCall::Release(_))), 1);

    let ok = released(Ok(()));
    assert_eq!(view(&ok).admin.confirmation, None);
    assert!(view(&ok).admin.number_notice.is_some());
    assert!(
        ok.pending
            .iter()
            .any(|e| matches!(e, Effect::LoadOwnedNumbers { .. }))
    );

    let unconfirmed = view(&released(Err(server_error())));
    assert!(unconfirmed.admin.number_unrepeatable.is_some());
    assert!(
        unconfirmed
            .owned
            .iter()
            .filter_map(|row| row.manage.as_ref())
            .all(|manage| !manage.can_release)
    );
}

/// The billed lookup asks first, and shows its answer as lines.
#[test]
fn a_lookup_is_confirmed_and_shows_its_answer() {
    let typed = numbers(
        held(signed_in()),
        NumberAdminAction::EditLookup {
            phone_number: "  ".to_owned(),
        },
    );
    assert!(!view(&typed).admin.lookup.can_ask);
    let asked = ask_lookup();
    assert_eq!(calls(&asked, |_| true), 0);
    assert!(!view(&asked).admin.confirmation.unwrap().destructive);
    let answer = view(&looked_up()).admin.lookup;
    assert_eq!(answer.result, ["+14165550142", "CA", "mobile", "Rogers"]);
    assert!(answer.can_ask);
}

/// The carrier tab reads the accounts, the trunks and the passcode sender,
/// and its writes go once.
#[test]
fn the_carrier_tab_reads_and_writes() {
    let loading = carrier_tab(held(signed_in()));
    assert_eq!(calls(&loading, |_| true), 3);
    let carrier = view(&carrier_ready()).admin.carrier.unwrap();
    assert_eq!(carrier.accounts.len(), 1);
    assert_eq!(carrier.accounts[0].carrier, "Twilio");
    assert_eq!(carrier.sip.trunks.len(), 1);
    assert!(carrier.sip.trunks[0].unprovisioned.is_none());
    assert_eq!(carrier.verify.enabled, Some(false));
    assert!(carrier.verify.can_toggle);
    assert!(!carrier.sip.can_create);

    let typed = numbers(
        carrier_ready(),
        NumberAdminAction::EditSipTrunk {
            form: SipTrunkFormInput {
                name: "Branch".to_owned(),
                domain: "branch".to_owned(),
                ip_addresses: "198.51.100.7\n".to_owned(),
            },
        },
    );
    assert!(view(&typed).admin.carrier.unwrap().sip.can_create);
    let created = numbers(
        numbers(typed, NumberAdminAction::CreateSipTrunk),
        NumberAdminAction::CreateSipTrunk,
    );
    assert_eq!(
        calls(&created, |c| matches!(c, NumberCall::CreateSipTrunk(_))),
        1
    );
    assert!(view(&created).admin.carrier.unwrap().working);
    let answered = done(
        created,
        |c| matches!(c, NumberCall::CreateSipTrunk(_)),
        Ok(NumberAnswer::SipTrunkCreated(SipTrunkCreateResponse {
            success: true,
            trunk: trunk(),
        })),
    );
    assert!(!view(&answered).admin.carrier.unwrap().working);

    let toggled = numbers(
        carrier_ready(),
        NumberAdminAction::SetVerify { enabled: true },
    );
    assert_eq!(
        calls(&toggled, |c| matches!(
            c,
            NumberCall::SetVerifyService(true)
        )),
        1
    );
    let on = done(
        toggled,
        |c| matches!(c, NumberCall::SetVerifyService(_)),
        Ok(NumberAnswer::VerifyService(VerifyServiceResponse {
            success: true,
            enabled: true,
            verify_service_sid: Some("VA1".to_owned()),
        })),
    );
    assert_eq!(view(&on).admin.carrier.unwrap().verify.enabled, Some(true));

    let none = view(&carrier_none()).admin.carrier.unwrap();
    assert!(none.accounts_empty.is_some());
}

/// Both registrations ask first, and are filed on the member's go.
#[test]
fn the_registrations_are_confirmed() {
    let asked = a2p_asked();
    assert_eq!(
        calls(&asked, |c| matches!(c, NumberCall::RegisterA2p(_))),
        0
    );
    assert!(view(&asked).admin.confirmation.is_some());
    let filed = done(
        numbers(asked, NumberAdminAction::Confirm),
        |c| matches!(c, NumberCall::RegisterA2p(_)),
        Ok(NumberAnswer::A2pRegistered(A2pRegistrationResponse {
            success: true,
            status: "pending".to_owned(),
            brand_sid: "BN1".to_owned(),
            messaging_service_sid: "MG1".to_owned(),
            campaign_sid: "CM1".to_owned(),
        })),
    );
    assert!(view(&filed).admin.carrier.unwrap().notice.is_some());

    let toll_free = numbers(
        carrier_ready(),
        NumberAdminAction::EditTollFree {
            form: TollFreeFormInput {
                phone_number: "+18005550188".to_owned(),
                business_name: "Acme Roofing".to_owned(),
                opt_in_image_url: "https://example.com/opt-in.png".to_owned(),
                message_content: String::new(),
                use_case: "Customer Support".to_owned(),
            },
        },
    );
    assert!(view(&toll_free).admin.carrier.unwrap().toll_free.can_submit);
    let asked = numbers(toll_free, NumberAdminAction::AskTollFree);
    assert!(view(&asked).admin.confirmation.is_some());
    let filed = done(
        numbers(asked, NumberAdminAction::Confirm),
        |c| matches!(c, NumberCall::VerifyTollFree(_)),
        Ok(NumberAnswer::TollFreeFiled(TollFreeVerificationResponse {
            success: true,
            status: "PENDING_REVIEW".to_owned(),
            tfv_sid: "HH1".to_owned(),
        })),
    );
    assert!(view(&filed).admin.carrier.unwrap().notice.is_some());
}

/// A carrier tab read that fails says so, each in its own place.
#[test]
fn the_carrier_tab_reads_fail_apart() {
    let session = carrier_tab(held(signed_in()));
    let session = done(
        session,
        |call| matches!(call, NumberCall::CarrierStatus),
        Err(server_error()),
    );
    let session = done(
        session,
        |call| matches!(call, NumberCall::SipTrunks),
        Err(server_error()),
    );
    let session = done(
        session,
        |call| matches!(call, NumberCall::VerifyService),
        Err(server_error()),
    );
    let carrier = view(&session).admin.carrier.unwrap();
    assert!(matches!(
        carrier.accounts_status,
        district_ffi::LoadStatus::Failed { .. }
    ));
    assert!(matches!(
        carrier.sip.status,
        district_ffi::LoadStatus::Failed { .. }
    ));
    assert!(matches!(
        carrier.verify.status,
        district_ffi::LoadStatus::Failed { .. }
    ));
    assert!(!carrier.verify.can_toggle);
}

/// A lookup that fails says why in the lookup, and one that may have been
/// billed is not offered again.
#[test]
fn a_failed_lookup_is_not_repeated_blind() {
    let failed = done(
        numbers(ask_lookup(), NumberAdminAction::Confirm),
        |call| matches!(call, NumberCall::Lookup(_)),
        Err(server_error()),
    );
    let lookup = view(&failed).admin.lookup;
    assert!(lookup.notice.is_some());
    assert!(lookup.unrepeatable.is_some());
    assert!(!lookup.can_ask);
}

fn registrations_tab(session: Session) -> Session {
    session.ui(action(MarketplaceAction::SelectTab {
        tab: NumbersTab::Registrations,
    }))
}

/// A filing as the service lists it.
fn filing(id: &str, status: &str, documents: serde_json::Value) -> serde_json::Value {
    serde_json::json!({
        "id": id,
        "isoCountry": "DE",
        "numberType": "local",
        "endUserType": "business",
        "friendlyName": "Berlin office",
        "status": status,
        "rejectionReasons": if status == "twilio-rejected" {
            serde_json::json!(["The proof of address is older than three months."])
        } else {
            serde_json::json!([])
        },
        "createdAt": "2026-11-07T09:00:00Z",
        "documents": documents,
    })
}

fn filings(registrations: serde_json::Value) -> Result<NumberAnswer, ApiError> {
    Ok(NumberAnswer::Registrations(
        serde_json::from_value(serde_json::json!({
            "success": true,
            "registrations": registrations,
            "approvedCountries": ["DE"],
            "platformCountries": ["US", "CA"],
        }))
        .expect("the service's shape"),
    ))
}

fn german_requirements() -> Result<NumberAnswer, ApiError> {
    Ok(NumberAnswer::Requirements(
        serde_json::from_value(serde_json::json!({
            "success": true,
            "country": "DE",
            "numberType": "local",
            "purchasable": true,
            "requirements": {
                "isoCountry": "DE",
                "numberType": "local",
                "endUserType": "business",
                "regulationSid": "RN-contract-de",
                "friendlyName": "Germany: Local - Business",
                "requirements": [
                    {"kind": "end_user", "name": "Business", "requirementName": "business_info", "type": "end_user", "fields": ["business_name", "vat_number"]},
                    {"kind": "supporting_document", "name": "Proof of address", "requirementName": "proof_of_address", "type": "document", "fields": ["address"]}
                ]
            }
        }))
        .expect("the service's shape"),
    ))
}

fn form(country: &str) -> RegistrationFormInput {
    RegistrationFormInput {
        country: country.to_owned(),
        number_type: "local".to_owned(),
        end_user_type: "business".to_owned(),
        friendly_name: "Berlin office".to_owned(),
    }
}

/// Germany's requirements read, a draft for it listed with a document, a
/// filing in review and one refused.
fn registrations_ready() -> Session {
    let session = registrations_tab(held(signed_in()));
    let session = numbers(
        session,
        NumberAdminAction::EditRegistration { form: form("de") },
    );
    let session = numbers(session, NumberAdminAction::CheckRequirements);
    let session = done(
        session,
        |call| matches!(call, NumberCall::Requirements { .. }),
        german_requirements(),
    );
    done(
        session,
        |call| matches!(call, NumberCall::Registrations),
        filings(serde_json::json!([
            filing(
                "BU-1",
                "draft",
                serde_json::json!([{
                    "id": "RD-1", "requirementName": "proof_of_address", "mimeType": "application/pdf",
                    "sizeBytes": 19, "stored": true, "submitted": false, "updatedAt": "2026-11-07T09:05:00Z"
                }])
            ),
            filing("BU-2", "in-review", serde_json::json!([])),
            filing("BU-3", "twilio-rejected", serde_json::json!([])),
        ])),
    )
}

/// The Registrations tab: read on opening, a draft offered its details,
/// its upload, removal and filing; a filing elsewhere offered nothing; each
/// action the core's event; a viewer read nothing and offered nothing.
#[test]
fn the_registrations_tab_reads_and_files() {
    let opened = registrations_tab(held(signed_in()));
    assert_eq!(
        calls(&opened, |c| matches!(c, NumberCall::Registrations)),
        1
    );
    let session = registrations_ready();
    let tab = view(&session).admin.registrations.expect("the tab shows");
    assert!(tab.member && tab.can_check && tab.can_start);
    assert!(matches!(
        tab.requirements,
        district_ffi::RequirementsView::Ready { ref requirements, .. } if requirements.len() == 2
    ));
    assert_eq!(
        tab.gates
            .iter()
            .map(|g| g.countries.as_str())
            .collect::<Vec<_>>(),
        ["DE"],
        "the approvals, and nothing said of buying"
    );
    let draft = &tab.filings[0];
    assert!(draft.draft && draft.armed && draft.can_file);
    assert_eq!(
        draft
            .details
            .iter()
            .map(|d| d.field.as_str())
            .collect::<Vec<_>>(),
        ["business_name", "vat_number"]
    );
    assert_eq!(draft.uploads[0].label, "Upload Proof of address");
    assert_eq!(draft.uploads[0].requirement_name, "proof_of_address");
    assert!(draft.documents[0].remove_action.is_some());
    let review = &tab.filings[1];
    assert!(!review.draft && review.uploads.is_empty() && review.file_action.is_none());
    let refused = &tab.filings[2];
    assert_eq!(refused.tone, district_ffi::FilingTone::Danger);
    assert_eq!(refused.reasons.len(), 1);

    // The details go with the filing; the upload is typed by its bytes.
    let typed = numbers(
        session,
        NumberAdminAction::EditRegistrationDetails {
            bundle_id: "BU-1".to_owned(),
            details: vec![district_ffi::DetailFieldView {
                field: "business_name".to_owned(),
                value: "Acme GmbH".to_owned(),
            }],
        },
    );
    let typed_view = view(&typed).admin.registrations.unwrap();
    assert_eq!(typed_view.filings[0].details[0].value, "Acme GmbH");
    let uploading = numbers(
        typed,
        NumberAdminAction::UploadRegistrationDocument {
            bundle_id: "BU-1".to_owned(),
            requirement_name: "proof_of_address".to_owned(),
            file: district_ffi::desk::PickedFileView {
                file_name: "lease.pdf".to_owned(),
                size: 9,
                bytes: b"%PDF-1.7\n".to_vec(),
            },
        },
    );
    let sent = uploading.pending.iter().find_map(|e| match e {
        Effect::NumberAdmin {
            call: NumberCall::UploadRegistrationDocument(upload),
            ..
        } => Some(upload.clone()),
        _ => None,
    });
    let sent = sent.expect("the upload is sent");
    assert_eq!(sent.mime_type, "application/pdf");
    assert!(
        !view(&uploading).admin.registrations.unwrap().filings[0].armed,
        "one write at a time"
    );
    let uploaded = done(
        uploading,
        |c| matches!(c, NumberCall::UploadRegistrationDocument(_)),
        Err(server_error()),
    );
    let removing = numbers(
        done(
            uploaded,
            |c| matches!(c, NumberCall::Registrations),
            filings(serde_json::json!([filing(
                "BU-1",
                "draft",
                serde_json::json!([{
                    "id": "RD-1", "requirementName": "proof_of_address", "mimeType": "application/pdf",
                    "sizeBytes": 19, "stored": true, "submitted": false, "updatedAt": "2026-11-07T09:05:00Z"
                }])
            )])),
        ),
        NumberAdminAction::RemoveRegistrationDocument {
            bundle_id: "BU-1".to_owned(),
            document_id: "RD-1".to_owned(),
        },
    );
    assert_eq!(
        calls(&removing, |c| matches!(
            c,
            NumberCall::RemoveRegistrationDocument { .. }
        )),
        1
    );
    let removed = done(
        done(
            removing,
            |c| matches!(c, NumberCall::RemoveRegistrationDocument { .. }),
            Err(server_error()),
        ),
        |c| matches!(c, NumberCall::Registrations),
        filings(serde_json::json!([filing(
            "BU-1",
            "draft",
            serde_json::json!([])
        )])),
    );
    let asked = numbers(
        removed,
        NumberAdminAction::AskSubmitRegistration {
            bundle_id: "BU-1".to_owned(),
        },
    );
    assert!(view(&asked).admin.confirmation.is_some(), "it asks first");
    let filing_call = numbers(asked, NumberAdminAction::Confirm);
    assert_eq!(
        calls(&filing_call, |c| matches!(
            c,
            NumberCall::SubmitRegistration { .. }
        )),
        1
    );

    // Reloaded and started from the form.
    let reload = numbers(
        registrations_ready(),
        NumberAdminAction::ReloadRegistrations,
    );
    assert_eq!(
        calls(&reload, |c| matches!(c, NumberCall::Registrations)),
        1
    );
    let started = numbers(registrations_ready(), NumberAdminAction::StartRegistration);
    assert_eq!(
        calls(&started, |c| matches!(c, NumberCall::CreateRegistration(_))),
        1
    );

    // A viewer: the heading alone, nothing read.
    let viewer_tab = registrations_tab(held(viewer()));
    assert_eq!(
        calls(&viewer_tab, |c| matches!(c, NumberCall::Registrations)),
        0
    );
    let tab = view(&viewer_tab).admin.registrations.unwrap();
    assert!(!tab.member && !tab.can_check && !tab.can_start && tab.filings.is_empty());
}
