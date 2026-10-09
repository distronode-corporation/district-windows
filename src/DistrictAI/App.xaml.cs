using System.Runtime.InteropServices;
using DistrictAI.Core;
using DistrictAI.Core.Ffi;
using DistrictAI.Platform;
using DistrictAI.Platform.Calls;
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
    private PowerWatch? _power;
    private RingtonePlayer? _ringtone;
    private WindowAttention? _attention;
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
        // Before Start: the core may ring, notify or ask for the window as soon
        // as it runs. Each event arrives on the UI thread.
        _ringtone = new RingtonePlayer();
        _attention = new WindowAttention(_window);
        _core.NotificationRequested += (_, notification) => ToastNotifier.Show(notification);
        _core.NotificationWithdrawn += (_, id) => _ = ToastNotifier.WithdrawAsync(id);
        _core.RingtoneStarted += (_, _) => _ringtone?.Start();
        _core.RingtoneStopped += (_, _) => _ringtone?.Stop();
        _core.PresentWindowRequested += (_, _) => _attention?.Present();
        _core.Start(
            ApplicationData.Current.LocalFolder.Path,
            AppVersion(),
            Environment.MachineName,
            Package.Current.Id.FamilyName);
        // After Start: a sleep holds the machine until the core has stopped
        // this computer ringing, and a wake has it ring again.
        _power = PowerWatch.Start(_core);
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
        var launch = Activation.Read(_launch);
        Handle(launch);
        // Started by Windows at sign-in: stay in the tray until asked for.
        if (launch.Kind != ExtendedActivationKind.StartupTask)
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

    /// <summary>
    /// An activation a second process redirected here. Arrives on a background
    /// thread while that process waits for the redirection to be delivered, and
    /// its arguments are that process's objects: they are read here, before it
    /// exits, and only the values go to the UI thread. Read later, from the
    /// queued callback, they failed with RPC_S_CALL_FAILED once it had gone,
    /// and the exception ended this app (a link from the browser's sign-in, for
    /// one).
    /// </summary>
    private void OnRedirected(object? sender, AppActivationArguments args)
    {
        Activation activation;
        try
        {
            activation = Activation.Read(args);
        }
        catch (COMException)
        {
            // It went already. What it carried is lost (a sign-in waits for the
            // next link), but the window is still what was asked for.
            activation = new Activation(ExtendedActivationKind.Launch, null, null);
        }
        _window?.DispatcherQueue.TryEnqueue(() =>
        {
            Handle(activation);
            if (activation.Kind != ExtendedActivationKind.StartupTask)
            {
                ShowWindow();
            }
        });
    }

    private void Handle(Activation activation)
    {
        if (activation.Link is { } link)
        {
            _core?.OpenLink(link);
        }
        // A toast pressed while the app was not running.
        if (activation.Notification is { } notification)
        {
            ActivateNotification(notification);
        }
        // Launch: nothing more to do.
    }

    /// <summary>
    /// A toast pressed while the app runs. Arrives on a background thread, and
    /// like a redirection its arguments are read before the UI thread runs.
    /// </summary>
    private void OnNotificationInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
    {
        var arguments = new Dictionary<string, string>(args.Arguments);
        _window?.DispatcherQueue.TryEnqueue(() => ActivateNotification(arguments));
    }

    /// <summary>
    /// Hands a toast's press to the core, which knows what the notification was
    /// for. The toast's body opens the window as well; a button (Answer,
    /// Decline) leaves that to the core, which asks for the window when the
    /// press needs it.
    /// </summary>
    private void ActivateNotification(IDictionary<string, string> arguments)
    {
        if (!ToastNotifier.TryReadActivation(arguments, out var id, out var actionId))
        {
            return;
        }
        _core?.ActivateNotification(id, actionId);
        if (actionId is null)
        {
            ShowWindow();
        }
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
    private List<TrayMenuItem?> TrayMenu()
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
    /// Ends the app: the sleep and wake notifications first, so none reaches a
    /// core that is stopping, then the core (it saves the session within its
    /// own two-second budget), then the icon, then the window.
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
            _power?.Dispose();
            _power = null;
            if (_core is not null)
            {
                await _core.DisposeAsync();
            }
        }
        finally
        {
            _ringtone?.Dispose();
            _ringtone = null;
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
    /// Stops the sleep and wake notifications, then the core. It saves the
    /// session if a refresh left it unsaved, within its own two-second budget,
    /// before the process ends.
    /// </summary>
    public void Dispose()
    {
        _power?.Dispose();
        _power = null;
        _core?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _core = null;
        _ringtone?.Dispose();
        _ringtone = null;
        _tray?.Dispose();
        _tray = null;
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// What an activation asks for, copied out of its <see cref="AppActivationArguments"/>:
    /// its kind, the link it opens, and a pressed toast's arguments.
    /// </summary>
    private sealed record Activation(ExtendedActivationKind Kind, string? Link, IDictionary<string, string>? Notification)
    {
        /// <summary>Reads everything an activation carries, at once.</summary>
        public static Activation Read(AppActivationArguments args)
        {
            var kind = args.Kind;
            return kind switch
            {
                ExtendedActivationKind.Protocol when args.Data is IProtocolActivatedEventArgs protocol =>
                    new Activation(kind, protocol.Uri.AbsoluteUri, null),
                ExtendedActivationKind.AppNotification when args.Data is AppNotificationActivatedEventArgs notification =>
                    new Activation(kind, null, new Dictionary<string, string>(notification.Arguments)),
                _ => new Activation(kind, null, null),
            };
        }
    }
}
