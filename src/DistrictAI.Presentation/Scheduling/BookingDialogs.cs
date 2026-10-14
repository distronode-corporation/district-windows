using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DistrictAI.Core.Ffi;

namespace DistrictAI.ViewModels.Scheduling;

/// <summary>
/// A list to choose one from, read for a dialog (a day's free times, the
/// hosts who can take a booking): being read, a read that failed with "Try
/// again", nothing to choose (a fact, said in the core's words), or the
/// choices.
/// </summary>
public sealed partial class ChoiceListViewModel : ObservableObject
{
    private readonly Func<string, UiEvent> _pick;
    private readonly Func<UiEvent> _retry;
    private PageContext? _context;

    internal ChoiceListViewModel(Func<string, UiEvent> pick, Func<UiEvent> retry)
    {
        _pick = pick;
        _retry = retry;
    }

    /// <summary>The choices, in the core's order.</summary>
    public ObservableCollection<ChoiceEntry> Choices { get; } = [];

    /// <summary>Which of <see cref="Choices"/> is chosen, or -1.</summary>
    [ObservableProperty]
    public partial int SelectedIndex { get; set; } = -1;

    /// <summary>Whether the list is being read.</summary>
    [ObservableProperty]
    public partial bool Loading { get; set; }

    /// <summary>Whether the read failed.</summary>
    [ObservableProperty]
    public partial bool Failed { get; set; }

    /// <summary>Why the read failed.</summary>
    [ObservableProperty]
    public partial string FailureMessage { get; set; } = string.Empty;

    /// <summary>Whether "Try again" is offered for the failed read.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RetryCommand))]
    public partial bool CanRetry { get; set; }

    /// <summary>Whether the list is read and has nothing to choose.</summary>
    [ObservableProperty]
    public partial bool ShowEmpty { get; set; }

    /// <summary>What to say then.</summary>
    [ObservableProperty]
    public partial string EmptyText { get; set; } = string.Empty;

    /// <summary>Whether there are choices to show.</summary>
    [ObservableProperty]
    public partial bool HasChoices { get; set; }

    internal void Attach(PageContext context) => _context = context;

    internal void Show(LoadStatus status, ChoiceRowView[] choices, string empty)
    {
        var failure = (status as LoadStatus.Failed)?.Failure;
        Loading = status is LoadStatus.Loading;
        Failed = failure is not null;
        FailureMessage = Display.Failure(failure);
        CanRetry = failure?.Retryable ?? false;
        Display.Sync(Choices, [.. choices.Select(ChoiceEntry.From)]);
        HasChoices = Choices.Count > 0;
        ShowEmpty = status is LoadStatus.Ready && Choices.Count == 0;
        EmptyText = empty;
        SelectedIndex = Array.FindIndex(choices, choice => choice.Selected);
    }

    /// <summary>Chooses the entry at <paramref name="index"/>; choosing the one chosen sends nothing.</summary>
    internal void Pick(int index)
    {
        if (index < 0 || index >= Choices.Count || index == SelectedIndex)
        {
            return;
        }
        _context?.Send(_pick(Choices[index].Value));
    }

    [RelayCommand(CanExecute = nameof(CanRetry))]
    private void Retry()
    {
        CanRetry = false;
        _context?.Send(_retry());
    }
}

/// <summary>
/// Cancelling a booking (the core's <see cref="CancelDialogView"/>): an
/// optional reason for the attendee's email, and the confirm, which is
/// destructive.
/// </summary>
public sealed partial class CancelBookingViewModel : ObservableObject
{
    private readonly Func<string, UiEvent> _edit;
    private PageContext? _context;

    internal CancelBookingViewModel(Func<UiEvent> confirm, Func<UiEvent> close, Func<string, UiEvent> edit)
    {
        Dialog = new AdminDialogViewModel(confirm, close);
        _edit = edit;
    }

    /// <summary>The dialog's title, buttons and write.</summary>
    public AdminDialogViewModel Dialog { get; }

    /// <summary>The reason field's label.</summary>
    [ObservableProperty]
    public partial string ReasonLabel { get; set; } = string.Empty;

    /// <summary>Under it: optional, and it goes in the email.</summary>
    [ObservableProperty]
    public partial string ReasonHint { get; set; } = string.Empty;

    /// <summary>The reason, as the core holds it.</summary>
    [ObservableProperty]
    public partial string Reason { get; set; } = string.Empty;

    /// <summary>Why the reason cannot be sent, or empty.</summary>
    [ObservableProperty]
    public partial string ReasonRejected { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="ReasonRejected"/>.</summary>
    [ObservableProperty]
    public partial bool HasReasonRejected { get; set; }

    internal void Attach(PageContext context)
    {
        _context = context;
        Dialog.Attach(context);
    }

    internal void Show(CancelDialogView? view)
    {
        Dialog.Show(view?.Dialog);
        if (view is null)
        {
            return;
        }
        ReasonLabel = view.ReasonLabel;
        ReasonHint = view.ReasonHint;
        Reason = view.Reason;
        ReasonRejected = view.ReasonRejected ?? string.Empty;
        HasReasonRejected = view.ReasonRejected is not null;
    }

    /// <summary>The reason field now holds <paramref name="reason"/>; the core's own value sends nothing.</summary>
    internal void EditReason(string reason)
    {
        if (reason == Reason)
        {
            return;
        }
        Reason = reason;
        _context?.Send(_edit(reason));
    }
}

/// <summary>
/// Moving a booking (the core's <see cref="RescheduleDialogView"/>): a day,
/// its free times in the scheduler profile's zone, and the confirm, which
/// waits for a time.
/// </summary>
public sealed partial class RescheduleBookingViewModel : ObservableObject
{
    private readonly Func<string, UiEvent> _pickDay;
    private PageContext? _context;

    internal RescheduleBookingViewModel(
        Func<UiEvent> confirm,
        Func<UiEvent> close,
        Func<string, UiEvent> pickDay,
        Func<string, UiEvent> pickSlot,
        Func<UiEvent> retry)
    {
        Dialog = new AdminDialogViewModel(confirm, close);
        Slots = new ChoiceListViewModel(pickSlot, retry);
        _pickDay = pickDay;
    }

    /// <summary>The dialog's title, buttons and write.</summary>
    public AdminDialogViewModel Dialog { get; }

    /// <summary>The day's free times.</summary>
    public ChoiceListViewModel Slots { get; }

    /// <summary>"Times in America/Toronto".</summary>
    [ObservableProperty]
    public partial string TimesIn { get; set; } = string.Empty;

    /// <summary>The day picker's label.</summary>
    [ObservableProperty]
    public partial string DayLabel { get; set; } = string.Empty;

    /// <summary>The day whose times show, <c>yyyy-MM-dd</c>.</summary>
    [ObservableProperty]
    public partial string Day { get; set; } = string.Empty;

    /// <summary>The day, for the date picker, or null before the core names one.</summary>
    [ObservableProperty]
    public partial DateTimeOffset? DayDate { get; set; }

    /// <summary>The times' label.</summary>
    [ObservableProperty]
    public partial string TimeLabel { get; set; } = string.Empty;

    /// <summary>"Pick a time.", or empty.</summary>
    [ObservableProperty]
    public partial string SelectionRejected { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="SelectionRejected"/>.</summary>
    [ObservableProperty]
    public partial bool HasSelectionRejected { get; set; }

    internal void Attach(PageContext context)
    {
        _context = context;
        Dialog.Attach(context);
        Slots.Attach(context);
    }

    internal void Show(RescheduleDialogView? view)
    {
        Dialog.Show(view?.Dialog);
        if (view is null)
        {
            return;
        }
        TimesIn = view.TimesIn;
        DayLabel = view.DayLabel;
        Day = view.Day;
        DayDate = ParseDay(view.Day);
        TimeLabel = view.TimeLabel;
        Slots.Show(view.SlotsStatus, view.Slots, view.NoSlots);
        SelectionRejected = view.SelectionRejected ?? string.Empty;
        HasSelectionRejected = view.SelectionRejected is not null;
    }

    /// <summary>The date picker now shows <paramref name="date"/>; the day showing sends nothing.</summary>
    internal void PickDay(DateTimeOffset date)
    {
        var day = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (day == Day)
        {
            return;
        }
        Day = day;
        _context?.Send(_pickDay(day));
    }

    /// <summary>
    /// The core's day as a date at midnight in this computer's zone, as a date
    /// picker holds one; null for one this build cannot read.
    /// </summary>
    internal static DateTimeOffset? ParseDay(string day) =>
        DateTime.TryParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? new DateTimeOffset(date)
            : null;
}

/// <summary>
/// Handing a booking to another host (the core's <see cref="ReassignDialogView"/>):
/// the scheduler's users who can take it, and the confirm, which waits for one.
/// </summary>
public sealed partial class ReassignBookingViewModel : ObservableObject
{
    internal ReassignBookingViewModel(Func<UiEvent> confirm, Func<UiEvent> close, Func<string, UiEvent> pickHost, Func<UiEvent> retry)
    {
        Dialog = new AdminDialogViewModel(confirm, close);
        Hosts = new ChoiceListViewModel(pickHost, retry);
    }

    /// <summary>The dialog's title, buttons and write.</summary>
    public AdminDialogViewModel Dialog { get; }

    /// <summary>The hosts who can take it.</summary>
    public ChoiceListViewModel Hosts { get; }

    /// <summary>The host list's label.</summary>
    [ObservableProperty]
    public partial string HostLabel { get; set; } = string.Empty;

    internal void Attach(PageContext context)
    {
        Dialog.Attach(context);
        Hosts.Attach(context);
    }

    internal void Show(ReassignDialogView? view)
    {
        Dialog.Show(view?.Dialog);
        if (view is null)
        {
            return;
        }
        HostLabel = view.HostLabel;
        Hosts.Show(view.HostsStatus, view.Hosts, view.NoHosts);
    }
}
