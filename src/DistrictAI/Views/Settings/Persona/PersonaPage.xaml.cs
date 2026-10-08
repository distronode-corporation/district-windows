using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels;
using DistrictAI.ViewModels.Settings.Persona;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views.Settings.Persona;

/// <summary>
/// The persona section. A stub until the area's packet builds it: until then the core
/// shows the unavailable page instead, so this one is never on screen.
/// </summary>
public sealed partial class PersonaPage : UserControl
{
    /// <summary>A page with nothing shown yet.</summary>
    public PersonaPage()
    {
        InitializeComponent();
    }

    /// <summary>What the page shows, and its actions.</summary>
    public PersonaViewModel ViewModel { get; } = new();

    internal void Attach(PageContext context) => ViewModel.Attach(context);

    internal void Show(PersonaView view) => ViewModel.Show(view);
}
