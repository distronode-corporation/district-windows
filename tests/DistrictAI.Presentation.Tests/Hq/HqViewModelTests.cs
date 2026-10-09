using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels.Hq;
using Xunit;

namespace DistrictAI.Presentation.Tests.Hq;

// The stub's contract until the area's packet builds it: it keeps the core's
// view, and wraps each action in the area's event.
public sealed class HqViewModelTests
{
    [Fact]
    public void ItKeepsTheViewAndSendsTheAction()
    {
        var model = new HqViewModel();
        model.Send(default(HqAction));

        var (context, sink) = Pages.Context();
        model.Attach(context);
        var view = new HqView("Title");
        model.Show(view);
        model.Send(default(HqAction));

        Assert.Same(view, model.View);
        Assert.Equal([new UiEvent.Hq(default(HqAction))], sink.Sent);
    }
}
