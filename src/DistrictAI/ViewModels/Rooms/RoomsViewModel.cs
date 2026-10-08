using CommunityToolkit.Mvvm.ComponentModel;
using DistrictAI.Core.Ffi;

namespace DistrictAI.ViewModels.Rooms;

/// <summary>The meeting rooms: what the page shows, and what it sends. A stub until the area's packet builds it.</summary>
public sealed partial class RoomsViewModel : ObservableObject
{
    private PageContext? _context;

    /// <summary>The core's view of the screen.</summary>
    [ObservableProperty]
    public partial RoomsView? View { get; set; }

    internal void Attach(PageContext context) => _context = context;

    internal void Show(RoomsView view) => View = view;

    /// <summary>Forwards one of the screen's actions.</summary>
    internal void Send(RoomsAction action) => _context?.Send(new UiEvent.Rooms(action));
}
