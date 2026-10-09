using DistrictAI.UiTests.Live;
using Xunit;

namespace DistrictAI.UiTests;

/// <summary>
/// The live walk: the signed-in app against the QA workspace on production,
/// on a throwaway Windows Server VM that the win-smoke harness (monorepo
/// infra/windows-smoke) makes, signs in through, and deletes. It never runs
/// in CI: it skips unless DISTRICTAI_UI_LIVE is 1, which only the harness
/// sets. Each check's result goes to live-results.json in
/// DISTRICTAI_LIVE_DIR, under the 2.0 checklist's item ids; nothing is
/// bought, sent, confirmed or called, and everything made is named
/// "win-smoke &lt;run&gt;" and removed again.
/// </summary>
/// <remarks>
/// Run as <c>DistrictAI.UiTests.exe -class DistrictAI.UiTests.LiveWalkTests -method DistrictAI.UiTests.LiveWalkTests.Walk</c>,
/// then, once the harness has installed the GitHub copy beside the Store
/// one, <c>-method DistrictAI.UiTests.LiveWalkTests.TwoCopies</c>
/// (CONTRIBUTING.md, "Live walk").
/// </remarks>
public sealed class LiveWalkTests
{
    /// <summary>
    /// The whole walk, in one order: the owner's sign-in, every area, the
    /// writes, HQ and support, billing, booking pages, rooms and themes,
    /// then the viewer's and the no-workspace login's. It fails when any
    /// check FAILed; NOT_AUTOMATED and PROBE are for the reader.
    /// </summary>
    [Fact]
    public void Walk()
    {
        RequireLive();
        using var walk = LiveWalk.Start();
        try
        {
            walk.RunAll();
        }
        finally
        {
            walk.Finish();
        }
        var failed = walk.Results.Checks.Where(check => check.Result == "FAIL").Select(check => $"{check.Id}: {check.Detail}").ToArray();
        Assert.True(failed.Length == 0, string.Join(Environment.NewLine, failed));
    }

    /// <summary>
    /// Both copies installed (the harness adds the GitHub one after Walk):
    /// each window's title names its copy, and the sign-in page shows
    /// "Install one, not both" and holds browser sign-in.
    /// </summary>
    [Fact]
    public void TwoCopies()
    {
        RequireLive();
        using var walk = LiveWalk.Start();
        try
        {
            walk.TwoCopies();
        }
        finally
        {
            walk.Finish();
        }
        var check = walk.Results.For("two-copies");
        Assert.True(check.Result != "FAIL", $"two-copies: {check.Detail}");
    }

    private static void RequireLive()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The UI tests run on Windows only.");
        Assert.SkipUnless(
            Environment.GetEnvironmentVariable("DISTRICTAI_UI_LIVE") == "1",
            "The live walk runs only on the win-smoke VM, signed in to the QA workspace (DISTRICTAI_UI_LIVE=1; CONTRIBUTING.md, \"Live walk\").");
    }
}
