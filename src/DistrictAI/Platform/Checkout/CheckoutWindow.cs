using DistrictAI.Core;
using DistrictAI.ViewModels.Billing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.Web.WebView2.Core;
using Windows.Graphics;
using Windows.System;

namespace DistrictAI.Platform.Checkout;

/// <summary>One checkout window and its throwaway WebView2 profile.</summary>
internal sealed class CheckoutWindow
{
    private readonly CheckoutPolicy _policy;
    private readonly string _profile;
    private readonly Action<string> _handOff;
    private readonly IBrowser _browser;
    private readonly Window _window;
    private readonly WebView2 _view;
    private CoreWebView2Environment? _environment;
    private bool _quiet;

    internal CheckoutWindow(CheckoutPolicy policy, string profile, Action<string> handOff, IBrowser browser)
    {
        _policy = policy;
        _profile = profile;
        _handOff = handOff;
        _browser = browser;
        _window = new Window { Title = CheckoutHost.WindowTitle };
        _view = new WebView2();
        AutomationProperties.SetName(_view, "Checkout");
        // Closing from the keyboard: Alt+F4 as any window, Ctrl+W while the
        // window's own controls have focus, and the Close button, which Tab
        // reaches. Not Escape: a checkout page uses it to close its own menus.
        var closeKey = new KeyboardAccelerator { Key = VirtualKey.W, Modifiers = VirtualKeyModifiers.Control };
        closeKey.Invoked += (_, args) =>
        {
            args.Handled = true;
            _window.Close();
        };
        var close = new Button { Content = "Close", HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(8) };
        AutomationProperties.SetName(close, "Close checkout");
        close.Click += (_, _) => _window.Close();
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.KeyboardAccelerators.Add(closeKey);
        root.Children.Add(close);
        Grid.SetRow(_view, 1);
        root.Children.Add(_view);
        _window.Content = root;
        _window.AppWindow.Resize(new SizeInt32(1024, 900));
        _window.Closed += OnClosed;
    }

    /// <summary>Raised when the member closed the window (not when it was replaced).</summary>
    internal event EventHandler? ClosedByUser;

    /// <summary>Starts the view on a fresh profile and opens <paramref name="url"/>; false when it could not.</summary>
    internal async Task<bool> StartAsync(string url)
    {
        Directory.CreateDirectory(_profile);
        var options = new CoreWebView2EnvironmentOptions();
        _environment = await CoreWebView2Environment.CreateWithOptionsAsync(string.Empty, _profile, options);
        _environment.BrowserProcessExited += (_, _) => TryDelete(_profile);
        await _view.EnsureCoreWebView2Async(_environment);
        var core = _view.CoreWebView2;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.AreHostObjectsAllowed = false;
        core.Settings.IsWebMessageEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsPasswordAutosaveEnabled = false;
        core.Settings.IsGeneralAutofillEnabled = false;
        core.NavigationStarting += OnNavigationStarting;
        core.NewWindowRequested += OnNewWindowRequested;
        core.LaunchingExternalUriScheme += OnLaunchingExternalUriScheme;
        core.DocumentTitleChanged += (_, _) =>
            _window.Title = string.IsNullOrWhiteSpace(core.DocumentTitle)
                ? CheckoutHost.WindowTitle
                : $"{core.DocumentTitle} ({CheckoutHost.WindowTitle})";
        if (!Navigate(url))
        {
            return false;
        }
        _window.Activate();
        return true;
    }

    /// <summary>Opens <paramref name="url"/> in this window, if the policy lets it show here.</summary>
    internal bool Navigate(string url)
    {
        if (_view.CoreWebView2 is not { } core || _policy.Classify(url) != CheckoutNavigation.Allow)
        {
            return false;
        }
        core.Navigate(url);
        return true;
    }

    /// <summary>Closes the window without telling the core: it has moved on.</summary>
    internal void CloseQuietly()
    {
        _quiet = true;
        _window.Close();
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _view.Close();
        if (!_quiet)
        {
            ClosedByUser?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnNavigationStarting(CoreWebView2 sender, CoreWebView2NavigationStartingEventArgs args)
    {
        if (!Decide(args.Uri))
        {
            args.Cancel = true;
        }
    }

    private void OnNewWindowRequested(CoreWebView2 sender, CoreWebView2NewWindowRequestedEventArgs args)
    {
        // No second window: an allowed page opens here, anything else is decided as a navigation.
        args.Handled = true;
        if (Decide(args.Uri))
        {
            sender.Navigate(args.Uri);
        }
    }

    private void OnLaunchingExternalUriScheme(CoreWebView2 sender, CoreWebView2LaunchingExternalUriSchemeEventArgs args)
    {
        // Never Windows' own protocol handling: the answer is the core's, the rest refused.
        args.Cancel = true;
        _ = Decide(args.Uri);
    }

    /// <summary>Acts on a navigation to <paramref name="uri"/>; true when it may go ahead in this window.</summary>
    private bool Decide(string uri)
    {
        switch (_policy.Classify(uri))
        {
            case CheckoutNavigation.Allow:
                return true;
            case CheckoutNavigation.HandOff:
                _handOff(uri);
                return false;
            case CheckoutNavigation.OpenInBrowser:
                _ = _browser.OpenAsync(uri);
                return false;
            default:
                return false;
        }
    }

    /// <summary>Deletes <paramref name="folder"/>, if nothing holds it any more.</summary>
    internal static void TryDelete(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Still held (its browser process has not exited yet): the next
            // checkout sweeps it.
        }
    }
}
