using CommunityToolkit.Mvvm.ComponentModel;
using DistrictAI.Core.Ffi;

namespace DistrictAI.ViewModels.AccountSecurity;

/// <summary>
/// Deleting the account, in the app: what is deleted and what is kept, a
/// fresh re-authentication (<see cref="Reauth"/>), then the account's email
/// address, typed, and Delete my account. The service checks every step;
/// after the deletion the core signs this computer out, and the sign-in page
/// says the account was deleted.
/// </summary>
/// <remarks>
/// The typed address is the box's own, read once by <see cref="Delete"/>.
/// <see cref="EmailCleared"/> empties the box once the address is sent, when
/// the last step goes and when the page is left.
/// </remarks>
public sealed partial class DeleteAccountViewModel : ObservableObject
{
    private PageContext? _context;
    private bool _confirming;

    /// <summary>The screen, with nothing shown yet.</summary>
    public DeleteAccountViewModel()
    {
        Reauth = new ReauthViewModel(
            provider => Event(new DeleteAccountAction.UseProvider(provider)),
            () => Event(new DeleteAccountAction.UsePassword()),
            (password, code) => Event(new DeleteAccountAction.SubmitPassword(password, code)),
            () => Event(new DeleteAccountAction.Back()));
    }

    /// <summary>Confirming it is you, before the last step.</summary>
    public ReauthViewModel Reauth { get; }

    /// <summary>"Delete your account".</summary>
    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;

    /// <summary>What is deleted, cancelled and kept, and that it cannot be undone: one paragraph per line.</summary>
    [ObservableProperty]
    public partial string Explain { get; set; } = string.Empty;

    /// <summary>Whether a re-authentication or the deletion is on its way.</summary>
    [ObservableProperty]
    public partial bool Busy { get; set; }

    /// <summary>Whether the controls can be used: not <see cref="Busy"/>.</summary>
    [ObservableProperty]
    public partial bool Enabled { get; set; } = true;

    /// <summary>"Your account was not deleted.", over a refusal when that is certain; or empty.</summary>
    [ObservableProperty]
    public partial string FailureTitle { get; set; } = string.Empty;

    /// <summary>What went wrong last, or empty.</summary>
    [ObservableProperty]
    public partial string Failure { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="Failure"/>.</summary>
    [ObservableProperty]
    public partial bool HasFailure { get; set; }

    /// <summary>Whether the last step shows: the typed address and Delete my account.</summary>
    [ObservableProperty]
    public partial bool IsConfirm { get; set; }

    /// <summary>"Last step".</summary>
    [ObservableProperty]
    public partial string ConfirmTitle { get; set; } = string.Empty;

    /// <summary>"Type your account's email address to confirm. ..."</summary>
    [ObservableProperty]
    public partial string TypeEmail { get; set; } = string.Empty;

    /// <summary>"Your email address".</summary>
    [ObservableProperty]
    public partial string EmailLabel { get; set; } = string.Empty;

    /// <summary>"Delete my account", or "Deleting…" while it is on its way.</summary>
    [ObservableProperty]
    public partial string SubmitLabel { get; set; } = string.Empty;

    /// <summary>Raised when the typed address is to be emptied.</summary>
    public event EventHandler? EmailCleared;

    internal void Attach(PageContext context)
    {
        _context = context;
        Reauth.Attach(context);
    }

    /// <summary>Shows <paramref name="view"/>.</summary>
    internal void Show(DeleteAccountView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        Title = view.Title;
        Explain = string.Join("\n\n", view.Explain);
        Busy = view.Busy;
        Enabled = !view.Busy;
        FailureTitle = view.FailureTitle ?? string.Empty;
        Failure = view.Failure ?? string.Empty;
        HasFailure = Failure.Length > 0;
        Reauth.Show(view.Reauth, view.Busy);
        var confirming = view.Confirm is not null;
        IsConfirm = confirming;
        if (view.Confirm is { } confirm)
        {
            ConfirmTitle = confirm.Title;
            TypeEmail = confirm.TypeEmail;
            EmailLabel = confirm.EmailLabel;
            SubmitLabel = confirm.SubmitLabel;
        }
        if (_confirming && !confirming)
        {
            EmailCleared?.Invoke(this, EventArgs.Empty);
        }
        _confirming = confirming;
    }

    /// <summary>The page is no longer showing: the typed password, code and address go.</summary>
    internal void Leave()
    {
        Reauth.Clear();
        EmailCleared?.Invoke(this, EventArgs.Empty);
        _confirming = false;
    }

    /// <summary>
    /// Deletes the account, confirmed by the typed <paramref name="email"/>,
    /// and empties the box as the address goes (<see cref="EmailCleared"/>),
    /// even when the send throws, as the password's box empties. Nothing goes
    /// while something is on its way or the box is empty.
    /// </summary>
    internal bool Delete(string email)
    {
        ArgumentNullException.ThrowIfNull(email);
        if (Busy || !IsConfirm || email.Trim().Length == 0)
        {
            return false;
        }
        try
        {
            _context?.Send(Event(new DeleteAccountAction.Delete(email)));
        }
        finally
        {
            EmailCleared?.Invoke(this, EventArgs.Empty);
        }
        return true;
    }

    private static UiEvent.AccountDeletion Event(DeleteAccountAction action) => new(action);
}
