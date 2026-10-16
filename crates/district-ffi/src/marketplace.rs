//! Phone numbers, as District AI for Linux shows them (`pages/marketplace.rs`,
//! `ui/marketplace-page.ui`): the numbers the workspace holds, a search of the
//! numbers for sale, and the carrier tab. Managing a held number and the
//! carrier account is crate::numbers'.
//!
//! Nothing here buys a number: a member whose role could buy one is offered
//! the web dashboard's marketplace instead (the core opens it in the browser);
//! a viewer is told who can change numbers. The two reads never share a
//! failure, and a workspace with no carrier connected is an account state the
//! service explains, shown without a "Try again" that cannot help.

use district_core::{
    Capabilities, Event, MarketplaceEvent, MarketplaceScreen, MarketplaceTab, Model, NumberAdmin,
    NumberSearchForm, NumberSearchState, OwnedNumbersList, Route, SignedIn, format_phone_number,
    numbers_scope_note,
};
use district_l10n::{Locale, WireCategory};
use district_model::{AvailableNumber, OwnedNumber};
use serde::Serialize;

use crate::l10n::{cs, l, s};
use crate::numbers::{
    NumberAdminAction, NumberAdminView, NumberManageView, admin_view, manage_view,
};
use crate::screen::ScreenView;
use crate::views::{EmptyView, FailureView, LoadStatus, humanize, wire};

/// Whether this version has the area's screens.
pub(crate) const BUILT: bool = true;

/// The phone numbers screen.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct MarketplaceView {
    /// The heading: "Phone numbers".
    pub title: String,
    /// The tab showing.
    pub tab: NumbersTab,
    /// The note saying what the screen does and does not do (buying is on
    /// the web), worded for the role.
    pub scope_note: String,
    /// Whether to offer the web marketplace: only to a role that could buy
    /// there.
    pub offers_web: bool,
    /// The web marketplace link's words.
    pub web_action: String,
    /// The caption under it.
    pub web_caption: String,
    /// Where the read of the held numbers stands.
    pub owned_status: LoadStatus,
    /// What to say when the workspace holds none.
    pub owned_empty: Option<EmptyView>,
    /// The numbers the workspace holds.
    pub owned: Vec<NumberRowView>,
    /// A note that a carrier did not answer, so the list may be short.
    pub partial_note: Option<String>,
    /// Whether the held numbers are being read again, with these showing.
    pub refreshing: bool,
    /// The search's filters, as typed.
    pub form: NumberFormView,
    /// Where the search stands.
    pub search: NumberSearchView,
    /// Managing the numbers held and the carrier account.
    pub admin: NumberAdminView,
}

impl Default for MarketplaceView {
    fn default() -> Self {
        Self {
            title: s::marketplace_title(l()).to_owned(),
            tab: NumbersTab::Owned,
            scope_note: numbers_scope_note(false).to_owned(),
            offers_web: false,
            web_action: cs::marketplace_web_action(l()).to_owned(),
            web_caption: cs::marketplace_web_caption(l()).to_owned(),
            owned_status: LoadStatus::Loading,
            owned_empty: None,
            owned: Vec::new(),
            partial_note: None,
            refreshing: false,
            form: NumberFormView::from(&NumberSearchForm::default()),
            search: NumberSearchView::Idle,
            admin: admin_view(&NumberAdmin::default(), false, false),
        }
    }
}

/// The screen's two tabs.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Serialize, uniffi::Enum)]
pub enum NumbersTab {
    /// The numbers the workspace holds.
    Owned,
    /// The numbers for sale.
    Search,
    /// The carrier accounts, SIP trunks, one-time passcodes and messaging
    /// registrations.
    Carrier,
}

impl From<MarketplaceTab> for NumbersTab {
    fn from(tab: MarketplaceTab) -> Self {
        match tab {
            MarketplaceTab::Owned => Self::Owned,
            MarketplaceTab::Search => Self::Search,
            MarketplaceTab::Carrier => Self::Carrier,
        }
    }
}

impl From<NumbersTab> for MarketplaceTab {
    fn from(tab: NumbersTab) -> Self {
        match tab {
            NumbersTab::Owned => Self::Owned,
            NumbersTab::Search => Self::Search,
            NumbersTab::Carrier => Self::Carrier,
        }
    }
}

/// A number in a list.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct NumberRowView {
    /// The number, grouped to read.
    pub number: String,
    /// The line under it: its name, type, carrier and what it can do, or
    /// where it is, its type and what it can do.
    pub line: String,
    /// What the carrier charges a month, as quoted, or `None` when it quoted
    /// nothing (never shown as zero).
    pub monthly: Option<String>,
    /// What can be done to a number the workspace holds; `None` for one for
    /// sale.
    pub manage: Option<NumberManageView>,
}

/// The search's filters.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct NumberFormView {
    /// An area code, or blank for any.
    pub area_code: String,
    /// A country code.
    pub country: String,
    /// The number type, as the service names it (`local`).
    pub number_type: String,
    /// The number types to choose from, in the core's order.
    pub number_types: Vec<NumberTypeView>,
}

impl From<&NumberSearchForm> for NumberFormView {
    fn from(form: &NumberSearchForm) -> Self {
        Self {
            area_code: form.area_code.clone(),
            country: form.country.clone(),
            number_type: form.number_type.clone(),
            number_types: NumberSearchForm::number_types()
                .iter()
                .map(|(value, label)| NumberTypeView {
                    value: (*value).to_owned(),
                    label: (*label).to_owned(),
                })
                .collect(),
        }
    }
}

/// One number type.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct NumberTypeView {
    /// As the service names it.
    pub value: String,
    /// As it reads.
    pub label: String,
}

/// Where the number search stands.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Enum)]
pub enum NumberSearchView {
    /// Nothing searched yet: not "no results".
    Idle,
    /// Waiting for the typing to stop, or on its way.
    Searching,
    /// The carrier's answer, with at least one number.
    Found {
        /// "Offered by" the carrier.
        provider_line: String,
        /// The numbers for sale.
        numbers: Vec<NumberRowView>,
    },
    /// Nothing, or no carrier connected, or a failure: a status with a
    /// heading, and "Try again" only where it may help.
    Status {
        /// The heading.
        title: String,
        /// The text under it.
        body: String,
        /// Whether to offer "Try again".
        retry: bool,
    },
}

/// Something the member did on the phone numbers screen.
#[derive(Clone, Debug, PartialEq, Eq, uniffi::Enum)]
pub enum MarketplaceAction {
    /// Open the phone numbers.
    Open,
    /// Open the phone numbers on the web dashboard.
    OpenWeb,
    /// Show this tab.
    SelectTab {
        /// The tab.
        tab: NumbersTab,
    },
    /// The search's filters changed: the core searches once the typing stops.
    EditSearch {
        /// An area code, or blank.
        area_code: String,
        /// A country code.
        country: String,
        /// The number type, as the service names it.
        number_type: String,
    },
    /// Search now (or again).
    Search,
    /// Manage a held number or the carrier account.
    Numbers {
        /// What was done.
        action: NumberAdminAction,
    },
}

/// The core events `action` is, in the order the core is to hear them.
pub(crate) fn events(action: MarketplaceAction) -> Vec<Event> {
    vec![match action {
        MarketplaceAction::Open => Event::Navigate(Route::Marketplace),
        MarketplaceAction::OpenWeb => Event::Marketplace(MarketplaceEvent::OpenWeb),
        MarketplaceAction::SelectTab { tab } => {
            Event::Marketplace(MarketplaceEvent::SelectTab(tab.into()))
        }
        MarketplaceAction::EditSearch {
            area_code,
            country,
            number_type,
        } => Event::Marketplace(MarketplaceEvent::EditSearch(NumberSearchForm {
            area_code,
            country,
            number_type,
        })),
        MarketplaceAction::Search => Event::Marketplace(MarketplaceEvent::Search),
        MarketplaceAction::Numbers { action } => action.event(),
    }]
}

/// The page of the phone numbers, for a signed-in model.
pub(crate) fn screen(_model: &Model, signed_in: &SignedIn) -> ScreenView {
    ScreenView::Marketplace {
        view: marketplace_view(&signed_in.marketplace, &signed_in.capabilities()),
    }
}

fn marketplace_view(screen: &MarketplaceScreen, capabilities: &Capabilities) -> MarketplaceView {
    let mut view = MarketplaceView {
        tab: screen.tab.into(),
        scope_note: MarketplaceScreen::scope_note(capabilities).to_owned(),
        offers_web: MarketplaceScreen::offers_web(capabilities),
        form: NumberFormView::from(&screen.form),
        search: search_view(&screen.search),
        admin: admin_view(
            &screen.admin,
            capabilities.can_change,
            screen.tab == MarketplaceTab::Carrier,
        ),
        ..MarketplaceView::default()
    };
    match &screen.owned {
        OwnedNumbersList::NotLoaded | OwnedNumbersList::Loading => {}
        OwnedNumbersList::Failed(failure) => {
            view.owned_status =
                LoadStatus::failed(cs::marketplace_owned_failed_title(l()), failure);
        }
        OwnedNumbersList::Ready(held) => {
            view.owned_status = LoadStatus::Ready;
            let note = held.partial_note();
            if held.numbers.is_empty() {
                let body = note.unwrap_or_else(|| cs::marketplace_owned_empty_body(l()).to_owned());
                view.owned_empty = Some(EmptyView::new(
                    cs::marketplace_owned_empty_title(l()),
                    &body,
                ));
            } else {
                view.partial_note = note;
            }
            view.owned = held
                .numbers
                .iter()
                .map(|number| owned_row(number, &screen.admin, capabilities.can_change))
                .collect();
            view.refreshing = held.refreshing;
        }
    }
    view
}

fn search_view(search: &NumberSearchState) -> NumberSearchView {
    let status = |title: &str, body: &str, retry: bool| NumberSearchView::Status {
        title: title.to_owned(),
        body: body.to_owned(),
        retry,
    };
    match search {
        NumberSearchState::Idle => NumberSearchView::Idle,
        NumberSearchState::Searching => NumberSearchView::Searching,
        NumberSearchState::Ready { numbers, .. } if numbers.is_empty() => status(
            cs::marketplace_search_none_title(l()),
            cs::marketplace_search_none_body(l()),
            false,
        ),
        NumberSearchState::Ready { provider, numbers } => NumberSearchView::Found {
            provider_line: s::marketplace_offered_by(l(), &humanize(provider)),
            numbers: numbers.iter().map(available_row).collect(),
        },
        NumberSearchState::NotConfigured(message) => {
            status(cs::marketplace_not_configured_title(l()), message, false)
        }
        NumberSearchState::Failed(failure) => {
            let failure = FailureView::from(failure);
            status(
                cs::marketplace_search_failed_title(l()),
                &failure.message,
                failure.retryable,
            )
        }
    }
}

/// A number type as it reads: the core's label, or the service's word.
fn type_label(raw: &str) -> String {
    NumberSearchForm::number_types()
        .iter()
        .find(|(wire, _)| *wire == raw)
        .map_or_else(|| humanize(raw), |(_, label)| (*label).to_owned())
}

/// What a number can do, as it reads: `SMS, Voice` (`Texto, Voix`), each
/// capability the core's label in its language. English keeps the two message
/// kinds as their acronyms, where the vocabulary's English reads `Sms`.
fn capabilities_label(raw: &[String]) -> String {
    raw.iter()
        .map(|capability| {
            match (
                district_l10n::current(),
                capability.to_ascii_lowercase().as_str(),
            ) {
                (Locale::En, "sms" | "mms") => capability.to_ascii_uppercase(),
                (_, lower) => wire(WireCategory::NumberCapability, lower),
            }
        })
        .collect::<Vec<_>>()
        .join(", ")
}

/// `parts` that say something, on one line.
fn line(parts: &[Option<String>], separator: &str) -> String {
    parts
        .iter()
        .flatten()
        .map(|part| part.trim())
        .filter(|part| !part.is_empty())
        .collect::<Vec<_>>()
        .join(separator)
}

/// What the carrier charges a month, as money is written everywhere else in
/// the app (the core's `format::price`, as Billing's prices): two decimals,
/// in the currency the carrier named (`$1.15`, `1,15 $ US`, `€2.50`), or, when
/// it named none, as a dollar amount (`$1.15`, `1,15 $`). `None` when it quoted
/// no price: a missing price is not zero.
fn monthly(amount: Option<f64>, currency: Option<&str>) -> Option<String> {
    // A monthly price in major units; the service quotes it to the cent.
    let cents = (amount? * 100.0).round() as i64;
    let currency = currency.map(str::trim).filter(|code| !code.is_empty());
    let price = district_l10n::format::price(l(), cents, currency);
    Some(s::marketplace_monthly(l(), &price))
}

fn owned_row(number: &OwnedNumber, admin: &NumberAdmin, can_change: bool) -> NumberRowView {
    NumberRowView {
        number: format_phone_number(&number.phone_number),
        line: line(
            &[
                number.friendly_name.clone(),
                Some(type_label(&number.number_type)),
                Some(humanize(&number.provider)),
                Some(capabilities_label(&number.capabilities)),
            ],
            " \u{b7} ",
        ),
        monthly: monthly(number.monthly_price, None),
        manage: Some(manage_view(number, admin, can_change)),
    }
}

fn available_row(number: &AvailableNumber) -> NumberRowView {
    let place = line(&[number.locality.clone(), number.region.clone()], ", ");
    NumberRowView {
        number: format_phone_number(&number.phone_number),
        line: line(
            &[
                Some(place),
                Some(type_label(&number.number_type)),
                Some(capabilities_label(&number.capabilities)),
            ],
            " \u{b7} ",
        ),
        monthly: monthly(number.monthly_price, number.currency.as_deref()),
        manage: None,
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn labels_read_as_the_linux_app_words_them() {
        assert_eq!(type_label("tollFree"), "Toll-free");
        assert_eq!(type_label("shared_short_code"), "Shared short code");
        assert_eq!(
            capabilities_label(&["sms".to_owned(), "voice".to_owned(), "MMS".to_owned()]),
            "SMS, Voice, MMS"
        );
        assert_eq!(
            district_l10n::with_locale(Locale::FrCa, || capabilities_label(&[
                "sms".to_owned(),
                "voice".to_owned(),
                "MMS".to_owned()
            ])),
            "Texto, Voix, MMS"
        );
        assert_eq!(monthly(None, Some("USD")), None);
        let both = |amount: f64, currency: Option<&str>| {
            (
                monthly(Some(amount), currency).unwrap(),
                district_l10n::with_locale(Locale::FrCa, || {
                    monthly(Some(amount), currency).unwrap()
                }),
            )
        };
        // Two decimals always, in the currency the carrier named.
        assert_eq!(
            both(1.15, Some("USD")),
            (
                "$1.15 a month".to_owned(),
                "1,15\u{a0}$\u{a0}US par mois".to_owned()
            )
        );
        assert_eq!(
            both(2.5, Some(" eur ")),
            (
                "\u{20ac}2.50 a month".to_owned(),
                "2,50\u{a0}\u{20ac} par mois".to_owned()
            )
        );
        // A held number's price names no currency: the dollar, as Billing.
        assert_eq!(
            both(2.5, None),
            (
                "$2.50 a month".to_owned(),
                "2,50\u{a0}$ par mois".to_owned()
            )
        );
        assert_eq!(both(1.0, Some("")).0, "$1.00 a month");
        for tab in [NumbersTab::Owned, NumbersTab::Search, NumbersTab::Carrier] {
            assert_eq!(NumbersTab::from(MarketplaceTab::from(tab)), tab);
        }
    }
}
