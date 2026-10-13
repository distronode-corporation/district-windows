using FlaUI.Core.Definitions;
using Xunit;

namespace DistrictAI.UiTests;

/// <summary>
/// SPIKE (do not merge): which PrimaryLanguageOverride reaches the packaged
/// app, run against a package built with -p:DistrictFrench=true. Each launch
/// passes --spike-override= (src/DistrictAI/L10n/SpikeLanguages.cs), and the
/// app shows what every language API said in the element SpikeLanguages.
/// </summary>
public sealed class SpikeL10nTests
{
    private static readonly TimeSpan _startTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan _stepTimeout = TimeSpan.FromSeconds(30);

    // Plain, Windows.Globalization's override set, plain again (does it
    // persist?), cleared; then the same for the Windows App SDK's override.
    private static readonly string[] _launches =
    [
        "",
        "--spike-override=win:fr-CA",
        "",
        "--spike-override=win:",
        "--spike-override=mrt:fr-CA",
        "",
        "--spike-override=mrt:",
    ];

    [Fact]
    public void ReportsEachLanguageApiUnderEachOverride()
    {
        SmokeTests.RequireInstalledApp();
        var steps = new List<(string Arguments, string Report, string Button, string Template)>();
        try
        {
            foreach (var arguments in _launches)
            {
                steps.Add(Read(arguments));
            }
        }
        finally
        {
            _ = Read("--spike-override=win: --spike-override=mrt:");
            InstalledApp.StopAll();
        }

        // The answers are in the log; only the plain first launch is asserted.
        Assert.Equal(_launches.Length, steps.Count);
        Assert.Contains("S.French=false", steps[0].Report, StringComparison.Ordinal);
    }

    private static (string, string, string, string) Read(string arguments)
    {
        using var app = InstalledApp.Launch(arguments.Length == 0 ? null : arguments);
        _ = app.MainWindow(_startTimeout);
        _ = app.Find(ControlType.Text, "Welcome to District AI", _startTimeout);
        var report = app.FindById("SpikeLanguages", _stepTimeout, onScreen: false).Name;
        var button = app.FindById("SignInButton", _stepTimeout).Name;
        var template = app.FindById("SpikeTemplateText", _stepTimeout, onScreen: false).Name;
        InstalledApp.Log($"SPIKE launch [{arguments}]: button \"{button}\", template \"{template}\"");
        foreach (var part in report.Split(" | "))
        {
            InstalledApp.Log($"SPIKE   {part}");
        }
        return (arguments, report, button, template);
    }
}
