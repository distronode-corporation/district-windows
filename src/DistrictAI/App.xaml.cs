using DistrictAI.Core;
using DistrictAI.Core.Ffi;
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
public sealed partial class App : Application, IDisposable
{
    private readonly AppActivationArguments _launch;
    private CoreHost? _core;
    private MainWindow? _window;
    private string? _otherCopy;

    /// <summary>The app, started by <paramref name="launch"/>.</summary>
    public App(AppActivationArguments launch)
    {
        _launch = launch;
        InitializeComponent();
#if DISTRICT_SPIKES
        UnhandledException += (_, args) =>
            Spikes.SpikeLog.Write($"unhandled {args.Exception.GetType().Name} 0x{args.Exception.HResult:X8} {args.Message.ReplaceLineEndings(" ")}");
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Spikes.SpikeLog.Write($"unhandled-domain {args.ExceptionObject}".ReplaceLineEndings(" "));
#endif
    }

    /// <inheritdoc/>
    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        // Before the activation is read: a toast's button, pressed while the
        // app was not running, is delivered through this registration.
        var notifications = AppNotificationManager.Default;
        notifications.NotificationInvoked += OnNotificationInvoked;
#if DISTRICT_SPIKES
        try
        {
            notifications.Register();
            Spikes.SpikeLog.Write($"notifications-registered supported={AppNotificationManager.IsSupported()} setting={notifications.Setting}");
        }
        catch (Exception error)
        {
            Spikes.SpikeLog.Write($"notifications-register-failed {error.GetType().Name} 0x{error.HResult:X8} {error.Message.ReplaceLineEndings(" ")}");
        }
#else
        notifications.Register();
#endif
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
        _ = LookForOtherCopyAsync();
    }

    /// <summary>
    /// With the other copy of the app installed, says so and offers no
    /// browser sign-in (its answer could land in the other copy).
    /// </summary>
    private async Task LookForOtherCopyAsync()
    {
        _otherCopy = await OtherCopy.FindAsync();
#if DISTRICT_SPIKES
        Spikes.SpikeLog.Write($"other-copy found={_otherCopy ?? "none"} credentials={Package.Current.Id.FamilyName}");
#endif
        if (_otherCopy is not null)
        {
            _window?.BlockSignIn(OtherCopy.Notice(_otherCopy));
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
            _window.Activate();
        });

    private void Handle(AppActivationArguments args)
    {
        if (args.Kind == ExtendedActivationKind.Protocol && args.Data is IProtocolActivatedEventArgs protocol)
        {
            OpenLink(protocol.Uri.AbsoluteUri);
        }
#if DISTRICT_SPIKES
        if (args.Kind == ExtendedActivationKind.AppNotification && args.Data is AppNotificationActivatedEventArgs toast)
        {
            Spikes.SpikeLog.Write($"toast-activated cold=true args={Spikes.IncomingCallSpike.Describe(toast)}");
        }
#endif
        // Launch: nothing more to do. AppNotification: this build shows no
        // notifications yet, so there is no button to act on.
    }

    private void OpenLink(string uri)
    {
        var kind = DistrictFfi.LinkKind(uri);
        // A sign-in's answer while the other copy is installed and this one
        // started no sign-in: it belongs to the other copy, which holds the
        // attempt's verifier. Dropped, with a notice.
        if (kind == LinkKind.Auth && _otherCopy is not null
            && _core?.Current.Shell.Phase != SessionPhase.SigningIn)
        {
            _window?.ShowNotice(OtherCopy.WrongCopy);
#if DISTRICT_SPIKES
            Spikes.SpikeLog.Write("received link=Auth dropped=wrong-copy");
#endif
            return;
        }
        _core?.OpenLink(uri);
#if DISTRICT_SPIKES
        Spikes.SpikeLog.Write($"received link={kind} host={new Uri(uri).Host}");
        _ = SpikeLinkAsync(uri, kind);
#endif
    }

#if DISTRICT_SPIKES
    private async Task SpikeLinkAsync(string uri, LinkKind kind)
    {
        try
        {
            await SpikeLinkCoreAsync(uri, kind);
        }
        catch (Exception error)
        {
            Spikes.SpikeLog.Write($"spike-failed link={new Uri(uri).Host} {error.GetType().Name} 0x{error.HResult:X8} {error.Message.ReplaceLineEndings(" ")}");
        }
    }

    private async Task SpikeLinkCoreAsync(string uri, LinkKind kind)
    {
        var host = new Uri(uri).Host;
        if (kind == LinkKind.Unknown && host == "spike-toast")
        {
            await Spikes.IncomingCallSpike.ShowAsync();
        }
        else if (kind == LinkKind.Unknown && host == "spike-checkout")
        {
            await Spikes.CheckoutSpike.RunAsync();
        }
        else if (kind == LinkKind.Auth && _core is not null)
        {
            // What the core made of the answer: with no sign-in under way, the
            // sign-in page's error. The exchange itself needs the service.
            await Task.Delay(500);
            var screen = _core.Current.Screen as ScreenView.Session;
            Spikes.SpikeLog.Write($"auth-handled phase={_core.Current.Shell.Phase} error={screen?.View.Error ?? "none"}");
        }
    }
#endif

    private void OnNotificationInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
    {
#if DISTRICT_SPIKES
        Spikes.SpikeLog.Write($"toast-activated cold=false args={Spikes.IncomingCallSpike.Describe(args)}");
#endif
        // This build shows no notifications yet (W4 adds message toasts, W7
        // the incoming call's), so no button can be pressed.
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
        GC.SuppressFinalize(this);
    }
}
