using DistrictAI.Core;
using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views;

/// <summary>Everything outside a session: resuming, signed out, signing in and signing out.</summary>
public sealed partial class SignInPage : UserControl
{
    /// <summary>A page with nothing shown yet.</summary>
    public SignInPage()
    {
        InitializeComponent();
    }

    /// <summary>What the page shows, and the buttons' commands.</summary>
    public SignInViewModel ViewModel { get; } = new();

    internal void Attach(CoreHost core) => ViewModel.Attach(core);

    internal void Show(SessionScreen screen) => ViewModel.Show(screen);
}
