using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views;

/// <summary>One conversation, oldest first, and the reply box under it for a member who may reply.</summary>
public sealed partial class ThreadPage : UserControl
{
    private PageContext? _context;

    /// <summary>A page with nothing shown yet.</summary>
    public ThreadPage()
    {
        InitializeComponent();
        Status.Attach(ViewModel.Load);
        Refresh.Attach(ViewModel.Load);
    }

    /// <summary>What the page shows, and its buttons.</summary>
    public ThreadViewModel ViewModel { get; } = new();

    internal void Attach(PageContext context)
    {
        _context = context;
        ViewModel.Attach(context);
        ReplyBox.Attach(ViewModel.Composer, context);
    }

    internal void Show(ThreadView view) => ViewModel.Show(view, _context?.ReportSending ?? false);

    /// <summary>Where an item's time and Report button sit: sent messages on the right, everything else on the left.</summary>
    public static HorizontalAlignment SideFor(bool outbound) => outbound ? HorizontalAlignment.Right : HorizontalAlignment.Left;

    private async void OnReportClick(object sender, RoutedEventArgs e)
    {
        if (_context is null || XamlRoot is null || (sender as FrameworkElement)?.DataContext is not TimelineItem item)
        {
            return;
        }
        await ReportDialog.OfferAsync(XamlRoot, _context, item.Report, ViewModel.TargetFor(item)).ConfigureAwait(true);
    }
}
