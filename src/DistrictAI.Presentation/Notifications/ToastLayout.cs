using System.Security.Cryptography;
using System.Text;
using DistrictAI.Core.Ffi;

namespace DistrictAI.ViewModels.Notifications;

/// <summary>Which of Windows' toasts a notification is shown as.</summary>
public enum ToastStyle
{
    /// <summary>An ordinary toast: a call over, such as one missed. Its body opens it.</summary>
    Plain,

    /// <summary>A new message: an ordinary toast with no buttons, whose body opens the conversation.</summary>
    Message,

    /// <summary>A call ringing now: Windows' incoming-call toast, silent (the app rings), kept until dealt with.</summary>
    IncomingCall,

    /// <summary>Urgent without buttons, where Windows has the urgent scenario.</summary>
    Urgent,
}

/// <summary>A button on a toast, and what pressing it hands back.</summary>
/// <param name="Label">What it says.</param>
/// <param name="ActionId">The action id the core gave it.</param>
public sealed record ToastButton(string Label, string ActionId);

/// <summary>
/// What a toast for one of the core's notifications carries, decided apart
/// from Windows so that it can be tested anywhere: its tag, its arguments,
/// its words, its style and its buttons. <c>ToastNotifier</c> builds the toast
/// from it.
/// </summary>
/// <remarks>
/// A toast outlives the moment it was shown, so what pressing it does is in
/// its arguments: the notification's id and the button's action id, never the
/// content of a message or who is calling. The core keeps the table that turns
/// them back into what they were for, so a message's toast opens its
/// conversation through the core, which routes it.
/// </remarks>
/// <param name="Tag">The toast's tag (<see cref="TagFor"/>), by which it is replaced and withdrawn.</param>
/// <param name="Id">The notification's id, carried as <see cref="IdArgument"/>.</param>
/// <param name="Title">The heading.</param>
/// <param name="Body">The body.</param>
/// <param name="Style">Which toast it is.</param>
/// <param name="Buttons">Its buttons, in order. None for a message.</param>
public sealed record ToastLayout(string Tag, string Id, string Title, string Body, ToastStyle Style, IReadOnlyList<ToastButton> Buttons)
{
    /// <summary>The argument holding the notification's id.</summary>
    public const string IdArgument = "notification-id";

    /// <summary>The argument holding the button's action id. Absent for the toast's body.</summary>
    public const string ActionArgument = "notification-action";

    /// <summary>
    /// The toast for <paramref name="notification"/>. <paramref name="urgentSupported"/>
    /// is asked only for an urgent notification without buttons.
    /// </summary>
    public static ToastLayout For(NotificationView notification, Func<bool> urgentSupported)
    {
        ArgumentNullException.ThrowIfNull(notification);
        ArgumentNullException.ThrowIfNull(urgentSupported);
        var buttons = notification.Actions.Select(action => new ToastButton(action.Label, action.ActionId)).ToArray();
        var style = notification.Kind switch
        {
            // A message is never urgent and never has buttons, whatever else it carries.
            NotificationKind.Message => ToastStyle.Message,
            _ when notification.Urgent && buttons.Length > 0 => ToastStyle.IncomingCall,
            _ when notification.Urgent && urgentSupported() => ToastStyle.Urgent,
            _ => ToastStyle.Plain,
        };
        return new ToastLayout(
            TagFor(notification.Id),
            notification.Id,
            notification.Title,
            notification.Body,
            style,
            style == ToastStyle.Message ? [] : buttons);
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
    public static string TagFor(string id) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id)), 0, 8);
}
