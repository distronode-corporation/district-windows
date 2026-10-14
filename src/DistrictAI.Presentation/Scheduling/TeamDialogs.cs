using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DistrictAI.Core.Ffi;

namespace DistrictAI.ViewModels.Scheduling;

/// <summary>
/// A team's name (the core's <see cref="TeamEditorView"/>): creating a team,
/// or renaming one and, apart from the rename, deleting it, asked once more
/// in the dialog. The save is the dialog's confirm; what it came to shows in
/// the dialog.
/// </summary>
/// <remarks>
/// The name is written once, as the dialog opens: from then on what the
/// member types is the name, and each change goes to the core, which says
/// why it cannot be sent.
/// </remarks>
public sealed partial class TeamEditorViewModel : ObservableObject
{
    private PageContext? _context;
    private bool _writing;

    /// <summary>The editor, with nothing shown yet.</summary>
    public TeamEditorViewModel()
    {
        Dialog = new AdminDialogViewModel(
            () => SchedulingTeamViewModel.Event(new TeamAction.SaveName()),
            () => SchedulingTeamViewModel.Event(new TeamAction.CloseDialog()));
    }

    /// <summary>The dialog's title, save, way out and the last write's failure.</summary>
    public AdminDialogViewModel Dialog { get; }

    /// <summary>The labels and hints, or null while the dialog is closed.</summary>
    [ObservableProperty]
    public partial TeamEditorView? Words { get; set; }

    /// <summary>The name, as typed.</summary>
    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    /// <summary>Why the name cannot be sent, or empty.</summary>
    [ObservableProperty]
    public partial string NameError { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="NameError"/>.</summary>
    [ObservableProperty]
    public partial bool HasNameError { get; set; }

    /// <summary>That a team's booking-page address cannot change here, or empty.</summary>
    [ObservableProperty]
    public partial string SlugHint { get; set; } = string.Empty;

    /// <summary>Whether "Delete team" shows: a team, not a create.</summary>
    [ObservableProperty]
    public partial bool ShowsDelete { get; set; }

    /// <summary>"Delete team".</summary>
    [ObservableProperty]
    public partial string DeleteLabel { get; set; } = string.Empty;

    /// <summary>Whether "Delete team" can be pressed: nothing is on its way.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AskDeleteCommand), nameof(ConfirmDeleteCommand))]
    public partial bool CanDelete { get; set; }

    /// <summary>Whether the name can be changed: nothing is on its way.</summary>
    [ObservableProperty]
    public partial bool Editable { get; set; }

    /// <summary>What the last write came to, or empty.</summary>
    [ObservableProperty]
    public partial string Notice { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="Notice"/>.</summary>
    [ObservableProperty]
    public partial bool HasNotice { get; set; }

    /// <summary>Whether the delete is being asked about.</summary>
    [ObservableProperty]
    public partial bool Asking { get; set; }

    /// <summary>"Delete Sales?".</summary>
    [ObservableProperty]
    public partial string AskTitle { get; set; } = string.Empty;

    /// <summary>What deleting it does.</summary>
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
    internal void Show(TeamEditorView? view)
    {
        var opening = view is not null && !Dialog.IsOpen;
        Dialog.Show(view?.Dialog);
        Words = view;
        if (view is null)
        {
            return;
        }
        if (opening)
        {
            _writing = true;
            try
            {
                Name = view.Name;
            }
            finally
            {
                _writing = false;
            }
        }
        NameError = view.NameError ?? string.Empty;
        HasNameError = view.NameError is not null;
        SlugHint = view.SlugHint ?? string.Empty;
        ShowsDelete = view.ShowsDelete;
        DeleteLabel = view.DeleteLabel;
        CanDelete = view.CanDelete;
        Editable = view.Editable;
        Notice = view.Notice ?? string.Empty;
        HasNotice = view.Notice is not null;
        Asking = view.Delete is not null;
        AskTitle = view.Delete?.Title ?? string.Empty;
        AskBody = view.Delete?.Body ?? string.Empty;
        AskConfirm = view.Delete?.Confirm ?? string.Empty;
        AskCancel = view.Delete?.Cancel ?? string.Empty;
    }

    partial void OnNameChanged(string value)
    {
        if (!_writing && Dialog.IsOpen)
        {
            Send(new TeamAction.EditName(value));
        }
    }

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private void AskDelete() => Send(new TeamAction.AskDelete());

    /// <summary>Deletes the team: one press, one delete.</summary>
    [RelayCommand(CanExecute = nameof(CanDelete))]
    private void ConfirmDelete()
    {
        if (Asking)
        {
            Asking = false;
            CanDelete = false;
            Send(new TeamAction.ConfirmDelete());
        }
    }

    [RelayCommand]
    private void CancelDelete() => Send(new TeamAction.CancelDelete());

    private void Send(TeamAction action) => _context?.Send(SchedulingTeamViewModel.Event(action));
}

/// <summary>
/// A team's hosts (the core's <see cref="TeamHostsView"/>): each with their
/// routing priority, Save priority and Remove (asked once more in the
/// dialog), and the people who could be added, one picked and added by the
/// dialog's confirm. Every control saves on its own, so the way out is Done;
/// what the last write came to shows in the dialog.
/// </summary>
/// <remarks>
/// The rows are built again only when a host comes or goes, so a priority
/// being typed keeps its box.
/// </remarks>
public sealed partial class TeamHostsViewModel : ObservableObject
{
    private string[] _hostIds = [];
    private string[] _candidates = [];
    private Dictionary<string, string> _typed = [];
    private PageContext? _context;

    /// <summary>The hosts, with nothing shown yet.</summary>
    public TeamHostsViewModel()
    {
        Dialog = new AdminDialogViewModel(
            () => SchedulingTeamViewModel.Event(new TeamAction.AddMember()),
            () => SchedulingTeamViewModel.Event(new TeamAction.CloseDialog()));
    }

    /// <summary>The dialog's title (the team's name), Add member, Done and the last write's failure.</summary>
    public AdminDialogViewModel Dialog { get; }

    /// <summary>The hosts, as the team was last answered.</summary>
    public ObservableCollection<TeamHostEntry> Hosts { get; } = [];

    /// <summary>The people who could be added.</summary>
    public ObservableCollection<ChoiceEntry> Candidates { get; } = [];

    /// <summary>That a lower routing priority is offered a booking first.</summary>
    [ObservableProperty]
    public partial string Hint { get; set; } = string.Empty;

    /// <summary>What to say when the team has no host.</summary>
    [ObservableProperty]
    public partial string EmptyText { get; set; } = string.Empty;

    /// <summary>Whether the team has no host.</summary>
    [ObservableProperty]
    public partial bool ShowEmpty { get; set; }

    /// <summary>Why a priority cannot be sent, or empty.</summary>
    [ObservableProperty]
    public partial string PriorityError { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="PriorityError"/>.</summary>
    [ObservableProperty]
    public partial bool HasPriorityError { get; set; }

    /// <summary>Whether the people who could be added are being read.</summary>
    [ObservableProperty]
    public partial bool UsersLoading { get; set; }

    /// <summary>Whether their read failed.</summary>
    [ObservableProperty]
    public partial bool UsersFailed { get; set; }

    /// <summary>Why their read failed.</summary>
    [ObservableProperty]
    public partial string UsersFailure { get; set; } = string.Empty;

    /// <summary>"Host", over the people who could be added.</summary>
    [ObservableProperty]
    public partial string AddLabel { get; set; } = string.Empty;

    /// <summary>"Choose a host".</summary>
    [ObservableProperty]
    public partial string AddPlaceholder { get; set; } = string.Empty;

    /// <summary>Whether anyone could be added.</summary>
    [ObservableProperty]
    public partial bool HasCandidates { get; set; }

    /// <summary>Which of <see cref="Candidates"/> is picked, or -1.</summary>
    [ObservableProperty]
    public partial int PickedIndex { get; set; } = -1;

    /// <summary>That everyone is in the team already, or empty.</summary>
    [ObservableProperty]
    public partial string AddNone { get; set; } = string.Empty;

    /// <summary>Whether there is an <see cref="AddNone"/>.</summary>
    [ObservableProperty]
    public partial bool HasAddNone { get; set; }

    /// <summary>Whether the hosts and the choice can be changed: nothing is on its way.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmRemoveCommand))]
    public partial bool Editable { get; set; }

    /// <summary>What the last write came to, or empty.</summary>
    [ObservableProperty]
    public partial string Notice { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="Notice"/>.</summary>
    [ObservableProperty]
    public partial bool HasNotice { get; set; }

    /// <summary>Whether a host's removal is being asked about.</summary>
    [ObservableProperty]
    public partial bool Asking { get; set; }

    /// <summary>"Remove this host?".</summary>
    [ObservableProperty]
    public partial string AskTitle { get; set; } = string.Empty;

    /// <summary>What removing them does.</summary>
    [ObservableProperty]
    public partial string AskBody { get; set; } = string.Empty;

    /// <summary>The removal's confirm.</summary>
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
    internal void Show(TeamHostsView? view)
    {
        Dialog.Show(view?.Dialog);
        if (view is null)
        {
            _hostIds = [];
            _typed = [];
            Hosts.Clear();
            Candidates.Clear();
            _candidates = [];
            PickedIndex = -1;
            return;
        }
        _typed = view.Hosts.ToDictionary(host => host.UserId, host => host.Priority);
        string[] ids = [.. view.Hosts.Select(host => host.UserId)];
        if (!ids.SequenceEqual(_hostIds))
        {
            // Built again only when a host comes or goes: a box being typed in stays.
            _hostIds = ids;
            Display.Sync(Hosts, [.. view.Hosts.Select(host => new TeamHostEntry(
                host.UserId,
                host.Name,
                host.Email,
                host.Priority,
                view.PriorityLabel,
                view.SavePriorityLabel,
                view.RemoveLabel))]);
        }
        Hint = view.Hint;
        EmptyText = view.Empty;
        ShowEmpty = view.Hosts.Length == 0;
        PriorityError = view.PriorityError ?? string.Empty;
        HasPriorityError = view.PriorityError is not null;
        var failure = (view.UsersStatus as LoadStatus.Failed)?.Failure;
        UsersLoading = view.UsersStatus is LoadStatus.Loading;
        UsersFailed = failure is not null;
        UsersFailure = Display.Failure(failure);
        AddLabel = view.AddLabel;
        AddPlaceholder = view.AddPlaceholder;
        _candidates = [.. view.Candidates.Select(choice => choice.Value)];
        Display.Sync(Candidates, [.. view.Candidates.Select(ChoiceEntry.From)]);
        PickedIndex = Array.FindIndex(view.Candidates, choice => choice.Selected);
        HasCandidates = view.Candidates.Length > 0;
        AddNone = view.AddNone ?? string.Empty;
        HasAddNone = view.AddNone is not null;
        Editable = view.Editable;
        Notice = view.Notice ?? string.Empty;
        HasNotice = view.Notice is not null;
        Asking = view.Remove is not null;
        AskTitle = view.Remove?.Title ?? string.Empty;
        AskBody = view.Remove?.Body ?? string.Empty;
        AskConfirm = view.Remove?.Confirm ?? string.Empty;
        AskCancel = view.Remove?.Cancel ?? string.Empty;
    }

    /// <summary>Picks the person at <paramref name="index"/> of <see cref="Candidates"/>; the one picked sends nothing.</summary>
    internal void Pick(int index)
    {
        if (!Editable || index < 0 || index >= _candidates.Length || index == PickedIndex)
        {
            return;
        }
        PickedIndex = index;
        Send(new TeamAction.Pick(_candidates[index]));
    }

    /// <summary><paramref name="host"/>'s priority box now holds <paramref name="priority"/>; what the core holds sends nothing.</summary>
    internal void SetPriority(TeamHostEntry host, string priority)
    {
        if (!_typed.TryGetValue(host.UserId, out var typed) || typed == priority)
        {
            return;
        }
        _typed[host.UserId] = priority;
        Send(new TeamAction.EditPriority(host.UserId, priority));
    }

    /// <summary>Saves <paramref name="host"/>'s priority; nothing while a write is on its way.</summary>
    internal void SavePriority(TeamHostEntry host)
    {
        if (Editable)
        {
            // Until the core's next snapshot: one press, one save.
            Editable = false;
            Send(new TeamAction.SavePriority(host.UserId));
        }
    }

    /// <summary>Asks about removing <paramref name="host"/>; nothing while a write is on its way.</summary>
    internal void AskRemove(TeamHostEntry host)
    {
        if (Editable)
        {
            Send(new TeamAction.AskRemove(host.UserId));
        }
    }

    /// <summary>Removes the host asked about: one press, one removal.</summary>
    [RelayCommand(CanExecute = nameof(Editable))]
    private void ConfirmRemove()
    {
        if (Asking)
        {
            Asking = false;
            Editable = false;
            Send(new TeamAction.ConfirmRemove());
        }
    }

    [RelayCommand]
    private void CancelRemove() => Send(new TeamAction.CancelRemove());

    private void Send(TeamAction action) => _context?.Send(SchedulingTeamViewModel.Event(action));
}

/// <summary>
/// An account's archive (the core's <see cref="TeamArchiveView"/>): what
/// archiving does, why it cannot be done yet, and each upcoming booking in
/// the way. The dialog's confirm asks once more, in the dialog; what the
/// archive came to shows there.
/// </summary>
public sealed partial class TeamArchiveViewModel : ObservableObject
{
    private PageContext? _context;

    /// <summary>The archive, with nothing shown yet.</summary>
    public TeamArchiveViewModel()
    {
        // The dialog's confirm asks; the question's confirm archives.
        Dialog = new AdminDialogViewModel(
            () => SchedulingTeamViewModel.Event(new TeamAction.AskArchive()),
            () => SchedulingTeamViewModel.Event(new TeamAction.CloseDialog()));
    }

    /// <summary>The dialog's title, what archiving does, Archive account, Keep account and the archive's failure.</summary>
    public AdminDialogViewModel Dialog { get; }

    /// <summary>The upcoming bookings in the way.</summary>
    public ObservableCollection<UpcomingBookingEntry> Bookings { get; } = [];

    /// <summary>Whether the upcoming bookings are being read.</summary>
    [ObservableProperty]
    public partial bool Loading { get; set; }

    /// <summary>Whether their read failed.</summary>
    [ObservableProperty]
    public partial bool Failed { get; set; }

    /// <summary>Why their read failed.</summary>
    [ObservableProperty]
    public partial string FailureMessage { get; set; } = string.Empty;

    /// <summary>Why the account cannot be archived yet, or empty.</summary>
    [ObservableProperty]
    public partial string Blocked { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="Blocked"/>.</summary>
    [ObservableProperty]
    public partial bool HasBlocked { get; set; }

    /// <summary>What the archive came to, or empty.</summary>
    [ObservableProperty]
    public partial string Notice { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="Notice"/>.</summary>
    [ObservableProperty]
    public partial bool HasNotice { get; set; }

    /// <summary>Whether the archive is asked about once more.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmArchiveCommand))]
    public partial bool Asking { get; set; }

    /// <summary>"Archive Ann's account?".</summary>
    [ObservableProperty]
    public partial string AskTitle { get; set; } = string.Empty;

    /// <summary>What archiving does.</summary>
    [ObservableProperty]
    public partial string AskBody { get; set; } = string.Empty;

    /// <summary>The archive's confirm.</summary>
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
    internal void Show(TeamArchiveView? view)
    {
        Dialog.Show(view?.Dialog);
        if (view is null)
        {
            Bookings.Clear();
            Asking = false;
            return;
        }
        var failure = (view.Status as LoadStatus.Failed)?.Failure;
        Loading = view.Status is LoadStatus.Loading;
        Failed = failure is not null;
        FailureMessage = Display.Failure(failure);
        Blocked = view.Blocked ?? string.Empty;
        HasBlocked = view.Blocked is not null;
        Display.Sync(Bookings, [.. view.Bookings.Select(UpcomingBookingEntry.From)]);
        Notice = view.Notice ?? string.Empty;
        HasNotice = view.Notice is not null;
        Asking = view.Ask is not null;
        AskTitle = view.Ask?.Title ?? string.Empty;
        AskBody = view.Ask?.Body ?? string.Empty;
        AskConfirm = view.Ask?.Confirm ?? string.Empty;
        AskCancel = view.Ask?.Cancel ?? string.Empty;
    }

    /// <summary>Archives the account: one press, one archive.</summary>
    [RelayCommand(CanExecute = nameof(Asking))]
    private void ConfirmArchive()
    {
        if (Asking)
        {
            Asking = false;
            _context?.Send(SchedulingTeamViewModel.Event(new TeamAction.ConfirmArchive()));
        }
    }

    [RelayCommand]
    private void CancelArchive() => _context?.Send(SchedulingTeamViewModel.Event(new TeamAction.CancelArchive()));
}
