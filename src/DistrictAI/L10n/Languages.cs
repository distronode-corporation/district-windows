using DistrictAI.Core;
using DistrictAI.Core.Ffi;

namespace DistrictAI.L10n;

/// <summary>
/// The languages the app speaks in. A build made with
/// <c>-p:DistrictFrench=false</c> is English whatever the user's languages.
/// Every other build (the default, and every release from 3.0) speaks
/// Canadian French to anyone whose Windows languages pick French, whatever
/// the region (fr-CA, fr-FR, fr-BE, fr-CH), and English to everyone else.
/// </summary>
internal static class Languages
{
    /// <summary>The language the app writes French in.</summary>
    private const string CanadianFrench = "fr-CA";

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
    /// French build the Windows display languages (Settings, Time and
    /// language), else English. Never the app's PrimaryLanguageOverride,
    /// which <see cref="ApplyToWindows"/> writes from this list: read back, it
    /// would keep the app French after the user moved Windows to English.
    /// </summary>
    public static IReadOnlyList<string> ForCore() =>
        FrenchBuild
            ? CoreHost.UserLanguages(null, Windows.System.UserProfile.GlobalizationPreferences.Languages)
            : ["en-US"];

    /// <summary>
    /// Has Windows resolve the app's own resources (resources.pri, and the
    /// text WinUI's controls carry) in the language the app speaks, before any
    /// XAML loads: fr-CA when the app speaks French, so a fr-FR or fr-BE user
    /// gets the same Canadian French the app writes. When it speaks English,
    /// an fr-CA override an earlier French launch set is cleared (through
    /// Windows.Globalization, whose setter takes an empty string; the Windows
    /// App SDK's throws on one), so Windows' own languages apply again. Any
    /// other override is left alone.
    /// </summary>
    public static void ApplyToWindows()
    {
        if (!FrenchBuild)
        {
            return;
        }
        try
        {
            var current = Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride ?? string.Empty;
            var isFrench = string.Equals(current, CanadianFrench, StringComparison.OrdinalIgnoreCase);
            if (French && !isFrench)
            {
                Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = CanadianFrench;
            }
            else if (!French && isFrench)
            {
                Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = string.Empty;
            }
        }
        catch (Exception error) when (error is System.Runtime.InteropServices.COMException or UnauthorizedAccessException or ArgumentException)
        {
            // The override picks only the language of WinUI's own text and of
            // resources.pri. The app's words come from the generated strings
            // and the core, which do not read it, so a Windows that refuses it
            // costs that much, never the start.
        }
    }
}
