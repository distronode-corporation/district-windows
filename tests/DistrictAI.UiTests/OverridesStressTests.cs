using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using Xunit;
using static DistrictAI.UiTests.Walk;

namespace DistrictAI.UiTests;

/// <summary>
/// QA only (visual-qa/* branches; never merged): the working hours'
/// overrides editor, round after round (QA_STRESS_ROUNDS, 50 by default):
/// an override added, one deleted, Add override cancelled, a delete asked
/// and kept, and every fifth round the editor closed and opened again. A
/// crash is logged with its round and step, the app is started again and the
/// rounds go on, so a job counts its crashes; the test fails if there was any,
/// which has the script read the dumps and print the app's trace.
/// </summary>
public sealed class OverridesStressTests
{
    private const string Scene = "--district-scripted-scene=signed-in";
    private const string WorkspaceName = "Example Dental";
    private static readonly TimeSpan _startTimeout = TimeSpan.FromSeconds(90);

    private static int Rounds =>
        int.TryParse(Environment.GetEnvironmentVariable("QA_STRESS_ROUNDS"), out var rounds) && rounds > 0 ? rounds : 50;

    [Fact]
    public void TheOverridesEditorSurvivesRoundAfterRound()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The UI tests run on Windows only.");
        Assert.SkipUnless(Environment.GetEnvironmentVariable("DISTRICTAI_UI_SCRIPTED") == "1", "The stress walk needs the scripted test package.");
        var crashes = new List<string>();
        var app = Start();
        try
        {
            for (var round = 1; round <= Rounds; round++)
            {
                foreach (var (step, act) in Steps(round))
                {
                    try
                    {
                        act(app);
                    }
                    catch (Exception error) when (!app.IsRunning)
                    {
                        var line = $"CRASH in round {round}, step {step}: the app ended ({error.GetType().Name})";
                        InstalledApp.Log(line);
                        crashes.Add(line);
                        app.Dispose();
                        app = Start();
                        break;
                    }
                }
                InstalledApp.Log($"round {round} done");
            }
        }
        finally
        {
            app.Dispose();
        }
        InstalledApp.Log($"stress: {Rounds} rounds, {crashes.Count} crash(es)");
        Assert.True(crashes.Count == 0, string.Join(Environment.NewLine, crashes));
    }

    private static (string Step, Action<InstalledApp> Act)[] Steps(int round)
    {
        var steps = new List<(string, Action<InstalledApp>)>
        {
            ("add an override", app => Add(app, round)),
            ("delete an override", Delete),
            ("cancel Add override", CancelAdd),
            ("keep an override asked about", KeepAsked),
        };
        if (round % 5 == 0)
        {
            steps.Add(("close and open the editor", Reopen));
        }
        return [.. steps];
    }

    private static InstalledApp Start()
    {
        var app = InstalledApp.Launch(Scene);
        _ = app.MainWindow(_startTimeout);
        _ = Heading(app, WorkspaceName, _startTimeout);
        OpenUntilById(app, "NavScheduling");
        _ = OpenSettingsRowById(app, "SchedulingSectionHours");
        _ = app.FindById("SchedulingHoursSummary", PageTimeout);
        OpenEditor(app);
        return app;
    }

    private static void OpenEditor(InstalledApp app)
    {
        app.FindById("HoursOverridesButton", PageTimeout).AsButton().Invoke();
        _ = app.FindById("HoursOverridesDialog", PageTimeout);
    }

    private static void Add(InstalledApp app, int round)
    {
        Press(AddButton(app));
        app.FindById("HoursOverrideDate", PageTimeout, onScreen: false).AsTextBox().Text = $"2099-12-{(round % 28) + 1:00}";
        Press(app.FindById("HoursOverrideSave", PageTimeout, onScreen: false));
        PutAway(app, "HoursOverridesNotice");
    }

    private static void Delete(InstalledApp app)
    {
        Press(FirstDelete(app));
        Press(app.FindById("HoursOverrideDeleteConfirm", PageTimeout, onScreen: false));
        PutAway(app, "HoursOverridesNotice");
    }

    private static void CancelAdd(InstalledApp app)
    {
        Press(AddButton(app));
        Press(app.FindById("HoursOverrideCancel", PageTimeout, onScreen: false));
        _ = AddButton(app);
    }

    private static void KeepAsked(InstalledApp app)
    {
        Press(FirstDelete(app));
        Press(app.FindById("HoursOverrideDeleteCancel", PageTimeout, onScreen: false));
        Expect(app, () => app.TryFindById("HoursOverrideDeleteTitle", onScreen: false) is null, "the delete's question to go after Cancel");
    }

    private static void Reopen(InstalledApp app)
    {
        Keyboard.Type(VirtualKeyShort.ESCAPE);
        Expect(app, () => app.TryFindById("HoursOverridesDialog") is null, "the overrides' editor to close after Escape");
        OpenEditor(app);
    }

    private static AutomationElement AddButton(InstalledApp app) => Wait.For(
        () => app.TryFindById("HoursOverrideAdd", onScreen: false) is { IsEnabled: true } add ? add : null,
        PageTimeout,
        "Add override to work",
        app.Describe);

    private static AutomationElement FirstDelete(InstalledApp app) => Wait.For(
        () => app.TryMainWindow()?.FindAllDescendants(cf => cf.ByAutomationId("HoursOverrideDelete")).FirstOrDefault(found => found.IsEnabled),
        PageTimeout,
        "an override's Delete",
        app.Describe);

    private static void PutAway(InstalledApp app, string noticeId)
    {
        var notice = app.FindById(noticeId, PageTimeout, onScreen: false);
        Press(Wait.For(
            () => notice.FindFirstDescendant(cf => cf.ByControlType(ControlType.Button)),
            PageTimeout,
            $"the close button of {noticeId}",
            app.Describe));
        Expect(app, () => app.TryFindById(noticeId, onScreen: false) is null, $"{noticeId} to close");
    }

    private static void Expect(InstalledApp app, Func<bool> condition, string what) =>
        _ = Wait.For(() => condition() ? what : null, PageTimeout, what, app.Describe);
}
