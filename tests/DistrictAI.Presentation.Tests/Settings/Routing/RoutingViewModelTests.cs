using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels.Settings.Routing;
using Xunit;

namespace DistrictAI.Presentation.Tests.Settings.Routing;

// The stub's contract until the area's packet builds it: it keeps the core's
// view, and wraps each action in the area's event.
public sealed class RoutingViewModelTests
{
    [Fact]
    public void ItKeepsTheViewAndSendsTheAction()
    {
        var model = new RoutingViewModel();
        model.Send(default(RoutingAction));

        var (context, sink) = Pages.Context();
        model.Attach(context);
        var view = new RoutingView("Title");
        model.Show(view);
        model.Send(default(RoutingAction));

        Assert.Same(view, model.View);
        Assert.Equal([new UiEvent.Routing(default(RoutingAction))], sink.Sent);
    }
}
