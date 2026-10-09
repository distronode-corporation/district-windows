using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DistrictAI.Core.Ffi;

namespace DistrictAI.ViewModels.Billing;

/// <summary>
/// Choosing a plan and managing billing inside the app, while the core offers
/// them to this member on this computer (<see cref="PurchaseView"/>): the
/// plans, terms and promotion code, the step that names Stripe, and the
/// progress and notice of the checkout window. The billing screen shows it,
/// and so does the overview of an account with no workspace yet, which
/// checkout makes. Every word is the core's, and no price is shown here:
/// checkout shows it.
/// </summary>
public sealed partial class PlanChooserViewModel : ObservableObject
{
    private PageContext? _context;
    private int _promoMaxLength = 40;

    /// <summary>The core's purchase controls, as last shown; null while none are offered.</summary>
    [ObservableProperty]
    public partial PurchaseView? View { get; set; }

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

    internal void Attach(PageContext context) => _context = context;

    internal void Show(PurchaseView? purchase)
    {
        View = purchase;
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
        && View is { } purchase
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
        if (!CanContinue() || View is not { } purchase)
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
    private void DismissNotice() => Send(new BillingAction.DismissPurchaseNotice());

    /// <summary>Forwards one of the chooser's actions.</summary>
    internal void Send(BillingAction action) => _context?.Send(new UiEvent.Billing(action));
}
