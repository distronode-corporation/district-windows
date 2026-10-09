using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels;
using DistrictAI.ViewModels.Support;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views.Support;

/// <summary>
/// One support request: where it stands, the conversation with Distronode,
/// the reply, and marking it resolved, which asks first.
/// </summary>
public sealed partial class SupportRequestPage : UserControl
{
    /// <summary>A page with nothing shown yet.</summary>
    public SupportRequestPage()
    {
        InitializeComponent();
        Status.Attach(ViewModel.Load);
        Refresh.Attach(ViewModel.Load);
    }

    /// <summary>What the page shows, and its actions.</summary>
    public SupportRequestViewModel ViewModel { get; } = new();

    internal void Attach(PageContext context) => ViewModel.Attach(context);

    internal void Show(SupportRequestView view) => ViewModel.Show(view);
}
