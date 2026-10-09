using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels;
using DistrictAI.ViewModels.Settings.Directory;
using DistrictAI.Views.Contacts;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace DistrictAI.Views.Settings.Directory;

/// <summary>
/// The transfer directory: each entry's name and number, someone to add (a
/// name and a number both needed), and Save, which replaces the whole
/// directory and so asks first in the core's words; the question is a dialog
/// that lives exactly as long as the core asks it. Leaving with a change not
/// saved asks first: the window's "Discard your changes?" (the settings kit).
/// </summary>
public sealed partial class DirectoryPage : UserControl
{
    private readonly CoreQuestion _question = new();
    private PageContext? _context;

    /// <summary>A page with nothing shown yet.</summary>
    public DirectoryPage()
    {
        InitializeComponent();
        Loaded += (_, _) => Ask();
        // A question never outlives its page.
        Unloaded += (_, _) => _question.Close();
    }

    /// <summary>What the page shows, and its actions.</summary>
    public DirectoryViewModel ViewModel { get; } = new();

    internal void Attach(PageContext context)
    {
        _context = context;
        ViewModel.Attach(context);
    }

    internal void Show(DirectoryView view)
    {
        ViewModel.Show(view);
        Ask();
    }

    private void Ask()
    {
        var asked = ViewModel.Confirming;
        _question.Sync(
            this,
            _context,
            asked is not null,
            asked?.Title ?? string.Empty,
            asked?.Body ?? string.Empty,
            asked?.Action ?? string.Empty,
            asked?.Destructive ?? false,
            ViewModel.Answer);
    }

    /// <summary>Enter in the new number adds the entry, as on Linux.</summary>
    private void OnNewNumberKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && ViewModel.AddCommand.CanExecute(null))
        {
            ViewModel.AddCommand.Execute(null);
            e.Handled = true;
        }
    }
}
