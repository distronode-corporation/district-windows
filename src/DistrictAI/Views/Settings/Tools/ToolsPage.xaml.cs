using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels;
using DistrictAI.ViewModels.Settings.Tools;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views.Settings.Tools;

/// <summary>
/// The Skills section. A stub until the area's packet builds it: until then the core
/// shows the unavailable page instead, so this one is never on screen.
/// </summary>
public sealed partial class ToolsPage : UserControl
{
    /// <summary>A page with nothing shown yet.</summary>
    public ToolsPage()
    {
        InitializeComponent();
    }

    /// <summary>What the page shows, and its actions.</summary>
    public ToolsViewModel ViewModel { get; } = new();

    internal void Attach(PageContext context) => ViewModel.Attach(context);

    internal void Show(ToolsView view) => ViewModel.Show(view);
}
