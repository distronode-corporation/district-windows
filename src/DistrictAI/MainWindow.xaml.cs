using System.Collections.ObjectModel;
using System.Globalization;
using DistrictAI.Core;
using DistrictAI.Core.Ffi;
using DistrictAI.Platform;
using DistrictAI.ViewModels;
using DistrictAI.Views;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Tab = DistrictAI.Core.Ffi.TabView;

namespace DistrictAI;

/// <summary>
/// The one window: the sign-in page outside a session, and inside one the
/// navigation view with the page for the core's current screen. The window
/// decides nothing: it draws each snapshot and forwards what the user does.
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly CoreHost _core;
    private readonly PageContext _context;
    private readonly ObservableCollection<WorkspaceItem> _workspaces = [];
    private readonly Dictionary<Tab, NavigationViewItem> _tabs;
    private CoreSnapshot? _shown;
    private bool _rendering;

    private OverviewPage? _overview;
    private InboxPage? _inbox;
    private ThreadPage? _thread;
    private CallsPage? _calls;
    private CallDetailPage? _callDetail;
    private ContactsPage? _contacts;
    private ContactDetailPage? _contactDetail;
    private AccountPage? _account;
    private DevicesPage? _devices;
    private UnavailablePage? _unavailable;

    /// <summary>The window over <paramref name="core"/>.</summary>
    internal MainWindow(CoreHost core)
    {
        _core = core;
        InitializeComponent();
        // Mica on Windows 11; Windows 10 keeps the default background.
        if (MicaController.IsSupported())
        {
            SystemBackdrop = new MicaBackdrop();
        }
        _context = new PageContext(core, new LauncherBrowser(DispatcherQueue));
        _tabs = new()
        {
            [Tab.Overview] = OverviewTab,
            [Tab.Inbox] = InboxTab,
            [Tab.Calls] = CallsTab,
            [Tab.Contacts] = ContactsTab,
            [Tab.Account] = AccountTab,
        };
        WorkspacePicker.ItemsSource = _workspaces;
        // The mouse's back button, wherever the pointer is, even over a
        // control that handles the press itself.
        Root.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnPointerPressed), handledEventsToo: true);
        SignIn.Attach(core);
        core.Changed += (_, snapshot) => Render(snapshot);
        Render(core.Current);
    }

    private void Render(CoreSnapshot snapshot)
    {
        _shown = snapshot;
        var signedIn = snapshot.Shell.Phase == SessionPhase.SignedIn;
        Nav.Visibility = signedIn ? Visibility.Visible : Visibility.Collapsed;
        SignIn.Visibility = signedIn ? Visibility.Collapsed : Visibility.Visible;
        if (snapshot.Screen is ScreenView.Session session)
        {
            SignIn.Show(session.View);
        }
        _rendering = true;
        try
        {
            RenderShell(snapshot.Shell);
            if (signedIn)
            {
                RenderScreen(snapshot.Screen);
            }
            else
            {
                PageHost.Content = null;
                _devices?.Leave();
            }
        }
        finally
        {
            _rendering = false;
        }
    }

    private void RenderShell(ShellView shell)
    {
        // Before the page: the pages read it to enable their Report buttons.
        _context.ReportSending = shell.Report is ReportStatus.Sending;
        RenderCalls(shell);

        Nav.SelectedItem = shell.Tab is { } tab && _tabs.TryGetValue(tab, out var item) ? item : null;
        Nav.IsBackButtonVisible = shell.CanGoBack ? NavigationViewBackButtonVisible.Visible : NavigationViewBackButtonVisible.Collapsed;
        Nav.IsBackEnabled = shell.CanGoBack;

        UnreadBadge.Value = (int)Math.Min(shell.Unread, int.MaxValue);
        UnreadBadge.Visibility = shell.Unread > 0 ? Visibility.Visible : Visibility.Collapsed;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(
            InboxTab,
            shell.Unread > 0 ? string.Create(CultureInfo.CurrentCulture, $"Inbox, {shell.Unread} unread") : "Inbox");

        RenderWorkspaces(shell.Workspaces);

        NoticeBar.Message = shell.Notice ?? string.Empty;
        NoticeBar.IsOpen = shell.Notice is not null;

        LiveBar.Message = shell.Live?.Message ?? string.Empty;
        LiveBar.IsOpen = shell.Live is not null;
        LiveRefresh.Visibility = shell.Live?.CanRefresh == true ? Visibility.Visible : Visibility.Collapsed;
        LiveRefresh.IsEnabled = true;

        RenderReport(shell.Report);
    }

    private void RenderWorkspaces(WorkspaceSwitcherView workspaces)
    {
        Display.Sync(_workspaces, [.. workspaces.Entries.Select(WorkspaceItem.From)]);
        var active = _workspaces.FirstOrDefault(entry => string.Equals(entry.Id, workspaces.ActiveId, StringComparison.Ordinal));
        WorkspacePicker.SelectedItem = active;
        WorkspacePicker.IsEnabled = true;
        WorkspacePicker.Visibility = workspaces.CanSwitch ? Visibility.Visible : Visibility.Collapsed;
        WorkspaceName.Text = active?.Name ?? string.Empty;
        WorkspaceName.Visibility = !workspaces.CanSwitch && active is not null ? Visibility.Visible : Visibility.Collapsed;
        WorkspaceWarning.Text = workspaces.PartialWarning ?? string.Empty;
        WorkspaceWarning.Visibility = workspaces.PartialWarning is null ? Visibility.Collapsed : Visibility.Visible;
        WorkspaceBar.Title = workspaces.Title ?? string.Empty;
        WorkspaceBar.Message = workspaces.Message ?? string.Empty;
        WorkspaceBar.IsOpen = workspaces.Title is not null || workspaces.Message is not null;
    }

    private void RenderReport(ReportStatus? report)
    {
        switch (report)
        {
            case ReportStatus.Sending:
                ReportBar.Severity = InfoBarSeverity.Informational;
                ReportBar.Message = "Sending your report.";
                ReportBar.IsClosable = false;
                ReportBar.IsOpen = true;
                break;
            case ReportStatus.Sent sent:
                ReportBar.Severity = InfoBarSeverity.Success;
                ReportBar.Message = sent.Message;
                ReportBar.IsClosable = true;
                ReportBar.IsOpen = true;
                break;
            case ReportStatus.Failed failed:
                ReportBar.Severity = InfoBarSeverity.Error;
                ReportBar.Message = failed.Message;
                ReportBar.IsClosable = true;
                ReportBar.IsOpen = true;
                break;
            default:
                ReportBar.IsOpen = false;
                break;
        }
    }

    private void RenderScreen(ScreenView screen)
    {
        UIElement? page = null;
        switch (screen)
        {
            case ScreenView.Overview overview:
                _overview ??= Made(new OverviewPage(), p => p.Attach(_context));
                _overview.Show(overview.View);
                page = _overview;
                break;
            case ScreenView.Inbox inbox:
                _inbox ??= Made(new InboxPage(), p => p.Attach(_context));
                _inbox.Show(inbox.View);
                page = _inbox;
                break;
            case ScreenView.Thread thread:
                _thread ??= Made(new ThreadPage(), p => p.Attach(_context));
                _thread.Show(thread.View);
                page = _thread;
                break;
            case ScreenView.Calls calls:
                _calls ??= Made(new CallsPage(), p => p.Attach(_context));
                _calls.Show(calls.View);
                page = _calls;
                break;
            case ScreenView.CallDetail callDetail:
                _callDetail ??= Made(new CallDetailPage(), p => p.Attach(_context));
                _callDetail.Show(callDetail.View);
                page = _callDetail;
                break;
            case ScreenView.Contacts contacts:
                _contacts ??= Made(new ContactsPage(), p => p.Attach(_context));
                _contacts.Show(contacts.View);
                page = _contacts;
                break;
            case ScreenView.ContactDetail contactDetail:
                _contactDetail ??= Made(new ContactDetailPage(), p => p.Attach(_context));
                _contactDetail.Show(contactDetail.View);
                page = _contactDetail;
                break;
            case ScreenView.Account account:
                _account ??= Made(new AccountPage(), p => p.Attach(_context));
                _account.Show(account.View);
                page = _account;
                break;
            case ScreenView.Devices devices:
                _devices ??= Made(new DevicesPage(), p => p.Attach(_context));
                _devices.Show(devices.View);
                page = _devices;
                break;
            case ScreenView.Unavailable unavailable:
                _unavailable ??= Made(new UnavailablePage(), p => p.Attach(_context));
                _unavailable.Show(unavailable.Title, unavailable.Body);
                page = _unavailable;
                break;
            default:
                ShowOtherScreen(screen, _context, ref page);
                break;
        }
        if (!ReferenceEquals(page, _devices))
        {
            _devices?.Leave();
        }
        if (!ReferenceEquals(PageHost.Content, page))
        {
            PageHost.Content = page;
        }
    }

    /// <summary>
    /// A screen this file does not draw (the dialer, added with calls): set
    /// <paramref name="page"/> to its page, or leave it null for none.
    /// Implemented, if at all, in another part of this class.
    /// </summary>
    partial void ShowOtherScreen(ScreenView screen, PageContext context, ref UIElement? page);

    private static T Made<T>(T page, Action<T> attach)
    {
        attach(page);
        return page;
    }

    /// <summary>Whether the screen showing is a detail page, which Escape leaves.</summary>
    private bool OnDetailScreen() => _shown?.Screen is ScreenView.Thread
        or ScreenView.CallDetail
        or ScreenView.ContactDetail
        or ScreenView.Devices
        or ScreenView.Unavailable;

    private bool GoBack()
    {
        if (_shown?.Shell is not { Phase: SessionPhase.SignedIn, CanGoBack: true })
        {
            return false;
        }
        _core.Send(new UiEvent.Back());
        return true;
    }

    private void OnBackRequested(NavigationView sender, NavigationViewBackRequestedEventArgs args) => GoBack();

    private void OnBackAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) =>
        args.Handled = GoBack();

    private void OnEscapeAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) =>
        args.Handled = OnDetailScreen() && GoBack();

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (e.Pointer.PointerDeviceType == PointerDeviceType.Mouse
            && e.GetCurrentPoint(Root).Properties.IsXButton1Pressed
            && GoBack())
        {
            e.Handled = true;
        }
    }

    private void OnTabInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        foreach (var (tab, item) in _tabs)
        {
            if (ReferenceEquals(item, args.InvokedItemContainer))
            {
                _core.Send(new UiEvent.OpenTab(tab));
                return;
            }
        }
    }

    private void OnWorkspaceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_rendering
            || WorkspacePicker.SelectedItem is not WorkspaceItem chosen
            || string.Equals(chosen.Id, _shown?.Shell.Workspaces.ActiveId, StringComparison.Ordinal))
        {
            return;
        }
        // Until the core's next snapshot: one choice, one switch.
        WorkspacePicker.IsEnabled = false;
        _core.Send(new UiEvent.SelectWorkspace(chosen.Id));
    }

    private void OnNoticeClosed(InfoBar sender, object args) => _core.Send(new UiEvent.DismissNotice());

    private void OnReportClosed(InfoBar sender, object args) => _core.Send(new UiEvent.DismissReport());

    private void OnLiveRefresh(object sender, RoutedEventArgs e)
    {
        LiveRefresh.IsEnabled = false;
        _core.Send(new UiEvent.Refresh());
    }
}
