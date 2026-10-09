using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels;
using DistrictAI.ViewModels.Settings.Tools;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views.Settings.Tools;

/// <summary>
/// District Studio's Skills: a switch for every tool the core lists, saved
/// together, and outside research on contacts, saved alone. Leaving with a
/// switch moved and not saved asks "Discard your changes?" (the window asks,
/// from the core's <see cref="ShellView.Discard"/>).
/// </summary>
public sealed partial class ToolsPage : UserControl
{
    /// <summary>A page with nothing shown yet.</summary>
    public ToolsPage()
    {
        InitializeComponent();
    }

    /// <summary>What the page shows, and its actions.</summary>
    public ToolsViewModel ViewModel { get; } = new();

    /// <summary>A save notice's look: a save, or a failure.</summary>
    public static InfoBarSeverity SeverityFor(bool saved) => saved ? InfoBarSeverity.Success : InfoBarSeverity.Error;

    internal void Attach(PageContext context) => ViewModel.Attach(context);

    internal void Show(ToolsView view) => ViewModel.Show(view);
}
