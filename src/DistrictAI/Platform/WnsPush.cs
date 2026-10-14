using System.Diagnostics;
using System.Text;
using DistrictAI.Core;
using DistrictAI.Core.Ffi;
using Microsoft.UI.Dispatching;
using Microsoft.Windows.PushNotifications;

namespace DistrictAI.Platform;

/// <summary>
/// Windows push (WNS), for ringing and messages while the app is closed
/// (crates/district-ffi/src/push.rs). Registered at start, so a push can
/// launch the app; once someone is signed in, the channel is asked for and
/// its URI registered with the service, and asked for again before it
/// expires. A push is ids only, handed to the core, which rings or notifies.
/// <para>
/// Every failure here is logged and costs nothing else: until Microsoft maps
/// the package to the WNS app, asking for a channel fails, and the app works
/// exactly as it does without push. The core's sign-out unregisters the
/// channel with the service; this unregisters it with Windows.
/// </para>
/// </summary>
internal sealed partial class WnsPush : IDisposable
{
    /// <summary>
    /// The remote id <c>CreateChannelAsync</c> takes: the WNS app's service
    /// principal ObjectId (Enterprise applications), not the app registration's
    /// object id, which WNS refuses.
    /// </summary>
    internal static readonly Guid RemoteId = new("7ff2db5f-da52-4621-babb-ed927c19892a");

    /// <summary>How long before the channel's expiry a new one is asked for. Channels last about 30 days.</summary>
    private static readonly TimeSpan RenewAhead = TimeSpan.FromDays(1);

    /// <summary>How soon a failed channel request is tried again.</summary>
    private static readonly TimeSpan RetryAfter = TimeSpan.FromHours(1);

    private readonly CoreHost _core;
    private readonly DispatcherQueueTimer _renewal;
    private bool _registered;
    private bool _signedIn;
    private bool _asking;

    private WnsPush(CoreHost core, DispatcherQueue queue)
    {
        _core = core;
        _renewal = queue.CreateTimer();
        _renewal.IsRepeating = false;
        _renewal.Tick += (_, _) => _ = AskForChannelAsync();
    }

    /// <summary>
    /// Registers for push with Windows, before the activation is read (a push
    /// that launched the app is delivered through this registration), and
    /// follows the session from then on. Null where push is not supported.
    /// </summary>
    internal static WnsPush? Start(CoreHost core, DispatcherQueue queue)
    {
        if (!PushNotificationManager.IsSupported())
        {
            Log("push is not supported here");
            return null;
        }
        var push = new WnsPush(core, queue);
        PushNotificationManager.Default.PushReceived += push.OnPushReceived;
        push.Register();
        core.Changed += push.OnChanged;
        return push;
    }

    /// <summary>Registers this app with Windows for push, so a push can launch it.</summary>
    private void Register()
    {
        if (_registered)
        {
            return;
        }
        try
        {
            PushNotificationManager.Default.Register();
            _registered = true;
        }
        catch (Exception error) when (error is System.Runtime.InteropServices.COMException or InvalidOperationException or UnauthorizedAccessException)
        {
            Log($"push could not be registered with Windows: {error.Message}");
        }
    }

    /// <summary>The raw payload of a push, as the service wrote it (UTF-8 JSON).</summary>
    internal static string Payload(byte[] payload) => Encoding.UTF8.GetString(payload);

    /// <summary>A push while the app runs. Arrives on a background thread.</summary>
    private void OnPushReceived(PushNotificationManager sender, PushNotificationReceivedEventArgs args)
    {
        var payload = Payload(args.Payload);
        if (!_core.PushReceived(payload))
        {
            Log("a push this build does not read was ignored");
        }
    }

    /// <summary>The session's phase, at each change: a channel at sign-in, and Windows told at sign-out.</summary>
    private void OnChanged(object? sender, CoreSnapshot snapshot)
    {
        var signedIn = snapshot.Shell.Phase == SessionPhase.SignedIn;
        if (signedIn == _signedIn)
        {
            return;
        }
        _signedIn = signedIn;
        if (signedIn)
        {
            Register();
            _ = AskForChannelAsync();
            return;
        }
        // Signed out: the core's sign-out unregistered the channel with the
        // service; Windows closes it and stops launching the app for it,
        // until the next sign-in registers again.
        _renewal.Stop();
        if (!_registered)
        {
            return;
        }
        try
        {
            PushNotificationManager.Default.UnregisterAll();
        }
        catch (Exception error) when (error is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            Log($"push could not be unregistered with Windows: {error.Message}");
        }
        _registered = false;
    }

    /// <summary>Asks Windows for a channel and registers its URI, then schedules the next.</summary>
    private async Task AskForChannelAsync()
    {
        if (!_registered || !_signedIn || _asking)
        {
            return;
        }
        _asking = true;
        try
        {
            var result = await PushNotificationManager.Default.CreateChannelAsync(RemoteId);
            if (result.Status != PushNotificationChannelStatus.CompletedSuccess || result.Channel is null)
            {
                Log($"no push channel ({result.Status}, 0x{result.ExtendedError.HResult:X8}); the app rings only while it runs");
                Schedule(RetryAfter);
                return;
            }
            var channel = result.Channel;
            var registration = await _core.RegisterPushChannelAsync(channel.Uri.AbsoluteUri);
            if (registration is not PushRegistration.Registered)
            {
                Log($"the push channel was not registered: {Describe(registration)}");
            }
            var renew = channel.ExpirationTime - DateTimeOffset.Now - RenewAhead;
            Schedule(renew > TimeSpan.Zero ? renew : RetryAfter);
        }
        catch (Exception error) when (error is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            Log($"no push channel: {error.Message}");
            Schedule(RetryAfter);
        }
        finally
        {
            _asking = false;
        }
    }

    private void Schedule(TimeSpan after)
    {
        _renewal.Stop();
        _renewal.Interval = after;
        _renewal.Start();
    }

    /// <summary>A registration's outcome, for the log. Never the channel URI.</summary>
    internal static string Describe(PushRegistration registration) => registration switch
    {
        PushRegistration.Registered => "registered",
        PushRegistration.NotAChannel => "not a WNS channel",
        PushRegistration.NotStarted => "the core is not running",
        PushRegistration.Failed failed => failed.Reason,
        _ => "unknown",
    };

    private static void Log(string message) => Trace.TraceWarning($"District AI push: {message}");

    /// <summary>
    /// Stops following the session (the app is ending). The registration with
    /// Windows stays: it is what launches the closed app for a push.
    /// </summary>
    public void Dispose()
    {
        _core.Changed -= OnChanged;
        _renewal.Stop();
        PushNotificationManager.Default.PushReceived -= OnPushReceived;
    }
}
