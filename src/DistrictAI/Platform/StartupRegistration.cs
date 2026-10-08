using Windows.ApplicationModel;

namespace DistrictAI.Platform;

/// <summary>Whether District AI starts when the user signs in to Windows, and whether they may change it here.</summary>
/// <param name="Enabled">It starts at sign-in.</param>
/// <param name="CanChange">The app may turn it on or off; false when Windows Settings or a policy decides.</param>
/// <param name="Message">Why it cannot be changed here, to show beside the switch; null when it can.</param>
public sealed record StartupSetting(bool Enabled, bool CanChange, string? Message);

/// <summary>
/// Start at sign-in, over the package's startup task (<c>DistrictAIStartup</c>
/// in Package.appxmanifest, off until the person turns it on). Windows starts
/// the app hidden in the notification area when it runs.
/// </summary>
public static class StartupRegistration
{
    /// <summary>The startup task's id in Package.appxmanifest.</summary>
    public const string TaskId = "DistrictAIStartup";

    /// <summary>The setting as it is now.</summary>
    public static async Task<StartupSetting> GetAsync()
    {
        var task = await StartupTask.GetAsync(TaskId);
        return Describe(task.State);
    }

    /// <summary>
    /// Turns starting at sign-in on or off, and answers with the setting as it
    /// is afterwards: still off if the person turned it off in Windows Settings
    /// or a policy did, since only they can turn it back on.
    /// </summary>
    public static async Task<StartupSetting> SetAsync(bool enabled)
    {
        var task = await StartupTask.GetAsync(TaskId);
        if (enabled && task.State == StartupTaskState.Disabled)
        {
            return Describe(await task.RequestEnableAsync());
        }
        if (!enabled && task.State == StartupTaskState.Enabled)
        {
            task.Disable();
        }
        return Describe(task.State);
    }

    private static StartupSetting Describe(StartupTaskState state) => state switch
    {
        StartupTaskState.Enabled => new(Enabled: true, CanChange: true, Message: null),
        StartupTaskState.Disabled => new(Enabled: false, CanChange: true, Message: null),
        StartupTaskState.DisabledByUser => new(Enabled: false, CanChange: false, Message: "Turned off in Windows Settings, under Apps, Startup."),
        StartupTaskState.DisabledByPolicy => new(Enabled: false, CanChange: false, Message: "Turned off by your organisation."),
        StartupTaskState.EnabledByPolicy => new(Enabled: true, CanChange: false, Message: "Turned on by your organisation."),
        _ => new(Enabled: false, CanChange: false, Message: null),
    };
}
