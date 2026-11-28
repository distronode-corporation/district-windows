// SPDX-FileCopyrightText: Distronode Corporation
//
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DistrictAI.Core.Ffi;

namespace DistrictAI.ViewModels.AccountSecurity;

/// <summary>
/// Two-step verification for the account: whether it is on, since when and
/// how many recovery codes are left, and turning it on or off, each after a
/// fresh re-authentication (<see cref="Reauth"/>). Turning it on shows the new
/// secret as a QR code and a key, with Copy key and Open in an authenticator
/// app, asks for the authenticator's first code, then shows the recovery
/// codes once, with Copy all. Every sentence is the core's.
/// </summary>
/// <remarks>
/// The key, its link, its picture and the codes are held here only while
/// their step shows and the page is showing: <see cref="Leave"/> drops them
/// (the core keeps unsaved codes for the next visit), and
/// <see cref="SecretsCleared"/> tells the page to empty the code box and the
/// picture. The typed code goes to the core as UTF-8 bytes, cleared once
/// sent, as the password does (<see cref="ReauthViewModel"/>).
/// </remarks>
public sealed partial class TwoStepViewModel : ObservableObject
{
    private readonly SecretCopies _copies;
    private PageContext? _context;
    private TwoStepChangeView? _offered;
    private string _otpauthUri = string.Empty;
    private TwoStepStepView? _shownStep;
    private string _copied = string.Empty;

    /// <summary>A screen whose copies go through <paramref name="copies"/>.</summary>
    public TwoStepViewModel(SecretCopies copies)
    {
        ArgumentNullException.ThrowIfNull(copies);
        _copies = copies;
        Reauth = new ReauthViewModel(
            provider => Event(new TwoStepAction.UseProvider(provider)),
            () => Event(new TwoStepAction.UsePassword()),
            (password, code) => Event(new TwoStepAction.SubmitPassword(password, code)),
            () => Event(new TwoStepAction.Back()));
    }

    /// <summary>Confirming it is you, before a change.</summary>
    public ReauthViewModel Reauth { get; }

    /// <summary>"Two-step verification".</summary>
    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;

    /// <summary>Whether the status is being read for the first time.</summary>
    [ObservableProperty]
    public partial bool IsLoading { get; set; } = true;

    /// <summary>Whether the first read failed: the failure, and Try again.</summary>
    [ObservableProperty]
    public partial bool IsLoadFailed { get; set; }

    /// <summary>Whether the status shows, with the change it offers.</summary>
    [ObservableProperty]
    public partial bool IsOverview { get; set; }

    /// <summary>Whether the new secret shows, and the field for its first code.</summary>
    [ObservableProperty]
    public partial bool IsScan { get; set; }

    /// <summary>Whether the recovery codes show.</summary>
    [ObservableProperty]
    public partial bool IsCodes { get; set; }

    /// <summary>Whether a read or a change is on its way.</summary>
    [ObservableProperty]
    public partial bool Busy { get; set; }

    /// <summary>Whether the controls can be used: not <see cref="Busy"/>.</summary>
    [ObservableProperty]
    public partial bool Enabled { get; set; } = true;

    /// <summary>What went wrong last, or empty.</summary>
    [ObservableProperty]
    public partial string Failure { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="Failure"/>.</summary>
    [ObservableProperty]
    public partial bool HasFailure { get; set; }

    /// <summary>What the last finished change did, or empty.</summary>
    [ObservableProperty]
    public partial string Notice { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="Notice"/>.</summary>
    [ObservableProperty]
    public partial bool HasNotice { get; set; }

    /// <summary>"On" or "Off", or empty while unknown.</summary>
    [ObservableProperty]
    public partial string StatusLabel { get; set; } = string.Empty;

    /// <summary>What the status says under its label, one sentence per line.</summary>
    [ObservableProperty]
    public partial string StatusLines { get; set; } = string.Empty;

    /// <summary>"Turn on" or "Turn off", or empty when nothing is offered.</summary>
    [ObservableProperty]
    public partial string OfferedLabel { get; set; } = string.Empty;

    /// <summary>Whether a change is offered.</summary>
    [ObservableProperty]
    public partial bool HasOffered { get; set; }

    /// <summary>Whether the change offered turns it off (a destructive button).</summary>
    [ObservableProperty]
    public partial bool OffersTurnOff { get; set; }

    /// <summary>Whether it turns it on (the accent button).</summary>
    [ObservableProperty]
    public partial bool OffersTurnOn { get; set; }

    /// <summary>"Try again".</summary>
    [ObservableProperty]
    public partial string RetryLabel { get; set; } = string.Empty;

    /// <summary>The scan step's heading.</summary>
    [ObservableProperty]
    public partial string ScanTitle { get; set; } = string.Empty;

    /// <summary>What to do with the QR code or the key.</summary>
    [ObservableProperty]
    public partial string ScanIntro { get; set; } = string.Empty;

    /// <summary>The QR code as PNG bytes, or null (no picture, or the page is left).</summary>
    [ObservableProperty]
    public partial byte[]? QrPng { get; set; }

    /// <summary>Whether there is a <see cref="QrPng"/> to draw.</summary>
    [ObservableProperty]
    public partial bool HasQr { get; set; }

    /// <summary>Whether the picture could not be read: say <see cref="QrUnreadable"/>.</summary>
    [ObservableProperty]
    public partial bool ShowsQrUnreadable { get; set; }

    /// <summary>The picture's name, for screen readers.</summary>
    [ObservableProperty]
    public partial string QrLabel { get; set; } = string.Empty;

    /// <summary>"The QR code could not be shown. Enter the key by hand instead."</summary>
    [ObservableProperty]
    public partial string QrUnreadable { get; set; } = string.Empty;

    /// <summary>"Key".</summary>
    [ObservableProperty]
    public partial string KeyLabel { get; set; } = string.Empty;

    /// <summary>The key, or empty once the page is left.</summary>
    [ObservableProperty]
    public partial string Key { get; set; } = string.Empty;

    /// <summary>"Copy key", then "Copied" once it has copied.</summary>
    [ObservableProperty]
    public partial string CopyKeyLabel { get; set; } = string.Empty;

    /// <summary>"Open in an authenticator app".</summary>
    [ObservableProperty]
    public partial string OpenLabel { get; set; } = string.Empty;

    /// <summary>What to do when no app opened the link.</summary>
    [ObservableProperty]
    public partial string OpenFailed { get; set; } = string.Empty;

    /// <summary>Whether no app opened the link.</summary>
    [ObservableProperty]
    public partial bool ShowsOpenFailed { get; set; }

    /// <summary>"Then enter the 6-digit code your authenticator app shows for District AI."</summary>
    [ObservableProperty]
    public partial string CodePrompt { get; set; } = string.Empty;

    /// <summary>"6-digit code".</summary>
    [ObservableProperty]
    public partial string CodeLabel { get; set; } = string.Empty;

    /// <summary>"Turn on two-step verification".</summary>
    [ObservableProperty]
    public partial string ConfirmLabel { get; set; } = string.Empty;

    /// <summary>"Back".</summary>
    [ObservableProperty]
    public partial string BackLabel { get; set; } = string.Empty;

    /// <summary>"Save your recovery codes".</summary>
    [ObservableProperty]
    public partial string CodesTitle { get; set; } = string.Empty;

    /// <summary>That each signs in once and they will not be shown again.</summary>
    [ObservableProperty]
    public partial string CodesWarning { get; set; } = string.Empty;

    /// <summary>The recovery codes, one per line, or empty once the page is left.</summary>
    [ObservableProperty]
    public partial string Codes { get; set; } = string.Empty;

    /// <summary>"Copy all", then "Copied" once it has copied.</summary>
    [ObservableProperty]
    public partial string CopyAllLabel { get; set; } = string.Empty;

    /// <summary>"I have saved them".</summary>
    [ObservableProperty]
    public partial string DoneLabel { get; set; } = string.Empty;

    /// <summary>Raised when the secret leaves: the page empties the code box and the picture.</summary>
    public event EventHandler? SecretsCleared;

    internal void Attach(PageContext context)
    {
        _context = context;
        Reauth.Attach(context);
    }

    /// <summary>Shows <paramref name="view"/>.</summary>
    internal void Show(TwoStepView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        var stepChanged = _shownStep != view.Step;
        if (stepChanged && _shownStep is TwoStepStepView.Scan or TwoStepStepView.RecoveryCodes)
        {
            DropSecrets();
        }
        _shownStep = view.Step;
        Title = view.Title;
        IsLoading = view.Step == TwoStepStepView.Loading;
        IsLoadFailed = view.Step == TwoStepStepView.LoadFailed;
        IsOverview = view.Step == TwoStepStepView.Overview;
        IsScan = view.Step == TwoStepStepView.Scan;
        IsCodes = view.Step == TwoStepStepView.RecoveryCodes;
        Busy = view.Busy;
        Enabled = !view.Busy;
        Failure = view.Failure ?? string.Empty;
        HasFailure = Failure.Length > 0;
        Notice = view.Notice ?? string.Empty;
        HasNotice = Notice.Length > 0;
        StatusLabel = view.StatusLabel ?? string.Empty;
        StatusLines = string.Join("\n", view.StatusLines);
        _offered = view.Offered;
        OfferedLabel = view.OfferedLabel ?? string.Empty;
        HasOffered = view.Offered is not null;
        OffersTurnOff = view.Offered == TwoStepChangeView.TurnOff;
        OffersTurnOn = view.Offered == TwoStepChangeView.TurnOn;
        RetryLabel = view.RetryLabel;
        Reauth.Show(view.Reauth, view.Busy);
        if (view.Scan is { } scan)
        {
            ShowScan(scan, stepChanged);
        }
        if (view.Codes is { } codes)
        {
            ShowCodes(codes, stepChanged);
        }
    }

    private void ShowScan(TwoStepScanView scan, bool arriving)
    {
        ScanTitle = scan.Title;
        ScanIntro = scan.Intro;
        QrLabel = scan.QrLabel;
        QrUnreadable = scan.QrUnreadable;
        KeyLabel = scan.KeyLabel;
        OpenLabel = scan.OpenLabel;
        OpenFailed = scan.OpenFailed;
        CodePrompt = scan.CodePrompt;
        CodeLabel = scan.CodeLabel;
        ConfirmLabel = scan.ConfirmLabel;
        BackLabel = scan.BackLabel;
        _copied = scan.CopiedLabel;
        if (arriving || Key.Length == 0)
        {
            Key = Encoding.UTF8.GetString(scan.Key);
            _otpauthUri = Encoding.UTF8.GetString(scan.OtpauthUri);
            QrPng = scan.QrPng;
            HasQr = scan.QrPng is { Length: > 0 };
            ShowsQrUnreadable = !HasQr;
            ShowsOpenFailed = false;
            CopyKeyLabel = scan.CopyKeyLabel;
        }
    }

    private void ShowCodes(TwoStepCodesView codes, bool arriving)
    {
        CodesTitle = codes.Title;
        CodesWarning = codes.Warning;
        DoneLabel = codes.DoneLabel;
        _copied = codes.CopiedLabel;
        if (arriving || Codes.Length == 0)
        {
            Codes = Encoding.UTF8.GetString(codes.Codes);
            CopyAllLabel = codes.CopyAllLabel;
        }
    }

    /// <summary>
    /// The page is no longer showing: the key, its link, its picture and the
    /// codes go from here and from the page. The next visit shows what the
    /// core still holds.
    /// </summary>
    internal void Leave()
    {
        DropSecrets();
        Reauth.Clear();
        _shownStep = null;
    }

    private void DropSecrets()
    {
        Key = string.Empty;
        _otpauthUri = string.Empty;
        QrPng = null;
        HasQr = false;
        Codes = string.Empty;
        SecretsCleared?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Windows could not draw <paramref name="png"/>, the picture showing:
    /// the page says to enter the key by hand instead. A picture that has
    /// already gone is left alone.
    /// </summary>
    internal void QrUnreadableHere(byte[] png)
    {
        if (ReferenceEquals(png, QrPng))
        {
            HasQr = false;
            ShowsQrUnreadable = true;
        }
    }

    /// <summary>
    /// Turns it on with the typed <paramref name="code"/>. True when it went;
    /// the page then empties the box.
    /// </summary>
    internal bool Confirm(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        if (Busy || !IsScan || code.Trim().Length == 0)
        {
            return false;
        }
        var typed = Encoding.UTF8.GetBytes(code);
        try
        {
            _context?.Send(Event(new TwoStepAction.Confirm(typed)));
        }
        finally
        {
            Array.Clear(typed);
        }
        return true;
    }

    /// <summary>Opens the key's link in an authenticator app; says so when none opened it.</summary>
    internal async Task OpenAuthenticatorAsync()
    {
        if (_context is null || _otpauthUri.Length == 0)
        {
            return;
        }
        var opened = await _context.OpenAsync(_otpauthUri).ConfigureAwait(true);
        ShowsOpenFailed = !opened && IsScan;
    }

    private static UiEvent.TwoStep Event(TwoStepAction action) => new(action);

    [RelayCommand]
    private void Retry()
    {
        if (!Busy)
        {
            _context?.Send(Event(new TwoStepAction.Retry()));
        }
    }

    [RelayCommand]
    private void Begin()
    {
        if (!Busy && _offered is { } change)
        {
            _context?.Send(Event(new TwoStepAction.Begin(change)));
        }
    }

    [RelayCommand]
    private void CopyKey()
    {
        if (Key.Length > 0 && _copies.Copy(Key))
        {
            CopyKeyLabel = _copied;
        }
    }

    [RelayCommand]
    private void CopyCodes()
    {
        if (Codes.Length > 0 && _copies.Copy(Codes))
        {
            CopyAllLabel = _copied;
        }
    }

    [RelayCommand]
    private void FinishRecoveryCodes()
    {
        if (!Busy)
        {
            _context?.Send(Event(new TwoStepAction.FinishRecoveryCodes()));
        }
    }

    [RelayCommand]
    private void Back()
    {
        if (!Busy)
        {
            _context?.Send(Event(new TwoStepAction.Back()));
        }
    }

    [RelayCommand]
    private void DismissNotices() => _context?.Send(Event(new TwoStepAction.DismissNotices()));
}
