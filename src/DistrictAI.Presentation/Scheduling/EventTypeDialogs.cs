using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DistrictAI.Core.Ffi;

namespace DistrictAI.ViewModels.Scheduling;

/// <summary>
/// The event type's form (the core's <see cref="EventTypeEditorView"/>): the
/// short one that creates (a name, the booking page it makes, a duration, a
/// location) and the whole one that edits. Its save is the dialog's confirm.
/// </summary>
/// <remarks>
/// The fields are written once, as the dialog opens: from then on what the
/// member types is the form, and each change sends the whole of it to the
/// core, which says why it cannot be sent and what the save came to.
/// </remarks>
public sealed partial class EventTypeEditorViewModel : ObservableObject
{
    private readonly Func<EventTypeFormView, UiEvent> _edit;
    private string[] _locations = [];
    private string _openedLocation = string.Empty;
    private PageContext? _context;
    private bool _writing;

    /// <summary>A form whose save sends <paramref name="save"/>, whose way out sends <paramref name="close"/>, and whose changes send <paramref name="edit"/>.</summary>
    internal EventTypeEditorViewModel(Func<UiEvent> save, Func<UiEvent> close, Func<EventTypeFormView, UiEvent> edit)
    {
        Dialog = new AdminDialogViewModel(save, close);
        _edit = edit;
    }

    /// <summary>The dialog's title, save, way out and the save's failure.</summary>
    public AdminDialogViewModel Dialog { get; }

    /// <summary>The location types to choose from.</summary>
    public ObservableCollection<ChoiceEntry> Locations { get; } = [];

    /// <summary>The form's labels and hints, as the core gave them when it opened.</summary>
    [ObservableProperty]
    public partial EventTypeEditorView? Words { get; set; }

    /// <summary>Whether this is a create, which shows the short form.</summary>
    [ObservableProperty]
    public partial bool Creating { get; set; }

    /// <summary>Whether every field shows: an edit.</summary>
    [ObservableProperty]
    public partial bool ShowsAll { get; set; }

    /// <summary>The name, as typed.</summary>
    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    /// <summary>The description, as typed.</summary>
    [ObservableProperty]
    public partial string Description { get; set; } = string.Empty;

    /// <summary>The duration in minutes, as typed.</summary>
    [ObservableProperty]
    public partial string Duration { get; set; } = string.Empty;

    /// <summary>The minutes between start times, as typed.</summary>
    [ObservableProperty]
    public partial string Interval { get; set; } = string.Empty;

    /// <summary>Which of <see cref="Locations"/> is chosen, or -1.</summary>
    [ObservableProperty]
    public partial int LocationIndex { get; set; } = -1;

    /// <summary>The phone number, address or link, as typed.</summary>
    [ObservableProperty]
    public partial string LocationValue { get; set; } = string.Empty;

    /// <summary>Whether it can be booked.</summary>
    [ObservableProperty]
    public partial bool IsActive { get; set; }

    /// <summary>Whether it is listed on the booking page.</summary>
    [ObservableProperty]
    public partial bool IsPublic { get; set; }

    /// <summary>Whether taken times show as taken.</summary>
    [ObservableProperty]
    public partial bool ShowTakenSlots { get; set; }

    /// <summary>Minutes kept free before, as typed.</summary>
    [ObservableProperty]
    public partial string BufferBefore { get; set; } = string.Empty;

    /// <summary>Minutes kept free after, as typed.</summary>
    [ObservableProperty]
    public partial string BufferAfter { get; set; } = string.Empty;

    /// <summary>The minimum notice in minutes, as typed.</summary>
    [ObservableProperty]
    public partial string MinNotice { get; set; } = string.Empty;

    /// <summary>How many days ahead it can be booked, as typed.</summary>
    [ObservableProperty]
    public partial string MaxFutureDays { get; set; } = string.Empty;

    /// <summary>How many bookings one attendee may hold, as typed.</summary>
    [ObservableProperty]
    public partial string MaxActiveBookings { get; set; } = string.Empty;

    /// <summary>The booking page's slug: the event type's own, or the one a create makes from the name.</summary>
    [ObservableProperty]
    public partial string BookingPage { get; set; } = string.Empty;

    /// <summary>The duration as the core reads it: "45 min", or "Not set".</summary>
    [ObservableProperty]
    public partial string DurationShown { get; set; } = string.Empty;

    /// <summary>The location value's label ("Phone number"), or empty.</summary>
    [ObservableProperty]
    public partial string LocationValueLabel { get; set; } = string.Empty;

    /// <summary>Whether the location value shows: an edit, and a location that takes one.</summary>
    [ObservableProperty]
    public partial bool HasLocationValue { get; set; }

    /// <summary>Why the form cannot be sent, or empty.</summary>
    [ObservableProperty]
    public partial string Validation { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="Validation"/>.</summary>
    [ObservableProperty]
    public partial bool HasValidation { get; set; }

    internal void Attach(PageContext context)
    {
        _context = context;
        Dialog.Attach(context);
    }

    /// <summary>Shows <paramref name="view"/>, or closes the form when the core holds none.</summary>
    internal void Show(EventTypeEditorView? view)
    {
        var opening = view is not null && !Dialog.IsOpen;
        Dialog.Show(view?.Dialog);
        if (view is null)
        {
            return;
        }
        if (opening)
        {
            Fill(view);
        }
        BookingPage = view.BookingPage;
        DurationShown = view.DurationShown;
        LocationValueLabel = view.LocationValueLabel ?? string.Empty;
        HasLocationValue = !view.Creating && view.LocationValueLabel is not null;
        Validation = view.Validation ?? string.Empty;
        HasValidation = view.Validation is not null;
    }

    /// <summary>Chooses the location at <paramref name="index"/> of <see cref="Locations"/>; the one chosen sends nothing.</summary>
    internal void PickLocation(int index)
    {
        if (index < 0 || index >= _locations.Length || index == LocationIndex)
        {
            return;
        }
        LocationIndex = index;
        Edited();
    }

    /// <summary>The form the fields hold.</summary>
    internal EventTypeFormView Form() => new(
        Name,
        Description,
        Duration,
        Interval,
        LocationIndex >= 0 && LocationIndex < _locations.Length ? _locations[LocationIndex] : _openedLocation,
        LocationValue,
        IsActive,
        IsPublic,
        ShowTakenSlots,
        BufferBefore,
        BufferAfter,
        MinNotice,
        MaxFutureDays,
        MaxActiveBookings);

    /// <summary>Writes the fields from the form the dialog opened with.</summary>
    private void Fill(EventTypeEditorView view)
    {
        _writing = true;
        try
        {
            Words = view;
            Creating = view.Creating;
            ShowsAll = !view.Creating;
            var form = view.Form;
            Name = form.Name;
            Description = form.Description;
            Duration = form.Duration;
            Interval = form.Interval;
            LocationValue = form.LocationValue;
            IsActive = form.IsActive;
            IsPublic = form.IsPublic;
            ShowTakenSlots = form.ShowTakenSlots;
            BufferBefore = form.BufferBefore;
            BufferAfter = form.BufferAfter;
            MinNotice = form.MinNotice;
            MaxFutureDays = form.MaxFutureDays;
            MaxActiveBookings = form.MaxActiveBookings;
            _openedLocation = form.Location;
            _locations = [.. view.Locations.Select(choice => choice.Value)];
            Display.Sync(Locations, [.. view.Locations.Select(ChoiceEntry.From)]);
            LocationIndex = Array.FindIndex(view.Locations, choice => choice.Selected);
        }
        finally
        {
            _writing = false;
        }
    }

    partial void OnNameChanged(string value) => Edited();

    partial void OnDescriptionChanged(string value) => Edited();

    partial void OnDurationChanged(string value) => Edited();

    partial void OnIntervalChanged(string value) => Edited();

    partial void OnLocationValueChanged(string value) => Edited();

    partial void OnIsActiveChanged(bool value) => Edited();

    partial void OnIsPublicChanged(bool value) => Edited();

    partial void OnShowTakenSlotsChanged(bool value) => Edited();

    partial void OnBufferBeforeChanged(string value) => Edited();

    partial void OnBufferAfterChanged(string value) => Edited();

    partial void OnMinNoticeChanged(string value) => Edited();

    partial void OnMaxFutureDaysChanged(string value) => Edited();

    partial void OnMaxActiveBookingsChanged(string value) => Edited();

    /// <summary>A field changed: the whole form goes to the core, when the member changed it.</summary>
    private void Edited()
    {
        if (_writing || !Dialog.IsOpen)
        {
            return;
        }
        _context?.Send(_edit(Form()));
    }
}

/// <summary>
/// An event type's state actions (the core's <see cref="EventTypeActionsView"/>):
/// on or off, archive or restore, a test email of each kind to the member,
/// and delete, asked once more in the dialog. One action at a time; what the
/// last one came to shows in the dialog until it is put away.
/// </summary>
public sealed partial class EventTypeActionsViewModel : ObservableObject
{
    private PageContext? _context;

    /// <summary>The state actions, with nothing shown yet.</summary>
    public EventTypeActionsViewModel()
    {
        // It has no confirm (each action is its own button), so only the way
        // out is ever sent.
        Dialog = new AdminDialogViewModel(
            () => SchedulingEventTypeViewModel.Event(new EventTypeAction.CloseDialog()),
            () => SchedulingEventTypeViewModel.Event(new EventTypeAction.CloseDialog()));
    }

    /// <summary>The dialog's title (the event type's name), its way out and why the last action failed.</summary>
    public AdminDialogViewModel Dialog { get; }

    /// <summary>The test emails: the kind sent, and its label.</summary>
    public ObservableCollection<ChoiceEntry> TestEmails { get; } = [];

    /// <summary>"Turn off", or "Turn on".</summary>
    [ObservableProperty]
    public partial string ToggleLabel { get; set; } = string.Empty;

    /// <summary>"Archive", or "Restore".</summary>
    [ObservableProperty]
    public partial string ArchiveLabel { get; set; } = string.Empty;

    /// <summary>"Send test email".</summary>
    [ObservableProperty]
    public partial string TestEmailHeading { get; set; } = string.Empty;

    /// <summary>"Delete event type".</summary>
    [ObservableProperty]
    public partial string DeleteLabel { get; set; } = string.Empty;

    /// <summary>Whether an action can be pressed: none is on its way.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ToggleCommand), nameof(ArchiveCommand), nameof(AskDeleteCommand), nameof(ConfirmDeleteCommand))]
    public partial bool CanAct { get; set; }

    /// <summary>What the last action came to, or empty.</summary>
    [ObservableProperty]
    public partial string Notice { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="Notice"/>.</summary>
    [ObservableProperty]
    public partial bool HasNotice { get; set; }

    /// <summary>Whether the delete is being asked about.</summary>
    [ObservableProperty]
    public partial bool Asking { get; set; }

    /// <summary>"Delete event type?".</summary>
    [ObservableProperty]
    public partial string AskTitle { get; set; } = string.Empty;

    /// <summary>What goes, and what stays.</summary>
    [ObservableProperty]
    public partial string AskBody { get; set; } = string.Empty;

    /// <summary>The delete's confirm.</summary>
    [ObservableProperty]
    public partial string AskConfirm { get; set; } = string.Empty;

    /// <summary>The way back from the question.</summary>
    [ObservableProperty]
    public partial string AskCancel { get; set; } = string.Empty;

    internal void Attach(PageContext context)
    {
        _context = context;
        Dialog.Attach(context);
    }

    /// <summary>Shows <paramref name="view"/>, or closes the dialog when the core holds none.</summary>
    internal void Show(EventTypeActionsView? view)
    {
        Dialog.Show(view?.Dialog);
        if (view is null)
        {
            return;
        }
        ToggleLabel = view.ToggleLabel;
        ArchiveLabel = view.ArchiveLabel;
        TestEmailHeading = view.TestEmailHeading;
        Display.Sync(TestEmails, [.. view.TestEmails.Select(ChoiceEntry.From)]);
        DeleteLabel = view.DeleteLabel;
        CanAct = view.CanAct;
        Notice = view.Notice ?? string.Empty;
        HasNotice = view.Notice is not null;
        Asking = view.Delete is not null;
        AskTitle = view.Delete?.Title ?? string.Empty;
        AskBody = view.Delete?.Body ?? string.Empty;
        AskConfirm = view.Delete?.Confirm ?? string.Empty;
        AskCancel = view.Delete?.Cancel ?? string.Empty;
    }

    /// <summary>Sends a test email of <paramref name="email"/>'s kind to the member.</summary>
    internal void SendTestEmail(ChoiceEntry email)
    {
        if (CanAct)
        {
            Act(new EventTypeAction.SendTestEmail(email.Value));
        }
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private void Toggle() => Act(new EventTypeAction.ToggleActive());

    [RelayCommand(CanExecute = nameof(CanAct))]
    private void Archive() => Act(new EventTypeAction.ToggleArchived());

    [RelayCommand(CanExecute = nameof(CanAct))]
    private void AskDelete() => Send(new EventTypeAction.AskDelete());

    /// <summary>Deletes it: the core goes back to the list once it lands.</summary>
    [RelayCommand(CanExecute = nameof(CanAct))]
    private void ConfirmDelete()
    {
        if (Asking)
        {
            Act(new EventTypeAction.ConfirmDelete());
        }
    }

    [RelayCommand]
    private void CancelDelete() => Send(new EventTypeAction.CancelDelete());

    [RelayCommand]
    private void DismissNotice() => Send(new EventTypeAction.DismissNotice());

    /// <summary>One press, one action: the buttons are off until the core's next snapshot.</summary>
    private void Act(EventTypeAction action)
    {
        CanAct = false;
        Send(action);
    }

    private void Send(EventTypeAction action) => _context?.Send(SchedulingEventTypeViewModel.Event(action));
}

/// <summary>
/// An event type's hosts and how its bookings are routed among them (the
/// core's <see cref="EventTypeHostsView"/>), saved whole: the save is the
/// dialog's confirm, offered once the hosts are read. The rows are built
/// again only when a host comes or goes, so a priority being typed keeps its
/// box.
/// </summary>
public sealed partial class EventTypeHostsViewModel : ObservableObject
{
    private string[] _routingModes = [];
    private string[] _rotations = [];
    private string[] _roles = [];
    private string[] _hostIds = [];
    private Dictionary<string, (string Role, string Priority)> _known = [];
    private PageContext? _context;

    /// <summary>The hosts dialog, with nothing shown yet.</summary>
    public EventTypeHostsViewModel()
    {
        Dialog = new AdminDialogViewModel(
            () => SchedulingEventTypeViewModel.Event(new EventTypeAction.SaveHosts()),
            () => SchedulingEventTypeViewModel.Event(new EventTypeAction.CloseDialog()));
    }

    /// <summary>The dialog's title, save, way out and the save's failure.</summary>
    public AdminDialogViewModel Dialog { get; }

    /// <summary>The routing modes.</summary>
    public ObservableCollection<ChoiceEntry> RoutingModes { get; } = [];

    /// <summary>The rotations.</summary>
    public ObservableCollection<ChoiceEntry> Rotations { get; } = [];

    /// <summary>The hosts, as edited.</summary>
    public ObservableCollection<HostEntry> Hosts { get; } = [];

    /// <summary>Who could be added.</summary>
    public ObservableCollection<CandidateEntry> Candidates { get; } = [];

    /// <summary>Whether the hosts are being read.</summary>
    [ObservableProperty]
    public partial bool Loading { get; set; }

    /// <summary>Whether their read failed.</summary>
    [ObservableProperty]
    public partial bool Failed { get; set; }

    /// <summary>Why their read failed.</summary>
    [ObservableProperty]
    public partial string FailureMessage { get; set; } = string.Empty;

    /// <summary>Whether the hosts are read: the routing and the list show.</summary>
    [ObservableProperty]
    public partial bool Ready { get; set; }

    /// <summary>"Routing".</summary>
    [ObservableProperty]
    public partial string RoutingLabel { get; set; } = string.Empty;

    /// <summary>Which of <see cref="RoutingModes"/> is chosen, or -1.</summary>
    [ObservableProperty]
    public partial int RoutingIndex { get; set; } = -1;

    /// <summary>Whether the rotation shows: round robin.</summary>
    [ObservableProperty]
    public partial bool ShowsRotation { get; set; }

    /// <summary>"Rotation".</summary>
    [ObservableProperty]
    public partial string RotationLabel { get; set; } = string.Empty;

    /// <summary>What the rotation decides.</summary>
    [ObservableProperty]
    public partial string RotationHint { get; set; } = string.Empty;

    /// <summary>Which of <see cref="Rotations"/> is chosen, or -1.</summary>
    [ObservableProperty]
    public partial int RotationIndex { get; set; } = -1;

    /// <summary>Whether the hosts are read and there are none.</summary>
    [ObservableProperty]
    public partial bool ShowEmpty { get; set; }

    /// <summary>What to say then.</summary>
    [ObservableProperty]
    public partial string EmptyText { get; set; } = string.Empty;

    /// <summary>Whether anyone could be added.</summary>
    [ObservableProperty]
    public partial bool HasCandidates { get; set; }

    /// <summary>Why the hosts cannot be saved, or empty.</summary>
    [ObservableProperty]
    public partial string Validation { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="Validation"/>.</summary>
    [ObservableProperty]
    public partial bool HasValidation { get; set; }

    internal void Attach(PageContext context)
    {
        _context = context;
        Dialog.Attach(context);
    }

    /// <summary>Shows <paramref name="view"/>, or closes the dialog when the core holds none.</summary>
    internal void Show(EventTypeHostsView? view)
    {
        Dialog.Show(view?.Dialog);
        if (view is null)
        {
            _hostIds = [];
            return;
        }
        var failure = (view.Status as LoadStatus.Failed)?.Failure;
        Loading = view.Status is LoadStatus.Loading;
        Failed = failure is not null;
        FailureMessage = Display.Failure(failure);
        Ready = view.Status is LoadStatus.Ready;
        RoutingLabel = view.RoutingLabel;
        _routingModes = [.. view.RoutingModes.Select(choice => choice.Value)];
        Display.Sync(RoutingModes, [.. view.RoutingModes.Select(ChoiceEntry.From)]);
        RoutingIndex = Array.FindIndex(view.RoutingModes, choice => choice.Selected);
        ShowsRotation = view.ShowsRotation;
        RotationLabel = view.RotationLabel;
        RotationHint = view.RotationHint;
        _rotations = [.. view.Rotations.Select(choice => choice.Value)];
        Display.Sync(Rotations, [.. view.Rotations.Select(ChoiceEntry.From)]);
        RotationIndex = Array.FindIndex(view.Rotations, choice => choice.Selected);
        _roles = [.. view.Roles.Select(choice => choice.Value)];
        _known = view.Hosts.ToDictionary(host => host.UserId, host => (host.Role, host.Priority));
        string[] ids = [.. view.Hosts.Select(host => host.UserId)];
        if (!ids.SequenceEqual(_hostIds))
        {
            // Built again only when a host comes or goes: a box being typed in stays.
            _hostIds = ids;
            string[] labels = [.. view.Roles.Select(choice => choice.Label)];
            Display.Sync(Hosts, [.. view.Hosts.Select(host => new HostEntry(
                host.UserId,
                host.Name,
                host.Email,
                labels,
                Array.IndexOf(_roles, host.Role),
                host.Priority,
                view.PriorityLabel,
                view.RemoveLabel))]);
        }
        ShowEmpty = Ready && view.Hosts.Length == 0;
        EmptyText = view.Empty;
        Display.Sync(Candidates, [.. view.Candidates.Select(user => new CandidateEntry(user.Value, user.Label, view.AddLabel))]);
        HasCandidates = Ready && Candidates.Count > 0;
        Validation = view.Validation ?? string.Empty;
        HasValidation = view.Validation is not null;
    }

    /// <summary>Chooses the routing mode at <paramref name="index"/>; the one chosen sends nothing.</summary>
    internal void PickRouting(int index)
    {
        if (index >= 0 && index < _routingModes.Length && index != RoutingIndex)
        {
            RoutingIndex = index;
            Send(new EventTypeAction.SetRoutingMode(_routingModes[index]));
        }
    }

    /// <summary>Chooses the rotation at <paramref name="index"/>; the one chosen sends nothing.</summary>
    internal void PickRotation(int index)
    {
        if (index >= 0 && index < _rotations.Length && index != RotationIndex)
        {
            RotationIndex = index;
            Send(new EventTypeAction.SetRotation(_rotations[index]));
        }
    }

    /// <summary><paramref name="host"/>'s role is now the one at <paramref name="index"/>; their own sends nothing.</summary>
    internal void SetRole(HostEntry host, int index)
    {
        if (index < 0 || index >= _roles.Length || !_known.TryGetValue(host.UserId, out var known) || known.Role == _roles[index])
        {
            return;
        }
        _known[host.UserId] = (_roles[index], known.Priority);
        Send(new EventTypeAction.SetHostRole(host.UserId, _roles[index]));
    }

    /// <summary><paramref name="host"/>'s priority box now holds <paramref name="priority"/>; the core's own value sends nothing.</summary>
    internal void SetPriority(HostEntry host, string priority)
    {
        if (!_known.TryGetValue(host.UserId, out var known) || known.Priority == priority)
        {
            return;
        }
        _known[host.UserId] = (known.Role, priority);
        Send(new EventTypeAction.SetHostPriority(host.UserId, priority));
    }

    /// <summary>Takes <paramref name="host"/> off.</summary>
    internal void Remove(HostEntry host) => Send(new EventTypeAction.RemoveHost(host.UserId));

    /// <summary>Adds <paramref name="candidate"/> as a host.</summary>
    internal void Add(CandidateEntry candidate) => Send(new EventTypeAction.AddHost(candidate.UserId));

    private void Send(EventTypeAction action) => _context?.Send(SchedulingEventTypeViewModel.Event(action));
}

/// <summary>
/// An event type's booking questions (the core's <see cref="EventTypeQuestionsView"/>):
/// the list in the order the booker sees it, each with Edit and Delete (asked
/// once more), Add question, and one question's form in place of the list
/// while it is open. What the last write came to shows until put away.
/// </summary>
/// <remarks>
/// The form's fields are written as it opens on a question, then only by the
/// member; a select's choices are the core's, added and taken off through it.
/// </remarks>
public sealed partial class EventTypeQuestionsViewModel : ObservableObject
{
    private string[] _kinds = [];
    private string[] _options = [];
    private string? _formId;
    private string _formKind = string.Empty;
    private PageContext? _context;
    private bool _writing;

    /// <summary>The questions dialog, with nothing shown yet.</summary>
    public EventTypeQuestionsViewModel()
    {
        // It has no confirm (the form has its own save), so only the way out
        // is ever sent.
        Dialog = new AdminDialogViewModel(
            () => SchedulingEventTypeViewModel.Event(new EventTypeAction.CloseDialog()),
            () => SchedulingEventTypeViewModel.Event(new EventTypeAction.CloseDialog()));
    }

    /// <summary>The dialog's title, its way out and why the last write failed.</summary>
    public AdminDialogViewModel Dialog { get; }

    /// <summary>The questions, in the order the booker sees them.</summary>
    public ObservableCollection<QuestionEntry> Questions { get; } = [];

    /// <summary>The question types.</summary>
    public ObservableCollection<ChoiceEntry> Kinds { get; } = [];

    /// <summary>A select's choices, as the core holds them.</summary>
    public ObservableCollection<OptionEntry> Options { get; } = [];

    /// <summary>The form's labels and hints, or null while it is closed.</summary>
    [ObservableProperty]
    public partial QuestionEditorView? Words { get; set; }

    /// <summary>Whether the questions are being read.</summary>
    [ObservableProperty]
    public partial bool Loading { get; set; }

    /// <summary>Whether their read failed.</summary>
    [ObservableProperty]
    public partial bool Failed { get; set; }

    /// <summary>Why their read failed.</summary>
    [ObservableProperty]
    public partial string FailureMessage { get; set; } = string.Empty;

    /// <summary>Whether the questions are read and there are none.</summary>
    [ObservableProperty]
    public partial bool ShowEmpty { get; set; }

    /// <summary>What to say then.</summary>
    [ObservableProperty]
    public partial string EmptyText { get; set; } = string.Empty;

    /// <summary>"Add question".</summary>
    [ObservableProperty]
    public partial string AddLabel { get; set; } = string.Empty;

    /// <summary>Whether a question can be added.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddQuestionCommand))]
    public partial bool CanAdd { get; set; }

    /// <summary>What the last write came to, or empty.</summary>
    [ObservableProperty]
    public partial string Notice { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="Notice"/>.</summary>
    [ObservableProperty]
    public partial bool HasNotice { get; set; }

    /// <summary>Whether one question's form is open, in place of the list.</summary>
    [ObservableProperty]
    public partial bool Editing { get; set; }

    /// <summary>Whether the list shows: no form is open.</summary>
    [ObservableProperty]
    public partial bool ShowsList { get; set; }

    /// <summary>The question, as typed.</summary>
    [ObservableProperty]
    public partial string Label { get; set; } = string.Empty;

    /// <summary>Which of <see cref="Kinds"/> is chosen, or -1.</summary>
    [ObservableProperty]
    public partial int KindIndex { get; set; } = -1;

    /// <summary>Whether it must be answered.</summary>
    [ObservableProperty]
    public partial bool Required { get; set; }

    /// <summary>Its place, as typed.</summary>
    [ObservableProperty]
    public partial string Position { get; set; } = string.Empty;

    /// <summary>The choice being typed, to add.</summary>
    [ObservableProperty]
    public partial string NewOption { get; set; } = string.Empty;

    /// <summary>Whether the choices show: a select.</summary>
    [ObservableProperty]
    public partial bool ShowsOptions { get; set; }

    /// <summary>The form's save: "Save question", or "Add question".</summary>
    [ObservableProperty]
    public partial string SaveLabel { get; set; } = string.Empty;

    /// <summary>Whether the save can be pressed.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveQuestionCommand))]
    public partial bool CanSave { get; set; }

    /// <summary>Why the question cannot be sent, or empty.</summary>
    [ObservableProperty]
    public partial string Validation { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="Validation"/>.</summary>
    [ObservableProperty]
    public partial bool HasValidation { get; set; }

    /// <summary>Whether a question's delete is being asked about.</summary>
    [ObservableProperty]
    public partial bool Asking { get; set; }

    /// <summary>"Delete question?".</summary>
    [ObservableProperty]
    public partial string AskTitle { get; set; } = string.Empty;

    /// <summary>What goes, and what stays.</summary>
    [ObservableProperty]
    public partial string AskBody { get; set; } = string.Empty;

    /// <summary>The delete's confirm.</summary>
    [ObservableProperty]
    public partial string AskConfirm { get; set; } = string.Empty;

    /// <summary>The way back from the question.</summary>
    [ObservableProperty]
    public partial string AskCancel { get; set; } = string.Empty;

    internal void Attach(PageContext context)
    {
        _context = context;
        Dialog.Attach(context);
    }

    /// <summary>Shows <paramref name="view"/>, or closes the dialog when the core holds none.</summary>
    internal void Show(EventTypeQuestionsView? view)
    {
        var opening = view is not null && !Dialog.IsOpen;
        Dialog.Show(view?.Dialog);
        if (view is null)
        {
            Close();
            return;
        }
        var failure = (view.Status as LoadStatus.Failed)?.Failure;
        Loading = view.Status is LoadStatus.Loading;
        Failed = failure is not null;
        FailureMessage = Display.Failure(failure);
        Display.Sync(Questions, [.. view.Questions.Select(question => new QuestionEntry(
            question.Id, question.Label, question.Value, view.EditLabel, view.DeleteLabel))]);
        ShowEmpty = view.Status is LoadStatus.Ready && view.Questions.Length == 0;
        EmptyText = view.Empty;
        AddLabel = view.AddLabel;
        CanAdd = view.CanAdd;
        Notice = view.Notice ?? string.Empty;
        HasNotice = view.Notice is not null;
        Asking = view.Delete is not null;
        AskTitle = view.Delete?.Title ?? string.Empty;
        AskBody = view.Delete?.Body ?? string.Empty;
        AskConfirm = view.Delete?.Confirm ?? string.Empty;
        AskCancel = view.Delete?.Cancel ?? string.Empty;
        ShowForm(view.Form, opening);
    }

    /// <summary>Chooses the question type at <paramref name="index"/>; the one chosen sends nothing.</summary>
    internal void PickKind(int index)
    {
        if (index < 0 || index >= _kinds.Length || index == KindIndex)
        {
            return;
        }
        KindIndex = index;
        Edited();
    }

    /// <summary>Edits <paramref name="question"/>.</summary>
    internal void Edit(QuestionEntry question) => Send(new EventTypeAction.EditQuestion(question.Id));

    /// <summary>Asks about deleting <paramref name="question"/>.</summary>
    internal void AskDelete(QuestionEntry question) => Send(new EventTypeAction.AskDeleteQuestion(question.Id));

    /// <summary>Takes the choice <paramref name="option"/> off the select.</summary>
    internal void RemoveOption(OptionEntry option) => Send(new EventTypeAction.RemoveOption(option.Option));

    /// <summary>The form as the fields hold it, with the core's choices.</summary>
    internal QuestionFormView Form() => new(
        _formId,
        Label,
        KindIndex >= 0 && KindIndex < _kinds.Length ? _kinds[KindIndex] : _formKind,
        _options,
        Required,
        Position);

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private void AddQuestion() => Send(new EventTypeAction.AddQuestion());

    /// <summary>Adds the choice typed, and empties its box; an empty one sends nothing.</summary>
    [RelayCommand]
    private void AddOption()
    {
        var option = NewOption;
        _writing = true;
        try
        {
            NewOption = string.Empty;
        }
        finally
        {
            _writing = false;
        }
        if (option.Trim().Length > 0)
        {
            Send(new EventTypeAction.AddOption(option));
        }
    }

    /// <summary>Saves the question: off as it is pressed, so one press is one save.</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private void SaveQuestion()
    {
        CanSave = false;
        Send(new EventTypeAction.SaveQuestion());
    }

    [RelayCommand]
    private void CancelQuestion() => Send(new EventTypeAction.CancelQuestion());

    /// <summary>Deletes the question asked about.</summary>
    [RelayCommand]
    private void ConfirmDelete()
    {
        if (Asking)
        {
            Asking = false;
            Send(new EventTypeAction.ConfirmDeleteQuestion());
        }
    }

    [RelayCommand]
    private void CancelDelete() => Send(new EventTypeAction.CancelDeleteQuestion());

    [RelayCommand]
    private void DismissNotice() => Send(new EventTypeAction.DismissNotice());

    partial void OnLabelChanged(string value) => Edited();

    partial void OnRequiredChanged(bool value) => Edited();

    partial void OnPositionChanged(string value) => Edited();

    /// <summary>Shows the question's form, writing its fields when it has just opened on a question.</summary>
    private void ShowForm(QuestionEditorView? form, bool opening)
    {
        var opened = form is not null && (opening || !Editing || form.Form.Id != _formId);
        Editing = form is not null;
        ShowsList = form is null;
        Words = form;
        if (form is null)
        {
            return;
        }
        _options = form.Form.Options;
        Display.Sync(Options, [.. form.Form.Options.Select(option => new OptionEntry(option, form.RemoveLabel))]);
        ShowsOptions = form.ShowsOptions;
        SaveLabel = form.SaveLabel;
        CanSave = form.CanSave;
        Validation = form.Validation ?? string.Empty;
        HasValidation = form.Validation is not null;
        if (!opened)
        {
            return;
        }
        _writing = true;
        try
        {
            _formId = form.Form.Id;
            _formKind = form.Form.Kind;
            Label = form.Form.Label;
            Required = form.Form.Required;
            Position = form.Form.Position;
            NewOption = string.Empty;
            _kinds = [.. form.Kinds.Select(choice => choice.Value)];
            Display.Sync(Kinds, [.. form.Kinds.Select(ChoiceEntry.From)]);
            KindIndex = Array.FindIndex(form.Kinds, choice => choice.Selected);
        }
        finally
        {
            _writing = false;
        }
    }

    private void Close()
    {
        Editing = false;
        ShowsList = true;
        Words = null;
    }

    /// <summary>A field of the form changed: the whole form goes to the core, when the member changed it.</summary>
    private void Edited()
    {
        if (_writing || !Editing)
        {
            return;
        }
        Send(new EventTypeAction.EditQuestionForm(Form()));
    }

    private void Send(EventTypeAction action) => _context?.Send(SchedulingEventTypeViewModel.Event(action));
}
