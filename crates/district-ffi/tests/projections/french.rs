//! The values the core writes the French way since district-core-rust 4.0.0
//! (a call's length, a price in its currency, a chart's days), as they reach
//! the screens. The snapshots pin the English, which these leave unchanged.

use district_l10n::{Locale, with_locale};

use super::*;

/// The JSON of case `name`'s screen, rendered in `locale`, with each
/// no-break space read as a space.
fn screen_in(locale: Locale, name: &str) -> String {
    let (_, session) = cases()
        .into_iter()
        .find(|(case, _)| *case == name)
        .unwrap_or_else(|| panic!("no case {name}"));
    with_locale(locale, || {
        serde_json::to_string(&screen_view(&session.model))
    })
    .unwrap()
    .replace('\u{a0}', " ")
}

#[test]
fn a_call_row_gives_its_length_the_french_way() {
    let french = screen_in(Locale::FrCa, "calls-loaded");
    assert!(french.contains("1 min 5 s"), "{french}");
    assert!(french.contains("2 min 34 s"), "{french}");
    let english = screen_in(Locale::En, "calls-loaded");
    assert!(english.contains("1m 5s") && !english.contains("1 min 5 s"));
}

#[test]
fn a_subscription_and_an_invoice_name_their_currency_in_french() {
    let french = screen_in(Locale::FrCa, "billing-ready");
    // The fixture's subscription and paid invoice are 249 US dollars.
    assert_eq!(french.matches("\"249,00 $ US\"").count(), 2, "{french}");
    assert_eq!(french.matches("\"15,00 $ US\"").count(), 2, "{french}");
    let english = screen_in(Locale::En, "billing-ready");
    assert_eq!(english.matches("\"$249.00\"").count(), 2, "{english}");
}

#[test]
fn the_calls_per_day_chart_names_its_days_in_french() {
    let french = screen_in(Locale::FrCa, "analytics-loaded");
    assert!(french.contains("\"axis_start\":\"9 août\""), "{french}");
    assert!(french.contains("\"axis_end\":\"15 août\""), "{french}");
    let english = screen_in(Locale::En, "analytics-loaded");
    assert!(english.contains("\"axis_start\":\"Aug 9\""), "{english}");
}

/// A support request's status is the core's label in the app's language,
/// in the list and on the request, never the desk's own word.
#[test]
fn a_support_requests_status_is_labelled_in_french() {
    let french = screen_in(Locale::FrCa, "support-loaded");
    assert!(french.contains("\"status\":\"En cours\""), "{french}");
    assert!(french.contains("\"status\":\"Terminée\""), "{french}");
    assert!(!french.contains("In Progress"), "{french}");
    let english = screen_in(Locale::En, "support-loaded");
    assert!(english.contains("\"status\":\"In progress\""), "{english}");
    assert!(!english.contains("In Progress"), "{english}");

    let french = screen_in(Locale::FrCa, "support-request-loaded");
    assert!(french.contains("\"status_name\":\"En cours\""), "{french}");
    let english = screen_in(Locale::En, "support-request-loaded");
    assert!(
        english.contains("\"status_name\":\"In progress\""),
        "{english}"
    );
}
