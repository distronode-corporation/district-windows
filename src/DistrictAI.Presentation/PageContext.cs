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

    /// <summary>Forwards something the user did.</summary>
    internal void Send(UiEvent uiEvent) => _core.Send(uiEvent);

    /// <summary>Opens <paramref name="url"/> in the user's own browser.</summary>
    internal Task<bool> OpenAsync(string url) => _browser.OpenAsync(url);
}
