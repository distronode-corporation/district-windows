using DistrictAI.Core.Ffi;
using DistrictAI.L10n;
using DistrictAI.ViewModels;
using DistrictAI.ViewModels.Marketplace;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace DistrictAI.Views.Marketplace;

/// <summary>
/// The workspace's phone numbers, a search of the numbers for sale, and the
/// carrier tab: reapplying a number's routing, releasing it, the paid lookup,
/// the SIP trunks, the passcode sender and the registrations, each write that
/// costs money or cannot be undone asked about first by the core. Buying is on
/// the web, for a role that could buy there.
/// </summary>
public sealed partial class MarketplacePage : UserControl
{
    private readonly NumberConfirmationDialog.Host _confirmation = new();
    private PageContext? _context;

    /// <summary>A page with nothing shown yet.</summary>
    public MarketplacePage()
    {
        InitializeComponent();
        TabChooser.Picked += (_, index) => ViewModel.TabIndex = index;
        Status.Attach(ViewModel.Load);
        Refresh.Attach(ViewModel.Load);
        CarrierPanel.Attach(ViewModel.Admin);
        Loaded += (_, _) => SyncConfirmation();
        // A dialog never outlives its page; the core still asks, so it opens
        // again when the page is back.
        Unloaded += (_, _) => _confirmation.Close();
    }

    /// <summary>What the page shows, and its actions.</summary>
    public MarketplaceViewModel ViewModel { get; } = new();

    /// <summary>The three tabs, in the order of <see cref="MarketplaceViewModel.TabIndex"/>: the numbers held, the search, the carrier.</summary>
    public static TabLabel[] TabsOf(string carrier) =>
    [
        new(S.NumbersYours, "NumbersYoursTab"),
        new(S.NumbersFind, "NumbersFindTab"),
        new(carrier, "NumbersCarrierTab"),
    ];

    internal void Attach(PageContext context)
    {
        _context = context;
        ViewModel.Attach(context);
    }

    internal void Show(MarketplaceView view)
    {
        ViewModel.Show(view);
        SyncConfirmation();
    }

    private void SyncConfirmation() => _confirmation.Sync(this, _context, ViewModel.Admin);

    private void OnConfigureClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is NumberRowItem row)
        {
            ViewModel.Admin.Configure(row);
        }
    }

    private void OnReleaseClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is NumberRowItem row)
        {
            ViewModel.Admin.AskRelease(row);
        }
    }

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
