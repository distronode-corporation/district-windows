//! Call analytics and metered usage, as District AI for Linux's analytics page
//! shows them (`pages/analytics.rs`, `ui/analytics-page.ui`): the period
//! chooser, the call figures over the period with their charts, this month's
//! usage, and the last few months of it.
//!
//! The core reads the three apart and each fails apart, so each card carries
//! its own state, and a figure already read is never blanked because another
//! read failed. The page as a whole has failed only when all three have
//! ([`AnalyticsScreen::failure`]). A measure nothing metered reads "Not
//! recorded", never zero. The charts are [`crate::chart`]'s.
//!
//! Every role the core lets open the screen sees the same page: it changes
//! nothing, and its only controls (the period, and Try again) are reads.

use district_core::{
    AnalyticsCard, AnalyticsEvent, AnalyticsReport, AnalyticsScreen, Event, HistoryCard, Model,
    Route, SignedIn, UsageCard, history_rows, month_label, usage_lines,
};
use district_model::AnalyticsRange;
use serde::Serialize;

pub use crate::chart::{
    BarChartView, ChartBarView, HistoryChartView, HistoryMonthView, SentimentBandView,
    SentimentChartView, SentimentTone,
};
use crate::chart;
use crate::screen::ScreenView;
use crate::views::{FactView, FailureView, LoadStatus};

/// Whether this version has the area's screens.
pub(crate) const BUILT: bool = true;

/// The page's heading, the navigation entry's.
pub const ANALYTICS_TITLE: &str = "Analytics";
/// The period chooser's label.
pub const PERIOD_LABEL: &str = "Period";
/// The call card's heading before its figures are read.
pub const CALLS_TITLE: &str = "Calls";
/// The funnel's heading.
pub const FUNNEL_TITLE: &str = "Call funnel";
/// The sentiment breakdown's heading.
pub const SENTIMENT_TITLE: &str = "Caller sentiment";
/// This month's usage card's heading.
pub const USAGE_TITLE: &str = "This month's usage";
/// The usage history card's heading.
pub const HISTORY_TITLE: &str = "Recent months";

/// The periods, in the order of their buttons.
const PERIODS: [AnalyticsPeriod; 3] = [
    AnalyticsPeriod::SevenDays,
    AnalyticsPeriod::ThirtyDays,
    AnalyticsPeriod::NinetyDays,
];

/// The analytics screen.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct AnalyticsView {
    /// The heading: "Analytics".
    pub title: String,
    /// `Failed` (a status page, with Try again when it may help) only when
    /// every read failed; `Ready` otherwise, each card saying where its own
    /// read stands.
    pub status: LoadStatus,
    /// The period chooser's label: "Period".
    pub period_label: String,
    /// The periods, in order, the one selected marked.
    pub periods: Vec<PeriodChoiceView>,
    /// Whether anything showing is being read again.
    pub refreshing: bool,
    /// The calls over the period.
    pub report: ReportCardView,
    /// This month's metered usage.
    pub usage: UsageCardView,
    /// The last few months of metered usage.
    pub history: HistoryCardView,
}

/// One button of the period chooser.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct PeriodChoiceView {
    /// The period it selects (send [`AnalyticsAction::SelectPeriod`]).
    pub period: AnalyticsPeriod,
    /// What it says: "7 days".
    pub label: String,
    /// Whether it is the period selected. It moves when the member picks
    /// another, before that period's figures arrive.
    pub selected: bool,
}

/// A period the call figures cover.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash, Serialize, uniffi::Enum)]
pub enum AnalyticsPeriod {
    /// The last 7 days, a bar per day.
    SevenDays,
    /// The last 30 days, a bar per day.
    ThirtyDays,
    /// The last 90 days, a bar per week.
    NinetyDays,
}

impl AnalyticsPeriod {
    fn range(self) -> AnalyticsRange {
        match self {
            Self::SevenDays => AnalyticsRange::SevenDays,
            Self::ThirtyDays => AnalyticsRange::ThirtyDays,
            Self::NinetyDays => AnalyticsRange::NinetyDays,
        }
    }
}

/// The calls over the period: the figures and their charts.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct ReportCardView {
    /// "Calls", then which period the figures on screen are for: "Calls over
    /// 7 days" (not the period picked while its figures are on their way).
    pub title: String,
    /// Where the read stands: a progress ring, the failure (under the core's
    /// heading, "Could not load call analytics"), or the figures.
    pub status: LoadStatus,
    /// Whether the figures are being read again, with these still showing.
    pub refreshing: bool,
    /// The five figures: calls, average call, converted, missed, abandoned.
    pub figures: Vec<FactView>,
    /// How call volume moved against the period before, in a sentence.
    pub change: String,
    /// What a bar of the trend stands for: "Calls per day", "Calls per week".
    pub trend_title: String,
    /// The trend as columns, or `None` when it has no calls at all.
    pub trend: Option<BarChartView>,
    /// What to say instead of a trend with no calls.
    pub trend_empty: Option<String>,
    /// The funnel's heading: "Call funnel".
    pub funnel_title: String,
    /// The funnel's stages as rows.
    pub funnel: BarChartView,
    /// The sentiment breakdown's heading: "Caller sentiment".
    pub sentiment_title: String,
    /// The sentiment breakdown, or `None` with no calls to analyse: never an
    /// even split of nothing.
    pub sentiment: Option<SentimentChartView>,
    /// What to say instead of a breakdown of no calls.
    pub sentiment_empty: Option<String>,
}

/// This month's metered usage.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct UsageCardView {
    /// "This month's usage".
    pub title: String,
    /// The month: "Aug 2026", once read and metered.
    pub month: Option<String>,
    /// Where the read stands.
    pub status: LoadStatus,
    /// Whether it is being read again, with this still showing.
    pub refreshing: bool,
    /// Each measure metered this month and its amount. Measures never metered
    /// are left out, not shown as zero.
    pub lines: Vec<FactView>,
    /// What to say when nothing has been metered this month yet.
    pub empty: Option<String>,
}

/// The last few months of metered usage.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct HistoryCardView {
    /// "Recent months".
    pub title: String,
    /// What the bars compare, under the heading.
    pub caption: String,
    /// Where the read stands.
    pub status: LoadStatus,
    /// Whether it is being read again, with this still showing.
    pub refreshing: bool,
    /// The months, or `None` when nothing has been metered in any of them.
    pub chart: Option<HistoryChartView>,
    /// What to say when nothing has been metered in any of them.
    pub empty: Option<String>,
}

/// Something the member did on the analytics screen.
#[derive(Clone, Debug, PartialEq, Eq, uniffi::Enum)]
pub enum AnalyticsAction {
    /// Open the analytics.
    Open,
    /// Show the figures for `period`. Picking the period selected does nothing.
    SelectPeriod {
        /// The period.
        period: AnalyticsPeriod,
    },
    /// Read everything again: Try again, after a failure.
    Retry,
}

/// The core events `action` is, in the order the core is to hear them.
pub(crate) fn events(action: AnalyticsAction) -> Vec<Event> {
    vec![match action {
        AnalyticsAction::Open => Event::Navigate(Route::Analytics),
        AnalyticsAction::SelectPeriod { period } => {
            Event::Analytics(AnalyticsEvent::SelectRange(period.range()))
        }
        AnalyticsAction::Retry => Event::Refresh,
    }]
}

/// The page of the analytics, for a signed-in model.
pub(crate) fn screen(_model: &Model, signed_in: &SignedIn) -> ScreenView {
    ScreenView::Analytics {
        view: analytics_view(&signed_in.analytics),
    }
}

/// The page for `screen`.
pub(crate) fn analytics_view(screen: &AnalyticsScreen) -> AnalyticsView {
    let report = report_card(&screen.report);
    let usage = usage_card(&screen.usage);
    let history = history_card(&screen.history);
    AnalyticsView {
        title: ANALYTICS_TITLE.to_owned(),
        status: screen.failure().map_or(LoadStatus::Ready, |failure| {
            LoadStatus::failed(AnalyticsScreen::FAILED_TITLE, failure)
        }),
        period_label: PERIOD_LABEL.to_owned(),
        periods: PERIODS
            .iter()
            .map(|&period| PeriodChoiceView {
                period,
                label: AnalyticsScreen::range_label(period.range()).to_owned(),
                selected: period.range() == screen.range,
            })
            .collect(),
        refreshing: report.refreshing || usage.refreshing || history.refreshing,
        report,
        usage,
        history,
    }
}

/// The heading of the figures for `range`.
fn report_title(range: AnalyticsRange) -> String {
    format!("Calls over {}", AnalyticsScreen::range_label(range))
}

/// What a bar of the trend stands for: a day, or a week in the longest
/// period.
fn trend_title(range: AnalyticsRange) -> &'static str {
    match range {
        AnalyticsRange::NinetyDays => "Calls per week",
        AnalyticsRange::SevenDays | AnalyticsRange::ThirtyDays => "Calls per day",
    }
}

fn report_card(card: &AnalyticsCard) -> ReportCardView {
    let mut view = ReportCardView {
        title: CALLS_TITLE.to_owned(),
        status: LoadStatus::Loading,
        refreshing: false,
        figures: Vec::new(),
        change: String::new(),
        trend_title: String::new(),
        trend: None,
        trend_empty: None,
        funnel_title: FUNNEL_TITLE.to_owned(),
        funnel: chart::rows(FUNNEL_TITLE, &[]),
        sentiment_title: SENTIMENT_TITLE.to_owned(),
        sentiment: None,
        sentiment_empty: None,
    };
    match card {
        AnalyticsCard::NotLoaded | AnalyticsCard::Loading => view,
        AnalyticsCard::Failed(failure) => ReportCardView {
            status: LoadStatus::failed(AnalyticsCard::FAILED_TITLE, failure),
            ..view
        },
        AnalyticsCard::Ready(report) => {
            let trend_title = trend_title(report.range);
            let bands = report.sentiment();
            let no_sentiment = bands.iter().all(|band| band.value <= 0);
            view = ReportCardView {
                title: report_title(report.range),
                status: LoadStatus::Ready,
                refreshing: report.refreshing,
                figures: figures(report),
                change: report.change().text(),
                trend_title: trend_title.to_owned(),
                trend: (!report.trend_is_empty())
                    .then(|| chart::columns(trend_title, &report.trend())),
                trend_empty: report
                    .trend_is_empty()
                    .then(|| AnalyticsReport::NO_CALLS.to_owned()),
                funnel: chart::rows(FUNNEL_TITLE, &report.funnel()),
                sentiment: (!no_sentiment).then(|| chart::sentiment(&bands)),
                sentiment_empty: no_sentiment.then(|| AnalyticsReport::NO_SENTIMENT.to_owned()),
                ..view
            };
            view
        }
    }
}

/// The report's figures, each with its caption, in the order of the tiles.
fn figures(report: &AnalyticsReport) -> Vec<FactView> {
    let metrics = &report.response.metrics;
    [
        ("Calls", metrics.total_calls.to_string()),
        ("Average call", report.average_call()),
        ("Converted", report.conversion()),
        ("Missed", metrics.missed_calls.to_string()),
        ("Abandoned", metrics.abandoned_calls.to_string()),
    ]
    .into_iter()
    .map(|(label, value)| FactView {
        label: label.to_owned(),
        value,
    })
    .collect()
}

fn usage_card(card: &UsageCard) -> UsageCardView {
    let view = UsageCardView {
        title: USAGE_TITLE.to_owned(),
        month: None,
        status: LoadStatus::Loading,
        refreshing: false,
        lines: Vec::new(),
        empty: None,
    };
    match card {
        UsageCard::NotLoaded | UsageCard::Loading => view,
        UsageCard::Failed(failure) => UsageCardView {
            status: LoadStatus::failed(UsageCard::FAILED_TITLE, failure),
            ..view
        },
        UsageCard::Ready { month, refreshing } => {
            let lines: Vec<FactView> = month
                .iter()
                .flat_map(usage_lines)
                .map(|line| FactView {
                    label: line.label.to_owned(),
                    value: line.amount,
                })
                .collect();
            UsageCardView {
                month: month
                    .as_ref()
                    .filter(|_| !lines.is_empty())
                    .map(|month| month_label(&month.month)),
                status: LoadStatus::Ready,
                refreshing: *refreshing,
                empty: lines.is_empty().then(|| UsageCard::NONE.to_owned()),
                lines,
                ..view
            }
        }
    }
}

fn history_card(card: &HistoryCard) -> HistoryCardView {
    let view = HistoryCardView {
        title: HISTORY_TITLE.to_owned(),
        caption: HistoryCard::CAPTION.to_owned(),
        status: LoadStatus::Loading,
        refreshing: false,
        chart: None,
        empty: None,
    };
    match card {
        HistoryCard::NotLoaded | HistoryCard::Loading => view,
        HistoryCard::Failed(failure) => HistoryCardView {
            status: LoadStatus::failed(HistoryCard::FAILED_TITLE, failure),
            ..view
        },
        HistoryCard::Ready { months, refreshing } => HistoryCardView {
            status: LoadStatus::Ready,
            refreshing: *refreshing,
            chart: (!months.is_empty()).then(|| chart::history(&history_rows(months))),
            empty: months.is_empty().then(|| HistoryCard::NONE.to_owned()),
            ..view
        },
    }
}

/// A page for tests that only need one (the trip to C# and back).
#[cfg(test)]
pub(crate) fn sample() -> AnalyticsView {
    analytics_view(&AnalyticsScreen::default())
}

#[cfg(test)]
mod tests {
    use district_core::FailureText;

    use super::*;

    fn failed(message: &str) -> FailureText {
        FailureText {
            message: message.to_owned(),
            degraded_regions: Vec::new(),
            session_ended: None,
            retryable: true,
        }
    }

    #[test]
    fn a_card_that_failed_says_so_under_its_own_heading() {
        let usage = usage_card(&UsageCard::Failed(failed("Usage is down.")));
        assert_eq!(
            usage.status,
            LoadStatus::Failed {
                failure: FailureView {
                    message: "Usage is down.".to_owned(),
                    regions_line: None,
                    retryable: true,
                },
                title: UsageCard::FAILED_TITLE.to_owned(),
            }
        );
        let history = history_card(&HistoryCard::Failed(failed("History is down.")));
        assert!(
            matches!(&history.status, LoadStatus::Failed { title, .. } if title == HistoryCard::FAILED_TITLE)
        );
        let report = report_card(&AnalyticsCard::Failed(failed("Calls are down.")));
        assert!(
            matches!(&report.status, LoadStatus::Failed { title, .. } if title == AnalyticsCard::FAILED_TITLE)
        );
        assert_eq!(report.title, CALLS_TITLE);
    }

    #[test]
    fn the_trend_counts_days_then_weeks() {
        assert_eq!(trend_title(AnalyticsRange::SevenDays), "Calls per day");
        assert_eq!(trend_title(AnalyticsRange::ThirtyDays), "Calls per day");
        assert_eq!(trend_title(AnalyticsRange::NinetyDays), "Calls per week");
        assert_eq!(report_title(AnalyticsRange::ThirtyDays), "Calls over 30 days");
    }

    #[test]
    fn a_month_with_only_unmetered_measures_is_nothing_yet() {
        let month: district_model::UsageMonth =
            serde_json::from_value(serde_json::json!({ "month": "2026-08" })).unwrap();
        let usage = usage_card(&UsageCard::Ready {
            month: Some(month),
            refreshing: false,
        });
        assert_eq!(usage.month, None);
        assert!(usage.lines.is_empty());
        assert_eq!(usage.empty.as_deref(), Some(UsageCard::NONE));
    }
}
