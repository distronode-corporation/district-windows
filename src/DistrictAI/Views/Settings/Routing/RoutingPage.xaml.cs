using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels;
using DistrictAI.ViewModels.Settings.Routing;
using DistrictAI.Views.Contacts;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views.Settings.Routing;

/// <summary>
/// The routing rules: a card per rule with the web console's choices, its
/// engine shown and not changed, Add, and Save, which replaces every rule and
/// so asks first in the core's words; the question is a dialog that lives
/// exactly as long as the core asks it. Leaving with a change not saved asks
/// first: the window's "Discard your changes?" (the settings kit).
/// </summary>
public sealed partial class RoutingPage : UserControl
{
    private readonly CoreQuestion _question = new();
    private PageContext? _context;

    /// <summary>A page with nothing shown yet.</summary>
    public RoutingPage()
    {
        InitializeComponent();
        Loaded += (_, _) => Ask();
        // A question never outlives its page.
        Unloaded += (_, _) => _question.Close();
    }

    /// <summary>What the page shows, and its actions.</summary>
    public RoutingViewModel ViewModel { get; } = new();

    internal void Attach(PageContext context)
    {
        _context = context;
        ViewModel.Attach(context);
    }

    internal void Show(RoutingView view)
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
}
