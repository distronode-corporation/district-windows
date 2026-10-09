using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels;
using DistrictAI.Views.Contacts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views;

/// <summary>
/// One contact: their details, the AI dossier with Report, and the changes a
/// member may make. The core asks before deleting, clearing research,
/// blocking and unblocking, and holds the edit form; each question and the
/// form are dialogs that live exactly as long as the core holds them.
/// </summary>
public sealed partial class ContactDetailPage : UserControl
{
    private readonly CoreQuestion _question = new();
    private readonly ContactFormDialog.Host _form = new();
    private PageContext? _context;

    /// <summary>A page with nothing shown yet.</summary>
    public ContactDetailPage()
    {
        InitializeComponent();
        Status.Attach(ViewModel.Load);
        Loaded += (_, _) => SyncDialogs();
        // A dialog never outlives its page.
        Unloaded += (_, _) =>
        {
            _question.Close();
            _form.Close();
        };
    }

    /// <summary>What the page shows, and its buttons.</summary>
    public ContactDetailViewModel ViewModel { get; } = new();

    internal void Attach(PageContext context)
    {
        _context = context;
        ViewModel.Attach(context);
    }

    internal void Show(ContactDetailView view)
    {
        ViewModel.Show(view, _context?.ReportSending ?? false);
        SyncDialogs();
    }

    private void SyncDialogs()
    {
        var changes = ViewModel.Changes;
        _question.Sync(
            this,
            _context,
            changes.Confirming,
            changes.ConfirmAction,
            changes.ConfirmQuestion,
            changes.ConfirmAction,
            changes.ConfirmDestructive,
            changes.Answer);
        _form.Sync(this, _context, changes.Edit);
    }

    private void OnFailureClosed(InfoBar sender, object args) => ViewModel.Changes.DismissFailureCommand.Execute(null);

    private async void OnReportClick(object sender, RoutedEventArgs e)
    {
        if (_context is null || XamlRoot is null)
        {
            return;
        }
        await ReportDialog.OfferAsync(XamlRoot, _context, ViewModel.Report, ViewModel.Target()).ConfigureAwait(true);
    }
}
