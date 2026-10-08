using DistrictAI.Core;
using DistrictAI.Core.Ffi;
using DistrictAI.Platform;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.AppNotifications;
using Windows.ApplicationModel.Activation;
using Windows.Storage;
using Package = Windows.ApplicationModel.Package;

namespace DistrictAI;

/// <summary>
/// The application: the core, the window, the tray icon, and every way Windows
/// activates the app. Closing the window hides it to the tray; only the tray's
/// Quit ends the app.
/// </summary>
public sealed partial class App : Application, IDisposable
{
    /// <summary>The LocalSettings key that records the hide-to-tray tip was shown.</summary>
    private const string TrayTipShownKey = "TrayTipShown";

    private readonly AppActivationArguments _launch;
    private CoreHost? _core;
    private MainWindow? _window;
    private TrayIcon? _tray;
    // Set by Quit, so the window's Closing is no longer turned into a hide.
    private bool _quitting;

    /// <summary>The app, started by <paramref name="launch"/>.</summary>
    public App(AppActivationArguments launch)
    {
        _launch = launch;
        InitializeComponent();
    }

    /// <inheritdoc/>
    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        // Before the activation is read: a toast's button, pressed while the
        // app was not running, is delivered through this registration.
        var notifications = AppNotificationManager.Default;
        notifications.NotificationInvoked += OnNotificationInvoked;
        notifications.Register();
        AppInstance.GetCurrent().Activated += OnRedirected;

        var queue = DispatcherQueue.GetForCurrentThread();
        _core = new CoreHost(new QueueDispatcher(queue), new LauncherBrowser(queue));
        _window = new MainWindow(_core);
        _window.Closed += OnClosed;
        _window.AppWindow.Closing += OnClosing;
        _core.Start(
            ApplicationData.Current.LocalFolder.Path,
            AppVersion(),
            Environment.MachineName,
            Package.Current.Id.FamilyName);
        // After Start: the icon reports the window's visibility to the core
        // from the moment it exists, hidden until the window is shown.
        _tray = new TrayIcon(
            WinRT.Interop.WindowNative.GetWindowHandle(_window),
            queue,
            Path.Combine(AppContext.BaseDirectory, "Assets", "TrayIcon.ico"),
            "District AI",
            TrayMenu);
        _tray.Selected += (_, _) => ShowWindow();
        // The one place the core hears about the window's visibility.
        _tray.VisibilityChanged += (_, visible) =>
        {
            if (!_quitting)
            {
                _core?.Send(new UiEvent.WindowVisible(visible));
            }
        };
        Handle(_launch);
        // Started by Windows at sign-in: stay in the tray until asked for.
        if (_launch.Kind != ExtendedActivationKind.StartupTask)
        {
            ShowWindow();
        }
    }

    /// <summary>The package's version, as the service sees it: major.minor.build.</summary>
    private static string AppVersion()
    {
        var version = Package.Current.Id.Version;
        return $"{version.Major}.{version.Minor}.{version.Build}";
    }

    /// <summary>An activation a second process redirected here. Arrives on a background thread.</summary>
    private void OnRedirected(object? sender, AppActivationArguments args) =>
        _window?.DispatcherQueue.TryEnqueue(() =>
        {
            Handle(args);
            if (args.Kind != ExtendedActivationKind.StartupTask)
            {
                ShowWindow();
            }
        });

    private void Handle(AppActivationArguments args)
    {
        if (args.Kind == ExtendedActivationKind.Protocol && args.Data is IProtocolActivatedEventArgs protocol)
        {
            _core?.OpenLink(protocol.Uri.AbsoluteUri);
        }
        // Launch: nothing more to do. AppNotification: this build shows no
        // notifications yet, so there is no button to act on.
    }

    private void OnNotificationInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
    {
        // This build shows no notifications yet (W4 adds message toasts, W7
        // the incoming call's), so no button can be pressed.
    }

    /// <summary>Shows the window, restored if it was minimised, and brings it forward.</summary>
    private void ShowWindow()
    {
        if (_window is null)
        {
            return;
        }
        if (_window.AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
        {
            presenter.Restore();
        }
        _window.AppWindow.Show();
        _window.Activate();
    }

    /// <summary>The tray menu, as it is when it opens.</summary>
    private IReadOnlyList<TrayMenuItem?> TrayMenu()
    {
        var items = new List<TrayMenuItem?> { new("Open District AI", ShowWindow) };
        if (_core?.Current.Shell.Phase == SessionPhase.SignedIn)
        {
            items.Add(new("Sign out", () => _core?.Send(new UiEvent.SignOut())));
        }
        items.Add(null);
        items.Add(new("Quit", Quit));
        return items;
    }

    /// <summary>The window's close button hides it to the tray, unless the app is quitting.</summary>
    private void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_quitting)
        {
            return;
        }
        args.Cancel = true;
        sender.Hide();
        var settings = ApplicationData.Current.LocalSettings.Values;
        if (settings.TryGetValue(TrayTipShownKey, out var shown) && shown is true)
        {
            return;
        }
        _tray?.ShowTip("District AI", "District AI is still running in the notification area. Quit from its icon.");
        settings[TrayTipShownKey] = true;
    }

    /// <summary>
    /// Ends the app: the core first (it saves the session within its own
    /// two-second budget), then the icon, then the window.
    /// </summary>
    private async void Quit()
    {
        if (_quitting)
        {
            return;
        }
        _quitting = true;
        try
        {
            if (_core is not null)
            {
                await _core.DisposeAsync();
            }
        }
        finally
        {
            _tray?.Dispose();
            _tray = null;
            Exit();
        }
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        AppNotificationManager.Default.Unregister();
        Dispose();
    }

    /// <summary>
    /// Stops the core. It saves the session if a refresh left it unsaved,
    /// within its own two-second budget, before the process ends.
    /// </summary>
    public void Dispose()
    {
        _core?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _core = null;
        _tray?.Dispose();
        _tray = null;
        GC.SuppressFinalize(this);
    }
}
