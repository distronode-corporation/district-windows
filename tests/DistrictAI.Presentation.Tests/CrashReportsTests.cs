using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels;
using Xunit;

namespace DistrictAI.Presentation.Tests;

public sealed class CrashReportsTests
{
    /// <summary>
    /// A new user (or one upgrading from a version that never asked) is asked
    /// once. Until then the reporter is handed Denied. "Don't send", the
    /// question's default and what closing it answers, keeps Denied, and no
    /// later start asks again.
    /// </summary>
    [Fact]
    public void TheQuestionIsAskedOnceAndItsDefaultKeepsDenied()
    {
        var answers = new FakeCrashAnswers();
        var reporter = new RecordingCrashReporter();
        var crash = Crash.Reports(answers: answers, reporter: reporter);
        Assert.True(crash.Available);
        Assert.Null(crash.Answer);
        Assert.True(crash.ShouldAsk);

        crash.Start();
        Assert.Equal([CrashConsent.Denied], reporter.Applied);

        crash.Answered(send: false);
        Assert.False(crash.ShouldAsk);
        Assert.Equal(CrashConsent.Denied, crash.Answer);
        Assert.False(crash.Sending);
        Assert.Equal(CrashConsent.Denied, answers.Kept);
        Assert.Equal([CrashConsent.Denied, CrashConsent.Denied], reporter.Applied);

        // The next start: the answer is kept, so no question, and Denied again.
        var again = Crash.Reports(answers: answers, reporter: reporter);
        Assert.False(again.ShouldAsk);
        again.Start();
        Assert.Equal([CrashConsent.Denied, CrashConsent.Denied, CrashConsent.Denied], reporter.Applied);
        Assert.Equal(1, answers.Writes);
    }

    [Fact]
    public void SendReportsKeepsGrantedAndTheNextStartAppliesIt()
    {
        var answers = new FakeCrashAnswers();
        var reporter = new RecordingCrashReporter();
        var changes = 0;
        var crash = Crash.Reports(answers: answers, reporter: reporter);
        crash.Changed += (_, _) => changes++;

        crash.Answered(send: true);
        Assert.True(crash.Sending);
        Assert.False(crash.ShouldAsk);
        Assert.Equal(CrashConsent.Granted, answers.Kept);
        Assert.Equal(1, changes);

        Crash.Reports(answers: answers, reporter: reporter).Start();
        Assert.Equal([CrashConsent.Granted, CrashConsent.Granted], reporter.Applied);
    }

    /// <summary>
    /// A build with no DSN (from source, a fork's, any CI build that is not a
    /// release) never asks and shows no switch, and what the reporter is
    /// handed has no DSN, so it sets nothing up.
    /// </summary>
    [Fact]
    public void WithoutADsnNothingIsAskedOrShown()
    {
        var reporter = new RecordingCrashReporter();
        var crash = Crash.Reports(dsn: "  ", reporter: reporter);
        Assert.False(crash.Available);
        Assert.False(crash.ShouldAsk);
        crash.Start();
        Assert.Null(Assert.Single(reporter.With).Dsn);

        var account = new AccountViewModel(new FakeStartupTask(), crash);
        Assert.False(account.CrashReportsVisible);
        Assert.False(account.CrashReportsOn);
    }

    /// <summary>An answer that cannot be kept still holds until the app quits.</summary>
    [Fact]
    public void AnAnswerThatCannotBeKeptStillHolds()
    {
        var answers = new FakeCrashAnswers { Writable = false };
        var reporter = new RecordingCrashReporter();
        var crash = Crash.Reports(answers: answers, reporter: reporter);
        crash.Answered(send: true);
        Assert.Null(answers.Kept);
        Assert.True(crash.Sending);
        Assert.False(crash.ShouldAsk);
        Assert.Equal([CrashConsent.Granted], reporter.Applied);
    }

    [Fact]
    public void TheReleaseNamesTheAppTheVersionAndTheBuild()
    {
        var settings = CrashReports.SettingsFor(" https://key@example.com/1\n", "3.1.0", "0123456789ab");
        Assert.Equal("https://key@example.com/1", settings.Dsn);
        Assert.Equal("com.distronode.DistrictAI@3.1.0+0123456789ab", settings.Release);
        Assert.Equal("production", settings.Environment);

        var local = CrashReports.SettingsFor(null, "3.1.0", " ");
        Assert.Null(local.Dsn);
        Assert.Equal("com.distronode.DistrictAI@3.1.0+local", local.Release);
    }

    [Fact]
    public void ItNeedsSettingsAStoreAndAReporter()
    {
        var settings = CrashReports.SettingsFor(Crash.Dsn, "3.1.0", null);
        Assert.Throws<ArgumentNullException>(() => new CrashReports(null!, new FakeCrashAnswers(), new RecordingCrashReporter()));
        Assert.Throws<ArgumentNullException>(() => new CrashReports(settings, null!, new RecordingCrashReporter()));
        Assert.Throws<ArgumentNullException>(() => new CrashReports(settings, new FakeCrashAnswers(), null!));
    }
}

public sealed class AccountCrashReportsTests
{
    /// <summary>
    /// The switch shows in a build that can send, off until the user says
    /// yes, and hands each change to the reporter as it is made: Granted, then
    /// Denied.
    /// </summary>
    [Fact]
    public void TheSwitchHandsTheNewAnswerToTheReporter()
    {
        var answers = new FakeCrashAnswers();
        var reporter = new RecordingCrashReporter();
        var crash = Crash.Reports(answers: answers, reporter: reporter);
        var account = new AccountViewModel(new FakeStartupTask(), crash);
        Assert.True(account.CrashReportsVisible);
        Assert.False(account.CrashReportsOn);
        Assert.Empty(reporter.Applied);

        account.CrashReportsOn = true;
        Assert.Equal([CrashConsent.Granted], reporter.Applied);
        Assert.Equal(CrashConsent.Granted, answers.Kept);

        account.CrashReportsOn = false;
        Assert.Equal([CrashConsent.Granted, CrashConsent.Denied], reporter.Applied);
        Assert.Equal(CrashConsent.Denied, answers.Kept);
        Assert.False(crash.ShouldAsk);
    }

    /// <summary>
    /// The question's answer moves the switch, and the switch does not hand
    /// it on a second time; a page made after an answer starts from it.
    /// </summary>
    [Fact]
    public void TheSwitchFollowsTheQuestionWithoutSendingItAgain()
    {
        var answers = new FakeCrashAnswers();
        var reporter = new RecordingCrashReporter();
        var crash = Crash.Reports(answers: answers, reporter: reporter);
        var account = new AccountViewModel(new FakeStartupTask(), crash);

        crash.Answered(send: true);
        Assert.True(account.CrashReportsOn);
        Assert.Equal([CrashConsent.Granted], reporter.Applied);

        var later = new AccountViewModel(new FakeStartupTask(), Crash.Reports(answers: answers, reporter: reporter));
        Assert.True(later.CrashReportsOn);
        Assert.Equal([CrashConsent.Granted], reporter.Applied);
    }
}
