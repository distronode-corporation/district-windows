using System.Runtime.InteropServices;
using DistrictAI.L10n;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI;

/// <summary>
/// "Send crash reports?": asked once, the first time the window shows, in a
/// build that can send reports and only until the user has answered
/// (<see cref="ViewModels.CrashReports.ShouldAsk"/>), whether the app is new
/// or upgraded from a version that never asked. "Don't send" is the close
/// button, the default (Enter) and what closing the question (Escape) answers;
/// "Send reports" is the primary button. The answer is kept and applied at
/// once, and the account page's switch follows it.
/// </summary>
public sealed partial class MainWindow
{
    private ContentDialog? _crashQuestion;
    private bool _crashQuestionWaits;

    /// <summary>
    /// Asks, if the question is owed. Called each time the window is shown: a
    /// window not laid out yet asks once it is, and while another dialog is
    /// open the question waits for the next time the window is shown.
    /// </summary>
    internal void AskCrashReports()
    {
        if (!_crashReports.ShouldAsk || _crashQuestion is not null)
        {
            return;
        }
        if (Root.XamlRoot is not { } root)
        {
            if (!_crashQuestionWaits)
            {
                _crashQuestionWaits = true;
                Root.Loaded += OnRootLoadedAsk;
            }
            return;
        }
        if (_context.DialogOpen)
        {
            return;
        }
        ShowCrashQuestion(root);
    }

    private void OnRootLoadedAsk(object sender, RoutedEventArgs e)
    {
        Root.Loaded -= OnRootLoadedAsk;
        _crashQuestionWaits = false;
        AskCrashReports();
    }

    private async void ShowCrashQuestion(XamlRoot root)
    {
        var question = new ContentDialog
        {
            XamlRoot = root,
            Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"],
            Title = S.CrashQuestionTitle,
            Content = new TextBlock { Text = S.CrashQuestionBody, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = S.CrashSend,
            CloseButtonText = S.CrashDontSend,
            // Sending is never what Enter does.
            DefaultButton = ContentDialogButton.Close,
        };
        Views.DialogButtons.WrapLabels(question);
        _crashQuestion = question;
        _context.DialogOpen = true;
        ContentDialogResult result;
        try
        {
            result = await question.ShowAsync();
        }
        catch (COMException)
        {
            // Windows had another dialog up after all: unanswered, so asked again
            // the next time the window is shown.
            return;
        }
        finally
        {
            _crashQuestion = null;
            _context.DialogOpen = false;
        }
        _crashReports.Answered(send: result == ContentDialogResult.Primary);
    }
}
