using DistrictAI.ViewModels.Scheduling;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views.Scheduling;

/// <summary>
/// The booking page: its business name, the logo's height and opacity, the
/// banner's opacity, the privacy and terms links, the language it falls back
/// to, and its logo and banner, which save at once.
/// </summary>
public sealed partial class SettingsBrandingContent : UserControl
{
    /// <summary>The fields of <paramref name="model"/>'s dialog.</summary>
    public SettingsBrandingContent(SettingsBrandingViewModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        ViewModel = model;
        InitializeComponent();
        LogoHost.Child = new SettingsImageControl(model.Logo);
        BannerHost.Child = new SettingsImageControl(model.Banner);
    }

    /// <summary>The dialog's fields.</summary>
    public SettingsBrandingViewModel ViewModel { get; }

    private void OnLocaleChanged(object sender, SelectionChangedEventArgs e) => ViewModel.PickLocale(LocaleChooser.SelectedIndex);
}
