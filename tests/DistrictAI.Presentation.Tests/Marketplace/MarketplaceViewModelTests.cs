using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels.Marketplace;
using Xunit;

namespace DistrictAI.Presentation.Tests.Marketplace;

// The stub's contract until the area's packet builds it: it keeps the core's
// view, and wraps each action in the area's event.
public sealed class MarketplaceViewModelTests
{
    [Fact]
    public void ItKeepsTheViewAndSendsTheAction()
    {
        var model = new MarketplaceViewModel();
        model.Send(default(MarketplaceAction));

        var (context, sink) = Pages.Context();
        model.Attach(context);
        var view = new MarketplaceView("Title");
        model.Show(view);
        model.Send(default(MarketplaceAction));

        Assert.Same(view, model.View);
        Assert.Equal([new UiEvent.Marketplace(default(MarketplaceAction))], sink.Sent);
    }
}
