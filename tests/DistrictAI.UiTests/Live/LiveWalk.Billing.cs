using FlaUI.Core.Definitions;

namespace DistrictAI.UiTests.Live;

/// <summary>Billing: the plan and invoices, Purchases off and on, the Stripe step cancelled, and the checkout window (a probe).</summary>
internal sealed partial class LiveWalk
{
    /// <summary>The Account toggle (the core's purchase.rs, PurchaseSetting::LABEL).</summary>
    private const string PurchasesToggle = "Purchases on this computer";

    /// <summary>The checkout window's title, or the end of it once a page loads (Platform/Checkout/CheckoutHost.cs).</summary>
    private const string CheckoutTitle = "District AI checkout";

    /// <summary>
    /// The plan and the invoices show. With Purchases on this computer Off,
    /// Billing offers no plans and says it is read only; set back to "Sign in
    /// every time", the plans come back.
    /// </summary>
    private Outcome BillingView(Check check)
    {
        _ = Ui.Go(App, "Billing", "Billing");
        Check.Expect(Subheadings().Contains("Your plan"), "Billing shows no \"Your plan\"");
        _ = Ui.Text(App, "Invoices");
        check.Shot();

        _ = Ui.Go(App, "Account", "Account");
        Check.Expect(Ui.IsOn(App, PurchasesToggle), "Purchases on this computer was already Off");
        Ui.SetToggle(App, PurchasesToggle, false);
        try
        {
            _ = Ui.Go(App, "Billing", "Billing");
            _ = Ui.Text(App, "This screen is read only.");
            Check.Expect(Ui.Stays(() => Shows(App, ControlType.Button, "Choose a plan"), TimeSpan.FromSeconds(3)), "Billing offers \"Choose a plan\" with Purchases Off");
            Check.Expect(!Shows(App, ControlType.Button, "Manage billing"), "Billing offers \"Manage billing\" with Purchases Off");
            check.Shot();
        }
        finally
        {
            _ = Ui.Go(App, "Account", "Account");
            Ui.SetToggle(App, PurchasesToggle, true);
        }
        _ = Ui.Go(App, "Billing", "Billing");
        _ = Ui.Find(App, ControlType.Button, "Choose a plan");
        return Outcome.Pass("the plan and invoices show; with Purchases Off Billing is read only with no plans, and back on \"Sign in every time\" they return");
    }

    /// <summary>Choose a plan, Continue: the next step names Stripe; Cancel there. No checkout opens, nothing is bought.</summary>
    private Outcome BillingConfirm(Check check)
    {
        _ = Ui.Go(App, "Billing", "Billing");
        Ui.Press(App, "Choose a plan");
        var plan = Wait.For(
            () => App.TryMainWindow()?.FindAllDescendants(cf => cf.ByControlType(ControlType.RadioButton))
                .FirstOrDefault(radio => UiTests.Walk.NameOf(radio).StartsWith("Voice ", StringComparison.Ordinal)),
            Ui.Step,
            "a plan to choose",
            App.Describe);
        plan.Patterns.SelectionItem.Pattern.Select();
        Ui.Press(App, "Continue");
        _ = Ui.Text(App, "Continue to checkout with Stripe");
        _ = Ui.Text(App, "Payment is handled by Stripe");
        Check.Expect(Shows(App, ControlType.Button, "Continue to checkout"), "the Stripe step offers no \"Continue to checkout\"");
        check.Shot();
        Ui.Press(App, "Cancel");
        Check.Expect(
            Wait.Until(() => Ui.TryText(App, "Continue to checkout with Stripe") is null, Ui.Step),
            "the Stripe step stayed after Cancel");
        Check.Expect(App.TryWindow(CheckoutTitle) is null, "a checkout window opened");
        if (Shows(App, ControlType.Button, "Close the plans"))
        {
            Ui.Press(App, "Close the plans");
        }
        return Outcome.Pass($"chose {UiTests.Walk.NameOf(plan)}, Continue showed the step that names Stripe, Cancel closed it; no checkout opened");
    }

    /// <summary>
    /// Manage billing opens the checkout window (WebView2) within 30 s; it is
    /// saved and closed. A crash or no window is a probe finding: WebView2
    /// on Server 2025, or a challenge to the VM's address.
    /// </summary>
    private Outcome BillingManage(Check check)
    {
        _ = Ui.Go(App, "Billing", "Billing");
        if (!Shows(App, ControlType.Button, "Manage billing"))
        {
            throw new PreconditionException("Billing offers no \"Manage billing\" (no plan, or purchases off)");
        }
        Ui.Press(App, "Manage billing");
        var window = Wait.For(() => App.TryWindow(CheckoutTitle), TimeSpan.FromSeconds(30), "the checkout window", App.Describe);
        // Its page's title, once loaded, comes before the window's own.
        var loaded = Wait.Until(() => (App.TryWindow(CheckoutTitle) is { } now && UiTests.Walk.NameOf(now) != CheckoutTitle), TimeSpan.FromSeconds(20));
        window = App.TryWindow(CheckoutTitle) ?? window;
        check.ShotOf(window);
        Ui.PressIn(App, window, "Close checkout");
        Check.Expect(Wait.Until(() => App.TryWindow(CheckoutTitle) is null, Ui.Step), "the checkout window stayed after Close checkout");
        return Outcome.Pass(loaded
            ? "the checkout window opened, loaded its page, and closed"
            : "the checkout window opened (its page did not name itself within 20 s; see the screenshot) and closed");
    }
}
