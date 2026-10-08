using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DistrictAI.Core;
using DistrictAI.Core.Ffi;

namespace DistrictAI.ViewModels.Calls;

/// <summary>
/// The dialler's state, copied from the core's <see cref="DialerView"/>: the
/// number box, Call, and what the core says about the dial. The box holds the
/// number exactly as typed, which is what the core keeps; every edit is sent as
/// it happens. Call works only when the core says a call can be placed.
/// </summary>
public sealed partial class DialerViewModel : ObservableObject
{
    private readonly TextEcho _echo = new();
    private CoreHost? _core;
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

    /// <summary>The core's note under the box (why Call is off, what calling uses), or empty.</summary>
    [ObservableProperty]
    public partial string Note { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="Note"/>.</summary>
    [ObservableProperty]
    public partial bool HasNote { get; set; }

    internal void Attach(CoreHost core) => _core = core;

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
        CanDial = view.CanDial;
        Dialing = view.Dialing;
        Failure = FailureText.Of(view.Failure);
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
        _core?.Send(new UiEvent.DialerEdit(value));
    }

    [RelayCommand(CanExecute = nameof(CanDial))]
    private void Dial() => _core?.Send(new UiEvent.Dial());
}
