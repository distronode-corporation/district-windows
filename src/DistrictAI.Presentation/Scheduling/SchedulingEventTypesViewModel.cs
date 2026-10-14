using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DistrictAI.Core.Ffi;

namespace DistrictAI.ViewModels.Scheduling;

/// <summary>
/// The event types (the core's <see cref="SchedulingEventTypesView"/>): each
/// one's name, how long it runs and where, its state and its booking page,
/// which a click opens, and "Create event type" for a member who may change
/// things, over a list that was read.
/// </summary>
public sealed partial class SchedulingEventTypesViewModel : ObservableObject
{
    private PageContext? _context;

    /// <summary>A list with nothing shown yet.</summary>
    public SchedulingEventTypesViewModel()
    {
        Create = new EventTypeEditorViewModel(
            () => Event(new EventTypesAction.Save()),
            () => Event(new EventTypesAction.CloseEditor()),
            form => Event(new EventTypesAction.EditForm(form)));
    }

    /// <summary>Loading, a read that failed, and an empty list.</summary>
    public LoadStateViewModel Load { get; } = new();

    /// <summary>The create form.</summary>
    public EventTypeEditorViewModel Create { get; }

    /// <summary>The event types.</summary>
    public ObservableCollection<EventTypeEntry> Rows { get; } = [];

    /// <summary>Whether "Create event type" is offered.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenCreateCommand))]
    public partial bool OffersCreate { get; set; }

    /// <summary>"Create event type".</summary>
    [ObservableProperty]
    public partial string CreateLabel { get; set; } = string.Empty;

    internal void Attach(PageContext context)
    {
        _context = context;
        Load.Attach(context);
        Create.Attach(context);
    }

    internal void Show(SchedulingEventTypesView view)
    {
        Load.Show(view.Status, view.Rows.Length > 0, view.Empty, refreshing: false, refreshFailure: null);
        Display.Sync(Rows, [.. view.Rows.Select(EventTypeEntry.From)]);
        OffersCreate = view.OffersCreate;
        CreateLabel = view.CreateLabel;
        Create.Show(view.Create);
    }

    /// <summary>Closes the create form: the list is no longer showing.</summary>
    internal void Leave() => Create.Show(null);

    /// <summary>Opens <paramref name="row"/>'s event type.</summary>
    internal void Open(EventTypeEntry row) =>
        _context?.Send(new UiEvent.SchedulingAdmin(new SchedulingAdminAction.OpenEventType(row.Slug)));

    [RelayCommand(CanExecute = nameof(OffersCreate))]
    private void OpenCreate() => _context?.Send(Event(new EventTypesAction.OpenCreate()));

    /// <summary>The event for <paramref name="action"/>.</summary>
    internal static UiEvent Event(EventTypesAction action) =>
        new UiEvent.SchedulingAdmin(new SchedulingAdminAction.EventTypes(action));
}
