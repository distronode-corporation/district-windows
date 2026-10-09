using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels.Settings.Knowledge;
using Xunit;

namespace DistrictAI.Presentation.Tests.Settings.Knowledge;

// The stub's contract until the area's packet builds it: it keeps the core's
// view, and wraps each action in the area's event.
public sealed class KnowledgeViewModelTests
{
    [Fact]
    public void ItKeepsTheViewAndSendsTheAction()
    {
        var model = new KnowledgeViewModel();
        model.Send(default(KnowledgeAction));

        var (context, sink) = Pages.Context();
        model.Attach(context);
        var view = new KnowledgeView("Title");
        model.Show(view);
        model.Send(default(KnowledgeAction));

        Assert.Same(view, model.View);
        Assert.Equal([new UiEvent.Knowledge(default(KnowledgeAction))], sink.Sent);
    }
}
