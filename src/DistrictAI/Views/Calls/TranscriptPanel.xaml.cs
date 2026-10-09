using DistrictAI.ViewModels.Calls;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views.Calls;

/// <summary>
/// The call's live transcript, under the call strip. What it shows, and when
/// it follows the newest line or announces one, is
/// <see cref="TranscriptViewModel"/>'s; this only scrolls and speaks.
/// </summary>
public sealed partial class TranscriptPanel : UserControl
{
    /// <summary>How near the end, in pixels, still counts as at the end.</summary>
    private const double EndSlack = 8;

    private TranscriptViewModel _viewModel = new();

    /// <summary>A panel with no transcript.</summary>
    public TranscriptPanel()
    {
        InitializeComponent();
        Bind(_viewModel);
    }

    /// <summary>What the panel shows.</summary>
    public TranscriptViewModel ViewModel => _viewModel;

    /// <summary>Shows <paramref name="viewModel"/>: the call strip's transcript.</summary>
    internal void Attach(TranscriptViewModel viewModel)
    {
        _viewModel.FollowRequested -= OnFollowRequested;
        _viewModel.Announced -= OnAnnounced;
        _viewModel = viewModel;
        Bind(viewModel);
        Bindings.Update();
    }

    private void Bind(TranscriptViewModel viewModel)
    {
        viewModel.FollowRequested += OnFollowRequested;
        viewModel.Announced += OnAnnounced;
    }

    // Once the new lines are laid out, to the end.
    private void OnFollowRequested(object? sender, EventArgs e) =>
        DispatcherQueue.TryEnqueue(() =>
        {
            LinesScroller.UpdateLayout();
            _ = LinesScroller.ChangeView(null, LinesScroller.ScrollableHeight, null, disableAnimation: true);
        });

    // Where the member is: at the end, the panel follows; scrolled up, it stays.
    private void OnLinesScrolled(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (!e.IsIntermediate)
        {
            _viewModel.Scrolled(LinesScroller.VerticalOffset >= LinesScroller.ScrollableHeight - EndSlack);
        }
    }

    // A final line, read out politely: the live region's text changes, and
    // Narrator is told so.
    private void OnAnnounced(object? sender, string line)
    {
        Announcer.Text = line;
        FrameworkElementAutomationPeer.CreatePeerForElement(Announcer)
            ?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }
}
