using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DistrictAI.Core.Ffi;

namespace DistrictAI.ViewModels.Scheduling;

/// <summary>
/// One booking (the core's <see cref="SchedulingBookingView"/>): its row from
/// the list, what the booker answered, and the cancel, the one write this page
/// offers, as on District AI for iPhone.
/// </summary>
public sealed partial class SchedulingBookingViewModel : ObservableObject
{
    private PageContext? _context;

    /// <summary>A booking with nothing shown yet.</summary>
    public SchedulingBookingViewModel()
    {
        Cancel = new CancelBookingViewModel(
            () => Event(new BookingAction.Confirm()),
            () => Event(new BookingAction.CloseDialog()),
            reason => Event(new BookingAction.EditCancelReason(reason)));
    }

    /// <summary>Loading and failure, for the answers.</summary>
    public LoadStateViewModel Load { get; } = new();

    /// <summary>The cancel dialog.</summary>
    public CancelBookingViewModel Cancel { get; }

    /// <summary>The answers to the event type's questions.</summary>
    public ObservableCollection<FactItem> Answers { get; } = [];

    /// <summary>Whether the list's row for the booking is known.</summary>
    [ObservableProperty]
    public partial bool HasSummary { get; set; }

    /// <summary>When it starts.</summary>
    [ObservableProperty]
    public partial string When { get; set; } = string.Empty;

    /// <summary>Who, what and with whom.</summary>
    [ObservableProperty]
    public partial string Subtitle { get; set; } = string.Empty;

    /// <summary>The first attendee's address, or empty.</summary>
    [ObservableProperty]
    public partial string Email { get; set; } = string.Empty;

    /// <summary>The status badge's words.</summary>
    [ObservableProperty]
    public partial string StatusLabel { get; set; } = string.Empty;

    /// <summary>Whether the status is a good one.</summary>
    [ObservableProperty]
    public partial bool IsSuccess { get; set; }

    /// <summary>Whether the status is a bad one.</summary>
    [ObservableProperty]
    public partial bool IsDanger { get; set; }

    /// <summary>Whether the status is neither.</summary>
    [ObservableProperty]
    public partial bool IsNeutral { get; set; }

    /// <summary>The answers' heading.</summary>
    [ObservableProperty]
    public partial string AnswersHeading { get; set; } = string.Empty;

    /// <summary>Said when the event type asks nothing, once read.</summary>
    [ObservableProperty]
    public partial string NoQuestions { get; set; } = string.Empty;

    /// <summary>Whether the answers are read and there are none.</summary>
    [ObservableProperty]
    public partial bool ShowNoQuestions { get; set; }

    /// <summary>The heading over the cancel.</summary>
    [ObservableProperty]
    public partial string ManageHeading { get; set; } = string.Empty;

    /// <summary>Whether "Cancel booking" is offered.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenCancelCommand))]
    public partial bool OffersCancel { get; set; }

    /// <summary>"Cancel booking".</summary>
    [ObservableProperty]
    public partial string CancelLabel { get; set; } = string.Empty;

    /// <summary>"Booking cancelled", or empty.</summary>
    [ObservableProperty]
    public partial string Notice { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="Notice"/>.</summary>
    [ObservableProperty]
    public partial bool HasNotice { get; set; }

    internal void Attach(PageContext context)
    {
        _context = context;
        Load.Attach(context);
        Cancel.Attach(context);
    }

    internal void Show(SchedulingBookingView view)
    {
        Load.Show(view.Status, true, null, refreshing: false, refreshFailure: null);
        var summary = view.Summary;
        HasSummary = summary is not null;
        When = summary?.When ?? string.Empty;
        Subtitle = summary?.Subtitle ?? string.Empty;
        Email = summary?.Email ?? string.Empty;
        StatusLabel = summary?.StatusLabel ?? string.Empty;
        IsSuccess = summary?.Tone == RunTone.Success;
        IsDanger = summary?.Tone == RunTone.Danger;
        IsNeutral = summary is not null && !IsSuccess && !IsDanger;
        AnswersHeading = view.AnswersHeading;
        Display.Sync(Answers, [.. view.Answers.Select(FactItem.From)]);
        NoQuestions = view.NoQuestions;
        ShowNoQuestions = view.Status is LoadStatus.Ready && view.Answers.Length == 0;
        ManageHeading = view.ManageHeading;
        OffersCancel = view.OffersCancel;
        CancelLabel = view.CancelLabel;
        Notice = view.Notice ?? string.Empty;
        HasNotice = view.Notice is not null;
        Cancel.Show(view.Cancel);
    }

    [RelayCommand(CanExecute = nameof(OffersCancel))]
    private void OpenCancel() => Send(new BookingAction.OpenCancel());

    /// <summary>Puts away what the cancel came to.</summary>
    [RelayCommand]
    private void DismissNotice() => Send(new BookingAction.CloseDialog());

    /// <summary>Forwards one of the page's actions.</summary>
    internal void Send(BookingAction action) => _context?.Send(Event(action));

    /// <summary>The event for <paramref name="action"/>.</summary>
    internal static UiEvent Event(BookingAction action) =>
        new UiEvent.SchedulingAdmin(new SchedulingAdminAction.Booking(action));
}
