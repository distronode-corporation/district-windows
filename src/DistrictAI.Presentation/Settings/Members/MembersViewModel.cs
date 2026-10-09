using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels.Calls;

namespace DistrictAI.ViewModels.Settings.Members;

/// <summary>A role to choose, with what it may do.</summary>
/// <param name="Role">The role.</param>
/// <param name="Label">Its name.</param>
/// <param name="Description">What it may do.</param>
public sealed record RoleItem(MemberRoleView Role, string Label, string Description)
{
    /// <inheritdoc/>
    public override string ToString() => Label;
}

/// <summary>
/// One member: the address as the service stores it, their role's line, and,
/// for an agency member, their role to change and a Remove button.
/// </summary>
public sealed partial class MemberItem : ObservableObject
{
    private readonly MembersViewModel _owner;
    private bool _writing;

    internal MemberItem(MembersViewModel owner, MemberRowView row, IReadOnlyList<RoleItem> roles)
    {
        _owner = owner;
        Row = row;
        Roles = roles;
        Reset();
    }

    /// <summary>The core's row.</summary>
    public MemberRowView Row { get; }

    /// <summary>The address, as the service stores it.</summary>
    public string Email => Row.Email;

    /// <summary>Their role's name and what it may do.</summary>
    public string RoleLine => Row.RoleLine;

    /// <summary>Whether their role can be changed and they can be removed now.</summary>
    public bool CanChange => Row.CanChange;

    /// <summary>The Remove button's words.</summary>
    public string RemoveLabel => Row.RemoveLabel;

    /// <summary>What a screen reader says for the row.</summary>
    public string AccessibleName => Email + ", " + RoleLine;

    /// <summary>The roles to choose from.</summary>
    public IReadOnlyList<RoleItem> Roles { get; }

    /// <summary>Their role, chosen; choosing another asks the core to change it.</summary>
    [ObservableProperty]
    public partial RoleItem? SelectedRole { get; set; }

    /// <summary>Shows the stored role again: a change refused, or not made yet, never shows as made.</summary>
    internal void Reset()
    {
        _writing = true;
        SelectedRole = Roles.FirstOrDefault(role => role.Role == Row.Role);
        _writing = false;
    }

    partial void OnSelectedRoleChanged(RoleItem? value)
    {
        if (!_writing && value is not null && value.Role != Row.Role)
        {
            _owner.Send(new MembersAction.ChangeRole(Email, value.Role));
        }
    }

    /// <summary>Asks before removing them.</summary>
    [RelayCommand]
    private void Remove() => _owner.Send(new MembersAction.AskRemove(Email));
}

/// <summary>
/// The members section: who belongs here and their roles, adding (which sends
/// no invitation), changing a role and removing (asked first), for an agency
/// member; renaming the workspace for an agency or client member. Every word
/// is the core's.
/// </summary>
public sealed partial class MembersViewModel : ObservableObject
{
    private readonly TextEcho _emailEcho = new();
    private readonly TextEcho _nameEcho = new();
    private PageContext? _context;
    private bool _writing;
    private MembersView? _drawn;

    /// <summary>The members, in the service's order.</summary>
    public ObservableCollection<MemberItem> Members { get; } = [];

    /// <summary>The roles, in the order offered.</summary>
    public ObservableCollection<RoleItem> Roles { get; } = [];

    /// <summary>The core's view of the screen, as last shown.</summary>
    [ObservableProperty]
    public partial MembersView? View { get; set; }

    /// <summary>The heading.</summary>
    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;

    /// <summary>Whether the members are being read.</summary>
    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    /// <summary>Whether the section shows.</summary>
    [ObservableProperty]
    public partial bool IsReady { get; set; }

    /// <summary>Whether the read failed.</summary>
    [ObservableProperty]
    public partial bool HasStatus { get; set; }

    /// <summary>The failure's heading.</summary>
    [ObservableProperty]
    public partial string StatusTitle { get; set; } = string.Empty;

    /// <summary>Why.</summary>
    [ObservableProperty]
    public partial string StatusBody { get; set; } = string.Empty;

    /// <summary>Whether "Try again" is offered.</summary>
    [ObservableProperty]
    public partial bool CanRetry { get; set; }

    /// <summary>Whether a change of a member is on its way.</summary>
    [ObservableProperty]
    public partial bool Changing { get; set; }

    /// <summary>Why this member cannot change who belongs here, or empty.</summary>
    [ObservableProperty]
    public partial string ReadOnlyNote { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="ReadOnlyNote"/>.</summary>
    [ObservableProperty]
    public partial bool HasReadOnlyNote { get; set; }

    /// <summary>Whether the notice shows.</summary>
    [ObservableProperty]
    public partial bool HasNotice { get; set; }

    /// <summary>"Saved.", or the service's reason.</summary>
    [ObservableProperty]
    public partial string NoticeText { get; set; } = string.Empty;

    /// <summary>Whether the notice is a success.</summary>
    [ObservableProperty]
    public partial bool NoticeSaved { get; set; }

    /// <summary>Whether the add form shows (agency only).</summary>
    [ObservableProperty]
    public partial bool CanManage { get; set; }

    /// <summary>The add form's heading.</summary>
    [ObservableProperty]
    public partial string AddHeading { get; set; } = string.Empty;

    /// <summary>The address box's label.</summary>
    [ObservableProperty]
    public partial string EmailLabel { get; set; } = string.Empty;

    /// <summary>The address, as typed.</summary>
    [ObservableProperty]
    public partial string Email { get; set; } = string.Empty;

    /// <summary>The role picker's label.</summary>
    [ObservableProperty]
    public partial string RoleLabel { get; set; } = string.Empty;

    /// <summary>The role the address will get.</summary>
    [ObservableProperty]
    public partial RoleItem? NewRole { get; set; }

    /// <summary>Whether the add form can be edited.</summary>
    [ObservableProperty]
    public partial bool AddEditable { get; set; }

    /// <summary>Whether "Add" can be pressed: nothing on its way. The core says whether it is an address.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    public partial bool CanAdd { get; set; }

    /// <summary>Why the last Add was refused, or empty.</summary>
    [ObservableProperty]
    public partial string AddRejected { get; set; } = string.Empty;

    /// <summary>Whether there is an <see cref="AddRejected"/>.</summary>
    [ObservableProperty]
    public partial bool HasAddRejected { get; set; }

    /// <summary>That adding someone sends no invitation, in the core's words.</summary>
    [ObservableProperty]
    public partial string AddNote { get; set; } = string.Empty;

    /// <summary>The add button's words.</summary>
    [ObservableProperty]
    public partial string AddLabel { get; set; } = string.Empty;

    /// <summary>Whether the rename form shows (agency and client).</summary>
    [ObservableProperty]
    public partial bool CanRenameHere { get; set; }

    /// <summary>The rename form's heading.</summary>
    [ObservableProperty]
    public partial string RenameHeading { get; set; } = string.Empty;

    /// <summary>The label of the name now.</summary>
    [ObservableProperty]
    public partial string CurrentLabel { get; set; } = string.Empty;

    /// <summary>The name now.</summary>
    [ObservableProperty]
    public partial string CurrentName { get; set; } = string.Empty;

    /// <summary>The new name box's label.</summary>
    [ObservableProperty]
    public partial string NewNameLabel { get; set; } = string.Empty;

    /// <summary>The new name, as typed.</summary>
    [ObservableProperty]
    public partial string NewName { get; set; } = string.Empty;

    /// <summary>Whether the name box can be edited.</summary>
    [ObservableProperty]
    public partial bool NameEditable { get; set; }

    /// <summary>Whether "Rename the workspace" works.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RenameCommand))]
    public partial bool CanRename { get; set; }

    /// <summary>Whether a rename is on its way.</summary>
    [ObservableProperty]
    public partial bool Renaming { get; set; }

    /// <summary>The rename button's words.</summary>
    [ObservableProperty]
    public partial string RenameLabel { get; set; } = string.Empty;

    /// <summary>"Remove this member?", while it is asked.</summary>
    [ObservableProperty]
    public partial RemoveQuestionView? Question { get; set; }

    internal void Attach(PageContext context) => _context = context;

    internal void Show(MembersView view)
    {
        View = view;
        Title = view.Title;
        IsLoading = view.Status is SectionStatus.Loading;
        IsReady = view.Status is SectionStatus.Ready;
        var failed = view.Status as SectionStatus.Failed;
        HasStatus = failed is not null;
        StatusTitle = failed?.Title ?? string.Empty;
        StatusBody = Display.Failure(failed?.Failure);
        CanRetry = failed?.Failure.Retryable ?? false;
        Changing = view.Changing;
        ReadOnlyNote = view.ReadOnlyNote ?? string.Empty;
        HasReadOnlyNote = ReadOnlyNote.Length > 0;
        HasNotice = view.Notice is not null;
        NoticeText = view.Notice?.Message ?? string.Empty;
        NoticeSaved = view.Notice?.Saved ?? false;
        Display.Sync(Roles, [.. view.Roles.Select(role => new RoleItem(role.Role, role.Label, role.Description))]);
        ShowMembers(view);
        ShowAdd(view.Add);
        ShowRename(view.Rename);
        Question = view.ConfirmRemove;
    }

    /// <summary>The rows, made again only when the members, or what may be done to them, change.</summary>
    private void ShowMembers(MembersView view)
    {
        if (_drawn is not null && _drawn.Members.SequenceEqual(view.Members))
        {
            foreach (var member in Members)
            {
                member.Reset();
            }
            return;
        }
        _drawn = view;
        Members.Clear();
        foreach (var row in view.Members)
        {
            Members.Add(new MemberItem(this, row, [.. Roles]));
        }
    }

    private void ShowAdd(AddMemberView? add)
    {
        CanManage = add is not null;
        AddHeading = add?.Heading ?? string.Empty;
        EmailLabel = add?.EmailLabel ?? string.Empty;
        RoleLabel = add?.RoleLabel ?? string.Empty;
        AddEditable = add?.Editable ?? false;
        CanAdd = add?.Editable ?? false;
        AddRejected = add?.Rejected ?? string.Empty;
        HasAddRejected = AddRejected.Length > 0;
        AddNote = add?.Note ?? string.Empty;
        AddLabel = add?.AddLabel ?? string.Empty;
        _writing = true;
        try
        {
            if (add is not null && _emailEcho.Write(add.Email, Email))
            {
                Email = add.Email;
            }
            NewRole = add is null ? null : Roles.FirstOrDefault(role => role.Role == add.Role);
        }
        finally
        {
            _writing = false;
        }
    }

    private void ShowRename(RenameView? rename)
    {
        CanRenameHere = rename is not null;
        RenameHeading = rename?.Heading ?? string.Empty;
        CurrentLabel = rename?.CurrentLabel ?? string.Empty;
        CurrentName = rename?.Current ?? string.Empty;
        NewNameLabel = rename?.NewLabel ?? string.Empty;
        NameEditable = rename?.Editable ?? false;
        CanRename = rename?.CanRename ?? false;
        Renaming = rename?.Renaming ?? false;
        RenameLabel = rename?.RenameLabel ?? string.Empty;
        if (rename is not null && _nameEcho.Write(rename.NewName, NewName))
        {
            _writing = true;
            try
            {
                NewName = rename.NewName;
            }
            finally
            {
                _writing = false;
            }
        }
    }

    partial void OnEmailChanged(string value)
    {
        if (_writing)
        {
            return;
        }
        _emailEcho.Typed(value);
        Send(new MembersAction.EditEmail(value));
    }

    partial void OnNewRoleChanged(RoleItem? value)
    {
        if (!_writing && value is not null)
        {
            Send(new MembersAction.SetRole(value.Role));
        }
    }

    partial void OnNewNameChanged(string value)
    {
        if (_writing)
        {
            return;
        }
        _nameEcho.Typed(value);
        Send(new MembersAction.EditName(value));
    }

    /// <summary>Forwards one of the screen's actions.</summary>
    internal void Send(MembersAction action) => _context?.Send(new UiEvent.Members(action));

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private void Add() => Send(new MembersAction.Add());

    [RelayCommand(CanExecute = nameof(CanRename))]
    private void Rename() => Send(new MembersAction.Rename());

    [RelayCommand]
    private void DismissNotice() => Send(new MembersAction.DismissNotice());

    [RelayCommand]
    private void Retry() => _context?.Send(new UiEvent.Refresh());

    /// <summary>The removal question was answered: <paramref name="remove"/> for Remove.</summary>
    internal void Answer(bool remove) => Send(remove ? new MembersAction.ConfirmRemove() : new MembersAction.CancelRemove());
}
