using DistrictAI.Core.Ffi;
using DistrictAI.Platform;
using DistrictAI.ViewModels.Scheduling;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views.Scheduling;

/// <summary>
/// An image of a settings dialog (the booking page's logo or banner, the
/// member's picture): whether there is one, Choose image (the Windows file
/// chooser, the core checking what was picked), and Remove, asked once more
/// in place. Each saves at once.
/// </summary>
/// <remarks>
/// Its controls' UI Automation ids name the image: "SettingsImageLogoChoose",
/// "SettingsImageAvatarRemove" and so on.
/// </remarks>
public sealed partial class SettingsImageControl : UserControl
{
    /// <summary>The controls of <paramref name="model"/>'s image.</summary>
    public SettingsImageControl(SettingsImageViewModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        ViewModel = model;
        InitializeComponent();
        var prefix = "SettingsImage" + model.Target;
        AutomationProperties.SetAutomationId(this, prefix);
        AutomationProperties.SetAutomationId(ValueText, prefix + "Value");
        AutomationProperties.SetAutomationId(ChooseButton, prefix + "Choose");
        AutomationProperties.SetAutomationId(RemoveButton, prefix + "Remove");
        AutomationProperties.SetAutomationId(WorkingRing, prefix + "Working");
        AutomationProperties.SetAutomationId(AskTitleText, prefix + "RemoveTitle");
        AutomationProperties.SetAutomationId(ConfirmButton, prefix + "RemoveConfirm");
        AutomationProperties.SetAutomationId(CancelButton, prefix + "RemoveCancel");
        AutomationProperties.SetAutomationId(NoticeBar, prefix + "Notice");
        AutomationProperties.SetAutomationId(FailureBar, prefix + "Failure");
    }

    /// <summary>The image's state and actions.</summary>
    public SettingsImageViewModel ViewModel { get; }

    /// <summary>Opens the file chooser over the window, for one image of the types the scheduler takes.</summary>
    private async void OnChooseClick(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.MayPick || XamlRoot?.ContentIslandEnvironment is not { } island)
        {
            return;
        }
        var picker = new FilePicker(Win32Interop.GetWindowFromWindowId(island.AppWindowId));
        var picked = await picker.PickAsync(DistrictFfi.SchedulingImagePick()).ConfigureAwait(true);
        ViewModel.Picked(picked);
    }
}
