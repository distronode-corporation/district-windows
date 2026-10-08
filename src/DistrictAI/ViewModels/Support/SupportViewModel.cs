using CommunityToolkit.Mvvm.ComponentModel;
using DistrictAI.Core.Ffi;

namespace DistrictAI.ViewModels.Support;

/// <summary>The support requests: what the page shows, and what it sends. A stub until the area's packet builds it.</summary>
public sealed partial class SupportViewModel : ObservableObject
{
    private PageContext? _context;

    /// <summary>The core's view of the screen.</summary>
    [ObservableProperty]
    public partial SupportView? View { get; set; }

    internal void Attach(PageContext context) => _context = context;

    internal void Show(SupportView view) => View = view;

    /// <summary>Forwards one of the screen's actions.</summary>
    internal void Send(SupportAction action) => _context?.Send(new UiEvent.Support(action));
}
