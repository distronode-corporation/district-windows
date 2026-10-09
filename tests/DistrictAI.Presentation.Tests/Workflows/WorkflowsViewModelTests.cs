using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels.Workflows;
using Xunit;

namespace DistrictAI.Presentation.Tests.Workflows;

// The stub's contract until the area's packet builds it: it keeps the core's
// view, and wraps each action in the area's event.
public sealed class WorkflowsViewModelTests
{
    [Fact]
    public void ItKeepsTheViewAndSendsTheAction()
    {
        var model = new WorkflowsViewModel();
        model.Send(default(WorkflowsAction));

        var (context, sink) = Pages.Context();
        model.Attach(context);
        var view = new WorkflowsView("Title");
        model.Show(view);
        model.Send(default(WorkflowsAction));

        Assert.Same(view, model.View);
        Assert.Equal([new UiEvent.Workflows(default(WorkflowsAction))], sink.Sent);
    }
}
