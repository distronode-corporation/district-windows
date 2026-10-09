using CommunityToolkit.Mvvm.ComponentModel;
using DistrictAI.Core.Ffi;

namespace DistrictAI.ViewModels.Settings.Routing;

/// <summary>The routing rules section: what the page shows, and what it sends. A stub until the area's packet builds it.</summary>
public sealed partial class RoutingViewModel : ObservableObject
{
    private PageContext? _context;

    /// <summary>The core's view of the screen.</summary>
    [ObservableProperty]
    public partial RoutingView? View { get; set; }

    internal void Attach(PageContext context) => _context = context;

    internal void Show(RoutingView view) => View = view;

    /// <summary>Forwards one of the screen's actions.</summary>
    internal void Send(RoutingAction action) => _context?.Send(new UiEvent.Routing(action));
}
