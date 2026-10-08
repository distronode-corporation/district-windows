using CommunityToolkit.Mvvm.ComponentModel;
using DistrictAI.Core.Ffi;

namespace DistrictAI.ViewModels.Billing;

/// <summary>Billing: what the page shows, and what it sends. A stub until the area's packet builds it.</summary>
public sealed partial class BillingViewModel : ObservableObject
{
    private PageContext? _context;

    /// <summary>The core's view of the screen.</summary>
    [ObservableProperty]
    public partial BillingView? View { get; set; }

    internal void Attach(PageContext context) => _context = context;

    internal void Show(BillingView view) => View = view;

    /// <summary>Forwards one of the screen's actions.</summary>
    internal void Send(BillingAction action) => _context?.Send(new UiEvent.Billing(action));
}
