using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DistrictAI.Core.Ffi;

namespace DistrictAI.ViewModels.Scheduling;

/// <summary>
/// The member's calendars (the core's <see cref="SchedulingCalendarView"/>):
/// a card per connected account with Choose calendars and Disconnect (asked
/// once more), and the card that connects another: Zoom's line, a Connect
/// button per provider the scheduler can connect, and Connect a CalDAV
/// calendar. Every write is the member's own, so a viewer is offered them
/// too.
/// </summary>
/// <remarks>
/// Connect hands off to the browser: the core opens the provider's consent
/// page there, through the window's own way of opening a link, and the app
/// is never told when it is done, so the section offers Refresh meanwhile.
/// </remarks>
public sealed partial class SchedulingCalendarViewModel : ObservableObject
{
    private PageContext? _context;

    /// <summary>The calendars, with nothing shown yet.</summary>
    public SchedulingCalendarViewModel()
    {
        Disconnect = new AdminDialogViewModel(
            () => Event(new CalendarAction.ConfirmDisconnect()),
            () => Event(new CalendarAction.CancelDisconnect()));
    }

    /// <summary>Loading and failure, for the read of the connections.</summary>
    public LoadStateViewModel Load { get; } = new();

    /// <summary>The disconnect, while it is asked about: its confirm is destructive.</summary>
    public AdminDialogViewModel Disconnect { get; }

    /// <summary>One account's calendars.</summary>
    public CalendarPickerViewModel Picker { get; } = new();

    /// <summary>The CalDAV form.</summary>
    public CaldavViewModel Caldav { get; } = new();

    /// <summary>The connected accounts.</summary>
    public ObservableCollection<CalendarConnectionEntry> Connections { get; } = [];

    /// <summary>The Connect buttons.</summary>
    public ObservableCollection<CalendarProviderEntry> Providers { get; } = [];

    /// <summary>The connect card's heading.</summary>
    [ObservableProperty]
    public partial string ConnectHeading { get; set; } = string.Empty;

    /// <summary>The connect card's sentence.</summary>
    [ObservableProperty]
    public partial string Sentence { get; set; } = string.Empty;

    /// <summary>Whether Zoom's line shows.</summary>
    [ObservableProperty]
    public partial bool HasZoom { get; set; }

    /// <summary>"Zoom".</summary>
    [ObservableProperty]
    public partial string ZoomLabel { get; set; } = string.Empty;

    /// <summary>"Connected" or "Not connected".</summary>
    [ObservableProperty]
    public partial string ZoomValue { get; set; } = string.Empty;

    /// <summary>Under the Connect buttons: finish in the browser and refresh.</summary>
    [ObservableProperty]
    public partial string ConnectNote { get; set; } = string.Empty;

    /// <summary>Whether <see cref="ConnectNote"/> shows.</summary>
    [ObservableProperty]
    public partial bool ShowsConnectNote { get; set; }

    /// <summary>Whether a sign-in is on its way.</summary>
    [ObservableProperty]
    public partial bool Connecting { get; set; }

    /// <summary>Whether a consent page is open in the browser: Refresh is offered.</summary>
    [ObservableProperty]
    public partial bool AwaitingBrowser { get; set; }

    /// <summary>Why the last connect failed, or empty.</summary>
    [ObservableProperty]
    public partial string ConnectFailure { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="ConnectFailure"/>.</summary>
    [ObservableProperty]
    public partial bool HasConnectFailure { get; set; }

    /// <summary>"Connect a CalDAV calendar".</summary>
    [ObservableProperty]
    public partial string CaldavLabel { get; set; } = string.Empty;

    /// <summary>Whether a disconnect is on its way.</summary>
    [ObservableProperty]
    public partial bool Disconnecting { get; set; }

    /// <summary>Why the last disconnect failed, or empty.</summary>
    [ObservableProperty]
    public partial string DisconnectFailure { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="DisconnectFailure"/>.</summary>
    [ObservableProperty]
    public partial bool HasDisconnectFailure { get; set; }

    /// <summary>That no calendar receives new bookings now, or empty.</summary>
    [ObservableProperty]
    public partial string DisconnectNotice { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="DisconnectNotice"/>.</summary>
    [ObservableProperty]
    public partial bool HasDisconnectNotice { get; set; }

    internal void Attach(PageContext context)
    {
        _context = context;
        Load.Attach(context);
        Disconnect.Attach(context);
        Picker.Attach(context);
        Caldav.Attach(context);
    }

    internal void Show(SchedulingCalendarView view)
    {
        Load.Show(view.Status, true, null, refreshing: false, refreshFailure: null);
        Display.Sync(Connections, [.. view.Connections.Select(card => CalendarConnectionEntry.From(card, view))]);
        Display.Sync(Providers, [.. view.Providers.Select(p => new CalendarProviderEntry(p.Value, p.Label, !view.Connecting))]);
        ConnectHeading = view.ConnectHeading;
        Sentence = view.Sentence;
        HasZoom = view.Zoom is not null;
        ZoomLabel = view.Zoom?.Label ?? string.Empty;
        ZoomValue = view.Zoom?.Value ?? string.Empty;
        ConnectNote = view.ConnectNote;
        ShowsConnectNote = view.ShowsConnectNote;
        Connecting = view.Connecting;
        AwaitingBrowser = view.AwaitingBrowser;
        ConnectFailure = Display.Failure(view.ConnectFailure);
        HasConnectFailure = view.ConnectFailure is not null;
        CaldavLabel = view.CaldavLabel;
        Disconnecting = view.Disconnecting;
        DisconnectFailure = Display.Failure(view.DisconnectFailure);
        HasDisconnectFailure = view.DisconnectFailure is not null;
        DisconnectNotice = view.DisconnectNotice ?? string.Empty;
        HasDisconnectNotice = view.DisconnectNotice is not null;
        Disconnect.Show(view.Disconnect);
        Picker.Show(view.Picker);
        Caldav.Show(view.Caldav);
    }

    /// <summary>Closes its dialogs: the calendars are no longer showing.</summary>
    internal void Leave()
    {
        Disconnect.Show(null);
        Picker.Show(null);
        Caldav.Show(null);
    }

    /// <summary>Connects <paramref name="provider"/> in the browser; nothing while a sign-in is on its way.</summary>
    internal void Connect(CalendarProviderEntry provider)
    {
        if (!Connecting)
        {
            // Until the core's next snapshot: one press, one sign-in.
            Connecting = true;
            Send(new CalendarAction.Connect(provider.Provider));
        }
    }

    /// <summary>Opens <paramref name="card"/>'s calendars.</summary>
    internal void OpenCalendars(CalendarConnectionEntry card) => Send(new CalendarAction.OpenCalendars(card.Id));

    /// <summary>Asks about disconnecting <paramref name="card"/>; nothing while a disconnect is on its way.</summary>
    internal void AskDisconnect(CalendarConnectionEntry card)
    {
        if (card.CanDisconnect)
        {
            Send(new CalendarAction.AskDisconnect(card.Id));
        }
    }

    [RelayCommand]
    private void OpenCaldav() => Send(new CalendarAction.OpenCaldav());

    /// <summary>Reads what is connected now, once the browser is done.</summary>
    [RelayCommand]
    private void Refresh() => _context?.Send(new UiEvent.Refresh());

    [RelayCommand]
    private void DismissConnectFailure() => Send(new CalendarAction.DismissConnectFailure());

    [RelayCommand]
    private void DismissDisconnect() => Send(new CalendarAction.DismissDisconnect());

    private void Send(CalendarAction action) => _context?.Send(Event(action));

    /// <summary>The event for <paramref name="action"/>.</summary>
    internal static UiEvent Event(CalendarAction action) =>
        new UiEvent.SchedulingAdmin(new SchedulingAdminAction.Calendar(action));
}
