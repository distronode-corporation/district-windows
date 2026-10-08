using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels;
using DistrictAI.ViewModels.Settings.Directory;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views.Settings.Directory;

/// <summary>
/// The transfer directory section. A stub until the area's packet builds it: until then the core
/// shows the unavailable page instead, so this one is never on screen.
/// </summary>
public sealed partial class DirectoryPage : UserControl
{
    /// <summary>A page with nothing shown yet.</summary>
    public DirectoryPage()
    {
        InitializeComponent();
    }

    /// <summary>What the page shows, and its actions.</summary>
    public DirectoryViewModel ViewModel { get; } = new();

    internal void Attach(PageContext context) => ViewModel.Attach(context);

    internal void Show(DirectoryView view) => ViewModel.Show(view);
}
