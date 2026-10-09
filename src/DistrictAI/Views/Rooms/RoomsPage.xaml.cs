using DistrictAI.Core.Ffi;
using DistrictAI.Platform;
using DistrictAI.ViewModels;
using DistrictAI.ViewModels.Rooms;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace DistrictAI.Views.Rooms;

/// <summary>
/// Meeting rooms, audio only: starting or joining a room, the room joined, the
/// meetings held and a meeting's record. What it shows is
/// <see cref="RoomsViewModel"/>'s; this copies the guest link and offers
/// Report.
/// </summary>
public sealed partial class RoomsPage : UserControl
{
    private PageContext? _context;

    /// <summary>A page with nothing shown yet.</summary>
    public RoomsPage()
    {
        InitializeComponent();
        Status.Attach(ViewModel.Load);
        Refresh.Attach(ViewModel.Load);
    }

    /// <summary>What the page shows, and its actions.</summary>
    public RoomsViewModel ViewModel { get; } = new();

    internal void Attach(PageContext context)
    {
        _context = context;
        ViewModel.Attach(context);
    }

    internal void Show(RoomsView view) => ViewModel.Show(view, _context?.ReportSending ?? false);

    // Enter joins, as Join does.
    private void OnRoomNameKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && ViewModel.JoinCommand.CanExecute(null))
        {
            e.Handled = true;
            ViewModel.JoinCommand.Execute(null);
        }
    }

    // The link goes on the clipboard, never on screen; the page says whether
    // it went.
    private void OnCopyGuestLinkClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.GuestLink is { } link)
        {
            ViewModel.LinkCopied(ClipboardService.CopyText(link));
        }
    }

    private void OnMeetingClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is MeetingRowItem row)
        {
            ViewModel.OpenRecord(row);
        }
    }

    private void OnRejoinClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is MeetingRowItem row)
        {
            ViewModel.Rejoin(row);
        }
    }

    private async void OnReportMinutesClick(object sender, RoutedEventArgs e)
    {
        if (_context is not null && XamlRoot is not null)
        {
            var record = ViewModel.Record;
            await ReportDialog.OfferAsync(XamlRoot, _context, record.MinutesReport, record.MinutesTarget).ConfigureAwait(true);
        }
    }

    private async void OnReportActionItemsClick(object sender, RoutedEventArgs e)
    {
        if (_context is not null && XamlRoot is not null)
        {
            var record = ViewModel.Record;
            await ReportDialog.OfferAsync(XamlRoot, _context, record.ActionItemsReport, record.ActionItemsTarget).ConfigureAwait(true);
        }
    }
}
