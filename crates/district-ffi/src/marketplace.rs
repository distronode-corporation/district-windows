//! Phone numbers, read only, as District AI for Linux shows them
//! (`pages/marketplace.rs`, `ui/marketplace-page.ui`): the numbers the
//! workspace holds, and a search of the numbers for sale.
//!
//! Nothing here buys, releases or changes a number. A member whose role could
//! buy one is offered the web dashboard's marketplace instead (the core opens
//! it in the browser); a viewer is told who can. The two reads never share a
//! failure, and a workspace with no carrier connected is an account state the
//! service explains, shown without a "Try again" that cannot help.

use district_core::{
    Capabilities, Event, MarketplaceEvent, MarketplaceScreen, MarketplaceTab, Model,
    NumberSearchForm, NumberSearchState, OwnedNumbersList, Route, SignedIn, format_phone_number,
    price_label,
};
use district_model::{AvailableNumber, OwnedNumber};
use serde::Serialize;

use crate::screen::ScreenView;
use crate::views::{EmptyView, FailureView, LoadStatus, humanize};

/// Whether this version has the area's screens.
pub(crate) const BUILT: bool = true;

/// The page's heading, the navigation entry's.
pub const MARKETPLACE_TITLE: &str = "Phone numbers";
/// The line over the search's results, naming the carrier.
const OFFERED_BY: &str = "Offered by";

/// The phone numbers screen.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct MarketplaceView {
    /// The heading: "Phone numbers".
    pub title: String,
    /// The tab showing.
    pub tab: NumbersTab,
    /// The note saying the screen changes nothing, worded for the role.
    pub read_only_note: String,
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
}

impl Default for MarketplaceView {
    fn default() -> Self {
        Self {
            title: MARKETPLACE_TITLE.to_owned(),
            tab: NumbersTab::Owned,
            read_only_note: MarketplaceScreen::READ_ONLY_VIEWER.to_owned(),
            offers_web: false,
            web_action: MarketplaceScreen::WEB_ACTION.to_owned(),
            web_caption: MarketplaceScreen::WEB_CAPTION.to_owned(),
            owned_status: LoadStatus::Loading,
            owned_empty: None,
            owned: Vec::new(),
            partial_note: None,
            refreshing: false,
            form: NumberFormView::from(&NumberSearchForm::default()),
            search: NumberSearchView::Idle,
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
}

impl From<MarketplaceTab> for NumbersTab {
    fn from(tab: MarketplaceTab) -> Self {
        match tab {
            MarketplaceTab::Owned => Self::Owned,
            MarketplaceTab::Search => Self::Search,
        }
    }
}

impl From<NumbersTab> for MarketplaceTab {
    fn from(tab: NumbersTab) -> Self {
        match tab {
            NumbersTab::Owned => Self::Owned,
            NumbersTab::Search => Self::Search,
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
            number_types: NumberSearchForm::NUMBER_TYPES
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
        read_only_note: MarketplaceScreen::read_only_note(capabilities).to_owned(),
        offers_web: MarketplaceScreen::offers_web(capabilities),
        form: NumberFormView::from(&screen.form),
        search: search_view(&screen.search),
        ..MarketplaceView::default()
    };
    match &screen.owned {
        OwnedNumbersList::NotLoaded | OwnedNumbersList::Loading => {}
        OwnedNumbersList::Failed(failure) => {
            view.owned_status = LoadStatus::failed(OwnedNumbersList::FAILED_TITLE, failure);
        }
        OwnedNumbersList::Ready(held) => {
            view.owned_status = LoadStatus::Ready;
            let note = held.partial_note();
            if held.numbers.is_empty() {
                let body = note.unwrap_or_else(|| OwnedNumbersList::EMPTY_BODY.to_owned());
                view.owned_empty = Some(EmptyView::new(OwnedNumbersList::EMPTY_TITLE, &body));
            } else {
                view.partial_note = note;
            }
            view.owned = held.numbers.iter().map(owned_row).collect();
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
            NumberSearchState::NONE_TITLE,
            NumberSearchState::NONE_BODY,
            false,
        ),
        NumberSearchState::Ready { provider, numbers } => NumberSearchView::Found {
            provider_line: format!("{OFFERED_BY} {}", humanize(provider)),
            numbers: numbers.iter().map(available_row).collect(),
        },
        NumberSearchState::NotConfigured(message) => {
            status(NumberSearchState::NOT_CONFIGURED_TITLE, message, false)
        }
        NumberSearchState::Failed(failure) => {
            let failure = FailureView::from(failure);
            status(
                NumberSearchState::FAILED_TITLE,
                &failure.message,
                failure.retryable,
            )
        }
    }
}

/// A number type as it reads: the core's label, or the service's word.
fn type_label(raw: &str) -> String {
    NumberSearchForm::NUMBER_TYPES
        .iter()
        .find(|(wire, _)| *wire == raw)
        .map_or_else(|| humanize(raw), |(_, label)| (*label).to_owned())
}

/// What a number can do, as it reads: `SMS, Voice`.
fn capabilities_label(raw: &[String]) -> String {
    raw.iter()
        .map(
            |capability| match capability.to_ascii_lowercase().as_str() {
                "sms" | "mms" => capability.to_ascii_uppercase(),
                _ => humanize(capability),
            },
        )
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

/// What the carrier charges a month, as quoted.
fn monthly(amount: Option<f64>, currency: Option<&str>) -> Option<String> {
    price_label(amount, currency).map(|amount| format!("{amount} a month"))
}

fn owned_row(number: &OwnedNumber) -> NumberRowView {
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
        assert_eq!(monthly(None, Some("USD")), None);
        assert_eq!(
            monthly(Some(1.15), Some("USD")).as_deref(),
            Some("1.15 USD a month")
        );
        for tab in [NumbersTab::Owned, NumbersTab::Search] {
            assert_eq!(NumbersTab::from(MarketplaceTab::from(tab)), tab);
        }
    }
}
