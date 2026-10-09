namespace DistrictAI.ViewModels;

/// <summary>Whether District AI starts when the user signs in to Windows, and whether they may change it here.</summary>
/// <param name="Enabled">It starts at sign-in.</param>
/// <param name="CanChange">The app may turn it on or off; false when Windows Settings or a policy decides.</param>
/// <param name="Message">Why it cannot be changed here, to show beside the switch; null when it can.</param>
public sealed record StartupSetting(bool Enabled, bool CanChange, string? Message);

/// <summary>
/// Start at sign-in, as the account page reads and changes it. The app's is
/// the package's startup task (<c>StartupRegistration</c>); the tests' is a fake.
/// </summary>
public interface IStartupTask
{
    /// <summary>The setting as it is now.</summary>
    Task<StartupSetting> GetAsync();

    /// <summary>
    /// Turns starting at sign-in on or off, and answers with the setting as it
    /// is afterwards, which may not be what was asked for.
    /// </summary>
    Task<StartupSetting> SetAsync(bool enabled);
}
