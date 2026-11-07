//! The phone numbers screen's Registrations tab, on the core's
//! `NumberRegistrations`, as District AI for Android (`RegistrationsTab`) and
//! for Linux (`pages/registrations.rs`) show it: what a country's regulator
//! asks for, starting a draft filing, and the workspace's filings with their
//! status, the carrier's objections and their documents. On a draft, the
//! end-user details and the documents its regulation asks for, and filing it
//! with the carrier behind the core's confirmation (crate::numbers').
//!
//! What a draft asks for comes from the requirements read for the form's
//! country, number type and end-user type: a draft of that country and types
//! shows a field for each end-user detail and an upload for each document.
//!
//! A viewer may not file: the tab reads nothing for one and offers nothing,
//! and the reason is the screen's scope note. Every word is the core's
//! (`numbers.toml`, the wire vocabulary), but for the upload button's
//! (`numbers.upload_for`).

use std::collections::BTreeMap;

use district_core::{
    NumberAdmin, NumberRegistrations, NumberWrite, RegistrationForm, RegistrationList,
    RequirementsState, Tone, document_state, registration_status, registration_title,
    requirement_fields, requirement_kind,
};
use district_l10n::numbers_strings as ns;
use district_model::{
    CountryRequirements, NumberRegistration, RegistrationDocumentUpload, RegulatoryRequirement,
};
use serde::Serialize;

use crate::files::{FileKind, FilePickView, PickedFileView, sniff};
use crate::l10n::{l, s};
use crate::settings::ChoiceView;
use crate::views::{EmptyView, FailureView, LoadStatus};

/// The largest document the service takes: 10 MiB.
pub(crate) const MAX_DOCUMENT_BYTES: u64 = 10 * 1024 * 1024;

/// The document types the service takes, checked again there by the file's
/// contents.
const DOCUMENT_KINDS: [FileKind; 3] = [FileKind::Pdf, FileKind::Png, FileKind::Jpeg];

/// The Registrations tab, while it shows.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct RegistrationsTabView {
    /// Whether the member may file. A viewer is offered nothing, and only the
    /// tab's heading shows: the reason is the screen's scope note.
    pub member: bool,
    /// The list's heading, "Regulatory registrations".
    pub eyebrow: String,
    /// The form's heading, "Start a registration".
    pub start_title: String,
    /// The country field's label.
    pub country_label: String,
    /// The name field's label.
    pub name_label: String,
    /// The form, as typed.
    pub form: RegistrationFormInput,
    /// The number types to choose from, in the core's order.
    pub number_types: Vec<ChoiceView>,
    /// The end-user types to choose from, in the core's order.
    pub end_user_types: Vec<ChoiceView>,
    /// "Check requirements".
    pub check_action: String,
    /// Whether it works now: a country typed.
    pub can_check: bool,
    /// "Start registration".
    pub start_action: String,
    /// Whether it works now: a country typed, and no write on its way.
    pub can_start: bool,
    /// What the last write said, or `None`.
    pub notice: Option<String>,
    /// Why that write is not offered again, or `None`.
    pub unrepeatable: Option<String>,
    /// Whether that write failed: show the notice as an error.
    pub failed: bool,
    /// Whether a write is on its way.
    pub working: bool,
    /// What the form's country asks for.
    pub requirements: RequirementsView,
    /// Where the read of the filings stands.
    pub list_status: LoadStatus,
    /// That a status is read only when the list is, once there are filings.
    pub status_caption: Option<String>,
    /// None filed yet.
    pub empty: Option<EmptyView>,
    /// The workspace's approvals, when there are any. The countries open to
    /// every workspace are left to the web, which says where numbers are
    /// bought.
    pub gates: Vec<GateView>,
    /// The filings, newest first.
    pub filings: Vec<FilingView>,
}

/// The registration form, as typed.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct RegistrationFormInput {
    /// The country, two letters, as typed.
    pub country: String,
    /// One of the number types' values.
    pub number_type: String,
    /// One of the end-user types' values.
    pub end_user_type: String,
    /// The name the filing gets in the carrier's console. Optional.
    pub friendly_name: String,
}

/// What a country's regulator asks for.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Enum)]
pub enum RequirementsView {
    /// Not asked.
    Idle,
    /// Being asked.
    Loading {
        /// The heading, "What this country asks for".
        eyebrow: String,
    },
    /// The lookup failed, which is not "nothing to file".
    Failed {
        /// The heading.
        eyebrow: String,
        /// Why, and whether "Try again" may help.
        failure: FailureView,
    },
    /// Nothing to file.
    NoRegulation {
        /// The heading.
        eyebrow: String,
        /// "DE · No registration required".
        heading: String,
        /// Why there is nothing to file.
        body: String,
        /// That numbers there cannot be bought through Distronode at all, or
        /// `None`.
        not_purchasable: Option<String>,
    },
    /// The regulation.
    Ready {
        /// The heading.
        eyebrow: String,
        /// The regulation's name, as the carrier writes it.
        name: String,
        /// That numbers there cannot be bought through Distronode at all, or
        /// `None`.
        not_purchasable: Option<String>,
        /// What it asks for.
        requirements: Vec<RequirementRowView>,
    },
}

/// One thing a regulation asks for.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct RequirementRowView {
    /// Its name, as the carrier writes it.
    pub name: String,
    /// Its kind and the fields it asks for: "Your details · business_name".
    pub line: String,
}

/// The countries a list names, under their label.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct GateView {
    /// "Your approvals".
    pub label: String,
    /// The countries, comma-separated.
    pub countries: String,
}

/// How a filing's status reads, the core's `Tone`.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash, Serialize, uniffi::Enum)]
pub enum FilingTone {
    /// Approved.
    Success,
    /// Not used by a filing today.
    Warning,
    /// Refused.
    Danger,
    /// A draft, under review, or a status this build does not know.
    Neutral,
}

impl From<Tone> for FilingTone {
    fn from(tone: Tone) -> Self {
        match tone {
            Tone::Success => Self::Success,
            Tone::Warning => Self::Warning,
            Tone::Danger => Self::Danger,
            Tone::Neutral => Self::Neutral,
        }
    }
}

/// One filing.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct FilingView {
    /// Its id, which every action on it names.
    pub id: String,
    /// "DE · Local".
    pub title: String,
    /// The name it was given, or `None`.
    pub friendly_name: Option<String>,
    /// Its status, through the wire vocabulary.
    pub status: String,
    /// How the status reads.
    pub tone: FilingTone,
    /// "What the carrier objected to", when it objected.
    pub reasons_eyebrow: Option<String>,
    /// The carrier's objections, as it wrote them.
    pub reasons: Vec<String>,
    /// "Documents", when there are any.
    pub documents_eyebrow: Option<String>,
    /// Its documents.
    pub documents: Vec<FilingDocumentView>,
    /// Whether it is a draft: the only filing whose documents may change and
    /// that may be filed.
    pub draft: bool,
    /// On a draft of the country checked, a field for each end-user detail
    /// its regulation lists, with what was typed.
    pub details: Vec<DetailFieldView>,
    /// On a draft of the country checked, an upload for each document its
    /// regulation asks for.
    pub uploads: Vec<UploadView>,
    /// Whether Upload and Remove work now: a draft, and no write on its way.
    pub armed: bool,
    /// "File it", on a draft.
    pub file_action: Option<String>,
    /// Whether it works now: not for a draft whose filing may already have
    /// happened. It asks first.
    pub can_file: bool,
}

/// One document on a filing.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct FilingDocumentView {
    /// Its id, which a removal names.
    pub id: String,
    /// The requirement it meets.
    pub name: String,
    /// Where it is: filed, stored, or recorded with no file yet.
    pub state: String,
    /// "Remove", on a draft's document.
    pub remove_action: Option<String>,
}

/// One end-user detail a draft's regulation asks for.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct DetailFieldView {
    /// The field's name, as the regulation lists it: its label too.
    pub field: String,
    /// What was typed.
    pub value: String,
}

/// One document a draft's regulation asks for.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct UploadView {
    /// The requirement the upload names.
    pub requirement_name: String,
    /// "Upload Proof of address".
    pub label: String,
}

/// The form's fields, as the core holds them.
impl From<&RegistrationForm> for RegistrationFormInput {
    fn from(form: &RegistrationForm) -> Self {
        Self {
            country: form.country.clone(),
            number_type: form.number_type.clone(),
            end_user_type: form.end_user_type.clone(),
            friendly_name: form.friendly_name.clone(),
        }
    }
}

impl From<RegistrationFormInput> for RegistrationForm {
    fn from(form: RegistrationFormInput) -> Self {
        Self {
            country: form.country,
            number_type: form.number_type,
            end_user_type: form.end_user_type,
            friendly_name: form.friendly_name,
        }
    }
}

/// The pick for a filing's document: a PDF, PNG or JPEG, one at a time, read
/// to one byte past the largest the service takes, so its refusal says why.
#[uniffi::export]
pub fn registration_document_pick() -> FilePickView {
    let mut pick = crate::files::pick(&DOCUMENT_KINDS.map(FileKind::mime_type), 1);
    pick.read_cap = MAX_DOCUMENT_BYTES + 1;
    pick
}

/// The upload of `file`, for `requirement_name` of the draft `bundle_id`. Its
/// type is the one its first bytes say, never its name's.
pub(crate) fn upload(
    bundle_id: String,
    requirement_name: String,
    file: PickedFileView,
) -> RegistrationDocumentUpload {
    RegistrationDocumentUpload {
        bundle_id,
        requirement_name,
        mime_type: sniff(&file.bytes).mime_type().to_owned(),
        file_name: file.file_name,
        bytes: file.bytes,
    }
}

/// The details typed, each field that holds something.
pub(crate) fn details(fields: Vec<DetailFieldView>) -> BTreeMap<String, String> {
    fields
        .into_iter()
        .filter(|field| !field.value.trim().is_empty())
        .map(|field| (field.field, field.value))
        .collect()
}

/// The regulation `requirements` holds when it is the one `registration` is
/// filed under: the same country, number type and end-user type.
fn regulation_for<'a>(
    requirements: &'a RequirementsState,
    registration: &NumberRegistration,
) -> Option<&'a CountryRequirements> {
    match requirements {
        RequirementsState::Ready { regulation, .. }
            if regulation
                .iso_country
                .eq_ignore_ascii_case(&registration.iso_country)
                && regulation.number_type == registration.number_type
                && regulation.end_user_type == registration.end_user_type =>
        {
            Some(regulation)
        }
        _ => None,
    }
}

/// The end-user detail fields a regulation asks for, each once, in its order.
fn detail_fields(regulation: &CountryRequirements) -> Vec<String> {
    let mut fields: Vec<String> = Vec::new();
    for requirement in regulation.requirements.iter().filter(|r| !r.is_document()) {
        for field in &requirement.fields {
            if !fields.contains(field) {
                fields.push(field.clone());
            }
        }
    }
    fields
}

fn requirement_row(requirement: &RegulatoryRequirement) -> RequirementRowView {
    RequirementRowView {
        name: requirement.name.clone(),
        line: format!(
            "{} \u{b7} {}",
            requirement_kind(requirement),
            requirement_fields(requirement)
        ),
    }
}

fn choices(pairs: Vec<(&'static str, String)>) -> Vec<ChoiceView> {
    pairs
        .into_iter()
        .map(|(value, label)| ChoiceView {
            value: value.to_owned(),
            label,
        })
        .collect()
}

fn requirements_view(requirements: &RequirementsState) -> RequirementsView {
    let eyebrow = || ns::numbers_requirements_eyebrow(l()).to_owned();
    let not_purchasable = |purchasable: bool| {
        (!purchasable).then(|| ns::numbers_requirements_not_purchasable(l()).to_owned())
    };
    match requirements {
        RequirementsState::Idle => RequirementsView::Idle,
        RequirementsState::Loading => RequirementsView::Loading { eyebrow: eyebrow() },
        RequirementsState::Failed(failure) => RequirementsView::Failed {
            eyebrow: eyebrow(),
            failure: failure.into(),
        },
        RequirementsState::NoRegulation {
            country,
            purchasable,
        } => RequirementsView::NoRegulation {
            eyebrow: eyebrow(),
            heading: RequirementsState::none_heading(country),
            body: ns::numbers_requirements_none_body(l()).to_owned(),
            not_purchasable: not_purchasable(*purchasable),
        },
        RequirementsState::Ready {
            regulation,
            purchasable,
        } => RequirementsView::Ready {
            eyebrow: eyebrow(),
            name: regulation.friendly_name.clone(),
            not_purchasable: not_purchasable(*purchasable),
            requirements: regulation
                .requirements
                .iter()
                .map(requirement_row)
                .collect(),
        },
    }
}

fn filing_view(
    registrations: &NumberRegistrations,
    registration: &NumberRegistration,
) -> FilingView {
    let draft = registration.is_draft();
    let needs = draft
        .then(|| regulation_for(&registrations.requirements, registration))
        .flatten();
    let typed = registrations.details.get(&registration.id);
    FilingView {
        id: registration.id.clone(),
        title: registration_title(registration),
        friendly_name: registration
            .friendly_name
            .as_deref()
            .map(str::trim)
            .filter(|name| !name.is_empty())
            .map(str::to_owned),
        status: registration_status(&registration.status),
        tone: Tone::of_registration(&registration.status).into(),
        reasons_eyebrow: (!registration.rejection_reasons.is_empty())
            .then(|| ns::numbers_registration_reasons_eyebrow(l()).to_owned()),
        reasons: registration.rejection_reasons.clone(),
        documents_eyebrow: (!registration.documents.is_empty())
            .then(|| ns::numbers_registration_documents_eyebrow(l()).to_owned()),
        documents: registration
            .documents
            .iter()
            .map(|document| FilingDocumentView {
                id: document.id.clone(),
                name: document.requirement_name.clone(),
                state: document_state(document).to_owned(),
                remove_action: draft.then(|| ns::numbers_document_remove_action(l()).to_owned()),
            })
            .collect(),
        draft,
        details: needs
            .map(|regulation| {
                detail_fields(regulation)
                    .into_iter()
                    .map(|field| DetailFieldView {
                        value: typed
                            .and_then(|typed| typed.get(&field))
                            .cloned()
                            .unwrap_or_default(),
                        field,
                    })
                    .collect()
            })
            .unwrap_or_default(),
        uploads: needs
            .map(|regulation| {
                regulation
                    .requirements
                    .iter()
                    .filter(|requirement| requirement.is_document())
                    .map(|requirement| UploadView {
                        requirement_name: requirement.requirement_name.clone(),
                        label: s::numbers_upload_for(l(), &requirement.name),
                    })
                    .collect()
            })
            .unwrap_or_default(),
        armed: registrations.draft_armed(&registration.id),
        file_action: draft.then(|| ns::numbers_submit_confirm_action(l()).to_owned()),
        can_file: registrations.can_file(&registration.id),
    }
}

/// The Registrations tab, for a member who may file when `can_change`.
pub(crate) fn registrations_view(admin: &NumberAdmin, can_change: bool) -> RegistrationsTabView {
    let registrations = &admin.registrations;
    let write = &registrations.write;
    let mut view = RegistrationsTabView {
        member: can_change,
        eyebrow: ns::numbers_registrations_eyebrow(l()).to_owned(),
        start_title: ns::numbers_registration_start_title(l()).to_owned(),
        country_label: ns::numbers_registration_country_label(l()).to_owned(),
        name_label: ns::numbers_registration_name_label(l()).to_owned(),
        form: RegistrationFormInput::from(&registrations.form),
        number_types: choices(RegistrationForm::number_types()),
        end_user_types: choices(RegistrationForm::end_user_types()),
        check_action: ns::numbers_requirements_check_action(l()).to_owned(),
        can_check: can_change && registrations.form.is_submittable(),
        start_action: ns::numbers_registration_start_action(l()).to_owned(),
        can_start: can_change && registrations.can_start(),
        notice: write.notice().map(str::to_owned),
        unrepeatable: write.unrepeatable_note().map(str::to_owned),
        failed: matches!(write, NumberWrite::Failed { .. }),
        working: write.is_running(),
        requirements: requirements_view(&registrations.requirements),
        list_status: LoadStatus::Loading,
        status_caption: None,
        empty: None,
        gates: Vec::new(),
        filings: Vec::new(),
    };
    if !can_change {
        // Nothing is read for a viewer: the tab shows its heading alone.
        view.list_status = LoadStatus::Ready;
        view.requirements = RequirementsView::Idle;
        return view;
    }
    match &registrations.list {
        RegistrationList::NotLoaded | RegistrationList::Loading => {}
        RegistrationList::Failed(failure) => {
            view.list_status = LoadStatus::failed(ns::numbers_registrations_failed(l()), failure);
        }
        RegistrationList::Ready(list) => {
            view.list_status = LoadStatus::Ready;
            // The workspace's approvals only. The countries open to every
            // workspace are the core's "Ready to buy today", and this app says
            // nothing of buying (scripts/check-store-copy.py): numbers are
            // bought on the web, which lists them.
            view.gates = [(ns::numbers_your_approvals(l()), &list.approved_countries)]
                .into_iter()
                .filter(|(_, countries)| !countries.is_empty())
                .map(|(label, countries)| GateView {
                    label: label.to_owned(),
                    countries: countries.join(", "),
                })
                .collect();
            if list.registrations.is_empty() {
                view.empty = Some(EmptyView::new(
                    ns::numbers_registrations_empty_title(l()),
                    ns::numbers_registrations_empty_body(l()),
                ));
            } else {
                view.status_caption = Some(ns::numbers_registration_status_caption(l()).to_owned());
            }
            view.filings = list
                .registrations
                .iter()
                .map(|registration| filing_view(registrations, registration))
                .collect();
        }
    }
    view
}

#[cfg(test)]
mod tests {
    use district_l10n::Locale;

    use super::*;

    fn requirement(kind: &str, name: &str, fields: &[&str]) -> RegulatoryRequirement {
        RegulatoryRequirement {
            kind: kind.to_owned(),
            name: name.to_owned(),
            requirement_name: format!("{name}-id"),
            requirement_type: "type".to_owned(),
            fields: fields.iter().map(|f| (*f).to_owned()).collect(),
            accepted_documents: None,
        }
    }

    #[test]
    fn a_draft_reads_its_own_regulation_only() {
        let regulation = CountryRequirements {
            iso_country: "DE".to_owned(),
            number_type: "local".to_owned(),
            end_user_type: "business".to_owned(),
            regulation_sid: "RN".to_owned(),
            friendly_name: "Germany".to_owned(),
            requirements: vec![
                requirement("end_user", "Business", &["business_name", "vat"]),
                requirement("supporting_document", "Proof of address", &["address"]),
                requirement("end_user", "Contact", &["vat", "email"]),
            ],
        };
        assert_eq!(
            detail_fields(&regulation),
            ["business_name", "vat", "email"]
        );
        let ready = RequirementsState::Ready {
            regulation,
            purchasable: true,
        };
        let filed = |country: &str, number_type: &str| NumberRegistration {
            iso_country: country.to_owned(),
            number_type: number_type.to_owned(),
            end_user_type: "business".to_owned(),
            ..NumberRegistration::default()
        };
        assert!(regulation_for(&ready, &filed("de", "local")).is_some());
        assert!(regulation_for(&ready, &filed("FR", "local")).is_none());
        assert!(regulation_for(&ready, &filed("DE", "mobile")).is_none());
        assert!(regulation_for(&RequirementsState::Idle, &filed("DE", "local")).is_none());
    }

    /// A failed lookup is not "nothing to file", and a country Distronode
    /// cannot sell in says so either way; a failed list and an empty one each
    /// read as themselves.
    #[test]
    fn each_read_ends_its_own_way() {
        let failure = district_core::FailureText {
            message: "Busy.".to_owned(),
            degraded_regions: Vec::new(),
            session_ended: None,
            retryable: true,
        };
        assert!(matches!(
            requirements_view(&RequirementsState::Failed(failure.clone())),
            RequirementsView::Failed {
                failure: FailureView {
                    retryable: true,
                    ..
                },
                ..
            }
        ));
        let RequirementsView::NoRegulation {
            heading,
            not_purchasable,
            ..
        } = requirements_view(&RequirementsState::NoRegulation {
            country: "US".to_owned(),
            purchasable: false,
        })
        else {
            panic!("no regulation");
        };
        assert_eq!(heading, "US \u{b7} No registration required");
        assert!(not_purchasable.is_some());
        assert!(matches!(
            requirements_view(&RequirementsState::Loading),
            RequirementsView::Loading { .. }
        ));

        let mut admin = NumberAdmin::default();
        admin.registrations.list = RegistrationList::Failed(failure);
        assert!(matches!(
            registrations_view(&admin, true).list_status,
            LoadStatus::Failed { .. }
        ));
        admin.registrations.list =
            RegistrationList::Ready(district_model::NumberRegistrationsResponse {
                success: true,
                registrations: Vec::new(),
                approved_countries: vec!["DE".to_owned()],
                platform_countries: Vec::new(),
            });
        let view = registrations_view(&admin, true);
        assert!(view.empty.is_some() && view.status_caption.is_none());
        assert_eq!(view.gates.len(), 1, "only the lists that name a country");
    }

    #[test]
    fn an_upload_is_typed_by_its_bytes_and_details_by_what_holds_something() {
        let sent = upload(
            "BU1".to_owned(),
            "proof".to_owned(),
            PickedFileView {
                file_name: "lease.png".to_owned(),
                size: 9,
                bytes: b"%PDF-1.7\n".to_vec(),
            },
        );
        assert_eq!(sent.mime_type, "application/pdf", "the bytes, not the name");
        assert_eq!(sent.requirement_name, "proof");
        let typed = details(vec![
            DetailFieldView {
                field: "business_name".to_owned(),
                value: "Acme GmbH".to_owned(),
            },
            DetailFieldView {
                field: "vat".to_owned(),
                value: " ".to_owned(),
            },
        ]);
        assert_eq!(typed.len(), 1);
        let pick = registration_document_pick();
        assert_eq!(pick.extensions, [".pdf", ".png", ".jpg", ".jpeg"]);
        assert_eq!(pick.max_files, 1);
        assert_eq!(pick.read_cap, MAX_DOCUMENT_BYTES + 1);
        assert_eq!(
            district_l10n::with_locale(Locale::FrCa, || s::numbers_upload_for(
                l(),
                "Preuve d'adresse"
            )),
            "T\u{e9}l\u{e9}verser\u{a0}: Preuve d'adresse"
        );
        for tone in [Tone::Success, Tone::Warning, Tone::Danger, Tone::Neutral] {
            let _ = FilingTone::from(tone);
        }
    }
}
