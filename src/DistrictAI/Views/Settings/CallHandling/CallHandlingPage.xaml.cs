using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels;
using DistrictAI.ViewModels.Settings.CallHandling;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views.Settings.CallHandling;

/// <summary>
/// District Studio's Call handling: who answers and how long the devices ring,
/// saved for the whole workspace with a button, and the member's own "Ring me
/// for calls" switch, sent at once. Leaving with a change not saved asks
/// first: the window's "Discard your changes?" (the settings kit).
/// </summary>
public sealed partial class CallHandlingPage : UserControl
{
    /// <summary>A page with nothing shown yet.</summary>
    public CallHandlingPage()
    {
        InitializeComponent();
    }

    /// <summary>What the page shows, and its actions.</summary>
    public CallHandlingViewModel ViewModel { get; } = new();

    /// <summary>A save notice's look: a save, or a failure.</summary>
    public static InfoBarSeverity SeverityFor(bool saved) => saved ? InfoBarSeverity.Success : InfoBarSeverity.Error;

    internal void Attach(PageContext context) => ViewModel.Attach(context);

    internal void Show(CallHandlingView view) => ViewModel.Show(view);

    private void OnModeClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is CallHandlingModeItem mode)
        {
            ViewModel.SelectMode(mode);
        }
    }
}
