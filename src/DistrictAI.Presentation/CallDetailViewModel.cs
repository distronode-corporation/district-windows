using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DistrictAI.Core.Ffi;

namespace DistrictAI.ViewModels;

/// <summary>One call: its facts, the AI summary and analysis, and the transcript.</summary>
public sealed partial class CallDetailViewModel : ObservableObject
{
    private PageContext? _context;

    /// <summary>Loading, failure and a failed refresh.</summary>
    public LoadStateViewModel Load { get; } = new();

    /// <summary>Number, direction, start, duration and outcome.</summary>
    public ObservableCollection<FactItem> Facts { get; } = [];

    /// <summary>The follow-up sent after the call.</summary>
    public ObservableCollection<FactItem> FollowUp { get; } = [];

    /// <summary>Sentiment, intent and the rest of the AI analysis.</summary>
    public ObservableCollection<FactItem> Analysis { get; } = [];

    /// <summary>The call, as the core names it.</summary>
    public string CallId { get; private set; } = string.Empty;

    /// <summary>What a report is about.</summary>
    public ReportAvailability Report { get; private set; }

    /// <summary>The number "Call" dials back, E.164, or empty for none.</summary>
    public string CallbackNumber { get; private set; } = string.Empty;

    /// <summary>Whether "Call" is offered: there is a number to call back, in a build that can carry calls.</summary>
    [ObservableProperty]
    public partial bool CanCall { get; set; }

    /// <summary>Who called or was called.</summary>
    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;

    /// <summary>When it started, in local time, and how long it lasted.</summary>
    [ObservableProperty]
    public partial string Subtitle { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="Subtitle"/>.</summary>
    [ObservableProperty]
    public partial bool HasSubtitle { get; set; }

    /// <summary>Whether a follow-up was sent.</summary>
    [ObservableProperty]
    public partial bool HasFollowUp { get; set; }

    /// <summary>Whether there is an AI summary.</summary>
    [ObservableProperty]
    public partial bool HasSummary { get; set; }

    /// <summary>The label over the summary.</summary>
    [ObservableProperty]
    public partial string SummaryLabel { get; set; } = string.Empty;

    /// <summary>The summary.</summary>
    [ObservableProperty]
    public partial string SummaryText { get; set; } = string.Empty;

    /// <summary>Whether there is any analysis.</summary>
    [ObservableProperty]
    public partial bool HasAnalysis { get; set; }

    /// <summary>Whether the transcript is loading.</summary>
    [ObservableProperty]
    public partial bool TranscriptLoading { get; set; }

    /// <summary>The transcript, or empty.</summary>
    [ObservableProperty]
    public partial string TranscriptText { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="TranscriptText"/>.</summary>
    [ObservableProperty]
    public partial bool HasTranscriptText { get; set; }

    /// <summary>Why there is no transcript, or why it failed to load.</summary>
    [ObservableProperty]
    public partial string TranscriptMessage { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="TranscriptMessage"/>.</summary>
    [ObservableProperty]
    public partial bool HasTranscriptMessage { get; set; }

    /// <summary>Whether "Try again" is offered for the transcript.</summary>
    [ObservableProperty]
    public partial bool CanRetryTranscript { get; set; }

    /// <summary>The Report button's words.</summary>
    [ObservableProperty]
    public partial string ReportLabel { get; set; } = string.Empty;

    /// <summary>Whether there is a Report button.</summary>
    [ObservableProperty]
    public partial bool ReportVisible { get; set; }

    /// <summary>Whether the Report button can be pressed now.</summary>
    [ObservableProperty]
    public partial bool ReportEnabled { get; set; }

    internal void Attach(PageContext context)
    {
        _context = context;
        Load.Attach(context);
    }

    internal void Show(CallDetailView view, bool reportSending)
    {
        CallId = view.CallId;
        Report = view.Report;
        Title = view.Title;
        Load.Show(view.Status, hasRows: true, empty: null, refreshing: false, view.RefreshFailure);
        Display.Sync(Facts, [.. view.Facts.Select(FactItem.From)]);
        Display.Sync(Analysis, [.. view.Analysis.Select(FactItem.From)]);
        Display.Sync(FollowUp, [.. view.FollowUp.Select(FactItem.From)]);
        HasFollowUp = view.FollowUp.Length > 0;
        Subtitle = string.Join(" \u00B7 ", new[] { Display.When(view.StartedAt), view.DurationLabel ?? string.Empty }.Where(part => part.Length > 0));
        HasSubtitle = Subtitle.Length > 0;
        HasAnalysis = view.Analysis.Length > 0;
        HasSummary = view.Summary is not null;
        SummaryLabel = view.Summary?.Label ?? string.Empty;
        SummaryText = view.Summary?.Text ?? string.Empty;
        ShowTranscript(view.Transcript);
        ReportLabel = Display.ReportLabel(view.Report);
        ReportVisible = ReportLabel.Length > 0;
        ReportEnabled = !reportSending;
        CallbackNumber = view.CallbackNumber ?? string.Empty;
        CanCall = CallbackNumber.Length > 0 && (_context?.CallsAvailable ?? false);
    }

    private void ShowTranscript(TranscriptState transcript)
    {
        TranscriptLoading = transcript is TranscriptState.Loading;
        TranscriptText = transcript is TranscriptState.Ready ready ? ready.Text : string.Empty;
        HasTranscriptText = TranscriptText.Length > 0;
        TranscriptMessage = transcript switch
        {
            TranscriptState.Absent absent => absent.Message,
            TranscriptState.Failed failed => Display.Failure(failed.Failure),
            _ => string.Empty,
        };
        HasTranscriptMessage = TranscriptMessage.Length > 0;
        CanRetryTranscript = transcript is TranscriptState.Failed { Failure.Retryable: true };
    }

    /// <summary>What a report about this call is about.</summary>
    internal ReportTarget Target() => new ReportTarget.Call(CallId);

    [RelayCommand]
    private void Call() => _context?.Send(new UiEvent.CallNumber(CallbackNumber));

    [RelayCommand]
    private void RetryTranscript() => _context?.Send(new UiEvent.Refresh());
}
