using DistrictAI.Core.Ffi;
using DistrictAI.Platform;
using DistrictAI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views;

/// <summary>
/// The account: who is signed in, this build and this computer, starting at
/// sign-in, the devices, signing out, and where deleting the account starts.
/// </summary>
public sealed partial class AccountPage : UserControl
{
    /// <summary>A page with nothing shown yet.</summary>
    public AccountPage()
    {
        InitializeComponent();
        // Read each time the page opens: the user can change it in Task
        // Manager or Settings while the app runs.
        Loaded += OnLoaded;
    }

    /// <summary>What the page shows, and its buttons.</summary>
    public AccountViewModel ViewModel { get; } = new(new StartupRegistration());

    internal void Attach(PageContext context) => ViewModel.Attach(context);

    internal void Show(AccountView view) => ViewModel.Show(view);

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadStartupAsync().ConfigureAwait(true);
        StartupSwitch.IsOn = ViewModel.StartupEnabled;
    }

    private async void OnStartupToggled(object sender, RoutedEventArgs e)
    {
        await ViewModel.SetStartupAsync(StartupSwitch.IsOn).ConfigureAwait(true);
        // The setting as it really is, which may not be what was asked for.
        StartupSwitch.IsOn = ViewModel.StartupEnabled;
    }
}
