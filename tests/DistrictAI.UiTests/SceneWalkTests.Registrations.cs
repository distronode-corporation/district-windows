using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using Xunit;
using static DistrictAI.UiTests.Walk;

namespace DistrictAI.UiTests;

/// <summary>
/// The phone numbers screen's Registrations tab, in the scripted scene
/// (scripted.rs, registrations_answer), in English and in Canadian French:
/// the tab reads the workspace's filings (a draft and a refused one), Check
/// requirements reads Germany's regulation, the draft then offers its details
/// and an upload for its document, and File it opens the core's question,
/// which Escape closes with nothing filed. Everything is found by its
/// AutomationId, the same in every language. With DISTRICTAI_SCREENSHOTS set,
/// the tab is saved at 1920x1080 and at <see cref="NarrowWidth"/> by
/// <see cref="ShortHeight"/> as <c>98-registrations-LANGUAGE-WIDTHxHEIGHT.png</c>.
/// </summary>
public sealed partial class SceneWalkTests
{
    [Theory]
    [InlineData("en")]
    [InlineData("fr")]
    public void ARegistrationsDraftOffersItsDocumentsAndAsksBeforeFiling(string language)
    {
        RequireScriptedPackage();
        var shots = Environment.GetEnvironmentVariable("DISTRICTAI_SCREENSHOTS");
        using var app = InstalledApp.Launch(Arguments(SceneArgument, language));
        var window = app.MainWindow(_startTimeout);
        var handle = window.Properties.NativeWindowHandle.Value;
        _ = Heading(app, WorkspaceName, _startTimeout);

        _ = OpenById(app, "NavMarketplace");
        var tab = app.FindById("NumbersRegistrationsTab", PageTimeout);
        if (tab.Patterns.SelectionItem.PatternOrDefault is { } item)
        {
            item.Select();
        }
        else
        {
            Press(tab);
        }
        _ = app.FindById("RegistrationsHeading", PageTimeout);
        Expect(app, () => AllById(app, "FilingFile").Length == 1, "the draft's File it, and none on the refused filing");
        _ = app.FindById("FilingReasons", PageTimeout, onScreen: false);
        Assert.Empty(AllById(app, "FilingUpload"));
        InstalledApp.Log("the tab lists a draft and a refused filing with its reasons");

        app.FindById("RegistrationCountry", PageTimeout).AsTextBox().Text = "DE";
        var check = Wait.For(
            () => app.TryFindById("RequirementsCheck") is { IsEnabled: true } enabled ? enabled : null,
            PageTimeout,
            "Check requirements to work once a country is typed",
            app.Describe);
        Press(check);
        Expect(app, () => AllById(app, "FilingUpload").Length == 1, "the draft's upload for its regulation's document");
        Expect(app, () => AllById(app, "FilingDetail").Length == 2, "the draft's two end-user details");
        InstalledApp.Log("the regulation read, the draft offers its details and its upload");
        if (shots is { Length: > 0 })
        {
            foreach (var (width, height) in new[] { (1920, 1080), (NarrowWidth, ShortHeight) })
            {
                SizeClient(handle, width, height);
                Thread.Sleep(500);
                Save(handle, Path.Combine(shots, FormattableString.Invariant($"98-registrations-{language}-{width}x{height}.png")), clientOnly: true);
            }
            SizeClient(handle, 1920, 1080);
            Thread.Sleep(500);
        }

        // File it asks first; Escape closes the question with nothing filed
        // (the scene answers no write).
        Press(app.FindById("FilingFile", PageTimeout, onScreen: false), allow: "it only asks; the question is closed with Escape");
        _ = app.FindById("NumberConfirmationDialog", PageTimeout);
        InstalledApp.Log("File it asks first");
        Keyboard.Type(VirtualKeyShort.ESCAPE);
        Assert.True(
            Wait.Until(() => app.TryFindById("NumberConfirmationDialog") is null, PageTimeout),
            "the filing's question stayed open after Escape");
        Assert.True(app.IsRunning, "the app ended on the Registrations tab");
    }
}
