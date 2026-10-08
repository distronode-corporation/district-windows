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
    private CoreHost? _core;
    private SessionScreen? _shown;
    private bool _signInBlocked;

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

    /// <summary>Whether "Sign in with your browser" is offered.</summary>
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

    internal void Attach(CoreHost core) => _core = core;

    /// <summary>
    /// Offers no browser sign-in from now on: another copy of the app is
    /// installed, and the answer could land there.
    /// </summary>
    internal void BlockSignIn()
    {
        _signInBlocked = true;
        if (_shown is not null)
        {
            Show(_shown);
        }
    }

    internal void Show(SessionScreen screen)
    {
        _shown = screen;
        Title = screen.Title;
        Body = screen.Body;
        Busy = screen.Busy;
        Error = screen.Error ?? string.Empty;
        HasError = screen.Error is not null;
        CanSignIn = screen.SignIn && !_signInBlocked;
        CanRetry = screen.Retry;
        CanRetrySignOut = screen.RetrySignOut;
        CanCancel = screen.Cancel;
    }

    [RelayCommand]
    private void SignIn() => _core?.Send(new UiEvent.SignIn());

    [RelayCommand]
    private void Retry() => _core?.Send(new UiEvent.RetryRestore());

    [RelayCommand]
    private void RetrySignOut() => _core?.Send(new UiEvent.RetrySignOut());

    [RelayCommand]
    private void Cancel() => _core?.Send(new UiEvent.CancelSignIn());
}
