using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels;
using DistrictAI.ViewModels.Blocked;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views.Blocked;

/// <summary>
/// The blocked callers. A stub until the area's packet builds it: until then the core
/// shows the unavailable page instead, so this one is never on screen.
/// </summary>
public sealed partial class BlockedPage : UserControl
{
    /// <summary>A page with nothing shown yet.</summary>
    public BlockedPage()
    {
        InitializeComponent();
    }

    /// <summary>What the page shows, and its actions.</summary>
    public BlockedViewModel ViewModel { get; } = new();

    internal void Attach(PageContext context) => ViewModel.Attach(context);

    internal void Show(BlockedView view) => ViewModel.Show(view);
}
