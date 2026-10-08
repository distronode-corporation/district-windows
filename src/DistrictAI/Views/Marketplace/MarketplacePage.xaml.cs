using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels;
using DistrictAI.ViewModels.Marketplace;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views.Marketplace;

/// <summary>
/// The phone numbers. A stub until the area's packet builds it: until then the core
/// shows the unavailable page instead, so this one is never on screen.
/// </summary>
public sealed partial class MarketplacePage : UserControl
{
    /// <summary>A page with nothing shown yet.</summary>
    public MarketplacePage()
    {
        InitializeComponent();
    }

    /// <summary>What the page shows, and its actions.</summary>
    public MarketplaceViewModel ViewModel { get; } = new();

    internal void Attach(PageContext context) => ViewModel.Attach(context);

    internal void Show(MarketplaceView view) => ViewModel.Show(view);
}
