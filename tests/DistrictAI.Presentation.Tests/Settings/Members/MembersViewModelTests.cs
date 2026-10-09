using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels.Settings.Members;
using Xunit;

namespace DistrictAI.Presentation.Tests.Settings.Members;

// The stub's contract until the area's packet builds it: it keeps the core's
// view, and wraps each action in the area's event.
public sealed class MembersViewModelTests
{
    [Fact]
    public void ItKeepsTheViewAndSendsTheAction()
    {
        var model = new MembersViewModel();
        model.Send(default(MembersAction));

        var (context, sink) = Pages.Context();
        model.Attach(context);
        var view = new MembersView("Title");
        model.Show(view);
        model.Send(default(MembersAction));

        Assert.Same(view, model.View);
        Assert.Equal([new UiEvent.Members(default(MembersAction))], sink.Sent);
    }
}
