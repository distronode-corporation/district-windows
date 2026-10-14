using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DistrictAI.Core.Ffi;

namespace DistrictAI.ViewModels.Scheduling;

/// <summary>
/// One event type (the core's <see cref="SchedulingEventTypeView"/>): its
/// details, its hosts and its booking questions, each read on its own and
/// failing alone, and its four writes for a member who may change things
/// (the editor, the state actions, the hosts and the questions), each a
/// dialog the core holds open.
/// </summary>
public sealed partial class SchedulingEventTypeViewModel : ObservableObject
{
    private PageContext? _context;

    /// <summary>An event type with nothing shown yet.</summary>
    public SchedulingEventTypeViewModel()
    {
        Editor = new EventTypeEditorViewModel(
            () => Event(new EventTypeAction.Save()),
            () => Event(new EventTypeAction.CloseDialog()),
            form => Event(new EventTypeAction.EditForm(form)));
    }

    /// <summary>Loading and failure, for the event type's own read.</summary>
    public LoadStateViewModel Load { get; } = new();

    /// <summary>The editor.</summary>
    public EventTypeEditorViewModel Editor { get; }

    /// <summary>The state actions.</summary>
    public EventTypeActionsViewModel Actions { get; } = new();

    /// <summary>The hosts dialog.</summary>
    public EventTypeHostsViewModel HostsEditor { get; } = new();

    /// <summary>The questions dialog.</summary>
    public EventTypeQuestionsViewModel QuestionsEditor { get; } = new();

    /// <summary>The details that say something.</summary>
    public ObservableCollection<FactItem> Facts { get; } = [];

    /// <summary>The hosts, each with their role.</summary>
    public ObservableCollection<FactItem> Hosts { get; } = [];

    /// <summary>The questions, each with its type.</summary>
    public ObservableCollection<FactItem> Questions { get; } = [];

    /// <summary>Its name once read, its slug before.</summary>
    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    /// <summary>What its booking page says about it, or empty.</summary>
    [ObservableProperty]
    public partial string Description { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="Description"/>.</summary>
    [ObservableProperty]
    public partial bool HasDescription { get; set; }

    /// <summary>The details' heading.</summary>
    [ObservableProperty]
    public partial string DetailsHeading { get; set; } = string.Empty;

    /// <summary>Whether the four writes are offered.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenEditorCommand), nameof(OpenActionsCommand), nameof(OpenHostsCommand), nameof(OpenQuestionsCommand))]
    public partial bool OffersWrites { get; set; }

    /// <summary>"Edit".</summary>
    [ObservableProperty]
    public partial string EditLabel { get; set; } = string.Empty;

    /// <summary>"Manage".</summary>
    [ObservableProperty]
    public partial string ManageLabel { get; set; } = string.Empty;

    /// <summary>"Hosts", the button.</summary>
    [ObservableProperty]
    public partial string HostsLabel { get; set; } = string.Empty;

    /// <summary>"Questions", the button.</summary>
    [ObservableProperty]
    public partial string QuestionsLabel { get; set; } = string.Empty;

    /// <summary>The hosts' heading.</summary>
    [ObservableProperty]
    public partial string HostsHeading { get; set; } = string.Empty;

    /// <summary>Whether the hosts are being read.</summary>
    [ObservableProperty]
    public partial bool HostsLoading { get; set; }

    /// <summary>Why the hosts could not be read, or empty.</summary>
    [ObservableProperty]
    public partial string HostsFailure { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="HostsFailure"/>.</summary>
    [ObservableProperty]
    public partial bool HasHostsFailure { get; set; }

    /// <summary>Whether the hosts are read and there are none.</summary>
    [ObservableProperty]
    public partial bool ShowNoHosts { get; set; }

    /// <summary>What to say then.</summary>
    [ObservableProperty]
    public partial string NoHosts { get; set; } = string.Empty;

    /// <summary>The questions' heading.</summary>
    [ObservableProperty]
    public partial string QuestionsHeading { get; set; } = string.Empty;

    /// <summary>Whether the questions are being read.</summary>
    [ObservableProperty]
    public partial bool QuestionsLoading { get; set; }

    /// <summary>Why the questions could not be read, or empty.</summary>
    [ObservableProperty]
    public partial string QuestionsFailure { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="QuestionsFailure"/>.</summary>
    [ObservableProperty]
    public partial bool HasQuestionsFailure { get; set; }

    /// <summary>Whether the questions are read and there are none.</summary>
    [ObservableProperty]
    public partial bool ShowNoQuestions { get; set; }

    /// <summary>What to say then.</summary>
    [ObservableProperty]
    public partial string NoQuestions { get; set; } = string.Empty;

    internal void Attach(PageContext context)
    {
        _context = context;
        Load.Attach(context);
        Editor.Attach(context);
        Actions.Attach(context);
        HostsEditor.Attach(context);
        QuestionsEditor.Attach(context);
    }

    internal void Show(SchedulingEventTypeView view)
    {
        Load.Show(view.Status, true, null, refreshing: false, refreshFailure: null);
        Name = view.Name;
        Description = view.Description;
        HasDescription = view.Description.Length > 0;
        DetailsHeading = view.DetailsHeading;
        Display.Sync(Facts, [.. view.Facts.Select(FactItem.From)]);
        OffersWrites = view.OffersWrites;
        EditLabel = view.EditLabel;
        ManageLabel = view.ManageLabel;
        HostsLabel = view.HostsLabel;
        QuestionsLabel = view.QuestionsLabel;

        HostsHeading = view.HostsHeading;
        HostsLoading = view.HostsStatus is LoadStatus.Loading;
        var hostsFailure = (view.HostsStatus as LoadStatus.Failed)?.Failure;
        HostsFailure = Display.Failure(hostsFailure);
        HasHostsFailure = hostsFailure is not null;
        Display.Sync(Hosts, [.. view.Hosts.Select(FactItem.From)]);
        ShowNoHosts = view.HostsStatus is LoadStatus.Ready && view.Hosts.Length == 0;
        NoHosts = view.NoHosts;

        QuestionsHeading = view.QuestionsHeading;
        QuestionsLoading = view.QuestionsStatus is LoadStatus.Loading;
        var questionsFailure = (view.QuestionsStatus as LoadStatus.Failed)?.Failure;
        QuestionsFailure = Display.Failure(questionsFailure);
        HasQuestionsFailure = questionsFailure is not null;
        Display.Sync(Questions, [.. view.Questions.Select(FactItem.From)]);
        ShowNoQuestions = view.QuestionsStatus is LoadStatus.Ready && view.Questions.Length == 0;
        NoQuestions = view.NoQuestions;

        Editor.Show(view.Editor);
        Actions.Show(view.Actions);
        HostsEditor.Show(view.HostsDialog);
        QuestionsEditor.Show(view.QuestionsDialog);
    }

    /// <summary>Closes its dialogs: the event type is no longer showing.</summary>
    internal void Leave()
    {
        Editor.Show(null);
        Actions.Show(null);
        HostsEditor.Show(null);
        QuestionsEditor.Show(null);
    }

    [RelayCommand(CanExecute = nameof(OffersWrites))]
    private void OpenEditor() => Send(new EventTypeAction.OpenEditor());

    [RelayCommand(CanExecute = nameof(OffersWrites))]
    private void OpenActions() => Send(new EventTypeAction.OpenActions());

    [RelayCommand(CanExecute = nameof(OffersWrites))]
    private void OpenHosts() => Send(new EventTypeAction.OpenHosts());

    [RelayCommand(CanExecute = nameof(OffersWrites))]
    private void OpenQuestions() => Send(new EventTypeAction.OpenQuestions());

    /// <summary>Forwards one of the page's actions.</summary>
    internal void Send(EventTypeAction action) => _context?.Send(Event(action));

    /// <summary>The event for <paramref name="action"/>.</summary>
    internal static UiEvent Event(EventTypeAction action) =>
        new UiEvent.SchedulingAdmin(new SchedulingAdminAction.EventType(action));
}
