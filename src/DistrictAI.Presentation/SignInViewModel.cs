using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DistrictAI.Core;
using DistrictAI.Core.Ffi;

namespace DistrictAI.ViewModels;

/// <summary>
/// The sign-in page's state, copied from the core's <see cref="SessionScreen"/>,
/// and its buttons, each forwarded to the core as the event it names. The page
/// decides nothing.
/// </summary>
public sealed partial class SignInViewModel : ObservableObject
{
    private ICoreSink? _core;
    private bool _screenOffersSignIn;
    private bool _signInHeld;

    /// <summary>The heading.</summary>
    [ObservableProperty]
    public partial string Title { get; set; } = "District AI";

    /// <summary>The text under it.</summary>
    [ObservableProperty]
    public partial string Body { get; set; } = string.Empty;

    /// <summary>Whether something is under way.</summary>
    [ObservableProperty]
    public partial bool Busy { get; set; }

    /// <summary>Why the last sign-in failed, or empty.</summary>
    [ObservableProperty]
    public partial string Error { get; set; } = string.Empty;

    /// <summary>Whether there is an <see cref="Error"/>.</summary>
    [ObservableProperty]
    public partial bool HasError { get; set; }

    /// <summary>
    /// Why browser sign-in is held (another copy of the app is installed),
    /// or empty. Its heading is <see cref="HeldTitle"/>.
    /// </summary>
    [ObservableProperty]
    public partial string HeldMessage { get; set; } = string.Empty;

    /// <summary>The heading over <see cref="HeldMessage"/>.</summary>
    [ObservableProperty]
    public partial string HeldTitle { get; set; } = string.Empty;

    /// <summary>Whether browser sign-in is held.</summary>
    [ObservableProperty]
    public partial bool SignInHeld { get; set; }

    /// <summary>Which copy of the app this is, while another is installed, or empty.</summary>
    [ObservableProperty]
    public partial string CopyLine { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="CopyLine"/>.</summary>
    [ObservableProperty]
    public partial bool HasCopyLine { get; set; }

    /// <summary>Whether "Sign in with your browser" is offered: the core offers it and it is not held.</summary>
    [ObservableProperty]
    public partial bool CanSignIn { get; set; }

    /// <summary>Whether "Try again" is offered.</summary>
    [ObservableProperty]
    public partial bool CanRetry { get; set; }

    /// <summary>Whether "Sign out again" is offered.</summary>
    [ObservableProperty]
    public partial bool CanRetrySignOut { get; set; }

    /// <summary>Whether "Cancel" is offered.</summary>
    [ObservableProperty]
    public partial bool CanCancel { get; set; }

    internal void Attach(ICoreSink core) => _core = core;

    internal void Show(SessionScreen screen)
    {
        Title = screen.Title;
        Body = screen.Body;
        Busy = screen.Busy;
        Error = screen.Error ?? string.Empty;
        HasError = screen.Error is not null;
        _screenOffersSignIn = screen.SignIn;
        CanSignIn = _screenOffersSignIn && !_signInHeld;
        CanRetry = screen.Retry;
        CanRetrySignOut = screen.RetrySignOut;
        CanCancel = screen.Cancel;
    }

    /// <summary>
    /// Shows what the core says about the copies installed: with another copy
    /// installed, browser sign-in is held with the explanation, and the page
    /// says which copy this is.
    /// </summary>
    internal void ShowCopies(CopiesView copies)
    {
        ArgumentNullException.ThrowIfNull(copies);
        _signInHeld = copies.SignInHeld;
        SignInHeld = copies.SignInHeld;
        HeldTitle = copies.Title ?? string.Empty;
        HeldMessage = copies.Message ?? string.Empty;
        CopyLine = copies.ThisCopyLine ?? string.Empty;
        HasCopyLine = copies.ThisCopyLine is not null;
        CanSignIn = _screenOffersSignIn && !_signInHeld;
    }

    [RelayCommand]
    private void SignIn()
    {
        // Held: a press that slipped through (a stale button) sends nothing.
        if (!_signInHeld)
        {
            _core?.Send(new UiEvent.SignIn());
        }
    }

    [RelayCommand]
    private void Retry() => _core?.Send(new UiEvent.RetryRestore());

    [RelayCommand]
    private void RetrySignOut() => _core?.Send(new UiEvent.RetrySignOut());

    [RelayCommand]
    private void Cancel() => _core?.Send(new UiEvent.CancelSignIn());
}
