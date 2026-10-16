using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels.Scheduling;
using Xunit;

namespace DistrictAI.Presentation.Tests.Scheduling;

public sealed class SchedulingEventTypesViewModelTests
{
    private static readonly SchedulingSectionEntryView[] _sections =
    [
        new(SchedulingAdminSection.Overview, "Overview", "Your booking desk", false),
        new(SchedulingAdminSection.EventTypes, "Event types", "The pages a customer can book", true),
        new(SchedulingAdminSection.Bookings, "Bookings", "Every booking", false),
    ];

    private static readonly LoadStatus _failed =
        new LoadStatus.Failed(new FailureView("Offline.", null, true), "Could not load this section");

    private static DialogView Dialog(
        string title = "Edit event type",
        string confirm = "Save",
        bool canConfirm = true,
        bool working = false,
        FailureView? failure = null) =>
        new(title, string.Empty, confirm, "Cancel", canConfirm, working, false, failure);

    private static EventTypeFormView Form(string name = "Phone consultation", string location = "phone") =>
        new(
            Name: name,
            Description: "A call.",
            Duration: "30",
            Interval: "15",
            Location: location,
            LocationValue: "+14165550142",
            IsActive: true,
            IsPublic: true,
            ShowTakenSlots: false,
            BufferBefore: "5",
            BufferAfter: "10",
            MinNotice: "120",
            MaxFutureDays: "60",
            MaxActiveBookings: "3");

    private static EventTypeEditorView Editor(bool creating = false, EventTypeFormView? form = null, DialogView? dialog = null, string? validation = null) =>
        new(
            Dialog: dialog ?? Dialog(),
            Creating: creating,
            Form: form ?? Form(),
            BookingPage: "phone-consultation",
            DurationShown: "30 min",
            Locations:
            [
                new("livekit", "Built-in video", false),
                new("phone", "Phone", true),
                new("in_person", "In person", false),
                new("link", "Link", false),
            ],
            LocationValueLabel: "Phone number",
            Validation: validation,
            NameLabel: "Name",
            NamePlaceholder: "Phone consultation",
            BookingPageLabel: "Booking page",
            BookingPageHint: "It cannot be changed.",
            DescriptionLabel: "Description",
            DescriptionHint: "Shown on the booking page.",
            DurationLabel: "Duration",
            DurationHint: "In minutes.",
            IntervalLabel: "Starts every",
            IntervalHint: "How often a booking may start.",
            LocationLabel: "Location",
            ActiveLabel: "Active",
            ListedLabel: "Listed on your booking page",
            ShowTakenLabel: "Show taken times as taken",
            BufferBeforeLabel: "Buffer before",
            BufferBeforeHint: "Minutes kept free before a booking.",
            BufferAfterLabel: "Buffer after",
            BufferAfterHint: "Minutes kept free after a booking.",
            MinNoticeLabel: "Minimum notice",
            MinNoticeHint: "Minutes.",
            MaxFutureLabel: "Bookable ahead",
            MaxFutureHint: "0 means no limit.",
            MaxActiveLabel: "Active bookings per attendee",
            MaxActiveHint: "0 means no limit.");

    private static SchedulingEventTypesView List(
        LoadStatus? status = null,
        EventTypeRowView[]? rows = null,
        bool offersCreate = true,
        EventTypeEditorView? create = null) =>
        new(
            Status: status ?? new LoadStatus.Ready(),
            Rows: rows ??
            [
                new("phone-consultation", "Phone consultation", "30 min · Phone", "Active", RunTone.Success, "https://book.example.com/book/phone-consultation"),
                new("site-visit", "Site visit", "60 min · In person", "Archived", RunTone.Neutral, "Not public"),
            ],
            Empty: new EmptyView("No event types yet", "Create one to be booked."),
            OffersCreate: offersCreate,
            CreateLabel: "Create event type",
            Create: create);

    private static SchedulingEventTypeView Detail(
        EventTypeEditorView? editor = null,
        EventTypeActionsView? actions = null,
        EventTypeHostsView? hosts = null,
        EventTypeQuestionsView? questions = null,
        LoadStatus? hostsStatus = null,
        bool offersWrites = true) =>
        new(
            Slug: "phone-consultation",
            Name: "Phone consultation",
            Status: new LoadStatus.Ready(),
            Description: "A call.",
            DetailsHeading: "Details",
            Facts: [new FactView("Duration", "30 min"), new FactView("State", "Active")],
            OffersWrites: offersWrites,
            EditLabel: "Edit",
            ManageLabel: "Manage",
            HostsLabel: "Hosts",
            QuestionsLabel: "Questions",
            HostsHeading: "Hosts",
            HostsStatus: hostsStatus ?? new LoadStatus.Ready(),
            Hosts: hostsStatus is null ? [new FactView("Contract Member", "Required")] : [],
            NoHosts: "No hosts are assigned.",
            QuestionsHeading: "Questions",
            QuestionsStatus: new LoadStatus.Ready(),
            Questions: [],
            NoQuestions: "This event type asks no questions.",
            Editor: editor,
            Actions: actions,
            HostsDialog: hosts,
            QuestionsDialog: questions);

    private static EventTypeActionsView Actions(string? notice = null, bool canAct = true, bool asking = false, FailureView? failure = null) =>
        new(
            Dialog: Dialog("Phone consultation", string.Empty, false, !canAct, failure),
            ToggleLabel: "Turn off",
            ArchiveLabel: "Archive",
            TestEmailHeading: "Send test email",
            TestEmails: [new("confirmation", "Confirmation", false), new("reminder", "Reminder", false)],
            DeleteLabel: "Delete event type",
            CanAct: canAct,
            Notice: notice,
            Delete: asking ? new SchedulingAskView("Delete event type?", "Its booking page stops working.", "Delete event type", "Cancel") : null);

    private static EventTypeHostsView Hosts(
        LoadStatus? status = null,
        EventTypeHostView[]? hosts = null,
        string? validation = null,
        LoadStatus? candidatesStatus = null) =>
        new(
            Dialog: Dialog("Hosts", "Save", status is null),
            Status: status ?? new LoadStatus.Ready(),
            RoutingLabel: "Routing",
            RoutingModes: [new("fixed", "Fixed", false), new("round_robin", "Round robin", true), new("collective", "Collective", false)],
            ShowsRotation: true,
            RotationLabel: "Rotation",
            RotationHint: "Which host the next booking goes to.",
            Rotations: [new("even", "Even", false), new("soonest", "Soonest", false), new("priority", "Priority", true)],
            Hosts: hosts ??
            [
                new("sched-user-contract", "Contract Member", "contract@example.com", "required", "0"),
                new("sched-user-rotation", "Rotation Host", "rotation@example.com", "rotation", "2"),
            ],
            Roles: [new("required", "Required", false), new("rotation", "Rotation", false), new("optional", "Optional", false)],
            Empty: "No hosts yet.",
            PriorityLabel: "Priority",
            RemoveLabel: "Remove",
            AddLabel: "Add",
            Candidates: candidatesStatus is null or LoadStatus.Ready ? [new("sched-user-2", "sam@example.com", false)] : [],
            // The core reads who could be added only once the hosts are read.
            CandidatesStatus: candidatesStatus ?? (status is null ? new LoadStatus.Ready() : new LoadStatus.Loading()),
            Validation: validation);

    private static QuestionEditorView QuestionForm(string? id = null, string kind = "select", string[]? options = null, bool canSave = true) =>
        new(
            Title: id is null ? "Add question" : "Edit question",
            Form: new QuestionFormView(id, "Which office?", kind, options ?? ["Toronto"], true, "2"),
            Kinds: [new("text", "Text", kind == "text"), new("select", "Select", kind == "select"), new("checkbox", "Checkbox", kind == "checkbox")],
            ShowsOptions: kind == "select",
            LabelLabel: "Label",
            LabelPlaceholder: "What would you like to talk about?",
            TypeLabel: "Type",
            RequiredLabel: "Required",
            PositionLabel: "Position",
            PositionHint: "Lower numbers are asked first.",
            OptionsLabel: "Options",
            OptionsHint: "What the customer can choose from.",
            OptionPlaceholder: "Add an option",
            AddOptionLabel: "Add option",
            RemoveLabel: "Remove",
            SaveLabel: id is null ? "Add question" : "Save question",
            CancelLabel: "Cancel",
            CanSave: canSave,
            Validation: null);

    private static EventTypeQuestionsView Questions(
        LoadStatus? status = null,
        QuestionEditorView? form = null,
        bool asking = false,
        string? notice = null,
        bool canAdd = true) =>
        new(
            Dialog: Dialog("Questions", string.Empty, false),
            Status: status ?? new LoadStatus.Ready(),
            Questions:
            [
                new("q-1", "Which product are you asking about?", "Select · required"),
                new("q-2", "Anything we should read first?", "Text"),
            ],
            Empty: "No questions yet.",
            AddLabel: "Add question",
            CanAdd: canAdd,
            EditLabel: "Edit",
            DeleteLabel: "Delete",
            Notice: notice,
            Form: form,
            Delete: asking ? new SchedulingAskView("Delete question?", "Customers stop being asked it.", "Delete question", "Cancel") : null);

    private static SchedulingAdminView Page(SchedulingAdminPage page, string title = "Event types") =>
        new(title, _sections, page);

    private static (SchedulingAdminViewModel Model, RecordingSink Sink) Attached()
    {
        var model = new SchedulingAdminViewModel();
        var (context, sink) = Pages.Context();
        model.Attach(context);
        return (model, sink);
    }

    private static UiEvent.SchedulingAdmin ListEvent(EventTypesAction action) =>
        new(new SchedulingAdminAction.EventTypes(action));

    private static UiEvent.SchedulingAdmin DetailEvent(EventTypeAction action) =>
        new(new SchedulingAdminAction.EventType(action));

    /// <summary>The detail action <paramref name="sent"/> carries.</summary>
    private static EventTypeAction DetailAction(UiEvent sent) =>
        Assert.IsType<SchedulingAdminAction.EventType>(Assert.IsType<UiEvent.SchedulingAdmin>(sent).Action).Action;

    [Fact]
    public void TheListShowsEachEventTypeAndOpensIt()
    {
        var (model, sink) = Attached();
        model.Show(Page(new SchedulingAdminPage.EventTypes(List())));
        var vm = model.EventTypes;
        Assert.True(model.ShowsEventTypes);
        Assert.False(model.ShowsEventType || model.ShowsBookings || model.ShowsOverview);
        Assert.Equal(1, model.SelectedSection);
        Assert.True(vm.Load.Ready);
        Assert.Equal(2, vm.Rows.Count);
        var row = vm.Rows[0];
        Assert.True(row.IsSuccess);
        Assert.False(row.IsNeutral);
        Assert.True(vm.Rows[1].IsNeutral);
        Assert.Equal("EventTypeRowphone-consultation", row.AutomationId);
        Assert.Equal(
            "Phone consultation, 30 min · Phone, Active, https://book.example.com/book/phone-consultation",
            row.ToString());
        Assert.Equal("Create event type", vm.CreateLabel);
        Assert.True(vm.OpenCreateCommand.CanExecute(null));
        vm.Open(row);
        vm.OpenCreateCommand.Execute(null);
        Assert.Equal(
            [
                new UiEvent.SchedulingAdmin(new SchedulingAdminAction.OpenEventType("phone-consultation")),
                ListEvent(new EventTypesAction.OpenCreate()),
            ],
            sink.Sent);
    }

    [Fact]
    public void AFailedListIsAStatusPageWithNoCreateAndAnEmptyOneSaysSo()
    {
        var (model, _) = Attached();
        model.Show(Page(new SchedulingAdminPage.EventTypes(List(status: _failed, rows: [], offersCreate: false))));
        Assert.True(model.EventTypes.Load.Failed);
        Assert.True(model.EventTypes.Load.CanRetry);
        Assert.False(model.EventTypes.OpenCreateCommand.CanExecute(null));
        model.Show(Page(new SchedulingAdminPage.EventTypes(List(rows: []))));
        Assert.True(model.EventTypes.Load.ShowEmpty);
        Assert.Equal("No event types yet", model.EventTypes.Load.EmptyTitle);
    }

    [Fact]
    public void TheCreateFormIsTheShortOneAndSendsTheWholeForm()
    {
        var (model, sink) = Attached();
        var create = Editor(creating: true, dialog: Dialog("Create event type"), form: Form(name: string.Empty, location: "livekit"));
        model.Show(Page(new SchedulingAdminPage.EventTypes(List(create: create with
        {
            Locations = [new("livekit", "Built-in video", true), new("phone", "Phone", false)],
            LocationValueLabel = null,
        }))));
        var vm = model.EventTypes.Create;
        Assert.True(vm.Dialog.IsOpen);
        Assert.True(vm.Creating);
        Assert.False(vm.ShowsAll);
        Assert.False(vm.HasLocationValue);
        Assert.Equal("Create event type", vm.Dialog.Title);
        Assert.Equal("Name", vm.Words?.NameLabel);
        Assert.Equal(0, vm.LocationIndex);
        Assert.Equal(2, vm.Locations.Count);
        Assert.Equal("Built-in video", vm.Locations[0].ToString());
        Assert.Empty(sink.Sent);

        vm.Name = "Intro call";
        vm.PickLocation(0);
        vm.PickLocation(9);
        vm.PickLocation(1);
        Assert.Equal(
            [
                ListEvent(new EventTypesAction.EditForm(Form(name: "Intro call", location: "livekit"))),
                ListEvent(new EventTypesAction.EditForm(Form(name: "Intro call", location: "phone"))),
            ],
            sink.Sent);

        // The core's next snapshot does not write over what is typed.
        model.Show(Page(new SchedulingAdminPage.EventTypes(List(create: create with { Validation = "Give the event type a name." }))));
        Assert.Equal("Intro call", vm.Name);
        Assert.True(vm.HasValidation);
        Assert.Equal("Give the event type a name.", vm.Validation);
        vm.Dialog.ConfirmCommand.Execute(null);
        vm.Dialog.CloseCommand.Execute(null);
        Assert.Equal(ListEvent(new EventTypesAction.Save()), sink.Sent[^2]);
        Assert.Equal(ListEvent(new EventTypesAction.CloseEditor()), sink.Sent[^1]);

        model.Show(Page(new SchedulingAdminPage.EventTypes(List())));
        Assert.False(vm.Dialog.IsOpen);
        var count = sink.Sent.Count;
        vm.Name = "Closed";
        Assert.Equal(count, sink.Sent.Count);
    }

    [Fact]
    public void OneEventTypeShowsItsDetailsHostsAndQuestionsAndOpensItsWrites()
    {
        var (model, sink) = Attached();
        model.Show(Page(new SchedulingAdminPage.EventType(Detail()), "Event type"));
        var vm = model.EventType;
        Assert.True(model.ShowsEventType);
        Assert.False(model.ShowsEventTypes);
        Assert.Equal("Phone consultation", vm.Name);
        Assert.True(vm.HasDescription);
        Assert.Equal("A call.", vm.Description);
        Assert.Equal("Details", vm.DetailsHeading);
        Assert.Equal(2, vm.Facts.Count);
        Assert.Equal("Hosts", vm.HostsHeading);
        Assert.Equal("Contract Member: Required", Assert.Single(vm.Hosts).ToString());
        Assert.False(vm.HostsLoading || vm.HasHostsFailure || vm.ShowNoHosts);
        Assert.Equal("Questions", vm.QuestionsHeading);
        Assert.True(vm.ShowNoQuestions);
        Assert.Equal("This event type asks no questions.", vm.NoQuestions);
        Assert.Equal("Edit", vm.EditLabel);
        Assert.Equal("Manage", vm.ManageLabel);
        Assert.Equal("Hosts", vm.HostsLabel);
        Assert.Equal("Questions", vm.QuestionsLabel);
        vm.OpenEditorCommand.Execute(null);
        vm.OpenActionsCommand.Execute(null);
        vm.OpenHostsCommand.Execute(null);
        vm.OpenQuestionsCommand.Execute(null);
        Assert.Equal(
            [
                DetailEvent(new EventTypeAction.OpenEditor()),
                DetailEvent(new EventTypeAction.OpenActions()),
                DetailEvent(new EventTypeAction.OpenHosts()),
                DetailEvent(new EventTypeAction.OpenQuestions()),
            ],
            sink.Sent);

        model.Show(Page(new SchedulingAdminPage.EventType(Detail(hostsStatus: _failed, offersWrites: false) with
        {
            Description = string.Empty,
            QuestionsStatus = new LoadStatus.Loading(),
        }), "Event type"));
        Assert.False(vm.HasDescription);
        Assert.True(vm.HasHostsFailure);
        Assert.Equal("Offline.", vm.HostsFailure);
        Assert.Empty(vm.Hosts);
        Assert.False(vm.ShowNoHosts);
        Assert.True(vm.QuestionsLoading);
        Assert.False(vm.ShowNoQuestions);
        Assert.False(vm.OpenEditorCommand.CanExecute(null));

        model.Show(Page(new SchedulingAdminPage.EventType(Detail(hostsStatus: new LoadStatus.Ready()) with
        {
            QuestionsStatus = _failed,
        }), "Event type"));
        Assert.True(vm.ShowNoHosts);
        Assert.True(vm.HasQuestionsFailure);
    }

    [Fact]
    public void TheEditorIsWrittenOnceAsItOpensAndSendsEveryField()
    {
        var (model, sink) = Attached();
        model.Show(Page(new SchedulingAdminPage.EventType(Detail(editor: Editor())), "Event type"));
        var vm = model.EventType.Editor;
        Assert.True(vm.Dialog.IsOpen);
        Assert.True(vm.ShowsAll);
        Assert.True(vm.HasLocationValue);
        Assert.Equal("Phone number", vm.LocationValueLabel);
        Assert.Equal("phone-consultation", vm.BookingPage);
        Assert.Equal("30 min", vm.DurationShown);
        Assert.Equal(Form(), vm.Form());
        vm.Description = "Longer.";
        vm.Duration = "45";
        vm.Interval = "45";
        vm.LocationValue = "+14165550199";
        vm.IsActive = false;
        vm.IsPublic = false;
        vm.ShowTakenSlots = true;
        vm.BufferBefore = "0";
        vm.BufferAfter = "0";
        vm.MinNotice = "0";
        vm.MaxFutureDays = "0";
        vm.MaxActiveBookings = "0";
        Assert.Equal(12, sink.Sent.Count);
        var last = Assert.IsType<EventTypeAction.EditForm>(DetailAction(sink.Sent[^1]));
        Assert.Equal(
            new EventTypeFormView("Phone consultation", "Longer.", "45", "45", "phone", "+14165550199", false, false, true, "0", "0", "0", "0", "0"),
            last.Form);

        model.Show(Page(new SchedulingAdminPage.EventType(Detail(editor: Editor(dialog: Dialog(confirm: "Saving…", canConfirm: false, working: true)))), "Event type"));
        Assert.Equal("Longer.", vm.Description);
        Assert.True(vm.Dialog.Working);
        Assert.Equal("Saving…", vm.Dialog.ConfirmText);
        model.Show(Page(new SchedulingAdminPage.EventType(Detail(editor: Editor(dialog: Dialog(failure: new FailureView("That did not save.", null, true))))), "Event type"));
        Assert.True(vm.Dialog.HasFailure);
        vm.Dialog.ConfirmCommand.Execute(null);
        Assert.Equal(DetailEvent(new EventTypeAction.Save()), sink.Sent[^1]);
        model.Show(Page(new SchedulingAdminPage.EventType(Detail()), "Event type"));
        Assert.False(vm.Dialog.IsOpen);
    }

    [Fact]
    public void TheStateActionsArePressedOnceAndTheDeleteIsAskedInPlace()
    {
        var (model, sink) = Attached();
        model.Show(Page(new SchedulingAdminPage.EventType(Detail(actions: Actions())), "Event type"));
        var vm = model.EventType.Actions;
        Assert.True(vm.Dialog.IsOpen);
        Assert.Equal("Phone consultation", vm.Dialog.Title);
        Assert.Equal(string.Empty, vm.Dialog.ConfirmText);
        Assert.False(vm.Dialog.ConfirmCommand.CanExecute(null));
        Assert.Equal("Turn off", vm.ToggleLabel);
        Assert.Equal("Archive", vm.ArchiveLabel);
        Assert.Equal("Send test email", vm.TestEmailHeading);
        Assert.Equal("Delete event type", vm.DeleteLabel);
        Assert.Equal(2, vm.TestEmails.Count);
        Assert.False(vm.Asking || vm.HasNotice);
        vm.ToggleCommand.Execute(null);
        Assert.False(vm.CanAct);
        Assert.False(vm.ArchiveCommand.CanExecute(null));
        vm.SendTestEmail(vm.TestEmails[0]);

        model.Show(Page(new SchedulingAdminPage.EventType(Detail(actions: Actions(notice: "Event type turned off"))), "Event type"));
        Assert.True(vm.HasNotice);
        Assert.Equal("Event type turned off", vm.Notice);
        vm.DismissNoticeCommand.Execute(null);
        vm.SendTestEmail(vm.TestEmails[1]);
        model.Show(Page(new SchedulingAdminPage.EventType(Detail(actions: Actions())), "Event type"));
        vm.ArchiveCommand.Execute(null);
        model.Show(Page(new SchedulingAdminPage.EventType(Detail(actions: Actions())), "Event type"));
        vm.ConfirmDeleteCommand.Execute(null);
        vm.AskDeleteCommand.Execute(null);

        model.Show(Page(new SchedulingAdminPage.EventType(Detail(actions: Actions(asking: true))), "Event type"));
        Assert.True(vm.Asking);
        Assert.Equal("Delete event type?", vm.AskTitle);
        Assert.Equal("Its booking page stops working.", vm.AskBody);
        Assert.Equal("Delete event type", vm.AskConfirm);
        Assert.Equal("Cancel", vm.AskCancel);
        vm.CancelDeleteCommand.Execute(null);
        vm.ConfirmDeleteCommand.Execute(null);
        Assert.Equal(
            [
                DetailEvent(new EventTypeAction.ToggleActive()),
                DetailEvent(new EventTypeAction.DismissNotice()),
                DetailEvent(new EventTypeAction.SendTestEmail("reminder")),
                DetailEvent(new EventTypeAction.ToggleArchived()),
                DetailEvent(new EventTypeAction.AskDelete()),
                DetailEvent(new EventTypeAction.CancelDelete()),
                DetailEvent(new EventTypeAction.ConfirmDelete()),
            ],
            sink.Sent);

        // A delete that landed goes back to the list: the dialog closes.
        model.Show(Page(new SchedulingAdminPage.EventTypes(List())));
        Assert.False(vm.Dialog.IsOpen);
    }

    [Fact]
    public void TheHostsAreEditedRowByRowAndSavedOnceRead()
    {
        var (model, sink) = Attached();
        model.Show(Page(new SchedulingAdminPage.EventType(Detail(hosts: Hosts(status: new LoadStatus.Loading(), hosts: []))), "Event type"));
        var vm = model.EventType.HostsEditor;
        Assert.True(vm.Dialog.IsOpen);
        Assert.True(vm.Loading);
        Assert.False(vm.Ready || vm.ShowEmpty || vm.HasCandidates);
        Assert.False(vm.CandidatesLoad.Loading, "who could be added waits for the hosts");
        Assert.False(vm.CanRetry || vm.CandidatesLoad.CanRetry);
        Assert.False(vm.Dialog.ConfirmCommand.CanExecute(null));

        model.Show(Page(new SchedulingAdminPage.EventType(Detail(hosts: Hosts())), "Event type"));
        Assert.True(vm.Ready);
        Assert.Equal("Routing", vm.RoutingLabel);
        Assert.Equal(1, vm.RoutingIndex);
        Assert.True(vm.ShowsRotation);
        Assert.Equal("Rotation", vm.RotationLabel);
        Assert.Equal("Which host the next booking goes to.", vm.RotationHint);
        Assert.Equal(2, vm.RotationIndex);
        Assert.Equal(2, vm.Hosts.Count);
        var host = vm.Hosts[1];
        Assert.Equal(1, host.RoleIndex);
        Assert.Equal(["Required", "Rotation", "Optional"], host.Roles);
        Assert.Equal("EventTypeHostsched-user-rotation", host.AutomationId);
        Assert.Equal("Role: Rotation Host", host.RoleName);
        Assert.Equal("Priority: Rotation Host", host.PriorityName);
        Assert.Equal("Remove: Rotation Host", host.RemoveName);
        Assert.Equal("Rotation Host, rotation@example.com", host.ToString());
        var candidate = Assert.Single(vm.Candidates);
        Assert.True(vm.HasCandidates);
        Assert.Equal("Add: sam@example.com", candidate.AddName);
        Assert.Equal("sam@example.com", candidate.ToString());

        // What the row already holds sends nothing.
        vm.PickRouting(1);
        vm.PickRotation(7);
        vm.SetRole(host, 1);
        vm.SetRole(host, 9);
        vm.SetPriority(host, "2");
        vm.SetPriority(host with { UserId = "someone-else" }, "4");
        Assert.Empty(sink.Sent);

        vm.PickRouting(0);
        vm.PickRotation(0);
        vm.SetRole(host, 2);
        vm.SetPriority(host, "3");
        vm.Remove(vm.Hosts[0]);
        vm.Add(candidate);
        vm.Dialog.ConfirmCommand.Execute(null);
        Assert.Equal(
            [
                DetailEvent(new EventTypeAction.SetRoutingMode("fixed")),
                DetailEvent(new EventTypeAction.SetRotation("even")),
                DetailEvent(new EventTypeAction.SetHostRole("sched-user-rotation", "optional")),
                DetailEvent(new EventTypeAction.SetHostPriority("sched-user-rotation", "3")),
                DetailEvent(new EventTypeAction.RemoveHost("sched-user-contract")),
                DetailEvent(new EventTypeAction.AddHost("sched-user-2")),
                DetailEvent(new EventTypeAction.SaveHosts()),
            ],
            sink.Sent);

        // The rows stay while nobody comes or goes, and are built again when someone does.
        var shown = vm.Hosts[0];
        model.Show(Page(new SchedulingAdminPage.EventType(Detail(hosts: Hosts(validation: "A host's priority has to be a whole number, 0 or more."))), "Event type"));
        Assert.Same(shown, vm.Hosts[0]);
        Assert.True(vm.HasValidation);
        model.Show(Page(new SchedulingAdminPage.EventType(Detail(hosts: Hosts(hosts: []))), "Event type"));
        Assert.Empty(vm.Hosts);
        Assert.True(vm.ShowEmpty);
        Assert.Equal("No hosts yet.", vm.EmptyText);

        model.Show(Page(new SchedulingAdminPage.EventType(Detail(hosts: Hosts(status: _failed, hosts: []))), "Event type"));
        Assert.True(vm.Failed);
        Assert.Equal("Offline.", vm.FailureMessage);
        Assert.False(vm.CandidatesLoad.Loading || vm.CandidatesLoad.Failed);
        model.Show(Page(new SchedulingAdminPage.EventType(Detail()), "Event type"));
        Assert.False(vm.Dialog.IsOpen);
    }

    [Fact]
    public void TheHostsDialogTriesAgainWhatFailedToRead()
    {
        var (model, sink) = Attached();
        var vm = model.EventType.HostsEditor;

        // The hosts' read failed: Try again reads them again, once.
        model.Show(Page(new SchedulingAdminPage.EventType(Detail(hosts: Hosts(status: _failed, hosts: []))), "Event type"));
        Assert.True(vm.Failed);
        Assert.True(vm.CanRetry);
        vm.RetryCommand.Execute(null);
        Assert.Equal(DetailEvent(new EventTypeAction.RetryDialogRead()), sink.Sent[^1]);
        Assert.False(vm.RetryCommand.CanExecute(null));
        model.Show(Page(new SchedulingAdminPage.EventType(Detail(hosts: Hosts(status: new LoadStatus.Loading(), hosts: []))), "Event type"));
        Assert.True(vm.Loading);
        Assert.False(vm.CanRetry);

        // The hosts are read; who could be added is on its way.
        model.Show(Page(new SchedulingAdminPage.EventType(Detail(hosts: Hosts(candidatesStatus: new LoadStatus.Loading()))), "Event type"));
        Assert.True(vm.Ready);
        Assert.True(vm.CandidatesLoad.Loading);
        Assert.False(vm.HasCandidates);

        // Their read failed: the hosts stay editable, and Try again reads them again.
        model.Show(Page(new SchedulingAdminPage.EventType(Detail(hosts: Hosts(candidatesStatus: _failed))), "Event type"));
        Assert.True(vm.Ready);
        Assert.False(vm.Failed);
        Assert.True(vm.CandidatesLoad.Failed);
        Assert.Equal("Offline.", vm.CandidatesLoad.FailureMessage);
        Assert.Equal(2, vm.Hosts.Count);
        Assert.False(vm.HasCandidates);
        Assert.True(vm.Dialog.ConfirmCommand.CanExecute(null));
        Assert.True(vm.CandidatesLoad.CanRetry);
        var sent = sink.Sent.Count;
        vm.CandidatesLoad.RetryCommand.Execute(null);
        Assert.Equal(sent + 1, sink.Sent.Count);
        Assert.Equal(DetailEvent(new EventTypeAction.RetryDialogRead()), sink.Sent[^1]);

        // Read again: the users show, and nothing is left to try again.
        model.Show(Page(new SchedulingAdminPage.EventType(Detail(hosts: Hosts())), "Event type"));
        Assert.True(vm.CandidatesLoad.Ready);
        Assert.False(vm.CandidatesLoad.CanRetry);
        Assert.Single(vm.Candidates);
    }

    [Fact]
    public void TheQuestionsListOpenTheirFormAndAskBeforeADelete()
    {
        var (model, sink) = Attached();
        model.Show(Page(new SchedulingAdminPage.EventType(Detail(questions: Questions(status: new LoadStatus.Loading(), canAdd: false))), "Event type"));
        var vm = model.EventType.QuestionsEditor;
        Assert.True(vm.Dialog.IsOpen);
        Assert.True(vm.Loading);
        Assert.False(vm.AddQuestionCommand.CanExecute(null));

        model.Show(Page(new SchedulingAdminPage.EventType(Detail(questions: Questions())), "Event type"));
        Assert.True(vm.ShowsList);
        Assert.False(vm.Editing || vm.ShowEmpty || vm.Asking || vm.HasNotice);
        Assert.Equal("Add question", vm.AddLabel);
        Assert.Equal(2, vm.Questions.Count);
        var question = vm.Questions[0];
        Assert.Equal("EventTypeQuestionq-1", question.AutomationId);
        Assert.Equal("Edit: Which product are you asking about?", question.EditName);
        Assert.Equal("Delete: Which product are you asking about?", question.DeleteName);
        Assert.Equal("Which product are you asking about?: Select · required", question.ToString());
        vm.AddQuestionCommand.Execute(null);
        vm.Edit(question);
        vm.AskDelete(question);

        model.Show(Page(new SchedulingAdminPage.EventType(Detail(questions: Questions(form: QuestionForm()))), "Event type"));
        Assert.True(vm.Editing);
        Assert.False(vm.ShowsList);
        Assert.Equal("Add question", vm.Words?.Title);
        Assert.Equal("Which office?", vm.Label);
        Assert.Equal(1, vm.KindIndex);
        Assert.True(vm.Required);
        Assert.Equal("2", vm.Position);
        Assert.True(vm.ShowsOptions);
        var option = Assert.Single(vm.Options);
        Assert.Equal("Remove: Toronto", option.RemoveName);
        Assert.Equal("Toronto", option.ToString());
        Assert.Equal("Add question", vm.SaveLabel);
        vm.Label = "Which city?";
        vm.PickKind(1);
        vm.PickKind(2);
        vm.Required = false;
        vm.Position = "3";
        vm.NewOption = "  ";
        vm.AddOptionCommand.Execute(null);
        vm.NewOption = "Ottawa";
        vm.AddOptionCommand.Execute(null);
        Assert.Equal(string.Empty, vm.NewOption);
        vm.RemoveOption(option);
        vm.SaveQuestionCommand.Execute(null);
        Assert.False(vm.CanSave);
        vm.CancelQuestionCommand.Execute(null);
        var sent = sink.Sent.Skip(3).Select(DetailAction).ToList();
        Assert.Equal(8, sent.Count);
        var edited = Assert.IsType<EventTypeAction.EditQuestionForm>(sent[0]).Form;
        Assert.Equal(("Which city?", "select", true, "2"), (edited.Label, edited.Kind, edited.Required, edited.Position));
        Assert.Null(edited.Id);
        Assert.Equal(["Toronto"], edited.Options);
        Assert.Equal("checkbox", Assert.IsType<EventTypeAction.EditQuestionForm>(sent[1]).Form.Kind);
        Assert.False(Assert.IsType<EventTypeAction.EditQuestionForm>(sent[2]).Form.Required);
        Assert.Equal("3", Assert.IsType<EventTypeAction.EditQuestionForm>(sent[3]).Form.Position);
        Assert.Equal(new EventTypeAction.AddOption("Ottawa"), sent[4]);
        Assert.Equal(new EventTypeAction.RemoveOption("Toronto"), sent[5]);
        Assert.Equal(new EventTypeAction.SaveQuestion(), sent[6]);
        Assert.Equal(new EventTypeAction.CancelQuestion(), sent[7]);
        Assert.Equal(
            [new EventTypeAction.AddQuestion(), new EventTypeAction.EditQuestion("q-1"), new EventTypeAction.AskDeleteQuestion("q-1")],
            sink.Sent.Take(3).Select(DetailAction));

        // The fields are not written over while the form stays on one question.
        model.Show(Page(new SchedulingAdminPage.EventType(Detail(questions: Questions(form: QuestionForm(options: [], canSave: true) with
        {
            Validation = "A Select question needs at least one option.",
        }))), "Event type"));
        Assert.Equal("Which city?", vm.Label);
        Assert.Empty(vm.Options);
        Assert.True(vm.HasValidation);
        Assert.True(vm.CanSave);

        // Another question's form is written afresh.
        model.Show(Page(new SchedulingAdminPage.EventType(Detail(questions: Questions(form: QuestionForm(id: "q-2", kind: "text")))), "Event type"));
        Assert.Equal("Which office?", vm.Label);
        Assert.Equal(0, vm.KindIndex);
        Assert.False(vm.ShowsOptions);
        Assert.Equal("Save question", vm.SaveLabel);

        model.Show(Page(new SchedulingAdminPage.EventType(Detail(questions: Questions(asking: true, notice: "Question added"))), "Event type"));
        Assert.True(vm.ShowsList);
        Assert.True(vm.Asking);
        Assert.Equal("Delete question?", vm.AskTitle);
        Assert.Equal("Customers stop being asked it.", vm.AskBody);
        Assert.Equal("Delete question", vm.AskConfirm);
        Assert.Equal("Cancel", vm.AskCancel);
        Assert.True(vm.HasNotice);
        Assert.Equal("Question added", vm.Notice);
        var before = sink.Sent.Count;
        vm.Label = "Not editing";
        Assert.Equal(before, sink.Sent.Count);
        vm.CancelDeleteCommand.Execute(null);
        vm.ConfirmDeleteCommand.Execute(null);
        vm.ConfirmDeleteCommand.Execute(null);
        vm.DismissNoticeCommand.Execute(null);
        Assert.Equal(
            [new EventTypeAction.CancelDeleteQuestion(), new EventTypeAction.ConfirmDeleteQuestion(), new EventTypeAction.DismissNotice()],
            sink.Sent.Skip(before).Select(DetailAction));

        model.Show(Page(new SchedulingAdminPage.EventType(Detail(questions: Questions(status: _failed))), "Event type"));
        Assert.True(vm.Failed);
        Assert.Equal("Offline.", vm.FailureMessage);
        model.Show(Page(new SchedulingAdminPage.EventTypes(List())));
        Assert.False(vm.Dialog.IsOpen);
        Assert.True(vm.ShowsList);
    }

    [Fact]
    public void NothingIsSentBeforeAttaching()
    {
        var model = new SchedulingAdminViewModel();
        model.Show(Page(new SchedulingAdminPage.EventTypes(List(create: Editor(creating: true)))));
        model.EventTypes.Open(model.EventTypes.Rows[0]);
        model.EventTypes.Create.Name = "x";
        model.Show(Page(new SchedulingAdminPage.EventType(Detail(hosts: Hosts())), "Event type"));
        model.EventType.Send(new EventTypeAction.OpenEditor());
        model.EventType.HostsEditor.PickRouting(0);
        var (context, sink) = Pages.Context();
        model.Attach(context);
        Assert.Empty(sink.Sent);
    }
}
