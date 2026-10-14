using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DistrictAI.Core.Ffi;

namespace DistrictAI.ViewModels.Scheduling;

/// <summary>
/// Developer (the core's <see cref="SchedulingDeveloperView"/>): a tab each
/// for the API keys, the connected apps beside the MCP address, and the
/// webhooks with their deliveries. Each tab is read the first time it shows
/// and its own Try again reads it alone. Making a key, adding or changing a
/// webhook, and revoking or deleting are offered only to a member who may
/// change things; every removal is asked once more, its confirm never the
/// default.
/// </summary>
/// <remarks>
/// A key and a webhook's signing secret are shown once, in the dialog that
/// made them (<see cref="DeveloperKeyViewModel"/>,
/// <see cref="WebhookEditorViewModel"/>); no row here carries either.
/// </remarks>
public sealed partial class SchedulingDeveloperViewModel : ObservableObject
{
    private DeveloperTabView[] _tabs = [];
    private string _openUrl = string.Empty;
    private PageContext? _context;

    /// <summary>Developer, with nothing shown yet.</summary>
    public SchedulingDeveloperViewModel()
    {
        Removal = new AdminDialogViewModel(
            () => Event(new DeveloperAction.ConfirmRemoval()),
            () => Event(new DeveloperAction.CancelRemoval()));
        var refresh = () => Event(new DeveloperAction.Refresh());
        KeysLoad = new TabLoadViewModel(refresh);
        AppsLoad = new TabLoadViewModel(refresh);
        WebhooksLoad = new TabLoadViewModel(refresh);
        DeliveriesLoad = new TabLoadViewModel(null);
    }

    /// <summary>A removal, while it is asked about: its confirm is destructive.</summary>
    public AdminDialogViewModel Removal { get; }

    /// <summary>Making a key.</summary>
    public DeveloperKeyViewModel KeyDialog { get; } = new();

    /// <summary>Adding or changing a webhook.</summary>
    public WebhookEditorViewModel WebhookDialog { get; } = new();

    /// <summary>The keys' read.</summary>
    public TabLoadViewModel KeysLoad { get; }

    /// <summary>The apps' read.</summary>
    public TabLoadViewModel AppsLoad { get; }

    /// <summary>The webhooks' read.</summary>
    public TabLoadViewModel WebhooksLoad { get; }

    /// <summary>The open webhook's deliveries' read.</summary>
    public TabLoadViewModel DeliveriesLoad { get; }

    /// <summary>The tabs.</summary>
    public ObservableCollection<DeveloperTabEntry> Tabs { get; } = [];

    /// <summary>The keys.</summary>
    public ObservableCollection<ApiKeyEntry> Keys { get; } = [];

    /// <summary>The connected apps.</summary>
    public ObservableCollection<ConnectedAppEntry> Apps { get; } = [];

    /// <summary>The webhooks.</summary>
    public ObservableCollection<WebhookEntry> Webhooks { get; } = [];

    /// <summary>The open webhook's deliveries.</summary>
    public ObservableCollection<DeliveryEntry> Deliveries { get; } = [];

    /// <summary>"Times in America/Toronto".</summary>
    [ObservableProperty]
    public partial string TimesIn { get; set; } = string.Empty;

    /// <summary>Which of <see cref="Tabs"/> is shown, or -1.</summary>
    [ObservableProperty]
    public partial int SelectedTab { get; set; } = -1;

    /// <summary>Whether the keys show.</summary>
    [ObservableProperty]
    public partial bool ShowsKeys { get; set; }

    /// <summary>Whether the apps show.</summary>
    [ObservableProperty]
    public partial bool ShowsApps { get; set; }

    /// <summary>Whether the webhooks show.</summary>
    [ObservableProperty]
    public partial bool ShowsWebhooks { get; set; }

    /// <summary>Whether Create key and Add webhook are offered.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenCreateKeyCommand), nameof(OpenCreateWebhookCommand))]
    public partial bool OffersWrites { get; set; }

    /// <summary>The keys' heading.</summary>
    [ObservableProperty]
    public partial string KeysHeading { get; set; } = string.Empty;

    /// <summary>What a key is for.</summary>
    [ObservableProperty]
    public partial string KeysSubtitle { get; set; } = string.Empty;

    /// <summary>Said when there is no key.</summary>
    [ObservableProperty]
    public partial string KeysEmpty { get; set; } = string.Empty;

    /// <summary>Whether the keys are read and there are none.</summary>
    [ObservableProperty]
    public partial bool ShowKeysEmpty { get; set; }

    /// <summary>"Create key".</summary>
    [ObservableProperty]
    public partial string CreateKeyLabel { get; set; } = string.Empty;

    /// <summary>Whether Create key shows: offered, over keys that were read.</summary>
    [ObservableProperty]
    public partial bool ShowsCreateKey { get; set; }

    /// <summary>The MCP card's heading.</summary>
    [ObservableProperty]
    public partial string McpHeading { get; set; } = string.Empty;

    /// <summary>Whether the scheduler has a public address.</summary>
    [ObservableProperty]
    public partial bool HasMcp { get; set; }

    /// <summary>Whether it has none.</summary>
    [ObservableProperty]
    public partial bool NoMcp { get; set; }

    /// <summary>Said while it has none.</summary>
    [ObservableProperty]
    public partial string McpNoHost { get; set; } = string.Empty;

    /// <summary>"MCP URL".</summary>
    [ObservableProperty]
    public partial string McpUrlLabel { get; set; } = string.Empty;

    /// <summary>The MCP address.</summary>
    [ObservableProperty]
    public partial string McpUrl { get; set; } = string.Empty;

    /// <summary>"Authorization header".</summary>
    [ObservableProperty]
    public partial string McpAuthLabel { get; set; } = string.Empty;

    /// <summary>The header's value, a placeholder where the key goes.</summary>
    [ObservableProperty]
    public partial string McpAuth { get; set; } = string.Empty;

    /// <summary>What the address is for.</summary>
    [ObservableProperty]
    public partial string McpNote { get; set; } = string.Empty;

    /// <summary>The apps' heading.</summary>
    [ObservableProperty]
    public partial string AppsHeading { get; set; } = string.Empty;

    /// <summary>What a connected app is.</summary>
    [ObservableProperty]
    public partial string AppsSubtitle { get; set; } = string.Empty;

    /// <summary>Said when there is no app.</summary>
    [ObservableProperty]
    public partial string AppsEmpty { get; set; } = string.Empty;

    /// <summary>Whether the apps are read and there are none.</summary>
    [ObservableProperty]
    public partial bool ShowAppsEmpty { get; set; }

    /// <summary>The webhooks' heading.</summary>
    [ObservableProperty]
    public partial string WebhooksHeading { get; set; } = string.Empty;

    /// <summary>What a webhook does.</summary>
    [ObservableProperty]
    public partial string WebhooksSubtitle { get; set; } = string.Empty;

    /// <summary>Said when there is no webhook.</summary>
    [ObservableProperty]
    public partial string WebhooksEmpty { get; set; } = string.Empty;

    /// <summary>Whether the webhooks are read and there are none.</summary>
    [ObservableProperty]
    public partial bool ShowWebhooksEmpty { get; set; }

    /// <summary>"Add webhook".</summary>
    [ObservableProperty]
    public partial string AddWebhookLabel { get; set; } = string.Empty;

    /// <summary>Whether Add webhook shows: offered, over webhooks that were read.</summary>
    [ObservableProperty]
    public partial bool ShowsAddWebhook { get; set; }

    /// <summary>Whether a webhook's deliveries are open.</summary>
    [ObservableProperty]
    public partial bool HasDeliveries { get; set; }

    /// <summary>"Deliveries: https://…", over them.</summary>
    [ObservableProperty]
    public partial string DeliveriesHeading { get; set; } = string.Empty;

    /// <summary>Said when there is none.</summary>
    [ObservableProperty]
    public partial string DeliveriesEmpty { get; set; } = string.Empty;

    /// <summary>Whether they are read and there is none.</summary>
    [ObservableProperty]
    public partial bool ShowDeliveriesEmpty { get; set; }

    /// <summary>Whether a removal is on its way.</summary>
    [ObservableProperty]
    public partial bool Removing { get; set; }

    /// <summary>Why the last removal failed, or empty.</summary>
    [ObservableProperty]
    public partial string RemovalFailure { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="RemovalFailure"/>.</summary>
    [ObservableProperty]
    public partial bool HasRemovalFailure { get; set; }

    internal void Attach(PageContext context)
    {
        _context = context;
        KeysLoad.Attach(context);
        AppsLoad.Attach(context);
        WebhooksLoad.Attach(context);
        Removal.Attach(context);
        KeyDialog.Attach(context);
        WebhookDialog.Attach(context);
    }

    internal void Show(SchedulingDeveloperView view)
    {
        TimesIn = view.TimesIn;
        _tabs = [.. view.Tabs.Select(entry => entry.Tab)];
        Display.Sync(Tabs, [.. view.Tabs.Select(entry => new DeveloperTabEntry(entry.Tab, entry.Label))]);
        SelectedTab = Array.FindIndex(view.Tabs, entry => entry.Selected);
        ShowsKeys = view.Tab == DeveloperTabView.Keys;
        ShowsApps = view.Tab == DeveloperTabView.Apps;
        ShowsWebhooks = view.Tab == DeveloperTabView.Webhooks;
        OffersWrites = view.OffersWrites;
        Removing = view.Removing;
        var canRemove = view.OffersWrites && !view.Removing;

        KeysHeading = view.KeysHeading;
        KeysSubtitle = view.KeysSubtitle;
        KeysEmpty = view.KeysEmpty;
        KeysLoad.Show(view.KeysStatus);
        Display.Sync(Keys, [.. view.Keys.Select(key => new ApiKeyEntry(
            key.Id, key.Name, key.Line, view.OffersWrites, canRemove, view.RevokeKeyLabel))]);
        ShowKeysEmpty = KeysLoad.Ready && view.Keys.Length == 0;
        CreateKeyLabel = view.CreateKeyLabel;
        ShowsCreateKey = view.OffersWrites && KeysLoad.Ready;

        McpHeading = view.McpHeading;
        HasMcp = view.Mcp is not null;
        NoMcp = view.Mcp is null;
        McpNoHost = view.McpNoHost;
        McpUrlLabel = view.Mcp?.UrlLabel ?? string.Empty;
        McpUrl = view.Mcp?.Url ?? string.Empty;
        McpAuthLabel = view.Mcp?.AuthLabel ?? string.Empty;
        McpAuth = view.Mcp?.Auth ?? string.Empty;
        McpNote = view.McpNote;
        AppsHeading = view.AppsHeading;
        AppsSubtitle = view.AppsSubtitle;
        AppsEmpty = view.AppsEmpty;
        AppsLoad.Show(view.AppsStatus);
        Display.Sync(Apps, [.. view.Apps.Select(app => new ConnectedAppEntry(
            app.Id, app.Name, app.LastUsed, view.OffersWrites, canRemove, view.RevokeAppLabel))]);
        ShowAppsEmpty = AppsLoad.Ready && view.Apps.Length == 0;

        WebhooksHeading = view.WebhooksHeading;
        WebhooksSubtitle = view.WebhooksSubtitle;
        WebhooksEmpty = view.WebhooksEmpty;
        WebhooksLoad.Show(view.WebhooksStatus);
        Display.Sync(Webhooks, [.. view.Webhooks.Select(hook => new WebhookEntry(
            hook.Id,
            hook.Url,
            hook.Events,
            hook.StateLabel,
            hook.Tone,
            hook.Open,
            view.OffersWrites,
            canRemove,
            view.DeliveriesLabel,
            view.EditWebhookLabel,
            view.DeleteWebhookLabel))]);
        ShowWebhooksEmpty = WebhooksLoad.Ready && view.Webhooks.Length == 0;
        AddWebhookLabel = view.AddWebhookLabel;
        ShowsAddWebhook = view.OffersWrites && WebhooksLoad.Ready;

        var deliveries = view.Deliveries;
        _openUrl = deliveries is null
            ? string.Empty
            : Array.Find(view.Webhooks, hook => hook.Id == deliveries.WebhookId)?.Url ?? string.Empty;
        HasDeliveries = deliveries is not null;
        DeliveriesHeading = deliveries is null ? string.Empty : view.DeliveriesLabel + ": " + _openUrl;
        DeliveriesLoad.Show(deliveries?.Status ?? new LoadStatus.Ready());
        Display.Sync(Deliveries, [.. (deliveries?.Rows ?? []).Select(DeliveryEntry.From)]);
        DeliveriesEmpty = deliveries?.Empty ?? string.Empty;
        ShowDeliveriesEmpty = deliveries is not null && DeliveriesLoad.Ready && deliveries.Rows.Length == 0;

        RemovalFailure = Display.Failure(view.RemovalFailure);
        HasRemovalFailure = view.RemovalFailure is not null;
        Removal.Show(view.Removal);
        KeyDialog.Show(view.KeyDialog);
        WebhookDialog.Show(view.WebhookDialog);
    }

    /// <summary>Closes its dialogs: Developer is no longer showing. A key or a secret shown goes with them.</summary>
    internal void Leave()
    {
        Removal.Show(null);
        KeyDialog.Show(null);
        WebhookDialog.Show(null);
    }

    /// <summary>Shows the tab at <paramref name="index"/> of <see cref="Tabs"/>; the one shown sends nothing.</summary>
    internal void SelectTab(int index)
    {
        if (index < 0 || index >= _tabs.Length || index == SelectedTab)
        {
            return;
        }
        SelectedTab = index;
        Send(new DeveloperAction.SelectTab(_tabs[index]));
    }

    /// <summary>Asks about revoking <paramref name="key"/>; nothing while a removal is on its way.</summary>
    internal void AskRevokeKey(ApiKeyEntry key)
    {
        if (key.CanRevoke)
        {
            Send(new DeveloperAction.AskRevokeKey(key.Id));
        }
    }

    /// <summary>Asks about revoking <paramref name="app"/>'s access; nothing while a removal is on its way.</summary>
    internal void AskRevokeApp(ConnectedAppEntry app)
    {
        if (app.CanRevoke)
        {
            Send(new DeveloperAction.AskRevokeApp(app.Id));
        }
    }

    /// <summary>Opens <paramref name="webhook"/>'s deliveries, or closes them.</summary>
    internal void ToggleDeliveries(WebhookEntry webhook) => Send(new DeveloperAction.ToggleDeliveries(webhook.Id));

    /// <summary>Opens <paramref name="webhook"/>'s form; nothing when it is not offered.</summary>
    internal void EditWebhook(WebhookEntry webhook)
    {
        if (webhook.OffersWrites)
        {
            Send(new DeveloperAction.OpenEditWebhook(webhook.Id));
        }
    }

    /// <summary>Asks about deleting <paramref name="webhook"/>; nothing while a removal is on its way.</summary>
    internal void AskDeleteWebhook(WebhookEntry webhook)
    {
        if (webhook.CanDelete)
        {
            Send(new DeveloperAction.AskDeleteWebhook(webhook.Id));
        }
    }

    [RelayCommand(CanExecute = nameof(OffersWrites))]
    private void OpenCreateKey() => Send(new DeveloperAction.OpenCreateKey());

    [RelayCommand(CanExecute = nameof(OffersWrites))]
    private void OpenCreateWebhook() => Send(new DeveloperAction.OpenCreateWebhook());

    private void Send(DeveloperAction action) => _context?.Send(Event(action));

    /// <summary>The event for <paramref name="action"/>.</summary>
    internal static UiEvent Event(DeveloperAction action) =>
        new UiEvent.SchedulingAdmin(new SchedulingAdminAction.Developer(action));
}

/// <summary>
/// Where one tab's read stands, a list's own: loading, a read that failed
/// with its own Try again (which reads that tab alone), or read.
/// </summary>
public sealed partial class TabLoadViewModel : ObservableObject
{
    private readonly Func<UiEvent>? _retry;
    private PageContext? _context;

    /// <summary>A read whose Try again sends <paramref name="retry"/>, or offers none for null.</summary>
    internal TabLoadViewModel(Func<UiEvent>? retry) => _retry = retry;

    /// <summary>Whether it is being read.</summary>
    [ObservableProperty]
    public partial bool Loading { get; set; }

    /// <summary>Whether its read failed.</summary>
    [ObservableProperty]
    public partial bool Failed { get; set; }

    /// <summary>Why its read failed.</summary>
    [ObservableProperty]
    public partial string FailureMessage { get; set; } = string.Empty;

    /// <summary>Whether Try again is offered.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RetryCommand))]
    public partial bool CanRetry { get; set; }

    /// <summary>Whether it is read.</summary>
    [ObservableProperty]
    public partial bool Ready { get; set; }

    internal void Attach(PageContext context) => _context = context;

    internal void Show(LoadStatus status)
    {
        var failure = (status as LoadStatus.Failed)?.Failure;
        Loading = status is LoadStatus.Loading;
        Failed = failure is not null;
        FailureMessage = Display.Failure(failure);
        CanRetry = _retry is not null && (failure?.Retryable ?? false);
        Ready = status is LoadStatus.Ready;
    }

    [RelayCommand(CanExecute = nameof(CanRetry))]
    private void Retry()
    {
        if (_retry is not null)
        {
            _context?.Send(_retry());
        }
    }
}
