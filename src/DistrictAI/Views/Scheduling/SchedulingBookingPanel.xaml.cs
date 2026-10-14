using DistrictAI.ViewModels;
using DistrictAI.ViewModels.Scheduling;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views.Scheduling;

/// <summary>
/// One booking: its row from the list, what the booker answered, and the
/// cancel, confirmed once (<see cref="SchedulingDialogHost"/>). Moving it and
/// changing its host are offered on the list, which has read what they need.
/// </summary>
public sealed partial class SchedulingBookingPanel : UserControl
{
    private readonly SchedulingDialogHost _cancel = new("CancelBookingDialog");
    private PageContext? _context;

    /// <summary>A panel showing nothing until <see cref="Attach"/>.</summary>
    public SchedulingBookingPanel()
    {
        InitializeComponent();
        Status.Attach(ViewModel.Load);
        Loaded += (_, _) => SyncDialogs();
    }

    /// <summary>What the panel shows, and its actions.</summary>
    public SchedulingBookingViewModel ViewModel { get; private set; } = new();

    /// <summary>Shows <paramref name="model"/>, the page's own, from now on.</summary>
    internal void Attach(SchedulingBookingViewModel model, PageContext context)
    {
        ViewModel = model;
        _context = context;
        Status.Attach(model.Load);
        Bindings.Update();
    }

    /// <summary>Opens the cancel dialog while the core holds it open, and closes it once it does not.</summary>
    internal void SyncDialogs() =>
        _cancel.Sync(this, _context, ViewModel.Cancel.Dialog, () => new CancelBookingContent(ViewModel.Cancel));

    /// <summary>Closes the dialog without asking the core: the page left the screen.</summary>
    internal void CloseDialogs() => _cancel.Close();
}
