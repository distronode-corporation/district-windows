using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

namespace DistrictAI.UiTests.Live;

/// <summary>The owner's read-only checks: every area, Analytics, Phone numbers.</summary>
internal sealed partial class LiveWalk
{
    /// <summary>The settings sections the hub offers an owner, each opened by its row (the tenth row, Phone numbers, is a pane entry too).</summary>
    public static readonly string[] SettingsSections =
    [
        "Persona",
        "Voice",
        "Call handling",
        "Call routing rules",
        "Transfer directory",
        "Skills",
        "Knowledge",
        "Messaging accounts",
        "Members",
    ];

    /// <summary>The analytics periods (the core's analytics.rs, range labels).</summary>
    private static readonly string[] _periods = ["7 days", "30 days", "90 days"];

    /// <summary>
    /// Every pane entry and every settings section opens with its level-one
    /// heading, finishes loading and shows no failure; each is saved.
    /// </summary>
    private Outcome Areas(Check check)
    {
        var window = App.TryMainWindow() ?? throw new CheckFailedException("no main window");
        var entries = UiTests.Walk.NavEntries(window);
        Check.Expect(entries.Length >= 5, $"the pane offers {entries.Length} entries");
        var problems = new List<string>();
        var opened = 0;
        foreach (var entry in entries)
        {
            var expected = entry == "Overview" ? Config.WorkspaceName
                : entry.StartsWith("Inbox", StringComparison.Ordinal) ? "Conversations"
                : entry;
            if (Open(entry, () => UiTests.Walk.Open(App, entry), expected, problems))
            {
                opened++;
                check.Shot();
            }
            if (entry != "Workspace settings")
            {
                continue;
            }
            foreach (var section in SettingsSections)
            {
                if (Open(section, () => UiTests.Walk.OpenSettingsRow(App, section), section, problems))
                {
                    opened++;
                    check.Shot();
                }
                _ = Open(entry, () => UiTests.Walk.Open(App, entry), entry, problems);
            }
        }
        return problems.Count == 0
            ? Outcome.Pass($"{opened} pages opened with their heading and no failure: {entries.Length} pane entries and {SettingsSections.Length} settings sections")
            : Outcome.Fail($"{problems.Count} problem(s): {string.Join("; ", problems)}");
    }

    /// <summary>Opens a page and checks it as the scripted walk does (Walk.Visit), without its screenshots.</summary>
    private bool Open(string name, Action open, string expected, List<string> problems)
    {
        var before = problems.Count;
        var problemsHere = new List<string>();
        _ = UiTests.Walk.Visit(App, Handle, name, open, expected, null, 0, problemsHere);
        // The dump each problem carries goes to the log, not the result.
        foreach (var problem in problemsHere)
        {
            InstalledApp.Log(problem);
            var cut = problem.IndexOf(" UI Automation held:", StringComparison.Ordinal);
            var line = problem.IndexOf(Environment.NewLine, StringComparison.Ordinal);
            var end = new[] { cut, line }.Where(at => at > 0).DefaultIfEmpty(problem.Length).Min();
            problems.Add(problem[..end]);
        }
        return problems.Count == before;
    }

    /// <summary>
    /// Each period of Analytics renders its figures, and every chart on it
    /// has a name: the sentence Narrator reads.
    /// </summary>
    private Outcome Analytics(Check check)
    {
        _ = Ui.Go(App, "Analytics", "Analytics");
        var charts = 0;
        var unnamed = new List<string>();
        foreach (var period in _periods)
        {
            var choice = Ui.Find(App, ControlType.RadioButton, period);
            choice.Patterns.SelectionItem.Pattern.Select();
            // The figures' heading names the period they are for, once they are.
            _ = Wait.For(
                () => App.TryMainWindow()?.FindAllDescendants(cf => cf.ByControlType(ControlType.Text))
                    .FirstOrDefault(text => UiTests.Walk.NameOf(text) == $"Calls over {period}"
                        && text.Properties.HeadingLevel.ValueOrDefault == HeadingLevel.Level2),
                Ui.Step,
                $"the figures for {period}",
                App.Describe);
            _ = Wait.Until(() => App.TryFind(ControlType.ProgressBar, "Refreshing") is null, Ui.Step);
            var frames = App.TryMainWindow()?.FindAllDescendants(cf => cf.ByClassName("ChartFrame"))
                .Where(frame => !frame.Properties.IsOffscreen.ValueOrDefault)
                .ToArray() ?? [];
            charts += frames.Length;
            unnamed.AddRange(frames.Where(frame => UiTests.Walk.NameOf(frame).Trim().Length == 0).Select(_ => period));
            InstalledApp.Log($"{period}: {frames.Length} chart(s): {string.Join(" | ", frames.Select(frame => UiTests.Walk.NameOf(frame)))}");
            check.Shot();
        }
        if (charts == 0)
        {
            return Outcome.Fail("no chart showed in any period (the call funnel always shows once loaded)");
        }
        return unnamed.Count == 0
            ? Outcome.Pass($"7, 30 and 90 days each rendered; all {charts} charts have a name")
            : Outcome.Fail($"{unnamed.Count} of {charts} charts have no name ({string.Join(", ", unnamed.Distinct())})");
    }

    /// <summary>Find a number, area code 416 (Canada): a list of numbers for sale appears. Nothing is bought (the app cannot).</summary>
    private Outcome Numbers(Check check)
    {
        _ = Ui.Go(App, "Phone numbers", "Phone numbers");
        Ui.Find(App, ControlType.RadioButton, "Find a number").Patterns.SelectionItem.Pattern.Select();
        Ui.Type(App, "Country code", "CA");
        Ui.Type(App, "Area code", "416");
        Ui.Press(App, "Search for numbers");
        var outcome = Wait.For(
            () =>
            {
                if (App.TryFind(ControlType.List, "Numbers for sale") is { } list
                    && list.FindAllChildren(cf => cf.ByControlType(ControlType.ListItem)) is { Length: > 0 } rows
                    && App.TryFind(ControlType.ProgressBar, "Searching") is null)
                {
                    return Outcome.Pass($"{rows.Length} numbers for sale in area code 416; no Buy in the app");
                }
                foreach (var (title, result) in new[]
                {
                    ("No matches", CheckResult.Fail),
                    ("No carrier connected", CheckResult.NotAutomated),
                    ("Could not search for numbers", CheckResult.Fail),
                })
                {
                    if (Ui.TryText(App, title) is not null)
                    {
                        return new Outcome(result, $"the search shows \"{title}\"");
                    }
                }
                return (Outcome?)null;
            },
            TimeSpan.FromSeconds(60),
            "numbers for sale, or why not",
            App.Describe);
        check.Shot();
        return outcome;
    }

    /// <summary>The level-two headings on the page now.</summary>
    private string[] Subheadings() =>
        App.TryMainWindow()?.FindAllDescendants(cf => cf.ByControlType(ControlType.Text))
            .Where(text => !text.Properties.IsOffscreen.ValueOrDefault && text.Properties.HeadingLevel.ValueOrDefault == HeadingLevel.Level2)
            .Select(UiTests.Walk.NameOf)
            .ToArray() ?? [];

    /// <summary>The on-screen elements under the main window whose name starts with <paramref name="start"/>.</summary>
    private AutomationElement[] Named(ControlType type, string start) =>
        App.TryMainWindow()?.FindAllDescendants(cf => cf.ByControlType(type))
            .Where(element => !element.Properties.IsOffscreen.ValueOrDefault && UiTests.Walk.NameOf(element).StartsWith(start, StringComparison.Ordinal))
            .ToArray() ?? [];
}
