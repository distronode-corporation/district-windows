using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DistrictAI.Core.Ffi;

namespace DistrictAI.ViewModels.Contacts;

/// <summary>
/// The changes a member makes to the open contact: Edit, Delete, Run research,
/// Clear research, Block and Unblock, each as the core offers it
/// (<see cref="ContactDetailView.Writes"/>, which is absent for a role that may
/// change nothing), and the question the core asks before each but research.
/// </summary>
public sealed partial class ContactChangesViewModel : ObservableObject
{
    /// <summary>What a member whose role cannot change contacts reads instead of the controls, as District AI for Linux words it.</summary>
    public const string ReadOnlyNote = "You have read-only access to this workspace, so you cannot change contacts.";

    private PageContext? _context;

    /// <summary>The form changing the contact.</summary>
    public ContactFormViewModel Edit { get; } = new(ContactFormKind.Edit);

    /// <summary>Whether the member may change contacts at all: the controls show.</summary>
    [ObservableProperty]
    public partial bool Writable { get; set; }

    /// <summary>Whether to say the access is read-only: the contact is read, and the member may change nothing.</summary>
    [ObservableProperty]
    public partial bool ReadOnly { get; set; }

    /// <summary>Whether "Edit" works now.</summary>
    [ObservableProperty]
    public partial bool CanEdit { get; set; }

    /// <summary>Whether "Delete" works now.</summary>
    [ObservableProperty]
    public partial bool CanDelete { get; set; }

    /// <summary>Whether "Run research" works now (billed).</summary>
    [ObservableProperty]
    public partial bool CanEnrich { get; set; }

    /// <summary>Whether "Clear research" shows: there is research to clear.</summary>
    [ObservableProperty]
    public partial bool OffersClearResearch { get; set; }

    /// <summary>Whether "Clear research" works now.</summary>
    [ObservableProperty]
    public partial bool CanClearResearch { get; set; }

    /// <summary>Whether the block control works now.</summary>
    [ObservableProperty]
    public partial bool CanBlock { get; set; }

    /// <summary>Whether the caller is known to be blocked.</summary>
    [ObservableProperty]
    public partial bool Blocked { get; set; }

    /// <summary>The block control's words: "Block", or "Unblock" for a blocked caller.</summary>
    [ObservableProperty]
    public partial string BlockLabel { get; set; } = "Block";

    /// <summary>What is on its way ("Deleting the contact"), or empty.</summary>
    [ObservableProperty]
    public partial string BusyText { get; set; } = string.Empty;

    /// <summary>Whether a change is on its way.</summary>
    [ObservableProperty]
    public partial bool Busy { get; set; }

    /// <summary>Whether the core is asking before a change.</summary>
    public bool Confirming { get; private set; }

    /// <summary>The question, in the core's words.</summary>
    public string ConfirmQuestion { get; private set; } = string.Empty;

    /// <summary>The button that answers yes, in the core's words; also the dialog's heading.</summary>
    public string ConfirmAction { get; private set; } = string.Empty;

    /// <summary>Whether yes removes or hides something, so Enter answers no.</summary>
    public bool ConfirmDestructive { get; private set; }

    internal void Attach(PageContext context)
    {
        _context = context;
        Edit.Attach(context);
    }

    internal void Show(ContactDetailView view)
    {
        var writes = view.Writes;
        Writable = writes is not null;
        ReadOnly = writes is null && view.Status is LoadStatus.Ready;
        CanEdit = writes?.CanEdit ?? false;
        CanDelete = writes?.CanDelete ?? false;
        CanEnrich = writes?.CanEnrich ?? false;
        OffersClearResearch = writes?.CanClearResearch ?? false;
        CanClearResearch = OffersClearResearch;
        CanBlock = writes?.CanBlock ?? false;
        Blocked = view.Blocked;
        BlockLabel = view.Blocked ? "Unblock" : "Block";
        BusyText = view.Busy ?? string.Empty;
        Busy = BusyText.Length > 0;
        Confirming = view.Confirming is not null;
        ConfirmQuestion = view.Confirming?.Question ?? string.Empty;
        ConfirmAction = view.Confirming?.Action ?? string.Empty;
        ConfirmDestructive = view.Confirming?.Destructive ?? false;
        Edit.Show(view.Editing);
    }

    /// <summary>The answer to the core's question: yes makes the change, no puts it away.</summary>
    internal void Answer(bool confirmed) =>
        Send(confirmed ? new ContactsAction.Confirm() : new ContactsAction.Cancel());

    // Each control turns itself off as it is pressed, until the core's next
    // view says what it offers: a double click never asks twice, and research,
    // which is billed and not asked about, never starts twice.

    [RelayCommand]
    private void StartEdit()
    {
        if (CanEdit)
        {
            CanEdit = false;
            Send(new ContactsAction.StartEdit());
        }
    }

    [RelayCommand]
    private void AskDelete()
    {
        if (CanDelete)
        {
            CanDelete = false;
            Send(new ContactsAction.AskDelete());
        }
    }

    [RelayCommand]
    private void AskClearResearch()
    {
        if (CanClearResearch)
        {
            CanClearResearch = false;
            Send(new ContactsAction.AskClearResearch());
        }
    }

    [RelayCommand]
    private void AskBlock()
    {
        if (CanBlock)
        {
            CanBlock = false;
            Send(new ContactsAction.AskBlock());
        }
    }

    [RelayCommand]
    private void Enrich()
    {
        if (CanEnrich)
        {
            CanEnrich = false;
            Send(new ContactsAction.Enrich());
        }
    }

    /// <summary>Puts the contact's failure away.</summary>
    [RelayCommand]
    private void DismissFailure() => Send(new ContactsAction.DismissFailure());

    private void Send(ContactsAction action) => _context?.Send(new UiEvent.Contacts(action));
}
