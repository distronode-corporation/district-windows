using CommunityToolkit.Mvvm.ComponentModel;
using DistrictAI.Core.Ffi;

namespace DistrictAI.ViewModels.Settings.Persona;

/// <summary>The persona section: what the page shows, and what it sends. A stub until the area's packet builds it.</summary>
public sealed partial class PersonaViewModel : ObservableObject
{
    private PageContext? _context;

    /// <summary>The core's view of the screen.</summary>
    [ObservableProperty]
    public partial PersonaView? View { get; set; }

    internal void Attach(PageContext context) => _context = context;

    internal void Show(PersonaView view) => View = view;

    /// <summary>Forwards one of the screen's actions.</summary>
    internal void Send(PersonaAction action) => _context?.Send(new UiEvent.Persona(action));
}
