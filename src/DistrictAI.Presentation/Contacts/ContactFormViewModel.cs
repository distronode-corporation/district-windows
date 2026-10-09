using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DistrictAI.Core.Ffi;

namespace DistrictAI.ViewModels.Contacts;

/// <summary>Which form <see cref="ContactFormViewModel"/> is.</summary>
public enum ContactFormKind
{
    /// <summary>Adding a contact, from the contacts list.</summary>
    Create,

    /// <summary>Changing the open contact.</summary>
    Edit,
}

/// <summary>
/// The form adding a contact, or changing the open one. What is typed goes to
/// the core whole at every change, and the core decides whether it can be sent
/// and what to say about it; closing asks the core, which refuses while the
/// form is saving, so its answer always lands on a form.
/// </summary>
public sealed partial class ContactFormViewModel(ContactFormKind kind) : ObservableObject
{
    private PageContext? _context;

    /// <summary>Whether the fields are being filled from the core, not typed in.</summary>
    private bool _filling;

    /// <summary>Which form this is.</summary>
    public ContactFormKind Kind { get; } = kind;

    /// <summary>Whether the core holds the form open.</summary>
    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    /// <summary>The dialog's heading: "Add contact" or "Edit contact".</summary>
    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;

    /// <summary>The sending button's label: "Add" or "Save".</summary>
    [ObservableProperty]
    public partial string SubmitLabel { get; set; } = string.Empty;

    /// <summary>The name, as typed.</summary>
    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    /// <summary>The phone number, as typed.</summary>
    [ObservableProperty]
    public partial string PhoneNumber { get; set; } = string.Empty;

    /// <summary>The email address, as typed.</summary>
    [ObservableProperty]
    public partial string Email { get; set; } = string.Empty;

    /// <summary>The core's guidance under the form, or empty.</summary>
    [ObservableProperty]
    public partial string Hint { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="Hint"/>.</summary>
    [ObservableProperty]
    public partial bool HasHint { get; set; }

    /// <summary>Whether the sending button works now.</summary>
    [ObservableProperty]
    public partial bool CanSubmit { get; set; }

    /// <summary>Whether the form is on its way.</summary>
    [ObservableProperty]
    public partial bool Saving { get; set; }

    /// <summary>Whether the fields and Cancel take input: not while saving.</summary>
    [ObservableProperty]
    public partial bool Editable { get; set; } = true;

    /// <summary>Why the last attempt failed, or empty.</summary>
    [ObservableProperty]
    public partial string Failure { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="Failure"/>.</summary>
    [ObservableProperty]
    public partial bool HasFailure { get; set; }

    internal void Attach(PageContext context) => _context = context;

    /// <summary>
    /// Shows the core's form, or closes it. The fields are filled only as the
    /// form opens: after that the core holds exactly what was typed, so writing
    /// it back could only move the caret.
    /// </summary>
    internal void Show(ContactFormView? view)
    {
        if (view is null)
        {
            IsOpen = false;
            return;
        }
        if (!IsOpen)
        {
            _filling = true;
            Name = view.Form.Name;
            PhoneNumber = view.Form.PhoneNumber;
            Email = view.Form.Email;
            _filling = false;
        }
        Title = view.Title;
        SubmitLabel = view.SubmitLabel;
        Hint = view.Hint ?? string.Empty;
        HasHint = Hint.Length > 0;
        CanSubmit = view.CanSubmit;
        Saving = view.Saving;
        Editable = !view.Saving;
        Failure = Display.Failure(view.Failure);
        HasFailure = Failure.Length > 0;
        IsOpen = true;
    }

    partial void OnNameChanged(string value) => Typed();

    partial void OnPhoneNumberChanged(string value) => Typed();

    partial void OnEmailChanged(string value) => Typed();

    private void Typed()
    {
        if (_filling || !IsOpen)
        {
            return;
        }
        var form = new ContactFormInput(Name, PhoneNumber, Email);
        Send(Kind == ContactFormKind.Create
            ? new ContactsAction.EditCreate(form)
            : new ContactsAction.Edit(form));
    }

    /// <summary>"Add" or "Save", or Enter in a field. Once per press: the button turns off until the core answers.</summary>
    [RelayCommand]
    private void Submit()
    {
        if (!CanSubmit)
        {
            return;
        }
        CanSubmit = false;
        Send(Kind == ContactFormKind.Create ? new ContactsAction.SubmitCreate() : new ContactsAction.SaveEdit());
    }

    /// <summary>Cancel, Escape, or the dialog closed. The core refuses while the form is saving.</summary>
    [RelayCommand]
    private void Cancel() =>
        Send(Kind == ContactFormKind.Create ? new ContactsAction.CancelCreate() : new ContactsAction.CancelEdit());

    private void Send(ContactsAction action) => _context?.Send(new UiEvent.Contacts(action));
}
