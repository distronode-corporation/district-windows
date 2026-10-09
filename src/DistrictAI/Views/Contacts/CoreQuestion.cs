using DistrictAI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views.Contacts;

/// <summary>
/// A question the core asks before a change (deleting a contact, clearing its
/// research, blocking or unblocking a caller), as a dialog that lives exactly
/// as long as the core asks it: the pattern of the devices page.
/// </summary>
internal sealed class CoreQuestion
{
    private ContentDialog? _question;
    private bool _closedByCore;

    /// <summary>
    /// Opens the question when <paramref name="asking"/> and none is open, or
    /// closes the open one when the core stopped asking. The answer goes to
    /// <paramref name="answer"/>, unless the core closed the dialog first.
    /// </summary>
    internal async void Sync(
        FrameworkElement owner,
        PageContext? context,
        bool asking,
        string title,
        string question,
        string action,
        bool destructive,
        Action<bool> answer)
    {
        if (!asking)
        {
            Close();
            return;
        }
        if (_question is not null || context is null || context.DialogOpen || owner.XamlRoot is null)
        {
            return;
        }
        var dialog = new ContentDialog
        {
            XamlRoot = owner.XamlRoot,
            Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"],
            Title = title,
            Content = new TextBlock { Text = question, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = action,
            CloseButtonText = "Cancel",
            // Nothing that removes or hides anything happens on Enter.
            DefaultButton = destructive ? ContentDialogButton.Close : ContentDialogButton.Primary,
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(dialog, title);
        _question = dialog;
        context.DialogOpen = true;
        ContentDialogResult result;
        try
        {
            result = await dialog.ShowAsync();
        }
        finally
        {
            _question = null;
            context.DialogOpen = false;
        }
        if (_closedByCore)
        {
            _closedByCore = false;
            return;
        }
        answer(result == ContentDialogResult.Primary);
    }

    /// <summary>Closes the open question without answering it.</summary>
    internal void Close()
    {
        if (_question is not null)
        {
            _closedByCore = true;
            _question.Hide();
        }
    }
}
