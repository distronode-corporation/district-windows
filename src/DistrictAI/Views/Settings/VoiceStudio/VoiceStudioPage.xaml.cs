using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels;
using DistrictAI.ViewModels.Settings.VoiceStudio;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views.Settings.VoiceStudio;

/// <summary>
/// The Voice section. A stub until the area's packet builds it: until then the core
/// shows the unavailable page instead, so this one is never on screen.
/// </summary>
public sealed partial class VoiceStudioPage : UserControl
{
    /// <summary>A page with nothing shown yet.</summary>
    public VoiceStudioPage()
    {
        InitializeComponent();
    }

    /// <summary>What the page shows, and its actions.</summary>
    public VoiceStudioViewModel ViewModel { get; } = new();

    internal void Attach(PageContext context) => ViewModel.Attach(context);

    internal void Show(VoiceStudioView view) => ViewModel.Show(view);
}
