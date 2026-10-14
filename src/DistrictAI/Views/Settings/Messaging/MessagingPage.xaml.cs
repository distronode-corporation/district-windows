using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels;
using DistrictAI.ViewModels.Settings.Messaging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views.Settings.Messaging;

/// <summary>
/// The workspace's messaging (carrier) accounts: the accounts, each channel's
/// sender, the owner's mobile number, and the form that adds or edits an
/// account. Keys are typed into password boxes and never shown again.
/// </summary>
public sealed partial class MessagingPage : UserControl
{
    /// <summary>A page with nothing shown yet.</summary>
    public MessagingPage()
    {
        InitializeComponent();
    }

    /// <summary>What the page shows, and its actions.</summary>
    public MessagingViewModel ViewModel { get; } = new();

    internal void Attach(PageContext context) => ViewModel.Attach(context);

    internal void Show(MessagingView view) => ViewModel.Show(view);

    // An ItemsRepeater whose template uses x:Bind gives what it makes no
    // DataContext, so each row's control carries its account's id (or its
    // channel) in Tag.
    private MessagingAccountItem? AccountOf(object sender) =>
        (sender as FrameworkElement)?.Tag is string id ? ViewModel.Accounts.FirstOrDefault(account => account.Id == id) : null;

    private void OnMakeDefault(object sender, RoutedEventArgs e)
    {
        if (AccountOf(sender) is { } account)
        {
            ViewModel.MakeAccountDefault(account);
        }
    }

    private void OnEditAccount(object sender, RoutedEventArgs e)
    {
        if (AccountOf(sender) is { } account)
        {
            ViewModel.EditAccount(account);
        }
    }

    private void OnRemoveAccount(object sender, RoutedEventArgs e)
    {
        if (AccountOf(sender) is { } account)
        {
            ViewModel.RemoveAccount(account);
        }
    }

    private void OnSenderChosen(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { Tag: SenderChannel kind } picker && ViewModel.Channels.FirstOrDefault(item => item.Channel == kind) is { } channel)
        {
            ViewModel.ChooseSender(channel, picker.SelectedIndex);
        }
    }
}
