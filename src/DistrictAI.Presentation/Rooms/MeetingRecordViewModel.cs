using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DistrictAI.Core.Ffi;

namespace DistrictAI.ViewModels.Rooms;

/// <summary>
/// A meeting's record, over the rooms lobby: when it was and who was there,
/// its minutes and action items (each with Report, as the Companion wrote
/// them), and its whole transcript.
/// </summary>
public sealed partial class MeetingRecordViewModel : ObservableObject
{
    private string _meetingId = string.Empty;

    /// <summary>Its title.</summary>
    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;

    /// <summary>Whether it is being read.</summary>
    [ObservableProperty]
    public partial bool Loading { get; set; }

    /// <summary>Whether it could not be read.</summary>
    [ObservableProperty]
    public partial bool Failed { get; set; }

    /// <summary>What failed: "Could not load this meeting".</summary>
    [ObservableProperty]
    public partial string FailureTitle { get; set; } = string.Empty;

    /// <summary>Why.</summary>
    [ObservableProperty]
    public partial string FailureMessage { get; set; } = string.Empty;

    /// <summary>Whether it is read.</summary>
    [ObservableProperty]
    public partial bool Ready { get; set; }

    /// <summary>Its facts: status, started, ended, length, people.</summary>
    public ObservableCollection<FactItem> Facts { get; } = [];

    /// <summary>"Minutes".</summary>
    [ObservableProperty]
    public partial string MinutesTitle { get; set; } = string.Empty;

    /// <summary>The minutes, or why there are none.</summary>
    [ObservableProperty]
    public partial string Minutes { get; set; } = string.Empty;

    /// <summary>How Report is offered on the minutes.</summary>
    [ObservableProperty]
    public partial ReportAvailability MinutesReport { get; set; } = ReportAvailability.Hidden;

    /// <summary>Report's words on the minutes, or empty for none.</summary>
    [ObservableProperty]
    public partial string MinutesReportLabel { get; set; } = string.Empty;

    /// <summary>Whether Report is offered on the minutes.</summary>
    [ObservableProperty]
    public partial bool HasMinutesReport { get; set; }

    /// <summary>"Action items".</summary>
    [ObservableProperty]
    public partial string ActionItemsTitle { get; set; } = string.Empty;

    /// <summary>The action items.</summary>
    public ObservableCollection<string> ActionItems { get; } = [];

    /// <summary>Whether there are action items.</summary>
    [ObservableProperty]
    public partial bool HasActionItems { get; set; }

    /// <summary>How Report is offered on the action items.</summary>
    [ObservableProperty]
    public partial ReportAvailability ActionItemsReport { get; set; } = ReportAvailability.Hidden;

    /// <summary>Report's words on the action items, or empty for none.</summary>
    [ObservableProperty]
    public partial string ActionItemsReportLabel { get; set; } = string.Empty;

    /// <summary>Whether Report is offered on the action items.</summary>
    [ObservableProperty]
    public partial bool HasActionItemsReport { get; set; }

    /// <summary>Whether a Report button works: no report is on its way.</summary>
    [ObservableProperty]
    public partial bool ReportEnabled { get; set; } = true;

    /// <summary>"Transcript".</summary>
    [ObservableProperty]
    public partial string TranscriptTitle { get; set; } = string.Empty;

    /// <summary>The whole transcript, or empty.</summary>
    [ObservableProperty]
    public partial string Transcript { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="Transcript"/>.</summary>
    [ObservableProperty]
    public partial bool HasTranscript { get; set; }

    /// <summary>What a report on the minutes is about: the meeting, by its id.</summary>
    internal ReportTarget MinutesTarget => new ReportTarget.MeetingMinutes(_meetingId);

    /// <summary>What a report on the action items is about.</summary>
    internal ReportTarget ActionItemsTarget => new ReportTarget.MeetingActionItems(_meetingId);

    internal void Show(MeetingRecordView? record, bool reportSending)
    {
        if (record is null)
        {
            return;
        }
        _meetingId = record.MeetingId;
        Title = record.Title;
        var failed = record.Status as LoadStatus.Failed;
        Loading = record.Status is LoadStatus.Loading;
        Failed = failed is not null;
        FailureTitle = failed?.Title ?? string.Empty;
        FailureMessage = Display.Failure(failed?.Failure);
        Ready = record.Status is LoadStatus.Ready;
        var facts = record.Facts.Select(FactItem.From).ToList();
        var started = Display.When(record.StartedAt);
        if (started.Length > 0)
        {
            facts.Insert(Math.Min(1, facts.Count), new FactItem("Started", started));
        }
        var ended = Display.When(record.EndedAt);
        if (ended.Length > 0)
        {
            facts.Insert(Math.Min(2, facts.Count), new FactItem("Ended", ended));
        }
        Display.Sync(Facts, facts);
        MinutesTitle = record.MinutesTitle;
        Minutes = record.Minutes;
        MinutesReport = record.MinutesReport;
        MinutesReportLabel = Display.ReportLabel(record.MinutesReport);
        HasMinutesReport = MinutesReportLabel.Length > 0;
        ActionItemsTitle = record.ActionItemsTitle;
        Display.Sync(ActionItems, record.ActionItems);
        HasActionItems = record.ActionItems.Length > 0;
        ActionItemsReport = record.ActionItemsReport;
        ActionItemsReportLabel = Display.ReportLabel(record.ActionItemsReport);
        HasActionItemsReport = ActionItemsReportLabel.Length > 0;
        ReportEnabled = !reportSending;
        TranscriptTitle = record.TranscriptTitle;
        Transcript = record.Transcript ?? string.Empty;
        HasTranscript = Transcript.Length > 0;
    }
}
