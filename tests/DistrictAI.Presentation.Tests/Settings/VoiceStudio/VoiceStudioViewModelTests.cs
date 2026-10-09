using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels.Settings.VoiceStudio;
using Xunit;

namespace DistrictAI.Presentation.Tests.Settings.VoiceStudio;

// The stub's contract until the area's packet builds it: it keeps the core's
// view, and wraps each action in the area's event.
public sealed class VoiceStudioViewModelTests
{
    [Fact]
    public void ItKeepsTheViewAndSendsTheAction()
    {
        var model = new VoiceStudioViewModel();
        model.Send(default(VoiceStudioAction));

        var (context, sink) = Pages.Context();
        model.Attach(context);
        var view = new VoiceStudioView("Title");
        model.Show(view);
        model.Send(default(VoiceStudioAction));

        Assert.Same(view, model.View);
        Assert.Equal([new UiEvent.VoiceStudio(default(VoiceStudioAction))], sink.Sent);
    }
}
