using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels;
using DistrictAI.ViewModels.Settings;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views.Settings;

/// <summary>
/// The workspace settings hub. A stub until the area's packet builds it: until then the core
/// shows the unavailable page instead, so this one is never on screen.
/// </summary>
public sealed partial class SettingsHubPage : UserControl
{
    /// <summary>A page with nothing shown yet.</summary>
    public SettingsHubPage()
    {
        InitializeComponent();
    }

    /// <summary>What the page shows, and its actions.</summary>
    public SettingsHubViewModel ViewModel { get; } = new();

    internal void Attach(PageContext context) => ViewModel.Attach(context);

    internal void Show(SettingsHubView view) => ViewModel.Show(view);
}
