using DistrictAI.ViewModels.Billing;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views.Billing;

/// <summary>
/// Choosing a plan and managing billing inside the app, with the step that
/// names Stripe before the checkout window opens. Shown by the billing screen
/// and by the overview of an account with no workspace; the page that holds it
/// owns its <see cref="PlanChooserViewModel"/>.
/// </summary>
public sealed partial class PlanChooser : UserControl
{
    /// <summary>A chooser showing nothing until <see cref="Attach"/>.</summary>
    public PlanChooser()
    {
        InitializeComponent();
    }

    /// <summary>What the chooser shows, and its commands.</summary>
    public PlanChooserViewModel ViewModel { get; private set; } = new();

    /// <summary>Shows <paramref name="model"/>, the page's own, from now on.</summary>
    internal void Attach(PlanChooserViewModel model)
    {
        ViewModel = model;
        Bindings.Update();
    }
}
