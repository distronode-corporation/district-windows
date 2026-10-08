using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels;
using DistrictAI.ViewModels.Billing;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views.Billing;

/// <summary>
/// Billing. A stub until the area's packet builds it: until then the core
/// shows the unavailable page instead, so this one is never on screen.
/// </summary>
public sealed partial class BillingPage : UserControl
{
    /// <summary>A page with nothing shown yet.</summary>
    public BillingPage()
    {
        InitializeComponent();
    }

    /// <summary>What the page shows, and its actions.</summary>
    public BillingViewModel ViewModel { get; } = new();

    internal void Attach(PageContext context) => ViewModel.Attach(context);

    internal void Show(BillingView view) => ViewModel.Show(view);
}
