using DistrictAI.Core;
using DistrictAI.Platform;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.AppNotifications;
using Windows.ApplicationModel.Activation;
using Windows.Storage;
using Package = Windows.ApplicationModel.Package;

namespace DistrictAI;

/// <summary>The application: the core, the window, and every way Windows activates the app.</summary>
public partial class App : Application
{
    private readonly AppActivationArguments _launch;
    private CoreHost? _core;
    private MainWindow? _window;

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
        _core.Start(
            ApplicationData.Current.LocalFolder.Path,
            AppVersion(),
            Environment.MachineName,
            Package.Current.Id.FamilyName);
        Handle(_launch);
        _window.Activate();
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
            _window.Activate();
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

    private void OnClosed(object sender, WindowEventArgs args)
    {
        AppNotificationManager.Default.Unregister();
        // The core saves the session if a refresh left it unsaved, within its
        // own two-second budget, before the process ends.
        _core?.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
