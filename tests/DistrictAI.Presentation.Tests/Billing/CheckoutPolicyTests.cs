using DistrictAI.ViewModels.Billing;
using Xunit;

namespace DistrictAI.Presentation.Tests.Billing;

public sealed class CheckoutPolicyTests
{
    private static readonly CheckoutPolicy _policy = new("https://www.distronode.com/dashboard/handoff/start?state=abc");

    [Theory]
    [InlineData("https://www.distronode.com/checkout?tier=VoicePro&term=monthly")]
    [InlineData("https://WWW.DISTRONODE.COM/dashboard/district/billing")]
    [InlineData("https://checkout.stripe.com/c/pay/cs_test_1")]
    [InlineData("https://js.stripe.com/v3/")]
    [InlineData("https://b.js.stripe.com/")]
    [InlineData("https://hooks.stripe.com/3d_secure_2/hosted")]
    [InlineData("https://m.stripe.network/inner.html")]
    [InlineData("https://checkout.link.com/")]
    [InlineData("https://link.com/")]
    [InlineData("about:blank")]
    public void TheServiceAndStripeShowInTheWindow(string url) =>
        Assert.Equal(CheckoutNavigation.Allow, _policy.Classify(url));

    [Theory]
    [InlineData("districtai://handoff?state=s&nonce=n")]
    [InlineData("DISTRICTAI://HANDOFF?state=s&nonce=n")]
    public void TheStartPagesAnswerGoesToTheCore(string url) =>
        Assert.Equal(CheckoutNavigation.HandOff, _policy.Classify(url));

    [Theory]
    [InlineData("https://evil.example/")]
    [InlineData("https://www.distronode.com.example/checkout")]
    [InlineData("https://evilstripe.com/")]
    [InlineData("https://stripe.com.evil.example/")]
    // User info before the host: written in two parts so it reads as no address.
    [InlineData("https://stripe.com" + "@" + "evil.example/")]
    [InlineData("https://www.distronode.com:8443/checkout")]
    [InlineData("http://www.distronode.com/checkout")]
    [InlineData("http://checkout.stripe.com/")]
    [InlineData("https://www.google.com/")]
    public void EverywhereElseOpensInTheBrowser(string url) =>
        Assert.Equal(CheckoutNavigation.OpenInBrowser, _policy.Classify(url));

    [Theory]
    [InlineData("districtai://auth?code=c&state=s")]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/Windows/win.ini")]
    [InlineData("data:text/html,hi")]
    [InlineData("ms-settings:privacy")]
    [InlineData("not a url")]
    [InlineData("")]
    [InlineData(null)]
    public void AnythingElseIsRefused(string? url) =>
        Assert.Equal(CheckoutNavigation.Block, _policy.Classify(url));

    [Theory]
    [InlineData("http://www.distronode.com/")]
    [InlineData("districtai://handoff")]
    [InlineData("nonsense")]
    public void ThePolicyNeedsAnHttpsOrigin(string firstPage) =>
        Assert.Throws<ArgumentException>(() => new CheckoutPolicy(firstPage));
}
