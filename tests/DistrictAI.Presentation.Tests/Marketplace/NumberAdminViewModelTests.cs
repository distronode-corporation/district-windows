using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels.Marketplace;
using Xunit;

namespace DistrictAI.Presentation.Tests.Marketplace;

/// <summary>The core's number management records, with defaults, so each test names only what it is about.</summary>
internal static class N
{
    public static LookupView Lookup(string number = "", bool canAsk = false, string[]? result = null, string? notice = null, string? unrepeatable = null) =>
        new(
            Eyebrow: "Number lookup",
            NumberLabel: "Phone number, in full",
            Number: number,
            Action: "Look this number up",
            CanAsk: canAsk,
            Result: result ?? [],
            Notice: notice,
            Unrepeatable: unrepeatable);

    public static CarrierTabView Carrier(
        CarrierAccountView[]? accounts = null,
        SipTrunkView[]? trunks = null,
        SipTrunkFormInput? sip = null,
        bool canCreate = false,
        bool? verifyEnabled = false,
        bool canToggle = true,
        A2pFormInput? a2p = null,
        bool canSubmitA2p = false,
        TollFreeFormInput? tollFree = null,
        bool canSubmitTollFree = false,
        string? notice = null) =>
        new(
            AccountsEyebrow: "Carrier accounts",
            AccountsStatus: new LoadStatus.Ready(),
            AccountsEmpty: null,
            Accounts: accounts ?? [],
            Notice: notice,
            Unrepeatable: null,
            Working: false,
            Sip: new SipView(
                Eyebrow: "SIP trunks",
                Status: new LoadStatus.Ready(),
                Empty: null,
                Trunks: trunks ?? [],
                NameLabel: "Trunk name",
                DomainLabel: "Domain label",
                DomainCaption: "The carrier adds .sip.twilio.com.",
                IpLabel: "Allowed IPs",
                Form: sip ?? new SipTrunkFormInput(string.Empty, string.Empty, string.Empty),
                ChargeNote: "$25 per month.",
                CreateAction: "Create trunk",
                CanCreate: canCreate),
            Verify: new VerifyView(
                Eyebrow: "One-time passcodes",
                Status: new LoadStatus.Ready(),
                Enabled: verifyEnabled,
                State: "Not enabled",
                Action: "Enable one-time passcodes",
                Caption: "Creates a sender.",
                CanToggle: canToggle),
            ComplianceEyebrow: "Messaging compliance",
            ComplianceNote: "Neither registration can be checked from the app.",
            A2p: new A2pView(
                Title: "A2P 10DLC registration",
                Caption: "Required.",
                Labels: new A2pLabels("Legal business name", "EIN", "Website", "Industry", "Campaign type", "What you will send", "Example 1", "Example 2"),
                Form: a2p ?? new A2pFormInput(string.Empty, string.Empty, string.Empty, string.Empty, "CUSTOMER_CARE", string.Empty, string.Empty, string.Empty),
                CampaignTypes: [new ChoiceView("CUSTOMER_CARE", "CUSTOMER_CARE"), new ChoiceView("LOW_VOLUME", "LOW_VOLUME")],
                SubmitAction: "Submit registration",
                CanSubmit: canSubmitA2p),
            TollFree: new TollFreeView(
                Title: "Toll-free verification",
                Caption: "Required.",
                Labels: new TollFreeLabels("Phone number, in full", "Legal business name", "Opt-in link", "Example message", "Use case"),
                OptInCaption: "A reviewer opens this by hand.",
                Form: tollFree ?? new TollFreeFormInput(string.Empty, string.Empty, string.Empty, string.Empty, "Customer Support"),
                UseCases: [new ChoiceView("Customer Support", "Customer Support"), new ChoiceView("Marketing", "Marketing")],
                SubmitAction: "Submit verification",
                CanSubmit: canSubmitTollFree));

    public static NumberAdminView Admin(
        LookupView? lookup = null,
        NumberConfirmationView? confirmation = null,
        CarrierTabView? carrier = null,
        string? notice = null,
        string? unrepeatable = null) =>
        new(
            CarrierTab: "Carrier",
            ConfigureAction: "Reapply call routing",
            ConfigureCaption: "Points this number back at this workspace.",
            ReleaseAction: "Release this number",
            NumberNotice: notice,
            NumberUnrepeatable: unrepeatable,
            Lookup: lookup ?? Lookup(),
            Confirmation: confirmation,
            Carrier: carrier);

    public static NumberConfirmationView Release(bool canConfirm = true, bool running = false) =>
        new("Release this number?", "+14165550100 goes back to the carrier's pool.", running ? "Working…" : "Release it", "Cancel", true, canConfirm, running, null, null);
}

public sealed class NumberAdminViewModelTests
{
    private static (NumberAdminViewModel Model, RecordingSink Sink) Attached()
    {
        var model = new NumberAdminViewModel();
        var (context, sink) = Pages.Context();
        model.Attach(context);
        return (model, sink);
    }

    private static UiEvent.Marketplace Sent(NumberAdminAction action) => new(new MarketplaceAction.Numbers(action));

    [Fact]
    public void AHeldNumberIsReappliedOrReleasedOnlyWhenTheCoreAllows()
    {
        var (model, sink) = Attached();
        var managed = NumberRowItem.From(
            new NumberRowView("+1 416 555 0100", "Main line", null, new NumberManageView("+14165550100", null, true, true, false)),
            N.Admin());
        Assert.True(managed.Manageable);
        Assert.Equal("Reapply call routing", managed.ConfigureLabel);
        model.Configure(managed);
        model.AskRelease(managed);
        Assert.Equal([Sent(new NumberAdminAction.Configure("+14165550100")), Sent(new NumberAdminAction.AskRelease("+14165550100"))], sink.Sent);

        var held = NumberRowItem.From(
            new NumberRowView("+1 416 555 0199", "Local", null, new NumberManageView("+14165550199", "Held by Distronode.", false, false, false)),
            N.Admin());
        Assert.False(held.Manageable);
        Assert.True(held.HasManageNote);
        model.Configure(held);
        model.AskRelease(held);
        Assert.Equal(2, sink.Sent.Count);

        var forSale = NumberRowItem.From(new NumberRowView("+1 800 555 0122", "Toll-free", null, null), null);
        Assert.False(forSale.Manageable);
        Assert.Equal(string.Empty, forSale.PhoneNumber);
    }

    [Fact]
    public void TheLookupAsksTheCoreAndShowsItsAnswer()
    {
        var (model, sink) = Attached();
        model.Show(N.Admin(notice: "Released.", unrepeatable: null));
        Assert.True(model.HasNumberNotice);
        Assert.False(model.AskLookupCommand.CanExecute(null));
        Assert.False(model.OnCarrier);

        model.LookupNumber = "+14165550142";
        Assert.Equal(Sent(new NumberAdminAction.EditLookup("+14165550142")), sink.Sent[^1]);
        model.Show(N.Admin(lookup: N.Lookup("+14165550142", canAsk: true)));
        Assert.True(model.AskLookupCommand.CanExecute(null));
        model.AskLookupCommand.Execute(null);
        Assert.Equal(Sent(new NumberAdminAction.AskLookup()), sink.Sent[^1]);

        model.Show(N.Admin(lookup: N.Lookup("+14165550142", canAsk: true, result: ["+14165550142", "CA"])));
        Assert.Equal("+14165550142" + Environment.NewLine + "CA", model.LookupResult);
        model.Show(N.Admin(lookup: N.Lookup("+14165550142", notice: "Busy.", unrepeatable: "Not offered again.")));
        Assert.Equal("Busy. Not offered again.", model.LookupResult);
    }

    [Fact]
    public void AConfirmationGoesAheadOnlyWhenItCan()
    {
        var (model, sink) = Attached();
        model.Show(N.Admin(confirmation: N.Release()));
        Assert.NotNull(model.Confirmation);
        model.ConfirmCommand.Execute(null);
        model.Show(N.Admin(confirmation: N.Release(canConfirm: false, running: true)));
        model.ConfirmCommand.Execute(null);
        model.CancelConfirmationCommand.Execute(null);
        Assert.Equal([Sent(new NumberAdminAction.Confirm()), Sent(new NumberAdminAction.CancelConfirmation())], sink.Sent);
        model.Show(N.Admin());
        Assert.Null(model.Confirmation);
    }

    [Fact]
    public void TheCarrierTabShowsTheCoresAndSendsItsForms()
    {
        var (model, sink) = Attached();
        model.Show(N.Admin(carrier: N.Carrier(
            accounts: [new CarrierAccountView("Twilio", "Acme", "Connected", false, "Balance 12.50 USD", null)],
            trunks: [new SipTrunkView("Office PBX", "office", "active · 1 address", null)],
            notice: "Submitted.")));
        Assert.True(model.OnCarrier);
        Assert.Equal("Acme" + Environment.NewLine + "Connected" + Environment.NewLine + "Balance 12.50 USD", Assert.Single(model.Accounts).Lines);
        Assert.False(Assert.Single(model.Trunks).HasNote);
        Assert.True(model.HasCarrierNotice);
        Assert.Equal(0, model.CampaignIndex);
        Assert.Equal(0, model.UseCaseIndex);
        Assert.Empty(sink.Sent);

        model.SipName = "Branch";
        Assert.Equal(Sent(new NumberAdminAction.EditSipTrunk(new SipTrunkFormInput("Branch", string.Empty, string.Empty))), sink.Sent[^1]);
        model.A2pBusiness = "Acme";
        Assert.Equal(
            Sent(new NumberAdminAction.EditA2p(new A2pFormInput("Acme", string.Empty, string.Empty, string.Empty, "CUSTOMER_CARE", string.Empty, string.Empty, string.Empty))),
            sink.Sent[^1]);
        model.CampaignIndex = 1;
        Assert.Equal(
            Sent(new NumberAdminAction.EditA2p(new A2pFormInput("Acme", string.Empty, string.Empty, string.Empty, "LOW_VOLUME", string.Empty, string.Empty, string.Empty))),
            sink.Sent[^1]);
        model.TollFreeNumber = "+18005550188";
        Assert.Equal(
            Sent(new NumberAdminAction.EditTollFree(new TollFreeFormInput("+18005550188", string.Empty, string.Empty, string.Empty, "Customer Support"))),
            sink.Sent[^1]);
        model.UseCaseIndex = 1;
        Assert.Equal(
            Sent(new NumberAdminAction.EditTollFree(new TollFreeFormInput("+18005550188", string.Empty, string.Empty, string.Empty, "Marketing"))),
            sink.Sent[^1]);

        var before = sink.Sent.Count;
        model.CreateSipTrunkCommand.Execute(null);
        model.AskA2pCommand.Execute(null);
        model.AskTollFreeCommand.Execute(null);
        Assert.Equal(before, sink.Sent.Count);

        model.Show(N.Admin(carrier: N.Carrier(canCreate: true, canSubmitA2p: true, canSubmitTollFree: true, verifyEnabled: true)));
        model.CreateSipTrunkCommand.Execute(null);
        model.ToggleVerifyCommand.Execute(null);
        model.AskA2pCommand.Execute(null);
        model.AskTollFreeCommand.Execute(null);
        Assert.Equal(
            [
                Sent(new NumberAdminAction.CreateSipTrunk()),
                Sent(new NumberAdminAction.SetVerify(false)),
                Sent(new NumberAdminAction.AskA2p()),
                Sent(new NumberAdminAction.AskTollFree()),
            ],
            sink.Sent.Skip(before));
    }
}
