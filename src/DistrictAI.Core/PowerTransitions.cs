namespace DistrictAI.Core;

/// <summary>What the app does about one of Windows' power events.</summary>
public enum PowerAction
{
    /// <summary>Nothing: the event is not about sleeping, or repeats a wake already handled.</summary>
    None,

    /// <summary>The machine is about to sleep: hold the sleep on <see cref="CoreHost.SuspendAsync"/>, bounded by <see cref="PowerTransitions.SuspendWait"/>.</summary>
    Suspend,

    /// <summary>The machine has woken: <see cref="CoreHost.Resume"/>.</summary>
    Resume,
}

/// <summary>
/// Turns the power events Windows delivers to a suspend and resume
/// notification into what the core is told, so it hears each sleep once and
/// each wake once.
/// </summary>
/// <remarks>
/// Windows announces a sleep with <c>PBT_APMSUSPEND</c>. Waking, it sends
/// <c>PBT_APMRESUMEAUTOMATIC</c> every time, then <c>PBT_APMRESUMESUSPEND</c>
/// as well when someone is at the machine, so the first of the two that follows
/// a suspend is the wake and anything after it is ignored. A sleep is always
/// passed on, even twice in a row: telling the core again costs nothing, and
/// missing one would leave this computer ringing while it sleeps. Safe to call
/// from any thread.
/// </remarks>
public sealed class PowerTransitions
{
    /// <summary><c>PBT_APMSUSPEND</c>: the machine is about to sleep or hibernate.</summary>
    public const uint ApmSuspend = 0x4;

    /// <summary><c>PBT_APMRESUMESUSPEND</c>: awake, and someone is at the machine.</summary>
    public const uint ApmResumeSuspend = 0x7;

    /// <summary><c>PBT_APMRESUMEAUTOMATIC</c>: awake, sent on every wake.</summary>
    public const uint ApmResumeAutomatic = 0x12;

    /// <summary>
    /// The longest the app holds a sleep on the core. The core gives its own
    /// work 1.5 seconds and answers then; this is the backstop should that
    /// answer be late, still under the two seconds Windows allows an app to
    /// handle <c>PBT_APMSUSPEND</c>.
    /// </summary>
    public static readonly TimeSpan SuspendWait = TimeSpan.FromMilliseconds(1750);

    private int _asleep;

    /// <summary>What to do about the power event <paramref name="type"/>.</summary>
    public PowerAction Next(uint type)
    {
        switch (type)
        {
            case ApmSuspend:
                Volatile.Write(ref _asleep, 1);
                return PowerAction.Suspend;
            case ApmResumeAutomatic:
            case ApmResumeSuspend:
                return Interlocked.Exchange(ref _asleep, 0) == 1 ? PowerAction.Resume : PowerAction.None;
            default:
                return PowerAction.None;
        }
    }
}
