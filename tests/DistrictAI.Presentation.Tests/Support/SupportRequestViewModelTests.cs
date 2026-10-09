using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels.Support;
using Xunit;

namespace DistrictAI.Presentation.Tests.Support;

// The stub's contract until the area's packet builds it: it keeps the core's
// view, and wraps each action in the area's event.
public sealed class SupportRequestViewModelTests
{
    [Fact]
    public void ItKeepsTheViewAndSendsTheAction()
    {
        var model = new SupportRequestViewModel();
        model.Send(new SupportAction.Open());

        var (context, sink) = Pages.Context();
        model.Attach(context);
        var view = new SupportRequestView("Title");
        model.Show(view);
        model.Send(new SupportAction.Open());

        Assert.Same(view, model.View);
        Assert.Equal([new UiEvent.Support(new SupportAction.Open())], sink.Sent);
    }
}
