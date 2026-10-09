using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels.Settings.Persona;
using Xunit;

namespace DistrictAI.Presentation.Tests.Settings.Persona;

// The stub's contract until the area's packet builds it: it keeps the core's
// view, and wraps each action in the area's event.
public sealed class PersonaViewModelTests
{
    [Fact]
    public void ItKeepsTheViewAndSendsTheAction()
    {
        var model = new PersonaViewModel();
        model.Send(default(PersonaAction));

        var (context, sink) = Pages.Context();
        model.Attach(context);
        var view = new PersonaView("Title");
        model.Show(view);
        model.Send(default(PersonaAction));

        Assert.Same(view, model.View);
        Assert.Equal([new UiEvent.Persona(default(PersonaAction))], sink.Sent);
    }
}
