using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels;
using DistrictAI.Views.Calls;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace DistrictAI;

/// <summary>
/// The window's calls: over every signed-in screen, the banner of a call
/// ringing here and the strip of the call under way. The dialler is a screen,
/// in MainWindow.Screens.cs.
/// </summary>
public sealed partial class MainWindow
{
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

    // Ctrl+D and Ctrl+Shift+H, from anywhere in the window. A key that has no
    // call to act on is left to the focused control.
    private void OnMuteAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) =>
        args.Handled = _callBar?.ViewModel.ToggleMute() ?? false;

    private void OnHangUpAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) =>
        args.Handled = _callBar?.ViewModel.HangUpByKey() ?? false;
}
