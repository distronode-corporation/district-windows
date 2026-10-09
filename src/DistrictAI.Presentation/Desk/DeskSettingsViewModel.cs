using CommunityToolkit.Mvvm.ComponentModel;
using DistrictAI.Core.Ffi;

namespace DistrictAI.ViewModels.Desk;

/// <summary>The help desk's settings: what the page shows, and what it sends. A stub until the area's packet builds it.</summary>
public sealed partial class DeskSettingsViewModel : ObservableObject
{
    private PageContext? _context;

    /// <summary>The core's view of the screen.</summary>
    [ObservableProperty]
    public partial DeskSettingsView? View { get; set; }

    internal void Attach(PageContext context) => _context = context;

    internal void Show(DeskSettingsView view) => View = view;

    /// <summary>Forwards one of the screen's actions.</summary>
    internal void Send(DeskAction action) => _context?.Send(new UiEvent.Desk(action));
}
