using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels.Analytics;
using Xunit;

namespace DistrictAI.Presentation.Tests.Analytics;

// The stub's contract until the area's packet builds it: it keeps the core's
// view, and wraps each action in the area's event.
public sealed class AnalyticsViewModelTests
{
    [Fact]
    public void ItKeepsTheViewAndSendsTheAction()
    {
        var model = new AnalyticsViewModel();
        model.Send(default(AnalyticsAction));

        var (context, sink) = Pages.Context();
        model.Attach(context);
        var view = new AnalyticsView("Title");
        model.Show(view);
        model.Send(default(AnalyticsAction));

        Assert.Same(view, model.View);
        Assert.Equal([new UiEvent.Analytics(default(AnalyticsAction))], sink.Sent);
    }
}
