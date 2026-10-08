using Windows.ApplicationModel;
using Windows.System;

namespace DistrictAI.Platform;

/// <summary>
/// Another installed copy of District AI: the Store's and the GitHub one are
/// separate packages, and both claim <c>districtai://</c>. With both
/// installed, a sign-in's answer from the browser could land in the copy that
/// did not start it (and so does not hold its PKCE verifier), so this copy
/// says so and starts no browser sign-in.
/// </summary>
internal static class OtherCopy
{
    /// <summary>The other copy's name, or null when this is the only one.</summary>
    public static async Task<string?> FindAsync()
    {
        var mine = Package.Current.Id.FamilyName;
        var handlers = await Launcher.FindUriSchemeHandlersAsync("districtai");
        var other = handlers.FirstOrDefault(app => !string.Equals(app.PackageFamilyName, mine, StringComparison.Ordinal));
        return other is null ? null : $"{other.DisplayInfo.DisplayName} ({other.PackageFamilyName})";
    }

    /// <summary>The notice shown while another copy is installed.</summary>
    public static string Notice(string other) =>
        $"Another copy of District AI is installed: {other}. Sign-in works in one copy only. " +
        "Remove the copy you do not use in Settings, Apps, Installed apps.";

    /// <summary>The notice for a sign-in answer this copy did not ask for.</summary>
    public const string WrongCopy = "That sign-in was started in the other copy of District AI, so this copy did not use it.";
}
