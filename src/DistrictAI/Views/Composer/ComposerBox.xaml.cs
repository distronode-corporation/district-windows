using DistrictAI.Core.Ffi;
using DistrictAI.Platform;
using DistrictAI.ViewModels;
using DistrictAI.ViewModels.Composer;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.UI.Core;

namespace DistrictAI.Views.Composer;

/// <summary>
/// The reply box under a conversation: the text, the images with their remove
/// buttons, Attach, the AI button and its note, Send (and Ctrl+Enter), the
/// busy line and the failure line. Everything it shows is the view model's.
/// </summary>
public sealed partial class ComposerBox : UserControl
{
    private PageContext? _context;

    /// <summary>A box showing nothing until <see cref="Attach"/>.</summary>
    public ComposerBox()
    {
        InitializeComponent();
    }

    /// <summary>What the box shows, and its buttons.</summary>
    public ComposerViewModel ViewModel { get; private set; } = new();

    /// <summary>Shows <paramref name="viewModel"/>, the thread page's own, from now on.</summary>
    internal void Attach(ComposerViewModel viewModel, PageContext context)
    {
        ViewModel = viewModel;
        _context = context;
        Bindings.Update();
    }

    /// <summary>
    /// Ctrl+Enter sends; Enter alone is a new line. Taken before the box sees
    /// the key, which would otherwise put a new line in.
    /// </summary>
    private void OnBoxPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter
            || !InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down))
        {
            return;
        }
        e.Handled = true;
        if (ViewModel.SendCommand.CanExecute(null))
        {
            ViewModel.SendCommand.Execute(null);
        }
    }

    /// <summary>Opens the file chooser over the window, for one image, and hands what was picked to the box it was picked for.</summary>
    private async void OnAttachClick(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.MayAttach || XamlRoot?.ContentIslandEnvironment is not { } island)
        {
            return;
        }
        var threadKey = ViewModel.ThreadKey;
        var picker = new FilePicker(Win32Interop.GetWindowFromWindowId(island.AppWindowId));
        var picked = await picker.PickAsync(DistrictFfi.ComposerPick()).ConfigureAwait(true);
        ViewModel.Attached(threadKey, picked);
        Box.Focus(FocusState.Programmatic);
    }

    private void OnRemoveAttachmentClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is AttachmentChip chip)
        {
            ViewModel.RemoveAttachmentCommand.Execute(chip);
            Box.Focus(FocusState.Programmatic);
        }
    }

    private async void OnReportDraftClick(object sender, RoutedEventArgs e)
    {
        if (_context is null || XamlRoot is null)
        {
            return;
        }
        await ReportDialog.OfferAsync(XamlRoot, _context, ViewModel.DraftReport, ViewModel.DraftTarget).ConfigureAwait(true);
    }
}
