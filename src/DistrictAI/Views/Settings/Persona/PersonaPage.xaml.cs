using System.ComponentModel;
using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels;
using DistrictAI.ViewModels.Settings.Persona;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views.Settings.Persona;

/// <summary>
/// District Studio's Persona: the receptionist's texts, its language and
/// answer length, Save and its notice, and the audition dialog, which says
/// the audition is a real, billed call before Start places it.
/// </summary>
public sealed partial class PersonaPage : UserControl
{
    private PageContext? _context;
    private bool _closedByCore;
    private bool _showing;

    /// <summary>A page with nothing shown yet.</summary>
    public PersonaPage()
    {
        InitializeComponent();
        ViewModel.PropertyChanged += OnViewModelChanged;
    }

    /// <summary>What the page shows, and its actions.</summary>
    public PersonaViewModel ViewModel { get; } = new();

    /// <summary>The save notice's look: a save, or a failure.</summary>
    public static InfoBarSeverity SeverityFor(bool saved) => saved ? InfoBarSeverity.Success : InfoBarSeverity.Error;

    internal void Attach(PageContext context)
    {
        _context = context;
        ViewModel.Attach(context);
    }

    internal void Show(PersonaView view) => ViewModel.Show(view);

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PersonaViewModel.AuditionOpen))
        {
            ShowAudition();
        }
    }

    /// <summary>Opens the audition dialog the core holds open, or closes one it has closed.</summary>
    private async void ShowAudition()
    {
        if (!ViewModel.AuditionOpen)
        {
            if (_showing)
            {
                _closedByCore = true;
                Audition.Hide();
            }
            return;
        }
        if (_showing || _context is null || _context.DialogOpen || XamlRoot is null)
        {
            return;
        }
        Audition.XamlRoot = XamlRoot;
        _showing = true;
        _context.DialogOpen = true;
        try
        {
            _ = await Audition.ShowAsync();
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
        // Closed by the member: any audition stops, and its room is left.
        ViewModel.ClosePreview();
    }
}
