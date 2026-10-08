using DistrictAI.Core;
using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels.Calls;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace DistrictAI.Views.Calls;

/// <summary>
/// The dialler: the number box and Call. Enter in the box is the same as
/// pressing Call. Everything the page shows is the core's
/// <see cref="DialerView"/>; the page decides nothing.
/// </summary>
public sealed partial class DialerPage : UserControl
{
    /// <summary>A page with nothing shown yet.</summary>
    public DialerPage()
    {
        InitializeComponent();
    }

    /// <summary>What the page shows, and Call's command.</summary>
    public DialerViewModel ViewModel { get; } = new();

    internal void Attach(CoreHost core) => ViewModel.Attach(core);

    internal void Show(DialerView view) => ViewModel.Show(view);

    /// <summary>The dialler came on screen: the box takes the keyboard, so typing and pasting go straight into it.</summary>
    internal void FocusNumber() => NumberBox.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);

    private void OnNumberKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter)
        {
            return;
        }
        e.Handled = true;
        if (ViewModel.DialCommand.CanExecute(null))
        {
            ViewModel.DialCommand.Execute(null);
        }
    }
}
