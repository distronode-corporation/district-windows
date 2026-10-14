using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DistrictAI.Core.Ffi;

namespace DistrictAI.ViewModels.Scheduling;

/// <summary>
/// One account's calendars (the core's <see cref="CalendarPickerView"/>):
/// each with its switch for conflicts, and the ones that can receive bookings
/// to choose one from. Saved whole by the dialog's confirm, offered once they
/// are read; what the save came to shows in the dialog.
/// </summary>
public sealed partial class CalendarPickerViewModel : ObservableObject
{
    private string[] _destinations = [];
    private PageContext? _context;

    /// <summary>The picker, with nothing shown yet.</summary>
    public CalendarPickerViewModel()
    {
        Dialog = new AdminDialogViewModel(
            () => SchedulingCalendarViewModel.Event(new CalendarAction.SaveCalendars()),
            () => SchedulingCalendarViewModel.Event(new CalendarAction.CloseDialog()));
    }

    /// <summary>The dialog's title, save, way out and the save's failure.</summary>
    public AdminDialogViewModel Dialog { get; }

    /// <summary>Every calendar, with its switch.</summary>
    public ObservableCollection<CalendarRowEntry> Calendars { get; } = [];

    /// <summary>The calendars that can receive bookings.</summary>
    public ObservableCollection<ChoiceEntry> Destinations { get; } = [];

    /// <summary>The account, whose calendars a failed read asks for again.</summary>
    [ObservableProperty]
    public partial string ConnectionId { get; set; } = string.Empty;

    /// <summary>Whether the calendars are being read.</summary>
    [ObservableProperty]
    public partial bool Loading { get; set; }

    /// <summary>"Loading calendars…".</summary>
    [ObservableProperty]
    public partial string LoadingLabel { get; set; } = string.Empty;

    /// <summary>Whether their read failed.</summary>
    [ObservableProperty]
    public partial bool Failed { get; set; }

    /// <summary>Why their read failed.</summary>
    [ObservableProperty]
    public partial string FailureMessage { get; set; } = string.Empty;

    /// <summary>Whether "Try again" is offered for the failed read.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RetryCommand))]
    public partial bool CanRetry { get; set; }

    /// <summary>Whether the calendars are read.</summary>
    [ObservableProperty]
    public partial bool Ready { get; set; }

    /// <summary>"Checked for conflicts".</summary>
    [ObservableProperty]
    public partial string ConflictsLabel { get; set; } = string.Empty;

    /// <summary>What checking a calendar does.</summary>
    [ObservableProperty]
    public partial string ConflictsHint { get; set; } = string.Empty;

    /// <summary>"Receives bookings".</summary>
    [ObservableProperty]
    public partial string DestinationLabel { get; set; } = string.Empty;

    /// <summary>What that does.</summary>
    [ObservableProperty]
    public partial string DestinationHint { get; set; } = string.Empty;

    /// <summary>Which of <see cref="Destinations"/> receives bookings, or -1.</summary>
    [ObservableProperty]
    public partial int DestinationIndex { get; set; } = -1;

    /// <summary>Whether the choices can be changed: read, and no save on its way.</summary>
    [ObservableProperty]
    public partial bool Editable { get; set; }

    /// <summary>What the save came to, or empty.</summary>
    [ObservableProperty]
    public partial string Notice { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="Notice"/>.</summary>
    [ObservableProperty]
    public partial bool HasNotice { get; set; }

    internal void Attach(PageContext context)
    {
        _context = context;
        Dialog.Attach(context);
    }

    /// <summary>Shows <paramref name="view"/>, or closes the picker when the core holds none.</summary>
    internal void Show(CalendarPickerView? view)
    {
        Dialog.Show(view?.Dialog);
        if (view is null)
        {
            Calendars.Clear();
            Destinations.Clear();
            _destinations = [];
            DestinationIndex = -1;
            return;
        }
        var failure = (view.Status as LoadStatus.Failed)?.Failure;
        ConnectionId = view.ConnectionId;
        Loading = view.Status is LoadStatus.Loading;
        LoadingLabel = view.LoadingLabel;
        Failed = failure is not null;
        FailureMessage = Display.Failure(failure);
        CanRetry = failure?.Retryable ?? false;
        Ready = view.Status is LoadStatus.Ready;
        ConflictsLabel = view.ConflictsLabel;
        ConflictsHint = view.ConflictsHint;
        DestinationLabel = view.DestinationLabel;
        DestinationHint = view.DestinationHint;
        Editable = view.Editable;
        Notice = view.Notice ?? string.Empty;
        HasNotice = view.Notice is not null;
        Display.Sync(Calendars, [.. view.Calendars.Select(row => new CalendarRowEntry(row.Id, row.Name, row.ChecksConflicts, view.Editable, view.ConflictsLabel))]);
        _destinations = [.. view.Destinations.Select(choice => choice.Value)];
        Display.Sync(Destinations, [.. view.Destinations.Select(ChoiceEntry.From)]);
        DestinationIndex = Array.FindIndex(view.Destinations, choice => choice.Selected);
    }

    /// <summary><paramref name="row"/>'s switch now says <paramref name="on"/>; what it already says sends nothing.</summary>
    internal void SetCheckConflicts(CalendarRowEntry row, bool on)
    {
        if (Editable && on != row.ChecksConflicts)
        {
            Send(new CalendarAction.SetCheckConflicts(row.Id, on));
        }
    }

    /// <summary>Chooses the calendar at <paramref name="index"/> of <see cref="Destinations"/>; the one chosen sends nothing.</summary>
    internal void ChooseDestination(int index)
    {
        if (!Editable || index < 0 || index >= _destinations.Length || index == DestinationIndex)
        {
            return;
        }
        DestinationIndex = index;
        Send(new CalendarAction.ChooseDestination(_destinations[index]));
    }

    [RelayCommand(CanExecute = nameof(CanRetry))]
    private void Retry() => Send(new CalendarAction.OpenCalendars(ConnectionId));

    private void Send(CalendarAction action) => _context?.Send(SchedulingCalendarViewModel.Event(action));
}

/// <summary>
/// The CalDAV form (the core's <see cref="CaldavView"/>): where the calendar
/// lives, its server's address for a place that needs one, the user name and
/// an app password. Its connect is the dialog's confirm; each field's refusal
/// shows under it, and once an account connects the dialog says which.
/// </summary>
/// <remarks>
/// The fields are written as the form opens and again once an account
/// connects (the core empties them), then only by the member; each change
/// sends the whole form. The app password is kept here only while the form
/// is open, and goes once an account connects or the form closes.
/// </remarks>
public sealed partial class CaldavViewModel : ObservableObject
{
    private string[] _presets = [];
    private string _openedPreset = string.Empty;
    private PageContext? _context;
    private bool _writing;
    private bool _open;
    private string? _connected;

    /// <summary>The form, with nothing shown yet.</summary>
    public CaldavViewModel()
    {
        Dialog = new AdminDialogViewModel(
            () => SchedulingCalendarViewModel.Event(new CalendarAction.ConnectCaldav()),
            () => SchedulingCalendarViewModel.Event(new CalendarAction.CloseDialog()));
    }

    /// <summary>The dialog's title, connect, way out and the connect's failure.</summary>
    public AdminDialogViewModel Dialog { get; }

    /// <summary>The places a calendar can live.</summary>
    public ObservableCollection<ChoiceEntry> Presets { get; } = [];

    /// <summary>The labels and hints, or null while the form is closed.</summary>
    [ObservableProperty]
    public partial CaldavView? Words { get; set; }

    /// <summary>Which of <see cref="Presets"/> is chosen, or -1.</summary>
    [ObservableProperty]
    public partial int PresetIndex { get; set; } = -1;

    /// <summary>Where to get an app password there.</summary>
    [ObservableProperty]
    public partial string PresetNote { get; set; } = string.Empty;

    /// <summary>Whether the server's address shows.</summary>
    [ObservableProperty]
    public partial bool ShowsServerUrl { get; set; }

    /// <summary>The server's address, as typed.</summary>
    [ObservableProperty]
    public partial string ServerUrl { get; set; } = string.Empty;

    /// <summary>The user name, as typed.</summary>
    [ObservableProperty]
    public partial string Username { get; set; } = string.Empty;

    /// <summary>The app password, as typed; never shown back.</summary>
    [ObservableProperty]
    public partial string Password { get; set; } = string.Empty;

    /// <summary>Why the server's address was refused, or empty.</summary>
    [ObservableProperty]
    public partial string ServerUrlError { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="ServerUrlError"/>.</summary>
    [ObservableProperty]
    public partial bool HasServerUrlError { get; set; }

    /// <summary>Why the user name was refused, or empty.</summary>
    [ObservableProperty]
    public partial string UsernameError { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="UsernameError"/>.</summary>
    [ObservableProperty]
    public partial bool HasUsernameError { get; set; }

    /// <summary>Why the app password was refused, or empty.</summary>
    [ObservableProperty]
    public partial string PasswordError { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="PasswordError"/>.</summary>
    [ObservableProperty]
    public partial bool HasPasswordError { get; set; }

    /// <summary>Whether the fields can be changed: no connect is on its way.</summary>
    [ObservableProperty]
    public partial bool Editable { get; set; }

    /// <summary>Which account connected, or empty.</summary>
    [ObservableProperty]
    public partial string Notice { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="Notice"/>.</summary>
    [ObservableProperty]
    public partial bool HasNotice { get; set; }

    internal void Attach(PageContext context)
    {
        _context = context;
        Dialog.Attach(context);
    }

    /// <summary>Shows <paramref name="view"/>, or closes the form when the core holds none.</summary>
    internal void Show(CaldavView? view)
    {
        Dialog.Show(view?.Dialog);
        Words = view;
        if (view is null)
        {
            _open = false;
            _connected = null;
            Write(null);
            return;
        }
        PresetNote = view.PresetNote;
        ShowsServerUrl = view.ShowsServerUrl;
        ServerUrlError = view.ServerUrlError ?? string.Empty;
        HasServerUrlError = view.ServerUrlError is not null;
        UsernameError = view.UsernameError ?? string.Empty;
        HasUsernameError = view.UsernameError is not null;
        PasswordError = view.PasswordError ?? string.Empty;
        HasPasswordError = view.PasswordError is not null;
        Editable = view.Editable;
        Notice = view.Notice ?? string.Empty;
        HasNotice = view.Notice is not null;
        // As it opens, and once an account connects: the core's fields, and
        // no password.
        var connected = view.Notice is not null && !view.Dialog.Working ? view.Notice : null;
        if (!_open || (connected is not null && connected != _connected))
        {
            _open = true;
            Write(view);
        }
        _connected = connected;
    }

    /// <summary>Chooses the place at <paramref name="index"/>; the one chosen sends nothing.</summary>
    internal void PickPreset(int index)
    {
        if (index < 0 || index >= _presets.Length || index == PresetIndex)
        {
            return;
        }
        PresetIndex = index;
        Edited();
    }

    /// <summary>The form as the fields hold it.</summary>
    internal CaldavFormView Form() => new(
        PresetIndex >= 0 && PresetIndex < _presets.Length ? _presets[PresetIndex] : _openedPreset,
        ServerUrl,
        Username,
        Password);

    partial void OnServerUrlChanged(string value) => Edited();

    partial void OnUsernameChanged(string value) => Edited();

    partial void OnPasswordChanged(string value) => Edited();

    /// <summary>Writes the fields from <paramref name="view"/>, the password emptied; all of them emptied for none.</summary>
    private void Write(CaldavView? view)
    {
        ChoiceRowView[] presets = view?.Presets ?? [];
        _writing = true;
        try
        {
            ServerUrl = view?.ServerUrl ?? string.Empty;
            Username = view?.Username ?? string.Empty;
            Password = string.Empty;
            _presets = [.. presets.Select(choice => choice.Value)];
            _openedPreset = Array.Find(presets, choice => choice.Selected)?.Value ?? string.Empty;
            Display.Sync(Presets, [.. presets.Select(ChoiceEntry.From)]);
            PresetIndex = Array.FindIndex(presets, choice => choice.Selected);
        }
        finally
        {
            _writing = false;
        }
    }

    /// <summary>A field of the form changed: the whole form goes to the core, when the member changed it.</summary>
    private void Edited()
    {
        if (_writing || !_open)
        {
            return;
        }
        _context?.Send(SchedulingCalendarViewModel.Event(new CalendarAction.EditCaldav(Form())));
    }
}
