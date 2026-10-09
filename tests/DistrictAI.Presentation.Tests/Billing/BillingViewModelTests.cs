using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels.Billing;
using Xunit;

namespace DistrictAI.Presentation.Tests.Billing;

// The stub's contract until the area's packet builds it: it keeps the core's
// view, and wraps each action in the area's event.
public sealed class BillingViewModelTests
{
    [Fact]
    public void ItKeepsTheViewAndSendsTheAction()
    {
        var model = new BillingViewModel();
        model.Send(default(BillingAction));

        var (context, sink) = Pages.Context();
        model.Attach(context);
        var view = new BillingView("Title");
        model.Show(view);
        model.Send(default(BillingAction));

        Assert.Same(view, model.View);
        Assert.Equal([new UiEvent.Billing(default(BillingAction))], sink.Sent);
    }
}
