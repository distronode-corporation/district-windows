using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels.Calls;

namespace DistrictAI.ViewModels.Desk;

/// <summary>
/// The help desk's queue, and the form that raises a ticket for a customer:
/// what the page shows, copied from the core's <see cref="DeskView"/>, and what
/// its buttons send. A desk that is switched off shows that, with the offer to
/// turn it on, in place of the queue: its empty queue would say nothing about
/// the customers. The form's boxes hold what was typed, which is what the core
/// keeps; every change sends the whole form, and whether it can be raised is
/// the core's to say.
/// </summary>
public sealed partial class DeskViewModel : ObservableObject
{
    private readonly TextEcho _subjectEcho = new();
    private readonly TextEcho _messageEcho = new();
    private readonly TextEcho _nameEcho = new();
    private readonly TextEcho _emailEcho = new();
    private readonly TextEcho _phoneEcho = new();
    private PageContext? _context;
    private bool _writing;
    private DeskFilter[] _filters = [];

    /// <summary>Loading, failure, the empty queue and refresh.</summary>
    public LoadStateViewModel Load { get; } = new();

    /// <summary>The tickets the filter shows.</summary>
    public ObservableCollection<DeskRowItem> Rows { get; } = [];

    /// <summary>The filter's choices, by their labels, each with its count once read.</summary>
    public ObservableCollection<string> FilterLabels { get; } = [];

    /// <summary>The core's view of the screen.</summary>
    [ObservableProperty]
    public partial DeskView? View { get; set; }

    /// <summary>The heading.</summary>
    [ObservableProperty]
    public partial string Title { get; set; } = "Help desk";

    /// <summary>Whether the queue shows: read, and the desk is on.</summary>
    [ObservableProperty]
    public partial bool ShowQueue { get; set; }

    /// <summary>Whether the desk is switched off.</summary>
    [ObservableProperty]
    public partial bool IsOff { get; set; }

    /// <summary>The switched-off state's heading.</summary>
    [ObservableProperty]
    public partial string OffTitle { get; set; } = string.Empty;

    /// <summary>The switched-off state's text.</summary>
    [ObservableProperty]
    public partial string OffBody { get; set; } = string.Empty;

    /// <summary>The button that turns the desk on.</summary>
    [ObservableProperty]
    public partial string TurnOnLabel { get; set; } = string.Empty;

    /// <summary>Whether the button that turns the desk on works.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TurnOnCommand))]
    public partial bool CanTurnOn { get; set; }

    /// <summary>Whether turning the desk on is on its way.</summary>
    [ObservableProperty]
    public partial bool Enabling { get; set; }

    /// <summary>Why turning the desk on failed, or empty.</summary>
    [ObservableProperty]
    public partial string EnableFailure { get; set; } = string.Empty;

    /// <summary>Whether there is an <see cref="EnableFailure"/>.</summary>
    [ObservableProperty]
    public partial bool HasEnableFailure { get; set; }

    /// <summary>Whether the filter can be used: the queue is read.</summary>
    [ObservableProperty]
    public partial bool CanFilter { get; set; }

    /// <summary>Which of <see cref="FilterLabels"/> is chosen.</summary>
    [ObservableProperty]
    public partial int FilterIndex { get; set; } = -1;

    /// <summary>Whether there are tickets to list.</summary>
    [ObservableProperty]
    public partial bool HasRows { get; set; }

    /// <summary>The line when the filter matches nothing, or empty.</summary>
    [ObservableProperty]
    public partial string NoneMatching { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="NoneMatching"/> line.</summary>
    [ObservableProperty]
    public partial bool HasNoneMatching { get; set; }

    /// <summary>The confirmation of a ticket raised, or empty.</summary>
    [ObservableProperty]
    public partial string Submitted { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="Submitted"/> confirmation.</summary>
    [ObservableProperty]
    public partial bool HasSubmitted { get; set; }

    /// <summary>Whether "New ticket" is offered.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartTicketCommand))]
    public partial bool CanStart { get; set; }

    /// <summary>Whether "Settings" is offered.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenSettingsCommand))]
    public partial bool CanOpenSettings { get; set; }

    /// <summary>Whether the form raising a ticket is open.</summary>
    [ObservableProperty]
    public partial bool Composing { get; set; }

    /// <summary>The form's heading.</summary>
    [ObservableProperty]
    public partial string ComposeTitle { get; set; } = string.Empty;

    /// <summary>The form's button that raises the ticket.</summary>
    [ObservableProperty]
    public partial string SubmitLabel { get; set; } = string.Empty;

    /// <summary>The subject, as typed.</summary>
    [ObservableProperty]
    public partial string Subject { get; set; } = string.Empty;

    /// <summary>The customer's problem, as typed.</summary>
    [ObservableProperty]
    public partial string Message { get; set; } = string.Empty;

    /// <summary>The customer's name, as typed.</summary>
    [ObservableProperty]
    public partial string RequesterName { get; set; } = string.Empty;

    /// <summary>The customer's email address, as typed.</summary>
    [ObservableProperty]
    public partial string RequesterEmail { get; set; } = string.Empty;

    /// <summary>The customer's phone number, as typed.</summary>
    [ObservableProperty]
    public partial string RequesterPhone { get; set; } = string.Empty;

    /// <summary>What the form says about the customer's details.</summary>
    [ObservableProperty]
    public partial string CustomerNote { get; set; } = string.Empty;

    /// <summary>The longest subject the service takes.</summary>
    [ObservableProperty]
    public partial int SubjectMax { get; set; }

    /// <summary>The longest message the service takes.</summary>
    [ObservableProperty]
    public partial int MessageMax { get; set; }

    /// <summary>What the form needs before it can be raised, or empty.</summary>
    [ObservableProperty]
    public partial string Needs { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="Needs"/> line.</summary>
    [ObservableProperty]
    public partial bool HasNeeds { get; set; }

    /// <summary>Whether the raise button works.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SubmitTicketCommand))]
    public partial bool CanSubmit { get; set; }

    /// <summary>Whether the ticket is on its way.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelTicketCommand))]
    public partial bool Submitting { get; set; }

    /// <summary>Whether the form can be changed (not while it is on its way).</summary>
    [ObservableProperty]
    public partial bool CanEdit { get; set; }

    /// <summary>Why the last attempt failed, or empty.</summary>
    [ObservableProperty]
    public partial string ComposeFailure { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="ComposeFailure"/>.</summary>
    [ObservableProperty]
    public partial bool HasComposeFailure { get; set; }

    internal void Attach(PageContext context)
    {
        _context = context;
        Load.Attach(context);
    }

    internal void Show(DeskView view)
    {
        View = view;
        Title = view.Title;
        Load.Show(view.Status, view.Rows.Length > 0 || view.Off is not null, view.Empty, view.Refreshing, null);
        IsOff = view.Off is not null;
        ShowQueue = view.Status is LoadStatus.Ready && !IsOff;
        OffTitle = view.Off?.Title ?? string.Empty;
        OffBody = view.Off?.Body ?? string.Empty;
        TurnOnLabel = view.Off?.TurnOn ?? string.Empty;
        CanTurnOn = view.Off?.CanTurnOn ?? false;
        Enabling = view.Off?.Enabling ?? false;
        EnableFailure = Display.Failure(view.Off?.Failure);
        HasEnableFailure = view.Off?.Failure is not null;
        ShowFilters(view.Filters);
        CanFilter = view.CanFilter;
        Display.Sync(Rows, [.. view.Rows.Select(DeskRowItem.From)]);
        HasRows = view.Rows.Length > 0;
        NoneMatching = view.NoneMatching ?? string.Empty;
        HasNoneMatching = view.NoneMatching is not null;
        Submitted = view.Submitted ?? string.Empty;
        HasSubmitted = view.Submitted is not null;
        CanStart = view.CanStart;
        CanOpenSettings = view.CanOpenSettings;
        ShowCompose(view.Compose);
    }

    private void ShowFilters(DeskFilterView[] filters)
    {
        _filters = [.. filters.Select(filter => filter.Filter)];
        _writing = true;
        try
        {
            Display.Sync(FilterLabels, [.. filters.Select(filter => filter.Label)]);
            FilterIndex = Array.FindIndex(filters, filter => filter.Selected);
        }
        finally
        {
            _writing = false;
        }
    }

    private void ShowCompose(DeskComposeView? compose)
    {
        Composing = compose is not null;
        ComposeTitle = compose?.Title ?? string.Empty;
        SubmitLabel = compose?.SubmitLabel ?? string.Empty;
        _writing = true;
        try
        {
            Subject = Echoed(_subjectEcho, compose?.Subject, Subject);
            Message = Echoed(_messageEcho, compose?.Message, Message);
            RequesterName = Echoed(_nameEcho, compose?.RequesterName, RequesterName);
            RequesterEmail = Echoed(_emailEcho, compose?.RequesterEmail, RequesterEmail);
            RequesterPhone = Echoed(_phoneEcho, compose?.RequesterPhone, RequesterPhone);
        }
        finally
        {
            _writing = false;
        }
        CustomerNote = compose?.CustomerNote ?? string.Empty;
        SubjectMax = (int)Math.Min(compose?.SubjectMax ?? 0, int.MaxValue);
        MessageMax = (int)Math.Min(compose?.MessageMax ?? 0, int.MaxValue);
        Needs = compose?.Needs ?? string.Empty;
        HasNeeds = compose?.Needs is not null;
        CanSubmit = compose?.CanSubmit ?? false;
        Submitting = compose?.Submitting ?? false;
        CanEdit = compose is { Submitting: false };
        ComposeFailure = Display.Failure(compose?.Failure);
        HasComposeFailure = compose?.Failure is not null;
    }

    /// <summary>What a box holding <paramref name="shown"/> is to hold, given the core's <paramref name="value"/>.</summary>
    private static string Echoed(TextEcho echo, string? value, string shown)
    {
        var core = value ?? string.Empty;
        return echo.Write(core, shown) ? core : shown;
    }

    partial void OnFilterIndexChanged(int value)
    {
        if (_writing || value < 0 || value >= _filters.Length)
        {
            return;
        }
        Send(new DeskAction.Filter(_filters[value]));
    }

    partial void OnSubjectChanged(string value) => Typed(_subjectEcho, value);

    partial void OnMessageChanged(string value) => Typed(_messageEcho, value);

    partial void OnRequesterNameChanged(string value) => Typed(_nameEcho, value);

    partial void OnRequesterEmailChanged(string value) => Typed(_emailEcho, value);

    partial void OnRequesterPhoneChanged(string value) => Typed(_phoneEcho, value);

    /// <summary>A box of the form now holds <paramref name="value"/>: the whole form is sent, when the person changed it.</summary>
    private void Typed(TextEcho echo, string value)
    {
        if (_writing || !Composing)
        {
            return;
        }
        echo.Typed(value);
        Send(new DeskAction.EditTicket(Subject, Message, RequesterName, RequesterEmail, RequesterPhone));
    }

    /// <summary>Opens <paramref name="row"/>'s ticket.</summary>
    internal void OpenTicket(DeskRowItem row) => Send(new DeskAction.OpenTicket(row.TicketId));

    /// <summary>Turns the desk on: turned off as it is pressed, so one press is one request.</summary>
    [RelayCommand(CanExecute = nameof(CanTurnOn))]
    private void TurnOn()
    {
        if (!CanTurnOn)
        {
            return;
        }
        CanTurnOn = false;
        Send(new DeskAction.TurnOn());
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private void StartTicket() => Send(new DeskAction.StartTicket());

    [RelayCommand(CanExecute = nameof(CanOpenSettings))]
    private void OpenSettings() => Send(new DeskAction.OpenSettings());

    /// <summary>Raises the ticket: turned off as it is pressed, so one press is one ticket.</summary>
    [RelayCommand(CanExecute = nameof(CanSubmit))]
    private void SubmitTicket()
    {
        if (!CanSubmit)
        {
            return;
        }
        CanSubmit = false;
        Send(new DeskAction.SubmitTicket());
    }

    [RelayCommand(CanExecute = nameof(CanDiscard))]
    private void CancelTicket() => Send(new DeskAction.CancelTicket());

    [RelayCommand]
    private void DismissSubmitted() => Send(new DeskAction.DismissSubmitted());

    private bool CanDiscard() => !Submitting;

    /// <summary>Forwards one of the screen's actions.</summary>
    internal void Send(DeskAction action) => _context?.Send(new UiEvent.Desk(action));
}

/// <summary>A ticket in the queue.</summary>
/// <param name="TicketId">What opens it.</param>
/// <param name="Subject">The subject.</param>
/// <param name="Line">Its reference, who raised it, and when it last changed.</param>
/// <param name="StatusLabel">Its status, short ("Waiting").</param>
/// <param name="Status">Its status, when this build knows it.</param>
public sealed record DeskRowItem(string TicketId, string Subject, string Line, string StatusLabel, DeskStatus? Status)
{
    /// <summary>Whether it waits on the workspace (its badge stands out).</summary>
    public bool IsOpen => Status == DeskStatus.Open;

    /// <summary>Whether its badge is quiet: it waits on the customer, is resolved, or has a status this build does not know.</summary>
    public bool IsQuiet => !IsOpen;

    /// <summary>What a screen reader says for the row.</summary>
    public string AccessibleName => string.Join(", ", new[] { Subject, StatusLabel, Line }.Where(part => part.Length > 0));

    internal static DeskRowItem From(DeskRowView row)
    {
        var line = string.Join(
            " · ",
            new[] { row.Reference, row.Requester, Display.When(row.UpdatedAt) }.Where(part => part.Length > 0));
        return new DeskRowItem(row.TicketId, row.Subject, line, row.StatusLabel, row.Status);
    }

    /// <inheritdoc/>
    public override string ToString() => AccessibleName;
}
