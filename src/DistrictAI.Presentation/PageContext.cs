using DistrictAI.Core;
using DistrictAI.Core.Ffi;

namespace DistrictAI.ViewModels;

/// <summary>
/// What every signed-in page sends through: the core, the user's browser, and
/// the little the pages share between them (whether a report is on its way,
/// whether a dialog is open). The window makes one and hands it to each page.
/// </summary>
public sealed class PageContext
{
    private readonly ICoreSink _core;
    private readonly IBrowser _browser;

    internal PageContext(ICoreSink core, IBrowser browser)
    {
        _core = core;
        _browser = browser;
    }

    /// <summary>
    /// Whether the core is sending a report. Every Report button is disabled
    /// meanwhile, so one click is one report.
    /// </summary>
    internal bool ReportSending { get; set; }

    /// <summary>
    /// Whether a dialog is open. Windows shows one at a time, so a second waits
    /// for the first instead of failing.
    /// </summary>
    internal bool DialogOpen { get; set; }

    /// <summary>
    /// Whether this build can carry calls (<see cref="ShellView.CallsAvailable"/>),
    /// so the pages offer "Place a call" and "Call".
    /// </summary>
    internal bool CallsAvailable { get; set; }

    /// <summary>
    /// Why a Report would be refused now (<see cref="ShellView.ReportRefusal"/>:
    /// a support request is being written in Support), or null. The report
    /// dialog says it and does not send, so the draft is not touched.
    /// </summary>
    internal string? ReportRefusal { get; set; }

    /// <summary>The note of a report the dialog could not send, and what it was about.</summary>
    private (ReportTarget Target, string Note)? _keptReportNote;

    /// <summary>
    /// Keeps <paramref name="note"/>, written for a report about
    /// <paramref name="target"/> that was refused, for the next time a report
    /// about it is started. An empty note keeps nothing.
    /// </summary>
    internal void KeepReportNote(ReportTarget target, string note) =>
        _keptReportNote = note.Length > 0 ? (target, note) : null;

    /// <summary>
    /// The note kept for a report about <paramref name="target"/>, handed over
    /// once, or empty. A note kept for anything else is left where it is.
    /// </summary>
    internal string TakeReportNote(ReportTarget target)
    {
        if (_keptReportNote is not { } kept || !kept.Target.Equals(target))
        {
            return string.Empty;
        }
        _keptReportNote = null;
        return kept.Note;
    }

    /// <summary>Forwards something the user did.</summary>
    internal void Send(UiEvent uiEvent) => _core.Send(uiEvent);

    /// <summary>Opens <paramref name="url"/> in the user's own browser.</summary>
    internal Task<bool> OpenAsync(string url) => _browser.OpenAsync(url);
}
