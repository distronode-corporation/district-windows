using DistrictAI.Core;
using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels.Billing;
using Microsoft.UI.Dispatching;
using Microsoft.Web.WebView2.Core;

namespace DistrictAI.Platform.Checkout;

/// <summary>
/// The checkout window: the service's checkout and billing pages, run by
/// Stripe, in a WebView2 of their own, for the core's
/// <c>UrlOpener::open_embedded</c> (<see cref="IEmbeddedBrowser"/>).
/// </summary>
/// <remarks>
/// <para>
/// Each <see cref="EmbeddedViewKind.NewPrivate"/> opens a new window with a
/// new WebView2 environment over a fresh, empty user data folder of its own,
/// and closes any earlier one. Nothing is shared with an earlier checkout or
/// with the user's browser, and the folder is deleted once the window's
/// browser process has exited (and any a crash left behind, the next time a
/// checkout opens). A fresh folder rather than an InPrivate profile because
/// it is the stronger promise on every WebView2 runtime: InPrivate views of
/// one environment share a session while any of them lives, and a folder of
/// its own per checkout shares nothing with anything, by construction. That
/// is what "sign in every time" means here: each purchase redeems a fresh
/// one-time link in a browser profile that starts empty and is then thrown
/// away. <see cref="EmbeddedViewKind.Same"/> navigates the window open now.
/// </para>
/// <para>
/// Every top-level navigation, and every window a page asks to open, goes
/// through <see cref="CheckoutPolicy"/>: the service's own origin and
/// Stripe's hosts show here; the start page's <c>districtai://handoff</c>
/// answer is cancelled and handed to the core, as Windows hands over a link
/// from the browser; any other web page opens in the user's browser; anything
/// else is refused. Closing the window, with its button or Escape, tells the
/// core (<see cref="BillingAction.CheckoutClosed"/>).
/// </para>
/// </remarks>
internal sealed class CheckoutHost : IEmbeddedBrowser
{
    /// <summary>The window's title, which a screen reader reads.</summary>
    internal const string WindowTitle = "District AI checkout";

    private readonly DispatcherQueue _queue;
    private readonly Action<string> _handOff;
    private readonly Action _closed;
    private readonly IBrowser _browser;
    private readonly string _root;
    private CheckoutWindow? _open;

    /// <summary>
    /// A host that runs on <paramref name="queue"/> (the UI thread), keeps its
    /// profiles under <paramref name="root"/>, hands the start page's answer to
    /// <paramref name="handOff"/>, says the window closed through
    /// <paramref name="closed"/>, and opens other pages with
    /// <paramref name="browser"/>.
    /// </summary>
    internal CheckoutHost(DispatcherQueue queue, string root, Action<string> handOff, Action closed, IBrowser browser)
    {
        _queue = queue;
        _root = root;
        _handOff = handOff;
        _closed = closed;
        _browser = browser;
    }

    /// <summary>Whether this computer has a WebView2 runtime to show a page with.</summary>
    internal static bool RuntimeAvailable()
    {
        try
        {
            return !string.IsNullOrEmpty(CoreWebView2Environment.GetAvailableBrowserVersionString());
        }
        catch (WebView2RuntimeNotFoundException)
        {
            return false;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return false;
        }
    }

    /// <inheritdoc/>
    public Task<bool> OpenAsync(string url, EmbeddedViewKind view)
    {
        // The core asks from its own threads; windows are the UI thread's.
        var opened = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var queued = _queue.TryEnqueue(async () =>
        {
            try
            {
                opened.SetResult(await OpenOnUiThreadAsync(url, view));
            }
            catch (Exception error) when (error is System.Runtime.InteropServices.COMException or InvalidOperationException or ArgumentException or UnauthorizedAccessException or IOException)
            {
                opened.SetResult(false);
            }
        });
        return queued ? opened.Task : Task.FromResult(false);
    }

    private async Task<bool> OpenOnUiThreadAsync(string url, EmbeddedViewKind view)
    {
        if (view == EmbeddedViewKind.Same)
        {
            return _open is { } surface && surface.Navigate(url);
        }
        if (!RuntimeAvailable())
        {
            return false;
        }
        // The earlier window goes without a word to the core: the core has
        // already moved on to this checkout, and its close would give it up.
        _open?.CloseQuietly();
        _open = null;
        SweepLeftovers();
        var surface = new CheckoutWindow(new CheckoutPolicy(url), Path.Combine(_root, Guid.NewGuid().ToString("N")), _handOff, _browser);
        surface.ClosedByUser += (_, _) =>
        {
            if (ReferenceEquals(_open, surface))
            {
                _open = null;
                _closed();
            }
        };
        if (!await surface.StartAsync(url))
        {
            surface.CloseQuietly();
            return false;
        }
        _open = surface;
        return true;
    }

    /// <summary>Deletes profile folders a window that is gone left behind.</summary>
    private void SweepLeftovers()
    {
        if (!Directory.Exists(_root))
        {
            return;
        }
        foreach (var folder in Directory.EnumerateDirectories(_root))
        {
            CheckoutWindow.TryDelete(folder);
        }
    }
}
