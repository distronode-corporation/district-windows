//! The analytics charts: the series and fractions the core prepares
//! ([`ChartBar`], [`SentimentShare`], [`HistoryRow`]), as C# draws them.
//!
//! Nothing here works a figure out. Every length is a fraction the core
//! computed, carried as a whole number of thousandths ([`per_mille`]) so the
//! records compare exactly and the snapshots are stable; every number shown is
//! the service's, in the core's words. Each chart also carries one sentence
//! saying what it shows, for Narrator: the chart is one element named by that
//! sentence, and its bars are not read one by one. The sentences are District
//! AI for Linux's (`charts.rs`, `pages/analytics.rs`).

use district_core::{ChartBar, HistoryRow, SentimentShare};
use serde::Serialize;

use crate::l10n::{Locale, l, s};

/// The longest bar, in thousandths of the chart's length.
pub const FULL: u16 = 1000;

/// A bar chart: the trend's columns, or the funnel's rows.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct BarChartView {
    /// What the chart shows, in one sentence: the chart's name for Narrator.
    pub summary: String,
    /// The bars, in the order the service sent them.
    pub bars: Vec<ChartBarView>,
    /// The top of the scale, the largest value: "11".
    pub scale_top: String,
    /// The bottom of the scale: "0".
    pub scale_bottom: String,
    /// The first bar's label, under the start of the axis.
    pub axis_start: String,
    /// The last bar's label, under its end. Empty for one bar.
    pub axis_end: String,
}

/// One bar.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct ChartBarView {
    /// Its label: "Aug 9", "Connected Calls".
    pub label: String,
    /// Its value, as the service sent it.
    pub value: String,
    /// Its length in thousandths of the longest bar's, 0 to [`FULL`].
    pub per_mille: u16,
}

/// The sentiment breakdown: each band's share of the calls.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct SentimentChartView {
    /// What the chart shows, in one sentence: the chart's name for Narrator.
    pub summary: String,
    /// The bands, in the order the service sent them.
    pub bands: Vec<SentimentBandView>,
}

/// One band of the sentiment breakdown.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct SentimentBandView {
    /// Its label, as the service named it: "Positive Sentiment".
    pub label: String,
    /// Its legend line: "Positive Sentiment: 21 (44%)".
    pub legend: String,
    /// Its share of all the calls in thousandths, 0 to [`FULL`].
    pub per_mille: u16,
    /// Which band it is, for its fill: told apart by pattern as well as
    /// colour, so it reads under high contrast.
    pub tone: SentimentTone,
}

/// Which band of the sentiment breakdown, by the service's name for it: it
/// always sends positive, neutral and friction, and names them.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash, Serialize, uniffi::Enum)]
pub enum SentimentTone {
    /// Positive.
    Positive,
    /// Neutral.
    Neutral,
    /// Friction, and any band named otherwise.
    Friction,
}

impl SentimentTone {
    /// The band the service's `name` for it says (`Positive Sentiment`,
    /// `Neutral Sentiment`, `Friction Sentiment`): a wire value, the same in
    /// every language. Never the shown label, which the core translates, so
    /// a French band keeps its colour.
    pub(crate) fn of(name: &str) -> Self {
        let name = name.to_lowercase();
        if name.contains("positive") {
            Self::Positive
        } else if name.contains("neutral") {
            Self::Neutral
        } else {
            Self::Friction
        }
    }
}

/// The usage history: each month's call minutes as a bar against the busiest
/// month's, with its minutes and messages as the service counted them.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct HistoryChartView {
    /// What the chart shows, in one sentence: the chart's name for Narrator.
    pub summary: String,
    /// The column headings: month, the bar's (empty), call minutes, messages.
    pub headings: Vec<String>,
    /// The months, newest first, as the service ordered them.
    pub months: Vec<HistoryMonthView>,
}

/// One month of the usage history.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct HistoryMonthView {
    /// The month: "Aug 2026".
    pub month: String,
    /// Its call minutes, or "Not recorded".
    pub minutes: String,
    /// Its messages, or "Not recorded".
    pub messages: String,
    /// Its minutes bar in thousandths of the busiest month's, 0 to [`FULL`].
    pub per_mille: u16,
}

/// The column headings of the usage history, as District AI for Linux has
/// them.
fn history_headings() -> Vec<String> {
    [
        s::chart_history_month(l()),
        "",
        s::chart_history_minutes(l()),
        s::chart_history_messages(l()),
    ]
    .map(str::to_owned)
    .to_vec()
}

/// A fraction from 0 to 1 in thousandths. A bar above zero never rounds to
/// nothing, and one short of the longest never rounds to as long as it: the
/// drawing says which is larger even when the rounding would not. Anything
/// that is not a number is drawn at zero, and anything out of range at the
/// nearest end.
pub fn per_mille(fraction: f64) -> u16 {
    if fraction.is_nan() || fraction <= 0.0 {
        return 0;
    }
    if fraction >= 1.0 {
        return FULL;
    }
    // In range, so the cast cannot truncate: 0.0 < fraction * 1000 < 1000.
    let rounded = (fraction * f64::from(FULL)).round() as u16;
    rounded.clamp(1, FULL - 1)
}

/// A share as a whole percentage, as the core writes one in its language:
/// `44%`, `44 %` (a no-break space before the sign in French).
pub fn percent(share: f64) -> String {
    let whole = format!("{:.0}", share * 100.0);
    match l() {
        Locale::En => format!("{whole}%"),
        Locale::FrCa => format!("{whole}\u{a0}%"),
    }
}

/// `bars` as columns over time, named `what` ("Calls per day"): each bar,
/// and the largest.
pub(crate) fn columns(what: &str, bars: &[ChartBar]) -> BarChartView {
    let each = bars
        .iter()
        .map(|bar| format!("{} {}", bar.label, bar.value))
        .collect::<Vec<_>>()
        .join(", ");
    let summary = match bars.iter().max_by_key(|bar| bar.value) {
        Some(most) => s::chart_columns_summary(l(), what, &each, most.value, &most.label),
        None => s::chart_nothing(l(), what),
    };
    chart(summary, bars)
}

/// `bars` as rows, named `what` ("Call funnel"): each bar and its value.
pub(crate) fn rows(what: &str, bars: &[ChartBar]) -> BarChartView {
    let summary = if bars.is_empty() {
        s::chart_nothing(l(), what)
    } else {
        let each = bars
            .iter()
            .map(|bar| format!("{} {}", bar.label, bar.value))
            .collect::<Vec<_>>()
            .join(", ");
        s::chart_each(l(), what, &each)
    };
    chart(summary, bars)
}

/// The chart of `bars`, named by `summary`.
fn chart(summary: String, bars: &[ChartBar]) -> BarChartView {
    let most = bars.iter().map(|bar| bar.value).max().unwrap_or(0).max(0);
    let label = |bar: Option<&ChartBar>| bar.map(|bar| bar.label.clone()).unwrap_or_default();
    BarChartView {
        summary,
        bars: bars
            .iter()
            .map(|bar| ChartBarView {
                label: bar.label.clone(),
                value: bar.value.to_string(),
                per_mille: per_mille(bar.fraction),
            })
            .collect(),
        scale_top: most.to_string(),
        scale_bottom: "0".to_owned(),
        axis_start: label(bars.first()),
        axis_end: if bars.len() > 1 {
            label(bars.last())
        } else {
            String::new()
        },
    }
}

/// The sentiment breakdown of `bands`.
pub(crate) fn sentiment(bands: &[SentimentShare]) -> SentimentChartView {
    let legend =
        |band: &SentimentShare| format!("{} {} ({})", band.label, band.value, percent(band.share));
    let summary = if bands.is_empty() {
        s::chart_nothing(l(), s::analytics_sentiment_title(l()))
    } else {
        let each = bands.iter().map(legend).collect::<Vec<_>>().join(", ");
        s::chart_each(l(), s::analytics_sentiment_title(l()), &each)
    };
    SentimentChartView {
        summary,
        bands: bands
            .iter()
            .map(|band| SentimentBandView {
                label: band.label.clone(),
                legend: s::chart_legend(l(), &band.label, band.value, &percent(band.share)),
                per_mille: per_mille(band.share),
                tone: SentimentTone::of(&band.name),
            })
            .collect(),
    }
}

/// The usage history of `rows`, which the core scaled against each other.
pub(crate) fn history(rows: &[HistoryRow]) -> HistoryChartView {
    let line = |row: &HistoryRow| {
        s::chart_history_line(l(), &row.month, &row.minutes_text, &row.messages_text)
    };
    let summary = if rows.is_empty() {
        s::chart_nothing(l(), s::analytics_history_title(l()))
    } else {
        let each = rows.iter().map(line).collect::<Vec<_>>().join("; ");
        s::chart_each(l(), s::analytics_history_title(l()), &each)
    };
    HistoryChartView {
        summary,
        headings: history_headings(),
        months: rows
            .iter()
            .map(|row| HistoryMonthView {
                month: row.month.clone(),
                minutes: row.minutes_text.clone(),
                messages: row.messages_text.clone(),
                per_mille: per_mille(row.fraction),
            })
            .collect(),
    }
}

#[cfg(test)]
mod tests {
    use district_core::{NOT_RECORDED, fractions, history_rows};
    use district_model::UsageMonth;

    use super::*;

    fn bars(labels: &[&str], values: &[i64]) -> Vec<ChartBar> {
        labels
            .iter()
            .zip(values)
            .zip(fractions(values))
            .map(|((label, &value), fraction)| ChartBar {
                label: (*label).to_owned(),
                value,
                fraction,
            })
            .collect()
    }

    fn lengths(chart: &BarChartView) -> Vec<u16> {
        chart.bars.iter().map(|bar| bar.per_mille).collect()
    }

    #[test]
    fn a_fraction_is_thousandths_and_the_edges_stay_honest() {
        assert_eq!(per_mille(0.0), 0);
        assert_eq!(per_mille(1.0), FULL);
        assert_eq!(per_mille(0.5), 500);
        assert_eq!(per_mille(0.8181), 818);
        assert_eq!(per_mille(0.8186), 819);
        // Above zero is never drawn flat, short of the longest never as long.
        assert_eq!(per_mille(0.0001), 1);
        assert_eq!(per_mille(0.0004), 1);
        assert_eq!(per_mille(0.9996), 999);
        assert_eq!(per_mille(0.9999), 999);
        // Not a number, and out of range.
        assert_eq!(per_mille(f64::NAN), 0);
        assert_eq!(per_mille(-0.5), 0);
        assert_eq!(per_mille(f64::NEG_INFINITY), 0);
        assert_eq!(per_mille(1.5), FULL);
        assert_eq!(per_mille(f64::INFINITY), FULL);
    }

    #[test]
    fn an_empty_series_draws_nothing_and_says_so() {
        let trend = columns("Calls per day", &[]);
        assert_eq!(trend.summary, "Calls per day: nothing to show.");
        assert!(trend.bars.is_empty());
        assert_eq!(
            (trend.scale_top.as_str(), trend.scale_bottom.as_str()),
            ("0", "0")
        );
        assert_eq!(
            (trend.axis_start.as_str(), trend.axis_end.as_str()),
            ("", "")
        );
        assert_eq!(
            rows("Call funnel", &[]).summary,
            "Call funnel: nothing to show."
        );
        assert_eq!(sentiment(&[]).summary, "Caller sentiment: nothing to show.");
        let history = history(&[]);
        assert_eq!(history.summary, "Recent months: nothing to show.");
        assert!(history.months.is_empty());
    }

    #[test]
    fn a_single_point_is_the_whole_scale_with_one_label() {
        let trend = columns("Calls per day", &bars(&["Aug 9"], &[9]));
        assert_eq!(lengths(&trend), [FULL]);
        assert_eq!(trend.scale_top, "9");
        assert_eq!(
            (trend.axis_start.as_str(), trend.axis_end.as_str()),
            ("Aug 9", "")
        );
        assert_eq!(
            trend.summary,
            "Calls per day: Aug 9 9. The most was 9, on Aug 9."
        );
    }

    #[test]
    fn all_zero_draws_every_bar_at_nothing() {
        let trend = columns("Calls per day", &bars(&["Aug 9", "Aug 10"], &[0, 0]));
        assert_eq!(lengths(&trend), [0, 0]);
        assert_eq!(trend.scale_top, "0");
        let shares = [("Positive Sentiment", 0), ("Neutral Sentiment", 0)].map(|(label, value)| {
            SentimentShare {
                name: label.to_owned(),
                label: label.to_owned(),
                value,
                share: 0.0,
                color: None,
            }
        });
        let chart = sentiment(&shares);
        assert!(chart.bands.iter().all(|band| band.per_mille == 0));
        assert_eq!(
            chart.summary,
            "Caller sentiment: Positive Sentiment 0 (0%), Neutral Sentiment 0 (0%)."
        );
    }

    #[test]
    fn a_chart_says_what_it_shows_as_linux_does() {
        let trend = columns("Calls per day", &bars(&["Aug 9", "Aug 15"], &[9, 11]));
        assert_eq!(
            trend.summary,
            "Calls per day: Aug 9 9, Aug 15 11. The most was 11, on Aug 15."
        );
        assert_eq!(lengths(&trend), [818, FULL]);
        assert_eq!(trend.bars[0].value, "9");
        assert_eq!(
            (trend.axis_start.as_str(), trend.axis_end.as_str()),
            ("Aug 9", "Aug 15")
        );
        let funnel = rows(
            "Call funnel",
            &bars(&["Total Dials", "Connected Calls"], &[48, 35]),
        );
        assert_eq!(
            funnel.summary,
            "Call funnel: Total Dials 48, Connected Calls 35."
        );
        assert_eq!(lengths(&funnel), [FULL, 729]);
    }

    #[test]
    fn sentiment_bands_are_told_apart_by_name() {
        let shares = [
            ("Positive Sentiment", 2, 2.0 / 3.0),
            ("Friction", 1, 1.0 / 3.0),
            ("Neutral", 0, 0.0),
        ]
        .map(|(label, value, share)| SentimentShare {
            name: label.to_owned(),
            label: label.to_owned(),
            value,
            share,
            color: None,
        });
        let chart = sentiment(&shares);
        assert_eq!(
            chart.summary,
            "Caller sentiment: Positive Sentiment 2 (67%), Friction 1 (33%), Neutral 0 (0%)."
        );
        assert_eq!(
            chart
                .bands
                .iter()
                .map(|band| (band.tone, band.per_mille, band.legend.as_str()))
                .collect::<Vec<_>>(),
            [
                (SentimentTone::Positive, 667, "Positive Sentiment: 2 (67%)"),
                (SentimentTone::Friction, 333, "Friction: 1 (33%)"),
                (SentimentTone::Neutral, 0, "Neutral: 0 (0%)"),
            ]
        );
        assert_eq!(SentimentTone::of("Anything else"), SentimentTone::Friction);
        assert_eq!(percent(0.0), "0%");
    }

    /// In French the bands keep their tones, which follow the service's
    /// name for each band and never the translated label, and a share takes
    /// its space.
    #[test]
    fn the_sentiment_bands_keep_their_tones_in_french() {
        let shares = [
            ("Positive Sentiment", "Sentiment positif", 21, 0.4375),
            ("Neutral Sentiment", "Sentiment neutre", 16, 0.3333),
            ("Friction Sentiment", "Sentiment de friction", 11, 0.2292),
        ]
        .map(|(name, label, value, share)| SentimentShare {
            name: name.to_owned(),
            label: label.to_owned(),
            value,
            share,
            color: None,
        });
        let chart = district_l10n::with_locale(Locale::FrCa, || sentiment(&shares));
        assert_eq!(
            chart.bands.iter().map(|band| band.tone).collect::<Vec<_>>(),
            [
                SentimentTone::Positive,
                SentimentTone::Neutral,
                SentimentTone::Friction
            ]
        );
        assert_eq!(
            chart
                .bands
                .iter()
                .map(|band| band.label.as_str())
                .collect::<Vec<_>>(),
            [
                "Sentiment positif",
                "Sentiment neutre",
                "Sentiment de friction"
            ]
        );
        assert!(
            chart.bands[0].legend.ends_with("21 (44\u{a0}%)"),
            "{}",
            chart.bands[0].legend
        );
        assert_eq!(
            district_l10n::with_locale(Locale::FrCa, || percent(1.0)),
            "100\u{a0}%"
        );
    }

    #[test]
    fn the_history_names_every_month_and_what_was_not_recorded() {
        let month = |key: &str, minutes: Option<f64>| -> UsageMonth {
            serde_json::from_value(serde_json::json!({
                "month": key,
                "callMinutesOutbound": minutes,
            }))
            .unwrap()
        };
        let chart = history(&history_rows(&[
            month("2026-08", Some(100.0)),
            month("2026-07", Some(25.5)),
            month("2026-06", None),
        ]));
        assert_eq!(chart.headings, ["Month", "", "Call minutes", "Messages"]);
        assert_eq!(
            chart
                .months
                .iter()
                .map(|row| (row.month.as_str(), row.minutes.as_str(), row.per_mille))
                .collect::<Vec<_>>(),
            [
                ("Aug 2026", "100", FULL),
                ("Jul 2026", "25.5", 255),
                ("Jun 2026", NOT_RECORDED, 0),
            ]
        );
        assert_eq!(
            chart.summary,
            "Recent months: Aug 2026: 100 call minutes, Not recorded messages; \
             Jul 2026: 25.5 call minutes, Not recorded messages; \
             Jun 2026: Not recorded call minutes, Not recorded messages."
        );
    }
}
