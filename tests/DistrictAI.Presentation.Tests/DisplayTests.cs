using System.Collections.ObjectModel;
using System.Globalization;
using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels;
using Xunit;

namespace DistrictAI.Presentation.Tests;

public sealed class DisplayTests
{
    [Fact]
    public void EachReportAvailabilityHasItsWords()
    {
        Assert.Equal("Report", Display.ReportLabel(ReportAvailability.InApp));
        Assert.Equal("Report on the web", Display.ReportLabel(ReportAvailability.OnWeb));
        Assert.Equal(string.Empty, Display.ReportLabel(ReportAvailability.Hidden));
        // One a later core adds: no button.
        Assert.Equal(string.Empty, Display.ReportLabel((ReportAvailability)99));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a time")]
    public void NoTimeOrOneThatDoesNotParseIsEmpty(string? iso)
    {
        Assert.Equal(string.Empty, Display.When(iso, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void ATimeTodayIsTheTimeAndAnotherDayIsTheDate()
    {
        var at = new DateTimeOffset(2026, 3, 4, 15, 30, 0, TimeSpan.Zero);
        var local = at.ToLocalTime();
        var culture = CultureInfo.CurrentCulture;

        Assert.Equal(local.ToString("t", culture), Display.When("2026-03-04T15:30:00Z", at));
        Assert.Equal(local.ToString("d", culture), Display.When("2026-03-04T15:30:00Z", at.AddDays(3)));
    }

    [Fact]
    public void ATimeWithNoOffsetIsUtc()
    {
        var at = new DateTimeOffset(2026, 3, 4, 15, 30, 0, TimeSpan.Zero);
        Assert.Equal(Display.When("2026-03-04T15:30:00Z", at), Display.When("2026-03-04T15:30:00", at));
    }

    [Fact]
    public void WhenWithoutANowReadsTheClock()
    {
        var now = DateTimeOffset.UtcNow;
        var iso = now.ToString("O", CultureInfo.InvariantCulture);
        Assert.Equal(now.ToLocalTime().ToString("t", CultureInfo.CurrentCulture), Display.When(iso));
    }

    [Fact]
    public void AFailureIsItsMessageAndTheRegionsLineUnderIt()
    {
        Assert.Equal(string.Empty, Display.Failure(null));
        Assert.Equal("Nope.", Display.Failure(V.Failure("Nope.")));
        Assert.Equal("Nope.", Display.Failure(V.Failure("Nope.", regions: string.Empty)));
        Assert.Equal("Nope." + Environment.NewLine + "Affected regions: EU", Display.Failure(V.Failure("Nope.", regions: "Affected regions: EU")));
    }

    [Fact]
    public void SyncTouchesOnlyTheRowsThatChanged()
    {
        var target = new ObservableCollection<string>(["a", "b", "c"]);
        var changes = new List<string>();
        target.CollectionChanged += (_, e) => changes.Add(e.Action.ToString());

        Display.Sync(target, ["a", "B", "c", "d"]);
        Assert.Equal(["a", "B", "c", "d"], target);
        Assert.Equal(["Replace", "Add"], changes);

        changes.Clear();
        Display.Sync(target, ["a"]);
        Assert.Equal(["a"], target);
        Assert.Equal(["Remove", "Remove", "Remove"], changes);

        changes.Clear();
        Display.Sync(target, ["a"]);
        Assert.Empty(changes);
    }
}
