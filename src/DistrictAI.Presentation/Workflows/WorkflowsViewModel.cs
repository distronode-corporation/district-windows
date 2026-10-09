using CommunityToolkit.Mvvm.ComponentModel;
using DistrictAI.Core.Ffi;

namespace DistrictAI.ViewModels.Workflows;

/// <summary>The workflows: what the page shows, and what it sends. A stub until the area's packet builds it.</summary>
public sealed partial class WorkflowsViewModel : ObservableObject
{
    private PageContext? _context;

    /// <summary>The core's view of the screen.</summary>
    [ObservableProperty]
    public partial WorkflowsView? View { get; set; }

    internal void Attach(PageContext context) => _context = context;

    internal void Show(WorkflowsView view) => View = view;

    /// <summary>Forwards one of the screen's actions.</summary>
    internal void Send(WorkflowsAction action) => _context?.Send(new UiEvent.Workflows(action));
}
