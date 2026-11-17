using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels;
using DistrictAI.ViewModels.AccountSecurity;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views.AccountSecurity;

/// <summary>
/// Deleting the account, below the account page: what is deleted and what is
/// kept, confirming it is you (<see cref="ReauthPanel"/>), then the account's
/// email address, typed, and Delete my account. After the deletion the core
/// signs this computer out and the sign-in page says the account was deleted.
/// The typed password, code and address leave their boxes as they are sent,
/// and leave the page whenever it is not showing (<see cref="Leave"/>).
/// </summary>
public sealed partial class DeleteAccountPage : UserControl
{
    /// <summary>A page with nothing shown yet.</summary>
    public DeleteAccountPage()
    {
        InitializeComponent();
        ReauthHost.Content = new ReauthPanel(ViewModel.Reauth);
        ViewModel.EmailCleared += (_, _) => EmailInput.Text = string.Empty;
    }

    /// <summary>What the page shows, and its buttons.</summary>
    public DeleteAccountViewModel ViewModel { get; } = new();

    internal void Attach(PageContext context) => ViewModel.Attach(context);

    internal void Show(DeleteAccountView view) => ViewModel.Show(view);

    /// <summary>The page is no longer showing: what was typed goes.</summary>
    internal void Leave() => ViewModel.Leave();

    private void OnDeleteClick(object sender, RoutedEventArgs e) => _ = ViewModel.Delete(EmailInput.Text);
}
