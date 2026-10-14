using DistrictAI.ViewModels;
using DistrictAI.ViewModels.Scheduling;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views.Scheduling;

/// <summary>
/// The member's calendars: a card per connected account with Choose
/// calendars and Disconnect, and the card that connects another; and their
/// three dialogs (an account's calendars, the CalDAV form, and the
/// disconnect, asked once more), each one the core holds open
/// (<see cref="SchedulingDialogHost"/>).
/// </summary>
public sealed partial class SchedulingCalendarPanel : UserControl
{
    private readonly SchedulingDialogHost _picker = new("CalendarPickerDialog");
    private readonly SchedulingDialogHost _caldav = new("CaldavDialog");
    private readonly SchedulingDialogHost _disconnect = new("CalendarDisconnectDialog");
    private PageContext? _context;

    /// <summary>A panel showing nothing until <see cref="Attach"/>.</summary>
    public SchedulingCalendarPanel()
    {
        InitializeComponent();
        Status.Attach(ViewModel.Load);
        Loaded += (_, _) => SyncDialogs();
    }

    /// <summary>What the panel shows, and its actions.</summary>
    public SchedulingCalendarViewModel ViewModel { get; private set; } = new();

    /// <summary>Shows <paramref name="model"/>, the page's own, from now on.</summary>
    internal void Attach(SchedulingCalendarViewModel model, PageContext context)
    {
        ViewModel = model;
        _context = context;
        Status.Attach(model.Load);
        Bindings.Update();
    }

    /// <summary>Opens the dialog the core holds open, and closes the ones it does not.</summary>
    internal void SyncDialogs()
    {
        _picker.Sync(this, _context, ViewModel.Picker.Dialog, () => new CalendarPickerContent(ViewModel.Picker));
        _caldav.Sync(this, _context, ViewModel.Caldav.Dialog, () => new CaldavContent(ViewModel.Caldav));
        _disconnect.Sync(this, _context, ViewModel.Disconnect, DisconnectBody);
    }

    /// <summary>Closes every dialog without asking the core: the page left the screen.</summary>
    internal void CloseDialogs()
    {
        _picker.Close();
        _caldav.Close();
        _disconnect.Close();
    }

    /// <summary>What disconnecting does, under the question that names the account.</summary>
    private UIElement DisconnectBody()
    {
        var body = new TextBlock { Text = ViewModel.Disconnect.Body, TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetAutomationId(body, "CalendarDisconnectBody");
        return body;
    }

    private static CalendarConnectionEntry? CardOf(object sender) => (sender as FrameworkElement)?.DataContext as CalendarConnectionEntry;

    private void OnChooseClick(object sender, RoutedEventArgs e)
    {
        if (CardOf(sender) is { } card)
        {
            ViewModel.OpenCalendars(card);
        }
    }

    private void OnDisconnectClick(object sender, RoutedEventArgs e)
    {
        if (CardOf(sender) is { } card)
        {
            ViewModel.AskDisconnect(card);
        }
    }

    private void OnConnectClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is CalendarProviderEntry provider)
        {
            ViewModel.Connect(provider);
        }
    }
}
