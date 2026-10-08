using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views;

/// <summary>One contact: their details and the AI dossier. Editing, deleting and blocking stay on the web and the phone apps.</summary>
public sealed partial class ContactDetailPage : UserControl
{
    private PageContext? _context;

    /// <summary>A page with nothing shown yet.</summary>
    public ContactDetailPage()
    {
        InitializeComponent();
        Status.Attach(ViewModel.Load);
    }

    /// <summary>What the page shows, and its buttons.</summary>
    public ContactDetailViewModel ViewModel { get; } = new();

    internal void Attach(PageContext context)
    {
        _context = context;
        ViewModel.Attach(context);
    }

    internal void Show(ContactDetailView view) => ViewModel.Show(view, _context?.ReportSending ?? false);

    private async void OnReportClick(object sender, RoutedEventArgs e)
    {
        if (_context is null || XamlRoot is null)
        {
            return;
        }
        await ReportDialog.OfferAsync(XamlRoot, _context, ViewModel.Report, ViewModel.Target()).ConfigureAwait(true);
    }
}
