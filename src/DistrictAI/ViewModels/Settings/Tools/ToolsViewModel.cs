using CommunityToolkit.Mvvm.ComponentModel;
using DistrictAI.Core.Ffi;

namespace DistrictAI.ViewModels.Settings.Tools;

/// <summary>The Skills section: what the page shows, and what it sends. A stub until the area's packet builds it.</summary>
public sealed partial class ToolsViewModel : ObservableObject
{
    private PageContext? _context;

    /// <summary>The core's view of the screen.</summary>
    [ObservableProperty]
    public partial ToolsView? View { get; set; }

    internal void Attach(PageContext context) => _context = context;

    internal void Show(ToolsView view) => View = view;

    /// <summary>Forwards one of the screen's actions.</summary>
    internal void Send(ToolsAction action) => _context?.Send(new UiEvent.Tools(action));
}
