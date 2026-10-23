using System.Collections.ObjectModel;
using System.Globalization;
using DistrictAI.Core.Ffi;
using DistrictAI.L10n;
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
    public void FrenchWritesTheTimeAndTheDateAsTheCoreDoesWhateverTheRegionalFormat()
    {
        Assert.Equal("15 h 04", Display.Time(new DateTimeOffset(2026, 3, 4, 15, 4, 0, TimeSpan.Zero), S.Locale.FrCa));
        Assert.Equal("9 h", Display.Time(new DateTimeOffset(2026, 3, 4, 9, 0, 0, TimeSpan.Zero), S.Locale.FrCa));
        Assert.Equal("0 h 30", Display.Time(new DateTimeOffset(2026, 3, 4, 0, 30, 0, TimeSpan.Zero), S.Locale.FrCa));
        Assert.Equal("2026-03-04", Display.Date(new DateTimeOffset(2026, 3, 4, 15, 4, 0, TimeSpan.Zero), S.Locale.FrCa));

        var at = new DateTimeOffset(2026, 3, 4, 15, 30, 0, TimeSpan.Zero);
        var local = at.ToLocalTime();
        Assert.Equal(Display.Time(local, S.Locale.FrCa), Display.When("2026-03-04T15:30:00Z", at, S.Locale.FrCa));
        Assert.Equal(Display.Date(local, S.Locale.FrCa), Display.When("2026-03-04T15:30:00Z", at.AddDays(3), S.Locale.FrCa));
        Assert.DoesNotContain("M", Display.When("2026-03-04T15:30:00Z", at, S.Locale.FrCa), StringComparison.Ordinal);
    }

    // The house French clock (district-l10n's format::time and the contract's
    // format-vectors.json): the hour, a plain U+0020, "h", and the minutes only
    // when there are some, whatever the regional format.
    [Theory]
    [InlineData(9, 0, "9 h")]
    [InlineData(9, 30, "9 h 30")]
    [InlineData(17, 0, "17 h")]
    [InlineData(0, 15, "0 h 15")]
    public void FrenchWritesTheHouseClock(int hour, int minute, string expected)
    {
        var text = Display.Time(new DateTimeOffset(2026, 3, 4, hour, minute, 0, TimeSpan.Zero), S.Locale.FrCa);
        Assert.Equal(expected, text);
        Assert.DoesNotContain(":", text, StringComparison.Ordinal);
        Assert.DoesNotContain(" ", text, StringComparison.Ordinal);
        Assert.DoesNotContain(" ", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("en-CA")]
    [InlineData("en-GB")]
    public void EnglishTimesAreUnchangedInEachCulture(string name)
    {
        var before = CultureInfo.CurrentCulture;
        try
        {
            var culture = CultureInfo.GetCultureInfo(name);
            CultureInfo.CurrentCulture = culture;
            foreach (var (hour, minute) in new[] { (9, 0), (9, 30), (17, 0) })
            {
                var local = new DateTimeOffset(2026, 3, 4, hour, minute, 0, TimeSpan.Zero);
                var text = Display.Time(local, S.Locale.En);
                Assert.Equal(local.ToString("t", culture), text);
                Assert.DoesNotContain(" h", text, StringComparison.Ordinal);
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    [Fact]
    public void EnglishKeepsTheUsersCulture()
    {
        var local = new DateTimeOffset(2026, 3, 4, 15, 4, 0, TimeSpan.Zero);
        Assert.Equal(local.ToString("t", CultureInfo.CurrentCulture), Display.Time(local, S.Locale.En));
        Assert.Equal(local.ToString("d", CultureInfo.CurrentCulture), Display.Date(local, S.Locale.En));
        Assert.Equal(1500L.ToString("N0", CultureInfo.CurrentCulture), Display.Integer(1500, S.Locale.En));
    }

    [Theory]
    [InlineData(0, "0")]
    [InlineData(999, "999")]
    [InlineData(1500, "1\u00A0500")]
    [InlineData(1234567, "1\u00A0234\u00A0567")]
    [InlineData(-1500, "-1\u00A0500")]
    public void FrenchGroupsANumberWithANoBreakSpace(long value, string expected)
    {
        Assert.Equal(expected, Display.Integer(value, S.Locale.FrCa));
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
