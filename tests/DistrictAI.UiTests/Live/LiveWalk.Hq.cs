using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

namespace DistrictAI.UiTests.Live;

/// <summary>District HQ and Support: one read-only question, Report opened and cancelled, and the support draft's refusal.</summary>
internal sealed partial class LiveWalk
{
    /// <summary>The Report dialog's title (Views/ReportDialog.xaml).</summary>
    private const string ReportTitle = "Report AI-generated content";

    /// <summary>What Report shows while a support request is being written (district-ffi report.rs, DRAFT_OPEN).</summary>
    private const string DraftOpen = "Send or discard the support request you are writing in Support, then report this.";

    private bool _answered;

    /// <summary>
    /// One fixed, read-only question: the answer renders. A proposed change
    /// is never confirmed (the denylist holds "Confirm"); Report opens on
    /// the answer and is cancelled.
    /// </summary>
    private Outcome Hq(Check check)
    {
        if (Config.HqQuestion is not { Length: > 0 })
        {
            throw new PreconditionException("live-config.json names no hqQuestion");
        }
        _ = Ui.Go(App, "District HQ", "District HQ");
        var before = Answers().Length;
        Ui.Type(App, "Ask District HQ", Config.HqQuestion);
        Ui.Press(App, "Ask");
        var answer = Wait.For(
            () => App.TryFind(ControlType.ProgressBar, "Thinking.") is null && Answers().Length > before ? Answers()[^1] : null,
            TimeSpan.FromSeconds(150),
            "District HQ's answer",
            App.Describe);
        Check.Expect(UiTests.Walk.NameOf(answer).Length > "District HQ answered: ".Length, "the answer is empty");
        _answered = true;
        check.Shot();
        var proposed = Shows(App, ControlType.Button, "Confirm");
        if (proposed)
        {
            // A read-only question should propose nothing; it is dismissed, never confirmed.
            Ui.Press(App, "Dismiss");
        }
        Ui.Press(App, "Report", type: ControlType.Hyperlink);
        var report = Ui.Dialog(App, ReportTitle);
        check.Shot();
        Ui.PressIn(App, report, "Cancel");
        Check.Expect(Wait.Until(() => !Ui.HasDialog(App, ReportTitle), Ui.Step), "the Report dialog stayed after Cancel");
        return Outcome.Pass("the answer rendered; Report opened on it and was cancelled (nothing sent)"
            + (proposed ? "; it proposed a change, which was dismissed, not confirmed" : string.Empty));
    }

    /// <summary>
    /// A support request started and not sent; Report on the HQ answer then
    /// refuses with the core's support-draft line and Send off; Cancel; the
    /// request discarded.
    /// </summary>
    private Outcome SupportReport(Check check)
    {
        if (!_answered)
        {
            throw new PreconditionException("no HQ answer to report on (see hq)");
        }
        RequireRunId();
        _ = Ui.Go(App, "Support", "Support");
        Ui.Press(App, "New support request");
        // The refusal needs a draft with something in it.
        Ui.Type(App, "Subject", $"{Config.Prefix} draft");
        Ui.Type(App, "What is happening", "A draft the win-smoke walk writes and discards, never sends.");
        try
        {
            _ = Ui.Go(App, "District HQ", "District HQ");
            Check.Expect(Answers().Length > 0, "the HQ answer is gone");
            Ui.Press(App, "Report", type: ControlType.Hyperlink);
            var report = Ui.Dialog(App, ReportTitle);
            _ = Wait.For(
                () => App.TryMainWindow()?.FindAllDescendants(cf => cf.ByControlType(ControlType.Text))
                    .FirstOrDefault(text => UiTests.Walk.NameOf(text).Contains(DraftOpen, StringComparison.Ordinal))
                    ?? App.TryFind(null, "Report not sent"),
                Ui.Step,
                "the support draft's refusal",
                App.Describe);
            var send = InstalledApp.TryFindIn(report, ControlType.Button, "Send");
            Check.Expect(send is null || !send.IsEnabled, "Send is enabled in the Report dialog while a support request is open");
            check.Shot();
            Ui.PressIn(App, report, "Cancel");
            Check.Expect(Wait.Until(() => !Ui.HasDialog(App, ReportTitle), Ui.Step), "the Report dialog stayed after Cancel");
        }
        finally
        {
            _ = Ui.Go(App, "Support", "Support");
            Ui.Press(App, "Discard the support request");
        }
        Check.Expect(Wait.Until(() => !Shows(App, ControlType.Edit, "Subject"), Ui.Step), "the support request's form stayed after Discard");
        return Outcome.Pass("with a support request open, Report shows the support-draft line and Send is off; cancelled; the request discarded unsent");
    }

    /// <summary>HQ's answers on screen, oldest first: elements named "District HQ answered: ...".</summary>
    private AutomationElement[] Answers() =>
        App.TryMainWindow()?.FindAllDescendants(cf => cf.ByControlType(ControlType.Text).Or(cf.ByControlType(ControlType.Document)))
            .Where(element => UiTests.Walk.NameOf(element).StartsWith("District HQ answered: ", StringComparison.Ordinal))
            .ToArray() ?? [];
}
