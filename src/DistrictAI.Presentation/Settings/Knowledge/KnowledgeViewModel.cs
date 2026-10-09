using CommunityToolkit.Mvvm.ComponentModel;
using DistrictAI.Core.Ffi;

namespace DistrictAI.ViewModels.Settings.Knowledge;

/// <summary>The knowledge base section: what the page shows, and what it sends. A stub until the area's packet builds it.</summary>
public sealed partial class KnowledgeViewModel : ObservableObject
{
    private PageContext? _context;

    /// <summary>The core's view of the screen.</summary>
    [ObservableProperty]
    public partial KnowledgeView? View { get; set; }

    internal void Attach(PageContext context) => _context = context;

    internal void Show(KnowledgeView view) => View = view;

    /// <summary>Forwards one of the screen's actions.</summary>
    internal void Send(KnowledgeAction action) => _context?.Send(new UiEvent.Knowledge(action));
}
