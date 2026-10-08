using CommunityToolkit.Mvvm.ComponentModel;
using DistrictAI.Core.Ffi;

namespace DistrictAI.ViewModels.Scheduling;

/// <summary>The booking pages: what the page shows, and what it sends. A stub until the area's packet builds it.</summary>
public sealed partial class SchedulingViewModel : ObservableObject
{
    private PageContext? _context;

    /// <summary>The core's view of the screen.</summary>
    [ObservableProperty]
    public partial SchedulingView? View { get; set; }

    internal void Attach(PageContext context) => _context = context;

    internal void Show(SchedulingView view) => View = view;

    /// <summary>Forwards one of the screen's actions.</summary>
    internal void Send(SchedulingAction action) => _context?.Send(new UiEvent.Scheduling(action));
}
