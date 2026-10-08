using DistrictAI.Core.Ffi;
using DistrictAI.Views;
using DistrictAI.Views.Calls;
using DistrictAI.Views.Analytics;
using DistrictAI.Views.Billing;
using DistrictAI.Views.Blocked;
using DistrictAI.Views.Desk;
using DistrictAI.Views.Hq;
using DistrictAI.Views.Marketplace;
using DistrictAI.Views.Rooms;
using DistrictAI.Views.Scheduling;
using DistrictAI.Views.Settings.CallHandling;
using DistrictAI.Views.Settings.Directory;
using DistrictAI.Views.Settings.Knowledge;
using DistrictAI.Views.Settings.Members;
using DistrictAI.Views.Settings.Messaging;
using DistrictAI.Views.Settings.Persona;
using DistrictAI.Views.Settings.Routing;
using DistrictAI.Views.Settings.Tools;
using DistrictAI.Views.Settings.VoiceStudio;
using DistrictAI.Views.Settings;
using DistrictAI.Views.Support;
using DistrictAI.Views.Workflows;
using Microsoft.UI.Xaml;

namespace DistrictAI;

/// <summary>
/// The window's screens: the one place that maps each <see cref="ScreenView"/>
/// variant to its page (and through it, its view model). Each page is made the
/// first time its screen shows and kept, so it keeps its scroll position and
/// focus between visits. An area of 2.0 has its page here from the start,
/// as a stub, and its packet fills the page, not this file.
/// </summary>
public sealed partial class MainWindow
{
    private OverviewPage? _overview;
    private InboxPage? _inbox;
    private ThreadPage? _thread;
    private CallsPage? _calls;
    private CallDetailPage? _callDetail;
    private ContactsPage? _contacts;
    private ContactDetailPage? _contactDetail;
    private AccountPage? _account;
    private DevicesPage? _devices;
    private DialerPage? _dialer;
    private UnavailablePage? _unavailable;
    private BlockedPage? _blocked;
    private HqPage? _hq;
    private AnalyticsPage? _analytics;
    private MarketplacePage? _marketplace;
    private BillingPage? _billing;
    private WorkflowsPage? _workflows;
    private SchedulingPage? _scheduling;
    private DeskPage? _desk;
    private DeskTicketPage? _deskTicket;
    private DeskSettingsPage? _deskSettings;
    private SupportPage? _support;
    private SupportRequestPage? _supportRequest;
    private RoomsPage? _rooms;
    private SettingsHubPage? _settingsHub;
    private PersonaPage? _persona;
    private VoiceStudioPage? _voiceStudio;
    private CallHandlingPage? _callHandling;
    private RoutingPage? _routing;
    private DirectoryPage? _directory;
    private ToolsPage? _tools;
    private KnowledgePage? _knowledge;
    private MessagingPage? _messaging;
    private MembersPage? _members;

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
            case ScreenView.Dialer dialer:
                var arriving = _dialer is null || !ReferenceEquals(PageHost.Content, _dialer);
                _dialer ??= Made(new DialerPage(), p => p.Attach(_context));
                _dialer.Show(dialer.View);
                page = _dialer;
                if (arriving)
                {
                    // Once it is on screen: the box takes the keyboard, so typing and
                    // pasting a number go straight into it.
                    var shown = _dialer;
                    DispatcherQueue.TryEnqueue(shown.FocusNumber);
                }
                break;
            case ScreenView.Unavailable unavailable:
                _unavailable ??= Made(new UnavailablePage(), p => p.Attach(_context));
                _unavailable.Show(unavailable.Title, unavailable.Body);
                page = _unavailable;
                break;
            case ScreenView.BlockedContacts screenBlockedContacts:
                _blocked ??= Made(new BlockedPage(), p => p.Attach(_context));
                _blocked.Show(screenBlockedContacts.View);
                page = _blocked;
                break;
            case ScreenView.Hq screenHq:
                _hq ??= Made(new HqPage(), p => p.Attach(_context));
                _hq.Show(screenHq.View);
                page = _hq;
                break;
            case ScreenView.Analytics screenAnalytics:
                _analytics ??= Made(new AnalyticsPage(), p => p.Attach(_context));
                _analytics.Show(screenAnalytics.View);
                page = _analytics;
                break;
            case ScreenView.Marketplace screenMarketplace:
                _marketplace ??= Made(new MarketplacePage(), p => p.Attach(_context));
                _marketplace.Show(screenMarketplace.View);
                page = _marketplace;
                break;
            case ScreenView.Billing screenBilling:
                _billing ??= Made(new BillingPage(), p => p.Attach(_context));
                _billing.Show(screenBilling.View);
                page = _billing;
                break;
            case ScreenView.Workflows screenWorkflows:
                _workflows ??= Made(new WorkflowsPage(), p => p.Attach(_context));
                _workflows.Show(screenWorkflows.View);
                page = _workflows;
                break;
            case ScreenView.Scheduling screenScheduling:
                _scheduling ??= Made(new SchedulingPage(), p => p.Attach(_context));
                _scheduling.Show(screenScheduling.View);
                page = _scheduling;
                break;
            case ScreenView.Desk screenDesk:
                _desk ??= Made(new DeskPage(), p => p.Attach(_context));
                _desk.Show(screenDesk.View);
                page = _desk;
                break;
            case ScreenView.DeskTicket screenDeskTicket:
                _deskTicket ??= Made(new DeskTicketPage(), p => p.Attach(_context));
                _deskTicket.Show(screenDeskTicket.View);
                page = _deskTicket;
                break;
            case ScreenView.DeskSettings screenDeskSettings:
                _deskSettings ??= Made(new DeskSettingsPage(), p => p.Attach(_context));
                _deskSettings.Show(screenDeskSettings.View);
                page = _deskSettings;
                break;
            case ScreenView.Support screenSupport:
                _support ??= Made(new SupportPage(), p => p.Attach(_context));
                _support.Show(screenSupport.View);
                page = _support;
                break;
            case ScreenView.SupportRequest screenSupportRequest:
                _supportRequest ??= Made(new SupportRequestPage(), p => p.Attach(_context));
                _supportRequest.Show(screenSupportRequest.View);
                page = _supportRequest;
                break;
            case ScreenView.Rooms screenRooms:
                _rooms ??= Made(new RoomsPage(), p => p.Attach(_context));
                _rooms.Show(screenRooms.View);
                page = _rooms;
                break;
            case ScreenView.WorkspaceSettings screenWorkspaceSettings:
                _settingsHub ??= Made(new SettingsHubPage(), p => p.Attach(_context));
                _settingsHub.Show(screenWorkspaceSettings.View);
                page = _settingsHub;
                break;
            case ScreenView.Persona screenPersona:
                _persona ??= Made(new PersonaPage(), p => p.Attach(_context));
                _persona.Show(screenPersona.View);
                page = _persona;
                break;
            case ScreenView.VoiceStudio screenVoiceStudio:
                _voiceStudio ??= Made(new VoiceStudioPage(), p => p.Attach(_context));
                _voiceStudio.Show(screenVoiceStudio.View);
                page = _voiceStudio;
                break;
            case ScreenView.CallHandling screenCallHandling:
                _callHandling ??= Made(new CallHandlingPage(), p => p.Attach(_context));
                _callHandling.Show(screenCallHandling.View);
                page = _callHandling;
                break;
            case ScreenView.Routing screenRouting:
                _routing ??= Made(new RoutingPage(), p => p.Attach(_context));
                _routing.Show(screenRouting.View);
                page = _routing;
                break;
            case ScreenView.Directory screenDirectory:
                _directory ??= Made(new DirectoryPage(), p => p.Attach(_context));
                _directory.Show(screenDirectory.View);
                page = _directory;
                break;
            case ScreenView.Tools screenTools:
                _tools ??= Made(new ToolsPage(), p => p.Attach(_context));
                _tools.Show(screenTools.View);
                page = _tools;
                break;
            case ScreenView.Knowledge screenKnowledge:
                _knowledge ??= Made(new KnowledgePage(), p => p.Attach(_context));
                _knowledge.Show(screenKnowledge.View);
                page = _knowledge;
                break;
            case ScreenView.Messaging screenMessaging:
                _messaging ??= Made(new MessagingPage(), p => p.Attach(_context));
                _messaging.Show(screenMessaging.View);
                page = _messaging;
                break;
            case ScreenView.Members screenMembers:
                _members ??= Made(new MembersPage(), p => p.Attach(_context));
                _members.Show(screenMembers.View);
                page = _members;
                break;
            case ScreenView.Session:
                // Outside a session the sign-in page shows instead (Render).
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
        or ScreenView.Unavailable
        or ScreenView.BlockedContacts
        or ScreenView.DeskTicket
        or ScreenView.DeskSettings
        or ScreenView.SupportRequest
        or ScreenView.Persona
        or ScreenView.VoiceStudio
        or ScreenView.CallHandling
        or ScreenView.Routing
        or ScreenView.Directory
        or ScreenView.Tools
        or ScreenView.Knowledge
        or ScreenView.Messaging
        or ScreenView.Members;
}
