using System.Runtime.InteropServices;
using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels.Notifications;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace DistrictAI.Platform.Calls;

/// <summary>
/// The core's notifications (calls and new messages) as Windows toasts, and a
/// toast's activation back as the notification and button it names.
/// </summary>
/// <remarks>
/// What a toast carries is decided by <see cref="ToastLayout"/>, which is
/// tested on every platform; this class only hands it to Windows. A message's
/// toast has no buttons, and its body opens the conversation through the core.
/// <para>
/// Needs <see cref="AppNotificationManager.Register()"/> to have run, which
/// <c>App.OnLaunched</c> does before anything can be shown.
/// </para>
/// </remarks>
internal static class ToastNotifier
{
    /// <summary>
    /// Shows <paramref name="notification"/>, replacing any toast already shown
    /// for the same id, in the style <see cref="ToastLayout.For"/> picks.
    /// </summary>
    /// <returns>Whether Windows took it. False when notifications are off or unavailable.</returns>
    public static bool Show(NotificationView notification)
    {
        ArgumentNullException.ThrowIfNull(notification);
        var layout = ToastLayout.For(notification, AppNotificationBuilder.IsUrgentScenarioSupported);
        var builder = new AppNotificationBuilder()
            .SetTag(layout.Tag)
            .AddArgument(ToastLayout.IdArgument, layout.Id)
            .AddText(layout.Title)
            .AddText(layout.Body);
        switch (layout.Style)
        {
            case ToastStyle.IncomingCall:
                builder.SetScenario(AppNotificationScenario.IncomingCall).MuteAudio();
                break;
            case ToastStyle.Urgent:
                builder.SetScenario(AppNotificationScenario.Urgent);
                break;
            case ToastStyle.Message:
            case ToastStyle.Plain:
            default:
                break;
        }
        foreach (var button in layout.Buttons)
        {
            builder.AddButton(new AppNotificationButton(button.Label)
                .AddArgument(ToastLayout.IdArgument, layout.Id)
                .AddArgument(ToastLayout.ActionArgument, button.ActionId));
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
            // the window still shows the call, and the inbox the message.
            return false;
        }
    }

    /// <summary>
    /// Takes away the toast shown for <paramref name="id"/>, from the screen and
    /// from Action Center, when the core withdraws it. Nothing happens if there
    /// is none.
    /// </summary>
    public static async Task WithdrawAsync(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        try
        {
            if (AppNotificationManager.IsSupported())
            {
                await AppNotificationManager.Default.RemoveByTagAsync(ToastLayout.TagFor(id));
            }
        }
        catch (Exception error) when (error is COMException or InvalidOperationException or UnauthorizedAccessException)
        {
            // Already gone, or notifications are off: nothing to take away.
        }
    }

    /// <inheritdoc cref="ToastLayout.TryReadActivation"/>
    public static bool TryReadActivation(IDictionary<string, string> arguments, out string id, out string? actionId) =>
        ToastLayout.TryReadActivation(arguments, out id, out actionId);
}
