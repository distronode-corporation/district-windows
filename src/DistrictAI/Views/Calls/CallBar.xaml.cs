using DistrictAI.Core;
using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels.Calls;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views.Calls;

/// <summary>
/// The strip under every signed-in screen while a call is under way: it sits
/// outside the pages, so it stays as the member moves about the app. Shown
/// while <see cref="ShellView.Call"/> is set; everything on it is the core's
/// <see cref="ActiveCallView"/>.
/// </summary>
public sealed partial class CallBar : UserControl
{
    /// <summary>A strip with no call.</summary>
    public CallBar()
    {
        InitializeComponent();
    }

    /// <summary>What the strip shows, and its buttons' commands.</summary>
    public CallBarViewModel ViewModel { get; } = new();

    internal void Attach(CoreHost core) => ViewModel.Attach(core);

    internal void Show(ShellView shell) => ViewModel.Show(shell.Call);
}
