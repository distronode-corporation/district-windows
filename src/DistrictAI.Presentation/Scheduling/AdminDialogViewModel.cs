using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DistrictAI.Core.Ffi;

namespace DistrictAI.ViewModels.Scheduling;

/// <summary>
/// What every confirm-once dialog of the booking scheduler's administration
/// shows besides its own fields (the core's <see cref="DialogView"/>): open
/// exactly while the core holds it, its confirm pressed once (off at once,
/// and while the write is on its way), a refusal said in the dialog with the
/// edits kept, and a write that landed closing it. Each section's dialog
/// holds one and adds its own fields.
/// </summary>
public sealed partial class AdminDialogViewModel : ObservableObject
{
    private readonly Func<UiEvent> _confirm;
    private readonly Func<UiEvent> _close;
    private PageContext? _context;

    /// <summary>A dialog whose confirm sends <paramref name="confirm"/> and whose way out sends <paramref name="close"/>.</summary>
    internal AdminDialogViewModel(Func<UiEvent> confirm, Func<UiEvent> close)
    {
        _confirm = confirm;
        _close = close;
    }

    /// <summary>Whether the core holds the dialog open.</summary>
    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    /// <summary>The question.</summary>
    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;

    /// <summary>What saying yes does.</summary>
    [ObservableProperty]
    public partial string Body { get; set; } = string.Empty;

    /// <summary>The confirm button's words.</summary>
    [ObservableProperty]
    public partial string ConfirmText { get; set; } = string.Empty;

    /// <summary>The way out's words.</summary>
    [ObservableProperty]
    public partial string CloseText { get; set; } = string.Empty;

    /// <summary>Whether the confirm can be pressed now.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    public partial bool CanConfirm { get; set; }

    /// <summary>Whether the write is on its way.</summary>
    [ObservableProperty]
    public partial bool Working { get; set; }

    /// <summary>Whether the confirm undoes something for a customer: never the default button.</summary>
    [ObservableProperty]
    public partial bool Destructive { get; set; }

    /// <summary>Why the write was refused, or empty.</summary>
    [ObservableProperty]
    public partial string Failure { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="Failure"/>.</summary>
    [ObservableProperty]
    public partial bool HasFailure { get; set; }

    internal void Attach(PageContext context) => _context = context;

    /// <summary>Shows <paramref name="dialog"/>, or closes the dialog when the core holds none.</summary>
    internal void Show(DialogView? dialog)
    {
        IsOpen = dialog is not null;
        Title = dialog?.Title ?? string.Empty;
        Body = dialog?.Body ?? string.Empty;
        ConfirmText = dialog?.Confirm ?? string.Empty;
        CloseText = dialog?.Close ?? string.Empty;
        CanConfirm = dialog?.CanConfirm ?? false;
        Working = dialog?.Working ?? false;
        Destructive = dialog?.Destructive ?? false;
        Failure = Display.Failure(dialog?.Failure);
        HasFailure = dialog?.Failure is not null;
    }

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm()
    {
        // Until the core's next snapshot: one press, one write.
        CanConfirm = false;
        _context?.Send(_confirm());
    }

    [RelayCommand]
    private void Close() => _context?.Send(_close());
}
