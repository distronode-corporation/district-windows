using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels;
using DistrictAI.ViewModels.Settings.Routing;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views.Settings.Routing;

/// <summary>
/// The routing rules section. A stub until the area's packet builds it: until then the core
/// shows the unavailable page instead, so this one is never on screen.
/// </summary>
public sealed partial class RoutingPage : UserControl
{
    /// <summary>A page with nothing shown yet.</summary>
    public RoutingPage()
    {
        InitializeComponent();
    }

    /// <summary>What the page shows, and its actions.</summary>
    public RoutingViewModel ViewModel { get; } = new();

    internal void Attach(PageContext context) => ViewModel.Attach(context);

    internal void Show(RoutingView view) => ViewModel.Show(view);
}
