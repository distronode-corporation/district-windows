using DistrictAI.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views;

/// <summary>A screen this build does not have yet: where to find it instead, and the way back.</summary>
public sealed partial class UnavailablePage : UserControl
{
    /// <summary>A page with nothing shown yet.</summary>
    public UnavailablePage()
    {
        InitializeComponent();
    }

    /// <summary>What the page shows, and its button.</summary>
    public UnavailableViewModel ViewModel { get; } = new();

    internal void Attach(PageContext context) => ViewModel.Attach(context);

    internal void Show(string title, string body) => ViewModel.Show(title, body);
}
