using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels;
using DistrictAI.ViewModels.Workflows;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views.Workflows;

/// <summary>
/// The workflows. A stub until the area's packet builds it: until then the core
/// shows the unavailable page instead, so this one is never on screen.
/// </summary>
public sealed partial class WorkflowsPage : UserControl
{
    /// <summary>A page with nothing shown yet.</summary>
    public WorkflowsPage()
    {
        InitializeComponent();
    }

    /// <summary>What the page shows, and its actions.</summary>
    public WorkflowsViewModel ViewModel { get; } = new();

    internal void Attach(PageContext context) => ViewModel.Attach(context);

    internal void Show(WorkflowsView view) => ViewModel.Show(view);
}
