using CommunityToolkit.Mvvm.ComponentModel;
using DistrictAI.Core.Ffi;

namespace DistrictAI.ViewModels.Blocked;

/// <summary>The blocked callers: what the page shows, and what it sends. A stub until the area's packet builds it.</summary>
public sealed partial class BlockedViewModel : ObservableObject
{
    private PageContext? _context;

    /// <summary>The core's view of the screen.</summary>
    [ObservableProperty]
    public partial BlockedView? View { get; set; }

    internal void Attach(PageContext context) => _context = context;

    internal void Show(BlockedView view) => View = view;

    /// <summary>Forwards one of the screen's actions.</summary>
    internal void Send(BlockedAction action) => _context?.Send(new UiEvent.Blocked(action));
}
