using DistrictAI.ViewModels;
using DistrictAI.ViewModels.Scheduling;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views.Scheduling;

/// <summary>
/// The settings: the four tabs, what the tab showing read, and its Edit; and
/// their four dialogs (the booking page, the assistant, the profile, the
/// notifications), each one the core holds open
/// (<see cref="SchedulingDialogHost"/>).
/// </summary>
public sealed partial class SchedulingSettingsPanel : UserControl
{
    private readonly SchedulingDialogHost _branding = new("SettingsBrandingDialog");
    private readonly SchedulingDialogHost _assistant = new("SettingsAssistantDialog");
    private readonly SchedulingDialogHost _profile = new("SettingsProfileDialog");
    private readonly SchedulingDialogHost _notifications = new("SettingsNotificationsDialog");
    private PageContext? _context;

    /// <summary>A panel showing nothing until <see cref="Attach"/>.</summary>
    public SchedulingSettingsPanel()
    {
        InitializeComponent();
        Status.Attach(ViewModel.Load);
        Loaded += (_, _) => SyncDialogs();
    }

    /// <summary>What the panel shows, and its actions.</summary>
    public SchedulingSettingsViewModel ViewModel { get; private set; } = new();

    /// <summary>Shows <paramref name="model"/>, the page's own, from now on.</summary>
    internal void Attach(SchedulingSettingsViewModel model, PageContext context)
    {
        ViewModel = model;
        _context = context;
        Status.Attach(model.Load);
        Bindings.Update();
    }

    /// <summary>Opens the dialog the core holds open, and closes the ones it does not.</summary>
    internal void SyncDialogs()
    {
        _branding.Sync(this, _context, ViewModel.Branding.Dialog, () => new SettingsBrandingContent(ViewModel.Branding));
        _assistant.Sync(this, _context, ViewModel.Assistant.Dialog, () => new SettingsAssistantContent(ViewModel.Assistant));
        _profile.Sync(this, _context, ViewModel.Profile.Dialog, () => new SettingsProfileContent(ViewModel.Profile));
        _notifications.Sync(this, _context, ViewModel.Notifications.Dialog, () => new SettingsNotificationsContent(ViewModel.Notifications));
    }

    /// <summary>Closes every dialog without asking the core: the page left the screen.</summary>
    internal void CloseDialogs()
    {
        _branding.Close();
        _assistant.Close();
        _profile.Close();
        _notifications.Close();
    }

    private void OnTabChanged(object sender, SelectionChangedEventArgs e) =>
        ViewModel.SelectTab(TabChooser.SelectedIndex);
}
