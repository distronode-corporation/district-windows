using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels;
using DistrictAI.ViewModels.Contacts;
using Xunit;

namespace DistrictAI.Presentation.Tests.Contacts;

/// <summary>The core's contact records, with defaults, so each test names only what it is about.</summary>
internal static class C
{
    public static ContactFormView Form(
        string title = "Add contact",
        string submit = "Add",
        string name = "",
        string phone = "",
        string email = "",
        string? hint = null,
        bool canSubmit = false,
        bool saving = false,
        FailureView? failure = null) =>
        new(title, submit, new ContactFormInput(name, phone, email), hint, canSubmit, saving, failure);

    public static ContactsView List(bool canCreate = true, ContactFormView? create = null) =>
        new(V.Ready, [], null, null, V.Paging(), canCreate, create);

    public static ContactWritesView Writes(
        bool edit = true,
        bool delete = true,
        bool enrich = true,
        bool clear = true,
        bool block = true) =>
        new(edit, delete, enrich, clear, block);

    public static ContactDetailView Detail(
        ContactWritesView? writes = null,
        bool blocked = false,
        string? busy = null,
        ContactQuestionView? confirming = null,
        ContactFormView? editing = null,
        FailureView? failure = null,
        LoadStatus? status = null) =>
        new(
            "c-1",
            status ?? V.Ready,
            "Alex",
            string.Empty,
            [],
            [],
            null,
            null,
            null,
            null,
            failure,
            ReportAvailability.Hidden,
            null,
            blocked,
            writes,
            busy,
            confirming,
            editing);
}

public sealed class ContactFormViewModelTests
{
    [Fact]
    public void ItFillsTheFieldsAsTheFormOpensAndThenLeavesThemToTheTyping()
    {
        var (context, sink) = Pages.Context();
        var form = new ContactFormViewModel(ContactFormKind.Edit);
        form.Attach(context);
        Assert.False(form.IsOpen);

        form.Show(C.Form("Edit contact", "Save", "Ada", "14165550142", "ada@example.com", canSubmit: true));
        Assert.True(form.IsOpen);
        Assert.Equal(("Edit contact", "Save"), (form.Title, form.SubmitLabel));
        Assert.Equal(("Ada", "14165550142", "ada@example.com"), (form.Name, form.PhoneNumber, form.Email));
        Assert.Empty(sink.Sent);

        form.Name = "Ada Lovelace";
        Assert.Equal(
            [new UiEvent.Contacts(new ContactsAction.Edit(new ContactFormInput("Ada Lovelace", "14165550142", "ada@example.com")))],
            sink.Sent);

        // The core's echo of an older keystroke does not overwrite what is typed.
        form.Show(C.Form("Edit contact", "Save", "Ada", "14165550142", "ada@example.com", canSubmit: true));
        Assert.Equal("Ada Lovelace", form.Name);

        form.Show(null);
        Assert.False(form.IsOpen);
        form.Email = "after@example.com";
        Assert.Single(sink.Sent);
    }

    [Fact]
    public void ItShowsTheHintTheFailureAndTheSaving()
    {
        var form = new ContactFormViewModel(ContactFormKind.Create);
        form.Show(C.Form(name: "Grace", hint: "Enter a phone number or an email address."));
        Assert.True(form.HasHint);
        Assert.False(form.CanSubmit);
        Assert.True(form.Editable);

        form.Show(C.Form(name: "Grace", phone: "+1", saving: true));
        Assert.False(form.HasHint);
        Assert.True(form.Saving);
        Assert.False(form.Editable);

        form.Show(C.Form(name: "Grace", phone: "+1", canSubmit: true, failure: V.Failure("Already exists.")));
        Assert.True(form.HasFailure);
        Assert.Equal("Already exists.", form.Failure);
        Assert.True(form.CanSubmit);
    }

    [Fact]
    public void EachFormSendsItsOwnEventsAndSubmitsOnce()
    {
        var (context, sink) = Pages.Context();
        var create = new ContactFormViewModel(ContactFormKind.Create);
        var edit = new ContactFormViewModel(ContactFormKind.Edit);
        create.Attach(context);
        edit.Attach(context);

        // Nothing to send before the core allows it.
        create.SubmitCommand.Execute(null);
        Assert.Empty(sink.Sent);

        create.Show(C.Form(canSubmit: true));
        create.PhoneNumber = "+1 416 555 0181";
        create.SubmitCommand.Execute(null);
        create.SubmitCommand.Execute(null);
        create.CancelCommand.Execute(null);
        edit.Show(C.Form(canSubmit: true));
        edit.SubmitCommand.Execute(null);
        edit.CancelCommand.Execute(null);

        Assert.Equal(
            [
                new UiEvent.Contacts(new ContactsAction.EditCreate(new ContactFormInput(string.Empty, "+1 416 555 0181", string.Empty))),
                new UiEvent.Contacts(new ContactsAction.SubmitCreate()),
                new UiEvent.Contacts(new ContactsAction.CancelCreate()),
                new UiEvent.Contacts(new ContactsAction.SaveEdit()),
                new UiEvent.Contacts(new ContactsAction.CancelEdit()),
            ],
            sink.Sent);
    }
}

public sealed class ContactsListWritesTests
{
    [Fact]
    public void AddContactOpensTheFormOnlyForAMemberWhoMayAndOnlyOnce()
    {
        var (context, sink) = Pages.Context();
        var contacts = new ContactsViewModel();
        contacts.Attach(context);

        contacts.Show(C.List(canCreate: false));
        Assert.False(contacts.CanCreate);
        contacts.AddContactCommand.Execute(null);
        Assert.Empty(sink.Sent);

        contacts.Show(C.List());
        contacts.AddContactCommand.Execute(null);
        contacts.Show(C.List(create: C.Form()));
        Assert.True(contacts.Create.IsOpen);
        contacts.AddContactCommand.Execute(null);
        contacts.OpenBlockedCommand.Execute(null);

        Assert.Equal(
            [
                new UiEvent.Contacts(new ContactsAction.StartCreate()),
                new UiEvent.Blocked(new BlockedAction.Open()),
            ],
            sink.Sent);
    }
}

public sealed class ContactChangesViewModelTests
{
    [Fact]
    public void AViewerSeesNoWriteControlsAndReadsWhy()
    {
        var detail = new ContactDetailViewModel();
        detail.Show(C.Detail(blocked: true), reportSending: false);
        var changes = detail.Changes;
        Assert.False(changes.Writable);
        Assert.True(changes.ReadOnly);
        Assert.True(changes.Blocked);
        Assert.False(changes.CanEdit || changes.CanDelete || changes.CanEnrich || changes.CanBlock || changes.OffersClearResearch);

        // Not while the contact is still loading.
        detail.Show(C.Detail(status: V.Loading), reportSending: false);
        Assert.False(changes.ReadOnly);
        Assert.False(string.IsNullOrEmpty(ContactChangesViewModel.ReadOnlyNote));
    }

    [Fact]
    public void AMemberGetsWhatTheCoreOffers()
    {
        var changes = new ContactChangesViewModel();
        changes.Show(C.Detail(C.Writes(enrich: false, clear: false)));
        Assert.True(changes.Writable);
        Assert.False(changes.ReadOnly);
        Assert.True(changes.CanEdit && changes.CanDelete && changes.CanBlock);
        Assert.False(changes.CanEnrich);
        Assert.False(changes.OffersClearResearch);
        Assert.Equal("Block", changes.BlockLabel);
        Assert.False(changes.Busy);

        changes.Show(C.Detail(C.Writes(), blocked: true, busy: "Deleting the contact"));
        Assert.Equal("Unblock", changes.BlockLabel);
        Assert.True(changes.Busy);
        Assert.Equal("Deleting the contact", changes.BusyText);
        Assert.True(changes.OffersClearResearch);
    }

    [Fact]
    public void EachControlSendsItsActionOnceUntilTheCoreAnswers()
    {
        var (context, sink) = Pages.Context();
        var changes = new ContactChangesViewModel();
        changes.Attach(context);
        changes.Show(C.Detail(C.Writes()));

        foreach (var command in new[]
        {
            changes.StartEditCommand,
            changes.AskDeleteCommand,
            changes.AskClearResearchCommand,
            changes.AskBlockCommand,
            changes.EnrichCommand,
        })
        {
            command.Execute(null);
            command.Execute(null);
        }
        changes.DismissFailureCommand.Execute(null);

        Assert.Equal(
            [
                new UiEvent.Contacts(new ContactsAction.StartEdit()),
                new UiEvent.Contacts(new ContactsAction.AskDelete()),
                new UiEvent.Contacts(new ContactsAction.AskClearResearch()),
                new UiEvent.Contacts(new ContactsAction.AskBlock()),
                new UiEvent.Contacts(new ContactsAction.Enrich()),
                new UiEvent.Contacts(new ContactsAction.DismissFailure()),
            ],
            sink.Sent);

        // The core's next view turns them on again.
        changes.Show(C.Detail(C.Writes()));
        Assert.True(changes.CanEdit && changes.CanDelete && changes.CanClearResearch && changes.CanBlock && changes.CanEnrich);
    }

    [Fact]
    public void TheQuestionIsTheCoresAndTheAnswerGoesBack()
    {
        var (context, sink) = Pages.Context();
        var changes = new ContactChangesViewModel();
        changes.Attach(context);
        changes.Show(C.Detail(C.Writes(), confirming: new ContactQuestionView("Delete this contact? This cannot be undone.", "Delete", true)));
        Assert.True(changes.Confirming);
        Assert.Equal("Delete this contact? This cannot be undone.", changes.ConfirmQuestion);
        Assert.Equal("Delete", changes.ConfirmAction);
        Assert.True(changes.ConfirmDestructive);

        changes.Answer(true);
        changes.Answer(false);
        Assert.Equal(
            [new UiEvent.Contacts(new ContactsAction.Confirm()), new UiEvent.Contacts(new ContactsAction.Cancel())],
            sink.Sent);

        changes.Show(C.Detail(C.Writes()));
        Assert.False(changes.Confirming);
        Assert.Equal(string.Empty, changes.ConfirmQuestion);
        Assert.False(changes.ConfirmDestructive);
    }

    [Fact]
    public void TheEditFormOpensFromTheCore()
    {
        var changes = new ContactChangesViewModel();
        changes.Show(C.Detail(C.Writes(), editing: C.Form("Edit contact", "Save", "Alex", "+1", string.Empty, canSubmit: true)));
        Assert.True(changes.Edit.IsOpen);
        Assert.Equal(ContactFormKind.Edit, changes.Edit.Kind);
        Assert.Equal("Alex", changes.Edit.Name);
        changes.Show(C.Detail(C.Writes()));
        Assert.False(changes.Edit.IsOpen);
    }
}
