using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels;
using DistrictAI.ViewModels.Desk;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views.Desk;

/// <summary>
/// One help desk ticket: who raised it, its status and the buttons that move
/// it, its conversation, and the reply. Each change is sent once, on its
/// press, and the ticket shows what the service answers with.
/// </summary>
public sealed partial class DeskTicketPage : UserControl
{
    /// <summary>A page with nothing shown yet.</summary>
    public DeskTicketPage()
    {
        InitializeComponent();
        Status.Attach(ViewModel.Load);
        Refresh.Attach(ViewModel.Load);
    }

    /// <summary>What the page shows, and its actions.</summary>
    public DeskTicketViewModel ViewModel { get; } = new();

    internal void Attach(PageContext context) => ViewModel.Attach(context);

    internal void Show(DeskTicketView view) => ViewModel.Show(view);

    private void OnStatusClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is DeskStatusItem choice)
        {
            ViewModel.SetStatus(choice);
        }
    }
}
