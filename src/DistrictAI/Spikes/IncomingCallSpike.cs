#if DISTRICT_SPIKES
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace DistrictAI.Spikes;

/// <summary>
/// W2 spike 5: an IncomingCall toast with Answer and Decline, shown on
/// <c>districtai://spike-toast</c>. W7 builds the real one from the core's
/// urgent notification; this proves the scenario, the buttons and both ways
/// they come back (to the running app, and by cold COM activation).
/// </summary>
internal static class IncomingCallSpike
{
    public const string Tag = "spike-call";

    public static async Task ShowAsync()
    {
        var notification = new AppNotificationBuilder()
            .SetScenario(AppNotificationScenario.IncomingCall)
            .AddArgument("call", "spike")
            .AddText("Incoming call")
            .AddText("Spike caller, +1 212 555 0142")
            .AddButton(new AppNotificationButton("Answer").AddArgument("action", "answer").AddArgument("call", "spike"))
            .AddButton(new AppNotificationButton("Decline").AddArgument("action", "decline").AddArgument("call", "spike"))
            .SetTag(Tag)
            .BuildNotification();
        AppNotificationManager.Default.Show(notification);
        var shown = await AppNotificationManager.Default.GetAllAsync();
        SpikeLog.Write($"toast-shown id={notification.Id} listed={shown.Any(n => n.Tag == Tag)} setting={AppNotificationManager.Default.Setting}");
    }

    public static string Describe(AppNotificationActivatedEventArgs args) =>
        string.Join(";", args.Arguments.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key}={pair.Value}"));
}
#endif
