using DistrictAI.ViewModels;
using DistrictAI.ViewModels.Scheduling;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views.Scheduling;

/// <summary>
/// The bookings: a view read a page at a time, each booking with the writes
/// the core offers on it under it, and the dialog of the one open (cancel,
/// move, change host), each confirmed once (<see cref="SchedulingDialogHost"/>).
/// </summary>
public sealed partial class SchedulingBookingsPanel : UserControl
{
    private readonly SchedulingDialogHost _cancel = new("CancelBookingDialog");
    private readonly SchedulingDialogHost _reschedule = new("RescheduleBookingDialog");
    private readonly SchedulingDialogHost _reassign = new("ReassignBookingDialog");
    private PageContext? _context;

    /// <summary>A panel showing nothing until <see cref="Attach"/>.</summary>
    public SchedulingBookingsPanel()
    {
        InitializeComponent();
        Status.Attach(ViewModel.Load);
        Loaded += (_, _) => SyncDialogs();
    }

    /// <summary>What the panel shows, and its actions.</summary>
    public SchedulingBookingsViewModel ViewModel { get; private set; } = new();

    /// <summary>Shows <paramref name="model"/>, the page's own, from now on.</summary>
    internal void Attach(SchedulingBookingsViewModel model, PageContext context)
    {
        ViewModel = model;
        _context = context;
        Status.Attach(model.Load);
        Bindings.Update();
    }

    /// <summary>Opens the dialog the core holds open, and closes the ones it does not.</summary>
    internal void SyncDialogs()
    {
        _cancel.Sync(this, _context, ViewModel.Cancel.Dialog, () => new CancelBookingContent(ViewModel.Cancel));
        _reschedule.Sync(this, _context, ViewModel.Reschedule.Dialog, () => new RescheduleBookingContent(ViewModel.Reschedule));
        _reassign.Sync(this, _context, ViewModel.Reassign.Dialog, () => new ReassignBookingContent(ViewModel.Reassign));
    }

    /// <summary>Closes every dialog without asking the core: the page left the screen.</summary>
    internal void CloseDialogs()
    {
        _cancel.Close();
        _reschedule.Close();
        _reassign.Close();
    }

    private static BookingEntry? RowOf(object sender) => (sender as FrameworkElement)?.DataContext as BookingEntry;

    private void OnTabChanged(object sender, SelectionChangedEventArgs e) => ViewModel.SelectTab(TabChooser.SelectedIndex);

    private void OnBookingClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is BookingEntry row)
        {
            ViewModel.Open(row);
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is { } row)
        {
            ViewModel.OpenCancel(row);
        }
    }

    private void OnRescheduleClick(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is { } row)
        {
            ViewModel.OpenReschedule(row);
        }
    }

    private void OnReassignClick(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is { } row)
        {
            ViewModel.OpenReassign(row);
        }
    }
}
