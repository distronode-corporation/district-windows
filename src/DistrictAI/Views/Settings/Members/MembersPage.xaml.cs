using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels;
using DistrictAI.ViewModels.Settings.Members;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views.Settings.Members;

/// <summary>
/// The members section. A stub until the area's packet builds it: until then the core
/// shows the unavailable page instead, so this one is never on screen.
/// </summary>
public sealed partial class MembersPage : UserControl
{
    /// <summary>A page with nothing shown yet.</summary>
    public MembersPage()
    {
        InitializeComponent();
    }

    /// <summary>What the page shows, and its actions.</summary>
    public MembersViewModel ViewModel { get; } = new();

    internal void Attach(PageContext context) => ViewModel.Attach(context);

    internal void Show(MembersView view) => ViewModel.Show(view);
}
