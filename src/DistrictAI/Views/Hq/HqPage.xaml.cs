using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels;
using DistrictAI.ViewModels.Hq;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views.Hq;

/// <summary>
/// District HQ. A stub until the area's packet builds it: until then the core
/// shows the unavailable page instead, so this one is never on screen.
/// </summary>
public sealed partial class HqPage : UserControl
{
    /// <summary>A page with nothing shown yet.</summary>
    public HqPage()
    {
        InitializeComponent();
    }

    /// <summary>What the page shows, and its actions.</summary>
    public HqViewModel ViewModel { get; } = new();

    internal void Attach(PageContext context) => ViewModel.Attach(context);

    internal void Show(HqView view) => ViewModel.Show(view);
}
