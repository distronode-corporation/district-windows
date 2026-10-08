using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels;
using DistrictAI.ViewModels.Settings.Messaging;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views.Settings.Messaging;

/// <summary>
/// The messaging accounts section. A stub until the area's packet builds it: until then the core
/// shows the unavailable page instead, so this one is never on screen.
/// </summary>
public sealed partial class MessagingPage : UserControl
{
    /// <summary>A page with nothing shown yet.</summary>
    public MessagingPage()
    {
        InitializeComponent();
    }

    /// <summary>What the page shows, and its actions.</summary>
    public MessagingViewModel ViewModel { get; } = new();

    internal void Attach(PageContext context) => ViewModel.Attach(context);

    internal void Show(MessagingView view) => ViewModel.Show(view);
}
