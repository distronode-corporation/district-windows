using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels;
using DistrictAI.ViewModels.Support;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views.Support;

/// <summary>
/// One support request. A stub until the area's packet builds it: until then the core
/// shows the unavailable page instead, so this one is never on screen.
/// </summary>
public sealed partial class SupportRequestPage : UserControl
{
    /// <summary>A page with nothing shown yet.</summary>
    public SupportRequestPage()
    {
        InitializeComponent();
    }

    /// <summary>What the page shows, and its actions.</summary>
    public SupportRequestViewModel ViewModel { get; } = new();

    internal void Attach(PageContext context) => ViewModel.Attach(context);

    internal void Show(SupportRequestView view) => ViewModel.Show(view);
}
