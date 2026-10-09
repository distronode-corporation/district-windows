using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels;
using DistrictAI.ViewModels.Blocked;
using DistrictAI.Views.Contacts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views.Blocked;

/// <summary>
/// The callers the workspace has blocked, below contacts, each with Unblock
/// for a member who may change contacts. The core asks before each unblock;
/// the question is a dialog that lives exactly as long as the core asks it.
/// </summary>
public sealed partial class BlockedPage : UserControl
{
    private readonly CoreQuestion _question = new();
    private PageContext? _context;

    /// <summary>A page with nothing shown yet.</summary>
    public BlockedPage()
    {
        InitializeComponent();
        Status.Attach(ViewModel.Load);
        Loaded += (_, _) => Ask();
        // A question never outlives its page.
        Unloaded += (_, _) => _question.Close();
    }

    /// <summary>What the page shows, and its actions.</summary>
    public BlockedViewModel ViewModel { get; } = new();

    internal void Attach(PageContext context)
    {
        _context = context;
        ViewModel.Attach(context);
    }

    internal void Show(BlockedView view)
    {
        ViewModel.Show(view);
        Ask();
    }

    private void Ask() => _question.Sync(
        this,
        _context,
        ViewModel.Confirming,
        "Unblock " + ViewModel.ConfirmName + "?",
        ViewModel.ConfirmQuestion,
        ViewModel.ConfirmAction,
        destructive: false,
        ViewModel.Answer);

    private void OnUnblockClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is BlockedRowItem row)
        {
            ViewModel.AskUnblock(row);
        }
    }

    private void OnFailureClosed(InfoBar sender, object args) => ViewModel.DismissFailureCommand.Execute(null);
}
