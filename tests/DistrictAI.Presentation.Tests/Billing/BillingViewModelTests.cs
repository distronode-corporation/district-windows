using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels.Billing;
using Xunit;

namespace DistrictAI.Presentation.Tests.Billing;

public sealed class BillingViewModelTests
{
    private const string ConfirmBody = "Payment is handled by Stripe, District AI's payment processor.";

    private static PlanView Plan() => new(
        Name: "VoicePro",
        Status: "Active",
        StatusCaption: null,
        OverageNote: "Minutes beyond your plan are billed automatically.",
        MinutesUsed: 1523,
        IncludedMinutes: 1500,
        MeterPercent: 100);

    private static AccountBillingView Account() => new(
        State: AccountBillingState.Ready,
        Title: null,
        Body: null,
        Failure: null,
        Subscriptions: [new SubscriptionRowView("sub_1", "District AI Voice Pro", "active", "$249.00", 1_756_909_800, null, "Founding customer")],
        NoSubscriptions: null,
        Invoices:
        [
            new InvoiceRowView("in_paid", "$249.00", "paid", 1_754_231_400, true),
            new InvoiceRowView("in_open", "$15.00", "open", 1_756_909_800, false),
        ],
        NoInvoices: null,
        InvoicesTruncated: "Showing your most recent invoices. Older ones are on the website.");

    internal static PurchaseView Purchase(
        ConfirmPurchaseView? confirming = null,
        bool opening = false,
        bool enabled = true,
        FailureView? notice = null,
        string? inBrowser = null) => new(
            Plans:
            [
                new PlanOptionView(PlanTierView.VoiceSolo, "Voice Solo"),
                new PlanOptionView(PlanTierView.VoiceStarter, "Voice Starter"),
                new PlanOptionView(PlanTierView.VoicePro, "Voice Pro"),
                new PlanOptionView(PlanTierView.VoiceStudio, "Voice Studio"),
            ],
            Terms: [new TermOptionView(PlanTermView.Monthly, "Monthly"), new TermOptionView(PlanTermView.Annual, "Annual")],
            ChooseAction: "Choose a plan",
            PricesNote: "Shown at checkout before you pay.",
            PromoLabel: "Promotion code (optional)",
            PromoInvalid: "A promotion code has only letters, digits, hyphens and underscores, up to 40 of them.",
            PromoMaxLen: 40,
            OffersManage: true,
            ManageAction: "Manage billing",
            Confirming: confirming,
            Opening: opening,
            OpeningLabel: "Opening a secure page",
            Enabled: enabled,
            Notice: notice,
            InBrowserNote: inBrowser);

    internal static ConfirmPurchaseView Confirm() => new(
        Title: "Continue to checkout with Stripe",
        Body: ConfirmBody,
        Plan: "Voice Pro",
        Term: "Annual",
        Promo: "SAVE-100",
        Action: "Continue to checkout",
        Cancel: "Cancel");

    private static BillingView View(PurchaseView? purchase = null, bool offersWeb = false, LoadStatus? status = null) => new(
        Title: "Billing",
        Status: status ?? new LoadStatus.Ready(),
        Refreshing: false,
        Plan: Plan(),
        Account: Account(),
        Note: purchase is null ? "This screen is read only." : "Checkout and payment are handled by Stripe.",
        OffersWeb: offersWeb,
        WebAction: "Manage billing on the web",
        Purchase: purchase);

    private static (BillingViewModel Model, RecordingSink Sink) Attached()
    {
        var model = new BillingViewModel();
        var (context, sink) = Pages.Context();
        model.Attach(context);
        return (model, sink);
    }

    [Fact]
    public void ThePlanAndTheAccountShow()
    {
        var (model, _) = Attached();
        model.Show(View(Purchase()));
        Assert.True(model.Load.Ready);
        Assert.Equal("VoicePro", model.PlanName);
        Assert.Equal("Active", model.PlanStatus);
        Assert.False(model.HasPlanCaption);
        Assert.True(model.HasOverageNote);
        Assert.True(model.HasMeter);
        Assert.Equal(100, model.MeterPercent);
        Assert.Equal(BillingViewModel.MinutesOf(Plan()), model.MinutesLine);
        Assert.True(model.AccountReady);
        Assert.False(model.HasAccountProblem);
        var plan = Assert.Single(model.Plans);
        Assert.Equal("District AI Voice Pro", plan.Name);
        Assert.StartsWith("active, $249.00, renews ", plan.Detail, StringComparison.Ordinal);
        Assert.EndsWith(", Founding customer", plan.Detail, StringComparison.Ordinal);
        Assert.Equal(2, model.Invoices.Count);
        Assert.True(model.Invoices[0].CanOpen);
        Assert.False(model.Invoices[1].CanOpen);
        Assert.True(model.HasInvoicesNote);
    }

    [Fact]
    public void MinutesAreNeverAZeroNobodyMeasured()
    {
        Assert.Equal(string.Empty, BillingViewModel.MinutesOf(null));
        Assert.Equal(string.Empty, BillingViewModel.MinutesOf(Plan() with { MinutesUsed = null }));
        Assert.Contains("minutes this month", BillingViewModel.MinutesOf(Plan() with { IncludedMinutes = null }), StringComparison.Ordinal);
    }

    [Fact]
    public void AnOutageIsNeverShownAsNone()
    {
        var (model, _) = Attached();
        model.Show(View() with
        {
            Account = Account() with
            {
                State = AccountBillingState.Unavailable,
                Title = "Billing details unavailable",
                Body = "The payment provider could not be reached just now.",
                Subscriptions = [],
                Invoices = [],
                InvoicesTruncated = null,
            },
        });
        Assert.True(model.HasAccountProblem);
        Assert.False(model.AccountReady);
        Assert.Equal("Billing details unavailable", model.AccountProblemTitle);
        Assert.False(model.HasNoPlans);

        model.Show(View() with { Account = Account() with { State = AccountBillingState.Failed, Title = "Could not load", Failure = new FailureView("Offline.", null, true) } });
        Assert.True(model.HasAccountProblem);
        Assert.Contains("Offline.", model.AccountProblem, StringComparison.Ordinal);
    }

    [Fact]
    public void PurchasesOffShowTheReadOnlyScreen()
    {
        var (model, sink) = Attached();
        model.Show(View(purchase: null, offersWeb: true));
        Assert.False(model.Purchase.OffersPurchase);
        Assert.False(model.Purchase.ChooserVisible);
        Assert.False(model.Purchase.Confirming);
        Assert.False(model.Purchase.OffersManage);
        Assert.True(model.OffersWeb);
        Assert.False(model.Purchase.ContinueCommand.CanExecute(null));
        Assert.False(model.Purchase.ManageInAppCommand.CanExecute(null));
        model.ManageOnWebCommand.Execute(null);
        Assert.Equal([new UiEvent.Billing(new BillingAction.ManageOnWeb())], sink.Sent);

        // Turned off while the chooser was open: it closes.
        model.Show(View(Purchase()));
        model.Purchase.OpenChooserCommand.Execute(null);
        Assert.True(model.Purchase.ChooserVisible);
        model.Show(View(purchase: null, offersWeb: true));
        Assert.False(model.Purchase.ChooserOpen);
        Assert.False(model.Purchase.ChooserVisible);
    }

    [Fact]
    public void ThePlanChosenGoesToTheCoreWithItsCode()
    {
        var (model, sink) = Attached();
        model.Show(View(Purchase()));
        Assert.True(model.Purchase.OffersPurchase);
        Assert.Equal(["Voice Solo", "Voice Starter", "Voice Pro", "Voice Studio"], model.Purchase.PlanNames);
        Assert.Equal(["Monthly", "Annual"], model.Purchase.TermNames);
        Assert.False(model.Purchase.ChooserVisible);
        model.Purchase.OpenChooserCommand.Execute(null);
        Assert.True(model.Purchase.ChooserVisible);
        Assert.False(model.Purchase.ContinueCommand.CanExecute(null), "no plan chosen yet");

        model.Purchase.SelectedPlan = 2;
        model.Purchase.SelectedTerm = 1;
        model.Purchase.PromoText = " save-100 ";
        Assert.False(model.Purchase.HasPromoError);
        Assert.True(model.Purchase.ContinueCommand.CanExecute(null));
        model.Purchase.ContinueCommand.Execute(null);

        model.Purchase.PromoText = "   ";
        model.Purchase.ContinueCommand.Execute(null);
        Assert.Equal(
            [
                new UiEvent.Billing(new BillingAction.ChoosePlan(PlanTierView.VoicePro, PlanTermView.Annual, "save-100")),
                new UiEvent.Billing(new BillingAction.ChoosePlan(PlanTierView.VoicePro, PlanTermView.Annual, null)),
            ],
            sink.Sent);

        model.Purchase.CloseChooserCommand.Execute(null);
        Assert.False(model.Purchase.ChooserVisible);
    }

    [Theory]
    [InlineData("SAVE&tier=Dgi")]
    [InlineData("save 10")]
    [InlineData("café")]
    [InlineData("ſALE")]
    [InlineData("SAVE#1")]
    [InlineData("12345678901234567890123456789012345678901")]
    public void ACodeTheCoreWouldRefuseIsSaidAndNotSent(string typed)
    {
        var (model, sink) = Attached();
        model.Show(View(Purchase()));
        model.Purchase.OpenChooserCommand.Execute(null);
        model.Purchase.SelectedPlan = 0;
        model.Purchase.PromoText = typed;
        Assert.True(model.Purchase.HasPromoError);
        Assert.False(model.Purchase.ContinueCommand.CanExecute(null));
        model.Purchase.ContinueCommand.Execute(null);
        Assert.Empty(sink.Sent);
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("SAVE100", true)]
    [InlineData("  launch_2-a ", true)]
    [InlineData("a", true)]
    [InlineData("1234567890123456789012345678901234567890", true)]
    [InlineData("12345678901234567890123456789012345678901", false)]
    [InlineData("SAVE.1", false)]
    public void ThePromotionCodeRuleIsTheCores(string typed, bool valid) =>
        Assert.Equal(valid, PlanChooserViewModel.PromoIsValid(typed, 40));

    [Fact]
    public void TheConfirmationNamesStripeAndContinuesOrCancels()
    {
        var (model, sink) = Attached();
        model.Show(View(Purchase()));
        model.Purchase.OpenChooserCommand.Execute(null);
        model.Show(View(Purchase(confirming: Confirm())));
        Assert.True(model.Purchase.Confirming);
        Assert.False(model.Purchase.ChooserVisible, "the step shows in place of the chooser");
        Assert.Contains("Stripe", model.Purchase.ConfirmTitle, StringComparison.Ordinal);
        Assert.Equal(ConfirmBody, model.Purchase.ConfirmBody);
        Assert.Equal("Voice Pro, Annual, SAVE-100", model.Purchase.ConfirmChoice);
        Assert.True(model.Purchase.ConfirmCommand.CanExecute(null));
        model.Purchase.ConfirmCommand.Execute(null);
        model.Purchase.CancelCommand.Execute(null);
        Assert.Equal(
            [new UiEvent.Billing(new BillingAction.ConfirmPurchase()), new UiEvent.Billing(new BillingAction.CancelPurchase())],
            sink.Sent);

        model.Show(View(Purchase(confirming: Confirm() with { Promo = null })));
        Assert.Equal("Voice Pro, Annual", model.Purchase.ConfirmChoice);
        model.Show(View(Purchase()));
        Assert.True(model.Purchase.ChooserVisible, "back to the chooser after Cancel");
    }

    [Fact]
    public void WhileALinkIsAskedForNothingCanBePressed()
    {
        var (model, sink) = Attached();
        model.Show(View(Purchase(confirming: Confirm(), opening: true, enabled: false)));
        Assert.True(model.Purchase.Opening);
        Assert.False(model.Purchase.ConfirmCommand.CanExecute(null));
        Assert.False(model.Purchase.ManageInAppCommand.CanExecute(null));
        model.Show(View(Purchase(opening: true)));
        Assert.True(model.Purchase.ManageInAppCommand.CanExecute(null));
        model.Purchase.ManageInAppCommand.Execute(null);
        Assert.Equal([new UiEvent.Billing(new BillingAction.ManageInApp())], sink.Sent);
    }

    [Fact]
    public void TheNoticeAndTheBrowserFallbackAreSaid()
    {
        var (model, sink) = Attached();
        model.Show(View(Purchase(
            notice: new FailureView("Updating the app should fix it.", null, false),
            inBrowser: "This computer cannot show checkout inside District AI, so it opens in your browser.")));
        Assert.True(model.Purchase.HasNotice);
        Assert.Contains("Updating the app", model.Purchase.Notice, StringComparison.Ordinal);
        Assert.True(model.Purchase.HasInBrowserNote);
        Assert.Contains("browser", model.Purchase.InBrowserNote, StringComparison.Ordinal);
        model.Purchase.DismissNoticeCommand.Execute(null);
        model.OpenInvoiceCommand.Execute("in_paid");
        model.OpenInvoiceCommand.Execute(null);
        Assert.Equal(
            [new UiEvent.Billing(new BillingAction.DismissPurchaseNotice()), new UiEvent.Billing(new BillingAction.OpenInvoice("in_paid"))],
            sink.Sent);
    }

    [Fact]
    public void NothingIsSentBeforeTheModelIsAttached()
    {
        var model = new BillingViewModel();
        model.Show(View(Purchase()));
        model.Send(new BillingAction.ConfirmPurchase());
        model.Purchase.Send(new BillingAction.ConfirmPurchase());
        Assert.Same(model.View, model.View);
    }
}
