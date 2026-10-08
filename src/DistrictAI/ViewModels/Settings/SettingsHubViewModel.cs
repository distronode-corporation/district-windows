using CommunityToolkit.Mvvm.ComponentModel;
using DistrictAI.Core.Ffi;

namespace DistrictAI.ViewModels.Settings;

/// <summary>The workspace settings hub: what the page shows, and what it sends. A stub until the area's packet builds it.</summary>
public sealed partial class SettingsHubViewModel : ObservableObject
{
    private PageContext? _context;

    /// <summary>The core's view of the screen.</summary>
    [ObservableProperty]
    public partial SettingsHubView? View { get; set; }

    internal void Attach(PageContext context) => _context = context;

    internal void Show(SettingsHubView view) => View = view;

    /// <summary>Forwards one of the screen's actions.</summary>
    internal void Send(SettingsAction action) => _context?.Send(new UiEvent.Settings(action));
}
