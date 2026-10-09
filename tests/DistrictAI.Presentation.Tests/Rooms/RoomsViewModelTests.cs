using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels.Rooms;
using Xunit;

namespace DistrictAI.Presentation.Tests.Rooms;

// The stub's contract until the area's packet builds it: it keeps the core's
// view, and wraps each action in the area's event.
public sealed class RoomsViewModelTests
{
    [Fact]
    public void ItKeepsTheViewAndSendsTheAction()
    {
        var model = new RoomsViewModel();
        model.Send(default(RoomsAction));

        var (context, sink) = Pages.Context();
        model.Attach(context);
        var view = new RoomsView("Title");
        model.Show(view);
        model.Send(default(RoomsAction));

        Assert.Same(view, model.View);
        Assert.Equal([new UiEvent.Rooms(default(RoomsAction))], sink.Sent);
    }
}
