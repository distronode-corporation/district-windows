// SPIKE (do not merge): a hand-written stand-in for the Strings.g.cs that
// l10n/windows.toml will generate. XAML reads each key as a static property,
// {x:Bind l10n:S.Key}, so a key that does not exist fails the XAML compile.

namespace DistrictAI.L10n;

/// <summary>The app's strings, one static property per catalog key.</summary>
public static class S
{
    /// <summary>
    /// Whether the strings are French: only in a build with DistrictFrench, and
    /// only when the app's first language (after PrimaryLanguageOverride) is a
    /// French one. Read once, on first use, after Program.Main has applied any
    /// override.
    /// </summary>
    public static bool French { get; } = ChooseFrench();

    /// <summary>The sign-in page's button.</summary>
    public static string SignInWithBrowser => Pick("Sign in with your browser", "Se connecter avec votre navigateur");

    /// <summary>A line inside a DataTemplate with x:DataType, bound statically.</summary>
    public static string SpikeTemplateText => Pick("Static x:Bind inside a DataTemplate", "x:Bind statique dans un DataTemplate");

    private static string Pick(string english, string french) => French ? french : english;

    private static bool ChooseFrench()
    {
#if DISTRICT_FRENCH
        var languages = Microsoft.Windows.Globalization.ApplicationLanguages.Languages;
        return languages.Count > 0 && languages[0].StartsWith("fr", StringComparison.OrdinalIgnoreCase);
#else
        return false;
#endif
    }
}

/// <summary>One row of the spike's DataTemplate.</summary>
/// <param name="Label">The row's own text, bound through x:DataType.</param>
public sealed record SpikeRow(string Label)
{
    /// <summary>The rows the sign-in page's spike list shows.</summary>
    public static IReadOnlyList<SpikeRow> All { get; } = [new("row")];
}
