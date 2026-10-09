using CommunityToolkit.Mvvm.ComponentModel;
using DistrictAI.Core.Ffi;

namespace DistrictAI.ViewModels.Hq;

/// <summary>District HQ: what the page shows, and what it sends. A stub until the area's packet builds it.</summary>
public sealed partial class HqViewModel : ObservableObject
{
    private PageContext? _context;

    /// <summary>The core's view of the screen.</summary>
    [ObservableProperty]
    public partial HqView? View { get; set; }

    internal void Attach(PageContext context) => _context = context;

    internal void Show(HqView view) => View = view;

    /// <summary>Forwards one of the screen's actions.</summary>
    internal void Send(HqAction action) => _context?.Send(new UiEvent.Hq(action));
}
