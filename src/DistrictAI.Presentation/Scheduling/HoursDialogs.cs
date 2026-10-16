using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DistrictAI.Core.Ffi;
using DistrictAI.L10n;

namespace DistrictAI.ViewModels.Scheduling;

/// <summary>
/// The week's editor (the core's <see cref="HoursWeekView"/>): every day,
/// Monday first, with its windows, Add range, Copy to all weekdays on a
/// weekday with a window, and Not bookable on a day with none. Its save is
/// the dialog's confirm, offered once the week is read and every window is
/// sound; what the save came to shows in the dialog until it is put away.
/// </summary>
public sealed partial class HoursWeekViewModel : ObservableObject
{
    private PageContext? _context;

    /// <summary>The week's editor, with nothing shown yet.</summary>
    public HoursWeekViewModel()
    {
        Dialog = new AdminDialogViewModel(
            () => SchedulingHoursViewModel.Event(new HoursAction.SaveWeek()),
            () => SchedulingHoursViewModel.Event(new HoursAction.CloseDialog()));
    }

    /// <summary>The dialog's title, save, way out and the save's failure.</summary>
    public AdminDialogViewModel Dialog { get; }

    /// <summary>The days, Monday first; none until the week is read.</summary>
    public ObservableCollection<HoursDayEntry> Days { get; } = [];

    /// <summary>Whether the week is being read.</summary>
    [ObservableProperty]
    public partial bool Loading { get; set; }

    /// <summary>Whether its read failed.</summary>
    [ObservableProperty]
    public partial bool Failed { get; set; }

    /// <summary>Why its read failed.</summary>
    [ObservableProperty]
    public partial string FailureMessage { get; set; } = string.Empty;

    /// <summary>Whether "Try again" is offered for the failed read.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RetryCommand))]
    public partial bool CanRetry { get; set; }

    /// <summary>Whether the windows can be changed: the week is read and no save is on its way.</summary>
    [ObservableProperty]
    public partial bool Editable { get; set; }

    /// <summary>What the last save came to, or empty.</summary>
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

    /// <summary>Shows <paramref name="view"/>, or closes the editor when the core holds none.</summary>
    internal void Show(HoursWeekView? view)
    {
        Dialog.Show(view?.Dialog);
        if (view is null)
        {
            Days.Clear();
            return;
        }
        var failure = (view.Status as LoadStatus.Failed)?.Failure;
        Loading = view.Status is LoadStatus.Loading;
        Failed = failure is not null;
        FailureMessage = Display.Failure(failure);
        CanRetry = failure?.Retryable ?? false;
        Editable = view.Editable;
        Notice = view.Notice ?? string.Empty;
        HasNotice = view.Notice is not null;
        if (Days.Count != view.Days.Length)
        {
            Days.Clear();
            foreach (var day in view.Days)
            {
                Days.Add(new HoursDayEntry(day.Day, day.Name, view.AddRangeLabel, view.CopyLabel, view.NotBookable));
            }
        }
        for (var i = 0; i < view.Days.Length; i++)
        {
            Days[i].Show(view.Days[i], view.ToLabel, view.RemoveLabel, view.StartPlaceholder, view.EndPlaceholder);
        }
    }

    /// <summary>Adds a window to <paramref name="day"/>, 9 to 5.</summary>
    internal void AddRange(HoursDayEntry day) => Send(new HoursAction.AddRange(day.Day));

    /// <summary>Gives every other weekday <paramref name="day"/>'s windows.</summary>
    internal void CopyToWeekdays(HoursDayEntry day) => Send(new HoursAction.CopyToWeekdays(day.Day));

    /// <summary>Takes <paramref name="range"/> off.</summary>
    internal void Remove(HoursRangeEntry range) => Send(new HoursAction.RemoveRange(range.Day, range.Key));

    /// <summary><paramref name="range"/>'s opening box now holds <paramref name="value"/>; what it last sent sends nothing.</summary>
    internal void SetStart(HoursRangeEntry range, string value)
    {
        if (range.Start == value)
        {
            return;
        }
        range.Start = value;
        Send(new HoursAction.SetRangeStart(range.Day, range.Key, value));
    }

    /// <summary><paramref name="range"/>'s closing box now holds <paramref name="value"/>; what it last sent sends nothing.</summary>
    internal void SetEnd(HoursRangeEntry range, string value)
    {
        if (range.End == value)
        {
            return;
        }
        range.End = value;
        Send(new HoursAction.SetRangeEnd(range.Day, range.Key, value));
    }

    [RelayCommand]
    private void DismissNotice() => Send(new HoursAction.DismissNotice());

    /// <summary>"Try again": the editor reads its hours again.</summary>
    [RelayCommand(CanExecute = nameof(CanRetry))]
    private void Retry()
    {
        CanRetry = false;
        Send(new HoursAction.RetryRead());
    }

    private void Send(HoursAction action) => _context?.Send(SchedulingHoursViewModel.Event(action));
}

/// <summary>
/// The overrides' editor (the core's <see cref="HoursOverridesView"/>): the
/// days still to come, a run of days as one row, each with Delete (asked once
/// more in the dialog); Add override; and the add form in place of the list
/// while it is open, its kind deciding its fields. What the last write came
/// to shows until it is put away.
/// </summary>
/// <remarks>
/// The form's fields are written as it opens, then only by the member; each
/// change sends the whole form to the core, which says why it cannot be sent.
/// </remarks>
public sealed partial class HoursOverridesViewModel : ObservableObject
{
    private string[] _kinds = [];
    private string _openedKind = string.Empty;
    private PageContext? _context;
    private bool _writing;

    /// <summary>The overrides' editor, with nothing shown yet.</summary>
    public HoursOverridesViewModel()
    {
        // It has no confirm (the form has its own save), so only the way out
        // is ever sent.
        Dialog = new AdminDialogViewModel(
            () => SchedulingHoursViewModel.Event(new HoursAction.CloseDialog()),
            () => SchedulingHoursViewModel.Event(new HoursAction.CloseDialog()));
    }

    /// <summary>The dialog's title, its way out and why the last write failed.</summary>
    public AdminDialogViewModel Dialog { get; }

    /// <summary>The days still to come.</summary>
    public ObservableCollection<OverrideEntry> Rows { get; } = [];

    /// <summary>The kinds of day.</summary>
    public ObservableCollection<ChoiceEntry> Kinds { get; } = [];

    /// <summary>The form's labels and hints, or null while it is closed.</summary>
    [ObservableProperty]
    public partial OverrideEditorView? Words { get; set; }

    /// <summary>What the editor is for.</summary>
    [ObservableProperty]
    public partial string Hint { get; set; } = string.Empty;

    /// <summary>Whether the overrides are being read.</summary>
    [ObservableProperty]
    public partial bool Loading { get; set; }

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

    /// <summary>Whether the overrides are read and none is to come.</summary>
    [ObservableProperty]
    public partial bool ShowEmpty { get; set; }

    /// <summary>What to say then.</summary>
    [ObservableProperty]
    public partial string EmptyText { get; set; } = string.Empty;

    /// <summary>"Add override".</summary>
    [ObservableProperty]
    public partial string AddLabel { get; set; } = string.Empty;

    /// <summary>Whether an override can be added or deleted: they are read and nothing is on its way.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddOverrideCommand))]
    public partial bool CanChange { get; set; }

    /// <summary>What the last write came to, or empty.</summary>
    [ObservableProperty]
    public partial string Notice { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="Notice"/>.</summary>
    [ObservableProperty]
    public partial bool HasNotice { get; set; }

    /// <summary>Whether the add form is open, in place of the list.</summary>
    [ObservableProperty]
    public partial bool Adding { get; set; }

    /// <summary>Whether the list shows: the form is closed.</summary>
    [ObservableProperty]
    public partial bool ShowsList { get; set; } = true;

    /// <summary>Which of <see cref="Kinds"/> is chosen, or -1.</summary>
    [ObservableProperty]
    public partial int KindIndex { get; set; } = -1;

    /// <summary>The (first) day, as typed.</summary>
    [ObservableProperty]
    public partial string Date { get; set; } = string.Empty;

    /// <summary>A day off's last day, as typed.</summary>
    [ObservableProperty]
    public partial string EndDate { get; set; } = string.Empty;

    /// <summary>Custom hours' opening, as typed.</summary>
    [ObservableProperty]
    public partial string Start { get; set; } = string.Empty;

    /// <summary>Custom hours' closing, as typed.</summary>
    [ObservableProperty]
    public partial string End { get; set; } = string.Empty;

    /// <summary>"First day", or "Date" for custom hours.</summary>
    [ObservableProperty]
    public partial string DateLabel { get; set; } = string.Empty;

    /// <summary>Whether the last day shows: a day off.</summary>
    [ObservableProperty]
    public partial bool ShowsLastDay { get; set; }

    /// <summary>Whether the hours show: custom hours.</summary>
    [ObservableProperty]
    public partial bool ShowsHours { get; set; }

    /// <summary>What a screen reader says for custom hours' opening: "Opens: Hours that day".</summary>
    [ObservableProperty]
    public partial string StartName { get; set; } = string.Empty;

    /// <summary>What a screen reader says for custom hours' closing: "Closes: Hours that day".</summary>
    [ObservableProperty]
    public partial string EndName { get; set; } = string.Empty;

    /// <summary>Whether the form's save can be pressed.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveOverrideCommand))]
    public partial bool CanSave { get; set; }

    /// <summary>Why the override cannot be sent, or empty.</summary>
    [ObservableProperty]
    public partial string Validation { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="Validation"/>.</summary>
    [ObservableProperty]
    public partial bool HasValidation { get; set; }

    /// <summary>Whether a row's delete is being asked about.</summary>
    [ObservableProperty]
    public partial bool Asking { get; set; }

    /// <summary>"Delete this override?".</summary>
    [ObservableProperty]
    public partial string AskTitle { get; set; } = string.Empty;

    /// <summary>What goes back to the weekly hours: one day, or a run's days.</summary>
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

    /// <summary>Shows <paramref name="view"/>, or closes the editor when the core holds none.</summary>
    internal void Show(HoursOverridesView? view)
    {
        Dialog.Show(view?.Dialog);
        if (view is null)
        {
            ShowForm(null);
            return;
        }
        var failure = (view.Status as LoadStatus.Failed)?.Failure;
        Hint = view.Hint;
        Loading = view.Status is LoadStatus.Loading;
        Failed = failure is not null;
        FailureMessage = Display.Failure(failure);
        CanRetry = failure?.Retryable ?? false;
        Display.Sync(Rows, [.. view.Rows.Select(row => OverrideEntry.From(row, view.DeleteLabel))]);
        ShowEmpty = view.Status is LoadStatus.Ready && view.Rows.Length == 0;
        EmptyText = view.Empty;
        AddLabel = view.AddLabel;
        CanChange = view.CanChange;
        Notice = view.Notice ?? string.Empty;
        HasNotice = view.Notice is not null;
        Asking = view.Delete is not null;
        AskTitle = view.Delete?.Title ?? string.Empty;
        AskBody = view.Delete?.Body ?? string.Empty;
        AskConfirm = view.Delete?.Confirm ?? string.Empty;
        AskCancel = view.Delete?.Cancel ?? string.Empty;
        ShowForm(view.Form);
    }

    /// <summary>Chooses the kind of day at <paramref name="index"/>; the one chosen sends nothing.</summary>
    internal void PickKind(int index)
    {
        if (index < 0 || index >= _kinds.Length || index == KindIndex)
        {
            return;
        }
        KindIndex = index;
        Edited();
    }

    /// <summary>Asks about deleting <paramref name="row"/>; nothing while a write is on its way.</summary>
    internal void AskDelete(OverrideEntry row)
    {
        if (CanChange)
        {
            Send(new HoursAction.AskDeleteOverride(row.Key));
        }
    }

    /// <summary>The form as the fields hold it.</summary>
    internal OverrideFormView Form() => new(
        KindIndex >= 0 && KindIndex < _kinds.Length ? _kinds[KindIndex] : _openedKind,
        Date,
        EndDate,
        Start,
        End);

    [RelayCommand(CanExecute = nameof(CanChange))]
    private void AddOverride() => Send(new HoursAction.AddOverride());

    /// <summary>Adds the override: off as it is pressed, so one press is one write.</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private void SaveOverride()
    {
        CanSave = false;
        Send(new HoursAction.SaveOverride());
    }

    [RelayCommand]
    private void CancelOverride() => Send(new HoursAction.CancelOverride());

    /// <summary>Deletes the row asked about (a run whole).</summary>
    [RelayCommand]
    private void ConfirmDelete()
    {
        if (Asking)
        {
            Asking = false;
            Send(new HoursAction.ConfirmDeleteOverride());
        }
    }

    [RelayCommand]
    private void CancelDelete() => Send(new HoursAction.CancelDeleteOverride());

    [RelayCommand]
    private void DismissNotice() => Send(new HoursAction.DismissNotice());

    partial void OnDateChanged(string value) => Edited();

    partial void OnEndDateChanged(string value) => Edited();

    partial void OnStartChanged(string value) => Edited();

    partial void OnEndChanged(string value) => Edited();

    /// <summary>Shows the add form, writing its fields when it has just opened.</summary>
    private void ShowForm(OverrideEditorView? form)
    {
        var opened = form is not null && !Adding;
        Adding = form is not null;
        ShowsList = form is null;
        Words = form;
        if (form is null)
        {
            return;
        }
        DateLabel = form.DateLabel;
        ShowsLastDay = form.ShowsLastDay;
        ShowsHours = form.ShowsHours;
        StartName = S.SchedulingHoursOpens + ": " + form.HoursLabel;
        EndName = S.SchedulingHoursCloses + ": " + form.HoursLabel;
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
            _openedKind = form.Form.Reason;
            Date = form.Form.Date;
            EndDate = form.Form.EndDate;
            Start = form.Form.Start;
            End = form.Form.End;
            _kinds = [.. form.Kinds.Select(choice => choice.Value)];
            Display.Sync(Kinds, [.. form.Kinds.Select(ChoiceEntry.From)]);
            KindIndex = Array.FindIndex(form.Kinds, choice => choice.Selected);
        }
        finally
        {
            _writing = false;
        }
    }

    /// <summary>A field of the form changed: the whole form goes to the core, when the member changed it.</summary>
    private void Edited()
    {
        if (_writing || !Adding)
        {
            return;
        }
        Send(new HoursAction.EditOverride(Form()));
    }

    /// <summary>"Try again": the editor reads its hours again.</summary>
    [RelayCommand(CanExecute = nameof(CanRetry))]
    private void Retry()
    {
        CanRetry = false;
        Send(new HoursAction.RetryRead());
    }

    private void Send(HoursAction action) => _context?.Send(SchedulingHoursViewModel.Event(action));
}
