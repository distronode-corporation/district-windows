// SPDX-FileCopyrightText: Distronode Corporation
//
// SPDX-License-Identifier: Apache-2.0

using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.Patterns;
using Xunit;
using static DistrictAI.UiTests.Walk;

namespace DistrictAI.UiTests;

/// <summary>
/// Two-step verification and Delete account, below the account page, in the
/// scripted scene (scripted.rs: off at first, any password stands, the
/// core's example key and placeholder codes, one code refused, a deletion
/// never answered), in English and in Canadian French. Two-step verification
/// is turned on through the password: confirming it is you, the QR code and
/// the key, a refused code, the first code, the recovery codes, then on. The
/// deletion screen reaches its last step, and Delete my account is never
/// pressed.
/// </summary>
/// <remarks>
/// At each step, in a window <see cref="SecurityNarrowWidth"/> wide, every
/// button must fit across the window: its label wraps rather than hold the
/// page wider than the window (French is long). With DISTRICTAI_SCREENSHOTS
/// set, each step is saved at 1920x1080 and at that width as
/// <c>97-account-security-STEP-LANGUAGE-WIDTHxHEIGHT.png</c>.
/// </remarks>
public sealed partial class SceneWalkTests
{
    /// <summary>The narrow window the account's security pages are checked in.</summary>
    public const int SecurityNarrowWidth = 400;

    private const string ScenePassword = "example password";

    /// <summary>The one authenticator code the scene refuses (scripted.rs, <c>REFUSED_TWO_STEP_CODE</c>).</summary>
    private const string RefusedCode = "000000";

    [Theory]
    [InlineData("en")]
    [InlineData("fr")]
    public void TwoStepVerificationTurnsOnAndTheDeletionReachesItsLastStep(string language)
    {
        RequireScriptedPackage();
        var shots = Environment.GetEnvironmentVariable("DISTRICTAI_SCREENSHOTS");
        using var app = InstalledApp.Launch(Arguments(SceneArgument, language));
        var window = app.MainWindow(_startTimeout);
        var handle = window.Properties.NativeWindowHandle.Value;
        _ = Heading(app, WorkspaceName, _startTimeout);
        var problems = new List<string>();

        _ = OpenById(app, "NavAccount");
        Press(app.FindById("TwoStepButton", PageTimeout, onScreen: false));
        _ = app.FindById("TwoStepHeading", PageTimeout);
        var turnOn = app.FindById("TwoStepTurnOn", PageTimeout);
        Assert.Null(Failure(app));
        ShootSecurity(app, handle, shots, "two-step-off", language, problems, "TwoStepTurnOn");

        Press(turnOn);
        _ = app.FindById("ReauthGoogle", PageTimeout);
        ShootSecurity(app, handle, shots, "two-step-confirm-it-is-you", language, problems, "ReauthGoogle", "ReauthMicrosoft", "ReauthApple", "ReauthUsePassword", "ReauthBack");

        GivePassword(app);
        _ = app.FindById("TwoStepKey", PageTimeout);
        _ = app.FindById("TwoStepQr", PageTimeout);
        ShootSecurity(app, handle, shots, "two-step-scan", language, problems, "TwoStepCopyKey", "TwoStepOpen", "TwoStepConfirm", "TwoStepBack");

        // A code the scene refuses: the scan step stays with its failure, and
        // the typed code left its box as it went, while the box still shows.
        app.FindById("TwoStepCode", PageTimeout, onScreen: false).AsTextBox().Text = RefusedCode;
        Press(app.FindById("TwoStepConfirm", PageTimeout, onScreen: false), allow: "the scene's own confirm of two-step verification, which refuses this code; nothing reaches the service");
        Expect(app, () => app.TryFindById("TwoStepProblem", onScreen: false) is { } problem && NameOf(problem).Length > 0, "the refused code's failure");
        Assert.Empty(app.FindById("TwoStepCode", PageTimeout, onScreen: false).AsTextBox().Text);

        app.FindById("TwoStepCode", PageTimeout, onScreen: false).AsTextBox().Text = "123456";
        Press(app.FindById("TwoStepConfirm", PageTimeout, onScreen: false), allow: "the scene's own confirm of two-step verification; nothing reaches the service");
        _ = app.FindById("TwoStepCodes", PageTimeout, onScreen: false);
        ShootSecurity(app, handle, shots, "two-step-recovery-codes", language, problems, "TwoStepCopyCodes", "TwoStepCodesDone");

        Press(app.FindById("TwoStepCodesDone", PageTimeout, onScreen: false));
        _ = app.FindById("TwoStepTurnOff", PageTimeout);
        _ = app.FindById("TwoStepNotice", PageTimeout, onScreen: false);
        ShootSecurity(app, handle, shots, "two-step-on", language, problems, "TwoStepTurnOff");
        InstalledApp.Log("two-step verification turned on through the password");

        _ = OpenById(app, "NavAccount");
        Press(
            app.FindById("DeleteAccountButton", PageTimeout, onScreen: false),
            allow: "it opens the deletion screen; nothing is deleted without the typed address and Delete my account, which the walk never presses");
        _ = app.FindById("DeleteAccountHeading", PageTimeout);
        Assert.Null(Failure(app));
        ShootSecurity(app, handle, shots, "delete-account", language, problems, "ReauthGoogle", "ReauthMicrosoft", "ReauthApple", "ReauthUsePassword");

        GivePassword(app);
        _ = app.FindById("DeleteAccountEmail", PageTimeout, onScreen: false);
        ShootSecurity(app, handle, shots, "delete-account-last-step", language, problems, "DeleteAccountSubmit");
        InstalledApp.Log("the deletion reached its last step; nothing was deleted");

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
        Assert.True(app.IsRunning, "the app ended on the account's security pages");
    }

    /// <summary>Confirms it is you with the scene's password, which any password is.</summary>
    private static void GivePassword(InstalledApp app)
    {
        Press(app.FindById("ReauthUsePassword", PageTimeout, onScreen: false));
        var box = app.FindById("ReauthPassword", PageTimeout, onScreen: false);
        box.Focus();
        Keyboard.Type(ScenePassword);
        FlaUI.Core.Input.Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));
        Press(app.FindById("ReauthContinue", PageTimeout, onScreen: false));
    }

    /// <summary>
    /// Checks that each of <paramref name="buttons"/> fits across a window
    /// <see cref="SecurityNarrowWidth"/> wide, and saves the step at
    /// 1920x1080 and at that width when screenshots are on. The window is
    /// left at 1920x1080.
    /// </summary>
    private static void ShootSecurity(InstalledApp app, nint handle, string? shots, string step, string language, List<string> problems, params string[] buttons)
    {
        foreach (var (width, height) in new[] { (1920, 1080), (SecurityNarrowWidth, ShortHeight) })
        {
            SizeClient(handle, width, height);
            // A frame for the layout to follow the new size.
            Thread.Sleep(500);
            if (shots is { Length: > 0 })
            {
                Save(handle, Path.Combine(shots, FormattableString.Invariant($"97-account-security-{step}-{language}-{width}x{height}.png")), clientOnly: true);
            }
            if (width == SecurityNarrowWidth)
            {
                FitsAcross(app, handle, buttons, FormattableString.Invariant($"{step} ({language}, {width}x{height})"), problems);
            }
        }
        SizeClient(handle, 1920, 1080);
        Thread.Sleep(500);
    }

    /// <summary>
    /// Whether each element with one of <paramref name="ids"/> lies across
    /// the client area, its label wrapped rather than cut. One below the fold
    /// is scrolled to first, as a member scrolls to it: off screen, UI
    /// Automation gives it no rectangle to measure.
    /// </summary>
    private static void FitsAcross(InstalledApp app, nint handle, string[] ids, string where, List<string> problems)
    {
        _ = Native.GetClientRect(handle, out var client);
        var origin = default(Native.Point);
        _ = Native.ClientToScreen(handle, ref origin);
        foreach (var id in ids)
        {
            var found = AllById(app, id);
            if (found.Length == 0)
            {
                problems.Add($"{where}: no {id} at all");
            }
            foreach (var element in found)
            {
                // UI Automation's "no scroll" for the axis left alone.
                const double NoScroll = -1;
                var scroller = BringOnScreen(app, element);
                var box = element.BoundingRectangle;
                if (box.Width <= 0 || box.Left < origin.X || box.Right > origin.X + client.Width)
                {
                    problems.Add(FormattableString.Invariant($"{where}: {id} \"{NameOf(element)}\" is {box.Left - origin.X}..{box.Right - origin.X} across a client area {client.Width} wide"));
                }
                scroller?.SetScrollPercent(NoScroll, 0);
            }
        }
    }

    /// <summary>
    /// Scrolls the scroll viewer holding <paramref name="element"/>, from the
    /// top down, until the element is on screen, and returns that scroll
    /// viewer; null when the element is on screen already or no scroll viewer
    /// holds it.
    /// </summary>
    private static IScrollPattern? BringOnScreen(InstalledApp app, AutomationElement element)
    {
        // UI Automation's "no scroll" for the axis left alone.
        const double NoScroll = -1;
        if (!element.Properties.IsOffscreen.ValueOrDefault)
        {
            return null;
        }
        var walker = app.Automation.TreeWalkerFactory.GetControlViewWalker();
        for (var at = walker.GetParent(element); at is not null; at = walker.GetParent(at))
        {
            if (at.Patterns.Scroll.PatternOrDefault is { } scroll && scroll.VerticallyScrollable.ValueOrDefault)
            {
                for (var percent = 0; percent <= 100 && element.Properties.IsOffscreen.ValueOrDefault; percent += 10)
                {
                    scroll.SetScrollPercent(NoScroll, percent);
                    Thread.Sleep(150);
                }
                return scroll;
            }
        }
        return null;
    }
}
