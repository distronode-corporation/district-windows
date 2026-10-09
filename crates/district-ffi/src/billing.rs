//! Billing: the workspace's plan and the account's subscriptions and invoices,
//! read only as District AI for Linux shows them, and, in this app, choosing a
//! plan and managing billing on the service's own checkout and billing pages,
//! shown inside the app (district-core's `purchase` module).
//!
//! Every word here is the core's, and no price is: checkout shows the price,
//! from the service. A plan is chosen from the core's closed lists, and a
//! promotion code is taken only in the shape the core's `PromoCode` reads, so
//! nothing the member types can build a destination the service refuses. The
//! one-time links of the hand-off never reach these views: they say only that
//! a page is being opened.

use district_core::{
    AccountSection, BillingEvent, BillingScreen, Event, Model, PlanCard, PlanChoice, PlanStatus,
    PlanTerm, PlanTier, PromoCode, PurchaseSetting, PurchaseState, Renewal, Route, SignedIn,
    format_cents, invoice_amount, meter_fraction, minutes_used, overage_note, plan_name,
};
use district_model::{BillingInvoice, BillingSubscription, WorkspaceBilling};
use serde::Serialize;

use crate::screen::ScreenView;
use crate::views::{FailureView, LoadStatus, failure};

/// Whether this version has the area's screens.
pub(crate) const BUILT: bool = true;

/// The page's heading, the navigation entry's.
pub const BILLING_TITLE: &str = "Billing";

/// The billing screen.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct BillingView {
    /// The heading: "Billing".
    pub title: String,
    /// Where the plan's read stands: the screen's status page while loading
    /// or failed.
    pub status: LoadStatus,
    /// Whether the plan is being read again, with it showing.
    pub refreshing: bool,
    /// The workspace's plan, once read.
    pub plan: Option<PlanView>,
    /// The account's subscriptions and invoices.
    pub account: AccountBillingView,
    /// The note under the heading: what this screen can change.
    pub note: String,
    /// Whether "Manage billing on the web" is offered: for a role that could
    /// change the plan, while purchases in the app are not.
    pub offers_web: bool,
    /// Its label.
    pub web_action: String,
    /// Choosing a plan and managing billing in the app, while they are
    /// offered to this member on this computer; `None` hides them all.
    pub purchase: Option<PurchaseView>,
}

/// The workspace's plan.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct PlanView {
    /// The plan's name, or the line for a workspace with none.
    pub name: String,
    /// The status badge.
    pub status: String,
    /// What the badge means, when it needs saying.
    pub status_caption: Option<String>,
    /// What happens past the included minutes.
    pub overage_note: Option<String>,
    /// The call minutes metered this month, rounded, or `None` when nothing
    /// was metered (never a zero that was not measured).
    pub minutes_used: Option<i64>,
    /// The minutes the plan includes, when the processor said.
    pub included_minutes: Option<i64>,
    /// How full the minutes meter is, 0 to 100, when both are known.
    pub meter_percent: Option<u8>,
}

/// Where the account's half stands.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Serialize, uniffi::Enum)]
pub enum AccountBillingState {
    /// Being read.
    Loading,
    /// Read.
    Ready,
    /// The payment processor could not be reached: never shown as "none".
    Unavailable,
    /// The read failed.
    Failed,
}

/// The account's subscriptions and invoices.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct AccountBillingView {
    /// Where it stands.
    pub state: AccountBillingState,
    /// The heading of the unavailable or failed card.
    pub title: Option<String>,
    /// Its body: why it is unavailable.
    pub body: Option<String>,
    /// Why the read failed.
    pub failure: Option<FailureView>,
    /// The subscriptions.
    pub subscriptions: Vec<SubscriptionRowView>,
    /// The line for an account with none.
    pub no_subscriptions: Option<String>,
    /// The latest invoices.
    pub invoices: Vec<InvoiceRowView>,
    /// The line for an account with none.
    pub no_invoices: Option<String>,
    /// The note under a list that is not all of them.
    pub invoices_truncated: Option<String>,
}

/// One subscription.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct SubscriptionRowView {
    /// The processor's id.
    pub id: String,
    /// The plan's name.
    pub name: String,
    /// The processor's word for its state.
    pub status: String,
    /// The amount per period ("$249.00"), when the processor sent one.
    pub amount: Option<String>,
    /// When it renews, in Unix seconds.
    pub renews_at: Option<i64>,
    /// When it ends instead, in Unix seconds.
    pub ends_at: Option<i64>,
    /// A coupon applied to it, by name.
    pub coupon: Option<String>,
}

/// One invoice.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct InvoiceRowView {
    /// The processor's id, to open it by.
    pub id: String,
    /// What it cost ("$249.00").
    pub amount: String,
    /// Its state ("paid"), or empty.
    pub status: String,
    /// When it was made, in Unix seconds.
    pub created: i64,
    /// Whether it has a page to open.
    pub can_open: bool,
}

/// A plan the app offers.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Serialize, uniffi::Enum)]
pub enum PlanTierView {
    /// Voice Solo.
    VoiceSolo,
    /// Voice Starter.
    VoiceStarter,
    /// Voice Pro.
    VoicePro,
    /// Voice Studio.
    VoiceStudio,
}

impl From<PlanTier> for PlanTierView {
    fn from(tier: PlanTier) -> Self {
        match tier {
            PlanTier::VoiceSolo => Self::VoiceSolo,
            PlanTier::VoiceStarter => Self::VoiceStarter,
            PlanTier::VoicePro => Self::VoicePro,
            PlanTier::VoiceStudio => Self::VoiceStudio,
        }
    }
}

impl From<PlanTierView> for PlanTier {
    fn from(tier: PlanTierView) -> Self {
        match tier {
            PlanTierView::VoiceSolo => Self::VoiceSolo,
            PlanTierView::VoiceStarter => Self::VoiceStarter,
            PlanTierView::VoicePro => Self::VoicePro,
            PlanTierView::VoiceStudio => Self::VoiceStudio,
        }
    }
}

/// How often a plan is billed.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Serialize, uniffi::Enum)]
pub enum PlanTermView {
    /// Every month.
    Monthly,
    /// A year up front.
    Annual,
}

impl From<PlanTerm> for PlanTermView {
    fn from(term: PlanTerm) -> Self {
        match term {
            PlanTerm::Monthly => Self::Monthly,
            PlanTerm::Annual => Self::Annual,
        }
    }
}

impl From<PlanTermView> for PlanTerm {
    fn from(term: PlanTermView) -> Self {
        match term {
            PlanTermView::Monthly => Self::Monthly,
            PlanTermView::Annual => Self::Annual,
        }
    }
}

/// One plan in the chooser.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct PlanOptionView {
    /// The plan.
    pub tier: PlanTierView,
    /// Its name.
    pub label: String,
}

/// One term in the chooser.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct TermOptionView {
    /// The term.
    pub term: PlanTermView,
    /// Its name.
    pub label: String,
}

/// The step before checkout opens, which names Stripe.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct ConfirmPurchaseView {
    /// The heading.
    pub title: String,
    /// The body: who handles payment, and where checkout opens.
    pub body: String,
    /// The plan chosen, by name.
    pub plan: String,
    /// The term chosen, by name.
    pub term: String,
    /// The promotion code, when one was given.
    pub promo: Option<String>,
    /// The confirming button's label.
    pub action: String,
    /// The way back's label.
    pub cancel: String,
}

/// Choosing a plan and managing billing in the app.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct PurchaseView {
    /// The plans, smallest first.
    pub plans: Vec<PlanOptionView>,
    /// The terms, monthly first.
    pub terms: Vec<TermOptionView>,
    /// The chooser's action: "Choose a plan".
    pub choose_action: String,
    /// The line beside the plans: the price is shown at checkout.
    pub prices_note: String,
    /// The promotion code field's label.
    pub promo_label: String,
    /// What to say about a code of the wrong shape.
    pub promo_invalid: String,
    /// The longest code taken.
    pub promo_max_len: u32,
    /// Whether "Manage billing" is offered.
    pub offers_manage: bool,
    /// Its label.
    pub manage_action: String,
    /// The step naming Stripe, while it shows.
    pub confirming: Option<ConfirmPurchaseView>,
    /// Whether a page is being opened (show progress).
    pub opening: bool,
    /// What to say meanwhile.
    pub opening_label: String,
    /// Whether the actions can be pressed: not while the link is being asked
    /// for.
    pub enabled: bool,
    /// What the last press came to, when it needs saying.
    pub notice: Option<FailureView>,
    /// Said once purchase pages open in the system browser, because this
    /// computer cannot show them in the app.
    pub in_browser_note: Option<String>,
}

/// Something the member did on the billing screen, or about purchases.
#[derive(Clone, Debug, PartialEq, Eq, uniffi::Enum)]
pub enum BillingAction {
    /// Open billing.
    Open,
    /// Manage billing on the web dashboard, in the browser.
    ManageOnWeb,
    /// Open an invoice's page in the browser.
    OpenInvoice {
        /// Which.
        invoice_id: String,
    },
    /// Read the plan again ("Try again").
    CheckAgain,
    /// Choose a plan: the step naming Stripe shows. A promotion code that is
    /// not of the shape the service reads sends nothing (the page says why
    /// before it lets the member press).
    ChoosePlan {
        /// The plan.
        tier: PlanTierView,
        /// How often it is billed.
        term: PlanTermView,
        /// The promotion code as typed, or `None` (an empty one is none).
        promo: Option<String>,
    },
    /// Continue to checkout.
    ConfirmPurchase,
    /// Back out of the confirmation step.
    CancelPurchase,
    /// Manage billing inside the app.
    ManageInApp,
    /// Put the purchase notice away.
    DismissPurchaseNotice,
    /// The checkout window closed.
    CheckoutClosed,
    /// The Account page's "Purchases on this computer": on ("Sign in every
    /// time") or off.
    SetPurchases {
        /// On, or off.
        on: bool,
    },
}

/// The core events `action` is, in the order the core is to hear them.
pub(crate) fn events(action: BillingAction) -> Vec<Event> {
    let event = match action {
        BillingAction::Open => Event::Navigate(Route::Billing),
        BillingAction::ManageOnWeb => Event::Billing(BillingEvent::ManageOnWeb),
        BillingAction::OpenInvoice { invoice_id } => {
            Event::Billing(BillingEvent::OpenInvoice { invoice_id })
        }
        BillingAction::CheckAgain => Event::Refresh,
        BillingAction::ChoosePlan { tier, term, promo } => {
            let Some(choice) = plan_choice(tier, term, promo.as_deref()) else {
                return Vec::new();
            };
            Event::Billing(BillingEvent::ChoosePlan(choice))
        }
        BillingAction::ConfirmPurchase => Event::Billing(BillingEvent::ConfirmPurchase),
        BillingAction::CancelPurchase => Event::Billing(BillingEvent::CancelPurchase),
        BillingAction::ManageInApp => Event::Billing(BillingEvent::ManageInApp),
        BillingAction::DismissPurchaseNotice => Event::Billing(BillingEvent::DismissPurchaseNotice),
        BillingAction::CheckoutClosed => Event::EmbeddedClosed,
        BillingAction::SetPurchases { on } => Event::SetPurchases(if on {
            PurchaseSetting::SignInEveryTime
        } else {
            PurchaseSetting::Off
        }),
    };
    vec![event]
}

/// The plan chosen, or `None` for a promotion code the core does not take.
fn plan_choice(tier: PlanTierView, term: PlanTermView, promo: Option<&str>) -> Option<PlanChoice> {
    let promo = match promo.map(str::trim).filter(|typed| !typed.is_empty()) {
        None => None,
        Some(typed) => Some(PromoCode::parse(typed)?),
    };
    Some(PlanChoice {
        tier: tier.into(),
        term: term.into(),
        promo,
    })
}

/// The page of billing, for a signed-in model.
pub(crate) fn screen(_model: &Model, signed_in: &SignedIn) -> ScreenView {
    ScreenView::Billing {
        view: page_of(signed_in),
    }
}

fn page_of(signed_in: &SignedIn) -> BillingView {
    let screen = &signed_in.billing;
    let capabilities = signed_in.capabilities();
    let purchase = purchase_view(signed_in);
    let (status, refreshing, plan) = match &screen.plan {
        PlanCard::NotLoaded | PlanCard::Loading => (LoadStatus::Loading, false, None),
        PlanCard::Failed(failure) => (
            LoadStatus::failed(PlanCard::FAILED_TITLE, failure),
            false,
            None,
        ),
        PlanCard::Ready {
            billing,
            refreshing,
        } => (
            LoadStatus::Ready,
            *refreshing,
            Some(plan_view(billing, screen.included_minutes())),
        ),
    };
    BillingView {
        title: BILLING_TITLE.to_owned(),
        status,
        refreshing,
        plan,
        account: section_view(&screen.account),
        note: if purchase.is_some() {
            PurchaseState::IN_APP_NOTE
        } else {
            BillingScreen::read_only_note(&capabilities)
        }
        .to_owned(),
        offers_web: purchase.is_none() && BillingScreen::offers_web(&capabilities),
        web_action: BillingScreen::WEB_ACTION.to_owned(),
        purchase,
    }
}

fn plan_view(billing: &WorkspaceBilling, included: Option<i64>) -> PlanView {
    let status = PlanStatus::of(billing);
    let used = minutes_used(billing);
    #[allow(clippy::cast_possible_truncation, clippy::cast_sign_loss)]
    let meter_percent = used
        .zip(included)
        .map(|(used, included)| (meter_fraction(used, included) * 100.0).round() as u8);
    #[allow(clippy::cast_possible_truncation)]
    let minutes_used = used.map(|used| used.round() as i64);
    PlanView {
        name: plan_name(billing).to_owned(),
        status: status.label().to_owned(),
        status_caption: status.caption().map(str::to_owned),
        overage_note: overage_note(billing).map(str::to_owned),
        minutes_used,
        included_minutes: included,
        meter_percent,
    }
}

fn section_view(section: &AccountSection) -> AccountBillingView {
    let mut view = AccountBillingView {
        state: AccountBillingState::Loading,
        title: None,
        body: None,
        failure: None,
        subscriptions: Vec::new(),
        no_subscriptions: None,
        invoices: Vec::new(),
        no_invoices: None,
        invoices_truncated: None,
    };
    match section {
        AccountSection::NotLoaded | AccountSection::Loading => {}
        AccountSection::Unavailable => {
            view.state = AccountBillingState::Unavailable;
            view.title = Some(AccountSection::UNAVAILABLE_TITLE.to_owned());
            view.body = Some(AccountSection::UNAVAILABLE_BODY.to_owned());
        }
        AccountSection::Failed(failure) => {
            view.state = AccountBillingState::Failed;
            view.title = Some(AccountSection::FAILED_TITLE.to_owned());
            view.failure = Some(failure.into());
        }
        AccountSection::Ready(account) => {
            view.state = AccountBillingState::Ready;
            view.subscriptions = account.subscriptions.iter().map(subscription_row).collect();
            view.no_subscriptions = account
                .subscriptions
                .is_empty()
                .then(|| AccountSection::NO_SUBSCRIPTION.to_owned());
            view.invoices = account.invoices.iter().map(invoice_row).collect();
            view.no_invoices = account
                .invoices
                .is_empty()
                .then(|| AccountSection::NO_INVOICES.to_owned());
            view.invoices_truncated = (account.invoices_has_more == Some(true))
                .then(|| AccountSection::INVOICES_TRUNCATED.to_owned());
        }
    }
    view
}

fn subscription_row(subscription: &BillingSubscription) -> SubscriptionRowView {
    let renewal = Renewal::of(subscription);
    SubscriptionRowView {
        id: subscription.id.clone(),
        name: subscription.tier_name.clone(),
        status: subscription.status.clone(),
        amount: subscription.amount.map(format_cents),
        renews_at: match renewal {
            Some(Renewal::Renews(at)) => Some(at),
            _ => None,
        },
        ends_at: match renewal {
            Some(Renewal::Ends(at)) => Some(at),
            _ => None,
        },
        coupon: subscription
            .discount
            .as_ref()
            .map(|discount| discount.coupon_name.clone()),
    }
}

fn invoice_row(invoice: &BillingInvoice) -> InvoiceRowView {
    InvoiceRowView {
        id: invoice.id.clone(),
        amount: format_cents(invoice_amount(invoice)),
        status: invoice.status.clone().unwrap_or_default(),
        created: invoice.created,
        can_open: invoice.hosted_invoice_url.is_some(),
    }
}

/// The purchase controls, while the core offers them to this member here.
fn purchase_view(signed_in: &SignedIn) -> Option<PurchaseView> {
    if !signed_in.offers_plans() {
        return None;
    }
    let state = &signed_in.purchase;
    Some(PurchaseView {
        plans: PlanTier::ALL
            .into_iter()
            .map(|tier| PlanOptionView {
                tier: tier.into(),
                label: tier.label().to_owned(),
            })
            .collect(),
        terms: PlanTerm::ALL
            .into_iter()
            .map(|term| TermOptionView {
                term: term.into(),
                label: term.label().to_owned(),
            })
            .collect(),
        choose_action: PurchaseState::CHOOSE_PLAN_ACTION.to_owned(),
        prices_note: PurchaseState::PRICES_NOTE.to_owned(),
        promo_label: PromoCode::LABEL.to_owned(),
        promo_invalid: PromoCode::INVALID.to_owned(),
        promo_max_len: u32::try_from(PromoCode::MAX_LEN).unwrap_or(u32::MAX),
        offers_manage: signed_in.offers_manage_in_app(),
        manage_action: PurchaseState::MANAGE_ACTION.to_owned(),
        confirming: state.confirming.as_ref().map(|choice| ConfirmPurchaseView {
            title: PurchaseState::CONFIRM_TITLE.to_owned(),
            body: state.confirm_body().to_owned(),
            plan: choice.tier.label().to_owned(),
            term: choice.term.label().to_owned(),
            promo: choice.promo.as_ref().map(|promo| promo.as_str().to_owned()),
            action: PurchaseState::CONFIRM_ACTION.to_owned(),
            cancel: PurchaseState::CANCEL_ACTION.to_owned(),
        }),
        opening: state.opening(),
        opening_label: PurchaseState::OPENING.to_owned(),
        enabled: !state.minting(),
        notice: failure(state.notice.as_ref()),
        in_browser_note: state
            .in_browser
            .then(|| PurchaseState::IN_BROWSER.to_owned()),
    })
}

/// A billing screen with every part filled in, for the trip through UniFFI's
/// converters (screen.rs).
#[cfg(test)]
pub(crate) fn sample() -> BillingView {
    BillingView {
        title: BILLING_TITLE.to_owned(),
        status: LoadStatus::Ready,
        refreshing: true,
        plan: Some(PlanView {
            name: "VoicePro".to_owned(),
            status: "Active".to_owned(),
            status_caption: Some("caption".to_owned()),
            overage_note: Some("note".to_owned()),
            minutes_used: Some(312),
            included_minutes: Some(1500),
            meter_percent: Some(21),
        }),
        account: AccountBillingView {
            state: AccountBillingState::Ready,
            title: None,
            body: None,
            failure: None,
            subscriptions: vec![SubscriptionRowView {
                id: "sub_1".to_owned(),
                name: "Voice Pro".to_owned(),
                status: "active".to_owned(),
                amount: Some("$249.00".to_owned()),
                renews_at: Some(1_790_000_000),
                ends_at: None,
                coupon: Some("LAUNCH".to_owned()),
            }],
            no_subscriptions: None,
            invoices: vec![InvoiceRowView {
                id: "in_1".to_owned(),
                amount: "$249.00".to_owned(),
                status: "paid".to_owned(),
                created: 1_780_000_000,
                can_open: true,
            }],
            no_invoices: None,
            invoices_truncated: Some("older".to_owned()),
        },
        note: PurchaseState::IN_APP_NOTE.to_owned(),
        offers_web: false,
        web_action: BillingScreen::WEB_ACTION.to_owned(),
        purchase: Some(PurchaseView {
            plans: vec![PlanOptionView {
                tier: PlanTierView::VoicePro,
                label: "Voice Pro".to_owned(),
            }],
            terms: vec![TermOptionView {
                term: PlanTermView::Annual,
                label: "Annual".to_owned(),
            }],
            choose_action: PurchaseState::CHOOSE_PLAN_ACTION.to_owned(),
            prices_note: PurchaseState::PRICES_NOTE.to_owned(),
            promo_label: PromoCode::LABEL.to_owned(),
            promo_invalid: PromoCode::INVALID.to_owned(),
            promo_max_len: 40,
            offers_manage: true,
            manage_action: PurchaseState::MANAGE_ACTION.to_owned(),
            confirming: Some(ConfirmPurchaseView {
                title: PurchaseState::CONFIRM_TITLE.to_owned(),
                body: PurchaseState::CONFIRM_BODY.to_owned(),
                plan: "Voice Pro".to_owned(),
                term: "Annual".to_owned(),
                promo: Some("SAVE".to_owned()),
                action: PurchaseState::CONFIRM_ACTION.to_owned(),
                cancel: PurchaseState::CANCEL_ACTION.to_owned(),
            }),
            opening: true,
            opening_label: PurchaseState::OPENING.to_owned(),
            enabled: false,
            notice: None,
            in_browser_note: Some(PurchaseState::IN_BROWSER.to_owned()),
        }),
    }
}
