using System.Text;
using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels.Scheduling;
using Xunit;

namespace DistrictAI.Presentation.Tests.Scheduling;

public sealed class SchedulingDeveloperViewModelTests
{
    private static readonly SchedulingSectionEntryView[] _sections =
    [
        new(SchedulingAdminSection.Overview, "Overview", "Your booking desk", false),
        new(SchedulingAdminSection.Team, "Team", "Who takes bookings", false),
        new(SchedulingAdminSection.Developer, "Developer", "API keys, connected apps and webhooks", true),
    ];

    private static readonly LoadStatus _failed =
        new LoadStatus.Failed(new FailureView("Offline.", null, true), "Could not load this section");

    private static readonly FailureView _refused = new("The booking system did not answer. Try again in a minute.", null, true);

    /// <summary>A key as a create answers it: made here, so no test holds one in its source.</summary>
    private static readonly string _key = "cno_" + Guid.NewGuid().ToString("N");

    /// <summary>A signing secret as an add answers it, made the same way.</summary>
    private static readonly string _secret = "whsec_" + Guid.NewGuid().ToString("N");

    private static DialogView Dialog(
        string title,
        string confirm,
        string close = "Cancel",
        bool canConfirm = true,
        bool working = false,
        bool destructive = false,
        FailureView? failure = null,
        string body = "") =>
        new(title, body, confirm, close, canConfirm, working, destructive, failure);

    private static SecretView Shown(string value, string copy = "Copy key", string? note = null) =>
        new("Copy it now. It is not shown again.", note, copy, Encoding.UTF8.GetBytes(value));

    private static KeyDialogView Key(
        string name = "",
        string? nameError = null,
        bool working = false,
        bool minted = false,
        string copy = "Copy key",
        FailureView? failure = null) =>
        new(
            Dialog: minted
                ? Dialog("Your key for Zapier", string.Empty, close: "Done", canConfirm: false)
                : Dialog("Create key", working ? "Creating…" : "Create key", canConfirm: name.Length > 0 && !working, working: working, failure: failure),
            NameLabel: "Name",
            NameHint: "So you know which tool uses it.",
            Name: name,
            NameError: nameError,
            Editable: !working,
            Minted: minted ? Shown(_key, copy) : null);

    private static readonly string[] _events = ["booking.created", "booking.cancelled", "booking.rescheduled"];

    private static readonly string[] _tabLabels = ["API keys", "Connected apps", "Webhooks"];

    private static ToggleRowView[] Events(params string[] on) =>
        [.. _events.Select(value => new ToggleRowView(value, on.Contains(value), null))];

    private static ToggleRowView[] Fields() =>
    [
        new("id", true, null),
        new("attendee_email", false, "Personal data"),
    ];

    private static WebhookEditorView Webhook(
        bool creating = true,
        string url = "",
        string? urlError = null,
        string[]? on = null,
        bool working = false,
        string? notice = null,
        bool minted = false,
        string? dropped = null,
        FailureView? failure = null) =>
        new(
            Dialog: minted
                ? Dialog("Your signing secret", string.Empty, close: "Done", canConfirm: false)
                : Dialog(
                    creating ? "Add webhook" : "Edit webhook",
                    working ? "Saving…" : creating ? "Add webhook" : "Save changes",
                    canConfirm: (on?.Length ?? 0) > 0 && !working,
                    working: working,
                    failure: failure),
            Creating: creating,
            UrlLabel: "Endpoint URL",
            UrlHint: creating ? "Where each delivery is posted. https only." : "The URL cannot be changed.",
            Url: url,
            UrlError: urlError,
            Dropped: dropped,
            EventsLabel: "Events",
            EventsHint: "What it is sent for.",
            Events: Events(on ?? []),
            EventsError: null,
            FieldsLabel: "Fields",
            FieldsHint: "What each delivery carries.",
            Fields: Fields(),
            Editable: !working,
            Notice: notice,
            Minted: minted ? Shown(_secret, "Copy signing secret", "Every delivery to https://hooks.example.com/in is signed with it.") : null);

    private static SchedulingDeveloperView Developer(
        DeveloperTabView tab = DeveloperTabView.Keys,
        bool viewer = false,
        LoadStatus? keysStatus = null,
        bool noKeys = false,
        LoadStatus? appsStatus = null,
        bool hasHost = true,
        LoadStatus? webhooksStatus = null,
        string? openWebhook = null,
        LoadStatus? deliveriesStatus = null,
        bool removing = false,
        FailureView? removalFailure = null,
        DialogView? removal = null,
        KeyDialogView? key = null,
        WebhookEditorView? webhook = null)
    {
        var keysRead = keysStatus is null;
        var appsRead = appsStatus is null;
        var webhooksRead = webhooksStatus is null;
        return new(
            TimesIn: "Times in America/Toronto",
            Tab: tab,
            Tabs:
            [
                new(DeveloperTabView.Keys, "API keys", tab == DeveloperTabView.Keys),
                new(DeveloperTabView.Apps, "Connected apps", tab == DeveloperTabView.Apps),
                new(DeveloperTabView.Webhooks, "Webhooks", tab == DeveloperTabView.Webhooks),
            ],
            OffersWrites: !viewer,
            KeysHeading: "Your API keys",
            KeysSubtitle: "Let your own tools reach your bookings.",
            KeysStatus: keysStatus ?? new LoadStatus.Ready(),
            Keys: keysRead && !noKeys
                ? [new("key-1", "Zapier", "Created Sep 1 · Last used Sep 10"), new("key-2", "Unused", "Created Sep 2 · Last used Never")]
                : [],
            KeysEmpty: "No API keys yet.",
            CreateKeyLabel: "Create key",
            RevokeKeyLabel: "Revoke key",
            McpHeading: "Connect an AI assistant",
            Mcp: hasHost ? new McpView("MCP URL", "https://book.example.com/mcp", "Authorization header", "Bearer <your API key>") : null,
            McpNoHost: "Your booking pages have no public address yet.",
            McpNote: "Use one of your API keys.",
            AppsHeading: "Connected apps",
            AppsSubtitle: "Apps you let reach your bookings.",
            AppsStatus: appsStatus ?? new LoadStatus.Ready(),
            Apps: appsRead ? [new("app-1", "Raycast", "Sep 10"), new("app-2", "Internal script", "Never")] : [],
            AppsEmpty: "No apps connected.",
            RevokeAppLabel: "Revoke access",
            WebhooksHeading: "Webhooks",
            WebhooksSubtitle: "Booking changes posted to your own address.",
            WebhooksStatus: webhooksStatus ?? new LoadStatus.Ready(),
            Webhooks: webhooksRead
                ?
                [
                    new("wh-1", "https://hooks.example.com/bookings", "booking.created, booking.cancelled", "On", RunTone.Success, openWebhook == "wh-1"),
                    new("wh-2", "https://hooks.example.com/notes", "notes.ready", "Off", RunTone.Neutral, openWebhook == "wh-2"),
                ]
                : [],
            WebhooksEmpty: "No webhooks yet.",
            AddWebhookLabel: "Add webhook",
            EditWebhookLabel: "Edit",
            DeleteWebhookLabel: "Delete webhook",
            DeliveriesLabel: "Deliveries",
            Deliveries: openWebhook is null
                ? null
                : new DeliveriesView(
                    openWebhook,
                    deliveriesStatus ?? new LoadStatus.Ready(),
                    deliveriesStatus is null
                        ?
                        [
                            new("d-1", "Sep 10, 9:00 a.m.", "booking.created", "Delivered", RunTone.Success),
                            new("d-2", "Sep 10, 9:30 a.m.", "booking.cancelled", "No answer", RunTone.Danger),
                            new("d-3", "Not tried yet", "booking.created", "Waiting", RunTone.Neutral),
                        ]
                        : [],
                    "Nothing delivered yet."),
            Removing: removing,
            RemovalFailure: removalFailure,
            Removal: removal,
            KeyDialog: key,
            WebhookDialog: webhook);
    }

    private static DialogView Revoke(string name = "Zapier") =>
        Dialog("Revoke " + name + "?", "Revoke key", destructive: true, body: "Anything using it stops working now.");

    private static SchedulingAdminView Page(SchedulingDeveloperView view) =>
        new("Developer", _sections, new SchedulingAdminPage.Developer(view));

    private static (SchedulingAdminViewModel Model, RecordingSink Sink) Attached()
    {
        var model = new SchedulingAdminViewModel();
        var (context, sink) = Pages.Context();
        model.Attach(context);
        return (model, sink);
    }

    /// <summary>The developer action <paramref name="sent"/> carries.</summary>
    private static DeveloperAction Action(UiEvent sent) =>
        Assert.IsType<SchedulingAdminAction.Developer>(Assert.IsType<UiEvent.SchedulingAdmin>(sent).Action).Action;

    [Fact]
    public void TheKeysShowAndARevokeIsAskedOnce()
    {
        var (model, sink) = Attached();
        model.Show(Page(Developer()));
        var vm = model.Developer;
        Assert.True(model.ShowsDeveloper);
        Assert.False(model.ShowsTeam || model.ShowsCalendar || model.ShowsOverview);
        Assert.Equal(2, model.SelectedSection);
        Assert.Equal("Times in America/Toronto", vm.TimesIn);
        Assert.Equal(_tabLabels, vm.Tabs.Select(tab => tab.ToString()));
        Assert.Equal("DeveloperTabWebhooks", vm.Tabs[2].AutomationId);
        Assert.Equal(0, vm.SelectedTab);
        Assert.True(vm.ShowsKeys);
        Assert.False(vm.ShowsApps || vm.ShowsWebhooks);
        Assert.True(vm.KeysLoad.Ready);
        Assert.False(vm.KeysLoad.Loading || vm.KeysLoad.Failed || vm.ShowKeysEmpty);
        Assert.Equal(("Your API keys", "Create key"), (vm.KeysHeading, vm.CreateKeyLabel));
        Assert.True(vm.ShowsCreateKey && vm.OffersWrites);
        Assert.Equal(2, vm.Keys.Count);
        var zapier = vm.Keys[0];
        Assert.Equal("Zapier, Created Sep 1 · Last used Sep 10", zapier.ToString());
        Assert.Equal("DeveloperKeykey-1", zapier.AutomationId);
        Assert.Equal("Revoke key: Zapier", zapier.RevokeName);
        Assert.True(zapier.OffersRevoke && zapier.CanRevoke);
        Assert.False(vm.Removal.IsOpen || vm.KeyDialog.Dialog.IsOpen || vm.WebhookDialog.Dialog.IsOpen);

        vm.OpenCreateKeyCommand.Execute(null);
        vm.AskRevokeKey(zapier);
        vm.SelectTab(0);
        vm.SelectTab(9);
        Assert.Equal(
            new DeveloperAction[] { new DeveloperAction.OpenCreateKey(), new DeveloperAction.AskRevokeKey("key-1") },
            sink.Sent.Select(Action).ToArray());

        // Asked once more, its confirm destructive: the way out keeps it, the confirm is pressed once.
        model.Show(Page(Developer(removal: Revoke())));
        Assert.True(vm.Removal.IsOpen);
        Assert.Equal("Revoke Zapier?", vm.Removal.Title);
        Assert.Equal("Anything using it stops working now.", vm.Removal.Body);
        Assert.Equal(("Revoke key", "Cancel"), (vm.Removal.ConfirmText, vm.Removal.CloseText));
        Assert.True(vm.Removal.Destructive);
        vm.Removal.CloseCommand.Execute(null);
        vm.Removal.ConfirmCommand.Execute(null);
        Assert.False(vm.Removal.CanConfirm);
        Assert.Equal(
            new DeveloperAction[] { new DeveloperAction.CancelRemoval(), new DeveloperAction.ConfirmRemoval() },
            sink.Sent.Skip(2).Select(Action).ToArray());

        // On its way: every revoke waits.
        model.Show(Page(Developer(removing: true)));
        Assert.True(vm.Removing);
        Assert.False(vm.Removal.IsOpen);
        Assert.False(vm.Keys[0].CanRevoke);
        var before = sink.Sent.Count;
        vm.AskRevokeKey(vm.Keys[0]);
        Assert.Equal(before, sink.Sent.Count);
        model.Show(Page(Developer(removalFailure: _refused)));
        Assert.True(vm.HasRemovalFailure);
        Assert.Equal("The booking system did not answer. Try again in a minute.", vm.RemovalFailure);

        // A viewer reads the same and is offered none of the writes.
        model.Show(Page(Developer(viewer: true)));
        Assert.False(vm.OffersWrites || vm.ShowsCreateKey);
        Assert.False(vm.Keys[0].OffersRevoke || vm.Keys[0].CanRevoke);
        Assert.False(vm.OpenCreateKeyCommand.CanExecute(null));
    }

    [Fact]
    public void ATabThatFailedIsReadAgainAlone()
    {
        var (model, sink) = Attached();
        var vm = model.Developer;
        model.Show(Page(Developer(keysStatus: new LoadStatus.Loading())));
        Assert.True(vm.KeysLoad.Loading);
        Assert.False(vm.ShowsCreateKey || vm.ShowKeysEmpty);
        model.Show(Page(Developer(keysStatus: _failed)));
        Assert.True(vm.KeysLoad.Failed);
        Assert.Equal("Offline.", vm.KeysLoad.FailureMessage);
        Assert.True(vm.KeysLoad.RetryCommand.CanExecute(null));
        vm.KeysLoad.RetryCommand.Execute(null);
        Assert.Equal(new DeveloperAction.Refresh(), Action(Assert.Single(sink.Sent)));
        model.Show(Page(Developer(noKeys: true)));
        Assert.True(vm.ShowKeysEmpty);
        Assert.Equal("No API keys yet.", vm.KeysEmpty);
    }

    [Fact]
    public void TheAppsShowBesideTheMcpAddress()
    {
        var (model, sink) = Attached();
        model.Show(Page(Developer()));
        var vm = model.Developer;
        vm.SelectTab(1);
        Assert.Equal(new DeveloperAction.SelectTab(DeveloperTabView.Apps), Action(Assert.Single(sink.Sent)));
        model.Show(Page(Developer(tab: DeveloperTabView.Apps)));
        Assert.True(vm.ShowsApps);
        Assert.Equal(1, vm.SelectedTab);
        Assert.True(vm.HasMcp);
        Assert.False(vm.NoMcp);
        Assert.Equal(("MCP URL", "https://book.example.com/mcp"), (vm.McpUrlLabel, vm.McpUrl));
        Assert.Equal(("Authorization header", "Bearer <your API key>"), (vm.McpAuthLabel, vm.McpAuth));
        Assert.Equal(("Connect an AI assistant", "Use one of your API keys."), (vm.McpHeading, vm.McpNote));
        Assert.Equal(2, vm.Apps.Count);
        var raycast = vm.Apps[0];
        Assert.Equal("Raycast, Sep 10", raycast.ToString());
        Assert.Equal("Revoke access: Raycast", raycast.RevokeName);
        Assert.Equal("DeveloperAppapp-1", raycast.AutomationId);
        vm.AskRevokeApp(raycast);
        Assert.Equal(new DeveloperAction.AskRevokeApp("app-1"), Action(sink.Sent[^1]));

        model.Show(Page(Developer(tab: DeveloperTabView.Apps, hasHost: false, appsStatus: _failed)));
        Assert.False(vm.HasMcp);
        Assert.True(vm.NoMcp);
        Assert.Equal("Your booking pages have no public address yet.", vm.McpNoHost);
        Assert.True(vm.AppsLoad.Failed);
        Assert.Empty(vm.Apps);
        Assert.False(vm.ShowAppsEmpty);
        model.Show(Page(Developer(tab: DeveloperTabView.Apps, removing: true)));
        var before = sink.Sent.Count;
        vm.AskRevokeApp(vm.Apps[0]);
        Assert.Equal(before, sink.Sent.Count);
    }

    [Fact]
    public void AWebhooksDeliveriesOpenAndItsRowsAct()
    {
        var (model, sink) = Attached();
        model.Show(Page(Developer(tab: DeveloperTabView.Webhooks)));
        var vm = model.Developer;
        Assert.True(vm.ShowsWebhooks);
        Assert.True(vm.ShowsAddWebhook);
        Assert.False(vm.HasDeliveries);
        Assert.Equal(2, vm.Webhooks.Count);
        var bookings = vm.Webhooks[0];
        Assert.Equal("https://hooks.example.com/bookings, On, booking.created, booking.cancelled", bookings.ToString());
        Assert.Equal("DeveloperWebhookwh-1", bookings.AutomationId);
        Assert.True(bookings.IsSuccess);
        Assert.True(vm.Webhooks[1].IsNeutral);
        Assert.Equal(
            ("Deliveries: https://hooks.example.com/bookings", "Edit: https://hooks.example.com/bookings", "Delete webhook: https://hooks.example.com/bookings"),
            (bookings.DeliveriesName, bookings.EditName, bookings.DeleteName));
        vm.ToggleDeliveries(bookings);
        vm.EditWebhook(bookings);
        vm.AskDeleteWebhook(vm.Webhooks[1]);
        vm.OpenCreateWebhookCommand.Execute(null);
        Assert.Equal(
            new DeveloperAction[]
            {
                new DeveloperAction.ToggleDeliveries("wh-1"),
                new DeveloperAction.OpenEditWebhook("wh-1"),
                new DeveloperAction.AskDeleteWebhook("wh-2"),
                new DeveloperAction.OpenCreateWebhook(),
            },
            sink.Sent.Select(Action).ToArray());

        model.Show(Page(Developer(tab: DeveloperTabView.Webhooks, openWebhook: "wh-1", deliveriesStatus: new LoadStatus.Loading())));
        Assert.True(vm.HasDeliveries);
        Assert.True(vm.Webhooks[0].Open);
        Assert.Equal("Deliveries: https://hooks.example.com/bookings", vm.DeliveriesHeading);
        Assert.True(vm.DeliveriesLoad.Loading);
        model.Show(Page(Developer(tab: DeveloperTabView.Webhooks, openWebhook: "wh-1")));
        Assert.True(vm.DeliveriesLoad.Ready);
        Assert.Equal(3, vm.Deliveries.Count);
        Assert.True(vm.Deliveries[0].IsSuccess);
        Assert.True(vm.Deliveries[1].IsDanger);
        Assert.True(vm.Deliveries[2].IsNeutral);
        Assert.Equal("Sep 10, 9:30 a.m., booking.cancelled, No answer", vm.Deliveries[1].ToString());
        Assert.Equal("DeveloperDeliveryd-2", vm.Deliveries[1].AutomationId);
        model.Show(Page(Developer(tab: DeveloperTabView.Webhooks, openWebhook: "wh-1", deliveriesStatus: _failed)));
        Assert.True(vm.DeliveriesLoad.Failed);
        Assert.Empty(vm.Deliveries);

        // Try again under the webhook reads its deliveries again.
        Assert.True(vm.DeliveriesLoad.CanRetry);
        vm.DeliveriesLoad.RetryCommand.Execute(null);
        Assert.Equal(new DeveloperAction.RetryDeliveries(), Action(sink.Sent[^1]));
        model.Show(Page(Developer(tab: DeveloperTabView.Webhooks, openWebhook: "wh-1", deliveriesStatus: new LoadStatus.Loading())));
        Assert.False(vm.DeliveriesLoad.CanRetry);
        Assert.True(vm.DeliveriesLoad.Loading);
        model.Show(Page(Developer(tab: DeveloperTabView.Webhooks)));
        Assert.False(vm.HasDeliveries);
        Assert.Empty(vm.Deliveries);

        // A viewer may open the deliveries, and nothing else.
        model.Show(Page(Developer(tab: DeveloperTabView.Webhooks, viewer: true)));
        var before = sink.Sent.Count;
        vm.EditWebhook(vm.Webhooks[0]);
        vm.AskDeleteWebhook(vm.Webhooks[0]);
        Assert.Equal(before, sink.Sent.Count);
        vm.ToggleDeliveries(vm.Webhooks[0]);
        Assert.Equal(before + 1, sink.Sent.Count);
        Assert.False(vm.ShowsAddWebhook);
    }

    [Fact]
    public void AKeyIsMadeAndShownOnce()
    {
        var (model, sink) = Attached();
        model.Show(Page(Developer(key: Key())));
        var vm = model.Developer.KeyDialog;
        Assert.True(vm.Dialog.IsOpen);
        Assert.Equal(("Create key", "Create key", "Cancel"), (vm.Dialog.Title, vm.Dialog.ConfirmText, vm.Dialog.CloseText));
        Assert.True(vm.ShowsForm && vm.Editable);
        Assert.False(vm.Shown.IsShown);
        Assert.Equal(("Name", "So you know which tool uses it."), (vm.NameLabel, vm.NameHint));
        Assert.Empty(sink.Sent);

        vm.Name = "Zapier";
        model.Show(Page(Developer(key: Key(name: "Zap"))));
        Assert.Equal("Zapier", vm.Name);
        vm.Dialog.ConfirmCommand.Execute(null);
        Assert.Equal(
            new DeveloperAction[] { new DeveloperAction.EditKeyName("Zapier"), new DeveloperAction.CreateKey() },
            sink.Sent.Select(Action).ToArray());
        model.Show(Page(Developer(key: Key(name: string.Empty, nameError: "Give the key a name."))));
        Assert.True(vm.HasNameError);
        Assert.Equal("Give the key a name.", vm.NameError);
        model.Show(Page(Developer(key: Key(name: "Zapier", working: true))));
        Assert.False(vm.Editable);
        Assert.True(vm.Dialog.Working);
        model.Show(Page(Developer(key: Key(name: "Zapier", failure: _refused))));
        Assert.True(vm.Dialog.HasFailure);

        // Made: the key shows, with nothing left to create; Done closes it.
        model.Show(Page(Developer(key: Key(minted: true))));
        Assert.False(vm.ShowsForm);
        Assert.True(vm.Shown.IsShown);
        Assert.Equal(_key, vm.Shown.Value);
        Assert.Equal("Copy it now. It is not shown again.", vm.Shown.Warning);
        Assert.Equal("Copy key", vm.Shown.CopyLabel);
        Assert.Equal(("Your key for Zapier", string.Empty, "Done"), (vm.Dialog.Title, vm.Dialog.ConfirmText, vm.Dialog.CloseText));
        Assert.False(vm.Dialog.CanConfirm);
        var before = sink.Sent.Count;
        vm.Copied(false);
        Assert.True(vm.Shown.CopyFailed);
        Assert.Equal(before, sink.Sent.Count);
        vm.Copied(true);
        Assert.False(vm.Shown.CopyFailed);
        Assert.Equal(new DeveloperAction.KeyCopied(), Action(sink.Sent[^1]));
        model.Show(Page(Developer(key: Key(minted: true, copy: "Copied"))));
        Assert.Equal("Copied", vm.Shown.CopyLabel);
        vm.Dialog.CloseCommand.Execute(null);
        Assert.Equal(new DeveloperAction.CloseDialog(), Action(sink.Sent[^1]));

        // Closed: the key goes.
        model.Show(Page(Developer()));
        Assert.False(vm.Dialog.IsOpen || vm.Shown.IsShown);
        Assert.Equal(string.Empty, vm.Shown.Value);
    }

    [Fact]
    public void AWebhookIsAddedAndChanged()
    {
        var (model, sink) = Attached();
        model.Show(Page(Developer(tab: DeveloperTabView.Webhooks, webhook: Webhook())));
        var vm = model.Developer.WebhookDialog;
        Assert.True(vm.Dialog.IsOpen);
        Assert.Equal(("Add webhook", "Add webhook"), (vm.Dialog.Title, vm.Dialog.ConfirmText));
        Assert.True(vm.Creating && vm.ShowsForm && vm.Editable);
        Assert.False(vm.Changing || vm.Shown.IsShown || vm.HasUrlError || vm.HasDropped || vm.HasNotice);
        Assert.Equal("Endpoint URL", vm.Words?.UrlLabel);
        Assert.Equal(3, vm.Events.Count);
        Assert.Equal("DeveloperWebhookEventbooking.created", vm.Events[0].EventAutomationId);
        Assert.False(vm.Events[0].On);
        Assert.Equal(2, vm.Fields.Count);
        Assert.Equal("attendee_email, Personal data", vm.Fields[1].ToString());
        Assert.True(vm.Fields[1].HasMark);
        Assert.False(vm.Fields[0].HasMark);
        Assert.Equal("DeveloperWebhookFieldattendee_email", vm.Fields[1].FieldAutomationId);
        Assert.Empty(sink.Sent);

        vm.Url = "https://hooks.example.com/in";
        vm.SetEvent("booking.created", false);
        vm.SetEvent("booking.created", true);
        vm.SetEvent("not.listed", true);
        vm.SetField("attendee_email", true);
        vm.SetField("id", true);
        Assert.Equal(
            new DeveloperAction[]
            {
                new DeveloperAction.EditWebhookUrl("https://hooks.example.com/in"),
                new DeveloperAction.ToggleEvent("booking.created"),
                new DeveloperAction.ToggleField("attendee_email"),
            },
            sink.Sent.Select(Action).ToArray());
        model.Show(Page(Developer(tab: DeveloperTabView.Webhooks, webhook: Webhook(url: "https://hooks", urlError: "The URL has to start with https://.", on: ["booking.created"]))));
        Assert.Equal("https://hooks.example.com/in", vm.Url);
        Assert.True(vm.HasUrlError);
        Assert.Equal("The URL has to start with https://.", vm.UrlError);
        Assert.True(vm.Events[0].On);
        vm.Dialog.ConfirmCommand.Execute(null);
        Assert.Equal(new DeveloperAction.SaveWebhook(), Action(sink.Sent[^1]));

        // Saving: nothing can change; then the secret, shown once.
        model.Show(Page(Developer(tab: DeveloperTabView.Webhooks, webhook: Webhook(on: ["booking.created"], working: true))));
        Assert.False(vm.Editable);
        Assert.False(vm.Events[0].Enabled);
        var before = sink.Sent.Count;
        vm.SetEvent("booking.cancelled", true);
        Assert.Equal(before, sink.Sent.Count);
        model.Show(Page(Developer(tab: DeveloperTabView.Webhooks, webhook: Webhook(minted: true))));
        Assert.False(vm.ShowsForm);
        Assert.True(vm.Shown.IsShown);
        Assert.Equal(_secret, vm.Shown.Value);
        Assert.True(vm.Shown.HasNote);
        Assert.Equal("Copy signing secret", vm.Shown.CopyLabel);
        Assert.Equal(("Your signing secret", "Done"), (vm.Dialog.Title, vm.Dialog.CloseText));
        vm.Copied(true);
        Assert.Equal(before, sink.Sent.Count);
        vm.Copied(false);
        Assert.True(vm.Shown.CopyFailed);
        model.Show(Page(Developer(tab: DeveloperTabView.Webhooks)));
        Assert.False(vm.Dialog.IsOpen || vm.Shown.IsShown || vm.Shown.CopyFailed);
        Assert.Equal(string.Empty, vm.Shown.Value);
        Assert.Empty(vm.Events);

        // Changing one: the address fixed and never sent; what it came to; a dropped event said.
        model.Show(Page(Developer(tab: DeveloperTabView.Webhooks, webhook: Webhook(creating: false, url: "https://hooks.example.com/bookings", on: ["booking.created"], dropped: "Saving will stop sending legacy.event."))));
        Assert.True(vm.Changing);
        Assert.False(vm.Creating);
        Assert.Equal("https://hooks.example.com/bookings", vm.Url);
        Assert.True(vm.HasDropped);
        Assert.Equal("Saving will stop sending legacy.event.", vm.Dropped);
        before = sink.Sent.Count;
        vm.Url = "https://elsewhere.example.com";
        Assert.Equal(before, sink.Sent.Count);
        model.Show(Page(Developer(tab: DeveloperTabView.Webhooks, webhook: Webhook(creating: false, url: "https://hooks.example.com/bookings", on: ["booking.created"], notice: "Webhook updated"))));
        Assert.True(vm.HasNotice);
        Assert.Equal("Webhook updated", vm.Notice);
        model.Show(Page(Developer(tab: DeveloperTabView.Webhooks, webhook: Webhook(creating: false, on: ["booking.created"], failure: _refused))));
        Assert.True(vm.Dialog.HasFailure);
    }

    [Fact]
    public void AnotherSectionClosesTheDialogsAndTheSecretGoes()
    {
        var (model, _) = Attached();
        model.Show(Page(Developer(removal: Revoke(), key: Key(minted: true), webhook: Webhook(minted: true))));
        Assert.True(model.Developer.Removal.IsOpen && model.Developer.KeyDialog.Dialog.IsOpen && model.Developer.WebhookDialog.Dialog.IsOpen);
        model.Show(new SchedulingAdminView("Overview", _sections, new SchedulingAdminPage.Overview(
            new SchedulingOverviewView("Times in UTC", "Your booking desk", [], "Coming up", new LoadStatus.Loading(), [], "Nothing coming up."))));
        Assert.False(model.ShowsDeveloper);
        Assert.False(model.Developer.Removal.IsOpen || model.Developer.KeyDialog.Dialog.IsOpen || model.Developer.WebhookDialog.Dialog.IsOpen);
        Assert.Equal(string.Empty, model.Developer.KeyDialog.Shown.Value);
        Assert.Equal(string.Empty, model.Developer.WebhookDialog.Shown.Value);
    }

    /// <summary>
    /// A key and a signing secret never print: not from the section's view
    /// models, nor from the views that carried them, nor from any event sent.
    /// </summary>
    [Fact]
    public void AKeyOrASecretNeverPrints()
    {
        var (model, sink) = Attached();
        var key = Key(minted: true);
        var webhook = Webhook(minted: true);
        var view = Developer(key: key, webhook: webhook);
        model.Show(Page(view));
        model.Developer.KeyDialog.Copied(true);
        Assert.Equal(_key, model.Developer.KeyDialog.Shown.Value);
        object[] printed =
        [
            model,
            model.Developer,
            model.Developer.KeyDialog,
            model.Developer.KeyDialog.Shown,
            model.Developer.WebhookDialog,
            model.Developer.WebhookDialog.Shown,
            key,
            key.Minted!,
            webhook,
            webhook.Minted!,
            view,
            Page(view),
            .. sink.Sent,
        ];
        foreach (var item in printed)
        {
            var text = item.ToString() ?? string.Empty;
            Assert.DoesNotContain(_key, text, StringComparison.Ordinal);
            Assert.DoesNotContain(_secret, text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void NothingIsSentBeforeAttaching()
    {
        var model = new SchedulingAdminViewModel();
        model.Show(Page(Developer(tab: DeveloperTabView.Webhooks, removal: Revoke(), key: Key(), webhook: Webhook())));
        var vm = model.Developer;
        vm.SelectTab(0);
        vm.AskRevokeKey(vm.Keys[0]);
        vm.ToggleDeliveries(vm.Webhooks[0]);
        vm.OpenCreateWebhookCommand.Execute(null);
        vm.KeyDialog.Name = "Zapier";
        vm.WebhookDialog.Url = "https://hooks.example.com/in";
        vm.WebhookDialog.SetEvent("booking.created", true);
        vm.KeyDialog.Copied(true);
        vm.KeysLoad.RetryCommand.Execute(null);
        var (context, sink) = Pages.Context();
        model.Attach(context);
        Assert.Empty(sink.Sent);
    }
}
