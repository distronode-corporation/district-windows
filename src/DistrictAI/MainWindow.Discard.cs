using DistrictAI.Core.Ffi;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI;

/// <summary>
/// "Discard your changes?": while the core holds a move away from a settings
/// section with unsaved changes (<see cref="ShellView.Discard"/>), the window
/// asks, in the core's words. Discard sends <see cref="UiEvent.DiscardChanges"/>,
/// and Keep editing (the default, and closing the question) sends
/// <see cref="UiEvent.KeepEditing"/>.
/// </summary>
public sealed partial class MainWindow
{
    private ContentDialog? _discard;
    private bool _discardClosedByCore;

    /// <summary>Opens the question the core is asking, or closes one it has stopped asking.</summary>
    private async void RenderDiscard(DiscardView? discard)
    {
        if (discard is null)
        {
            if (_discard is not null)
            {
                _discardClosedByCore = true;
                _discard.Hide();
            }
            return;
        }
        // Another dialog first: the next snapshot asks again.
        if (_discard is not null || _context.DialogOpen || Content?.XamlRoot is not { } root)
        {
            return;
        }
        var question = new ContentDialog
        {
            XamlRoot = root,
            Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"],
            Title = discard.Title,
            Content = new TextBlock { Text = discard.Body, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = discard.DiscardLabel,
            CloseButtonText = discard.KeepLabel,
            // Losing edits is never what Enter does.
            DefaultButton = ContentDialogButton.Close,
        };
        _discard = question;
        _context.DialogOpen = true;
        ContentDialogResult result;
        try
        {
            result = await question.ShowAsync();
        }
        finally
        {
            _discard = null;
            _context.DialogOpen = false;
        }
        if (_discardClosedByCore)
        {
            _discardClosedByCore = false;
            return;
        }
        _core.Send(result == ContentDialogResult.Primary ? new UiEvent.DiscardChanges() : new UiEvent.KeepEditing());
    }
}
