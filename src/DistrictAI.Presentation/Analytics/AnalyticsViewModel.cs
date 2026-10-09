using CommunityToolkit.Mvvm.ComponentModel;
using DistrictAI.Core.Ffi;

namespace DistrictAI.ViewModels.Analytics;

/// <summary>The analytics: what the page shows, and what it sends. A stub until the area's packet builds it.</summary>
public sealed partial class AnalyticsViewModel : ObservableObject
{
    private PageContext? _context;

    /// <summary>The core's view of the screen.</summary>
    [ObservableProperty]
    public partial AnalyticsView? View { get; set; }

    internal void Attach(PageContext context) => _context = context;

    internal void Show(AnalyticsView view) => View = view;

    /// <summary>Forwards one of the screen's actions.</summary>
    internal void Send(AnalyticsAction action) => _context?.Send(new UiEvent.Analytics(action));
}
