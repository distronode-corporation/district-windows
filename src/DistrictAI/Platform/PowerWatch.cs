using System.Runtime.InteropServices;
using DistrictAI.Core;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Power;
using Windows.Win32.UI.WindowsAndMessaging;

namespace DistrictAI.Platform;

/// <summary>
/// Tells the core when the machine is about to sleep and when it has woken,
/// over <c>PowerRegisterSuspendResumeNotification</c>. That is a callback
/// registration, not a window message, so it works the same while the window
/// is hidden to the tray.
/// </summary>
/// <remarks>
/// A computer that sleeps while it is registered to ring would hold a caller
/// for a ring nobody hears until the registration lapses, ten minutes later.
/// So the callback holds the sleep until the core has unregistered this
/// computer and ended any call, for at most
/// <see cref="PowerTransitions.SuspendWait"/>: Windows allows an app about
/// two seconds. Waking, the core registers again; nothing waits for that.
/// Windows calls back on a thread of its own, never the UI thread, so nothing
/// here touches XAML. Which events count is <see cref="PowerTransitions"/>'s
/// rule, tested with the core.
/// </remarks>
internal sealed class PowerWatch : IDisposable
{
    private readonly CoreHost _core;
    private readonly PowerTransitions _transitions = new();
    // Held for as long as the registration lasts: the native side keeps only a
    // function pointer to it.
    private readonly PDEVICE_NOTIFY_CALLBACK_ROUTINE _callback;
    // The DEVICE_NOTIFY_SUBSCRIBE_PARAMETERS the registration was given, in
    // native memory, freed only once the registration has ended.
    private readonly nint _parameters;
    private HPOWERNOTIFY _registration;
    private int _disposed;

    private unsafe PowerWatch(CoreHost core)
    {
        _core = core;
        _callback = Callback;
        _parameters = Marshal.AllocHGlobal(Marshal.SizeOf<DEVICE_NOTIFY_SUBSCRIBE_PARAMETERS>());
        Marshal.StructureToPtr(new DEVICE_NOTIFY_SUBSCRIBE_PARAMETERS { Callback = _callback }, _parameters, false);
    }

    /// <summary>
    /// Starts telling <paramref name="core"/> about sleep and wake, until
    /// disposed. Null when Windows refuses the registration: the app runs on,
    /// and a computer that sleeps stops ringing only when its registration
    /// lapses.
    /// </summary>
    public static unsafe PowerWatch? Start(CoreHost core)
    {
        ArgumentNullException.ThrowIfNull(core);
        var watch = new PowerWatch(core);
        void* registration;
        var result = PInvoke.PowerRegisterSuspendResumeNotification(
            REGISTER_NOTIFICATION_FLAGS.DEVICE_NOTIFY_CALLBACK,
            new HANDLE(watch._parameters),
            &registration);
        if (result != WIN32_ERROR.ERROR_SUCCESS)
        {
            watch.Dispose();
            return null;
        }
        watch._registration = new HPOWERNOTIFY((nint)registration);
        return watch;
    }

    /// <summary>Ends the registration. After it returns, the core hears nothing more from here.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }
        if (!_registration.IsNull)
        {
            _ = PInvoke.PowerUnregisterSuspendResumeNotification(_registration);
            _registration = default;
        }
        Marshal.FreeHGlobal(_parameters);
    }

    /// <summary>
    /// Windows' call, on its own thread. Returning is what lets a sleep go
    /// ahead, so a suspend returns only once the core has done its part, or
    /// the wait is over.
    /// </summary>
    private unsafe uint Callback(void* context, uint type, void* setting)
    {
        if (Volatile.Read(ref _disposed) == 1)
        {
            return (uint)WIN32_ERROR.ERROR_SUCCESS;
        }
        var action = _transitions.Next(type);
        if (action == PowerAction.Suspend)
        {
            HoldForTheCore();
        }
        else if (action == PowerAction.Resume)
        {
            _core.Resume();
        }
        return (uint)WIN32_ERROR.ERROR_SUCCESS;
    }

    private void HoldForTheCore()
    {
        try
        {
            // False at the bound, and the sleep goes ahead regardless.
            _ = _core.SuspendAsync().Wait(PowerTransitions.SuspendWait);
        }
        catch (AggregateException)
        {
            // The core could not answer. The sleep goes ahead; the
            // registration lapses by itself.
        }
    }
}
