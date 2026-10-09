using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels.Scheduling;
using Xunit;

namespace DistrictAI.Presentation.Tests.Scheduling;

// The stub's contract until the area's packet builds it: it keeps the core's
// view, and wraps each action in the area's event.
public sealed class SchedulingViewModelTests
{
    [Fact]
    public void ItKeepsTheViewAndSendsTheAction()
    {
        var model = new SchedulingViewModel();
        model.Send(default(SchedulingAction));

        var (context, sink) = Pages.Context();
        model.Attach(context);
        var view = new SchedulingView("Title");
        model.Show(view);
        model.Send(default(SchedulingAction));

        Assert.Same(view, model.View);
        Assert.Equal([new UiEvent.Scheduling(default(SchedulingAction))], sink.Sent);
    }
}
