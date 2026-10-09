using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels;
using DistrictAI.ViewModels.Settings;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views.Settings;

/// <summary>
/// The workspace settings hub: the core's sections for the member's role, in
/// its groups, each row opening its section, and the core's note under them.
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

    private void OnRowClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is SettingsRowItem row)
        {
            ViewModel.Open(row);
        }
    }
}
