namespace DistrictAI.ViewModels.Billing;

/// <summary>What the checkout window does with a page it is asked to show.</summary>
public enum CheckoutNavigation
{
    /// <summary>Show it in the window: the service's own origin, or Stripe's.</summary>
    Allow,

    /// <summary>
    /// The start page's answer (<c>districtai://handoff</c>): cancelled in the
    /// window and handed to the core, as a link from the browser is.
    /// </summary>
    HandOff,

    /// <summary>A web page anywhere else: cancelled, and opened in the user's browser.</summary>
    OpenInBrowser,

    /// <summary>Anything else (another scheme, another <c>districtai:</c> link): cancelled.</summary>
    Block,
}

/// <summary>
/// The checkout window's allowlist, for its top-level navigations and the
/// windows its pages ask to open: the service's origin (the one the core's
/// first page is on), and Stripe's own hosts, which Checkout, Stripe.js and
/// Elements, 3-D Secure (hooks.stripe.com) and Link need at the top level
/// (Stripe's integration security guide, "Content Security Policy":
/// checkout.stripe.com, js.stripe.com and *.js.stripe.com, hooks.stripe.com,
/// *.stripe.com, m.stripe.network, link.com and *.link.com). A card issuer's
/// own 3-D Secure page loads in a frame inside Stripe's, which this does not
/// police: frames are the pages' own business, and the window shows only what
/// those pages chose to embed.
/// </summary>
public sealed class CheckoutPolicy
{
    /// <summary>Stripe's domains: each, and any host below it, over HTTPS.</summary>
    public static readonly IReadOnlyList<string> StripeDomains = ["stripe.com", "stripe.network", "link.com"];

    private readonly Uri _origin;

    /// <summary>A policy for the service at the origin of <paramref name="firstPage"/>, which must be HTTPS.</summary>
    /// <exception cref="ArgumentException">The page is not an absolute HTTPS address.</exception>
    public CheckoutPolicy(string firstPage)
    {
        if (!Uri.TryCreate(firstPage, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("the service's pages are HTTPS", nameof(firstPage));
        }
        _origin = new Uri(uri.GetLeftPart(UriPartial.Authority));
    }

    /// <summary>What to do with a navigation to <paramref name="url"/>.</summary>
    public CheckoutNavigation Classify(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return CheckoutNavigation.Block;
        }
        if (string.Equals(uri.Scheme, "districtai", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(uri.Host, "handoff", StringComparison.OrdinalIgnoreCase)
                ? CheckoutNavigation.HandOff
                : CheckoutNavigation.Block;
        }
        if (uri.Scheme == "about" && uri.AbsolutePath == "blank")
        {
            return CheckoutNavigation.Allow;
        }
        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
        {
            return CheckoutNavigation.Block;
        }
        if (uri.Scheme == Uri.UriSchemeHttps && uri.UserInfo.Length == 0 && (OnOrigin(uri) || OnStripe(uri.IdnHost)))
        {
            return CheckoutNavigation.Allow;
        }
        return CheckoutNavigation.OpenInBrowser;
    }

    private bool OnOrigin(Uri uri) =>
        string.Equals(uri.IdnHost, _origin.IdnHost, StringComparison.OrdinalIgnoreCase) && uri.Port == _origin.Port;

    private static bool OnStripe(string host) =>
        StripeDomains.Any(domain =>
            string.Equals(host, domain, StringComparison.OrdinalIgnoreCase)
            || host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase));
}
