using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels;
using DistrictAI.ViewModels.Calls;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views.Calls;

/// <summary>
/// The banner of a call ringing here, with Answer and Decline. Shown while
/// <see cref="ShellView.Ring"/> is set; everything on it is the core's
/// <see cref="IncomingRingView"/>. A screen reader is told the moment a call
/// starts ringing, interrupting whatever it was reading.
/// </summary>
public sealed partial class IncomingCallBanner : UserControl
{
    /// <summary>A banner with nothing ringing.</summary>
    public IncomingCallBanner()
    {
        InitializeComponent();
    }

    /// <summary>What the banner shows, and its buttons' commands.</summary>
    public IncomingCallViewModel ViewModel { get; } = new();

    internal void Attach(PageContext context) => ViewModel.Attach(context);

    internal void Show(ShellView shell)
    {
        if (!ViewModel.Show(shell.Ring))
        {
            return;
        }
        // x:Bind writes the new name when the view model changes it, which has
        // happened by now; the event makes Narrator read it.
        var peer = FrameworkElementAutomationPeer.FromElement(Announcer)
            ?? FrameworkElementAutomationPeer.CreatePeerForElement(Announcer);
        peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }
}
