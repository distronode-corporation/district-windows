using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels;
using DistrictAI.ViewModels.Marketplace;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace DistrictAI.Views.Marketplace;

/// <summary>
/// The workspace's phone numbers and a search of the numbers for sale, read
/// only. Buying is on the web, for a role that could buy there.
/// </summary>
public sealed partial class MarketplacePage : UserControl
{
    /// <summary>A page with nothing shown yet.</summary>
    public MarketplacePage()
    {
        InitializeComponent();
        Status.Attach(ViewModel.Load);
        Refresh.Attach(ViewModel.Load);
    }

    /// <summary>What the page shows, and its actions.</summary>
    public MarketplaceViewModel ViewModel { get; } = new();

    internal void Attach(PageContext context) => ViewModel.Attach(context);

    internal void Show(MarketplaceView view) => ViewModel.Show(view);

    /// <summary>Enter in a search box searches at once, as the Linux app's does.</summary>
    private void OnSearchKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            ViewModel.SearchCommand.Execute(null);
            e.Handled = true;
        }
    }
}
