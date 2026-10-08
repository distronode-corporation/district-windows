using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DistrictAI.Core.Ffi;

namespace DistrictAI.ViewModels.Calls;

/// <summary>
/// The dialler's state, copied from the core's <see cref="DialerView"/>: the
/// number as it reads, the box, Call, and what the core says about the dial.
/// The box holds the number exactly as typed, which is what the core keeps,
/// and is never rewritten to match how it reads; every edit is sent as it
/// happens. Call works only when the core says a call can be placed.
/// </summary>
public sealed partial class DialerViewModel : ObservableObject
{
    private readonly TextEcho _echo = new();
    private PageContext? _context;
    private bool _writing;

    /// <summary>The number in the box, as typed.</summary>
    [ObservableProperty]
    public partial string Number { get; set; } = string.Empty;

    /// <summary>Whether Call works.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DialCommand))]
    public partial bool CanDial { get; set; }

    /// <summary>Whether the call is being placed.</summary>
    [ObservableProperty]
    public partial bool Dialing { get; set; }

    /// <summary>Why the last dial failed, or empty.</summary>
    [ObservableProperty]
    public partial string Failure { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="Failure"/>.</summary>
    [ObservableProperty]
    public partial bool HasFailure { get; set; }

    /// <summary>
    /// The number as it reads, its digits grouped, over the box; a no-break
    /// space when there is none, so the line keeps its height.
    /// </summary>
    [ObservableProperty]
    public partial string Formatted { get; set; } = "\u00A0";

    /// <summary>The line that says what to type.</summary>
    [ObservableProperty]
    public partial string Hint { get; set; } = string.Empty;

    /// <summary>"Placing a call turns on your microphone."</summary>
    [ObservableProperty]
    public partial string MicrophoneNote { get; set; } = string.Empty;

    /// <summary>Why Call is off while a call or meeting is in the way, or empty.</summary>
    [ObservableProperty]
    public partial string Note { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="Note"/>.</summary>
    [ObservableProperty]
    public partial bool HasNote { get; set; }

    internal void Attach(PageContext context) => _context = context;

    internal void Show(DialerView view)
    {
        if (_echo.Write(view.Number, Number))
        {
            _writing = true;
            try
            {
                Number = view.Number;
            }
            finally
            {
                _writing = false;
            }
        }
        Formatted = view.Formatted.Length > 0 ? view.Formatted : "\u00A0";
        Hint = view.Hint;
        MicrophoneNote = view.MicrophoneNote;
        CanDial = view.CanDial;
        Dialing = view.Dialing;
        Failure = Display.Failure(view.Failure);
        HasFailure = view.Failure is not null;
        Note = view.Note ?? string.Empty;
        HasNote = !string.IsNullOrEmpty(view.Note);
    }

    partial void OnNumberChanged(string value)
    {
        if (_writing)
        {
            return;
        }
        _echo.Typed(value);
        _context?.Send(new UiEvent.DialerEdit(value));
    }

    [RelayCommand(CanExecute = nameof(CanDial))]
    private void Dial() => _context?.Send(new UiEvent.Dial());
}
