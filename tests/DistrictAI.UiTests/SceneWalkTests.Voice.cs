using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using Xunit;
using static DistrictAI.UiTests.Walk;

namespace DistrictAI.UiTests;

/// <summary>
/// District HQ by voice, in the scripted scenes (scripted.rs, voice_room): no
/// media engine and no audio, but the session's states as the core shows
/// them. With DISTRICTAI_SCREENSHOTS set, each state is saved at the walk's
/// 1920x1080 and at <see cref="NarrowWidth"/> by <see cref="ShortHeight"/>,
/// in English and in Canadian French, as
/// <c>NN-hq-voice-STATE-LANGUAGE-WIDTHxHEIGHT.png</c>: the residency
/// statement (92), a listening session with its transcript and two cards on
/// District HQ (93), the voice dock with the two cards on Calls (94), the dock
/// muted (95), the session ended with its cards on District HQ (96), and the
/// dock with its cards under the strip of a call (97). In the narrow window
/// every card's Confirm and Cancel must be reachable, scrolled to if need be.
/// </summary>
public sealed partial class SceneWalkTests
{
    /// <summary>The scene whose voice agent leaves once it has proposed (scripted.rs, Scene::VoiceEnds), in a Canadian workspace.</summary>
    public const string VoiceEndsArgument = "--district-scripted-scene=signed-in-voice-ends";

    /// <summary>The scene's language, in place of Windows' (scripted.rs, LANGUAGE_ARG).</summary>
    public const string FrenchArgument = "--district-scripted-language=fr-CA";

    /// <summary>
    /// The narrow window's width. The window declares no minimum size; 500
    /// is about the narrowest a snapped window gets on a 1080p display, and
    /// below 641 the navigation pane folds away, as it does for a member.
    /// </summary>
    public const int NarrowWidth = 500;

    /// <summary>The short window's height: where Linux's dock ran off the bottom with two cards.</summary>
    public const int ShortHeight = 700;

    /// <summary>
    /// Start voice, the residency statement accepted, the session listening:
    /// its transcript and two cards on District HQ, the same cards in the dock
    /// on Calls, and the microphone muted from the dock. Stop voice ends it.
    /// </summary>
    [Theory]
    [InlineData("en")]
    [InlineData("fr")]
    public void AVoiceSessionListensProposesAndMutes(string language)
    {
        RequireScriptedPackage();
        var shots = Environment.GetEnvironmentVariable("DISTRICTAI_SCREENSHOTS");
        using var app = InstalledApp.Launch(Arguments(SceneArgument, language));
        var window = app.MainWindow(_startTimeout);
        var handle = window.Properties.NativeWindowHandle.Value;
        _ = Heading(app, WorkspaceName, _startTimeout);
        var problems = new List<string>();
        if (shots is { Length: > 0 })
        {
            SizeClient(handle, 1920, 1080);
        }

        OpenUntilById(app, "NavHq");
        Press(app.FindById("VoiceStart", PageTimeout), allow: "the scene answers the session; nothing leaves the computer");
        Press(app.FindById("VoiceAccept", PageTimeout), allow: "the scene answers the session; nothing leaves the computer");
        _ = app.FindById("VoiceLine", PageTimeout);
        Expect(app, () => AllById(app, "VoiceCardConfirm").Length == 2, "the two cards on District HQ");
        InstalledApp.Log("the session listens, with its transcript and two cards");
        Shoot(app, handle, shots, 93, "listening", language, problems);

        OpenUntilById(app, "NavCalls");
        _ = app.FindById("VoiceDock", PageTimeout);
        Expect(app, () => AllById(app, "VoiceCardConfirm").Length == 2, "the two cards in the dock");
        InstalledApp.Log("the dock shows the two cards on Calls");
        Shoot(app, handle, shots, 94, "dock", language, problems);

        var microphone = app.FindById("VoiceDockMicrophone", PageTimeout);
        Assert.Equal(ToggleState.Off, microphone.Patterns.Toggle.Pattern.ToggleState.Value);
        microphone.Patterns.Toggle.Pattern.Toggle();
        Expect(
            app,
            () => app.TryFindById("VoiceDockMicrophone") is { } found && found.Patterns.Toggle.Pattern.ToggleState.Value == ToggleState.On,
            "the dock's microphone muted");
        InstalledApp.Log("the dock's microphone is muted");
        Shoot(app, handle, shots, 95, "muted", language, problems);

        Press(app.FindById("VoiceDockStop", PageTimeout));
        Expect(app, () => app.TryFindById("VoiceDockStop") is null, "Stop voice to go once the session stopped");
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
        Assert.True(app.IsRunning, "the app ended in the voice session");
    }

    /// <summary>
    /// In a Canadian workspace the residency statement has its region line;
    /// the agent leaves once it has proposed, so the session ends with its
    /// cards waiting; and a call placed then shows its strip, live transcript
    /// included, over the dock and its two cards.
    /// </summary>
    [Theory]
    [InlineData("en")]
    [InlineData("fr")]
    public void AnEndedSessionsCardsWaitBesideACall(string language)
    {
        RequireScriptedPackage();
        var shots = Environment.GetEnvironmentVariable("DISTRICTAI_SCREENSHOTS");
        using var app = InstalledApp.Launch(Arguments(VoiceEndsArgument, language));
        var window = app.MainWindow(_startTimeout);
        var handle = window.Properties.NativeWindowHandle.Value;
        _ = Heading(app, WorkspaceName, _startTimeout);
        var problems = new List<string>();
        if (shots is { Length: > 0 })
        {
            SizeClient(handle, 1920, 1080);
        }

        OpenUntilById(app, "NavHq");
        Press(app.FindById("VoiceStart", PageTimeout), allow: "the scene answers the session; nothing leaves the computer");
        _ = app.FindById("VoiceDisclosure", PageTimeout);
        _ = app.FindById("VoiceDecline", PageTimeout);
        InstalledApp.Log("the residency statement shows, with its region line");
        Shoot(app, handle, shots, 92, "statement", language, problems, "VoiceAccept", "VoiceDecline");

        Press(app.FindById("VoiceAccept", PageTimeout, onScreen: false), allow: "the scene answers the session; nothing leaves the computer");
        _ = app.FindById("VoiceDismiss", PageTimeout);
        Expect(app, () => AllById(app, "VoiceCardConfirm").Length == 2, "the two cards left on District HQ");
        InstalledApp.Log("the session ended, its two cards waiting");
        Shoot(app, handle, shots, 96, "ended", language, problems);

        OpenUntilById(app, "NavCalls");
        _ = app.FindById("VoiceDockDismiss", PageTimeout);
        app.FindById("PlaceCallButton", PageTimeout).AsButton().Invoke();
        app.FindById("DialNumberBox", PageTimeout).AsTextBox().Text = "+12125550142";
        Wait.For(
            () => app.TryFindById("DialCallButton") is { IsEnabled: true } call ? call : null,
            PageTimeout,
            "Call to work",
            app.Describe).AsButton().Invoke();
        _ = app.FindById("TranscriptHeading", PageTimeout, onScreen: false);
        Expect(app, () => AllById(app, "VoiceCardConfirm").Length == 2, "the two cards in the dock, under the call");
        InstalledApp.Log("the call's strip shows over the dock and its two cards");
        Shoot(app, handle, shots, 97, "dock-call", language, problems);

        app.FindById("HangUpButton", PageTimeout, onScreen: false).AsButton().Invoke();
        _ = app.FindById("DismissCallButton", PageTimeout, onScreen: false);
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
        Assert.True(app.IsRunning, "the app ended beside the call");
    }

    /// <summary>The command line of <paramref name="scene"/> in <paramref name="language"/> ("en" or "fr").</summary>
    private static string Arguments(string scene, string language) =>
        language == "fr" ? $"{scene} {FrenchArgument}" : scene;

    /// <summary>
    /// Saves the window as it is at 1920x1080, then at the narrow size, as
    /// <c>NN-hq-voice-STATE-LANGUAGE-WIDTHxHEIGHT.png</c> when screenshots
    /// are on, and leaves it at 1920x1080 again, where the navigation pane
    /// shows. In the narrow window, every card's Confirm and Cancel (or the
    /// <paramref name="buttons"/> named) must be reachable: on screen, inside
    /// the window, once their scroll viewers are scrolled. Without
    /// screenshots the window is left as it is and only that is checked.
    /// </summary>
    private static void Shoot(InstalledApp app, nint handle, string? shots, int number, string state, string language, List<string> problems, params string[] buttons)
    {
        string[] ids = buttons.Length > 0 ? buttons : ["VoiceCardConfirm", "VoiceCardCancel"];
        if (shots is not { Length: > 0 })
        {
            CheckReachable(app, handle, ids, $"{state} ({language})", problems);
            return;
        }
        foreach (var (width, height) in new[] { (1920, 1080), (NarrowWidth, ShortHeight) })
        {
            SizeClient(handle, width, height);
            // A frame for the layout to follow the new size.
            Thread.Sleep(500);
            Save(handle, Path.Combine(shots, FormattableString.Invariant($"{number}-hq-voice-{state}-{language}-{width}x{height}.png")), clientOnly: true);
            if (width == NarrowWidth)
            {
                CheckReachable(app, handle, ids, FormattableString.Invariant($"{state} ({language}, {width}x{height})"), problems);
            }
        }
        SizeClient(handle, 1920, 1080);
        Thread.Sleep(500);
    }

    /// <summary>
    /// Whether each element with one of <paramref name="ids"/> can be brought
    /// inside the window's client area: the scroll viewers it may sit in (the
    /// call bar's band, the dock's cards, District HQ's voice section) are
    /// scrolled from top to bottom until it shows. Each one that never does
    /// is a problem.
    /// </summary>
    private static void CheckReachable(InstalledApp app, nint handle, string[] ids, string where, List<string> problems)
    {
        _ = Native.GetClientRect(handle, out var client);
        var origin = default(Native.Point);
        _ = Native.ClientToScreen(handle, ref origin);
        bool Inside(AutomationElement element)
        {
            if (element.Properties.IsOffscreen.ValueOrDefault)
            {
                return false;
            }
            var box = element.BoundingRectangle;
            return box.Width > 0
                && box.Left >= origin.X && box.Top >= origin.Y
                && box.Right <= origin.X + client.Width && box.Bottom <= origin.Y + client.Height;
        }
        string[] scrollers = ["CallBarScroll", "VoiceDockCards", "VoiceScroll"];
        string[] inners = ["VoiceDockCards", "VoiceScroll"];
        foreach (var id in ids)
        {
            var count = AllById(app, id).Length;
            if (count == 0)
            {
                problems.Add($"{where}: no {id} at all");
            }
            for (var index = 0; index < count; index++)
            {
                var reached = false;
                foreach (var outer in new[] { 0d, 50d, 100d })
                {
                    Scroll(app, "CallBarScroll", outer);
                    for (var inner = 0; inner <= 100 && !reached; inner += 20)
                    {
                        foreach (var scroller in inners)
                        {
                            Scroll(app, scroller, inner);
                        }
                        Thread.Sleep(150);
                        reached = AllById(app, id) is { } found && found.Length > index && Inside(found[index]);
                    }
                    if (reached)
                    {
                        break;
                    }
                }
                if (!reached)
                {
                    problems.Add(FormattableString.Invariant($"{where}: {id} {index + 1} of {count} cannot be brought into the window"));
                }
            }
        }
        InstalledApp.Log($"{where}: checked {string.Join(", ", ids)} can be reached");
        foreach (var scroller in scrollers)
        {
            Scroll(app, scroller, 0);
        }
    }

    /// <summary>
    /// Scrolls the scroll viewer <paramref name="automationId"/> to
    /// <paramref name="percent"/> of the way down, when it is there and has
    /// anything to scroll, on screen or not; else does nothing.
    /// </summary>
    private static void Scroll(InstalledApp app, string automationId, double percent)
    {
        // UI Automation's "no scroll" for the axis left alone.
        const double NoScroll = -1;
        if (app.TryFindById(automationId, onScreen: false)?.Patterns.Scroll.PatternOrDefault is { } scroll
            && scroll.VerticallyScrollable.ValueOrDefault)
        {
            scroll.SetScrollPercent(NoScroll, percent);
        }
    }
}
