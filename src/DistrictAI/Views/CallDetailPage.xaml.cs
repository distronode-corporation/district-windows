using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views;

/// <summary>One call: its details, the AI summary and analysis, and the transcript.</summary>
public sealed partial class CallDetailPage : UserControl
{
    private PageContext? _context;

    /// <summary>A page with nothing shown yet.</summary>
    public CallDetailPage()
    {
        InitializeComponent();
        Status.Attach(ViewModel.Load);
        Refresh.Attach(ViewModel.Load);
    }

    /// <summary>What the page shows, and its buttons.</summary>
    public CallDetailViewModel ViewModel { get; } = new();

    internal void Attach(PageContext context)
    {
        _context = context;
        ViewModel.Attach(context);
    }

    internal void Show(CallDetailView view) => ViewModel.Show(view, _context?.ReportSending ?? false);

    private async void OnReportClick(object sender, RoutedEventArgs e)
    {
        if (_context is null || XamlRoot is null)
        {
            return;
        }
        await ReportDialog.OfferAsync(XamlRoot, _context, ViewModel.Report, ViewModel.Target()).ConfigureAwait(true);
    }
}
