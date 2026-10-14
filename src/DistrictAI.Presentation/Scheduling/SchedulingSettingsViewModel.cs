using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DistrictAI.Core.Ffi;

namespace DistrictAI.ViewModels.Scheduling;

/// <summary>
/// The settings (the core's <see cref="SchedulingSettingsView"/>): four tabs,
/// the booking page, the booking assistant, the member's profile and their
/// notifications, each read the first time it shows and shown as rows, with
/// Edit opening its dialog. The booking page's and the assistant's Edit need
/// a member who may change things; the profile and the notifications are the
/// member's own.
/// </summary>
/// <remarks>
/// A tab whose read failed reads only itself again on "Try again", rather
/// than the whole section, which would go back to the first tab.
/// </remarks>
public sealed partial class SchedulingSettingsViewModel : ObservableObject
{
    private PageContext? _context;

    /// <summary>Loading and failure, for the read of the tab showing.</summary>
    public LoadStateViewModel Load { get; } = new()
    {
        RetryEvent = () => Event(new SchedulingSettingsAction.Refresh()),
    };

    /// <summary>The booking page's dialog.</summary>
    public SettingsBrandingViewModel Branding { get; } = new();

    /// <summary>The assistant's dialog.</summary>
    public SettingsAssistantViewModel Assistant { get; } = new();

    /// <summary>The profile's dialog.</summary>
    public SettingsProfileViewModel Profile { get; } = new();

    /// <summary>The notifications' dialog.</summary>
    public SettingsNotificationsViewModel Notifications { get; } = new();

    /// <summary>What the tab showing read.</summary>
    public ObservableCollection<SettingsGroupEntry> Groups { get; } = [];

    /// <summary>The tab showing.</summary>
    [ObservableProperty]
    public partial SchedulingSettingsTab Tab { get; set; }

    /// <summary>The tab showing, by its place: 0 the booking page, 3 the notifications.</summary>
    [ObservableProperty]
    public partial int TabIndex { get; set; }

    /// <summary>"Booking page".</summary>
    [ObservableProperty]
    public partial string BookingPageLabel { get; set; } = string.Empty;

    /// <summary>"Booking assistant".</summary>
    [ObservableProperty]
    public partial string AssistantLabel { get; set; } = string.Empty;

    /// <summary>"Your profile".</summary>
    [ObservableProperty]
    public partial string ProfileLabel { get; set; } = string.Empty;

    /// <summary>"Your notifications".</summary>
    [ObservableProperty]
    public partial string NotificationsLabel { get; set; } = string.Empty;

    /// <summary>Whether the booking page shows.</summary>
    [ObservableProperty]
    public partial bool OnBookingPage { get; set; }

    /// <summary>Whether the assistant shows.</summary>
    [ObservableProperty]
    public partial bool OnAssistant { get; set; }

    /// <summary>Whether the profile shows.</summary>
    [ObservableProperty]
    public partial bool OnProfile { get; set; }

    /// <summary>Whether the notifications show.</summary>
    [ObservableProperty]
    public partial bool OnNotifications { get; set; }

    /// <summary>Whether Edit is offered on the tab showing.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenEditorCommand))]
    public partial bool OffersEdit { get; set; }

    /// <summary>"Edit".</summary>
    [ObservableProperty]
    public partial string EditLabel { get; set; } = string.Empty;

    internal void Attach(PageContext context)
    {
        _context = context;
        Load.Attach(context);
        Branding.Attach(context);
        Assistant.Attach(context);
        Profile.Attach(context);
        Notifications.Attach(context);
    }

    internal void Show(SchedulingSettingsView view)
    {
        Load.Show(view.Status, view.Groups.Length > 0, null, refreshing: false, refreshFailure: null);
        var selected = view.Tabs.FirstOrDefault(tab => tab.Selected)?.Tab ?? SchedulingSettingsTab.BookingPage;
        Tab = selected;
        TabIndex = Array.IndexOf(_order, selected);
        OnBookingPage = selected == SchedulingSettingsTab.BookingPage;
        OnAssistant = selected == SchedulingSettingsTab.Assistant;
        OnProfile = selected == SchedulingSettingsTab.Profile;
        OnNotifications = selected == SchedulingSettingsTab.Notifications;
        BookingPageLabel = LabelOf(view, SchedulingSettingsTab.BookingPage);
        AssistantLabel = LabelOf(view, SchedulingSettingsTab.Assistant);
        ProfileLabel = LabelOf(view, SchedulingSettingsTab.Profile);
        NotificationsLabel = LabelOf(view, SchedulingSettingsTab.Notifications);
        Display.Sync(Groups, [.. view.Groups.Select(SettingsGroupEntry.From)]);
        OffersEdit = view.OffersEdit;
        EditLabel = view.EditLabel;
        Branding.Show(view.Branding);
        Assistant.Show(view.Assistant);
        Profile.Show(view.Profile);
        Notifications.Show(view.Notifications);
    }

    /// <summary>Closes its dialogs: the settings are no longer showing.</summary>
    internal void Leave()
    {
        Branding.Show(null);
        Assistant.Show(null);
        Profile.Show(null);
        Notifications.Show(null);
    }

    /// <summary>Shows the tab at <paramref name="index"/>; the one showing sends nothing.</summary>
    internal void SelectTab(int index)
    {
        if (index >= 0 && index < _order.Length)
        {
            SelectTab(_order[index]);
        }
    }

    /// <summary>Shows <paramref name="tab"/>; the one showing sends nothing.</summary>
    internal void SelectTab(SchedulingSettingsTab tab)
    {
        if (tab != Tab)
        {
            Send(new SchedulingSettingsAction.SelectTab(tab));
        }
    }

    /// <summary>Opens the dialog of the tab showing, when Edit is offered.</summary>
    [RelayCommand(CanExecute = nameof(OffersEdit))]
    private void OpenEditor() => Send(new SchedulingSettingsAction.OpenEditor(Tab));

    /// <summary>The tabs, in the order the window shows them.</summary>
    private static readonly SchedulingSettingsTab[] _order =
    [
        SchedulingSettingsTab.BookingPage,
        SchedulingSettingsTab.Assistant,
        SchedulingSettingsTab.Profile,
        SchedulingSettingsTab.Notifications,
    ];

    private static string LabelOf(SchedulingSettingsView view, SchedulingSettingsTab tab) =>
        view.Tabs.FirstOrDefault(entry => entry.Tab == tab)?.Label ?? string.Empty;

    private void Send(SchedulingSettingsAction action) => _context?.Send(Event(action));

    /// <summary>The event for <paramref name="action"/>.</summary>
    internal static UiEvent Event(SchedulingSettingsAction action) =>
        new UiEvent.SchedulingAdmin(new SchedulingAdminAction.Settings(action));
}

/// <summary>A group of what a tab read: its heading, the sentence under it, and its rows.</summary>
/// <param name="Heading">"Booking page", "As an attendee".</param>
/// <param name="Description">What the group is about, or empty.</param>
/// <param name="Rows">Each label and what was read for it.</param>
public sealed record SettingsGroupEntry(string Heading, string Description, IReadOnlyList<SettingsRowEntry> Rows)
{
    /// <summary>Whether there is a <see cref="Description"/>.</summary>
    public bool HasDescription => Description.Length > 0;

    internal static SettingsGroupEntry From(SchedulingSettingsGroupView group) =>
        new(group.Heading, group.Description ?? string.Empty, [.. group.Rows.Select(row => new SettingsRowEntry(row.Label, row.Value))]);

    /// <inheritdoc/>
    public override string ToString() => Heading;
}

/// <summary>One row: a label and what was read for it.</summary>
/// <param name="Label">"Business name".</param>
/// <param name="Value">"Contract Agency", "Not set", "On".</param>
public sealed record SettingsRowEntry(string Label, string Value)
{
    /// <summary>What a screen reader says for the row.</summary>
    public string AccessibleName => Label + ": " + Value;

    /// <inheritdoc/>
    public override string ToString() => AccessibleName;
}
