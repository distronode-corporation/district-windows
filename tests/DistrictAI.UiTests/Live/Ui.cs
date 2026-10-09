using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

namespace DistrictAI.UiTests.Live;

/// <summary>
/// The live walk's moves on the app's main window, each waited for and each
/// press through the denylist (<see cref="UiTests.Walk.Press"/>).
/// </summary>
internal static class Ui
{
    /// <summary>How long a control has to show.</summary>
    public static readonly TimeSpan Step = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Opens the pane entry <paramref name="entry"/> (by the start of its name:
    /// the Inbox's carries its unread count) until its page shows
    /// <paramref name="heading"/>, then waits for it to finish loading. A
    /// failure on the page fails the check.
    /// </summary>
    public static AutomationElement Go(InstalledApp app, string entry, string heading)
    {
        var name = Entry(app, entry);
        UiTests.Walk.OpenUntil(app, name, heading);
        var found = UiTests.Walk.Heading(app, heading, Step);
        Loaded(app);
        if (UiTests.Walk.Failure(app) is { } failure)
        {
            throw new CheckFailedException($"\"{heading}\" shows \"{failure}\"");
        }
        return found;
    }

    /// <summary>The full name of the pane entry whose name starts with <paramref name="entry"/>.</summary>
    public static string Entry(InstalledApp app, string entry) =>
        Wait.For(
            () => app.TryMainWindow() is { } window
                ? UiTests.Walk.NavEntries(window).FirstOrDefault(name => name == entry || name.StartsWith(entry + ",", StringComparison.Ordinal))
                : null,
            Step,
            $"the pane entry \"{entry}\"",
            app.Describe);

    /// <summary>Whether the pane offers an entry whose name starts with <paramref name="entry"/>.</summary>
    public static bool Offers(InstalledApp app, string entry) =>
        app.TryMainWindow() is { } window
        && UiTests.Walk.NavEntries(window).Any(name => name == entry || name.StartsWith(entry + ",", StringComparison.Ordinal));

    /// <summary>Waits until no "Loading" ring shows.</summary>
    public static void Loaded(InstalledApp app) =>
        _ = Wait.For(
            () => app.TryFind(ControlType.ProgressBar, "Loading") is null ? (object)true : null,
            Step,
            "the page to finish loading",
            app.Describe);

    /// <summary>The control named <paramref name="name"/> of <paramref name="type"/> (any, when null), waited for.</summary>
    public static AutomationElement Find(InstalledApp app, ControlType? type, string name, TimeSpan? timeout = null) =>
        app.Find(type, name, timeout ?? Step);

    /// <summary>
    /// Presses the button named <paramref name="name"/> once it is enabled,
    /// through the denylist; <paramref name="allow"/> is the reason a denied
    /// name may be pressed here.
    /// </summary>
    public static void Press(InstalledApp app, string name, string? allow = null, ControlType? type = null) =>
        PressIn(app, null, name, allow, type);

    /// <summary>As <see cref="Press"/>, inside <paramref name="scope"/> (a dialog) when given.</summary>
    public static void PressIn(InstalledApp app, AutomationElement? scope, string name, string? allow = null, ControlType? type = null)
    {
        var wanted = type ?? ControlType.Button;
        var button = Wait.For(
            () => (scope is null
                ? app.TryFind(wanted, name)
                : scope.FindAllDescendants(cf => cf.ByControlType(wanted).And(cf.ByName(name))).LastOrDefault(found => !found.IsOffscreen)) is { IsEnabled: true } found ? found : null,
            Step,
            $"\"{name}\" to be there and enabled",
            app.Describe);
        UiTests.Walk.Press(button, allow);
        InstalledApp.Log($"pressed \"{name}\"");
    }

    /// <summary>Types <paramref name="text"/> into the text box named <paramref name="name"/>, replacing what it held.</summary>
    public static void Type(InstalledApp app, string name, string text, AutomationElement? scope = null)
    {
        var box = scope is null ? app.Find(ControlType.Edit, name, Step) : app.FindIn(scope, ControlType.Edit, name, Step);
        box.AsTextBox().Text = text;
    }

    /// <summary>What the text box named <paramref name="name"/> holds.</summary>
    public static string Value(InstalledApp app, string name) =>
        app.Find(ControlType.Edit, name, Step).AsTextBox().Text ?? string.Empty;

    /// <summary>
    /// The open dialog titled <paramref name="title"/>, holding a button named
    /// <paramref name="closeButton"/>: an element of that name (a dialog's
    /// title can be its primary button's name too, "Block", so presses on a
    /// dialog look inside it), or a dialog with no name of its own that
    /// shows the title as text ("Discard your changes?").
    /// </summary>
    public static AutomationElement Dialog(InstalledApp app, string title, string closeButton = "Cancel", TimeSpan? timeout = null) =>
        Wait.For(
            () => TryDialog(app, title, closeButton),
            timeout ?? Step,
            $"the dialog \"{title}\"",
            app.Describe);

    /// <summary>Whether a dialog titled <paramref name="title"/> is open now.</summary>
    public static bool HasDialog(InstalledApp app, string title, string closeButton = "Cancel") =>
        TryDialog(app, title, closeButton) is not null;

    private static AutomationElement? TryDialog(InstalledApp app, string title, string closeButton)
    {
        if (app.TryMainWindow() is not { } window)
        {
            return null;
        }
        bool Closes(AutomationElement element) =>
            element.FindFirstDescendant(cf => cf.ByControlType(ControlType.Button).And(cf.ByName(closeButton))) is not null;
        return window.FindAllDescendants(cf => cf.ByName(title))
                .FirstOrDefault(element => element.ControlType != ControlType.Button && element.ControlType != ControlType.Text && Closes(element))
            ?? window.FindAllDescendants(cf => cf.ByControlType(ControlType.Window))
                .FirstOrDefault(element => element.FindFirstDescendant(cf => cf.ByControlType(ControlType.Text).And(cf.ByName(title))) is not null && Closes(element))
            // A dialog UI Automation does not show as one element: its title
            // and its buttons are on screen, so the window is the scope, and a
            // press in it takes the last match (a dialog draws over the page).
            ?? (window.FindFirstDescendant(cf => cf.ByControlType(ControlType.Text).And(cf.ByName(title))) is { IsOffscreen: false } && Closes(window) ? window : null);
    }

    /// <summary>The toggle switch named <paramref name="name"/> (not its header's text), waited for.</summary>
    public static AutomationElement Toggle(InstalledApp app, string name) =>
        Wait.For(() => TryToggle(app, name), Step, $"the switch \"{name}\"", app.Describe);

    private static AutomationElement? TryToggle(InstalledApp app, string name) =>
        app.TryMainWindow()?.FindAllDescendants(cf => cf.ByName(name))
            .FirstOrDefault(element => !element.IsOffscreen && element.Patterns.Toggle.IsSupported);

    /// <summary>The toggle switch named <paramref name="name"/>: whether it is on.</summary>
    public static bool IsOn(InstalledApp app, string name) =>
        Toggle(app, name).Patterns.Toggle.Pattern.ToggleState.Value == ToggleState.On;

    /// <summary>Sets the toggle switch named <paramref name="name"/> to <paramref name="on"/>, and waits until it reads so.</summary>
    public static void SetToggle(InstalledApp app, string name, bool on)
    {
        var toggle = Toggle(app, name);
        if ((toggle.Patterns.Toggle.Pattern.ToggleState.Value == ToggleState.On) != on)
        {
            toggle.Patterns.Toggle.Pattern.Toggle();
        }
        _ = Wait.For(
            () => TryToggle(app, name) is { } now && (now.Patterns.Toggle.Pattern.ToggleState.Value == ToggleState.On) == on ? (object)true : null,
            Step,
            $"\"{name}\" to be {(on ? "on" : "off")}",
            app.Describe);
        InstalledApp.Log($"\"{name}\" is {(on ? "on" : "off")}");
    }

    /// <summary>A text on screen whose name holds <paramref name="part"/>, waited for.</summary>
    public static AutomationElement Text(InstalledApp app, string part, TimeSpan? timeout = null) =>
        Wait.For(
            () => TryText(app, part),
            timeout ?? Step,
            $"a text holding \"{part}\"",
            app.Describe);

    /// <summary>A text (or any named element) on screen whose name holds <paramref name="part"/>, now, or null.</summary>
    public static AutomationElement? TryText(InstalledApp app, string part) =>
        app.TryMainWindow()?.FindAllDescendants(cf => cf.ByControlType(ControlType.Text))
            .FirstOrDefault(text => !text.Properties.IsOffscreen.ValueOrDefault
                && UiTests.Walk.NameOf(text).Contains(part, StringComparison.Ordinal));

    /// <summary>A list row whose name starts with <paramref name="start"/>, under <paramref name="scope"/> (the main window when null), now, or null.</summary>
    public static AutomationElement? TryRow(InstalledApp app, string start, AutomationElement? scope = null) =>
        (scope ?? app.TryMainWindow())?.FindAllDescendants(cf => cf.ByControlType(ControlType.ListItem))
            .FirstOrDefault(item => !item.Properties.IsOffscreen.ValueOrDefault
                && UiTests.Walk.NameOf(item).StartsWith(start, StringComparison.Ordinal));

    /// <summary>A list row whose name starts with <paramref name="start"/>, waited for.</summary>
    public static AutomationElement Row(InstalledApp app, string start, TimeSpan? timeout = null) =>
        Wait.For(() => TryRow(app, start), timeout ?? Step, $"a row \"{start}...\"", app.Describe);

    /// <summary>Whether <paramref name="probe"/> stays false for <paramref name="settle"/>: something that must not show, given time to.</summary>
    public static bool Stays(Func<bool> probe, TimeSpan settle) => !Wait.Until(() => !probe(), settle);

    /// <summary>The names of the enabled buttons on screen in the main window.</summary>
    public static string[] Buttons(InstalledApp app) =>
        app.TryMainWindow() is { } window
            ? [.. window.FindAllDescendants(cf => cf.ByControlType(ControlType.Button))
                .Where(button => !button.Properties.IsOffscreen.ValueOrDefault && button.Properties.IsEnabled.ValueOrDefault)
                .Select(UiTests.Walk.NameOf)
                .Where(name => name.Length > 0)]
            : [];
}
