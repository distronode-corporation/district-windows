//! Billing (src/billing.rs): the plan and the account read only, and choosing
//! a plan and managing billing in the app, with the step naming Stripe, the
//! browser fallback, and the purchases setting that hides them.

use district_core::{BillingEvent, PlanChoice, PlanTerm, PlanTier, PromoCode, PurchaseSetting};
use district_ffi::billing::{
    AccountBillingState, BillingAction, BillingView, PlanTermView, PlanTierView,
};
use district_ffi::{LoadStatus, NavDestination};

use super::*;

/// This area's snapshot cases.
pub(crate) fn cases() -> Vec<Case> {
    vec![
        ("billing-loading", opened(buying(signed_in()))),
        (
            "billing-failed",
            plan(opened(buying(signed_in())), Err(server_error())),
        ),
        ("billing-ready", ready(buying(signed_in()))),
        (
            "billing-account-unavailable",
            answer_account(
                plan_read(opened(buying(signed_in()))),
                Ok(contracts::json("district-billing-unavailable.json")),
            ),
        ),
        (
            "billing-account-failed",
            answer_account(plan_read(opened(buying(signed_in()))), Err(server_error())),
        ),
        (
            "billing-no-customer",
            answer_account(
                plan_read(opened(buying(signed_in()))),
                Ok(contracts::json("district-billing-no-customer.json")),
            ),
        ),
        ("billing-purchases-off", ready(not_buying(signed_in()))),
        ("billing-confirming", confirming(signed_in())),
        ("billing-opening", opening(signed_in())),
        ("billing-minting", minting(signed_in())),
        (
            "billing-hand-off-refused",
            handed_off(minting(signed_in()), Err(refused("invalid_next"))),
        ),
        ("billing-in-browser", in_browser(signed_in())),
    ]
}

fn action(action: BillingAction) -> UiEvent {
    UiEvent::Billing { action }
}

/// The purchases setting read as `setting`.
fn purchases(session: Session, setting: Option<PurchaseSetting>) -> Session {
    session.answer(
        |e| matches!(e, Effect::ReadPurchaseSetting { .. }),
        |ticket| Event::PurchaseSettingRead { ticket, setting },
    )
}

/// Purchases on this computer (never set: "Sign in every time").
fn buying(session: Session) -> Session {
    purchases(session, None)
}

/// Purchases off on this computer.
fn not_buying(session: Session) -> Session {
    purchases(session, Some(PurchaseSetting::Off))
}

fn opened(session: Session) -> Session {
    session.ui(action(BillingAction::Open))
}

fn plan(session: Session, result: Result<Value, ApiError>) -> Session {
    session.answer(
        |e| matches!(e, Effect::LoadWorkspaceBilling { .. }),
        |ticket| Event::WorkspaceBillingLoaded {
            ticket,
            result: result.map(|value| contracts::decode("workspace billing", value)),
        },
    )
}

fn plan_read(session: Session) -> Session {
    plan(
        session,
        Ok(contracts::json("district-workspace-billing.json")),
    )
}

fn answer_account(session: Session, result: Result<Value, ApiError>) -> Session {
    session.answer(
        |e| matches!(e, Effect::LoadAccountBilling { .. }),
        |ticket| Event::AccountBillingLoaded {
            ticket,
            result: result.map(|value| contracts::decode("account billing", value)),
        },
    )
}

/// Both halves read from the fixtures.
fn ready(session: Session) -> Session {
    answer_account(
        plan_read(opened(session)),
        Ok(contracts::json("district-billing.json")),
    )
}

fn choose(promo: Option<&str>) -> UiEvent {
    action(BillingAction::ChoosePlan {
        tier: PlanTierView::VoicePro,
        term: PlanTermView::Annual,
        promo: promo.map(str::to_owned),
    })
}

/// Voice Pro, annual, with a code: the step naming Stripe shows.
fn confirming(session: Session) -> Session {
    ready(buying(session)).ui(choose(Some(" save-100 ")))
}

/// Confirmed: the start page opens in the checkout window.
fn opening(session: Session) -> Session {
    confirming(session).ui(action(BillingAction::ConfirmPurchase))
}

/// The start page did not answer in time: the link is being asked for.
fn minting(session: Session) -> Session {
    opening(session).answer(
        |e| matches!(e, Effect::Wait { .. }),
        |ticket| Event::WaitOver { ticket },
    )
}

fn handed_off(session: Session, result: Result<Value, ApiError>) -> Session {
    session.answer(
        |e| matches!(e, Effect::RequestBillingHandOff { .. }),
        |ticket| Event::BillingHandOffReady {
            ticket,
            result: result.map(|value| contracts::decode("billing hand-off", value)),
        },
    )
}

/// The app could not show the checkout window: the start page opens in the
/// browser, and the screen says so.
fn in_browser(session: Session) -> Session {
    opening(session).send(Event::EmbeddedUnavailable)
}

fn refused(code: &str) -> ApiError {
    ApiError::Envelope {
        status: 400,
        code: code.to_owned(),
        detail: ErrorDetail {
            message: Some("next must be the checkout or the billing page".to_owned()),
            code: Some(code.to_owned()),
            degraded_regions: Vec::new(),
        },
    }
}

fn view(session: &Session) -> BillingView {
    let ScreenView::Billing { view } = screen_view(&session.model) else {
        panic!("not billing");
    };
    view
}

/// Built, the area is offered and shows the plan, the account and, for a
/// role that can change the plan with purchases on, the plan chooser.
#[test]
fn built_it_is_offered_and_shows_the_plan_and_the_chooser() {
    let session = ready(buying(signed_in()));
    assert_eq!(route(&session), Route::Billing);
    assert!(offered(&session).contains(&NavDestination::Billing));
    let view = view(&session);
    assert_eq!(view.status, LoadStatus::Ready);
    let plan = view.plan.expect("the plan");
    assert!(!plan.name.is_empty());
    assert_eq!(view.account.state, AccountBillingState::Ready);
    let purchase = view.purchase.expect("the chooser");
    let tiers: Vec<_> = purchase.plans.iter().map(|plan| plan.tier).collect();
    assert_eq!(
        tiers,
        [
            PlanTierView::VoiceSolo,
            PlanTierView::VoiceStarter,
            PlanTierView::VoicePro,
            PlanTierView::VoiceStudio
        ]
    );
    let terms: Vec<_> = purchase.terms.iter().map(|term| term.term).collect();
    assert_eq!(terms, [PlanTermView::Monthly, PlanTermView::Annual]);
    assert!(purchase.offers_manage && purchase.enabled && !purchase.opening);
    assert!(purchase.confirming.is_none() && purchase.in_browser_note.is_none());
    assert!(!view.offers_web, "in the app, not on the web");
    assert!(view.note.contains("Stripe"), "{}", view.note);
}

/// "Off" hides every purchase action: the screen is the read-only one.
#[test]
fn purchases_off_hides_everything() {
    let session = ready(not_buying(signed_in()));
    let shown = view(&session);
    assert_eq!(shown.purchase, None);
    assert!(shown.offers_web, "the read-only link stays");
    assert!(!shown.note.contains("Stripe"), "{}", shown.note);

    // Pressing anyway sends events the core refuses: nothing opens.
    let session = session
        .ui(choose(None))
        .ui(action(BillingAction::ConfirmPurchase))
        .ui(action(BillingAction::ManageInApp));
    assert!(
        !session.pending.iter().any(|e| matches!(
            e,
            Effect::OpenEmbedded { .. } | Effect::OpenOneTimeUrl { .. }
        )),
        "{:?}",
        session.pending
    );

    // Turned off from the Account page, the chooser goes at once.
    let session = confirming(signed_in()).ui(action(BillingAction::SetPurchases { on: false }));
    assert_eq!(view(&session).purchase, None);
    assert!(session.pending.iter().any(
        |e| matches!(e, Effect::SavePurchaseSetting { setting } if *setting == PurchaseSetting::Off)
    ));
}

/// The step before checkout names Stripe, in the core's words, with the plan
/// chosen and the code in its canonical shape.
#[test]
fn the_confirmation_names_stripe_and_the_choice() {
    let confirm = view(&confirming(signed_in()))
        .purchase
        .unwrap()
        .confirming
        .expect("the step shows");
    assert!(confirm.body.contains("Stripe"), "{}", confirm.body);
    assert!(confirm.title.contains("Stripe"), "{}", confirm.title);
    assert_eq!(confirm.plan, "Voice Pro");
    assert_eq!(confirm.term, "Annual");
    assert_eq!(confirm.promo.as_deref(), Some("SAVE-100"));

    let cancelled = confirming(signed_in()).ui(action(BillingAction::CancelPurchase));
    assert_eq!(view(&cancelled).purchase.unwrap().confirming, None);
}

/// Confirmed, the start page opens in a new private view inside the app; the
/// link that follows opens in the same view, and none of it reaches the
/// screen.
#[test]
fn checkout_opens_in_the_app_and_shows_no_link() {
    let session = opening(signed_in());
    assert!(session.pending.iter().any(|e| matches!(
        e,
        Effect::OpenEmbedded {
            view: district_core::EmbeddedView::NewPrivate,
            ..
        }
    )));
    assert!(view(&session).purchase.unwrap().opening);
    let asking = minting(signed_in());
    let shown = view(&asking).purchase.unwrap();
    assert!(shown.opening && !shown.enabled);
    let opened = handed_off(
        minting(signed_in()),
        Ok(
            json!({"url": "https://www.distronode.com/dashboard/handoff?code=one-time", "expiresIn": 60}),
        ),
    );
    assert!(opened.pending.iter().any(|e| matches!(
        e,
        Effect::OpenEmbedded {
            view: district_core::EmbeddedView::Same,
            ..
        }
    )));
    let json = serde_json::to_string(&screen_view(&opened.model)).unwrap();
    assert!(!json.contains("one-time"), "{json}");
    // The window closing reads billing again.
    let closed = opened.ui(action(BillingAction::CheckoutClosed));
    assert!(
        closed
            .pending
            .iter()
            .any(|e| matches!(e, Effect::LoadWorkspaceBilling { .. }))
    );
}

/// Without the checkout window, the screen says checkout opens in the browser.
#[test]
fn the_browser_fallback_is_said() {
    let shown = view(&in_browser(signed_in())).purchase.unwrap();
    let note = shown.in_browser_note.expect("the note");
    assert!(note.contains("browser"), "{note}");
}

/// A refused hand-off says why, and the actions come back.
#[test]
fn a_refused_hand_off_says_why() {
    let shown = view(&handed_off(
        minting(signed_in()),
        Err(refused("invalid_next")),
    ))
    .purchase
    .unwrap();
    let notice = shown.notice.expect("a notice");
    assert!(notice.message.contains("Updating the app"), "{notice:?}");
    assert!(shown.enabled && !shown.opening);
}

/// A viewer is offered neither the chooser nor the web link.
#[test]
fn a_viewer_is_offered_no_purchase() {
    let session = purchases(
        signed_in_as(
            config(),
            Ok(contracts::decode(
                "one workspace",
                json!({
                    "success": true,
                    "workspaces": [{
                        "id": "ws-1",
                        "name": "Example Dental",
                        "region": "us",
                        "role": "viewer",
                        "subscriptionTier": "VoicePro"
                    }],
                    "total": 1, "limit": 100, "offset": 0,
                    "degradedRegions": [], "inactiveCount": 0,
                    "defaultWorkspaceId": "ws-1"
                }),
            )),
        ),
        None,
    );
    let view = view(&ready(session));
    assert_eq!(view.purchase, None);
    assert!(!view.offers_web);
}

/// The account half's states, each told apart.
#[test]
fn the_account_half_never_shows_an_outage_as_none() {
    let unavailable = view(&answer_account(
        plan_read(opened(buying(signed_in()))),
        Ok(contracts::json("district-billing-unavailable.json")),
    ))
    .account;
    assert_eq!(unavailable.state, AccountBillingState::Unavailable);
    assert!(unavailable.no_subscriptions.is_none() && unavailable.body.is_some());
    let none = view(&answer_account(
        plan_read(opened(buying(signed_in()))),
        Ok(contracts::json("district-billing-no-customer.json")),
    ))
    .account;
    assert_eq!(none.state, AccountBillingState::Ready);
    assert!(none.no_subscriptions.is_some());
}

#[test]
fn each_action_is_its_core_event() {
    let one = |a| UiEvent::Billing { action: a }.events();
    assert_eq!(one(BillingAction::Open), [Event::Navigate(Route::Billing)]);
    assert_eq!(
        one(BillingAction::ManageOnWeb),
        [Event::Billing(BillingEvent::ManageOnWeb)]
    );
    assert_eq!(
        one(BillingAction::OpenInvoice {
            invoice_id: "in_1".to_owned()
        }),
        [Event::Billing(BillingEvent::OpenInvoice {
            invoice_id: "in_1".to_owned()
        })]
    );
    assert_eq!(one(BillingAction::CheckAgain), [Event::Refresh]);
    assert_eq!(
        one(BillingAction::ChoosePlan {
            tier: PlanTierView::VoiceSolo,
            term: PlanTermView::Monthly,
            promo: Some("   ".to_owned()),
        }),
        [Event::Billing(BillingEvent::ChoosePlan(PlanChoice {
            tier: PlanTier::VoiceSolo,
            term: PlanTerm::Monthly,
            promo: None,
        }))]
    );
    assert_eq!(
        one(BillingAction::ChoosePlan {
            tier: PlanTierView::VoiceStudio,
            term: PlanTermView::Annual,
            promo: Some("launch_2".to_owned()),
        }),
        [Event::Billing(BillingEvent::ChoosePlan(PlanChoice {
            tier: PlanTier::VoiceStudio,
            term: PlanTerm::Annual,
            promo: PromoCode::parse("LAUNCH_2"),
        }))]
    );
    assert_eq!(
        one(BillingAction::ChoosePlan {
            tier: PlanTierView::VoiceStarter,
            term: PlanTermView::Monthly,
            promo: None,
        }),
        [Event::Billing(BillingEvent::ChoosePlan(PlanChoice {
            tier: PlanTier::VoiceStarter,
            term: PlanTerm::Monthly,
            promo: None,
        }))]
    );
    // A code the core does not take sends nothing, whatever it holds.
    for hostile in ["SAVE&tier=Dgi", "save 10", "caf\u{e9}", &"9".repeat(41)] {
        assert_eq!(
            one(BillingAction::ChoosePlan {
                tier: PlanTierView::VoicePro,
                term: PlanTermView::Monthly,
                promo: Some(hostile.to_owned()),
            }),
            [],
            "{hostile:?}"
        );
    }
    assert_eq!(
        one(BillingAction::ConfirmPurchase),
        [Event::Billing(BillingEvent::ConfirmPurchase)]
    );
    assert_eq!(
        one(BillingAction::CancelPurchase),
        [Event::Billing(BillingEvent::CancelPurchase)]
    );
    assert_eq!(
        one(BillingAction::ManageInApp),
        [Event::Billing(BillingEvent::ManageInApp)]
    );
    assert_eq!(
        one(BillingAction::DismissPurchaseNotice),
        [Event::Billing(BillingEvent::DismissPurchaseNotice)]
    );
    assert_eq!(one(BillingAction::CheckoutClosed), [Event::EmbeddedClosed]);
    assert_eq!(
        one(BillingAction::SetPurchases { on: true }),
        [Event::SetPurchases(PurchaseSetting::SignInEveryTime)]
    );
    assert_eq!(
        one(BillingAction::SetPurchases { on: false }),
        [Event::SetPurchases(PurchaseSetting::Off)]
    );
}
