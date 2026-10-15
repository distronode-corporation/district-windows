using DistrictAI.L10n;
using Xunit;

namespace DistrictAI.Presentation.Tests.L10n;

/// <summary>
/// The strings generated from src/DistrictAI/l10n/ui.toml: English until the
/// app says otherwise, and the English is the words the app showed before the
/// catalog. Nothing here changes <see cref="S.Current"/>, which every test shares.
/// </summary>
public sealed class StringsTests
{
    [Fact]
    public void EnglishIsTheDefault()
    {
        Assert.Equal(S.Locale.En, S.Current);
    }

    /// <summary>Every entry of the catalog has its French (3.0), so no French branch carries English.</summary>
    [Fact]
    public void FrenchIsComplete()
    {
        Assert.True(S.FrComplete);
    }

    [Fact]
    public void KeysReadTheirEnglish()
    {
        Assert.Equal("District AI", S.BrandDistrictAi);
        Assert.Equal("Cancel", S.CommonCancel);
        Assert.Equal("Sign in with your browser", S.SignInButton);
        Assert.Equal("Mute (Ctrl+D)", S.CallBarMuteTooltip);
    }

    [Fact]
    public void ArgumentsFillTheirPlaces()
    {
        Assert.Equal("Type at least 3 characters to search.", S.InboxMinQuery(3));
        Assert.Equal("Pay (District AI checkout)", S.CheckoutWindowTitlePage("Pay"));
        Assert.Equal("Ada, this device, Windows", S.DevicesRowThisDevice("Ada", "Windows"));
    }
}
