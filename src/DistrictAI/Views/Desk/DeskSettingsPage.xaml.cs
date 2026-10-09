using DistrictAI.Core.Ffi;
using DistrictAI.Platform;
using DistrictAI.ViewModels;
using DistrictAI.ViewModels.Desk;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views.Desk;

/// <summary>
/// The help desk's settings: whether it takes tickets, whether customers are
/// emailed replies, the name customers see, and the logo, picked in the
/// Windows file chooser and checked before it is sent (a GIF, or a file over
/// the size limit, is refused with a sentence saying why).
/// </summary>
public sealed partial class DeskSettingsPage : UserControl
{
    /// <summary>A page with nothing shown yet.</summary>
    public DeskSettingsPage()
    {
        InitializeComponent();
        Status.Attach(ViewModel.Load);
    }

    /// <summary>What the page shows, and its actions.</summary>
    public DeskSettingsViewModel ViewModel { get; } = new();

    internal void Attach(PageContext context) => ViewModel.Attach(context);

    internal void Show(DeskSettingsView view) => ViewModel.Show(view);

    /// <summary>Opens the file chooser over the window, for one image of the types the service hosts as a logo.</summary>
    private async void OnChooseLogoClick(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.MayPickLogo || XamlRoot?.ContentIslandEnvironment is not { } island)
        {
            return;
        }
        var picker = new FilePicker(Win32Interop.GetWindowFromWindowId(island.AppWindowId));
        var picked = await picker.PickAsync(DistrictFfi.LogoPick()).ConfigureAwait(true);
        ViewModel.PickedLogo(picked);
    }
}
