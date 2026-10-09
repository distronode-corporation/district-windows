using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels.Settings;
using Xunit;

namespace DistrictAI.Presentation.Tests.Settings;

// The stub's contract until the area's packet builds it: it keeps the core's
// view, and wraps each action in the area's event.
public sealed class SettingsHubViewModelTests
{
    [Fact]
    public void ItKeepsTheViewAndSendsTheAction()
    {
        var model = new SettingsHubViewModel();
        model.Send(default(SettingsAction));

        var (context, sink) = Pages.Context();
        model.Attach(context);
        var view = new SettingsHubView("Title");
        model.Show(view);
        model.Send(default(SettingsAction));

        Assert.Same(view, model.View);
        Assert.Equal([new UiEvent.Settings(default(SettingsAction))], sink.Sent);
    }
}
