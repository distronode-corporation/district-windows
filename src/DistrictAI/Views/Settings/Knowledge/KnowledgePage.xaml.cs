using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels;
using DistrictAI.ViewModels.Settings.Knowledge;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views.Settings.Knowledge;

/// <summary>
/// The knowledge base section. A stub until the area's packet builds it: until then the core
/// shows the unavailable page instead, so this one is never on screen.
/// </summary>
public sealed partial class KnowledgePage : UserControl
{
    /// <summary>A page with nothing shown yet.</summary>
    public KnowledgePage()
    {
        InitializeComponent();
    }

    /// <summary>What the page shows, and its actions.</summary>
    public KnowledgeViewModel ViewModel { get; } = new();

    internal void Attach(PageContext context) => ViewModel.Attach(context);

    internal void Show(KnowledgeView view) => ViewModel.Show(view);
}
