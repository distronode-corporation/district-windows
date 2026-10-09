using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels.Settings.Tools;
using Xunit;

namespace DistrictAI.Presentation.Tests.Settings.Tools;

// The stub's contract until the area's packet builds it: it keeps the core's
// view, and wraps each action in the area's event.
public sealed class ToolsViewModelTests
{
    [Fact]
    public void ItKeepsTheViewAndSendsTheAction()
    {
        var model = new ToolsViewModel();
        model.Send(default(ToolsAction));

        var (context, sink) = Pages.Context();
        model.Attach(context);
        var view = new ToolsView("Title");
        model.Show(view);
        model.Send(default(ToolsAction));

        Assert.Same(view, model.View);
        Assert.Equal([new UiEvent.Tools(default(ToolsAction))], sink.Sent);
    }
}
