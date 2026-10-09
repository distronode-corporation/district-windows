//! Phone numbers (src/marketplace.rs).

use district_core::{MarketplaceEvent, MarketplaceTab, NumberSearchForm};
use district_ffi::marketplace::{MarketplaceAction, MarketplaceView, NumberSearchView, NumbersTab};

use super::*;

/// This area's snapshot cases.
pub(crate) fn cases() -> Vec<Case> {
    vec![
        ("numbers-loading", opened(signed_in())),
        (
            "numbers-failed",
            owned(opened(signed_in()), Err(server_error())),
        ),
        ("numbers-loaded", held(signed_in())),
        (
            "numbers-partial",
            owned(
                opened(signed_in()),
                Ok(contracts::json("district-provider-numbers-partial.json")),
            ),
        ),
        (
            "numbers-empty",
            owned(
                opened(signed_in()),
                Ok(
                    json!({"success": true, "numbers": [], "partial": false, "failedProviders": []}),
                ),
            ),
        ),
        ("numbers-refreshing", held(signed_in()).ui(UiEvent::Refresh)),
        ("numbers-viewer", held_by(viewer())),
        ("numbers-search-idle", search_tab(held(signed_in()))),
        (
            "numbers-search-typing",
            typed(search_tab(held(signed_in()))),
        ),
        (
            "numbers-search-found",
            found(
                searching(signed_in()),
                Ok(contracts::json("district-numbers-search.json")),
            ),
        ),
        (
            "numbers-search-none",
            found(
                searching(signed_in()),
                Ok(json!({"success": true, "provider": "twilio", "numbers": []})),
            ),
        ),
        (
            "numbers-search-not-configured",
            found(searching(signed_in()), Err(not_configured())),
        ),
        (
            "numbers-search-failed",
            found(searching(signed_in()), Err(server_error())),
        ),
    ]
}

fn action(action: MarketplaceAction) -> UiEvent {
    UiEvent::Marketplace { action }
}

fn opened(session: Session) -> Session {
    session.ui(action(MarketplaceAction::Open))
}

fn owned(session: Session, result: Result<Value, ApiError>) -> Session {
    session.answer(
        |e| matches!(e, Effect::LoadOwnedNumbers { .. }),
        |ticket| Event::OwnedNumbersLoaded {
            ticket,
            result: result.map(|value| contracts::decode("owned numbers", value)),
        },
    )
}

fn held_by(session: Session) -> Session {
    owned(
        opened(session),
        Ok(contracts::json("district-provider-numbers.json")),
    )
}

/// The fixture's numbers, held.
fn held(session: Session) -> Session {
    held_by(session)
}

fn search_tab(session: Session) -> Session {
    session.ui(action(MarketplaceAction::SelectTab {
        tab: NumbersTab::Search,
    }))
}

fn typed(session: Session) -> Session {
    session.ui(action(MarketplaceAction::EditSearch {
        area_code: "416".to_owned(),
        country: "CA".to_owned(),
        number_type: "local".to_owned(),
    }))
}

/// A search asked for at once, on its way.
fn searching(session: Session) -> Session {
    typed(search_tab(held(session))).ui(action(MarketplaceAction::Search))
}

fn found(session: Session, result: Result<Value, ApiError>) -> Session {
    session.answer(
        |e| matches!(e, Effect::SearchNumbers { .. }),
        |ticket| Event::NumbersFound {
            ticket,
            result: result.map(|value| contracts::decode("number search", value)),
        },
    )
}

fn not_configured() -> ApiError {
    ApiError::Rejected {
        status: 400,
        detail: ErrorDetail {
            code: Some("messaging_provider_not_configured".to_owned()),
            message: Some(
                "Connect a carrier in the workspace settings to search for numbers.".to_owned(),
            ),
            ..ErrorDetail::default()
        },
    }
}

fn view(session: &Session) -> MarketplaceView {
    let ScreenView::Marketplace { view } = screen_view(&session.model) else {
        panic!("not the phone numbers");
    };
    view
}

/// Built, the area is offered and lists the held numbers, read only.
#[test]
fn built_it_is_offered_and_lists_the_numbers() {
    let session = held(signed_in());
    assert_eq!(route(&session), Route::Marketplace);
    assert!(offered(&session).contains(&NavDestination::Marketplace));
    let view = view(&session);
    assert!(view.offers_web);
    assert!(!view.owned.is_empty());
    assert!(view.owned.iter().all(|row| !row.line.contains("webhooks")));
}

/// A viewer is told who can change numbers, and is not offered the web
/// marketplace; opening it anyway sends nothing.
#[test]
fn a_viewer_is_told_who_can() {
    let session = held(viewer());
    let view = view(&session);
    assert!(!view.offers_web);
    assert!(
        view.read_only_note
            .starts_with("This screen is read only. Ask")
    );
    let before = session.pending.len();
    let tried = session.ui(action(MarketplaceAction::OpenWeb));
    assert_eq!(tried.pending.len(), before);
}

/// The web marketplace opens in the browser for a member who could buy there.
#[test]
fn buying_opens_the_web() {
    let opened = held(signed_in()).ui(action(MarketplaceAction::OpenWeb));
    assert!(opened.pending.iter().any(|effect| matches!(
        effect,
        Effect::OpenUrl { url } if url.ends_with("/dashboard/district/marketplace")
    )));
}

/// No carrier connected is an account state: no "Try again". A failure may
/// offer one.
#[test]
fn the_search_says_what_happened() {
    let NumberSearchView::Status { retry, .. } =
        view(&found(searching(signed_in()), Err(not_configured()))).search
    else {
        panic!("a status");
    };
    assert!(!retry);
    let NumberSearchView::Found { numbers, .. } = view(&found(
        searching(signed_in()),
        Ok(contracts::json("district-numbers-search.json")),
    ))
    .search
    else {
        panic!("numbers found");
    };
    assert_eq!(numbers.len(), 2);
    assert_eq!(numbers[1].monthly, None);
}

#[test]
fn each_action_is_its_core_event() {
    assert_eq!(
        action(MarketplaceAction::Open).events(),
        [Event::Navigate(Route::Marketplace)]
    );
    assert_eq!(
        action(MarketplaceAction::OpenWeb).events(),
        [Event::Marketplace(MarketplaceEvent::OpenWeb)]
    );
    assert_eq!(
        action(MarketplaceAction::Search).events(),
        [Event::Marketplace(MarketplaceEvent::Search)]
    );
    for (tab, core) in [
        (NumbersTab::Owned, MarketplaceTab::Owned),
        (NumbersTab::Search, MarketplaceTab::Search),
    ] {
        assert_eq!(
            action(MarketplaceAction::SelectTab { tab }).events(),
            [Event::Marketplace(MarketplaceEvent::SelectTab(core))]
        );
    }
    assert_eq!(
        action(MarketplaceAction::EditSearch {
            area_code: "416".to_owned(),
            country: "CA".to_owned(),
            number_type: "tollFree".to_owned(),
        })
        .events(),
        [Event::Marketplace(MarketplaceEvent::EditSearch(
            NumberSearchForm {
                area_code: "416".to_owned(),
                country: "CA".to_owned(),
                number_type: "tollFree".to_owned(),
            }
        ))]
    );
}
