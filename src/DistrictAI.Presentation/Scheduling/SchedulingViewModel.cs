using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DistrictAI.Core.Ffi;

namespace DistrictAI.ViewModels.Scheduling;

/// <summary>
/// The booking pages: where they stand, turning them on where the service
/// says this member may, and managing them on the web, signed in, through the
/// core's hand-off. Copied from the core's <see cref="SchedulingView"/>; the
/// one-time link never reaches this page.
/// </summary>
public sealed partial class SchedulingViewModel : ObservableObject
{
    private PageContext? _context;

    /// <summary>Loading, a read that failed, and a refresh.</summary>
    public LoadStateViewModel Load { get; } = new();

    /// <summary>The card's icon (a Segoe Fluent Icons glyph) for where they stand.</summary>
    [ObservableProperty]
    public partial string StateGlyph { get; set; } = string.Empty;

    /// <summary>The card's sentence, in the core's words.</summary>
    [ObservableProperty]
    public partial string Message { get; set; } = string.Empty;

    /// <summary>The public booking page's address, or empty.</summary>
    [ObservableProperty]
    public partial string BookingUrl { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="BookingUrl"/>.</summary>
    [ObservableProperty]
    public partial bool HasBookingUrl { get; set; }

    /// <summary>When they were last live, in local time, or empty.</summary>
    [ObservableProperty]
    public partial string LastLive { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="LastLive"/>.</summary>
    [ObservableProperty]
    public partial bool HasLastLive { get; set; }

    /// <summary>What went wrong with the last setup, or empty.</summary>
    [ObservableProperty]
    public partial string Problem { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="Problem"/>.</summary>
    [ObservableProperty]
    public partial bool HasProblem { get; set; }

    /// <summary>What the last press came to, or empty.</summary>
    [ObservableProperty]
    public partial string Notice { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="Notice"/>.</summary>
    [ObservableProperty]
    public partial bool HasNotice { get; set; }

    /// <summary>Whether "Turn on booking pages" shows.</summary>
    [ObservableProperty]
    public partial bool OffersEnable { get; set; }

    /// <summary>Whether "Turn on booking pages" works (not while it is on its way).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EnableCommand))]
    public partial bool CanEnable { get; set; }

    /// <summary>Whether "Check again" shows.</summary>
    [ObservableProperty]
    public partial bool OffersCheck { get; set; }

    /// <summary>Whether "Manage on the web" shows.</summary>
    [ObservableProperty]
    public partial bool OffersWeb { get; set; }

    /// <summary>Whether "Manage on the web" works (not while the link is being asked for).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ManageOnWebCommand))]
    public partial bool CanManageOnWeb { get; set; }

    /// <summary>Whether turning them on or the hand-off is under way.</summary>
    [ObservableProperty]
    public partial bool Busy { get; set; }

    internal void Attach(PageContext context)
    {
        _context = context;
        Load.Attach(context);
    }

    internal void Show(SchedulingView view)
    {
        Load.Show(view.Status, true, null, view.Refreshing, null);
        StateGlyph = GlyphFor(view.State);
        Message = view.Message;
        BookingUrl = view.BookingUrl ?? string.Empty;
        HasBookingUrl = BookingUrl.Length > 0;
        LastLive = Display.When(view.LastReadyAt);
        HasLastLive = LastLive.Length > 0;
        Problem = view.Problem ?? string.Empty;
        HasProblem = Problem.Length > 0;
        Notice = Display.Failure(view.Notice);
        HasNotice = view.Notice is not null;
        OffersEnable = view.OffersEnable;
        CanEnable = view.OffersEnable && !view.Enabling;
        OffersCheck = view.OffersCheck;
        OffersWeb = view.OffersWeb;
        CanManageOnWeb = view.OffersWeb && view.WebEnabled;
        Busy = view.Enabling || view.Opening;
    }

    /// <summary>The icon for <paramref name="state"/>, as District AI for Linux picks it.</summary>
    internal static string GlyphFor(SchedulingState? state) => state switch
    {
        SchedulingState.NotOffered => "\uE733",
        SchedulingState.NotSetUp => "\uE787",
        SchedulingState.Provisioning => "\uE895",
        SchedulingState.Live => "\uE73E",
        SchedulingState.SetupFailed => "\uE7BA",
        SchedulingState.SwitchedOff or SchedulingState.Unknown => "\uE946",
        // Not read yet, or a state a later core adds: no icon.
        _ => string.Empty,
    };

    [RelayCommand(CanExecute = nameof(CanEnable))]
    private void Enable() => Send(SchedulingAction.Enable);

    [RelayCommand(CanExecute = nameof(CanManageOnWeb))]
    private void ManageOnWeb() => Send(SchedulingAction.ManageOnWeb);

    [RelayCommand]
    private void CheckAgain() => Send(SchedulingAction.CheckAgain);

    [RelayCommand]
    private void DismissNotice() => Send(SchedulingAction.DismissNotice);

    /// <summary>Forwards one of the screen's actions.</summary>
    internal void Send(SchedulingAction action) => _context?.Send(new UiEvent.Scheduling(action));
}
