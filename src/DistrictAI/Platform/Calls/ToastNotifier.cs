using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using DistrictAI.Core.Ffi;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace DistrictAI.Platform.Calls;

/// <summary>
/// The core's notifications as Windows toasts, and a toast's activation back as
/// the notification and button it names.
/// </summary>
/// <remarks>
/// A toast outlives the moment it was shown (Action Center keeps it, and can
/// start the app with it after a restart), so what pressing it does is carried
/// in the toast itself, as arguments: the notification's id and the button's
/// action id, never the content of a message or who is calling. The core keeps
/// the table that turns them back into what they were for.
/// <para>
/// Needs <see cref="AppNotificationManager.Register()"/> to have run, which
/// <c>App.OnLaunched</c> does before anything can be shown.
/// </para>
/// </remarks>
internal static class ToastNotifier
{
    /// <summary>The argument holding the notification's id.</summary>
    internal const string IdArgument = "notification-id";

    /// <summary>The argument holding the button's action id. Absent for the toast's body.</summary>
    internal const string ActionArgument = "notification-action";

    /// <summary>
    /// Shows <paramref name="notification"/>, replacing any toast already shown
    /// for the same id. An urgent one with buttons is an incoming call: it uses
    /// Windows' incoming-call toast, which stays on screen until it is answered
    /// or dismissed, and is silent, because the app plays its own ringtone.
    /// </summary>
    /// <returns>Whether Windows took it. False when notifications are off or unavailable.</returns>
    public static bool Show(NotificationView notification)
    {
        ArgumentNullException.ThrowIfNull(notification);
        var builder = new AppNotificationBuilder()
            .SetTag(TagFor(notification.Id))
            .AddArgument(IdArgument, notification.Id)
            .AddText(notification.Title)
            .AddText(notification.Body);
        if (notification.Urgent && notification.Actions.Length > 0)
        {
            builder.SetScenario(AppNotificationScenario.IncomingCall).MuteAudio();
        }
        else if (notification.Urgent && AppNotificationBuilder.IsUrgentScenarioSupported())
        {
            builder.SetScenario(AppNotificationScenario.Urgent);
        }
        foreach (var action in notification.Actions)
        {
            builder.AddButton(new AppNotificationButton(action.Label)
                .AddArgument(IdArgument, notification.Id)
                .AddArgument(ActionArgument, action.ActionId));
        }
        try
        {
            if (!AppNotificationManager.IsSupported())
            {
                return false;
            }
            var shown = builder.BuildNotification();
            AppNotificationManager.Default.Show(shown);
            return shown.Id != 0;
        }
        catch (Exception error) when (error is COMException or InvalidOperationException or UnauthorizedAccessException)
        {
            // Notifications turned off for the app, or the platform has none:
            // the window's own banner still shows the call.
            return false;
        }
    }

    /// <summary>
    /// Takes away the toast shown for <paramref name="id"/>, from the screen and
    /// from Action Center. Nothing happens if there is none.
    /// </summary>
    public static async Task WithdrawAsync(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        try
        {
            if (AppNotificationManager.IsSupported())
            {
                await AppNotificationManager.Default.RemoveByTagAsync(TagFor(id));
            }
        }
        catch (Exception error) when (error is COMException or InvalidOperationException or UnauthorizedAccessException)
        {
            // Already gone, or notifications are off: nothing to take away.
        }
    }

    /// <summary>
    /// Reads the notification id, and the button's action id (null for the
    /// toast's body), out of a toast activation's <paramref name="arguments"/>.
    /// </summary>
    /// <returns>False for arguments this class did not write.</returns>
    public static bool TryReadActivation(IDictionary<string, string> arguments, out string id, out string? actionId)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        actionId = arguments.TryGetValue(ActionArgument, out var action) && action.Length > 0 ? action : null;
        if (arguments.TryGetValue(IdArgument, out var found) && found.Length > 0)
        {
            id = found;
            return true;
        }
        id = string.Empty;
        actionId = null;
        return false;
    }

    /// <summary>
    /// The toast's tag for the notification <paramref name="id"/>: Windows caps
    /// a tag's length, and an id has no stated limit, so it is a hash of the
    /// id, 16 hexadecimal digits, the same for the same id in every process.
    /// </summary>
    internal static string TagFor(string id) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id)), 0, 8);
}
