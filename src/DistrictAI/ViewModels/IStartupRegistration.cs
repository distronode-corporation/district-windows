namespace DistrictAI.ViewModels;

// TEMPORARY. The Account page's "Start at sign-in" switch is bound to
// DistrictAI.Platform.StartupRegistration, which is not on main yet. Until it
// is, the page codes against this interface and NoStartupRegistration stands
// in for it. When StartupRegistration lands, replace NoStartupRegistration with
// an adapter over it (GetAsync and SetAsync map one to one, StartupSetting to
// StartupState), or delete this file and use StartupRegistration directly.

/// <summary>Whether the app starts when the user signs in to Windows, and changing it.</summary>
internal interface IStartupRegistration
{
    /// <summary>The setting as it is now.</summary>
    Task<StartupState> GetAsync();

    /// <summary>Turns it on or off, and answers the setting as it is afterwards.</summary>
    Task<StartupState> SetAsync(bool enabled);
}

/// <summary>Whether the app starts at sign-in.</summary>
/// <param name="Enabled">Whether it does.</param>
/// <param name="CanChange">Whether the user can change it here (a policy, or the user in Task Manager, can forbid it).</param>
/// <param name="Message">Why it cannot be changed, or empty.</param>
internal sealed record StartupState(bool Enabled, bool CanChange, string? Message);

/// <summary>Stands in for the platform's registration until it is built: off, and not changeable.</summary>
internal sealed class NoStartupRegistration : IStartupRegistration
{
    private static readonly StartupState _notYet = new(false, false, "Starting at sign-in arrives in a later build.");

    public Task<StartupState> GetAsync() => Task.FromResult(_notYet);

    public Task<StartupState> SetAsync(bool enabled) => Task.FromResult(_notYet);
}
