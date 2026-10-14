using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DistrictAI.Core.Ffi;

namespace DistrictAI.ViewModels.Scheduling;

/// <summary>
/// The bookings (the core's <see cref="SchedulingBookingsView"/>): a view
/// (upcoming, past, cancelled, all) read a page at a time, each booking with
/// the actions the core offers on it, and the dialog of the one open. What a
/// write came to shows over the list once its dialog has closed.
/// </summary>
public sealed partial class SchedulingBookingsViewModel : ObservableObject
{
    private BookingsTab[] _tabs = [];
    private PageContext? _context;

    /// <summary>A list with nothing shown yet.</summary>
    public SchedulingBookingsViewModel()
    {
        Paging = new PagingViewModel(() => Event(new BookingsAction.LoadMore()));
        Cancel = new CancelBookingViewModel(
            () => Event(new BookingsAction.Confirm()),
            () => Event(new BookingsAction.CloseDialog()),
            reason => Event(new BookingsAction.EditCancelReason(reason)));
        Reschedule = new RescheduleBookingViewModel(
            () => Event(new BookingsAction.Confirm()),
            () => Event(new BookingsAction.CloseDialog()),
            day => Event(new BookingsAction.PickDay(day)),
            start => Event(new BookingsAction.PickSlot(start)),
            () => Event(new BookingsAction.RetryDialogRead()));
        Reassign = new ReassignBookingViewModel(
            () => Event(new BookingsAction.Confirm()),
            () => Event(new BookingsAction.CloseDialog()),
            host => Event(new BookingsAction.PickHost(host)),
            () => Event(new BookingsAction.RetryDialogRead()));
    }

    /// <summary>Loading, a first page that failed, and an empty view.</summary>
    public LoadStateViewModel Load { get; } = new();

    /// <summary>"Load more" at the end of the list.</summary>
    public PagingViewModel Paging { get; }

    /// <summary>The cancel dialog.</summary>
    public CancelBookingViewModel Cancel { get; }

    /// <summary>The move dialog.</summary>
    public RescheduleBookingViewModel Reschedule { get; }

    /// <summary>The host dialog.</summary>
    public ReassignBookingViewModel Reassign { get; }

    /// <summary>The views' labels, in the core's order.</summary>
    public ObservableCollection<string> Tabs { get; } = [];

    /// <summary>The bookings read so far.</summary>
    public ObservableCollection<BookingEntry> Rows { get; } = [];

    /// <summary>Which of <see cref="Tabs"/> shows.</summary>
    [ObservableProperty]
    public partial int SelectedTab { get; set; } = -1;

    /// <summary>"Times in America/Toronto".</summary>
    [ObservableProperty]
    public partial string TimesIn { get; set; } = string.Empty;

    /// <summary>"Load more", or "Loading…" while the next page is on its way.</summary>
    [ObservableProperty]
    public partial string LoadMoreLabel { get; set; } = string.Empty;

    /// <summary>What the last write came to ("Booking cancelled"), or empty.</summary>
    [ObservableProperty]
    public partial string Notice { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="Notice"/>.</summary>
    [ObservableProperty]
    public partial bool HasNotice { get; set; }

    internal void Attach(PageContext context)
    {
        _context = context;
        Load.Attach(context);
        Paging.Attach(context);
        Cancel.Attach(context);
        Reschedule.Attach(context);
        Reassign.Attach(context);
    }

    internal void Show(SchedulingBookingsView view)
    {
        TimesIn = view.TimesIn;
        _tabs = [.. view.Tabs.Select(tab => tab.Tab)];
        Display.Sync(Tabs, [.. view.Tabs.Select(tab => tab.Label)]);
        SelectedTab = Array.FindIndex(view.Tabs, tab => tab.Selected);
        Load.Show(view.Status, view.Rows.Length > 0, view.Empty, refreshing: false, refreshFailure: null);
        Display.Sync(Rows, [.. view.Rows.Select(row => BookingEntry.From(row, view))]);
        Paging.Show(view.Paging);
        LoadMoreLabel = view.LoadMore;
        Notice = view.Notice ?? string.Empty;
        HasNotice = view.Notice is not null;
        Cancel.Show(view.Cancel);
        Reschedule.Show(view.Reschedule);
        Reassign.Show(view.Reassign);
    }

    /// <summary>Shows the view at <paramref name="index"/> of <see cref="Tabs"/>; the one showing sends nothing.</summary>
    internal void SelectTab(int index)
    {
        if (index >= 0 && index < _tabs.Length && index != SelectedTab)
        {
            Send(new BookingsAction.SelectTab(_tabs[index]));
        }
    }

    /// <summary>Opens <paramref name="row"/>'s booking.</summary>
    internal void Open(BookingEntry row) =>
        _context?.Send(new UiEvent.SchedulingAdmin(new SchedulingAdminAction.OpenBooking(row.Id)));

    /// <summary>Asks to cancel <paramref name="row"/>'s booking.</summary>
    internal void OpenCancel(BookingEntry row)
    {
        if (row.CanCancel)
        {
            Send(new BookingsAction.OpenCancel(row.Id));
        }
    }

    /// <summary>Asks to move <paramref name="row"/>'s booking.</summary>
    internal void OpenReschedule(BookingEntry row)
    {
        if (row.CanReschedule)
        {
            Send(new BookingsAction.OpenReschedule(row.Id));
        }
    }

    /// <summary>Asks to hand <paramref name="row"/>'s booking to another host.</summary>
    internal void OpenReassign(BookingEntry row)
    {
        if (row.CanReassign)
        {
            Send(new BookingsAction.OpenReassign(row.Id));
        }
    }

    /// <summary>Puts away what the last write came to.</summary>
    [RelayCommand]
    private void DismissNotice() => Send(new BookingsAction.CloseDialog());

    /// <summary>Forwards one of the list's actions.</summary>
    internal void Send(BookingsAction action) => _context?.Send(Event(action));

    /// <summary>The event for <paramref name="action"/>.</summary>
    internal static UiEvent Event(BookingsAction action) =>
        new UiEvent.SchedulingAdmin(new SchedulingAdminAction.Bookings(action));
}
