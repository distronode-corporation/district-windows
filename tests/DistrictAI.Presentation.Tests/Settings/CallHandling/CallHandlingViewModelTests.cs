using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels.Settings.CallHandling;
using Xunit;

namespace DistrictAI.Presentation.Tests.Settings.CallHandling;

// The stub's contract until the area's packet builds it: it keeps the core's
// view, and wraps each action in the area's event.
public sealed class CallHandlingViewModelTests
{
    [Fact]
    public void ItKeepsTheViewAndSendsTheAction()
    {
        var model = new CallHandlingViewModel();
        model.Send(default(CallHandlingAction));

        var (context, sink) = Pages.Context();
        model.Attach(context);
        var view = new CallHandlingView("Title");
        model.Show(view);
        model.Send(default(CallHandlingAction));

        Assert.Same(view, model.View);
        Assert.Equal([new UiEvent.CallHandling(default(CallHandlingAction))], sink.Sent);
    }
}
