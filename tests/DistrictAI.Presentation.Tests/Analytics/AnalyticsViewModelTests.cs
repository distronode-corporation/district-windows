using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels.Analytics;
using Xunit;

namespace DistrictAI.Presentation.Tests.Analytics;

public sealed class AnalyticsViewModelTests
{
    private static readonly PeriodChoiceView[] _periods =
    [
        new(AnalyticsPeriod.SevenDays, "7 days", true),
        new(AnalyticsPeriod.ThirtyDays, "30 days", false),
        new(AnalyticsPeriod.NinetyDays, "90 days", false),
    ];

    private static BarChartView Chart(string summary, params ChartBarView[] bars) =>
        new(summary, bars, "11", "0", bars.FirstOrDefault()?.Label ?? string.Empty, bars.Length > 1 ? bars[^1].Label : string.Empty);

    private static ReportCardView Report(LoadStatus? status = null, bool refreshing = false) => new(
        "Calls over 7 days",
        status ?? V.Ready,
        refreshing,
        [new FactView("Calls", "48"), new FactView("Average call", "2m 0s")],
        "Up 20% on the previous period.",
        "Calls per day",
        Chart("Calls per day: Aug 9 9, Aug 15 11. The most was 11, on Aug 15.", new("Aug 9", "9", 818), new("Aug 15", "11", 1000)),
        null,
        "Call funnel",
        Chart("Call funnel: Total Dials 48.", new ChartBarView("Total Dials", "48", 1000)),
        "Caller sentiment",
        new SentimentChartView(
            "Caller sentiment: Positive Sentiment 21 (44%), Friction Sentiment 11 (23%).",
            [
                new("Positive Sentiment", "Positive Sentiment: 21 (44%)", 438, SentimentTone.Positive),
                new("Neutral Sentiment", "Neutral Sentiment: 16 (33%)", 333, SentimentTone.Neutral),
                new("Friction Sentiment", "Friction Sentiment: 11 (23%)", 229, SentimentTone.Friction),
            ]),
        null);

    private static UsageCardView Usage(LoadStatus? status = null) =>
        new("This month's usage", "Aug 2026", status ?? V.Ready, false, [new FactView("Texts sent", "412")], null);

    private static HistoryCardView History(LoadStatus? status = null) => new(
        "Recent months",
        "The bars compare metered call minutes across these months.",
        status ?? V.Ready,
        false,
        new HistoryChartView(
            "Recent months: Aug 2026: 1522.75 call minutes, 505 messages.",
            ["Month", "", "Call minutes", "Messages"],
            [new("Aug 2026", "1522.75", "505", 1000), new("Jun 2026", "Not recorded", "12", 0)]),
        null);

    private static AnalyticsView View(
        LoadStatus? status = null,
        ReportCardView? report = null,
        UsageCardView? usage = null,
        HistoryCardView? history = null,
        PeriodChoiceView[]? periods = null,
        bool refreshing = false) =>
        new("Analytics", status ?? V.Ready, "Period", periods ?? _periods, refreshing, report ?? Report(), usage ?? Usage(), history ?? History());

    [Fact]
    public void ItShowsTheFiguresAndEachChartNamedBySentence()
    {
        var model = new AnalyticsViewModel();
        var view = View();
        model.Show(view);

        Assert.Same(view, model.View);
        Assert.Equal("Analytics", model.Title);
        Assert.Equal("Period", model.PeriodLabel);
        Assert.Equal(["7 days", "30 days", "90 days"], model.Periods);
        Assert.Equal(0, model.SelectedPeriod);
        Assert.True(model.Load.Ready);
        Assert.True(model.ReportLoad.Ready);
        Assert.Equal("Calls over 7 days", model.ReportTitle);
        Assert.Equal(["Calls: 48", "Average call: 2m 0s"], model.Figures.Select(fact => fact.AccessibleName));
        Assert.Equal("Up 20% on the previous period.", model.Change);
        Assert.Equal("Calls per day", model.TrendTitle);
        Assert.True(model.HasTrend);
        Assert.False(model.HasTrendEmpty);
        Assert.Equal("Calls per day: Aug 9 9, Aug 15 11. The most was 11, on Aug 15.", model.Trend.Summary);
        Assert.Equal(["Aug 9", "Aug 15"], model.Trend.Bars.Select(bar => bar.Label));
        Assert.Equal(2, model.Trend.Columns);
        Assert.Equal(("11", "0", "Aug 9", "Aug 15"), (model.Trend.ScaleTop, model.Trend.ScaleBottom, model.Trend.AxisStart, model.Trend.AxisEnd));
        Assert.Equal("Call funnel", model.FunnelTitle);
        Assert.Equal("Call funnel: Total Dials 48.", model.Funnel.Summary);
        Assert.Equal("Caller sentiment", model.SentimentTitle);
        Assert.True(model.HasSentiment);
        Assert.StartsWith("Caller sentiment: ", model.SentimentSummary);
        Assert.Equal(
            [(true, false, false), (false, true, false), (false, false, true)],
            model.SentimentBands.Select(band => (band.IsSolid, band.IsOutline, band.IsDashed)));
        Assert.Equal("This month's usage", model.UsageTitle);
        Assert.Equal("Aug 2026", model.UsageMonth);
        Assert.Equal(["Texts sent: 412"], model.UsageLines.Select(line => line.AccessibleName));
        Assert.False(model.HasUsageEmpty);
        Assert.Equal("Recent months", model.HistoryTitle);
        Assert.StartsWith("The bars compare", model.HistoryCaption);
        Assert.True(model.HasHistory);
        Assert.Equal(("Month", "Call minutes", "Messages"), (model.HistoryMonthHeading, model.HistoryMinutesHeading, model.HistoryMessagesHeading));
        Assert.StartsWith("Recent months: Aug 2026", model.HistorySummary);
        Assert.Equal([1000, 0], model.HistoryMonths.Select(month => month.PerMille));
        Assert.Equal("Not recorded", model.HistoryMonths[1].Minutes);
    }

    [Fact]
    public void ThousandthsBecomeTwoStarCellsAndAFloor()
    {
        var full = new BarItem("Aug 15", "11", 1000);
        var most = new BarItem("Aug 9", "9", 818);
        var least = new BarItem("Aug 13", "1", 1);
        var none = new BarItem("Aug 12", "0", 0);
        Assert.Equal((1000d, 0d, 2d), (full.Filled, full.Rest, full.Least));
        Assert.Equal((818d, 182d, 2d), (most.Filled, most.Rest, most.Least));
        Assert.Equal((1d, 999d, 2d), (least.Filled, least.Rest, least.Least));
        Assert.Equal((0d, 1000d, 0d), (none.Filled, none.Rest, none.Least));
        // Out of range is held to the track.
        Assert.Equal((1000d, 0d), (BarLength.Filled(1200), BarLength.Rest(1200)));
        Assert.Equal((0d, 1000d, 0d), (BarLength.Filled(-5), BarLength.Rest(-5), BarLength.Least(-5)));

        var band = new SentimentBandItem("Neutral Sentiment: 16 (33%)", 333, SentimentTone.Neutral);
        Assert.Equal((333d, 667d, 2d), (band.Filled, band.Rest, band.Least));
        var month = new HistoryMonthItem("Jul 2026", "290.75", "355", 191);
        Assert.Equal((191d, 809d, 2d), (month.Filled, month.Rest, month.Least));
    }

    [Fact]
    public void NothingToShowIsSaidRatherThanDrawn()
    {
        var empty = Report() with
        {
            Trend = null,
            TrendEmpty = "No calls in this window.",
            Sentiment = null,
            SentimentEmpty = "No calls to analyse yet.",
        };
        var model = new AnalyticsViewModel();
        model.Show(View(
            report: empty,
            usage: Usage() with { Month = null, Lines = [], Empty = "No usage has been recorded this month yet." },
            history: History() with { Chart = null, Empty = "No usage has been recorded in recent months." }));

        Assert.False(model.HasTrend);
        Assert.Empty(model.Trend.Bars);
        Assert.Equal(1, model.Trend.Columns);
        Assert.Equal(string.Empty, model.Trend.Summary);
        Assert.True(model.HasTrendEmpty);
        Assert.Equal("No calls in this window.", model.TrendEmpty);
        Assert.False(model.HasSentiment);
        Assert.Empty(model.SentimentBands);
        Assert.True(model.HasSentimentEmpty);
        Assert.Equal(string.Empty, model.UsageMonth);
        Assert.Empty(model.UsageLines);
        Assert.True(model.HasUsageEmpty);
        Assert.False(model.HasHistory);
        Assert.Empty(model.HistoryMonths);
        Assert.Equal(string.Empty, model.HistoryMonthHeading);
        Assert.True(model.HasHistoryEmpty);
    }

    [Fact]
    public void EachCardLoadsAndFailsApartAndTheWholePageOnlyWhenAllDid()
    {
        var model = new AnalyticsViewModel();
        model.Show(View(
            report: Report(V.Loading),
            usage: Usage(V.Failed(V.Failure("Usage is down.", retryable: true), "Could not load usage")),
            history: History() with { Refreshing = true },
            refreshing: true));
        Assert.True(model.Load.Ready);
        Assert.True(model.Load.Refreshing);
        Assert.True(model.ReportLoad.Loading);
        Assert.True(model.UsageLoad.Failed);
        Assert.Equal("Could not load usage", model.UsageLoad.FailureTitle);
        Assert.True(model.UsageLoad.CanRetry);
        Assert.True(model.HistoryLoad.Refreshing);

        model.Show(View(status: V.Failed(V.Failure(retryable: true), "Could not load analytics")));
        Assert.True(model.Load.Failed);
        Assert.Equal("Could not load analytics", model.Load.FailureTitle);
        Assert.True(model.Load.CanRetry);
    }

    [Fact]
    public void APeriodIsSentAsItsActionAndTryAgainReadsAgain()
    {
        var model = new AnalyticsViewModel();
        model.SelectPeriod(2);
        model.Send(new AnalyticsAction.Retry());

        var (context, sink) = Pages.Context();
        model.Attach(context);
        model.Show(View(periods:
        [
            _periods[0] with { Selected = false },
            _periods[1],
            _periods[2] with { Selected = true },
        ]));
        Assert.Equal(2, model.SelectedPeriod);
        model.SelectPeriod(1);
        model.SelectPeriod(-1);
        model.SelectPeriod(3);
        model.Send(new AnalyticsAction.Retry());
        model.Load.RetryCommand.Execute(null);

        Assert.Equal(
            [
                new UiEvent.Analytics(new AnalyticsAction.SelectPeriod(AnalyticsPeriod.ThirtyDays)),
                new UiEvent.Analytics(new AnalyticsAction.Retry()),
                new UiEvent.Refresh(),
            ],
            sink.Sent);
    }

    [Fact]
    public void NoPeriodSelectedIsMinusOne()
    {
        var model = new AnalyticsViewModel();
        model.Show(View(periods: [.. _periods.Select(period => period with { Selected = false })]));
        Assert.Equal(-1, model.SelectedPeriod);
    }
}
