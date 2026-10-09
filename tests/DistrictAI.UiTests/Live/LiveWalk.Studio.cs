using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

namespace DistrictAI.UiTests.Live;

/// <summary>
/// District Studio's writes, each put back: Persona's name, one skill, a
/// knowledge document, a routing rule and a transfer target; and the
/// unsaved-changes question.
/// </summary>
internal sealed partial class LiveWalk
{
    /// <summary>The skills' labels (the core's tools.rs): any of them is the one toggled.</summary>
    private static readonly string[] _skills =
    [
        "Transfer to a person", "Transfer to a person (fallback)", "Email dispatcher", "Check availability", "Book appointments",
        "Update contacts", "Take a message", "Knowledge base", "Text the caller", "List appointment types", "Read back appointments",
        "Cancel appointments", "Reschedule appointments",
    ];

    private const string Saved = "Saved.";

    private Outcome StudioSave(Check check)
    {
        RequireRunId();
        var done = new List<string>();

        // Persona: the name, " QA" added, saved, read again, put back, saved.
        OpenSection("Persona");
        var name = Ui.Value(App, "Name");
        Check.Expect(name.Length > 0, "Persona's Name is empty");
        Ui.Type(App, "Name", name + " QA");
        SaveAndWait("Save");
        OpenSection("Persona");
        Check.Expect(Ui.Value(App, "Name") == name + " QA", "Persona's name did not keep \" QA\" after Save and reopening");
        check.Shot();
        Ui.Type(App, "Name", name);
        SaveAndWait("Save");
        OpenSection("Persona");
        Check.Expect(Ui.Value(App, "Name") == name, "Persona's name was not put back");
        done.Add("persona name saved, reread and restored");

        // Skills: one toggled, saved, put back, saved.
        OpenSection("Skills");
        var skill = Wait.For(
            () => App.TryMainWindow()?.FindAllDescendants(cf => cf.ByControlType(ControlType.Button))
                .FirstOrDefault(button => _skills.Contains(UiTests.Walk.NameOf(button)) && button.Patterns.Toggle.IsSupported && button.IsEnabled),
            Ui.Step,
            "a skill's switch",
            App.Describe);
        var label = UiTests.Walk.NameOf(skill);
        var was = Ui.IsOn(App, label);
        Ui.SetToggle(App, label, !was);
        SaveAndWait("Save skills");
        OpenSection("Skills");
        Check.Expect(Ui.IsOn(App, label) == !was, $"the skill \"{label}\" did not keep its change after Save");
        Ui.SetToggle(App, label, was);
        SaveAndWait("Save skills");
        OpenSection("Skills");
        Check.Expect(Ui.IsOn(App, label) == was, $"the skill \"{label}\" was not put back");
        done.Add($"skill \"{label}\" toggled and restored");

        // Knowledge: a one-line document added by title and text, then deleted (asked first).
        OpenSection("Knowledge");
        var title = $"{Config.Prefix} document";
        Ui.Type(App, "Title", title);
        Ui.Type(App, "The document's text", "A one-line document the win-smoke walk adds and deletes.");
        Ui.Press(App, "Add document");
        _ = Ui.Text(App, title, TimeSpan.FromSeconds(60));
        check.Shot();
        Ui.Press(App, $"Delete this document: {title}");
        Ui.PressIn(App, Ui.Dialog(App, "Delete this document?"), "Delete");
        Check.Expect(Wait.Until(() => Ui.TryText(App, title) is null, TimeSpan.FromSeconds(60)), "the knowledge document is still listed after Delete");
        done.Add("knowledge document added and deleted");

        // Call routing rules: one added, saved (asked first), removed, saved.
        OpenSection("Call routing rules");
        var rules = RuleHeadings();
        Ui.Press(App, "Add a routing rule");
        var added = Wait.For(
            () => RuleHeadings().Except(rules).FirstOrDefault(),
            Ui.Step,
            "the new rule's card",
            App.Describe);
        var values = App.TryMainWindow()!.FindAllDescendants(cf => cf.ByControlType(ControlType.Edit).And(cf.ByName("Value")));
        Check.Expect(values.Length > 0, "the new rule has no Value box");
        values[^1].AsTextBox().Text = Config.Prefix;
        SaveRouting("Save the routing rules", "Replace the routing rules?", "Replace", "Remove every routing rule?", "Remove all rules");
        OpenSection("Call routing rules");
        Check.Expect(RuleHeadings().Contains(added), $"{added} is gone after Save");
        Ui.Press(App, $"Remove {added}");
        SaveRouting("Save the routing rules", "Replace the routing rules?", "Replace", "Remove every routing rule?", "Remove all rules");
        OpenSection("Call routing rules");
        Check.Expect(RuleHeadings().Length == rules.Length, $"the rules are {RuleHeadings().Length}, not the {rules.Length} there were");
        done.Add("routing rule added, saved, removed, saved");

        // Transfer directory: one added, saved (asked first), removed, saved.
        OpenSection("Transfer directory");
        var person = $"{Config.Prefix} transfer";
        Ui.Type(App, "Name of the person to add", person);
        Ui.Type(App, "Phone number of the person to add", "+14165550199");
        Ui.Press(App, "Add to the directory");
        _ = Ui.Find(App, ControlType.Edit, $"Name of {person}");
        SaveRouting("Save the transfer directory", "Replace the transfer directory?", "Replace", "Remove every transfer target?", "Remove everyone");
        OpenSection("Transfer directory");
        _ = Ui.Find(App, ControlType.Edit, $"Name of {person}");
        Ui.Press(App, $"Remove {person} from the directory");
        SaveRouting("Save the transfer directory", "Replace the transfer directory?", "Replace", "Remove every transfer target?", "Remove everyone");
        OpenSection("Transfer directory");
        Check.Expect(Ui.Stays(() => Shows(App, ControlType.Edit, $"Name of {person}"), TimeSpan.FromSeconds(3)), "the transfer target is still listed after removing it and saving");
        done.Add("transfer target added, saved, removed, saved");

        // Messaging accounts and Members: opened only (no keys, no members added).
        OpenSection("Messaging accounts");
        OpenSection("Members");
        done.Add("messaging accounts and members opened");
        return Outcome.Pass(string.Join("; ", done));
    }

    /// <summary>
    /// Unsaved changes: leaving Persona with an edit asks "Discard your
    /// changes?"; Keep editing stays with the edit; leaving again and
    /// Discard drops it.
    /// </summary>
    private Outcome StudioDiscard(Check check)
    {
        OpenSection("Persona");
        var name = Ui.Value(App, "Name");
        var edited = name + " discard";
        Ui.Type(App, "Name", edited);
        UiTests.Walk.Open(App, Ui.Entry(App, "Overview"));
        _ = Ui.Dialog(App, "Discard your changes?", "Keep editing");
        check.Shot();
        Ui.PressIn(App, Ui.Dialog(App, "Discard your changes?", "Keep editing"), "Keep editing");
        Check.Expect(Wait.Until(() => !Ui.HasDialog(App, "Discard your changes?", "Keep editing"), Ui.Step), "the question stayed after Keep editing");
        _ = UiTests.Walk.Heading(App, "Persona", Ui.Step);
        Check.Expect(Ui.Value(App, "Name") == edited, "Keep editing lost the edit");

        UiTests.Walk.Open(App, Ui.Entry(App, "Overview"));
        Ui.PressIn(App, Ui.Dialog(App, "Discard your changes?", "Keep editing"), "Discard");
        _ = UiTests.Walk.Heading(App, Config.WorkspaceName, Ui.Step);
        OpenSection("Persona");
        Check.Expect(Ui.Value(App, "Name") == name, "Discard did not drop the edit");
        return Outcome.Pass("leaving an edited Persona asks \"Discard your changes?\"; Keep editing keeps the edit, Discard drops it");
    }

    /// <summary>Opens a settings section from the hub, and waits for it to load.</summary>
    private void OpenSection(string section)
    {
        _ = Ui.Go(App, "Workspace settings", "Workspace settings");
        UiTests.Walk.OpenSettingsRow(App, section);
        _ = UiTests.Walk.Heading(App, section, Ui.Step);
        Ui.Loaded(App);
        if (UiTests.Walk.Failure(App) is { } failure)
        {
            throw new CheckFailedException($"{section} shows \"{failure}\"");
        }
    }

    /// <summary>Presses <paramref name="button"/> and waits for "Saved." with nothing saving.</summary>
    private void SaveAndWait(string button)
    {
        Ui.Press(App, button);
        WaitSaved(button);
    }

    /// <summary>Saves a list section that asks first (Replace, or Remove all when the list is empty), and waits for "Saved.".</summary>
    private void SaveRouting(string button, string replaceTitle, string replace, string emptyTitle, string empty)
    {
        Ui.Press(App, button);
        var asked = Wait.For(
            () => Ui.HasDialog(App, replaceTitle) ? replaceTitle : Ui.HasDialog(App, emptyTitle) ? emptyTitle : null,
            Ui.Step,
            $"the question before {button}",
            App.Describe);
        Ui.PressIn(App, Ui.Dialog(App, asked), asked == replaceTitle ? replace : empty);
        WaitSaved(button);
    }

    /// <summary>
    /// Waits for the save <paramref name="button"/> started to end: nothing
    /// saving, "Saved." on screen, and the button off again (nothing left to
    /// save), or 3 s gone by for a button that stays on. A "Saved." left from
    /// the save before is on screen throughout, so the button is what says
    /// this save is done.
    /// </summary>
    private void WaitSaved(string button)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        _ = Wait.For(
            () => App.TryFind(ControlType.ProgressBar, "Saving") is null
                && Ui.TryText(App, Saved) is not null
                && (App.TryFind(ControlType.Button, button) is not { IsEnabled: true } || clock.Elapsed > TimeSpan.FromSeconds(3))
                ? (object)true : null,
            TimeSpan.FromSeconds(45),
            $"\"{Saved}\" after {button}",
            App.Describe);
        foreach (var problem in new[] { "Settings not saved", "Problem" })
        {
            Check.Expect(App.TryFind(null, problem) is null, $"\"{problem}\" shows after {button}");
        }
    }

    /// <summary>The routing cards' headings: "Rule 1", "Rule 2", ...</summary>
    private string[] RuleHeadings() =>
        [.. Subheadings().Where(heading => heading.StartsWith("Rule ", StringComparison.Ordinal))];
}
