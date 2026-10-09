using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DistrictAI.Core.Ffi;

namespace DistrictAI.ViewModels;

/// <summary>The installations signed in to the account, and signing them out, each after a question.</summary>
public sealed partial class DevicesViewModel : ObservableObject
{
    private PageContext? _context;

    /// <summary>Loading, failure and refresh.</summary>
    public LoadStateViewModel Load { get; } = new();

    /// <summary>The devices.</summary>
    public ObservableCollection<DeviceRowItem> Rows { get; } = [];

    /// <summary>Whether a sign-out is under way.</summary>
    [ObservableProperty]
    public partial bool Busy { get; set; }

    /// <summary>Whether "Sign out of every device" can be pressed now.</summary>
    [ObservableProperty]
    public partial bool EverywhereEnabled { get; set; }

    /// <summary>Why the last sign-out failed, or that it had nothing to do; empty otherwise.</summary>
    [ObservableProperty]
    public partial string Notice { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="Notice"/>.</summary>
    [ObservableProperty]
    public partial bool HasNotice { get; set; }

    /// <summary>Whether the core is asking before a sign-out.</summary>
    public bool Confirming { get; private set; }

    /// <summary>The question it is asking.</summary>
    public string ConfirmQuestion { get; private set; } = string.Empty;

    /// <summary>The button that answers yes.</summary>
    public string ConfirmAction { get; private set; } = string.Empty;

    internal void Attach(PageContext context)
    {
        _context = context;
        Load.Attach(context);
    }

    internal void Show(DevicesView view)
    {
        Load.Show(view.Status, view.Rows.Length > 0, view.Empty, view.Refreshing, refreshFailure: null);
        Busy = view.Busy;
        EverywhereEnabled = Load.Ready && !view.Busy && view.Rows.Length > 0;
        Display.Sync(Rows, [.. view.Rows.Select(row => DeviceRowItem.From(row, view.Busy))]);
        Notice = view.Failure is not null
            ? Display.Failure(view.Failure)
            : view.NothingRevoked ? view.NothingRevokedNote ?? string.Empty : string.Empty;
        HasNotice = Notice.Length > 0;
        Confirming = view.Confirming is not null;
        ConfirmQuestion = view.Confirming?.Question ?? string.Empty;
        ConfirmAction = view.Confirming?.Action ?? string.Empty;
    }

    internal void AskSignOut(DeviceRowItem row)
    {
        if (Busy)
        {
            return;
        }
        _context?.Send(new UiEvent.AskSignOutDevice(row.DeviceId));
    }

    /// <summary>The answer to the question: yes signs out, no cancels.</summary>
    internal void Answer(bool confirmed) =>
        _context?.Send(confirmed ? new UiEvent.ConfirmDevices() : new UiEvent.CancelDevices());

    [RelayCommand]
    private void SignOutEverywhere()
    {
        if (!EverywhereEnabled)
        {
            return;
        }
        EverywhereEnabled = false;
        _context?.Send(new UiEvent.AskSignOutEverywhere());
    }

    [RelayCommand]
    private void DismissNotices() => _context?.Send(new UiEvent.DismissDevicesNotices());
}
