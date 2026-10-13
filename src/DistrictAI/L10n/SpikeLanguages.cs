// SPIKE (do not merge): which language APIs apply to the packaged app, read
// back through UI Automation by tests/DistrictAI.UiTests/SpikeL10nTests.cs.

using System.Globalization;
using System.Text;

namespace DistrictAI.L10n;

/// <summary>
/// Applies a PrimaryLanguageOverride named on the command line, before any
/// XAML loads, and reports what each language API then says.
/// </summary>
public static class SpikeLanguages
{
    private const string Flag = "--spike-override=";

    /// <summary>What every language API said at start-up, on one line per API.</summary>
    public static string Report { get; private set; } = string.Empty;

    /// <summary>
    /// <c>--spike-override=win:fr-CA</c> sets Windows.Globalization's override,
    /// <c>--spike-override=mrt:fr-CA</c> the Windows App SDK's, and an empty
    /// tag (<c>win:</c>, <c>mrt:</c>) clears it. Then the report is taken.
    /// </summary>
    public static void Apply(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var log = new StringBuilder();
        Line(log, "args", string.Join(" ", args.Skip(1)));
        foreach (var arg in args.Where(a => a.StartsWith(Flag, StringComparison.Ordinal)))
        {
            var value = arg[Flag.Length..];
            var colon = value.IndexOf(':', StringComparison.Ordinal);
            var api = colon < 0 ? value : value[..colon];
            var tag = colon < 0 ? string.Empty : value[(colon + 1)..];
            Try(log, "set " + api + ".override=" + tag, () =>
            {
                if (api == "win")
                {
                    Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = tag;
                }
                else if (api == "mrt")
                {
                    Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = tag;
                }
                else
                {
                    throw new ArgumentException("unknown api " + api);
                }
                return "ok";
            });
        }
        Try(log, "win.override", () => Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride);
        Try(log, "win.languages", () => string.Join(",", Windows.Globalization.ApplicationLanguages.Languages));
        Try(log, "win.manifest", () => string.Join(",", Windows.Globalization.ApplicationLanguages.ManifestLanguages));
        Try(log, "mrt.override", () => Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride);
        Try(log, "mrt.languages", () => string.Join(",", Microsoft.Windows.Globalization.ApplicationLanguages.Languages));
        Try(log, "mrt.manifest", () => string.Join(",", Microsoft.Windows.Globalization.ApplicationLanguages.ManifestLanguages));
        Try(log, "user.languages", () => string.Join(",", Windows.System.UserProfile.GlobalizationPreferences.Languages));
        Try(log, "resw.AppDisplayName", () => new Microsoft.Windows.ApplicationModel.Resources.ResourceLoader().GetString("AppDisplayName"));
        Try(log, "resw.AppDescription", () => new Microsoft.Windows.ApplicationModel.Resources.ResourceLoader().GetString("AppDescription"));
        Try(log, "culture", () => CultureInfo.CurrentCulture.Name + "/" + CultureInfo.CurrentUICulture.Name);
        Try(log, "S.French", () => S.French ? "true" : "false");
        Report = log.ToString().TrimEnd();
    }

    private static void Try(StringBuilder log, string key, Func<string> read)
    {
        string value;
        try
        {
            value = read();
        }
#pragma warning disable CA1031 // The spike reports every failure instead of stopping.
        catch (Exception e)
#pragma warning restore CA1031
        {
            value = string.Create(CultureInfo.InvariantCulture, $"THREW {e.GetType().Name} 0x{e.HResult:X8} {e.Message}");
        }
        Line(log, key, value);
    }

    private static void Line(StringBuilder log, string key, string value) =>
        log.Append(key).Append('=').Append(value.ReplaceLineEndings(" ")).Append(" | ");
}
