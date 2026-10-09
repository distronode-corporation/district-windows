using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using Xunit;

namespace DistrictAI.UiTests;

/// <summary>
/// What a walk of the app does, shared by the scripted walk (SceneWalkTests)
/// and the live walk (LiveWalkTests): open a pane entry or a settings row,
/// wait for a page's heading, read what a failed page shows, and save what
/// the window draws.
/// </summary>
internal static class Walk
{
    /// <summary>How long a page has to open and finish loading.</summary>
    public static readonly TimeSpan PageTimeout = TimeSpan.FromSeconds(30);

    /// <summary>What a page that failed shows (StatusPanel, UnavailablePage, the core's failure titles).</summary>
    public static readonly string[] FailureTexts = ["Something went wrong", "Could not load", "Not in this version yet"];

    /// <summary>
    /// Opens a page with <paramref name="open"/> and checks it: its heading
    /// (<paramref name="expected"/>, or any), loaded, no failure, then saves
    /// it when screenshots are on. With <paramref name="editName"/>, the
    /// page is ready only once a text box of that name shows. Problems go in
    /// <paramref name="problems"/>; false when the app has ended.
    /// </summary>
    public static bool Visit(InstalledApp app, nint handle, string name, Action open, string? expected, string? shots, int index, List<string> problems, string? editName = null)
    {
        var clock = Stopwatch.StartNew();
        try
        {
            open();
            InstalledApp.Log($"opened \"{name}\"");
            var heading = Heading(app, expected, PageTimeout);
            // Loaded: the status panel's progress ring gone.
            _ = Wait.For(
                () => app.TryFind(ControlType.ProgressBar, "Loading") is null ? heading : null,
                PageTimeout,
                $"\"{name}\" to finish loading",
                app.Describe);
            if (editName is not null)
            {
                _ = app.Find(ControlType.Edit, editName, PageTimeout);
            }
            if (Failure(app) is { } failure)
            {
                problems.Add($"\"{name}\" shows \"{failure}\".{Environment.NewLine}{app.Describe()}");
            }
            else
            {
                InstalledApp.Log(FormattableString.Invariant($"\"{name}\": \"{heading.Name}\" in {clock.ElapsedMilliseconds} ms"));
                if (shots is { Length: > 0 })
                {
                    Save(handle, Path.Combine(shots, FormattableString.Invariant($"{index:D2}-{FileName(name)}.png")), clientOnly: true);
                }
            }
        }
        catch (TimeoutException error)
        {
            problems.Add($"\"{name}\": {error.Message}");
        }
        if (app.IsRunning)
        {
            return true;
        }
        problems.Add($"the app ended after \"{name}\" was opened");
        return false;
    }

    /// <summary>The names of the pane's entries, in order (headings and separators are not entries).</summary>
    public static string[] NavEntries(AutomationElement window) =>
        [.. window.FindAllDescendants(cf => cf.ByControlType(ControlType.ListItem))
            .Where(item => item.ClassName.EndsWith(".NavigationViewItem", StringComparison.Ordinal) || item.ClassName == "NavigationViewItem")
            .Select(item => item.Name)
            .Where(name => name.Length > 0)
            .Distinct()];

    /// <summary>
    /// Opens the hub's row for <paramref name="title"/>, as a click on it does.
    /// A row's name is its title, then what the section holds.
    /// </summary>
    public static void OpenSettingsRow(InstalledApp app, string title)
    {
        var row = Wait.For(
            () => app.TryMainWindow()
                ?.FindAllDescendants(cf => cf.ByControlType(ControlType.ListItem))
                .FirstOrDefault(item => !item.Properties.IsOffscreen.ValueOrDefault
                    && NameOf(item).StartsWith(title + ". ", StringComparison.Ordinal)),
            PageTimeout,
            $"the settings row \"{title}\"",
            app.Describe);
        Activate(row);
    }

    /// <summary>Opens the Inbox's first conversation, as a click on it does.</summary>
    public static void OpenFirstConversation(InstalledApp app)
    {
        var list = app.Find(ControlType.List, "Conversations", PageTimeout);
        var first = Wait.For(
            () => list.FindFirstDescendant(cf => cf.ByControlType(ControlType.ListItem)),
            PageTimeout,
            "a conversation in the Inbox",
            app.Describe);
        Activate(first);
    }

    /// <summary>Chooses the entry named <paramref name="entry"/>, as a click does.</summary>
    public static void Open(InstalledApp app, string entry) =>
        Activate(app.Find(ControlType.ListItem, entry, PageTimeout));

    /// <summary>
    /// Chooses the entry named <paramref name="entry"/> until its page shows
    /// <paramref name="heading"/>. A choice made in the first moments after
    /// launch, while the scene is still filling the navigation pane, can be
    /// lost (in run 37892440647 "Calls" was chosen 20 ms after the overview's
    /// heading showed, and the overview stayed), so it is made again.
    /// </summary>
    public static void OpenUntil(InstalledApp app, string entry, string heading)
    {
        for (var attempt = 1; ; attempt++)
        {
            Open(app, entry);
            try
            {
                _ = Heading(app, heading, TimeSpan.FromSeconds(10));
                return;
            }
            catch (TimeoutException) when (attempt < 3)
            {
                InstalledApp.Log(FormattableString.Invariant($"\"{entry}\" did not open (attempt {attempt}): choosing it again"));
            }
        }
    }

    /// <summary>
    /// What a walk never presses unless that one press says why it may: what
    /// sends, buys, confirms, places a call, runs a billed job or ends more
    /// than this session. Compared with the whole name, ignoring case.
    /// </summary>
    public static readonly string[] Denied =
    [
        "Send",
        "Confirm",
        "Enable",
        "Buy",
        "Pay",
        "Continue to checkout",
        "Place a call",
        "Call",
        "Run research",
        "Sign out of every device",
        "Delete account",
        "Delete workspace",
    ];

    /// <summary>
    /// Presses <paramref name="element"/> (a button, a menu item, a row), as
    /// a click does, unless its name is <see cref="Denied"/> and no
    /// <paramref name="allow"/> reason is given: then nothing is pressed and
    /// <see cref="RefusedPressException"/> is thrown.
    /// </summary>
    public static void Press(AutomationElement element, string? allow = null)
    {
        var name = NameOf(element).Trim();
        if (Denied.Any(denied => string.Equals(denied, name, StringComparison.OrdinalIgnoreCase)))
        {
            if (allow is not { Length: > 0 })
            {
                throw new RefusedPressException(name);
            }
            InstalledApp.Log($"pressing \"{name}\", allowed: {allow}");
        }
        Invoke(element);
    }

    /// <summary>
    /// Invokes a list row or an item as a click on it does: its Invoke
    /// pattern when it has one, else a click. Denied names are refused here
    /// too; <see cref="Press"/> is the way to allow one.
    /// </summary>
    public static void Activate(AutomationElement element)
    {
        var name = NameOf(element).Trim();
        if (Denied.Any(denied => string.Equals(denied, name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new RefusedPressException(name);
        }
        Invoke(element);
    }

    private static void Invoke(AutomationElement element)
    {
        if (element.Patterns.Invoke.IsSupported)
        {
            element.Patterns.Invoke.Pattern.Invoke();
        }
        else
        {
            element.Click();
        }
    }

    /// <summary>
    /// The page's level-one heading: <paramref name="expected"/>, or any
    /// heading when no text is expected, but never the unavailable page's.
    /// </summary>
    public static AutomationElement Heading(InstalledApp app, string? expected, TimeSpan timeout) =>
        Wait.For(
            () => TryHeading(app, expected),
            timeout,
            expected is null ? "a level-one heading" : $"the heading \"{expected}\"",
            app.Describe);

    /// <summary>What <see cref="Heading"/> finds, now, or null.</summary>
    public static AutomationElement? TryHeading(InstalledApp app, string? expected) =>
        app.TryMainWindow()
            ?.FindAllDescendants(cf => cf.ByControlType(ControlType.Text))
            .FirstOrDefault(text =>
                !text.Properties.IsOffscreen.ValueOrDefault
                && text.Properties.HeadingLevel.ValueOrDefault == HeadingLevel.Level1
                && NameOf(text) is { Length: > 0 } name
                && (expected is null || name == expected));

    /// <summary>
    /// The element's name, or empty when UI Automation has none for it: a text
    /// element can be gone, or never named, between being listed and being
    /// read (seen as PropertyNotSupportedException on the Inbox).
    /// </summary>
    public static string NameOf(AutomationElement element) => element.Properties.Name.ValueOrDefault ?? string.Empty;

    /// <summary>What shows that the page failed, or null.</summary>
    public static string? Failure(InstalledApp app)
    {
        var window = app.TryMainWindow();
        if (window is null)
        {
            return "no window";
        }
        if (window.FindFirstDescendant(cf => cf.ByControlType(ControlType.Button).And(cf.ByName("Try again"))) is { IsOffscreen: false })
        {
            return "Try again";
        }
        return window.FindAllDescendants(cf => cf.ByControlType(ControlType.Text))
            .Where(text => !text.Properties.IsOffscreen.ValueOrDefault)
            .Select(NameOf)
            .FirstOrDefault(name => FailureTexts.Any(failure => name.StartsWith(failure, StringComparison.Ordinal)));
    }

    /// <summary>
    /// Makes the window's client area <paramref name="width"/> by
    /// <paramref name="height"/>, and waits until it is. A window with its
    /// title bar and frame would not fit a display of that size (the hosted
    /// runner's largest), so the frame is taken off first.
    /// </summary>
    public static void SizeClient(nint handle, int width, int height)
    {
        Native.Frameless(handle, width, height);
        var sized = Wait.Until(
            () => Native.GetClientRect(handle, out var now) && now.Width == width && now.Height == height,
            TimeSpan.FromSeconds(10));
        _ = Native.GetClientRect(handle, out var actual);
        InstalledApp.Log(FormattableString.Invariant($"client area {actual.Width}x{actual.Height} (asked for {width}x{height})"));
        Assert.True(sized, FormattableString.Invariant($"the client area is {actual.Width}x{actual.Height}, not {width}x{height}: is the display smaller?"));
    }

    /// <summary>
    /// Saves what the window draws as a PNG: its client area, or the visible
    /// window (title bar and frame, without the invisible resize borders).
    /// </summary>
    public static void Save(nint handle, string path, bool clientOnly)
    {
        _ = Native.SetForegroundWindow(handle);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _ = Native.GetWindowRect(handle, out var outer);
        Native.Rect area;
        if (clientOnly)
        {
            _ = Native.GetClientRect(handle, out var client);
            var origin = default(Native.Point);
            _ = Native.ClientToScreen(handle, ref origin);
            area = new Native.Rect { Left = origin.X, Top = origin.Y, Right = origin.X + client.Width, Bottom = origin.Y + client.Height };
        }
        else
        {
            area = Native.VisibleBounds(handle);
        }
        using var whole = new Bitmap(outer.Width, outer.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(whole))
        {
            var dc = graphics.GetHdc();
            try
            {
                Assert.True(Native.PrintWindow(handle, dc, 2), "PrintWindow failed");
            }
            finally
            {
                graphics.ReleaseHdc(dc);
            }
        }
        var crop = new Rectangle(area.Left - outer.Left, area.Top - outer.Top, area.Width, area.Height);
        using var image = whole.Clone(crop, PixelFormat.Format32bppArgb);
        image.Save(path, ImageFormat.Png);
        InstalledApp.Log(FormattableString.Invariant($"saved {path} ({image.Width}x{image.Height})"));
    }

    /// <summary>A file name made of <paramref name="entry"/>: letters and digits, the rest dashes.</summary>
    public static string FileName(string entry) =>
        new([.. entry.Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-')]);
}

/// <summary>A press the walk refused: the element's name is on <see cref="Walk.Denied"/> and nothing allowed it.</summary>
internal sealed class RefusedPressException(string name)
    : InvalidOperationException($"refused to press \"{name}\": it is on the walk's denylist and this press gave no reason")
{
    /// <summary>The refused element's name.</summary>
    public string Name { get; } = name;
}
