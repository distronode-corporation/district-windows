using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels;
using DistrictAI.ViewModels.Billing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views.Billing;

/// <summary>
/// Billing: the plan and the account's invoices, and, where the core offers
/// them, choosing a plan (with the step that names Stripe) and managing
/// billing, both in the checkout window.
/// </summary>
public sealed partial class BillingPage : UserControl
{
    /// <summary>A page with nothing shown yet.</summary>
    public BillingPage()
    {
        InitializeComponent();
        Status.Attach(ViewModel.Load);
        Refresh.Attach(ViewModel.Load);
    }

    /// <summary>What the page shows, and its actions.</summary>
    public BillingViewModel ViewModel { get; } = new();

    internal void Attach(PageContext context) => ViewModel.Attach(context);

    internal void Show(BillingView view) => ViewModel.Show(view);

    private void OnOpenInvoice(object sender, RoutedEventArgs e) =>
        ViewModel.OpenInvoiceCommand.Execute((sender as FrameworkElement)?.Tag as string);
}
