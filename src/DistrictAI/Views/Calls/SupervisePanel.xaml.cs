using DistrictAI.ViewModels.Calls;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views.Calls;

/// <summary>
/// Supervising a live call, on its page. What it shows, and what it
/// announces, is <see cref="SuperviseViewModel"/>'s; this only speaks.
/// </summary>
public sealed partial class SupervisePanel : UserControl
{
    private SuperviseViewModel _viewModel = new();

    /// <summary>A card with nothing shown.</summary>
    public SupervisePanel()
    {
        InitializeComponent();
        _viewModel.Announced += OnAnnounced;
    }

    /// <summary>What the card shows.</summary>
    public SuperviseViewModel ViewModel => _viewModel;

    /// <summary>Shows <paramref name="viewModel"/>: the call page's card.</summary>
    internal void Attach(SuperviseViewModel viewModel)
    {
        _viewModel.Announced -= OnAnnounced;
        _viewModel = viewModel;
        _viewModel.Announced += OnAnnounced;
        Bindings.Update();
    }

    // Read out politely: the live region's text changes, and Narrator is told so.
    private void OnAnnounced(object? sender, string line)
    {
        Announcer.Text = line;
        FrameworkElementAutomationPeer.CreatePeerForElement(Announcer)
            ?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }
}
