using DistrictAI.Core;
using DistrictAI.Core.Ffi;

namespace DistrictAI.L10n;

/// <summary>
/// The languages the app speaks in. French ships only in a build made with
/// <c>-p:DistrictFrench=true</c> (DISTRICT_FRENCH); any other build is English
/// whatever the user's languages, so nobody sees French before it is complete.
/// </summary>
internal static class Languages
{
    /// <summary>Whether this build may speak French.</summary>
#if DISTRICT_FRENCH
    public static bool FrenchBuild => true;
#else
    public static bool FrenchBuild => false;
#endif

    /// <summary>
    /// Whether the window speaks Canadian French: in a French build, when the
    /// core does too (district-ffi's speaks_french, the core's own rule over
    /// the same languages). Read once, at start-up.
    /// </summary>
    public static bool French { get; } = FrenchBuild && DistrictFfi.SpeaksFrench([.. ForCore()]);

    /// <summary>
    /// The languages the core is started with, most preferred first: in a
    /// French build the user's (the app's PrimaryLanguageOverride when set,
    /// then the Windows display languages), else English.
    /// </summary>
    public static IReadOnlyList<string> ForCore() =>
        FrenchBuild
            ? CoreHost.UserLanguages(
                Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride,
                Windows.System.UserProfile.GlobalizationPreferences.Languages)
            : ["en-US"];
}
