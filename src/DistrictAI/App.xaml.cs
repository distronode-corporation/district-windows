using System.Runtime.InteropServices;
using DistrictAI.Core;
using DistrictAI.Core.Ffi;
using DistrictAI.L10n;
using DistrictAI.Platform;
using DistrictAI.Platform.Calls;
using DistrictAI.Platform.Checkout;
using DistrictAI.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.PushNotifications;
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
    private WnsPush? _push;
    private WindowAttention? _attention;
    private CrashReporter? _crashReporter;
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
        // Crash reports first, so a crash in anything after is reported once
        // the user has said yes; until then (and always, in a build with no
        // DSN) nothing is set up (CrashReports, Platform/CrashReporter.cs).
        _crashReporter = new CrashReporter();
        var crashReports = new CrashReports(CrashReporter.BuildSettings(AppVersion()), new CrashAnswerStore(), _crashReporter);
        crashReports.Start();

        // Before the activation is read: a toast's button, pressed while the
        // app was not running, is delivered through this registration.
        var notifications = AppNotificationManager.Default;
        notifications.NotificationInvoked += OnNotificationInvoked;
        notifications.Register();
        AppInstance.GetCurrent().Activated += OnRedirected;

        var queue = DispatcherQueue.GetForCurrentThread();
        var browser = new LauncherBrowser(queue);
        // The checkout window (Platform/Checkout): the start page's answer goes
        // to the core as a link from the browser does, and its closing as
        // Billing's CheckoutClosed.
        var checkout = new CheckoutHost(
            queue,
            Path.Combine(ApplicationData.Current.TemporaryFolder.Path, "Checkout"),
            link => _core?.OpenLink(link),
            () => _core?.Send(new UiEvent.Billing(new BillingAction.CheckoutClosed())),
            browser);
        _core = new CoreHost(new QueueDispatcher(queue), browser, checkout);
        _window = new MainWindow(_core, crashReports);
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
        // Before the activation is read, as for toasts: a push that launched
        // the app is delivered through this registration.
        _push = WnsPush.Start(_core, queue);
        _core.Start(
            ApplicationData.Current.LocalFolder.Path,
            AppVersion(),
            Environment.MachineName,
            Package.Current.Id.FamilyName,
            L10n.Languages.ForCore());
        // Whether the other copy of the app is installed too, asked once: both
        // answer the browser's sign-in, so with both, sign-in is held.
        _ = CheckCopiesAsync(queue);
        // After Start: a sleep holds the machine until the core has stopped
        // this computer ringing, and a wake has it ring again.
        _power = PowerWatch.Start(_core);
        // After Start: the icon reports the window's visibility to the core
        // from the moment it exists, hidden until the window is shown.
        _tray = new TrayIcon(
            WinRT.Interop.WindowNative.GetWindowHandle(_window),
            queue,
            Path.Combine(AppContext.BaseDirectory, "Assets", "TrayIcon.ico"),
            S.BrandDistrictAi,
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
        // Started by Windows at sign-in, or by a push: stay in the tray until
        // asked for (a ring or a message shows its own toast).
        if (!launch.InBackground)
        {
            ShowWindow();
        }
    }

    /// <summary>
    /// Asks Windows which packages handle the sign-in scheme, and shows what
    /// that means on the window (OtherCopy, and the core's copies view). A
    /// failure to ask holds nothing.
    /// </summary>
    private async Task CheckCopiesAsync(DispatcherQueue queue)
    {
        var copies = await OtherCopy.CheckAsync(Package.Current.Id.FamilyName).ConfigureAwait(false);
        queue.TryEnqueue(() => _window?.ShowCopies(copies));
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
            activation = new Activation(ExtendedActivationKind.Launch, null, null, null);
        }
        _window?.DispatcherQueue.TryEnqueue(() =>
        {
            Handle(activation);
            if (!activation.InBackground)
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
        // A push that launched the app: the core rings or notifies once the
        // session is ready.
        if (activation.Push is { } push)
        {
            _core?.PushReceived(push);
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
        if (!ToastNotifier.TryReadActivation(arguments, out var id, out var actionId, out var openTarget))
        {
            return;
        }
        // The open target matters only for a toast an earlier run showed (the
        // core's table of toasts does not outlive the process); the core
        // checks it, and does nothing with one it cannot read.
        _core?.ActivateNotification(id, actionId, openTarget);
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
        // Once, the first time the window shows, in a build that can send reports.
        _window.AskCrashReports();
    }

    /// <summary>The tray menu, as it is when it opens.</summary>
    private List<TrayMenuItem?> TrayMenu()
    {
        var items = new List<TrayMenuItem?> { new(S.TrayOpen, ShowWindow) };
        if (_core?.Current.Shell.Phase == SessionPhase.SignedIn)
        {
            items.Add(new(S.TraySignOut, () => _core?.Send(new UiEvent.SignOut())));
        }
        items.Add(null);
        items.Add(new(S.TrayQuit, Quit));
        return items;
    }

    /// <summary>
    /// The window's close button hides it to the tray, unless the app is
    /// quitting. A District HQ voice session ends with it: its controls are in
    /// the window, and in the tray nothing would put them back on screen.
    /// Minimised, the window keeps the session, one click away on the taskbar.
    /// </summary>
    private void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_quitting)
        {
            return;
        }
        args.Cancel = true;
        _core?.Send(new UiEvent.Hq(new HqAction.StopVoice()));
        sender.Hide();
        var settings = ApplicationData.Current.LocalSettings.Values;
        if (settings.TryGetValue(TrayTipShownKey, out var shown) && shown is true)
        {
            return;
        }
        _tray?.ShowTip(S.BrandDistrictAi, S.TrayStillRunning);
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
            // After the core, so a crash while it stops is still reported.
            _crashReporter?.Dispose();
            _crashReporter = null;
            _ringtone?.Dispose();
            _ringtone = null;
            _tray?.Dispose();
            _tray = null;
            Exit();
        }
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _push?.Dispose();
        _push = null;
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
        _push?.Dispose();
        _push = null;
        _power?.Dispose();
        _power = null;
        _core?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _core = null;
        _crashReporter?.Dispose();
        _crashReporter = null;
        _ringtone?.Dispose();
        _ringtone = null;
        _tray?.Dispose();
        _tray = null;
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// What an activation asks for, copied out of its <see cref="AppActivationArguments"/>:
    /// its kind, the link it opens, a pressed toast's arguments, and a push's payload.
    /// </summary>
    private sealed record Activation(ExtendedActivationKind Kind, string? Link, IDictionary<string, string>? Notification, string? Push)
    {
        /// <summary>Whether the app was started without being asked for its window: at sign-in, or by a push.</summary>
        public bool InBackground => Kind is ExtendedActivationKind.StartupTask or ExtendedActivationKind.Push;

        /// <summary>Reads everything an activation carries, at once.</summary>
        public static Activation Read(AppActivationArguments args)
        {
            var kind = args.Kind;
            return kind switch
            {
                ExtendedActivationKind.Protocol when args.Data is IProtocolActivatedEventArgs protocol =>
                    new Activation(kind, protocol.Uri.AbsoluteUri, null, null),
                ExtendedActivationKind.AppNotification when args.Data is AppNotificationActivatedEventArgs notification =>
                    new Activation(kind, null, new Dictionary<string, string>(notification.Arguments), null),
                ExtendedActivationKind.Push when args.Data is PushNotificationReceivedEventArgs push =>
                    new Activation(kind, null, null, WnsPush.Payload(push.Payload)),
                _ => new Activation(kind, null, null, null),
            };
        }
    }
}
