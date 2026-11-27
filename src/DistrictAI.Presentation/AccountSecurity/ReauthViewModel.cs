// SPDX-FileCopyrightText: Distronode Corporation
//
// SPDX-License-Identifier: Apache-2.0

using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DistrictAI.Core.Ffi;

namespace DistrictAI.ViewModels.AccountSecurity;

/// <summary>One way to confirm it is you at a provider: its button.</summary>
public sealed record ReauthChoiceItem(ReauthProviderView Provider, string Label)
{
    /// <summary>The button's AutomationId: "ReauthGoogle", "ReauthMicrosoft", "ReauthApple".</summary>
    public string AutomationId => "Reauth" + Provider;
}

/// <summary>
/// Confirming it is you, on the two-step screen and the deletion screen: a
/// fresh sign-in at Google, Microsoft or Apple in the browser, or the password
/// with, once the service asks for it, the authenticator or recovery code.
/// </summary>
/// <remarks>
/// The password and the code are the boxes' own: never bound, never kept
/// here. <see cref="Submit"/> reads them once, as the page hands them over,
/// sends them to the core as UTF-8 bytes (which no record's <c>ToString</c>
/// prints), clears those bytes and raises <see cref="FieldsCleared"/>, which
/// empties the boxes. The boxes are emptied too whenever the password step
/// goes (a step back, the proof accepted) and when the page is left
/// (<see cref="Clear"/>).
/// </remarks>
public sealed partial class ReauthViewModel : ObservableObject
{
    private readonly Func<ReauthProviderView, UiEvent> _provider;
    private readonly Func<UiEvent> _password;
    private readonly Func<byte[], byte[]?, UiEvent> _submit;
    private readonly Func<UiEvent> _back;
    private PageContext? _context;

    /// <summary>A re-authentication whose actions are the screen's own events.</summary>
    public ReauthViewModel(
        Func<ReauthProviderView, UiEvent> provider,
        Func<UiEvent> password,
        Func<byte[], byte[]?, UiEvent> submit,
        Func<UiEvent> back)
    {
        _provider = provider;
        _password = password;
        _submit = submit;
        _back = back;
    }

    /// <summary>The providers, in the order the core offers them.</summary>
    public ObservableCollection<ReauthChoiceItem> Choices { get; } = [];

    /// <summary>Whether the screen is on this step at all.</summary>
    [ObservableProperty]
    public partial bool IsShown { get; set; }

    /// <summary>"Confirm it is you".</summary>
    [ObservableProperty]
    public partial string Heading { get; set; } = string.Empty;

    /// <summary>What happens once it is confirmed.</summary>
    [ObservableProperty]
    public partial string Intro { get; set; } = string.Empty;

    /// <summary>Whether the providers and "Use my password" show: choosing, or the browser open.</summary>
    [ObservableProperty]
    public partial bool IsChoosing { get; set; }

    /// <summary>"Waiting for sign-in to finish in your browser.", or empty.</summary>
    [ObservableProperty]
    public partial string Waiting { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="Waiting"/>.</summary>
    [ObservableProperty]
    public partial bool HasWaiting { get; set; }

    /// <summary>Whether the password box shows.</summary>
    [ObservableProperty]
    public partial bool IsPassword { get; set; }

    /// <summary>Whether the code goes with the password: the service asked for it.</summary>
    [ObservableProperty]
    public partial bool NeedsCode { get; set; }

    /// <summary>Why the code goes with the password, or empty.</summary>
    [ObservableProperty]
    public partial string CodePrompt { get; set; } = string.Empty;

    /// <summary>"Use my password".</summary>
    [ObservableProperty]
    public partial string UsePasswordLabel { get; set; } = string.Empty;

    /// <summary>"Your password".</summary>
    [ObservableProperty]
    public partial string PasswordLabel { get; set; } = string.Empty;

    /// <summary>"Authenticator code or recovery code".</summary>
    [ObservableProperty]
    public partial string CodeLabel { get; set; } = string.Empty;

    /// <summary>"Continue".</summary>
    [ObservableProperty]
    public partial string ContinueLabel { get; set; } = string.Empty;

    /// <summary>"Back".</summary>
    [ObservableProperty]
    public partial string BackLabel { get; set; } = string.Empty;

    /// <summary>Whether something is on its way: every control is disabled.</summary>
    [ObservableProperty]
    public partial bool Busy { get; set; }

    /// <summary>Whether the controls can be used: not <see cref="Busy"/>.</summary>
    [ObservableProperty]
    public partial bool Enabled { get; set; } = true;

    /// <summary>Raised when the password and code boxes are to be emptied.</summary>
    public event EventHandler? FieldsCleared;

    internal void Attach(PageContext context) => _context = context;

    /// <summary>Shows <paramref name="view"/>, or hides the step when the screen has none.</summary>
    internal void Show(ReauthView? view, bool busy)
    {
        var wasPassword = IsPassword;
        Busy = busy;
        Enabled = !busy;
        IsShown = view is not null;
        IsChoosing = view?.Mode is ReauthModeView.Choose or ReauthModeView.Browser;
        IsPassword = view?.Mode == ReauthModeView.Password;
        NeedsCode = view?.CodePrompt is not null;
        CodePrompt = view?.CodePrompt ?? string.Empty;
        Waiting = view?.Waiting ?? string.Empty;
        HasWaiting = Waiting.Length > 0;
        if (view is not null)
        {
            Heading = view.Heading;
            Intro = view.Intro;
            UsePasswordLabel = view.UsePasswordLabel;
            PasswordLabel = view.PasswordLabel;
            CodeLabel = view.CodeLabel;
            ContinueLabel = view.ContinueLabel;
            BackLabel = view.BackLabel;
            Display.Sync(Choices, [.. view.Choices.Select(choice => new ReauthChoiceItem(choice.Provider, choice.Label))]);
        }
        if (wasPassword && !IsPassword)
        {
            Clear();
        }
    }

    /// <summary>Empties the password and code boxes: the page is left, or the step has gone.</summary>
    internal void Clear() => FieldsCleared?.Invoke(this, EventArgs.Empty);

    /// <summary>Confirm it is you at <paramref name="provider"/>, in the browser.</summary>
    internal void Choose(ReauthProviderView provider)
    {
        if (!Busy)
        {
            _context?.Send(_provider(provider));
        }
    }

    /// <summary>
    /// Sends the typed <paramref name="password"/>, and <paramref name="code"/>
    /// when the step asks for one, once both are filled in. True when it went;
    /// the boxes are then emptied.
    /// </summary>
    internal bool Submit(string password, string code)
    {
        ArgumentNullException.ThrowIfNull(password);
        ArgumentNullException.ThrowIfNull(code);
        if (Busy || !IsPassword || password.Length == 0 || (NeedsCode && code.Trim().Length == 0))
        {
            return false;
        }
        var typed = Encoding.UTF8.GetBytes(password);
        var typedCode = NeedsCode ? Encoding.UTF8.GetBytes(code) : null;
        try
        {
            // Lowered into the core's own memory as it is sent.
            _context?.Send(_submit(typed, typedCode));
        }
        finally
        {
            Array.Clear(typed);
            if (typedCode is not null)
            {
                Array.Clear(typedCode);
            }
            // The boxes empty even when the send failed.
            Clear();
        }
        return true;
    }

    [RelayCommand]
    private void UsePassword()
    {
        if (!Busy)
        {
            _context?.Send(_password());
        }
    }

    [RelayCommand]
    private void Back()
    {
        if (!Busy)
        {
            _context?.Send(_back());
        }
    }
}
