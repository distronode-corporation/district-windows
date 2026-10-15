using Xunit;
using static DistrictAI.UiTests.Walk;

namespace DistrictAI.UiTests;

/// <summary>
/// QA only (visual-qa/* branches, never main): the screens the scene walk
/// does not reach, saved for the visual and style review. The scripted
/// package started without its scene sits at the sign-in page, as a first
/// run does; a sign-in answer nobody asked for shows its error bar.
/// </summary>
public sealed class QaShotTests
{
    private static readonly TimeSpan _startTimeout = TimeSpan.FromSeconds(90);

    [Fact]
    public void TheSignInPageAndItsError()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The UI tests run on Windows only.");
        var shots = Environment.GetEnvironmentVariable("DISTRICTAI_SCREENSHOTS");
        Assert.SkipUnless(shots is { Length: > 0 }, "QA shots need DISTRICTAI_SCREENSHOTS.");
        using var app = InstalledApp.Launch();
        var window = app.MainWindow(_startTimeout);
        var handle = window.Properties.NativeWindowHandle.Value;
        _ = app.FindById("SignInButton", _startTimeout);
        _ = Wait.For(
            () => app.TryFindById("SignInTitle") is { } found && NameOf(found).Length > 0 ? found : null,
            PageTimeout,
            "the sign-in page's heading",
            app.Describe);
        Thread.Sleep(1000);
        // The window as it first opens, title bar included, then at 1920x1080.
        Save(handle, Path.Combine(shots!, "sign-in", "00-sign-in-window.png"), clientOnly: false);
        SizeClient(handle, 1920, 1080);
        Thread.Sleep(700);
        Save(handle, Path.Combine(shots!, "sign-in", "01-sign-in.png"), clientOnly: true);

        InstalledApp.OpenLink("districtai://auth?code=x&state=y");
        _ = app.FindById("SignInProblem", PageTimeout);
        Thread.Sleep(700);
        Save(handle, Path.Combine(shots!, "sign-in", "02-sign-in-error.png"), clientOnly: true);
        Assert.True(app.IsRunning, "the app ended at the sign-in page");
    }
}
