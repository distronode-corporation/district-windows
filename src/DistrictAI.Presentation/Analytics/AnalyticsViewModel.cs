using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DistrictAI.Core.Ffi;

namespace DistrictAI.ViewModels.Analytics;

/// <summary>
/// The analytics: the period chooser, the calls over the period with their
/// charts, this month's usage and the last few months of it. Each card loads
/// and fails apart (its own <see cref="LoadStateViewModel"/>); the page fails
/// as a whole only when all three did.
/// </summary>
public sealed partial class AnalyticsViewModel : ObservableObject
{
    private PageContext? _context;
    private AnalyticsPeriod[] _periods = [];

    /// <summary>The page as a whole: failed, with Try again, only when every read failed.</summary>
    public LoadStateViewModel Load { get; } = new();

    /// <summary>The calls card: loading, its failure (with Try again), or its figures.</summary>
    public LoadStateViewModel ReportLoad { get; } = new();

    /// <summary>This month's usage card.</summary>
    public LoadStateViewModel UsageLoad { get; } = new();

    /// <summary>The usage history card.</summary>
    public LoadStateViewModel HistoryLoad { get; } = new();

    /// <summary>The core's view of the screen.</summary>
    [ObservableProperty]
    public partial AnalyticsView? View { get; set; }

    /// <summary>The heading.</summary>
    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;

    /// <summary>The period chooser's label.</summary>
    [ObservableProperty]
    public partial string PeriodLabel { get; set; } = string.Empty;

    /// <summary>The periods' labels, in the order of their buttons.</summary>
    public ObservableCollection<string> Periods { get; } = [];

    /// <summary>Which of <see cref="Periods"/> is selected, or -1.</summary>
    [ObservableProperty]
    public partial int SelectedPeriod { get; set; } = -1;

    /// <summary>The calls card's heading: "Calls over 7 days".</summary>
    [ObservableProperty]
    public partial string ReportTitle { get; set; } = string.Empty;

    /// <summary>The five figures.</summary>
    public ObservableCollection<FactItem> Figures { get; } = [];

    /// <summary>How call volume moved against the period before.</summary>
    [ObservableProperty]
    public partial string Change { get; set; } = string.Empty;

    /// <summary>What a bar of the trend stands for.</summary>
    [ObservableProperty]
    public partial string TrendTitle { get; set; } = string.Empty;

    /// <summary>The trend, as columns.</summary>
    public BarChartViewModel Trend { get; } = new();

    /// <summary>Whether the trend is drawn.</summary>
    [ObservableProperty]
    public partial bool HasTrend { get; set; }

    /// <summary>What to say instead of a trend with no calls.</summary>
    [ObservableProperty]
    public partial string TrendEmpty { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="TrendEmpty"/>.</summary>
    [ObservableProperty]
    public partial bool HasTrendEmpty { get; set; }

    /// <summary>The funnel's heading.</summary>
    [ObservableProperty]
    public partial string FunnelTitle { get; set; } = string.Empty;

    /// <summary>The funnel, as rows.</summary>
    public BarChartViewModel Funnel { get; } = new();

    /// <summary>The sentiment breakdown's heading.</summary>
    [ObservableProperty]
    public partial string SentimentTitle { get; set; } = string.Empty;

    /// <summary>The sentiment breakdown's sentence, its name for Narrator.</summary>
    [ObservableProperty]
    public partial string SentimentSummary { get; set; } = string.Empty;

    /// <summary>The sentiment breakdown's bands.</summary>
    public ObservableCollection<SentimentBandItem> SentimentBands { get; } = [];

    /// <summary>Whether the sentiment breakdown is drawn.</summary>
    [ObservableProperty]
    public partial bool HasSentiment { get; set; }

    /// <summary>What to say instead of a breakdown of no calls.</summary>
    [ObservableProperty]
    public partial string SentimentEmpty { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="SentimentEmpty"/>.</summary>
    [ObservableProperty]
    public partial bool HasSentimentEmpty { get; set; }

    /// <summary>This month's usage card's heading.</summary>
    [ObservableProperty]
    public partial string UsageTitle { get; set; } = string.Empty;

    /// <summary>The month the usage is for, or empty.</summary>
    [ObservableProperty]
    public partial string UsageMonth { get; set; } = string.Empty;

    /// <summary>Each measure metered this month.</summary>
    public ObservableCollection<FactItem> UsageLines { get; } = [];

    /// <summary>What to say when nothing has been metered this month.</summary>
    [ObservableProperty]
    public partial string UsageEmpty { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="UsageEmpty"/>.</summary>
    [ObservableProperty]
    public partial bool HasUsageEmpty { get; set; }

    /// <summary>The usage history card's heading.</summary>
    [ObservableProperty]
    public partial string HistoryTitle { get; set; } = string.Empty;

    /// <summary>What the history's bars compare.</summary>
    [ObservableProperty]
    public partial string HistoryCaption { get; set; } = string.Empty;

    /// <summary>The history's sentence, its name for Narrator.</summary>
    [ObservableProperty]
    public partial string HistorySummary { get; set; } = string.Empty;

    /// <summary>The month column's heading.</summary>
    [ObservableProperty]
    public partial string HistoryMonthHeading { get; set; } = string.Empty;

    /// <summary>The call minutes column's heading.</summary>
    [ObservableProperty]
    public partial string HistoryMinutesHeading { get; set; } = string.Empty;

    /// <summary>The messages column's heading.</summary>
    [ObservableProperty]
    public partial string HistoryMessagesHeading { get; set; } = string.Empty;

    /// <summary>The months, newest first.</summary>
    public ObservableCollection<HistoryMonthItem> HistoryMonths { get; } = [];

    /// <summary>Whether the history is drawn.</summary>
    [ObservableProperty]
    public partial bool HasHistory { get; set; }

    /// <summary>What to say when nothing has been metered in any month.</summary>
    [ObservableProperty]
    public partial string HistoryEmpty { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="HistoryEmpty"/>.</summary>
    [ObservableProperty]
    public partial bool HasHistoryEmpty { get; set; }

    internal void Attach(PageContext context)
    {
        _context = context;
        Load.Attach(context);
        ReportLoad.Attach(context);
        UsageLoad.Attach(context);
        HistoryLoad.Attach(context);
    }

    internal void Show(AnalyticsView view)
    {
        View = view;
        Load.Show(view.Status, hasRows: true, empty: null, view.Refreshing, refreshFailure: null);
        Title = view.Title;
        PeriodLabel = view.PeriodLabel;
        _periods = [.. view.Periods.Select(period => period.Period)];
        Display.Sync(Periods, [.. view.Periods.Select(period => period.Label)]);
        SelectedPeriod = Array.FindIndex(view.Periods, period => period.Selected);

        var report = view.Report;
        ReportLoad.Show(report.Status, hasRows: true, empty: null, report.Refreshing, refreshFailure: null);
        ReportTitle = report.Title;
        Display.Sync(Figures, [.. report.Figures.Select(FactItem.From)]);
        Change = report.Change;
        TrendTitle = report.TrendTitle;
        Trend.Show(report.Trend);
        HasTrend = report.Trend is not null;
        TrendEmpty = report.TrendEmpty ?? string.Empty;
        HasTrendEmpty = report.TrendEmpty is not null;
        FunnelTitle = report.FunnelTitle;
        Funnel.Show(report.Funnel);
        SentimentTitle = report.SentimentTitle;
        SentimentSummary = report.Sentiment?.Summary ?? string.Empty;
        Display.Sync(SentimentBands, [.. (report.Sentiment?.Bands ?? []).Select(SentimentBandItem.From)]);
        HasSentiment = report.Sentiment is not null;
        SentimentEmpty = report.SentimentEmpty ?? string.Empty;
        HasSentimentEmpty = report.SentimentEmpty is not null;

        var usage = view.Usage;
        UsageLoad.Show(usage.Status, hasRows: true, empty: null, usage.Refreshing, refreshFailure: null);
        UsageTitle = usage.Title;
        UsageMonth = usage.Month ?? string.Empty;
        Display.Sync(UsageLines, [.. usage.Lines.Select(FactItem.From)]);
        UsageEmpty = usage.Empty ?? string.Empty;
        HasUsageEmpty = usage.Empty is not null;

        var history = view.History;
        HistoryLoad.Show(history.Status, hasRows: true, empty: null, history.Refreshing, refreshFailure: null);
        HistoryTitle = history.Title;
        HistoryCaption = history.Caption;
        var chart = history.Chart;
        HistorySummary = chart?.Summary ?? string.Empty;
        var headings = chart?.Headings ?? [];
        HistoryMonthHeading = headings.ElementAtOrDefault(0) ?? string.Empty;
        HistoryMinutesHeading = headings.ElementAtOrDefault(2) ?? string.Empty;
        HistoryMessagesHeading = headings.ElementAtOrDefault(3) ?? string.Empty;
        Display.Sync(HistoryMonths, [.. (chart?.Months ?? []).Select(HistoryMonthItem.From)]);
        HasHistory = chart is not null;
        HistoryEmpty = history.Empty ?? string.Empty;
        HasHistoryEmpty = history.Empty is not null;
    }

    /// <summary>
    /// Shows the figures for the period at <paramref name="index"/> of
    /// <see cref="Periods"/>. Picking the period already selected sends it
    /// anyway; the core does nothing with it.
    /// </summary>
    internal void SelectPeriod(int index)
    {
        if (index >= 0 && index < _periods.Length)
        {
            Send(new AnalyticsAction.SelectPeriod(_periods[index]));
        }
    }

    /// <summary>Forwards one of the screen's actions.</summary>
    internal void Send(AnalyticsAction action) => _context?.Send(new UiEvent.Analytics(action));
}

/// <summary>A bar chart: the trend's columns, or the funnel's rows. One element, named by its sentence.</summary>
public sealed partial class BarChartViewModel : ObservableObject
{
    /// <summary>What the chart shows, in one sentence: its name for Narrator.</summary>
    [ObservableProperty]
    public partial string Summary { get; set; } = string.Empty;

    /// <summary>The bars, in order.</summary>
    public ObservableCollection<BarItem> Bars { get; } = [];

    /// <summary>
    /// How many columns the bars share the width in: one per bar, and never
    /// fewer than one (a layout of none is not a layout).
    /// </summary>
    [ObservableProperty]
    public partial int Columns { get; set; } = 1;

    /// <summary>The top of the scale.</summary>
    [ObservableProperty]
    public partial string ScaleTop { get; set; } = string.Empty;

    /// <summary>The bottom of the scale.</summary>
    [ObservableProperty]
    public partial string ScaleBottom { get; set; } = string.Empty;

    /// <summary>The label under the start of the axis.</summary>
    [ObservableProperty]
    public partial string AxisStart { get; set; } = string.Empty;

    /// <summary>The label under the end of the axis, or empty.</summary>
    [ObservableProperty]
    public partial string AxisEnd { get; set; } = string.Empty;

    internal void Show(BarChartView? chart)
    {
        Summary = chart?.Summary ?? string.Empty;
        Display.Sync(Bars, [.. (chart?.Bars ?? []).Select(BarItem.From)]);
        Columns = Math.Max(1, Bars.Count);
        ScaleTop = chart?.ScaleTop ?? string.Empty;
        ScaleBottom = chart?.ScaleBottom ?? string.Empty;
        AxisStart = chart?.AxisStart ?? string.Empty;
        AxisEnd = chart?.AxisEnd ?? string.Empty;
    }
}

/// <summary>
/// How a length in thousandths becomes a bar's size: the bar takes
/// <see cref="Filled"/> parts of the track and the space beside it
/// <see cref="Rest"/>, so the page lays them out as two star-sized cells and
/// no pixel arithmetic is done here. A bar above zero is never thinner than
/// <see cref="MinimumLength"/> pixels.
/// </summary>
public static class BarLength
{
    /// <summary>The whole track, in thousandths.</summary>
    public const int Full = 1000;

    /// <summary>The fewest pixels a bar above zero is drawn at.</summary>
    public const double MinimumLength = 2;

    /// <summary>The bar's share of the track: <paramref name="perMille"/>, held to 0 to <see cref="Full"/>.</summary>
    public static double Filled(int perMille) => Math.Clamp(perMille, 0, Full);

    /// <summary>The rest of the track.</summary>
    public static double Rest(int perMille) => Full - Filled(perMille);

    /// <summary>The bar's least size in pixels: none for nothing, <see cref="MinimumLength"/> otherwise.</summary>
    public static double Least(int perMille) => perMille > 0 ? MinimumLength : 0;
}

/// <summary>One bar.</summary>
/// <param name="Label">Its label.</param>
/// <param name="Value">Its value.</param>
/// <param name="PerMille">Its length in thousandths of the longest bar's.</param>
public sealed record BarItem(string Label, string Value, int PerMille)
{
    /// <summary>The bar's share of the track (<see cref="BarLength.Filled"/>).</summary>
    public double Filled => BarLength.Filled(PerMille);

    /// <summary>The rest of the track.</summary>
    public double Rest => BarLength.Rest(PerMille);

    /// <summary>The bar's least size in pixels.</summary>
    public double Least => BarLength.Least(PerMille);

    internal static BarItem From(ChartBarView bar) => new(bar.Label, bar.Value, bar.PerMille);
}

/// <summary>One band of the sentiment breakdown.</summary>
/// <param name="Legend">Its legend line: "Positive Sentiment: 21 (44%)".</param>
/// <param name="PerMille">Its share of all the calls, in thousandths.</param>
/// <param name="Tone">Which band it is.</param>
public sealed record SentimentBandItem(string Legend, int PerMille, SentimentTone Tone)
{
    /// <summary>The bar's share of the track.</summary>
    public double Filled => BarLength.Filled(PerMille);

    /// <summary>The rest of the track.</summary>
    public double Rest => BarLength.Rest(PerMille);

    /// <summary>The bar's least size in pixels.</summary>
    public double Least => BarLength.Least(PerMille);

    /// <summary>Drawn filled: the positive band.</summary>
    public bool IsSolid => Tone == SentimentTone.Positive;

    /// <summary>Drawn as an outline: the neutral band.</summary>
    public bool IsOutline => Tone == SentimentTone.Neutral;

    /// <summary>Drawn as a dashed outline: friction, and any band named otherwise.</summary>
    public bool IsDashed => !IsSolid && !IsOutline;

    internal static SentimentBandItem From(SentimentBandView band) => new(band.Legend, band.PerMille, band.Tone);
}

/// <summary>One month of the usage history.</summary>
/// <param name="Month">The month: "Aug 2026".</param>
/// <param name="Minutes">Its call minutes, or "Not recorded".</param>
/// <param name="Messages">Its messages, or "Not recorded".</param>
/// <param name="PerMille">Its minutes bar in thousandths of the busiest month's.</param>
public sealed record HistoryMonthItem(string Month, string Minutes, string Messages, int PerMille)
{
    /// <summary>The bar's share of the track.</summary>
    public double Filled => BarLength.Filled(PerMille);

    /// <summary>The rest of the track.</summary>
    public double Rest => BarLength.Rest(PerMille);

    /// <summary>The bar's least size in pixels.</summary>
    public double Least => BarLength.Least(PerMille);

    internal static HistoryMonthItem From(HistoryMonthView month) =>
        new(month.Month, month.Minutes, month.Messages, month.PerMille);
}
