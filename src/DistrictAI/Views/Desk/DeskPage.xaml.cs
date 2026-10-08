using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels;
using DistrictAI.ViewModels.Desk;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views.Desk;

/// <summary>
/// The help desk. A stub until the area's packet builds it: until then the core
/// shows the unavailable page instead, so this one is never on screen.
/// </summary>
public sealed partial class DeskPage : UserControl
{
    /// <summary>A page with nothing shown yet.</summary>
    public DeskPage()
    {
        InitializeComponent();
    }

    /// <summary>What the page shows, and its actions.</summary>
    public DeskViewModel ViewModel { get; } = new();

    internal void Attach(PageContext context) => ViewModel.Attach(context);

    internal void Show(DeskView view) => ViewModel.Show(view);
}
