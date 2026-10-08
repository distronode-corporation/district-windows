using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels;
using DistrictAI.Views.Calls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI;

/// <summary>
/// The window's calls: the dialler screen, and over every signed-in screen the
/// banner of a call ringing here and the strip of the call under way.
/// </summary>
public sealed partial class MainWindow
{
    private DialerPage? _dialer;
    private IncomingCallBanner? _ringBanner;
    private CallBar? _callBar;

    /// <summary>
    /// Draws the shell's calls: the ring's banner over the call's strip, in the
    /// call bar's place, so both stay as the member moves about the app. Runs
    /// before the screen, whose pages read <see cref="PageContext.CallsAvailable"/>.
    /// </summary>
    private void RenderCalls(ShellView shell)
    {
        _context.CallsAvailable = shell.CallsAvailable;
        if (_callBar is null || _ringBanner is null)
        {
            _ringBanner = new IncomingCallBanner();
            _ringBanner.Attach(_context);
            _callBar = new CallBar();
            _callBar.Attach(_context);
            CallBarHost.Content = new StackPanel { Children = { _ringBanner, _callBar } };
        }
        _ringBanner.Show(shell);
        _callBar.Show(shell);
    }

    partial void ShowOtherScreen(ScreenView screen, PageContext context, ref UIElement? page)
    {
        if (screen is not ScreenView.Dialer dialer)
        {
            return;
        }
        var arriving = _dialer is null || !ReferenceEquals(PageHost.Content, _dialer);
        _dialer ??= Made(new DialerPage(), p => p.Attach(context));
        _dialer.Show(dialer.View);
        page = _dialer;
        if (arriving)
        {
            // Once it is on screen: the box takes the keyboard, so typing and
            // pasting a number go straight into it.
            var shown = _dialer;
            DispatcherQueue.TryEnqueue(shown.FocusNumber);
        }
    }
}
