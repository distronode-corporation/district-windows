using DistrictAI.ViewModels;
using DistrictAI.ViewModels.Scheduling;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views.Scheduling;

/// <summary>
/// Developer: a tab each for the API keys, the connected apps beside the MCP
/// address, and the webhooks with the open one's deliveries; and its three
/// dialogs, each one the core holds open (<see cref="SchedulingDialogHost"/>):
/// making a key, adding or changing a webhook, and a revoke or a delete,
/// asked once more, its confirm never the default.
/// </summary>
public sealed partial class SchedulingDeveloperPanel : UserControl
{
    private readonly SchedulingDialogHost _key = new("DeveloperKeyDialog");
    private readonly SchedulingDialogHost _webhook = new("DeveloperWebhookDialog");
    private readonly SchedulingDialogHost _removal = new("DeveloperRemovalDialog");
    private PageContext? _context;

    /// <summary>A panel showing nothing until <see cref="Attach"/>.</summary>
    public SchedulingDeveloperPanel()
    {
        InitializeComponent();
        TabChooser.Describe = item => item is DeveloperTabEntry tab ? new TabLabel(tab.Label, tab.AutomationId) : new TabLabel(item.ToString() ?? string.Empty);
        TabChooser.Picked += (_, index) => ViewModel.SelectTab(index);
        Loaded += (_, _) => SyncDialogs();
    }

    /// <summary>What the panel shows, and its actions.</summary>
    public SchedulingDeveloperViewModel ViewModel { get; private set; } = new();

    /// <summary>Shows <paramref name="model"/>, the page's own, from now on.</summary>
    internal void Attach(SchedulingDeveloperViewModel model, PageContext context)
    {
        ViewModel = model;
        _context = context;
        Bindings.Update();
    }

    /// <summary>Opens the dialog the core holds open, and closes the ones it does not.</summary>
    internal void SyncDialogs()
    {
        _key.Sync(this, _context, ViewModel.KeyDialog.Dialog, () => new DeveloperKeyContent(ViewModel.KeyDialog));
        _webhook.Sync(this, _context, ViewModel.WebhookDialog.Dialog, () => new WebhookEditorContent(ViewModel.WebhookDialog));
        _removal.Sync(this, _context, ViewModel.Removal, RemovalBody);
    }

    /// <summary>Closes every dialog without asking the core: the page left the screen.</summary>
    internal void CloseDialogs()
    {
        _key.Close();
        _webhook.Close();
        _removal.Close();
    }

    /// <summary>What removing does, under the question that names what goes.</summary>
    private UIElement RemovalBody()
    {
        var body = new TextBlock { Text = ViewModel.Removal.Body, TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetAutomationId(body, "DeveloperRemovalBody");
        return body;
    }

    // An ItemsRepeater whose template uses x:Bind gives what it makes no
    // DataContext, so each row's button carries its row's id in Tag.
    private static string? IdOf(object sender) => (sender as FrameworkElement)?.Tag as string;

    private void OnRevokeKeyClick(object sender, RoutedEventArgs e)
    {
        if (IdOf(sender) is { } id && ViewModel.Keys.FirstOrDefault(key => key.Id == id) is { } key)
        {
            ViewModel.AskRevokeKey(key);
        }
    }

    private void OnRevokeAppClick(object sender, RoutedEventArgs e)
    {
        if (IdOf(sender) is { } id && ViewModel.Apps.FirstOrDefault(app => app.Id == id) is { } app)
        {
            ViewModel.AskRevokeApp(app);
        }
    }

    private WebhookEntry? WebhookOf(object sender) =>
        IdOf(sender) is { } id ? ViewModel.Webhooks.FirstOrDefault(webhook => webhook.Id == id) : null;

    private void OnDeliveriesClick(object sender, RoutedEventArgs e)
    {
        if (WebhookOf(sender) is { } webhook)
        {
            ViewModel.ToggleDeliveries(webhook);
        }
    }

    private void OnEditWebhookClick(object sender, RoutedEventArgs e)
    {
        if (WebhookOf(sender) is { } webhook)
        {
            ViewModel.EditWebhook(webhook);
        }
    }

    private void OnDeleteWebhookClick(object sender, RoutedEventArgs e)
    {
        if (WebhookOf(sender) is { } webhook)
        {
            ViewModel.AskDeleteWebhook(webhook);
        }
    }
}
