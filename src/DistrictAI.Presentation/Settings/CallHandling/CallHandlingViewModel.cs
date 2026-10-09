using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels.Calls;

namespace DistrictAI.ViewModels.Settings.CallHandling;

/// <summary>One way of answering, as offered.</summary>
/// <param name="Mode">Which.</param>
/// <param name="Label">Its name.</param>
/// <param name="Body">What it does.</param>
/// <param name="Selected">Whether it is the one on screen.</param>
/// <param name="CanEdit">Whether it can be chosen now.</param>
public sealed record CallHandlingModeItem(CallHandlingChoice Mode, string Label, string Body, bool Selected, bool CanEdit)
{
    /// <inheritdoc/>
    public override string ToString() => Label;
}

/// <summary>
/// District Studio's Call handling: who answers and how long the devices ring,
/// for the whole workspace and saved with a button; and whether the member is
/// rung, their own and sent at once. Each part is read on its own and fails on
/// its own; a viewer reads both and is offered no control. Every word is the
/// core's.
/// </summary>
public sealed partial class CallHandlingViewModel : ObservableObject
{
    private readonly TextEcho _ringEcho = new();
    private PageContext? _context;
    private bool _writing;

    /// <summary>The ways of answering offered.</summary>
    public ObservableCollection<CallHandlingModeItem> Modes { get; } = [];

    /// <summary>The core's view of the screen.</summary>
    [ObservableProperty]
    public partial CallHandlingView? View { get; set; }

    /// <summary>The heading.</summary>
    [ObservableProperty]
    public partial string Title { get; set; } = "Call handling";

    /// <summary>The heading over who answers.</summary>
    [ObservableProperty]
    public partial string HandlingHeading { get; set; } = string.Empty;

    /// <summary>What that part is for.</summary>
    [ObservableProperty]
    public partial string HandlingNote { get; set; } = string.Empty;

    /// <summary>Whether who answers is being read.</summary>
    [ObservableProperty]
    public partial bool HandlingLoading { get; set; }

    /// <summary>Whether who answers is read, and its controls show.</summary>
    [ObservableProperty]
    public partial bool HandlingReady { get; set; }

    /// <summary>Whether reading who answers failed.</summary>
    [ObservableProperty]
    public partial bool HandlingFailed { get; set; }

    /// <summary>What failed, and why.</summary>
    [ObservableProperty]
    public partial string HandlingFailure { get; set; } = string.Empty;

    /// <summary>Whether "Try again" is offered for it.</summary>
    [ObservableProperty]
    public partial bool CanRetryHandling { get; set; }

    /// <summary>A stored mode this app does not know, as stored, or empty.</summary>
    [ObservableProperty]
    public partial string UnknownMode { get; set; } = string.Empty;

    /// <summary>Whether there is an <see cref="UnknownMode"/>.</summary>
    [ObservableProperty]
    public partial bool HasUnknownMode { get; set; }

    /// <summary>The ring's label.</summary>
    [ObservableProperty]
    public partial string RingLabel { get; set; } = string.Empty;

    /// <summary>The range the service accepts, in words.</summary>
    [ObservableProperty]
    public partial string RingHint { get; set; } = string.Empty;

    /// <summary>How long the devices ring, in seconds, as the slider has it.</summary>
    [ObservableProperty]
    public partial double Ring { get; set; }

    /// <summary>The ring, in words.</summary>
    [ObservableProperty]
    public partial string RingWords { get; set; } = string.Empty;

    /// <summary>The shortest ring.</summary>
    [ObservableProperty]
    public partial double RingMin { get; set; }

    /// <summary>The longest ring.</summary>
    [ObservableProperty]
    public partial double RingMax { get; set; } = 30;

    /// <summary>Whether the member's role may change call handling (the slider shows, not the words).</summary>
    [ObservableProperty]
    public partial bool CanChange { get; set; }

    /// <summary>Whether the ring shows as words: a viewer's.</summary>
    [ObservableProperty]
    public partial bool ShowRingWords { get; set; }

    /// <summary>Whether the mode and the ring can be changed now.</summary>
    [ObservableProperty]
    public partial bool CanEdit { get; set; }

    /// <summary>Whether "Save" works.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial bool CanSave { get; set; }

    /// <summary>Whether "Save" shows: a member who may change it, once read.</summary>
    [ObservableProperty]
    public partial bool ShowSave { get; set; }

    /// <summary>Whether a save is on its way.</summary>
    [ObservableProperty]
    public partial bool Saving { get; set; }

    /// <summary>How the last save ended, or empty.</summary>
    [ObservableProperty]
    public partial string Notice { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="Notice"/>.</summary>
    [ObservableProperty]
    public partial bool HasNotice { get; set; }

    /// <summary>Whether the notice says it saved (rather than failed).</summary>
    [ObservableProperty]
    public partial bool NoticeSaved { get; set; }

    /// <summary>What a viewer is told, or empty.</summary>
    [ObservableProperty]
    public partial string ViewerNote { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="ViewerNote"/>.</summary>
    [ObservableProperty]
    public partial bool HasViewerNote { get; set; }

    /// <summary>The heading over the member's availability.</summary>
    [ObservableProperty]
    public partial string AvailabilityHeading { get; set; } = string.Empty;

    /// <summary>What it is for.</summary>
    [ObservableProperty]
    public partial string AvailabilityNote { get; set; } = string.Empty;

    /// <summary>Whether the availability is being read.</summary>
    [ObservableProperty]
    public partial bool AvailabilityLoading { get; set; }

    /// <summary>Whether reading it failed.</summary>
    [ObservableProperty]
    public partial bool AvailabilityFailed { get; set; }

    /// <summary>What failed, and why.</summary>
    [ObservableProperty]
    public partial string AvailabilityFailure { get; set; } = string.Empty;

    /// <summary>Whether "Try again" is offered for it.</summary>
    [ObservableProperty]
    public partial bool CanRetryAvailability { get; set; }

    /// <summary>The switch's label.</summary>
    [ObservableProperty]
    public partial string AvailabilityLabel { get; set; } = string.Empty;

    /// <summary>Whether the member's devices ring, as the switch has it.</summary>
    [ObservableProperty]
    public partial bool Available { get; set; }

    /// <summary>Whether the switch shows.</summary>
    [ObservableProperty]
    public partial bool ShowSwitch { get; set; }

    /// <summary>Whether the switch works.</summary>
    [ObservableProperty]
    public partial bool CanToggle { get; set; }

    /// <summary>Why the member cannot be rung, or empty.</summary>
    [ObservableProperty]
    public partial string Blocked { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="Blocked"/>.</summary>
    [ObservableProperty]
    public partial bool HasBlocked { get; set; }

    /// <summary>Whether a change of it is on its way.</summary>
    [ObservableProperty]
    public partial bool Changing { get; set; }

    /// <summary>How the last change ended, or empty.</summary>
    [ObservableProperty]
    public partial string AvailabilityNotice { get; set; } = string.Empty;

    /// <summary>Whether there is an <see cref="AvailabilityNotice"/>.</summary>
    [ObservableProperty]
    public partial bool HasAvailabilityNotice { get; set; }

    /// <summary>Whether that notice says it saved (rather than failed).</summary>
    [ObservableProperty]
    public partial bool AvailabilityNoticeSaved { get; set; }

    internal void Attach(PageContext context) => _context = context;

    internal void Show(CallHandlingView view)
    {
        View = view;
        Title = view.Title;
        HandlingHeading = view.HandlingHeading;
        HandlingNote = view.HandlingNote;
        (HandlingLoading, HandlingReady, HandlingFailed, HandlingFailure, CanRetryHandling) = Read(view.Status);
        CanChange = view.CanChange;
        CanEdit = view.CanEdit;
        Display.Sync(Modes, [.. view.Modes.Select(mode => new CallHandlingModeItem(mode.Mode, mode.Label, mode.Body, mode.Selected, view.CanEdit))]);
        UnknownMode = view.UnknownMode ?? string.Empty;
        HasUnknownMode = UnknownMode.Length > 0;
        RingLabel = view.RingLabel;
        RingHint = view.RingHint;
        RingMin = view.RingMin;
        RingMax = view.RingMax;
        RingWords = view.RingWords;
        ShowRingWords = !view.CanChange;
        CanSave = view.CanSave;
        ShowSave = view.CanChange && HandlingReady;
        Saving = view.Saving;
        (Notice, HasNotice, NoticeSaved) = Words(view.Notice);
        ViewerNote = view.ViewerNote ?? string.Empty;
        HasViewerNote = ViewerNote.Length > 0;

        var availability = view.Availability;
        AvailabilityHeading = availability.Heading;
        AvailabilityNote = availability.Note;
        (AvailabilityLoading, _, AvailabilityFailed, AvailabilityFailure, CanRetryAvailability) = Read(availability.Status);
        AvailabilityLabel = availability.Label;
        ShowSwitch = availability.ShowSwitch;
        CanToggle = availability.CanToggle;
        Blocked = availability.Blocked ?? string.Empty;
        HasBlocked = Blocked.Length > 0;
        Changing = availability.Changing;
        (AvailabilityNotice, HasAvailabilityNotice, AvailabilityNoticeSaved) = Words(availability.Notice);

        _writing = true;
        try
        {
            var seconds = view.RingSeconds.ToString(CultureInfo.InvariantCulture);
            if (_ringEcho.Write(seconds, Seconds(Ring)))
            {
                Ring = view.RingSeconds;
            }
            Available = availability.Available;
        }
        finally
        {
            _writing = false;
        }
    }

    /// <summary>A read's state: loading, ready, failed, why, and whether "Try again" helps.</summary>
    private static (bool Loading, bool Ready, bool Failed, string Failure, bool CanRetry) Read(LoadStatus status) => status switch
    {
        LoadStatus.Failed failed => (false, false, true, failed.Title + ". " + Display.Failure(failed.Failure), failed.Failure.Retryable),
        LoadStatus.Ready => (false, true, false, string.Empty, false),
        _ => (true, false, false, string.Empty, false),
    };

    /// <summary>A save notice's words, whether there is one, and whether it saved.</summary>
    internal static (string Message, bool Has, bool Saved) Words(SaveNoticeView? notice) =>
        (notice?.Message ?? string.Empty, notice is not null, notice?.Saved ?? false);

    private static string Seconds(double ring) => ((long)Math.Round(ring)).ToString(CultureInfo.InvariantCulture);

    /// <summary>Chooses who answers.</summary>
    internal void SelectMode(CallHandlingModeItem mode)
    {
        if (!CanEdit || mode.Selected)
        {
            return;
        }
        Send(new CallHandlingAction.SelectMode(mode.Mode));
    }

    partial void OnRingChanged(double value)
    {
        if (_writing || !CanEdit)
        {
            return;
        }
        var seconds = Seconds(value);
        _ringEcho.Typed(seconds);
        Send(new CallHandlingAction.SetRingSeconds((long)Math.Round(value)));
    }

    partial void OnAvailableChanged(bool value)
    {
        if (_writing || !CanToggle)
        {
            return;
        }
        // Turned off as it is pressed, so one press is one change.
        CanToggle = false;
        Send(new CallHandlingAction.SetAvailable(value));
    }

    /// <summary>Saves what changed: turned off as it is pressed, so one press is one save.</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        if (!CanSave)
        {
            return;
        }
        CanSave = false;
        Send(new CallHandlingAction.Save());
    }

    /// <summary>Puts the notices away.</summary>
    [RelayCommand]
    private void DismissNotices() => Send(new CallHandlingAction.DismissNotices());

    /// <summary>Reads the section again ("Try again").</summary>
    [RelayCommand]
    private void Retry() => _context?.Send(new UiEvent.Refresh());

    /// <summary>Forwards one of the screen's actions.</summary>
    internal void Send(CallHandlingAction action) => _context?.Send(new UiEvent.CallHandling(action));
}
