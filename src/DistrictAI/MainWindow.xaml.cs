using System.Collections.ObjectModel;
using DistrictAI.Core;
using DistrictAI.Core.Ffi;
using DistrictAI.Platform;
using DistrictAI.ViewModels;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

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
    private CoreSnapshot? _shown;
    private bool _rendering;

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

        RenderNav(shell.Nav, shell.Unread);
        Nav.IsBackButtonVisible = shell.CanGoBack ? NavigationViewBackButtonVisible.Visible : NavigationViewBackButtonVisible.Collapsed;
        Nav.IsBackEnabled = shell.CanGoBack;

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
