using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels.Settings.Messaging;
using Xunit;

namespace DistrictAI.Presentation.Tests.Settings.Messaging;

// The stub's contract until the area's packet builds it: it keeps the core's
// view, and wraps each action in the area's event.
public sealed class MessagingViewModelTests
{
    [Fact]
    public void ItKeepsTheViewAndSendsTheAction()
    {
        var model = new MessagingViewModel();
        model.Send(default(MessagingAction));

        var (context, sink) = Pages.Context();
        model.Attach(context);
        var view = new MessagingView("Title");
        model.Show(view);
        model.Send(default(MessagingAction));

        Assert.Same(view, model.View);
        Assert.Equal([new UiEvent.Messaging(default(MessagingAction))], sink.Sent);
    }
}
