using DistrictAI.Core;
using DistrictAI.Core.Ffi;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace DistrictAI;

/// <summary>The one window: the sign-in page outside a session, the app inside one.</summary>
public sealed partial class MainWindow : Window
{
    private readonly CoreHost _core;

    /// <summary>The window over <paramref name="core"/>.</summary>
    internal MainWindow(CoreHost core)
    {
        _core = core;
        InitializeComponent();
        // Mica on Windows 11; Windows 10 keeps the default background.
        if (MicaController.IsSupported())
        {
            SystemBackdrop = new MicaBackdrop();
        }
        SignIn.Attach(core);
        core.Changed += (_, snapshot) => Render(snapshot);
        Render(core.Current);
    }

    /// <summary>Shows <paramref name="message"/> over the window until closed.</summary>
    internal void ShowNotice(string message)
    {
        Notice.Message = message;
        Notice.IsOpen = true;
    }

    /// <summary>Offers no browser sign-in, and says why.</summary>
    internal void BlockSignIn(string why)
    {
        SignIn.BlockSignIn();
        ShowNotice(why);
    }

    private void Render(CoreSnapshot snapshot)
    {
        var signedIn = snapshot.Shell.Phase == SessionPhase.SignedIn;
        SignedIn.Visibility = signedIn ? Visibility.Visible : Visibility.Collapsed;
        SignIn.Visibility = signedIn ? Visibility.Collapsed : Visibility.Visible;
        if (snapshot.Screen is ScreenView.Session session)
        {
            SignIn.Show(session.View);
        }
    }
}
