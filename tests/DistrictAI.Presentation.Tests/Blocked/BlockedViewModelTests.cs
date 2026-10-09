using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels.Blocked;
using Xunit;

namespace DistrictAI.Presentation.Tests.Blocked;

// The stub's contract until the area's packet builds it: it keeps the core's
// view, and wraps each action in the area's event.
public sealed class BlockedViewModelTests
{
    [Fact]
    public void ItKeepsTheViewAndSendsTheAction()
    {
        var model = new BlockedViewModel();
        model.Send(default(BlockedAction));

        var (context, sink) = Pages.Context();
        model.Attach(context);
        var view = new BlockedView("Title");
        model.Show(view);
        model.Send(default(BlockedAction));

        Assert.Same(view, model.View);
        Assert.Equal([new UiEvent.Blocked(default(BlockedAction))], sink.Sent);
    }
}
