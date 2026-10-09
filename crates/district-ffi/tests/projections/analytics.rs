//! Analytics (src/analytics.rs, src/chart.rs).

use district_core::{AnalyticsEvent, Capabilities};
use district_ffi::analytics::{AnalyticsAction, AnalyticsPeriod, AnalyticsView};
use district_ffi::{LoadStatus, NavDestination};
use district_model::AnalyticsRange;

use super::*;

/// The analytics screen, opened: all three reads on their way.
fn opened(session: Session) -> Session {
    session.ui(UiEvent::Analytics {
        action: AnalyticsAction::Open,
    })
}

fn report(session: Session, fixture: &str) -> Session {
    let response = contracts::read(fixture);
    session.answer(
        |e| matches!(e, Effect::LoadAnalytics { .. }),
        |ticket| Event::AnalyticsLoaded {
            ticket,
            result: Ok(response),
        },
    )
}

fn usage(session: Session, fixture: &str) -> Session {
    let response = contracts::read(fixture);
    session.answer(
        |e| matches!(e, Effect::LoadUsage { .. }),
        |ticket| Event::UsageLoaded {
            ticket,
            result: Ok(response),
        },
    )
}

fn history(session: Session, months: Value) -> Session {
    let response = contracts::decode(
        "usage history",
        with(
            contracts::json("district-usage-history.json"),
            "usage",
            months,
        ),
    );
    session.answer(
        |e| matches!(e, Effect::LoadUsageHistory { .. }),
        |ticket| Event::UsageHistoryLoaded {
            ticket,
            result: Ok(response),
        },
    )
}

fn full_history(session: Session) -> Session {
    let months = contracts::json("district-usage-history.json")["usage"].clone();
    history(session, months)
}

/// Every read answered from the core's fixtures.
fn loaded(session: Session) -> Session {
    full_history(usage(
        report(opened(session), "district-analytics.json"),
        "district-usage.json",
    ))
}

/// Every read failed.
fn failed(session: Session) -> Session {
    opened(session)
        .answer(
            |e| matches!(e, Effect::LoadAnalytics { .. }),
            |ticket| Event::AnalyticsLoaded {
                ticket,
                result: Err(server_error()),
            },
        )
        .answer(
            |e| matches!(e, Effect::LoadUsage { .. }),
            |ticket| Event::UsageLoaded {
                ticket,
                result: Err(server_error()),
            },
        )
        .answer(
            |e| matches!(e, Effect::LoadUsageHistory { .. }),
            |ticket| Event::UsageHistoryLoaded {
                ticket,
                result: Err(server_error()),
            },
        )
}

fn select(session: Session, period: AnalyticsPeriod) -> Session {
    session.ui(UiEvent::Analytics {
        action: AnalyticsAction::SelectPeriod { period },
    })
}

/// This area's snapshot cases.
pub(crate) fn cases() -> Vec<Case> {
    vec![
        ("analytics-loading", opened(signed_in())),
        ("analytics-loaded", loaded(signed_in())),
        (
            "analytics-empty",
            history(
                usage(
                    report(opened(signed_in()), "district-analytics-new-workspace.json"),
                    "district-usage-empty.json",
                ),
                json!([]),
            ),
        ),
        ("analytics-failed", failed(signed_in())),
        (
            "analytics-card-failed",
            full_history(
                report(opened(signed_in()), "district-analytics.json").answer(
                    |e| matches!(e, Effect::LoadUsage { .. }),
                    |ticket| Event::UsageLoaded {
                        ticket,
                        result: Err(server_error()),
                    },
                ),
            ),
        ),
        (
            "analytics-period-30-days-on-its-way",
            select(loaded(signed_in()), AnalyticsPeriod::ThirtyDays),
        ),
        (
            "analytics-period-30-days",
            report(
                select(loaded(signed_in()), AnalyticsPeriod::ThirtyDays),
                "district-analytics.json",
            ),
        ),
        (
            "analytics-period-90-days",
            report(
                select(loaded(signed_in()), AnalyticsPeriod::NinetyDays),
                "district-analytics.json",
            ),
        ),
        (
            "analytics-refreshing",
            loaded(signed_in()).ui(UiEvent::Refresh),
        ),
        ("analytics-viewer", loaded(viewer())),
        (
            "analytics-no-workspace",
            opened(signed_in_to(Ok(overview::no_workspaces(&[])))),
        ),
    ]
}

/// The analytics page `session` shows.
fn page(session: &Session) -> AnalyticsView {
    match screen_view(&session.model) {
        ScreenView::Analytics { view } => view,
        other => panic!("not the analytics: {other:?}"),
    }
}

#[test]
fn built_it_is_offered_and_shows_its_page() {
    let session = opened(signed_in());
    assert_eq!(route(&session), Route::Analytics);
    assert!(offered(&session).contains(&NavDestination::Analytics));
    assert_eq!(shell_json(&session)["nav_selected"], json!("Analytics"));
    let view = page(&session);
    assert_eq!(view.title, "Analytics");
    assert_eq!(view.status, LoadStatus::Ready);
    assert_eq!(view.report.status, LoadStatus::Loading);
    assert_eq!(view.usage.status, LoadStatus::Loading);
    assert_eq!(view.history.status, LoadStatus::Loading);
}

/// The core keeps no role from the analytics: agency, client, viewer, and a
/// role it cannot read all see the page, which changes nothing. Only a
/// session with no workspace open stays where it was.
#[test]
fn every_role_may_open_it_and_no_workspace_may_not() {
    for role in [Some("agency"), Some("client"), Some("viewer"), None] {
        assert!(
            Capabilities::for_role(role).allows(&Route::Analytics),
            "{role:?}"
        );
    }
    let viewer = opened(viewer());
    assert_eq!(route(&viewer), Route::Analytics);
    assert!(offered(&viewer).contains(&NavDestination::Analytics));
    let nowhere = opened(signed_in_to(Ok(overview::no_workspaces(&[]))));
    assert_eq!(route(&nowhere), Route::Overview);
    assert!(!offered(&nowhere).contains(&NavDestination::Analytics));
}

#[test]
fn the_figures_and_charts_are_the_services() {
    let view = page(&loaded(signed_in()));
    let report = &view.report;
    assert_eq!(report.title, "Calls over 7 days");
    assert_eq!(
        report
            .figures
            .iter()
            .map(|fact| (fact.label.as_str(), fact.value.as_str()))
            .collect::<Vec<_>>(),
        [
            ("Calls", "48"),
            ("Average call", "2m 0s"),
            ("Converted", "38%"),
            ("Missed", "9"),
            ("Abandoned", "4"),
        ]
    );
    let trend = report.trend.as_ref().expect("a trend with calls is drawn");
    assert_eq!(
        trend.summary,
        "Calls per day: Aug 9 9, Aug 10 7, Aug 11 9, Aug 12 0, Aug 13 4, Aug 14 8, Aug 15 11. \
         The most was 11, on Aug 15."
    );
    assert_eq!(
        trend
            .bars
            .iter()
            .map(|bar| bar.per_mille)
            .collect::<Vec<_>>(),
        [818, 636, 818, 0, 364, 727, 1000]
    );
    assert_eq!(
        report.funnel.summary,
        "Call funnel: Total Dials 48, Connected Calls 35, Successful Leads 18."
    );
    let sentiment = report.sentiment.as_ref().expect("calls to analyse");
    assert_eq!(
        sentiment.summary,
        "Caller sentiment: Positive Sentiment 21 (44%), Neutral Sentiment 16 (33%), \
         Friction Sentiment 11 (23%)."
    );
    assert_eq!(view.usage.month.as_deref(), Some("Aug 2026"));
    let months = view.history.chart.as_ref().expect("months metered");
    assert_eq!(
        months
            .months
            .iter()
            .map(|month| (month.month.as_str(), month.per_mille))
            .collect::<Vec<_>>(),
        [("Aug 2026", 1000), ("Jul 2026", 191), ("Jun 2026", 0)]
    );
}

#[test]
fn nothing_to_show_is_said_rather_than_drawn() {
    let view = page(&history(
        usage(
            report(opened(signed_in()), "district-analytics-new-workspace.json"),
            "district-usage-empty.json",
        ),
        json!([]),
    ));
    assert_eq!(view.report.trend, None);
    assert_eq!(
        view.report.trend_empty.as_deref(),
        Some("No calls in this window.")
    );
    assert_eq!(view.report.sentiment, None);
    assert_eq!(
        view.report.sentiment_empty.as_deref(),
        Some("No calls to analyse yet.")
    );
    assert!(view.report.funnel.bars.iter().all(|bar| bar.per_mille == 0));
    assert!(view.usage.lines.is_empty());
    assert!(view.usage.empty.is_some());
    assert_eq!(view.history.chart, None);
    assert!(view.history.empty.is_some());
}

#[test]
fn every_read_failing_fails_the_page_with_try_again() {
    let view = page(&failed(signed_in()));
    match &view.status {
        LoadStatus::Failed { title, failure } => {
            assert_eq!(title, "Could not load analytics");
            assert!(failure.retryable);
        }
        other => panic!("not failed: {other:?}"),
    }
    let retried = failed(signed_in()).ui(UiEvent::Analytics {
        action: AnalyticsAction::Retry,
    });
    assert!(
        retried
            .pending
            .iter()
            .any(|e| matches!(e, Effect::LoadAnalytics { .. }))
    );
}

#[test]
fn a_new_period_is_selected_at_once_and_its_figures_follow() {
    let waiting = page(&select(loaded(signed_in()), AnalyticsPeriod::NinetyDays));
    assert_eq!(
        waiting
            .periods
            .iter()
            .map(|period| (period.label.as_str(), period.selected))
            .collect::<Vec<_>>(),
        [("7 days", false), ("30 days", false), ("90 days", true)]
    );
    // The figures on screen are still the last period's, being read again.
    assert_eq!(waiting.report.title, "Calls over 7 days");
    assert!(waiting.report.refreshing && waiting.refreshing);
    let landed = page(&report(
        select(loaded(signed_in()), AnalyticsPeriod::NinetyDays),
        "district-analytics.json",
    ));
    assert_eq!(landed.report.title, "Calls over 90 days");
    assert_eq!(landed.report.trend_title, "Calls per week");
}

#[test]
fn each_action_is_its_core_event() {
    let events = |action| UiEvent::Analytics { action }.events();
    assert_eq!(
        events(AnalyticsAction::Open),
        [Event::Navigate(Route::Analytics)]
    );
    for (period, range) in [
        (AnalyticsPeriod::SevenDays, AnalyticsRange::SevenDays),
        (AnalyticsPeriod::ThirtyDays, AnalyticsRange::ThirtyDays),
        (AnalyticsPeriod::NinetyDays, AnalyticsRange::NinetyDays),
    ] {
        assert_eq!(
            events(AnalyticsAction::SelectPeriod { period }),
            [Event::Analytics(AnalyticsEvent::SelectRange(range))]
        );
    }
    assert_eq!(events(AnalyticsAction::Retry), [Event::Refresh]);
}
