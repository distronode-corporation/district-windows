using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DistrictAI.Core.Ffi;

namespace DistrictAI.ViewModels;

/// <summary>
/// The account: who is signed in, this build and this computer, starting at
/// sign-in, the devices, signing out, and where deleting the account starts.
/// </summary>
public sealed partial class AccountViewModel : ObservableObject
{
    private readonly IStartupRegistration _startup = new NoStartupRegistration();
    private PageContext? _context;
    private bool _startupCanChange;
    private bool _startupBusy;

    /// <summary>The user's name, or empty.</summary>
    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="Name"/>.</summary>
    [ObservableProperty]
    public partial bool HasName { get; set; }

    /// <summary>The user's email address, or empty.</summary>
    [ObservableProperty]
    public partial string Email { get; set; } = string.Empty;

    /// <summary>Whether there is an <see cref="Email"/>.</summary>
    [ObservableProperty]
    public partial bool HasEmail { get; set; }

    /// <summary>The user's id.</summary>
    [ObservableProperty]
    public partial string UserId { get; set; } = string.Empty;

    /// <summary>This build, "Version 1.0.0".</summary>
    [ObservableProperty]
    public partial string AppVersion { get; set; } = string.Empty;

    /// <summary>This installation's id.</summary>
    [ObservableProperty]
    public partial string DeviceId { get; set; } = string.Empty;

    /// <summary>The Devices row's caption.</summary>
    [ObservableProperty]
    public partial string DevicesCaption { get; set; } = string.Empty;

    /// <summary>The Sign out row's caption.</summary>
    [ObservableProperty]
    public partial string SignOutCaption { get; set; } = string.Empty;

    /// <summary>The Delete account row's caption.</summary>
    [ObservableProperty]
    public partial string DeleteAccountCaption { get; set; } = string.Empty;

    /// <summary>Whether the app starts at sign-in.</summary>
    [ObservableProperty]
    public partial bool StartupEnabled { get; set; }

    /// <summary>Whether the switch can be used now.</summary>
    [ObservableProperty]
    public partial bool StartupSwitchEnabled { get; set; }

    /// <summary>Why the setting cannot be changed, or empty.</summary>
    [ObservableProperty]
    public partial string StartupMessage { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="StartupMessage"/>.</summary>
    [ObservableProperty]
    public partial bool HasStartupMessage { get; set; }

    internal void Attach(PageContext context) => _context = context;

    internal void Show(AccountView view)
    {
        Name = view.Name ?? string.Empty;
        HasName = Name.Length > 0;
        Email = view.Email ?? string.Empty;
        HasEmail = Email.Length > 0;
        UserId = view.UserId;
        AppVersion = "Version " + view.AppVersion;
        DeviceId = view.DeviceId;
        DevicesCaption = view.DevicesCaption;
        SignOutCaption = view.SignOutCaption;
        DeleteAccountCaption = view.DeleteAccountCaption;
    }

    /// <summary>Reads the start-at-sign-in setting, each time the page opens.</summary>
    internal async Task LoadStartupAsync()
    {
        if (_startupBusy)
        {
            return;
        }
        _startupBusy = true;
        StartupSwitchEnabled = false;
        try
        {
            ShowStartup(await _startup.GetAsync().ConfigureAwait(true));
        }
        finally
        {
            _startupBusy = false;
            StartupSwitchEnabled = _startupCanChange;
        }
    }

    /// <summary>The switch was flipped to <paramref name="enabled"/>.</summary>
    internal async Task SetStartupAsync(bool enabled)
    {
        if (_startupBusy || !_startupCanChange || enabled == StartupEnabled)
        {
            return;
        }
        _startupBusy = true;
        StartupSwitchEnabled = false;
        try
        {
            ShowStartup(await _startup.SetAsync(enabled).ConfigureAwait(true));
        }
        finally
        {
            _startupBusy = false;
            StartupSwitchEnabled = _startupCanChange;
        }
    }

    private void ShowStartup(StartupState state)
    {
        _startupCanChange = state.CanChange;
        StartupEnabled = state.Enabled;
        StartupMessage = state.Message ?? string.Empty;
        HasStartupMessage = StartupMessage.Length > 0;
    }

    [RelayCommand]
    private void OpenDevices() => _context?.Send(new UiEvent.OpenDevices());

    [RelayCommand]
    private void SignOut() => _context?.Send(new UiEvent.SignOut());

    [RelayCommand]
    private void DeleteAccount() => _context?.Send(new UiEvent.DeleteAccount());
}
