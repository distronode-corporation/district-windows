using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels.Calls;

namespace DistrictAI.ViewModels.Rooms;

/// <summary>
/// Meeting rooms, audio only: starting or joining a room by its name, the room
/// joined (who is there, mute, the guest link, leave), the meetings held, and
/// a meeting's record with Report on its minutes and action items. Every word
/// is the core's <see cref="RoomsView"/>; this only copies it and forwards what
/// the member does.
/// </summary>
public sealed partial class RoomsViewModel : ObservableObject
{
    private readonly TextEcho _echo = new();
    private PageContext? _context;
    private bool _writing;
    private string? _guestLink;

    /// <summary>The meetings' read: loading, its failure with Try again, none yet, or the list.</summary>
    public LoadStateViewModel Load { get; } = new();

    /// <summary>The core's view of the screen.</summary>
    [ObservableProperty]
    public partial RoomsView? View { get; set; }

    /// <summary>The heading.</summary>
    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;

    /// <summary>The form's heading.</summary>
    [ObservableProperty]
    public partial string StartTitle { get; set; } = string.Empty;

    /// <summary>The name box's label.</summary>
    [ObservableProperty]
    public partial string RoomNameLabel { get; set; } = string.Empty;

    /// <summary>The room name the member types; each change is sent to the core.</summary>
    [ObservableProperty]
    public partial string RoomName { get; set; } = string.Empty;

    /// <summary>Which room the name leads to, or what to type.</summary>
    [ObservableProperty]
    public partial string NameNote { get; set; } = string.Empty;

    /// <summary>The Companion's note, or a viewer's.</summary>
    [ObservableProperty]
    public partial string RoleNote { get; set; } = string.Empty;

    /// <summary>Why Join does not work while a call holds the microphone, or empty.</summary>
    [ObservableProperty]
    public partial string BusyNote { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="BusyNote"/>.</summary>
    [ObservableProperty]
    public partial bool HasBusyNote { get; set; }

    /// <summary>Join's words.</summary>
    [ObservableProperty]
    public partial string JoinLabel { get; set; } = string.Empty;

    /// <summary>Whether Join works.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(JoinCommand))]
    public partial bool CanJoin { get; set; }

    /// <summary>Whether a room's credential is being asked for.</summary>
    [ObservableProperty]
    public partial bool Joining { get; set; }

    /// <summary>Why the last join failed, or the last room ended; or empty.</summary>
    [ObservableProperty]
    public partial string Failure { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="Failure"/>.</summary>
    [ObservableProperty]
    public partial bool HasFailure { get; set; }

    /// <summary>Whether a room is joined.</summary>
    [ObservableProperty]
    public partial bool InRoom { get; set; }

    /// <summary>The room's heading.</summary>
    [ObservableProperty]
    public partial string RoomTitle { get; set; } = string.Empty;

    /// <summary>Where the room's connection stands.</summary>
    [ObservableProperty]
    public partial string RoomState { get; set; } = string.Empty;

    /// <summary>Who else is in the room.</summary>
    [ObservableProperty]
    public partial string PeopleLine { get; set; } = string.Empty;

    /// <summary>The people in the room, by name.</summary>
    public ObservableCollection<string> People { get; } = [];

    /// <summary>The room's notice (a microphone that could not be used), or empty.</summary>
    [ObservableProperty]
    public partial string RoomNotice { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="RoomNotice"/>.</summary>
    [ObservableProperty]
    public partial bool HasRoomNotice { get; set; }

    /// <summary>Whether the member may speak, so Mute is shown. A viewer listens.</summary>
    [ObservableProperty]
    public partial bool CanSpeak { get; set; }

    /// <summary>Whether the microphone is off.</summary>
    [ObservableProperty]
    public partial bool Muted { get; set; }

    /// <summary>Mute's words: "Mute", or "Unmute" while muted.</summary>
    [ObservableProperty]
    public partial string MuteLabel { get; set; } = string.Empty;

    /// <summary>Whether Mute works.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ToggleMuteCommand))]
    public partial bool CanMute { get; set; }

    /// <summary>Whether there is a guest link to copy.</summary>
    [ObservableProperty]
    public partial bool HasGuestLink { get; set; }

    /// <summary>The copy button's words.</summary>
    [ObservableProperty]
    public partial string CopyLinkLabel { get; set; } = string.Empty;

    /// <summary>What the guest link is for.</summary>
    [ObservableProperty]
    public partial string CopyLinkHint { get; set; } = string.Empty;

    /// <summary>What shows once the link is copied, or empty before.</summary>
    [ObservableProperty]
    public partial string CopiedNote { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="CopiedNote"/>.</summary>
    [ObservableProperty]
    public partial bool HasCopiedNote { get; set; }

    /// <summary>Leave's words.</summary>
    [ObservableProperty]
    public partial string LeaveLabel { get; set; } = string.Empty;

    /// <summary>The meetings' heading.</summary>
    [ObservableProperty]
    public partial string MeetingsTitle { get; set; } = string.Empty;

    /// <summary>What the meetings are.</summary>
    [ObservableProperty]
    public partial string MeetingsDescription { get; set; } = string.Empty;

    /// <summary>The meetings, newest first.</summary>
    public ObservableCollection<MeetingRowItem> Meetings { get; } = [];

    /// <summary>The meeting record open over the lobby.</summary>
    public MeetingRecordViewModel Record { get; } = new();

    /// <summary>Whether a record is open, in place of the meetings.</summary>
    [ObservableProperty]
    public partial bool RecordOpen { get; set; }

    /// <summary>Whether the meetings show: no record open over them.</summary>
    [ObservableProperty]
    public partial bool MeetingsShown { get; set; } = true;

    internal void Attach(PageContext context)
    {
        _context = context;
        Load.Attach(context);
    }

    internal void Show(RoomsView view) => Show(view, _context?.ReportSending ?? false);

    internal void Show(RoomsView view, bool reportSending)
    {
        View = view;
        Title = view.Title;
        StartTitle = view.StartTitle;
        RoomNameLabel = view.RoomNameLabel;
        if (_echo.Write(view.RoomName, RoomName))
        {
            _writing = true;
            try
            {
                RoomName = view.RoomName;
            }
            finally
            {
                _writing = false;
            }
        }
        NameNote = view.NameNote;
        RoleNote = view.RoleNote;
        BusyNote = view.BusyNote ?? string.Empty;
        HasBusyNote = BusyNote.Length > 0;
        JoinLabel = view.JoinLabel;
        CanJoin = view.CanJoin;
        Joining = view.Joining;
        Failure = Display.Failure(view.Failure);
        HasFailure = view.Failure is not null;
        ShowRoom(view.Room);
        MeetingsTitle = view.MeetingsTitle;
        MeetingsDescription = view.MeetingsDescription;
        Load.Show(view.MeetingsStatus, view.Meetings.Length > 0, view.MeetingsEmpty, view.MeetingsRefreshing, refreshFailure: null);
        Display.Sync(Meetings, [.. view.Meetings.Select(MeetingRowItem.From)]);
        Record.Show(view.Record, reportSending);
        RecordOpen = view.Record is not null;
        MeetingsShown = !RecordOpen;
    }

    private void ShowRoom(RoomView? room)
    {
        var wasInRoom = InRoom;
        InRoom = room is not null;
        RoomTitle = room?.Title ?? string.Empty;
        RoomState = room?.State ?? string.Empty;
        PeopleLine = room?.PeopleLine ?? string.Empty;
        Display.Sync(People, room?.People ?? []);
        RoomNotice = room?.Notice ?? string.Empty;
        HasRoomNotice = RoomNotice.Length > 0;
        CanSpeak = room?.CanSpeak ?? false;
        Muted = room?.Muted ?? true;
        MuteLabel = Muted ? "Unmute" : "Mute";
        CanMute = room?.CanMute ?? false;
        _guestLink = room?.GuestLink;
        HasGuestLink = _guestLink is not null;
        CopyLinkLabel = room?.CopyLinkLabel ?? string.Empty;
        CopyLinkHint = room?.CopyLinkHint ?? string.Empty;
        LeaveLabel = room?.LeaveLabel ?? string.Empty;
        if (!InRoom || !wasInRoom)
        {
            // A new room has not had its link copied.
            CopiedNote = string.Empty;
            HasCopiedNote = false;
        }
    }

    /// <summary>The guest link to put on the clipboard, or null when there is none.</summary>
    internal string? GuestLink => _guestLink;

    /// <summary>
    /// The page copied the guest link (<paramref name="copied"/>), or Windows
    /// refused the clipboard: say it was copied only when it was.
    /// </summary>
    internal void LinkCopied(bool copied)
    {
        CopiedNote = copied ? View?.Room?.CopiedNote ?? string.Empty : string.Empty;
        HasCopiedNote = CopiedNote.Length > 0;
    }

    partial void OnRoomNameChanged(string value)
    {
        if (_writing)
        {
            return;
        }
        _echo.Typed(value);
        Send(new RoomsAction.EditRoomName(value));
    }

    /// <summary>Forwards one of the screen's actions.</summary>
    internal void Send(RoomsAction action) => _context?.Send(new UiEvent.Rooms(action));

    [RelayCommand(CanExecute = nameof(CanJoin))]
    private void Join() => Send(new RoomsAction.Join());

    [RelayCommand]
    private void Leave() => Send(new RoomsAction.Leave());

    /// <summary>Turns the microphone on when it is off, and off when it is on.</summary>
    [RelayCommand(CanExecute = nameof(CanMute))]
    private void ToggleMute() => Send(new RoomsAction.Microphone(Muted));

    [RelayCommand]
    private void DismissFailure() => Send(new RoomsAction.DismissFailure());

    /// <summary>Opens <paramref name="row"/>'s record over the lobby.</summary>
    internal void OpenRecord(MeetingRowItem row) => Send(new RoomsAction.OpenRecord(row.MeetingId));

    /// <summary>Joins the room of <paramref name="row"/>, a meeting still running.</summary>
    internal void Rejoin(MeetingRowItem row)
    {
        if (row.CanRejoin)
        {
            Send(new RoomsAction.Rejoin(row.MeetingId));
        }
    }

    [RelayCommand]
    private void CloseRecord() => Send(new RoomsAction.CloseRecord());
}

/// <summary>One meeting in the list.</summary>
/// <param name="MeetingId">The meeting.</param>
/// <param name="Title">Its title.</param>
/// <param name="When">When it started, in local time.</param>
/// <param name="Detail">How long, and how many were there.</param>
/// <param name="Minutes">The start of its minutes, or that they come when it ends; or empty.</param>
/// <param name="InProgress">"In progress", or empty.</param>
/// <param name="CanRejoin">Whether Rejoin is offered.</param>
/// <param name="RejoinLabel">Rejoin's words.</param>
public sealed record MeetingRowItem(string MeetingId, string Title, string When, string Detail, string Minutes, string InProgress, bool CanRejoin, string RejoinLabel)
{
    /// <summary>Whether there are <see cref="Minutes"/>.</summary>
    public bool HasMinutes => Minutes.Length > 0;

    /// <summary>Whether the meeting is running.</summary>
    public bool IsInProgress => InProgress.Length > 0;

    /// <summary>What a screen reader says for the row.</summary>
    public string AccessibleName =>
        string.Join(", ", new[] { Title, InProgress, When, Detail }.Where(part => part.Length > 0));

    /// <inheritdoc/>
    public override string ToString() => AccessibleName;

    internal static MeetingRowItem From(MeetingRowView row) => new(
        row.MeetingId,
        row.Title,
        Display.When(row.StartedAt),
        row.Detail,
        row.Minutes ?? string.Empty,
        row.InProgress ?? string.Empty,
        row.CanRejoin,
        row.RejoinLabel);
}
