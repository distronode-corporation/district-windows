using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels.Settings;
using Xunit;

namespace DistrictAI.Presentation.Tests.Settings;

public sealed class SettingsHubViewModelTests
{
    private static SettingsHubView Hub(string note = "Changed on the website.") =>
        new(
            "Workspace settings",
            [
                new SettingsGroupView("District Studio",
                [
                    new SettingsRowView(SettingsSection.Persona, "Persona", "The receptionist's name."),
                    new SettingsRowView(SettingsSection.VoiceStudio, "Voice", "The voice."),
                ]),
                new SettingsGroupView("Workspace", [new SettingsRowView(SettingsSection.Numbers, "Phone numbers", "The numbers.")]),
            ],
            note);

    [Fact]
    public void ItShowsTheCoreGroupsInOrder()
    {
        var model = new SettingsHubViewModel();
        var view = Hub();
        model.Show(view);

        Assert.Same(view, model.View);
        Assert.Equal("Workspace settings", model.Title);
        Assert.Equal("Changed on the website.", model.Note);
        Assert.Equal(["District Studio", "Workspace"], model.Groups.Select(group => group.Heading));
        Assert.Equal(
            [SettingsSection.Persona, SettingsSection.VoiceStudio],
            model.Groups[0].Rows.Select(row => row.Section));
        Assert.Equal("Persona. The receptionist's name.", model.Groups[0].Rows[0].AccessibleName);
    }

    [Fact]
    public void AGroupDrawnOnceIsKeptAcrossSnapshots()
    {
        var model = new SettingsHubViewModel();
        model.Show(Hub());
        var first = model.Groups[0];
        model.Show(Hub("Another note."));
        Assert.Same(first, model.Groups[0]);
        Assert.Equal("Another note.", model.Note);
        Assert.False(first.Equals(null));
        Assert.Equal(first.GetHashCode(), model.Groups[0].GetHashCode());
    }

    [Fact]
    public void ARowOpensItsSection()
    {
        var model = new SettingsHubViewModel();
        model.Open(new SettingsRowItem(SettingsSection.Persona, "Persona", string.Empty));

        var (context, sink) = Pages.Context();
        model.Attach(context);
        model.Show(Hub());
        model.Open(model.Groups[1].Rows[0]);
        model.Send(new SettingsAction.Open());

        Assert.Equal(
            [
                new UiEvent.Settings(new SettingsAction.OpenSection(SettingsSection.Numbers)),
                new UiEvent.Settings(new SettingsAction.Open()),
            ],
            sink.Sent);
    }
}
