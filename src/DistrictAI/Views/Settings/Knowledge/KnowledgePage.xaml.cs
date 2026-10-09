using System.ComponentModel;
using DistrictAI.Core.Ffi;
using DistrictAI.Platform;
using DistrictAI.ViewModels;
using DistrictAI.ViewModels.Settings.Knowledge;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views.Settings.Knowledge;

/// <summary>
/// District Studio's Knowledge: where answers come from, adding a document
/// (typed, or read from a plain text file picked in the Windows file chooser,
/// and sent only by Add), and the documents. Deleting one, and switching to the
/// linked mode, ask first in a dialog, in the core's words.
/// </summary>
public sealed partial class KnowledgePage : UserControl
{
    private PageContext? _context;
    private bool _closedByCore;
    private bool _showing;

    /// <summary>A page with nothing shown yet.</summary>
    public KnowledgePage()
    {
        InitializeComponent();
        ViewModel.PropertyChanged += OnViewModelChanged;
        ViewModel.FileRequested += OnFileRequested;
    }

    /// <summary>What the page shows, and its actions.</summary>
    public KnowledgeViewModel ViewModel { get; } = new();

    /// <summary>The notice's look: a success, or a failure.</summary>
    public static InfoBarSeverity SeverityFor(bool saved) => saved ? InfoBarSeverity.Success : InfoBarSeverity.Error;

    internal void Attach(PageContext context)
    {
        _context = context;
        ViewModel.Attach(context);
    }

    internal void Show(KnowledgeView view) => ViewModel.Show(view);

    /// <summary>Opens the file chooser over the window, for one plain text file.</summary>
    private async void OnFileRequested(object? sender, EventArgs e)
    {
        if (!ViewModel.MayPickFile || XamlRoot?.ContentIslandEnvironment is not { } island)
        {
            return;
        }
        var picker = new FilePicker(Win32Interop.GetWindowFromWindowId(island.AppWindowId));
        var picked = await picker.PickAsync(DistrictFfi.KnowledgeFilePick()).ConfigureAwait(true);
        ViewModel.FilePicked(picked);
    }

    private void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is DocumentItem document)
        {
            ViewModel.AskDelete(document.Id);
        }
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(KnowledgeViewModel.ConfirmOpen))
        {
            ShowQuestion();
        }
    }

    /// <summary>Opens the question the core holds open, or closes one it has closed.</summary>
    private async void ShowQuestion()
    {
        if (!ViewModel.ConfirmOpen)
        {
            if (_showing)
            {
                _closedByCore = true;
                Question.Hide();
            }
            return;
        }
        if (_showing || _context is null || _context.DialogOpen || XamlRoot is null)
        {
            return;
        }
        Question.XamlRoot = XamlRoot;
        _showing = true;
        _context.DialogOpen = true;
        ContentDialogResult result;
        try
        {
            result = await Question.ShowAsync();
        }
        finally
        {
            _showing = false;
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
