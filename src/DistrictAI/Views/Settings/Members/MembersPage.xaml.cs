using System.ComponentModel;
using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels;
using DistrictAI.ViewModels.Settings.Members;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views.Settings.Members;

/// <summary>
/// The members and their roles, and the workspace's name. Removing a member
/// asks first, in the core's words; Cancel is the default.
/// </summary>
public sealed partial class MembersPage : UserControl
{
    private PageContext? _context;
    private ContentDialog? _question;
    private bool _closedByCore;

    /// <summary>A page with nothing shown yet.</summary>
    public MembersPage()
    {
        InitializeComponent();
        ViewModel.PropertyChanged += OnViewModelChanged;
    }

    /// <summary>What the page shows, and its actions.</summary>
    public MembersViewModel ViewModel { get; } = new();

    /// <summary>The notice's look: a change made, or refused.</summary>
    public static InfoBarSeverity SeverityFor(bool saved) => saved ? InfoBarSeverity.Success : InfoBarSeverity.Error;

    internal void Attach(PageContext context)
    {
        _context = context;
        ViewModel.Attach(context);
    }

    internal void Show(MembersView view) => ViewModel.Show(view);

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MembersViewModel.Question))
        {
            Ask();
        }
    }

    /// <summary>Opens the question the core is asking, or closes one it has stopped asking.</summary>
    private async void Ask()
    {
        var question = ViewModel.Question;
        if (question is null)
        {
            if (_question is not null)
            {
                _closedByCore = true;
                _question.Hide();
            }
            return;
        }
        if (_question is not null || _context is null || _context.DialogOpen || XamlRoot is null)
        {
            return;
        }
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"],
            Title = question.Title,
            Content = new TextBlock { Text = question.Body, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = question.RemoveLabel,
            CloseButtonText = question.CancelLabel,
            // Removing someone is never what Enter does.
            DefaultButton = ContentDialogButton.Close,
        };
        _question = dialog;
        _context.DialogOpen = true;
        ContentDialogResult result;
        try
        {
            result = await dialog.ShowAsync();
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
}
