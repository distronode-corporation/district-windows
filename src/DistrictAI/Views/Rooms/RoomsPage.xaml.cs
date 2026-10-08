using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels;
using DistrictAI.ViewModels.Rooms;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views.Rooms;

/// <summary>
/// The meeting rooms. A stub until the area's packet builds it: until then the core
/// shows the unavailable page instead, so this one is never on screen.
/// </summary>
public sealed partial class RoomsPage : UserControl
{
    /// <summary>A page with nothing shown yet.</summary>
    public RoomsPage()
    {
        InitializeComponent();
    }

    /// <summary>What the page shows, and its actions.</summary>
    public RoomsViewModel ViewModel { get; } = new();

    internal void Attach(PageContext context) => ViewModel.Attach(context);

    internal void Show(RoomsView view) => ViewModel.Show(view);
}
