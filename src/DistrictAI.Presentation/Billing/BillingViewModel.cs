using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DistrictAI.Core.Ffi;

namespace DistrictAI.ViewModels.Billing;

/// <summary>A plan or invoice line of the account's half, ready to show.</summary>
/// <param name="Name">The plan's name, or the invoice's amount.</param>
/// <param name="Detail">Its state, amount and date, as one line.</param>
/// <param name="InvoiceId">The invoice to open, when it has a page; null otherwise.</param>
public sealed record BillingLine(string Name, string Detail, string? InvoiceId)
{
    /// <summary>Whether the line has a page to open.</summary>
    public bool CanOpen => InvoiceId is not null;
}

/// <summary>
/// Billing: the workspace's plan and the account's plans and invoices, read
/// only, and, while the core offers them to this member on this computer,
/// choosing a plan and managing billing inside the app, through the service's
/// own checkout (run by Stripe) in the checkout window. Every word is the
/// core's, and no price is shown here: checkout shows it.
/// </summary>
public sealed partial class BillingViewModel : ObservableObject
{
    private PageContext? _context;
    private int _promoMaxLength = 40;

    /// <summary>Loading, a read that failed, and a refresh.</summary>
    public LoadStateViewModel Load { get; } = new();

    /// <summary>The core's view of the screen, as last shown.</summary>
    [ObservableProperty]
    public partial BillingView? View { get; set; }

    /// <summary>The plan's name.</summary>
    [ObservableProperty]
    public partial string PlanName { get; set; } = string.Empty;

    /// <summary>The plan's status badge.</summary>
    [ObservableProperty]
    public partial string PlanStatus { get; set; } = string.Empty;

    /// <summary>What the badge means, or empty.</summary>
    [ObservableProperty]
    public partial string PlanCaption { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="PlanCaption"/>.</summary>
    [ObservableProperty]
    public partial bool HasPlanCaption { get; set; }

    /// <summary>What happens past the included minutes, or empty.</summary>
    [ObservableProperty]
    public partial string OverageNote { get; set; } = string.Empty;

    /// <summary>Whether there is an <see cref="OverageNote"/>.</summary>
    [ObservableProperty]
    public partial bool HasOverageNote { get; set; }

    /// <summary>"1,523 of 1,500 minutes this month", or the minutes alone, or empty.</summary>
    [ObservableProperty]
    public partial string MinutesLine { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="MinutesLine"/>.</summary>
    [ObservableProperty]
    public partial bool HasMinutes { get; set; }

    /// <summary>How full the minutes meter is, 0 to 100.</summary>
    [ObservableProperty]
    public partial double MeterPercent { get; set; }

    /// <summary>Whether the meter shows.</summary>
    [ObservableProperty]
    public partial bool HasMeter { get; set; }

    /// <summary>The note under the heading: what this screen can change.</summary>
    [ObservableProperty]
    public partial string Note { get; set; } = string.Empty;

    /// <summary>Whether "Manage billing on the web" shows.</summary>
    [ObservableProperty]
    public partial bool OffersWeb { get; set; }

    /// <summary>Its label.</summary>
    [ObservableProperty]
    public partial string WebAction { get; set; } = string.Empty;

    /// <summary>Whether the account's half is being read.</summary>
    [ObservableProperty]
    public partial bool AccountLoading { get; set; }

    /// <summary>Whether the account's half is read and shown.</summary>
    [ObservableProperty]
    public partial bool AccountReady { get; set; }

    /// <summary>The heading of the account's unavailable or failed card, or empty.</summary>
    [ObservableProperty]
    public partial string AccountProblemTitle { get; set; } = string.Empty;

    /// <summary>Why it is unavailable, or why the read failed.</summary>
    [ObservableProperty]
    public partial string AccountProblem { get; set; } = string.Empty;

    /// <summary>Whether the account's half shows a problem instead.</summary>
    [ObservableProperty]
    public partial bool HasAccountProblem { get; set; }

    /// <summary>The account's plans.</summary>
    public ObservableCollection<BillingLine> Plans { get; } = [];

    /// <summary>The line for an account with none, or empty.</summary>
    [ObservableProperty]
    public partial string NoPlans { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="NoPlans"/>.</summary>
    [ObservableProperty]
    public partial bool HasNoPlans { get; set; }

    /// <summary>The latest invoices.</summary>
    public ObservableCollection<BillingLine> Invoices { get; } = [];

    /// <summary>The line for an account with none, or the note under a list that is not all of them.</summary>
    [ObservableProperty]
    public partial string InvoicesNote { get; set; } = string.Empty;

    /// <summary>Whether there is an <see cref="InvoicesNote"/>.</summary>
    [ObservableProperty]
    public partial bool HasInvoicesNote { get; set; }

    /// <summary>Whether choosing a plan is offered to this member on this computer.</summary>
    [ObservableProperty]
    public partial bool OffersPurchase { get; set; }

    /// <summary>The chooser's action: "Choose a plan".</summary>
    [ObservableProperty]
    public partial string ChooseAction { get; set; } = string.Empty;

    /// <summary>Whether the chooser is open (the member pressed "Choose a plan").</summary>
    [ObservableProperty]
    public partial bool ChooserOpen { get; set; }

    /// <summary>Whether the chooser shows: open, and the confirmation step not showing.</summary>
    [ObservableProperty]
    public partial bool ChooserVisible { get; set; }

    /// <summary>The plans offered, by name.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<string> PlanNames { get; set; } = [];

    /// <summary>Which plan is chosen, or -1.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ContinueCommand))]
    public partial int SelectedPlan { get; set; } = -1;

    /// <summary>The terms offered, by name.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<string> TermNames { get; set; } = [];

    /// <summary>Which term is chosen.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ContinueCommand))]
    public partial int SelectedTerm { get; set; }

    /// <summary>The promotion code as typed.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ContinueCommand))]
    public partial string PromoText { get; set; } = string.Empty;

    /// <summary>The promotion code field's label.</summary>
    [ObservableProperty]
    public partial string PromoLabel { get; set; } = string.Empty;

    /// <summary>What to say about a code of the wrong shape, while it is.</summary>
    [ObservableProperty]
    public partial string PromoError { get; set; } = string.Empty;

    /// <summary>Whether the code typed is of the wrong shape.</summary>
    [ObservableProperty]
    public partial bool HasPromoError { get; set; }

    /// <summary>The line beside the plans: the price is shown at checkout.</summary>
    [ObservableProperty]
    public partial string PricesNote { get; set; } = string.Empty;

    /// <summary>Whether "Manage billing" (in the app) shows.</summary>
    [ObservableProperty]
    public partial bool OffersManage { get; set; }

    /// <summary>Its label.</summary>
    [ObservableProperty]
    public partial string ManageAction { get; set; } = string.Empty;

    /// <summary>Whether the purchase actions can be pressed (not while a link is asked for).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ContinueCommand), nameof(ConfirmCommand), nameof(ManageInAppCommand))]
    public partial bool PurchaseEnabled { get; set; }

    /// <summary>Whether the confirmation step shows.</summary>
    [ObservableProperty]
    public partial bool Confirming { get; set; }

    /// <summary>The confirmation step's heading.</summary>
    [ObservableProperty]
    public partial string ConfirmTitle { get; set; } = string.Empty;

    /// <summary>The confirmation step's body, which names Stripe.</summary>
    [ObservableProperty]
    public partial string ConfirmBody { get; set; } = string.Empty;

    /// <summary>The plan, term and code chosen, as one line.</summary>
    [ObservableProperty]
    public partial string ConfirmChoice { get; set; } = string.Empty;

    /// <summary>The confirming button's label.</summary>
    [ObservableProperty]
    public partial string ConfirmAction { get; set; } = string.Empty;

    /// <summary>The way back's label.</summary>
    [ObservableProperty]
    public partial string CancelAction { get; set; } = string.Empty;

    /// <summary>Whether a page is being opened.</summary>
    [ObservableProperty]
    public partial bool Opening { get; set; }

    /// <summary>What to say meanwhile.</summary>
    [ObservableProperty]
    public partial string OpeningLabel { get; set; } = string.Empty;

    /// <summary>What the last press came to, or empty.</summary>
    [ObservableProperty]
    public partial string Notice { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="Notice"/>.</summary>
    [ObservableProperty]
    public partial bool HasNotice { get; set; }

    /// <summary>Said once checkout opens in the browser, or empty.</summary>
    [ObservableProperty]
    public partial string InBrowserNote { get; set; } = string.Empty;

    /// <summary>Whether there is an <see cref="InBrowserNote"/>.</summary>
    [ObservableProperty]
    public partial bool HasInBrowserNote { get; set; }

    internal void Attach(PageContext context)
    {
        _context = context;
        Load.Attach(context);
    }

    internal void Show(BillingView view)
    {
        View = view;
        Load.Show(view.Status, true, null, view.Refreshing, null);
        ShowPlan(view.Plan);
        ShowAccount(view.Account);
        Note = view.Note;
        OffersWeb = view.OffersWeb;
        WebAction = view.WebAction;
        ShowPurchase(view.Purchase);
    }

    private void ShowPlan(PlanView? plan)
    {
        PlanName = plan?.Name ?? string.Empty;
        PlanStatus = plan?.Status ?? string.Empty;
        PlanCaption = plan?.StatusCaption ?? string.Empty;
        HasPlanCaption = PlanCaption.Length > 0;
        OverageNote = plan?.OverageNote ?? string.Empty;
        HasOverageNote = OverageNote.Length > 0;
        MinutesLine = MinutesOf(plan);
        HasMinutes = MinutesLine.Length > 0;
        HasMeter = plan?.MeterPercent is not null;
        MeterPercent = plan?.MeterPercent ?? 0;
    }

    /// <summary>The minutes line: used of included, or used alone; never a zero nobody measured.</summary>
    internal static string MinutesOf(PlanView? plan) => plan switch
    {
        { MinutesUsed: { } used, IncludedMinutes: { } included } =>
            string.Format(CultureInfo.CurrentCulture, "{0:N0} of {1:N0} minutes this month", used, included),
        { MinutesUsed: { } used } => string.Format(CultureInfo.CurrentCulture, "{0:N0} minutes this month", used),
        _ => string.Empty,
    };

    private void ShowAccount(AccountBillingView account)
    {
        AccountLoading = account.State == AccountBillingState.Loading;
        AccountReady = account.State == AccountBillingState.Ready;
        HasAccountProblem = account.State is AccountBillingState.Unavailable or AccountBillingState.Failed;
        AccountProblemTitle = account.Title ?? string.Empty;
        AccountProblem = account.Body ?? Display.Failure(account.Failure);
        Display.Sync(Plans, [.. account.Subscriptions.Select(PlanLine)]);
        Display.Sync(Invoices, [.. account.Invoices.Select(InvoiceLine)]);
        NoPlans = account.NoSubscriptions ?? string.Empty;
        HasNoPlans = NoPlans.Length > 0;
        InvoicesNote = account.NoInvoices ?? account.InvoicesTruncated ?? string.Empty;
        HasInvoicesNote = InvoicesNote.Length > 0;
    }

    /// <summary>One of the account's plans as a line: state, amount, and when it renews or ends.</summary>
    internal static BillingLine PlanLine(SubscriptionRowView row)
    {
        var parts = new List<string> { row.Status };
        if (row.Amount is { } amount)
        {
            parts.Add(amount);
        }
        if (row.RenewsAt is { } renews)
        {
            parts.Add("renews " + DateOf(renews));
        }
        if (row.EndsAt is { } ends)
        {
            parts.Add("ends " + DateOf(ends));
        }
        if (row.Coupon is { } coupon)
        {
            parts.Add(coupon);
        }
        return new BillingLine(row.Name, string.Join(", ", parts), null);
    }

    /// <summary>An invoice as a line: amount, state and date, and its page when it has one.</summary>
    internal static BillingLine InvoiceLine(InvoiceRowView row)
    {
        var detail = row.Status.Length > 0 ? $"{row.Status}, {DateOf(row.Created)}" : DateOf(row.Created);
        return new BillingLine(row.Amount, detail, row.CanOpen ? row.Id : null);
    }

    /// <summary>A Unix time as a local date.</summary>
    internal static string DateOf(long unixSeconds) =>
        DateTimeOffset.FromUnixTimeSeconds(unixSeconds).ToLocalTime().ToString("d", CultureInfo.CurrentCulture);

    private void ShowPurchase(PurchaseView? purchase)
    {
        OffersPurchase = purchase is not null;
        if (purchase is null)
        {
            ChooserOpen = false;
            ChooserVisible = false;
            OffersManage = false;
            Confirming = false;
            Opening = false;
            PurchaseEnabled = false;
            HasNotice = false;
            Notice = string.Empty;
            HasInBrowserNote = false;
            InBrowserNote = string.Empty;
            return;
        }
        ChooseAction = purchase.ChooseAction;
        if (!PlanNames.SequenceEqual(purchase.Plans.Select(plan => plan.Label)))
        {
            PlanNames = [.. purchase.Plans.Select(plan => plan.Label)];
        }
        if (!TermNames.SequenceEqual(purchase.Terms.Select(term => term.Label)))
        {
            TermNames = [.. purchase.Terms.Select(term => term.Label)];
        }
        PromoLabel = purchase.PromoLabel;
        PromoError = purchase.PromoInvalid;
        _promoMaxLength = (int)purchase.PromoMaxLen;
        HasPromoError = !PromoIsValid(PromoText, _promoMaxLength);
        PricesNote = purchase.PricesNote;
        OffersManage = purchase.OffersManage;
        ManageAction = purchase.ManageAction;
        PurchaseEnabled = purchase.Enabled;
        Confirming = purchase.Confirming is not null;
        ConfirmTitle = purchase.Confirming?.Title ?? string.Empty;
        ConfirmBody = purchase.Confirming?.Body ?? string.Empty;
        ConfirmChoice = purchase.Confirming is { } confirm
            ? string.Join(", ", new[] { confirm.Plan, confirm.Term, confirm.Promo }.Where(part => part is { Length: > 0 }))
            : string.Empty;
        ConfirmAction = purchase.Confirming?.Action ?? string.Empty;
        CancelAction = purchase.Confirming?.Cancel ?? string.Empty;
        ChooserVisible = ChooserOpen && !Confirming;
        Opening = purchase.Opening;
        OpeningLabel = purchase.OpeningLabel;
        Notice = Display.Failure(purchase.Notice);
        HasNotice = purchase.Notice is not null;
        InBrowserNote = purchase.InBrowserNote ?? string.Empty;
        HasInBrowserNote = InBrowserNote.Length > 0;
    }

    /// <summary>
    /// Whether <paramref name="typed"/> is a promotion code the core takes, or
    /// empty (no code): the core's <c>PromoCode::parse</c> rule, trimmed, ASCII
    /// letters upper-cased, then 1 to <paramref name="maxLength"/> of A to Z,
    /// 0 to 9, hyphen and underscore. The core checks again and sends nothing
    /// for a code it does not take; this only says why before the press.
    /// </summary>
    internal static bool PromoIsValid(string typed, int maxLength)
    {
        var code = typed.Trim();
        if (code.Length == 0)
        {
            return true;
        }
        return code.Length <= maxLength
            && code.All(c => c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '_');
    }

    partial void OnPromoTextChanged(string value) => HasPromoError = !PromoIsValid(value, _promoMaxLength);

    partial void OnChooserOpenChanged(bool value) => ChooserVisible = value && !Confirming;

    private bool CanContinue() =>
        PurchaseEnabled
        && View?.Purchase is { } purchase
        && SelectedPlan >= 0 && SelectedPlan < purchase.Plans.Length
        && SelectedTerm >= 0 && SelectedTerm < purchase.Terms.Length
        && PromoIsValid(PromoText, _promoMaxLength);

    [RelayCommand]
    private void OpenChooser() => ChooserOpen = true;

    [RelayCommand]
    private void CloseChooser() => ChooserOpen = false;

    /// <summary>The chooser's "Continue": the plan, term and code go to the core, which shows the step naming Stripe.</summary>
    [RelayCommand(CanExecute = nameof(CanContinue))]
    private void Continue()
    {
        // Execute does not ask CanExecute: a code of the wrong shape never goes.
        if (!CanContinue() || View?.Purchase is not { } purchase)
        {
            return;
        }
        var promo = PromoText.Trim();
        Send(new BillingAction.ChoosePlan(
            purchase.Plans[SelectedPlan].Tier,
            purchase.Terms[SelectedTerm].Term,
            promo.Length > 0 ? promo : null));
    }

    private bool CanPress() => PurchaseEnabled;

    [RelayCommand(CanExecute = nameof(CanPress))]
    private void Confirm() => Send(new BillingAction.ConfirmPurchase());

    [RelayCommand]
    private void Cancel() => Send(new BillingAction.CancelPurchase());

    [RelayCommand(CanExecute = nameof(CanPress))]
    private void ManageInApp() => Send(new BillingAction.ManageInApp());

    [RelayCommand]
    private void ManageOnWeb() => Send(new BillingAction.ManageOnWeb());

    [RelayCommand]
    private void DismissNotice() => Send(new BillingAction.DismissPurchaseNotice());

    [RelayCommand]
    private void OpenInvoice(string? invoiceId)
    {
        if (invoiceId is { Length: > 0 })
        {
            Send(new BillingAction.OpenInvoice(invoiceId));
        }
    }

    /// <summary>Forwards one of the screen's actions.</summary>
    internal void Send(BillingAction action) => _context?.Send(new UiEvent.Billing(action));
}
