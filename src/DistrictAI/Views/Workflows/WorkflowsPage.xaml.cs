using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels;
using DistrictAI.ViewModels.Workflows;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views.Workflows;

/// <summary>
/// The workflows: the outbound campaign's card, and each workflow with its
/// switch and its runs. The core asks before pausing or resuming the campaign;
/// the question is a dialog that lives exactly as long as the core is asking
/// it, and no longer than the page shows (the devices page's pattern).
/// </summary>
public sealed partial class WorkflowsPage : UserControl
{
    private PageContext? _context;
    private ContentDialog? _question;
    private bool _closedByCore;

    /// <summary>A page with nothing shown yet.</summary>
    public WorkflowsPage()
    {
        InitializeComponent();
        Status.Attach(ViewModel.Load);
        CampaignStatus.Attach(ViewModel.CampaignLoad);
        Loaded += (_, _) => Ask();
        // A question never outlives its screen: leaving the page, or signing
        // out, takes it away. The core drops the question when the screen is
        // entered again.
        Unloaded += (_, _) => CloseQuestion();
    }

    /// <summary>What the page shows, and its actions.</summary>
    public WorkflowsViewModel ViewModel { get; } = new();

    internal void Attach(PageContext context)
    {
        _context = context;
        ViewModel.Attach(context);
    }

    internal void Show(WorkflowsView view)
    {
        ViewModel.Show(view);
        Ask();
    }

    /// <summary>Opens the question the core is asking, or closes one it has stopped asking.</summary>
    private async void Ask()
    {
        if (!ViewModel.Confirming)
        {
            CloseQuestion();
            return;
        }
        if (_question is not null || _context is null || _context.DialogOpen || XamlRoot is null)
        {
            return;
        }
        var question = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"],
            Title = ViewModel.ConfirmTitle,
            Content = new TextBlock { Text = ViewModel.ConfirmBody, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = ViewModel.ConfirmAction,
            CloseButtonText = "Cancel",
            // Resuming spends credit and pausing stops calls: neither happens on Enter.
            DefaultButton = ContentDialogButton.Close,
        };
        _question = question;
        _context.DialogOpen = true;
        ContentDialogResult result;
        try
        {
            result = await question.ShowAsync();
        }
        finally
        {
            _question = null;
            _context.DialogOpen = false;
        }
        if (_closedByCore)
        {
            _closedByCore = false;
            return;
        }
        ViewModel.Answer(result == ContentDialogResult.Primary);
    }

    private void CloseQuestion()
    {
        if (_question is not null)
        {
            _closedByCore = true;
            _question.Hide();
        }
    }

    private static WorkflowItem? RowOf(object sender) => (sender as FrameworkElement)?.DataContext as WorkflowItem;

    private void OnRunsClick(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is { } row)
        {
            ViewModel.ToggleRuns(row);
        }
    }

    private void OnSwitchToggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch toggle && RowOf(sender) is { } row)
        {
            ViewModel.SetActive(row, toggle.IsOn);
        }
    }

    private void OnMoreRunsClick(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is { } row)
        {
            // Until the core's next snapshot: one press, one page.
            ((Control)sender).IsEnabled = false;
            ViewModel.LoadMoreRuns(row);
        }
    }

    private void OnRetryRunsClick(object sender, RoutedEventArgs e)
    {
        ((Control)sender).IsEnabled = false;
        ViewModel.RetryRuns();
    }

    private void OnToggleFailureClosed(InfoBar sender, object args) => ViewModel.DismissToggleFailureCommand.Execute(null);
}
