using DistrictAI.ViewModels.Scheduling;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views.Scheduling;

/// <summary>
/// A team's name: creating a team, or renaming one and, apart from the
/// rename, deleting it, asked once more in place.
/// </summary>
public sealed partial class TeamEditorContent : UserControl
{
    /// <summary>The fields of <paramref name="model"/>'s dialog.</summary>
    public TeamEditorContent(TeamEditorViewModel model)
    {
        ViewModel = model;
        InitializeComponent();
    }

    /// <summary>The dialog's fields.</summary>
    public TeamEditorViewModel ViewModel { get; }
}
