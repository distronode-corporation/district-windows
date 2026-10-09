using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels.Settings.Directory;
using Xunit;

namespace DistrictAI.Presentation.Tests.Settings.Directory;

// The stub's contract until the area's packet builds it: it keeps the core's
// view, and wraps each action in the area's event.
public sealed class DirectoryViewModelTests
{
    [Fact]
    public void ItKeepsTheViewAndSendsTheAction()
    {
        var model = new DirectoryViewModel();
        model.Send(default(DirectoryAction));

        var (context, sink) = Pages.Context();
        model.Attach(context);
        var view = new DirectoryView("Title");
        model.Show(view);
        model.Send(default(DirectoryAction));

        Assert.Same(view, model.View);
        Assert.Equal([new UiEvent.Directory(default(DirectoryAction))], sink.Sent);
    }
}
