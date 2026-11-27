// SPDX-FileCopyrightText: Distronode Corporation
//
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using DistrictAI.Core;
using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels;
using DistrictAI.ViewModels.AccountSecurity;
using Xunit;

namespace DistrictAI.Presentation.Tests.AccountSecurity;

/// <summary>
/// Stands in for the core, and reads each typed secret as it is sent: the
/// view models clear the bytes right after, as the real core has already
/// copied them by then.
/// </summary>
internal sealed class SecretReadingSink : ICoreSink
{
    public List<UiEvent> Sent { get; } = [];

    /// <summary>Each secret as it was when sent, in order.</summary>
    public List<string> Typed { get; } = [];

    /// <summary>The byte arrays sent, to check they were cleared after.</summary>
    public List<byte[]> Buffers { get; } = [];

    public void Send(UiEvent uiEvent)
    {
        Sent.Add(uiEvent);
        foreach (var bytes in Secrets(uiEvent))
        {
            Typed.Add(Encoding.UTF8.GetString(bytes));
            Buffers.Add(bytes);
        }
    }

    private static IEnumerable<byte[]> Secrets(UiEvent uiEvent) => uiEvent switch
    {
        UiEvent.TwoStep { Action: TwoStepAction.SubmitPassword submit } => submit.Code is null ? [submit.Password] : [submit.Password, submit.Code],
        UiEvent.TwoStep { Action: TwoStepAction.Confirm confirm } => [confirm.Code],
        UiEvent.AccountDeletion { Action: DeleteAccountAction.SubmitPassword submit } => submit.Code is null ? [submit.Password] : [submit.Password, submit.Code],
        _ => [],
    };

    public static (PageContext Context, SecretReadingSink Sink) Context(bool opens = true)
    {
        var sink = new SecretReadingSink();
        return (new PageContext(sink, new AnsweringBrowser(opens)), sink);
    }
}

/// <summary>A core that is gone: every send throws.</summary>
internal sealed class ThrowingSink : ICoreSink
{
    public void Send(UiEvent uiEvent) => throw new InvalidOperationException("the core is gone");
}

/// <summary>A browser that opens everything, or nothing.</summary>
internal sealed class AnsweringBrowser(bool opens) : IBrowser
{
    public List<string> Opened { get; } = [];

    public Task<bool> OpenAsync(string url)
    {
        Opened.Add(url);
        return Task.FromResult(opens);
    }
}

/// <summary>A clipboard that keeps what it holds, and can refuse.</summary>
internal sealed class FakeClipboard : ISecretClipboard
{
    public string? Holds { get; set; }

    public bool Refuses { get; set; }

    public int Cleared { get; private set; }

    public bool Copy(string secret)
    {
        if (Refuses)
        {
            return false;
        }
        Holds = secret;
        return true;
    }

    public void ClearIfStill(string secret)
    {
        if (Holds == secret)
        {
            Holds = null;
            Cleared++;
        }
    }
}

internal static class A
{
    public static ReauthView Reauth(ReauthModeView mode = ReauthModeView.Choose, string? codePrompt = null, string? waiting = null) =>
        new(
            "Confirm it is you",
            "Sign in again the way you usually do.",
            mode,
            waiting,
            [
                new ReauthChoiceView(ReauthProviderView.Google, "Continue with Google"),
                new ReauthChoiceView(ReauthProviderView.Microsoft, "Continue with Microsoft"),
                new ReauthChoiceView(ReauthProviderView.Apple, "Continue with Apple"),
            ],
            "Use my password",
            "Your password",
            codePrompt,
            "Authenticator code or recovery code",
            "Continue",
            "Back");

    public static TwoStepScanView Scan(byte[]? qr = null) => new(
        "Add District AI to your authenticator app",
        "Scan this QR code.",
        qr,
        "QR code for your authenticator app",
        "The QR code could not be shown.",
        "Key",
        Encoding.UTF8.GetBytes("JBSWY3DPEHPK3PXP"),
        "Copy key",
        "Copied",
        Encoding.UTF8.GetBytes("otpauth://totp/District?secret=JBSWY3DPEHPK3PXP"),
        "Open in an authenticator app",
        "No app on this device opened it.",
        "Then enter the 6-digit code.",
        "6-digit code",
        "Turn on two-step verification",
        "Back");

    public static TwoStepCodesView Codes() => new(
        "Save your recovery codes",
        "They will not be shown again.",
        Encoding.UTF8.GetBytes("aaaa-bbbb\ncccc-dddd"),
        2,
        "Copy all",
        "Copied",
        "I have saved them");

    public static TwoStepView TwoStep(
        TwoStepStepView step = TwoStepStepView.Overview,
        bool busy = false,
        string? failure = null,
        string? notice = null,
        TwoStepChangeView? offered = TwoStepChangeView.TurnOn,
        ReauthView? reauth = null,
        TwoStepScanView? scan = null,
        TwoStepCodesView? codes = null) =>
        new(
            "Two-step verification",
            step,
            busy,
            failure,
            notice,
            offered is null ? null : offered == TwoStepChangeView.TurnOn ? "Off" : "On",
            offered == TwoStepChangeView.TurnOff ? ["On since Oct 1, 2026.", "7 recovery codes left."] : ["When it is on, signing in also asks for a code."],
            offered,
            offered is null ? null : offered == TwoStepChangeView.TurnOn ? "Turn on" : "Turn off",
            "Try again",
            reauth,
            scan,
            codes);

    public static DeleteAccountView Deletion(
        DeleteAccountStepView step = DeleteAccountStepView.Explain,
        bool busy = false,
        string? failureTitle = null,
        string? failure = null,
        ReauthView? reauth = null) =>
        new(
            "Delete your account",
            ["Your profile is deleted.", "Subscriptions are cancelled.", "Others' workspaces are kept.", "This cannot be undone."],
            step,
            busy,
            failureTitle,
            failure,
            step == DeleteAccountStepView.Explain ? reauth ?? Reauth() : null,
            step == DeleteAccountStepView.Confirm
                ? new DeleteConfirmView("Last step", "Type your account's email address.", "Your email address", busy ? "Deleting…" : "Delete my account")
                : null);
}

public sealed class ReauthViewModelTests
{
    private static TwoStepViewModel Screen(out SecretReadingSink sink)
    {
        var screen = new TwoStepViewModel(new SecretCopies(new FakeClipboard(), new ManualTime(DateTimeOffset.UnixEpoch)));
        var (context, recorder) = SecretReadingSink.Context();
        screen.Attach(context);
        sink = recorder;
        return screen;
    }

    [Fact]
    public void ChoosingOffersTheProvidersInOrderAndSendsTheOneChosen()
    {
        var screen = Screen(out var sink);
        screen.Show(A.TwoStep(TwoStepStepView.Reauth, reauth: A.Reauth()));
        var reauth = screen.Reauth;

        Assert.True(reauth.IsShown);
        Assert.True(reauth.IsChoosing);
        Assert.False(reauth.IsPassword);
        Assert.False(reauth.HasWaiting);
        Assert.Equal("Confirm it is you", reauth.Heading);
        Assert.Equal("Sign in again the way you usually do.", reauth.Intro);
        Assert.Equal("Use my password", reauth.UsePasswordLabel);
        Assert.Equal("Back", reauth.BackLabel);
        Assert.Equal(["ReauthGoogle", "ReauthMicrosoft", "ReauthApple"], reauth.Choices.Select(choice => choice.AutomationId));
        Assert.Equal("Continue with Google", reauth.Choices[0].Label);

        reauth.Choose(ReauthProviderView.Microsoft);
        reauth.UsePasswordCommand.Execute(null);
        reauth.BackCommand.Execute(null);
        Assert.Equal(
            [
                new UiEvent.TwoStep(new TwoStepAction.UseProvider(ReauthProviderView.Microsoft)),
                new UiEvent.TwoStep(new TwoStepAction.UsePassword()),
                new UiEvent.TwoStep(new TwoStepAction.Back()),
            ],
            sink.Sent);
    }

    [Fact]
    public void TheBrowserOpenSaysItWaitsAndStillOffersTheProviders()
    {
        var screen = Screen(out _);
        screen.Show(A.TwoStep(TwoStepStepView.Reauth, reauth: A.Reauth(ReauthModeView.Browser, waiting: "Waiting for sign-in.")));
        Assert.True(screen.Reauth.IsChoosing);
        Assert.True(screen.Reauth.HasWaiting);
        Assert.Equal("Waiting for sign-in.", screen.Reauth.Waiting);
        Assert.Equal(["ReauthGoogle", "ReauthMicrosoft", "ReauthApple"], screen.Reauth.Choices.Select(choice => choice.AutomationId));
    }

    [Fact]
    public void NothingIsSentWhileBusy()
    {
        var screen = Screen(out var sink);
        screen.Show(A.TwoStep(TwoStepStepView.Reauth, busy: true, reauth: A.Reauth(ReauthModeView.Password)));
        Assert.False(screen.Reauth.Enabled);

        screen.Reauth.Choose(ReauthProviderView.Google);
        screen.Reauth.UsePasswordCommand.Execute(null);
        screen.Reauth.BackCommand.Execute(null);
        Assert.False(screen.Reauth.Submit("hunter2", string.Empty));
        Assert.Empty(sink.Sent);
    }

    [Fact]
    public void ThePasswordGoesOnceAsBytesThatAreClearedAndTheBoxesEmpty()
    {
        var screen = Screen(out var sink);
        var cleared = 0;
        screen.Reauth.FieldsCleared += (_, _) => cleared++;
        screen.Show(A.TwoStep(TwoStepStepView.Reauth, reauth: A.Reauth(ReauthModeView.Password)));
        Assert.True(screen.Reauth.IsPassword);
        Assert.False(screen.Reauth.NeedsCode);

        Assert.False(screen.Reauth.Submit(string.Empty, string.Empty));
        Assert.Equal(0, cleared);
        // Without the code step, a code typed anyway is not sent.
        Assert.True(screen.Reauth.Submit("correct horse", "123456"));

        Assert.Equal(["correct horse"], sink.Typed);
        var sent = Assert.IsType<TwoStepAction.SubmitPassword>(Assert.IsType<UiEvent.TwoStep>(Assert.Single(sink.Sent)).Action);
        Assert.Null(sent.Code);
        Assert.All(sink.Buffers, buffer => Assert.All(buffer, b => Assert.Equal(0, b)));
        Assert.Equal(1, cleared);
        // Nothing of it reaches a record's text.
        Assert.DoesNotContain("correct horse", sink.Sent[0].ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void OnceAskedForTheCodeGoesWithThePassword()
    {
        var screen = Screen(out var sink);
        screen.Show(A.TwoStep(TwoStepStepView.Reauth, reauth: A.Reauth(ReauthModeView.Password, codePrompt: "Enter the code.")));
        Assert.True(screen.Reauth.NeedsCode);
        Assert.Equal("Enter the code.", screen.Reauth.CodePrompt);

        Assert.False(screen.Reauth.Submit("pw", "  "));
        Assert.True(screen.Reauth.Submit("pw", "aaaa-bbbb"));
        Assert.Equal(["pw", "aaaa-bbbb"], sink.Typed);
    }

    [Fact]
    public void LeavingThePasswordStepEmptiesTheBoxes()
    {
        var screen = Screen(out _);
        var cleared = 0;
        screen.Reauth.FieldsCleared += (_, _) => cleared++;
        screen.Show(A.TwoStep(TwoStepStepView.Reauth, reauth: A.Reauth(ReauthModeView.Password)));
        screen.Show(A.TwoStep(TwoStepStepView.Reauth, reauth: A.Reauth(ReauthModeView.Password)));
        Assert.Equal(0, cleared);
        screen.Show(A.TwoStep(TwoStepStepView.Reauth, reauth: A.Reauth()));
        Assert.Equal(1, cleared);
        screen.Show(A.TwoStep());
        Assert.False(screen.Reauth.IsShown);
        Assert.Equal(1, cleared);
        screen.Leave();
        Assert.Equal(2, cleared);
    }

    [Fact]
    public void WithNoPageNothingBreaks()
    {
        var reauth = new ReauthViewModel(
            p => new UiEvent.TwoStep(new TwoStepAction.UseProvider(p)),
            () => new UiEvent.TwoStep(new TwoStepAction.UsePassword()),
            (pw, code) => new UiEvent.TwoStep(new TwoStepAction.SubmitPassword(pw, code)),
            () => new UiEvent.TwoStep(new TwoStepAction.Back()));
        reauth.Show(A.Reauth(ReauthModeView.Password), busy: false);
        reauth.Choose(ReauthProviderView.Apple);
        reauth.UsePasswordCommand.Execute(null);
        reauth.BackCommand.Execute(null);
        Assert.True(reauth.Submit("pw", string.Empty));
        Assert.Throws<ArgumentNullException>(() => reauth.Submit(null!, string.Empty));
        Assert.Throws<ArgumentNullException>(() => reauth.Submit("pw", null!));
    }
}

public sealed class TwoStepViewModelTests
{
    private static (TwoStepViewModel Screen, SecretReadingSink Sink, FakeClipboard Clipboard, ManualTime Time, AnsweringBrowser Browser) Make(bool opens = true)
    {
        var clipboard = new FakeClipboard();
        var time = new ManualTime(DateTimeOffset.UnixEpoch);
        var screen = new TwoStepViewModel(new SecretCopies(clipboard, time));
        var sink = new SecretReadingSink();
        var browser = new AnsweringBrowser(opens);
        screen.Attach(new PageContext(sink, browser));
        return (screen, sink, clipboard, time, browser);
    }

    [Fact]
    public void TheStatusAndTheChangeItOffers()
    {
        var (screen, sink, _, _, _) = Make();
        Assert.True(screen.IsLoading);
        screen.Show(A.TwoStep(TwoStepStepView.Loading, busy: true, offered: null));
        Assert.True(screen.IsLoading);
        Assert.True(screen.Busy);
        Assert.False(screen.HasOffered);
        screen.BeginCommand.Execute(null);

        screen.Show(A.TwoStep(offered: TwoStepChangeView.TurnOff, notice: "Two-step verification is on."));
        Assert.Equal("Two-step verification", screen.Title);
        Assert.True(screen.IsOverview);
        Assert.False(screen.IsLoading);
        Assert.Equal("On", screen.StatusLabel);
        Assert.Equal("On since Oct 1, 2026.\n7 recovery codes left.", screen.StatusLines);
        Assert.Equal("Turn off", screen.OfferedLabel);
        Assert.True(screen.OffersTurnOff);
        Assert.False(screen.OffersTurnOn);
        Assert.True(screen.HasNotice);
        Assert.Equal("Two-step verification is on.", screen.Notice);
        screen.BeginCommand.Execute(null);
        screen.DismissNoticesCommand.Execute(null);

        Assert.Equal(
            [
                new UiEvent.TwoStep(new TwoStepAction.Begin(TwoStepChangeView.TurnOff)),
                new UiEvent.TwoStep(new TwoStepAction.DismissNotices()),
            ],
            sink.Sent);
    }

    [Fact]
    public void AFirstReadThatFailedOffersTryAgain()
    {
        var (screen, sink, _, _, _) = Make();
        screen.Show(A.TwoStep(TwoStepStepView.LoadFailed, failure: "Could not be read.", offered: null));
        Assert.True(screen.IsLoadFailed);
        Assert.True(screen.HasFailure);
        Assert.Equal("Could not be read.", screen.Failure);
        Assert.Equal("Try again", screen.RetryLabel);
        screen.RetryCommand.Execute(null);

        screen.Show(A.TwoStep(TwoStepStepView.Loading, busy: true, offered: null));
        screen.RetryCommand.Execute(null);
        // Dismissing sends nothing the core must wait for, so it goes while busy.
        screen.DismissNoticesCommand.Execute(null);
        Assert.Equal(
            [new UiEvent.TwoStep(new TwoStepAction.Retry()), new UiEvent.TwoStep(new TwoStepAction.DismissNotices())],
            sink.Sent);
    }

    [Fact]
    public void TheSecretShowsWhileItsStepDoesAndGoesWhenThePageIsLeft()
    {
        var (screen, _, _, _, _) = Make();
        var cleared = 0;
        screen.SecretsCleared += (_, _) => cleared++;
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        screen.Show(A.TwoStep(TwoStepStepView.Scan, scan: A.Scan(png)));

        Assert.True(screen.IsScan);
        Assert.Equal("JBSWY3DPEHPK3PXP", screen.Key);
        Assert.Same(png, screen.QrPng);
        Assert.True(screen.HasQr);
        Assert.False(screen.ShowsQrUnreadable);
        Assert.Equal("Add District AI to your authenticator app", screen.ScanTitle);
        Assert.Equal("Scan this QR code.", screen.ScanIntro);
        Assert.Equal("QR code for your authenticator app", screen.QrLabel);
        Assert.Equal("Key", screen.KeyLabel);
        Assert.Equal("Copy key", screen.CopyKeyLabel);
        Assert.Equal("Open in an authenticator app", screen.OpenLabel);
        Assert.Equal("6-digit code", screen.CodeLabel);
        Assert.Equal("Then enter the 6-digit code.", screen.CodePrompt);
        Assert.Equal("Turn on two-step verification", screen.ConfirmLabel);
        Assert.Equal("Back", screen.BackLabel);

        screen.Leave();
        Assert.Equal(string.Empty, screen.Key);
        Assert.Null(screen.QrPng);
        Assert.False(screen.HasQr);
        Assert.Equal(1, cleared);

        // Back on the page, the core still on the step: it shows again.
        screen.Show(A.TwoStep(TwoStepStepView.Scan, scan: A.Scan(png)));
        Assert.Equal("JBSWY3DPEHPK3PXP", screen.Key);

        // The step moves on: the secret goes.
        screen.Show(A.TwoStep());
        Assert.Equal(string.Empty, screen.Key);
        Assert.Equal(2, cleared);
    }

    [Fact]
    public void APictureWindowsCannotDrawSaysToEnterTheKey()
    {
        var (screen, _, _, _, _) = Make();
        var png = new byte[] { 1, 2, 3 };
        screen.Show(A.TwoStep(TwoStepStepView.Scan, scan: A.Scan(png)));
        // Another picture's failure, arriving late, changes nothing.
        screen.QrUnreadableHere([9]);
        Assert.True(screen.HasQr);
        screen.QrUnreadableHere(png);
        Assert.False(screen.HasQr);
        Assert.True(screen.ShowsQrUnreadable);
    }

    [Fact]
    public void AFailedSendStillEmptiesTheBoxes()
    {
        var reauth = new ReauthViewModel(
            p => new UiEvent.TwoStep(new TwoStepAction.UseProvider(p)),
            () => new UiEvent.TwoStep(new TwoStepAction.UsePassword()),
            (_, _) => throw new InvalidOperationException("the core is gone"),
            () => new UiEvent.TwoStep(new TwoStepAction.Back()));
        var cleared = 0;
        reauth.FieldsCleared += (_, _) => cleared++;
        reauth.Attach(Pages.Context().Context);
        reauth.Show(A.Reauth(ReauthModeView.Password), busy: false);
        Assert.Throws<InvalidOperationException>(() => reauth.Submit("pw", string.Empty));
        Assert.Equal(1, cleared);
    }

    [Fact]
    public void WithoutAPictureTheKeyIsTheWay()
    {
        var (screen, _, _, _, _) = Make();
        screen.Show(A.TwoStep(TwoStepStepView.Scan, scan: A.Scan()));
        Assert.False(screen.HasQr);
        Assert.True(screen.ShowsQrUnreadable);
        Assert.Equal("The QR code could not be shown.", screen.QrUnreadable);
    }

    [Fact]
    public void TheFirstCodeGoesOnceAsBytesThatAreCleared()
    {
        var (screen, sink, _, _, _) = Make();
        Assert.False(screen.Confirm("123456"));
        screen.Show(A.TwoStep(TwoStepStepView.Scan, scan: A.Scan()));
        Assert.False(screen.Confirm("   "));
        Assert.True(screen.Confirm("123456"));
        Assert.Equal(["123456"], sink.Typed);
        Assert.All(sink.Buffers, buffer => Assert.All(buffer, b => Assert.Equal(0, b)));

        screen.Show(A.TwoStep(TwoStepStepView.Scan, busy: true, scan: A.Scan()));
        Assert.False(screen.Confirm("123456"));
        screen.BackCommand.Execute(null);
        Assert.Single(sink.Sent);
        Assert.Throws<ArgumentNullException>(() => screen.Confirm(null!));
    }

    [Fact]
    public void ACopyLeavesTheClipboardAfterTwoMinutesUnlessSomethingElseWasCopied()
    {
        var (screen, _, clipboard, time, _) = Make();
        screen.Show(A.TwoStep(TwoStepStepView.Scan, scan: A.Scan()));
        screen.CopyKeyCommand.Execute(null);
        Assert.Equal("JBSWY3DPEHPK3PXP", clipboard.Holds);
        Assert.Equal("Copied", screen.CopyKeyLabel);

        time.Advance(SecretCopies.Lifetime - TimeSpan.FromSeconds(1));
        Assert.Equal("JBSWY3DPEHPK3PXP", clipboard.Holds);
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.Null(clipboard.Holds);
        Assert.Equal(1, clipboard.Cleared);

        // Something else copied since: left alone.
        screen.CopyKeyCommand.Execute(null);
        clipboard.Holds = "someone else's text";
        time.Advance(SecretCopies.Lifetime);
        Assert.Equal("someone else's text", clipboard.Holds);
        Assert.Equal(1, clipboard.Cleared);
    }

    [Fact]
    public void ASecondCopyIsNotClearedByTheFirstCopysTimer()
    {
        var (screen, _, clipboard, time, _) = Make();
        screen.Show(A.TwoStep(TwoStepStepView.Scan, scan: A.Scan()));
        screen.CopyKeyCommand.Execute(null);
        time.Advance(TimeSpan.FromSeconds(90));
        // The same key again: its time starts again.
        screen.CopyKeyCommand.Execute(null);
        time.Advance(TimeSpan.FromSeconds(60));
        Assert.Equal("JBSWY3DPEHPK3PXP", clipboard.Holds);
        Assert.Equal(0, clipboard.Cleared);
        time.Advance(TimeSpan.FromSeconds(60));
        Assert.Null(clipboard.Holds);
        Assert.Equal(1, clipboard.Cleared);
    }

    [Fact]
    public void ARefusedCopySaysNothingAndTimesNothing()
    {
        var (screen, _, clipboard, time, _) = Make();
        clipboard.Refuses = true;
        screen.Show(A.TwoStep(TwoStepStepView.Scan, scan: A.Scan()));
        screen.CopyKeyCommand.Execute(null);
        Assert.Equal("Copy key", screen.CopyKeyLabel);
        Assert.Equal(0, time.TimersCreated);
    }

    [Fact]
    public void TheCodesShowOnceWithCopyAllAndGoWhenThePageIsLeft()
    {
        var (screen, sink, clipboard, _, _) = Make();
        screen.Show(A.TwoStep(TwoStepStepView.RecoveryCodes, offered: TwoStepChangeView.TurnOff, codes: A.Codes()));
        Assert.True(screen.IsCodes);
        Assert.Equal("aaaa-bbbb\ncccc-dddd", screen.Codes);
        Assert.Equal("Save your recovery codes", screen.CodesTitle);
        Assert.Equal("They will not be shown again.", screen.CodesWarning);
        Assert.Equal("I have saved them", screen.DoneLabel);
        Assert.Equal("Copy all", screen.CopyAllLabel);

        screen.CopyCodesCommand.Execute(null);
        Assert.Equal("aaaa-bbbb\ncccc-dddd", clipboard.Holds);
        Assert.Equal("Copied", screen.CopyAllLabel);
        screen.FinishRecoveryCodesCommand.Execute(null);
        Assert.Equal([new UiEvent.TwoStep(new TwoStepAction.FinishRecoveryCodes())], sink.Sent);

        screen.Leave();
        Assert.Equal(string.Empty, screen.Codes);
        // Nothing to copy once they are gone.
        clipboard.Holds = null;
        screen.CopyCodesCommand.Execute(null);
        screen.CopyKeyCommand.Execute(null);
        Assert.Null(clipboard.Holds);

        screen.Show(A.TwoStep(TwoStepStepView.RecoveryCodes, busy: true, offered: TwoStepChangeView.TurnOff, codes: A.Codes()));
        Assert.Equal("aaaa-bbbb\ncccc-dddd", screen.Codes);
        screen.FinishRecoveryCodesCommand.Execute(null);
        Assert.Single(sink.Sent);
    }

    [Fact]
    public async Task TheKeyOpensInAnAuthenticatorOrSaysNoAppDid()
    {
        var (screen, _, _, _, browser) = Make();
        await screen.OpenAuthenticatorAsync();
        Assert.Empty(browser.Opened);
        screen.Show(A.TwoStep(TwoStepStepView.Scan, scan: A.Scan()));
        await screen.OpenAuthenticatorAsync();
        Assert.Equal(["otpauth://totp/District?secret=JBSWY3DPEHPK3PXP"], browser.Opened);
        Assert.False(screen.ShowsOpenFailed);

        var (refused, _, _, _, _) = Make(opens: false);
        refused.Show(A.TwoStep(TwoStepStepView.Scan, scan: A.Scan()));
        await refused.OpenAuthenticatorAsync();
        Assert.True(refused.ShowsOpenFailed);
        Assert.Equal("No app on this device opened it.", refused.OpenFailed);
    }

    [Fact]
    public async Task WithNoPageNothingBreaks()
    {
        var screen = new TwoStepViewModel(new SecretCopies(new FakeClipboard(), new ManualTime(DateTimeOffset.UnixEpoch)));
        screen.Show(A.TwoStep(TwoStepStepView.Scan, scan: A.Scan()));
        Assert.True(screen.Confirm("1"));
        await screen.OpenAuthenticatorAsync();
        screen.RetryCommand.Execute(null);
        screen.BeginCommand.Execute(null);
        screen.BackCommand.Execute(null);
        screen.FinishRecoveryCodesCommand.Execute(null);
        screen.DismissNoticesCommand.Execute(null);
        Assert.Throws<ArgumentNullException>(() => new TwoStepViewModel(null!));
        Assert.Throws<ArgumentNullException>(() => screen.Show(null!));
    }
}

public sealed class SecretCopiesTests
{
    [Fact]
    public void ATimerTickGoesBackToTheThreadThatCopied()
    {
        var clipboard = new FakeClipboard();
        var time = new ManualTime(DateTimeOffset.UnixEpoch);
        var copies = new SecretCopies(clipboard, time);
        var ui = new QueueingContext();
        var before = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(ui);
        try
        {
            Assert.True(copies.Copy("secret"));
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(before);
        }
        time.Advance(SecretCopies.Lifetime);
        Assert.Equal("secret", clipboard.Holds);
        Assert.Equal(1, ui.Pending);
        ui.RunAll();
        Assert.Null(clipboard.Holds);
    }

    [Fact]
    public void ItRefusesNothingToWorkWith()
    {
        Assert.Throws<ArgumentNullException>(() => new SecretCopies(null!, TimeProvider.System));
        Assert.Throws<ArgumentNullException>(() => new SecretCopies(new FakeClipboard(), null!));
        Assert.Throws<ArgumentNullException>(() => new SecretCopies(new FakeClipboard(), TimeProvider.System).Copy(null!));
    }
}

public sealed class DeleteAccountViewModelTests
{
    private static (DeleteAccountViewModel Screen, SecretReadingSink Sink) Make()
    {
        var screen = new DeleteAccountViewModel();
        var (context, sink) = SecretReadingSink.Context();
        screen.Attach(context);
        return (screen, sink);
    }

    [Fact]
    public void ItExplainsThenConfirmsItIsYou()
    {
        var (screen, sink) = Make();
        screen.Show(A.Deletion());
        Assert.Equal("Delete your account", screen.Title);
        Assert.Equal(
            "Your profile is deleted.\n\nSubscriptions are cancelled.\n\nOthers' workspaces are kept.\n\nThis cannot be undone.",
            screen.Explain);
        Assert.True(screen.Reauth.IsShown);
        Assert.False(screen.IsConfirm);
        Assert.False(screen.Delete("ada@example.com"));

        screen.Reauth.Choose(ReauthProviderView.Apple);
        screen.Reauth.UsePasswordCommand.Execute(null);
        screen.Show(A.Deletion(reauth: A.Reauth(ReauthModeView.Password)));
        Assert.True(screen.Reauth.Submit("pw", string.Empty));
        screen.Reauth.BackCommand.Execute(null);

        Assert.Equal(
            [
                new UiEvent.AccountDeletion(new DeleteAccountAction.UseProvider(ReauthProviderView.Apple)),
                new UiEvent.AccountDeletion(new DeleteAccountAction.UsePassword()),
                sink.Sent[2],
                new UiEvent.AccountDeletion(new DeleteAccountAction.Back()),
            ],
            sink.Sent);
        Assert.IsType<DeleteAccountAction.SubmitPassword>(Assert.IsType<UiEvent.AccountDeletion>(sink.Sent[2]).Action);
        Assert.Equal(["pw"], sink.Typed);
    }

    [Fact]
    public void TheLastStepSendsTheTypedAddressOnce()
    {
        var (screen, sink) = Make();
        var cleared = 0;
        screen.EmailCleared += (_, _) => cleared++;
        screen.Show(A.Deletion(DeleteAccountStepView.Confirm));
        Assert.True(screen.IsConfirm);
        Assert.False(screen.Reauth.IsShown);
        Assert.Equal("Last step", screen.ConfirmTitle);
        Assert.Equal("Type your account's email address.", screen.TypeEmail);
        Assert.Equal("Your email address", screen.EmailLabel);
        Assert.Equal("Delete my account", screen.SubmitLabel);
        Assert.False(screen.Delete("  "));
        Assert.Equal(0, cleared);
        Assert.True(screen.Delete("ada@example.com"));
        // The box empties as the address goes.
        Assert.Equal(1, cleared);

        screen.Show(A.Deletion(DeleteAccountStepView.Confirm, busy: true));
        Assert.Equal("Deleting…", screen.SubmitLabel);
        Assert.False(screen.Enabled);
        Assert.False(screen.Delete("ada@example.com"));
        Assert.Equal(1, cleared);
        Assert.Equal([new UiEvent.AccountDeletion(new DeleteAccountAction.Delete("ada@example.com"))], sink.Sent);
        Assert.Throws<ArgumentNullException>(() => screen.Delete(null!));
    }

    [Fact]
    public void AFailedSendStillEmptiesTheAddress()
    {
        var screen = new DeleteAccountViewModel();
        screen.Attach(new PageContext(new ThrowingSink(), new AnsweringBrowser(true)));
        var cleared = 0;
        screen.EmailCleared += (_, _) => cleared++;
        screen.Show(A.Deletion(DeleteAccountStepView.Confirm));
        Assert.Throws<InvalidOperationException>(() => screen.Delete("ada@example.com"));
        Assert.Equal(1, cleared);
    }

    [Fact]
    public void ARefusalSaysWhetherAnythingWasDeleted()
    {
        var (screen, _) = Make();
        screen.Show(A.Deletion(DeleteAccountStepView.Confirm, failureTitle: "Your account was not deleted.", failure: "Try again in a moment."));
        Assert.True(screen.HasFailure);
        Assert.Equal("Your account was not deleted.", screen.FailureTitle);
        Assert.Equal("Try again in a moment.", screen.Failure);
        screen.Show(A.Deletion(DeleteAccountStepView.Confirm, failure: "That is not this account's email address."));
        Assert.Equal(string.Empty, screen.FailureTitle);
    }

    [Fact]
    public void TheTypedAddressGoesWhenTheLastStepDoesOrThePageIsLeft()
    {
        var (screen, _) = Make();
        var cleared = 0;
        screen.EmailCleared += (_, _) => cleared++;
        screen.Show(A.Deletion(DeleteAccountStepView.Confirm));
        screen.Show(A.Deletion(DeleteAccountStepView.Confirm));
        Assert.Equal(0, cleared);
        // The proof ran out: back to confirming it is you.
        screen.Show(A.Deletion(failure: "Your confirmation ran out."));
        Assert.Equal(1, cleared);
        screen.Leave();
        Assert.Equal(2, cleared);
        Assert.Throws<ArgumentNullException>(() => screen.Show(null!));
    }

    [Fact]
    public void WithNoPageNothingBreaks()
    {
        var screen = new DeleteAccountViewModel();
        screen.Show(A.Deletion(DeleteAccountStepView.Confirm));
        Assert.True(screen.Delete("ada@example.com"));
    }
}
