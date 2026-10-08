using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views;

/// <summary>
/// The installations signed in to the account, and signing them out. The core
/// asks before each sign-out; the question is a dialog that lives exactly as
/// long as the core is asking it.
/// </summary>
public sealed partial class DevicesPage : UserControl
{
    private PageContext? _context;
    private ContentDialog? _question;
    private bool _closedByCore;

    /// <summary>A page with nothing shown yet.</summary>
    public DevicesPage()
    {
        InitializeComponent();
        Status.Attach(ViewModel.Load);
        Refresh.Attach(ViewModel.Load);
        Loaded += (_, _) => Ask();
    }

    /// <summary>What the page shows, and its buttons.</summary>
    public DevicesViewModel ViewModel { get; } = new();

    internal void Attach(PageContext context)
    {
        _context = context;
        ViewModel.Attach(context);
    }

    internal void Show(DevicesView view)
    {
        ViewModel.Show(view);
        Ask();
    }

    /// <summary>The page is no longer showing: a question never outlives its screen.</summary>
    internal void Leave() => CloseQuestion();

    /// <summary>Opens the question the core is asking, or closes one it has stopped asking.</summary>
    private async void Ask()
    {
        if (!ViewModel.Confirming)
        {
            CloseQuestion();
            return;
        }
        if (_question is not null || _context is null || _context.DialogOpen || XamlRoot is null)
        {
            return;
        }
        var question = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"],
            Title = "Sign out?",
            Content = new TextBlock { Text = ViewModel.ConfirmQuestion, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = ViewModel.ConfirmAction,
            CloseButtonText = "Cancel",
            // Nothing irreversible happens on Enter.
            DefaultButton = ContentDialogButton.Close,
        };
        _question = question;
        _context.DialogOpen = true;
        ContentDialogResult result;
        try
        {
            result = await question.ShowAsync();
        }
        finally
        {
            _question = null;
            _context.DialogOpen = false;
        }
        if (_closedByCore)
        {
            _closedByCore = false;
            return;
        }
        ViewModel.Answer(result == ContentDialogResult.Primary);
    }

    private void CloseQuestion()
    {
        if (_question is not null)
        {
            _closedByCore = true;
            _question.Hide();
        }
    }

    private void OnSignOutClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is DeviceRowItem row)
        {
            ViewModel.AskSignOut(row);
        }
    }

    private void OnNoticeClosed(InfoBar sender, object args) => ViewModel.DismissNoticesCommand.Execute(null);
}
