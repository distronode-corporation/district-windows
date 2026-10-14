//! Managing the numbers a workspace holds, on the core's `NumberAdmin`, as
//! District AI for iOS does (`NumberActionsSheet`, `CarrierSection`,
//! `ComplianceSection`): reapplying a held number's routing, releasing it
//! behind a destructive confirmation, the billed lookup behind a confirmation,
//! and the carrier tab (the carrier accounts, the SIP trunks, the one-time
//! passcode sender, and the A2P 10DLC and toll-free registrations).
//!
//! Buying a number stays on the web: nothing here buys one. Every word is the
//! core's (`numbers.toml`), but for the campaign type's label, which its
//! catalog has none for and this crate's has (`numbers.campaign_type`).

use district_core::{
    A2pForm, CarrierStatus, Event, MarketplaceEvent, NumberAdmin, NumberAdminEvent,
    NumberConfirmation, NumberWrite, SipTrunkForm, SipTrunks, TollFreeForm, VerifyService,
    carrier_balance, carrier_number_count, carrier_state, number_actions_note, sip_trunk_meta,
    verify_state,
};
use district_l10n::numbers_strings as ns;
use district_model::{OwnedNumber, ProviderStatusEntry, SipTrunk};
use serde::Serialize;

use crate::l10n::{l, s};
use crate::settings::ChoiceView;
use crate::views::{EmptyView, LoadStatus, humanize};

/// What can be done to one held number.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct NumberManageView {
    /// The number in full, to act on it by.
    pub phone_number: String,
    /// Why nothing can be done to it (a number Distronode holds, or a viewer),
    /// or `None` when it can be managed.
    pub note: Option<String>,
    /// Whether "Reapply call routing" works now.
    pub can_configure: bool,
    /// Whether "Release this number" works now: it asks first.
    pub can_release: bool,
    /// Whether a write to this number is on its way.
    pub busy: bool,
}

/// The managing part of the phone numbers screen.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct NumberAdminView {
    /// The carrier tab's label.
    pub carrier_tab: String,
    /// "Reapply call routing".
    pub configure_action: String,
    /// What reapplying does.
    pub configure_caption: String,
    /// "Release this number".
    pub release_action: String,
    /// What the last reapply or release said, or `None`.
    pub number_notice: Option<String>,
    /// Why that write is not offered again, or `None`.
    pub number_unrepeatable: Option<String>,
    /// The billed lookup.
    pub lookup: LookupView,
    /// The confirmation asked, if any.
    pub confirmation: Option<NumberConfirmationView>,
    /// The carrier tab, while it shows: it is read when it opens.
    pub carrier: Option<CarrierTabView>,
}

/// The billed lookup.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct LookupView {
    /// Its heading.
    pub eyebrow: String,
    /// The number field's label.
    pub number_label: String,
    /// The number, as typed.
    pub number: String,
    /// The button that asks before looking it up.
    pub action: String,
    /// Whether the button works now: a number typed, and no lookup on its way
    /// or unconfirmed.
    pub can_ask: bool,
    /// The last answer, a line each.
    pub result: Vec<String>,
    /// What the last lookup said when it failed, or `None`.
    pub notice: Option<String>,
    /// Why it is not offered again, or `None`.
    pub unrepeatable: Option<String>,
}

/// A confirmation asked before a write that cannot be undone or costs money.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct NumberConfirmationView {
    /// The question.
    pub title: String,
    /// What happens.
    pub body: String,
    /// The button that goes ahead: "Working…" while it is on its way.
    pub confirm: String,
    /// The button that closes it: "Close" once the write may have happened.
    pub close: String,
    /// Whether to draw it as destructive: a release.
    pub destructive: bool,
    /// Whether going ahead works now.
    pub can_confirm: bool,
    /// Whether its write is on its way: it cannot be closed meanwhile.
    pub running: bool,
    /// Why the write failed, or `None`.
    pub failure: Option<String>,
    /// Why it is not offered again, or `None`.
    pub unrepeatable: Option<String>,
}

/// The carrier tab.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct CarrierTabView {
    /// "Carrier accounts".
    pub accounts_eyebrow: String,
    /// Where the read of the carrier accounts stands.
    pub accounts_status: LoadStatus,
    /// No carrier connected: an account state, not a fault.
    pub accounts_empty: Option<EmptyView>,
    /// The carrier accounts.
    pub accounts: Vec<CarrierAccountView>,
    /// What the last carrier write (a trunk, the passcode sender, a
    /// registration) said, or `None`.
    pub notice: Option<String>,
    /// Why it is not offered again, or `None`.
    pub unrepeatable: Option<String>,
    /// Whether a carrier write is on its way.
    pub working: bool,
    /// The SIP trunks.
    pub sip: SipView,
    /// The one-time passcode sender.
    pub verify: VerifyView,
    /// "Messaging compliance".
    pub compliance_eyebrow: String,
    /// That neither registration can be checked from the app.
    pub compliance_note: String,
    /// The A2P 10DLC registration.
    pub a2p: A2pView,
    /// The toll-free verification.
    pub toll_free: TollFreeView,
}

/// One carrier account.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct CarrierAccountView {
    /// The carrier.
    pub carrier: String,
    /// The account's name, when the carrier gave one.
    pub account_name: Option<String>,
    /// Refused, held by Distronode, or connected.
    pub state: String,
    /// Whether its credentials were refused.
    pub refused: bool,
    /// The balance, when there is one.
    pub balance: Option<String>,
    /// How many numbers it holds, when it said.
    pub number_count: Option<String>,
}

/// The SIP trunks, and the form creating one.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct SipView {
    /// "SIP trunks".
    pub eyebrow: String,
    /// Where the read stands.
    pub status: LoadStatus,
    /// None yet.
    pub empty: Option<EmptyView>,
    /// The trunks.
    pub trunks: Vec<SipTrunkView>,
    /// The trunk name field's label.
    pub name_label: String,
    /// The domain label field's label.
    pub domain_label: String,
    /// What the carrier adds to the domain.
    pub domain_caption: String,
    /// The addresses field's label.
    pub ip_label: String,
    /// The form, as typed.
    pub form: SipTrunkFormInput,
    /// The monthly charge, said before the button.
    pub charge_note: String,
    /// "Create trunk".
    pub create_action: String,
    /// Whether it works now.
    pub can_create: bool,
}

/// One SIP trunk.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct SipTrunkView {
    /// The trunk's name.
    pub name: String,
    /// Its domain.
    pub domain: String,
    /// Its state and how many addresses may use it.
    pub meta: String,
    /// That no carrier resources are recorded for it, or `None`.
    pub unprovisioned: Option<String>,
}

/// The SIP trunk form.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct SipTrunkFormInput {
    /// The trunk's name.
    pub name: String,
    /// The domain's label only.
    pub domain: String,
    /// The allowed addresses, one per line.
    pub ip_addresses: String,
}

/// The one-time passcode sender.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct VerifyView {
    /// "One-time passcodes".
    pub eyebrow: String,
    /// Where the read stands.
    pub status: LoadStatus,
    /// Whether it is in use, once read.
    pub enabled: Option<bool>,
    /// Its state in words, once read.
    pub state: Option<String>,
    /// The button that turns it the other way, once read.
    pub action: Option<String>,
    /// What that does.
    pub caption: Option<String>,
    /// Whether the button works now.
    pub can_toggle: bool,
}

/// The A2P 10DLC registration form.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct A2pView {
    /// Its heading.
    pub title: String,
    /// Why it is needed.
    pub caption: String,
    /// The fields' labels.
    pub labels: A2pLabels,
    /// The form, as typed.
    pub form: A2pFormInput,
    /// The campaign types, a closed list: the type decides the brand and the
    /// price.
    pub campaign_types: Vec<ChoiceView>,
    /// "Submit registration", which asks first.
    pub submit_action: String,
    /// Whether it works now.
    pub can_submit: bool,
}

/// The A2P form's labels.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct A2pLabels {
    /// The legal business name.
    pub business_name: String,
    /// The tax or company number.
    pub ein: String,
    /// The website.
    pub website: String,
    /// The industry.
    pub vertical: String,
    /// The campaign type.
    pub campaign_type: String,
    /// What will be sent.
    pub campaign_description: String,
    /// The first example message.
    pub sample_one: String,
    /// The second example message.
    pub sample_two: String,
}

/// The A2P form, as typed.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct A2pFormInput {
    /// The legal business name.
    pub business_name: String,
    /// The tax or company number. Optional.
    pub ein: String,
    /// The website. Optional.
    pub website: String,
    /// The industry. Optional.
    pub vertical: String,
    /// One of [`A2pView::campaign_types`]' values.
    pub campaign_type: String,
    /// What will be sent.
    pub campaign_description: String,
    /// An example message.
    pub sample_one: String,
    /// Another example message.
    pub sample_two: String,
}

/// The toll-free verification form.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct TollFreeView {
    /// Its heading.
    pub title: String,
    /// Why it is needed.
    pub caption: String,
    /// The fields' labels.
    pub labels: TollFreeLabels,
    /// What a reviewer does with the opt-in link.
    pub opt_in_caption: String,
    /// The form, as typed.
    pub form: TollFreeFormInput,
    /// The use cases.
    pub use_cases: Vec<ChoiceView>,
    /// "Submit verification", which asks first.
    pub submit_action: String,
    /// Whether it works now.
    pub can_submit: bool,
}

/// The toll-free form's labels.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct TollFreeLabels {
    /// The toll-free number.
    pub phone_number: String,
    /// The business's name.
    pub business_name: String,
    /// The opt-in link.
    pub opt_in_image_url: String,
    /// The example message.
    pub message_content: String,
    /// The use case.
    pub use_case: String,
}

/// The toll-free form, as typed. The service's website and opt-in flow are
/// left to its defaults, as the iOS app leaves them.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct TollFreeFormInput {
    /// The toll-free number.
    pub phone_number: String,
    /// The business's name.
    pub business_name: String,
    /// A public link to the opt-in form or a screenshot of it.
    pub opt_in_image_url: String,
    /// An example message. Optional.
    pub message_content: String,
    /// One of [`TollFreeView::use_cases`]' values.
    pub use_case: String,
}

/// Something the member did to manage numbers.
#[derive(Clone, Debug, PartialEq, Eq, uniffi::Enum)]
pub enum NumberAdminAction {
    /// "Reapply call routing" on a held number. Not confirmed: it converges.
    Configure {
        /// The number in full.
        phone_number: String,
    },
    /// "Release this number": the core asks first.
    AskRelease {
        /// The number in full.
        phone_number: String,
    },
    /// The number to look up changed.
    EditLookup {
        /// What it holds now.
        phone_number: String,
    },
    /// "Look this number up": the core asks first.
    AskLookup,
    /// The SIP trunk form changed.
    EditSipTrunk {
        /// The whole form.
        form: SipTrunkFormInput,
    },
    /// "Create trunk".
    CreateSipTrunk,
    /// Turn the passcode sender on or off.
    SetVerify {
        /// On.
        enabled: bool,
    },
    /// The A2P form changed.
    EditA2p {
        /// The whole form.
        form: A2pFormInput,
    },
    /// "Submit registration": the core asks first.
    AskA2p,
    /// The toll-free form changed.
    EditTollFree {
        /// The whole form.
        form: TollFreeFormInput,
    },
    /// "Submit verification": the core asks first.
    AskTollFree,
    /// Go ahead with the confirmation asked.
    Confirm,
    /// Close the confirmation. The core refuses while its write is on its way.
    CancelConfirmation,
}

impl NumberAdminAction {
    /// The core event this is.
    pub(crate) fn event(self) -> Event {
        Event::Marketplace(MarketplaceEvent::Numbers(match self {
            Self::Configure { phone_number } => NumberAdminEvent::Configure(phone_number),
            Self::AskRelease { phone_number } => NumberAdminEvent::AskRelease(phone_number),
            Self::EditLookup { phone_number } => NumberAdminEvent::EditLookup(phone_number),
            Self::AskLookup => NumberAdminEvent::AskLookup,
            Self::EditSipTrunk { form } => NumberAdminEvent::EditSipTrunk(SipTrunkForm {
                name: form.name,
                domain: form.domain,
                ip_addresses: form.ip_addresses,
            }),
            Self::CreateSipTrunk => NumberAdminEvent::CreateSipTrunk,
            Self::SetVerify { enabled } => NumberAdminEvent::SetVerify(enabled),
            Self::EditA2p { form } => NumberAdminEvent::EditA2p(A2pForm {
                business_name: form.business_name,
                ein: form.ein,
                website: form.website,
                vertical: form.vertical,
                campaign_type: form.campaign_type,
                campaign_description: form.campaign_description,
                sample_one: form.sample_one,
                sample_two: form.sample_two,
            }),
            Self::AskA2p => NumberAdminEvent::AskA2p,
            // The website and the opt-in flow stay blank, for the service's
            // defaults, as the iOS app leaves them.
            Self::EditTollFree { form } => NumberAdminEvent::EditTollFree(TollFreeForm {
                phone_number: form.phone_number,
                business_name: form.business_name,
                use_case: form.use_case,
                message_content: form.message_content,
                opt_in_image_url: form.opt_in_image_url,
                ..TollFreeForm::default()
            }),
            Self::AskTollFree => NumberAdminEvent::AskTollFree,
            Self::Confirm => NumberAdminEvent::Confirm,
            Self::CancelConfirmation => NumberAdminEvent::CancelConfirmation,
        }))
    }
}

/// What can be done to `number`, held by the workspace, for a member who can
/// change numbers when `can_change`.
pub(crate) fn manage_view(
    number: &OwnedNumber,
    admin: &NumberAdmin,
    can_change: bool,
) -> NumberManageView {
    let note = number_actions_note(number, can_change).map(str::to_owned);
    let ours = admin.number_written.as_deref() == Some(number.phone_number.as_str());
    let armed = admin.number_write.is_armed();
    NumberManageView {
        phone_number: number.phone_number.clone(),
        can_configure: note.is_none() && armed,
        can_release: note.is_none() && armed && admin.confirmation.is_none(),
        busy: ours && admin.number_write.is_running(),
        note,
    }
}

/// The managing part of the screen, for a member who can change numbers when
/// `can_change`, with the carrier tab when `on_carrier_tab`.
pub(crate) fn admin_view(
    admin: &NumberAdmin,
    can_change: bool,
    on_carrier_tab: bool,
) -> NumberAdminView {
    NumberAdminView {
        carrier_tab: ns::numbers_tab_carrier(l()).to_owned(),
        configure_action: ns::numbers_configure_action(l()).to_owned(),
        configure_caption: ns::numbers_configure_caption(l()).to_owned(),
        release_action: ns::numbers_release_action(l()).to_owned(),
        number_notice: admin.number_write.notice().map(str::to_owned),
        number_unrepeatable: admin.number_write.unrepeatable_note().map(str::to_owned),
        lookup: lookup_view(admin, can_change),
        confirmation: admin
            .confirmation
            .as_ref()
            .map(|asked| confirmation_view(admin, asked)),
        carrier: on_carrier_tab.then(|| carrier_view(admin, can_change)),
    }
}

fn lookup_view(admin: &NumberAdmin, can_change: bool) -> LookupView {
    let write = &admin.lookup_write;
    LookupView {
        eyebrow: ns::numbers_lookup_eyebrow(l()).to_owned(),
        number_label: ns::numbers_lookup_number_label(l()).to_owned(),
        number: admin.lookup_number.clone(),
        action: ns::numbers_lookup_action(l()).to_owned(),
        can_ask: can_change
            && write.is_armed()
            && admin.confirmation.is_none()
            && !admin.lookup_number.trim().is_empty(),
        result: admin.lookup_result.clone().unwrap_or_default(),
        notice: failed_notice(write),
        unrepeatable: write.unrepeatable_note().map(str::to_owned),
    }
}

/// Why `write` failed, or `None`. A write that worked says so in its own
/// place (the lookup's answer, a confirmation closing).
fn failed_notice(write: &NumberWrite) -> Option<String> {
    matches!(write, NumberWrite::Failed { .. })
        .then(|| write.notice().map(str::to_owned))
        .flatten()
}

fn confirmation_view(admin: &NumberAdmin, asked: &NumberConfirmation) -> NumberConfirmationView {
    let write = admin.write_for(asked);
    NumberConfirmationView {
        title: asked.title().to_owned(),
        body: asked.body(),
        confirm: admin
            .confirm_label()
            .unwrap_or_else(|| asked.action())
            .to_owned(),
        close: write.close_label().to_owned(),
        destructive: asked.is_destructive(),
        can_confirm: write.is_armed(),
        running: write.is_running(),
        failure: failed_notice(write),
        unrepeatable: write.unrepeatable_note().map(str::to_owned),
    }
}

fn carrier_view(admin: &NumberAdmin, can_change: bool) -> CarrierTabView {
    let (accounts_status, accounts_empty, accounts) = match &admin.carrier {
        CarrierStatus::NotLoaded | CarrierStatus::Loading => {
            (LoadStatus::Loading, None, Vec::new())
        }
        CarrierStatus::Failed(failure) => (
            LoadStatus::failed(ns::numbers_carrier_eyebrow(l()), failure),
            None,
            Vec::new(),
        ),
        CarrierStatus::NotConnected => (
            LoadStatus::Ready,
            Some(EmptyView::new(
                ns::numbers_carrier_none_title(l()),
                ns::numbers_carrier_none_body(l()),
            )),
            Vec::new(),
        ),
        CarrierStatus::Ready(status) => (
            LoadStatus::Ready,
            None,
            status
                .providers
                .iter()
                .flatten()
                .map(|(carrier, entry)| account_view(carrier, entry))
                .collect(),
        ),
    };
    let write = &admin.carrier_write;
    CarrierTabView {
        accounts_eyebrow: ns::numbers_carrier_eyebrow(l()).to_owned(),
        accounts_status,
        accounts_empty,
        accounts,
        notice: write.notice().map(str::to_owned),
        unrepeatable: write.unrepeatable_note().map(str::to_owned),
        working: write.is_running(),
        sip: sip_view(admin, can_change),
        verify: verify_view(admin, can_change),
        compliance_eyebrow: ns::numbers_compliance_eyebrow(l()).to_owned(),
        compliance_note: ns::numbers_compliance_no_status(l()).to_owned(),
        a2p: a2p_view(admin, can_change),
        toll_free: toll_free_view(admin, can_change),
    }
}

fn account_view(carrier: &str, entry: &ProviderStatusEntry) -> CarrierAccountView {
    CarrierAccountView {
        carrier: humanize(carrier),
        account_name: entry
            .account_name
            .as_deref()
            .map(str::trim)
            .filter(|name| !name.is_empty())
            .map(str::to_owned),
        state: carrier_state(entry).to_owned(),
        refused: !entry.connected,
        balance: carrier_balance(entry),
        number_count: carrier_number_count(entry),
    }
}

fn sip_view(admin: &NumberAdmin, can_change: bool) -> SipView {
    let (status, empty, trunks) = match &admin.sip {
        SipTrunks::NotLoaded | SipTrunks::Loading => (LoadStatus::Loading, None, Vec::new()),
        SipTrunks::Failed(failure) => (
            LoadStatus::failed(ns::numbers_sip_eyebrow(l()), failure),
            None,
            Vec::new(),
        ),
        SipTrunks::Ready(trunks) => (
            LoadStatus::Ready,
            trunks.is_empty().then(|| {
                EmptyView::new(
                    ns::numbers_sip_empty_title(l()),
                    ns::numbers_sip_empty_body(l()),
                )
            }),
            trunks.iter().map(trunk_view).collect(),
        ),
    };
    let form = &admin.sip_form;
    SipView {
        eyebrow: ns::numbers_sip_eyebrow(l()).to_owned(),
        status,
        empty,
        trunks,
        name_label: ns::numbers_sip_name_label(l()).to_owned(),
        domain_label: ns::numbers_sip_domain_label(l()).to_owned(),
        domain_caption: ns::numbers_sip_domain_caption(l()).to_owned(),
        ip_label: ns::numbers_sip_ip_label(l()).to_owned(),
        form: SipTrunkFormInput {
            name: form.name.clone(),
            domain: form.domain.clone(),
            ip_addresses: form.ip_addresses.clone(),
        },
        charge_note: ns::numbers_sip_one_way(l()).to_owned(),
        create_action: ns::numbers_sip_create_action(l()).to_owned(),
        can_create: can_change && admin.carrier_write.is_armed() && form.is_submittable(),
    }
}

fn trunk_view(trunk: &SipTrunk) -> SipTrunkView {
    SipTrunkView {
        name: trunk.name.clone(),
        domain: trunk.domain.clone(),
        meta: sip_trunk_meta(trunk),
        unprovisioned: (trunk.domain_sid.is_none() && trunk.ip_acl_sid.is_none())
            .then(|| ns::numbers_sip_unprovisioned(l()).to_owned()),
    }
}

fn verify_view(admin: &NumberAdmin, can_change: bool) -> VerifyView {
    let eyebrow = ns::numbers_verify_eyebrow(l()).to_owned();
    let armed = can_change && admin.carrier_write.is_armed();
    match &admin.verify {
        VerifyService::NotLoaded | VerifyService::Loading => VerifyView {
            eyebrow,
            status: LoadStatus::Loading,
            enabled: None,
            state: None,
            action: None,
            caption: None,
            can_toggle: false,
        },
        VerifyService::Failed(failure) => VerifyView {
            status: LoadStatus::failed(&eyebrow, failure),
            eyebrow,
            enabled: None,
            state: None,
            action: None,
            caption: None,
            can_toggle: false,
        },
        VerifyService::Ready { enabled } => {
            let (action, caption) = if *enabled {
                (
                    ns::numbers_verify_disable_action(l()),
                    ns::numbers_verify_disable_caption(l()),
                )
            } else {
                (
                    ns::numbers_verify_enable_action(l()),
                    ns::numbers_verify_enable_caption(l()),
                )
            };
            VerifyView {
                eyebrow,
                status: LoadStatus::Ready,
                enabled: Some(*enabled),
                state: Some(verify_state(*enabled).to_owned()),
                action: Some(action.to_owned()),
                caption: Some(caption.to_owned()),
                can_toggle: armed,
            }
        }
    }
}

fn a2p_view(admin: &NumberAdmin, can_change: bool) -> A2pView {
    let form = &admin.a2p_form;
    A2pView {
        title: ns::numbers_a2p_title(l()).to_owned(),
        caption: ns::numbers_a2p_caption(l()).to_owned(),
        labels: A2pLabels {
            business_name: ns::numbers_a2p_business_label(l()).to_owned(),
            ein: ns::numbers_a2p_ein_label(l()).to_owned(),
            website: ns::numbers_a2p_website_label(l()).to_owned(),
            vertical: ns::numbers_a2p_vertical_label(l()).to_owned(),
            campaign_type: s::numbers_campaign_type(l()).to_owned(),
            campaign_description: ns::numbers_a2p_description_label(l()).to_owned(),
            sample_one: ns::numbers_a2p_sample_one_label(l()).to_owned(),
            sample_two: ns::numbers_a2p_sample_two_label(l()).to_owned(),
        },
        form: A2pFormInput {
            business_name: form.business_name.clone(),
            ein: form.ein.clone(),
            website: form.website.clone(),
            vertical: form.vertical.clone(),
            campaign_type: form.campaign_type.clone(),
            campaign_description: form.campaign_description.clone(),
            sample_one: form.sample_one.clone(),
            sample_two: form.sample_two.clone(),
        },
        // The service's own names, as the iOS app shows them: no catalog
        // holds words for them yet.
        campaign_types: A2pForm::CAMPAIGN_TYPES
            .iter()
            .map(|value| ChoiceView {
                value: (*value).to_owned(),
                label: (*value).to_owned(),
            })
            .collect(),
        submit_action: ns::numbers_a2p_submit_action(l()).to_owned(),
        can_submit: can_change
            && admin.carrier_write.is_armed()
            && admin.confirmation.is_none()
            && form.is_submittable(),
    }
}

fn toll_free_view(admin: &NumberAdmin, can_change: bool) -> TollFreeView {
    let form = &admin.toll_free_form;
    TollFreeView {
        title: ns::numbers_tfv_title(l()).to_owned(),
        caption: ns::numbers_tfv_caption(l()).to_owned(),
        labels: TollFreeLabels {
            phone_number: ns::numbers_lookup_number_label(l()).to_owned(),
            business_name: ns::numbers_a2p_business_label(l()).to_owned(),
            opt_in_image_url: ns::numbers_tfv_opt_in_label(l()).to_owned(),
            message_content: ns::numbers_tfv_message_label(l()).to_owned(),
            use_case: ns::numbers_tfv_use_case_label(l()).to_owned(),
        },
        opt_in_caption: ns::numbers_tfv_opt_in_caption(l()).to_owned(),
        form: TollFreeFormInput {
            phone_number: form.phone_number.clone(),
            business_name: form.business_name.clone(),
            opt_in_image_url: form.opt_in_image_url.clone(),
            message_content: form.message_content.clone(),
            use_case: form.use_case.clone(),
        },
        use_cases: TollFreeForm::use_cases()
            .iter()
            .map(|(value, label)| ChoiceView {
                value: (*value).to_owned(),
                label: (*label).to_owned(),
            })
            .collect(),
        submit_action: ns::numbers_tfv_submit_action(l()).to_owned(),
        can_submit: can_change
            && admin.carrier_write.is_armed()
            && admin.confirmation.is_none()
            && form.is_submittable(),
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn admin_event(action: NumberAdminAction) -> NumberAdminEvent {
        match action.event() {
            Event::Marketplace(MarketplaceEvent::Numbers(event)) => event,
            other => panic!("not a numbers event: {other:?}"),
        }
    }

    #[test]
    fn each_action_is_its_core_event() {
        let number = || "+14165550100".to_owned();
        let pairs = [
            (
                NumberAdminAction::Configure {
                    phone_number: number(),
                },
                NumberAdminEvent::Configure(number()),
            ),
            (
                NumberAdminAction::AskRelease {
                    phone_number: number(),
                },
                NumberAdminEvent::AskRelease(number()),
            ),
            (
                NumberAdminAction::EditLookup {
                    phone_number: number(),
                },
                NumberAdminEvent::EditLookup(number()),
            ),
            (NumberAdminAction::AskLookup, NumberAdminEvent::AskLookup),
            (
                NumberAdminAction::CreateSipTrunk,
                NumberAdminEvent::CreateSipTrunk,
            ),
            (
                NumberAdminAction::SetVerify { enabled: true },
                NumberAdminEvent::SetVerify(true),
            ),
            (NumberAdminAction::AskA2p, NumberAdminEvent::AskA2p),
            (
                NumberAdminAction::AskTollFree,
                NumberAdminEvent::AskTollFree,
            ),
            (NumberAdminAction::Confirm, NumberAdminEvent::Confirm),
            (
                NumberAdminAction::CancelConfirmation,
                NumberAdminEvent::CancelConfirmation,
            ),
        ];
        for (action, event) in pairs {
            assert_eq!(admin_event(action), event);
        }
        let NumberAdminEvent::EditSipTrunk(sip) = admin_event(NumberAdminAction::EditSipTrunk {
            form: SipTrunkFormInput {
                name: "a".to_owned(),
                domain: "b".to_owned(),
                ip_addresses: "c".to_owned(),
            },
        }) else {
            panic!("not the SIP form");
        };
        assert_eq!((sip.name.as_str(), sip.domain.as_str()), ("a", "b"));
        let NumberAdminEvent::EditTollFree(toll_free) =
            admin_event(NumberAdminAction::EditTollFree {
                form: TollFreeFormInput {
                    phone_number: "+18005550188".to_owned(),
                    business_name: "Acme".to_owned(),
                    opt_in_image_url: "https://example.com/a.png".to_owned(),
                    message_content: String::new(),
                    use_case: "Marketing".to_owned(),
                },
            })
        else {
            panic!("not the toll-free form");
        };
        // The fields this app leaves to the service stay blank.
        assert!(toll_free.website.is_empty() && toll_free.opt_in_flow.is_empty());
        assert_eq!(toll_free.use_case, "Marketing");
    }

    #[test]
    fn an_unprovisioned_trunk_says_so() {
        let trunk = SipTrunk {
            id: "t".to_owned(),
            name: "PBX".to_owned(),
            domain: "pbx".to_owned(),
            ip_access_control_list: Vec::new(),
            domain_sid: None,
            ip_acl_sid: None,
            status: "pending".to_owned(),
        };
        assert!(trunk_view(&trunk).unprovisioned.is_some());
    }

    #[test]
    fn a_refused_account_reads_as_refused() {
        let refused = account_view(
            "twilio",
            &ProviderStatusEntry {
                connected: false,
                account_name: Some("  ".to_owned()),
                ..ProviderStatusEntry::default()
            },
        );
        assert!(refused.refused);
        assert_eq!(refused.account_name, None);
        assert_eq!(refused.balance, None);
    }

    #[test]
    fn a_verify_sender_in_use_offers_to_stop() {
        let admin = NumberAdmin {
            verify: VerifyService::Ready { enabled: true },
            ..NumberAdmin::default()
        };
        let view = verify_view(&admin, true);
        assert_eq!(view.enabled, Some(true));
        assert_eq!(
            view.action.as_deref(),
            Some(ns::numbers_verify_disable_action(l()))
        );
    }
}
