using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels.Desk;
using Xunit;

namespace DistrictAI.Presentation.Tests.Desk;

// The stub's contract until the area's packet builds it: it keeps the core's
// view, and wraps each action in the area's event.
public sealed class DeskViewModelTests
{
    [Fact]
    public void ItKeepsTheViewAndSendsTheAction()
    {
        var model = new DeskViewModel();
        model.Send(new DeskAction.Open());

        var (context, sink) = Pages.Context();
        model.Attach(context);
        var view = new DeskView("Title");
        model.Show(view);
        model.Send(new DeskAction.Open());

        Assert.Same(view, model.View);
        Assert.Equal([new UiEvent.Desk(new DeskAction.Open())], sink.Sent);
    }
}
