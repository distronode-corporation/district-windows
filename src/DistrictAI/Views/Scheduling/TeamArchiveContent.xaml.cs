using DistrictAI.ViewModels.Scheduling;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views.Scheduling;

/// <summary>
/// An account's archive: what archiving does, why it cannot be done yet, the
/// upcoming bookings in the way, and the archive asked once more in place.
/// </summary>
public sealed partial class TeamArchiveContent : UserControl
{
    /// <summary>The fields of <paramref name="model"/>'s dialog.</summary>
    public TeamArchiveContent(TeamArchiveViewModel model)
    {
        ViewModel = model;
        InitializeComponent();
    }

    /// <summary>The dialog's fields.</summary>
    public TeamArchiveViewModel ViewModel { get; }
}
