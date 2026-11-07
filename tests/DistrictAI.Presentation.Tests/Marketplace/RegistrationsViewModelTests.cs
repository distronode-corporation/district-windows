using DistrictAI.Core;
using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels.Marketplace;
using Xunit;

namespace DistrictAI.Presentation.Tests.Marketplace;

/// <summary>The Registrations tab: the form, the requirements, the filings and what a draft offers.</summary>
public sealed class RegistrationsViewModelTests
{
    private static RegistrationFormInput Form(string country = "", string numberType = "local", string endUserType = "business", string name = "") =>
        new(country, numberType, endUserType, name);

    private static FilingView Draft(
        string id = "BU-1",
        bool armed = true,
        bool canFile = true,
        DetailFieldView[]? details = null,
        FilingDocumentView[]? documents = null) =>
        new(
            Id: id,
            Title: "DE · Local",
            FriendlyName: "Berlin office",
            Status: "Draft",
            Tone: FilingTone.Neutral,
            ReasonsEyebrow: null,
            Reasons: [],
            DocumentsEyebrow: documents is { Length: > 0 } ? "Documents" : null,
            Documents: documents ?? [],
            Draft: true,
            Details: details ?? [new DetailFieldView("business_name", string.Empty), new DetailFieldView("vat_number", string.Empty)],
            Uploads: [new UploadView("proof_of_address", "Upload Proof of address")],
            Armed: armed,
            FileAction: "File it",
            CanFile: canFile);

    private static FilingView Refused() =>
        new(
            Id: "BU-3",
            Title: "DE · Local",
            FriendlyName: null,
            Status: "Rejected",
            Tone: FilingTone.Danger,
            ReasonsEyebrow: "What the carrier objected to",
            Reasons: ["The proof of address is older than three months."],
            DocumentsEyebrow: null,
            Documents: [],
            Draft: false,
            Details: [],
            Uploads: [],
            Armed: false,
            FileAction: null,
            CanFile: false);

    internal static RegistrationsTabView Tab(
        bool member = true,
        RegistrationFormInput? form = null,
        bool canCheck = false,
        bool canStart = false,
        RequirementsView? requirements = null,
        LoadStatus? listStatus = null,
        FilingView[]? filings = null,
        string? notice = null,
        bool failed = false) =>
        new(
            Member: member,
            Eyebrow: "Regulatory registrations",
            StartTitle: "Start a registration",
            CountryLabel: "Country (two letters)",
            NameLabel: "Name for your carrier console (optional)",
            Form: form ?? Form(),
            NumberTypes: [new ChoiceView("local", "Local"), new ChoiceView("mobile", "Mobile")],
            EndUserTypes: [new ChoiceView("business", "Business"), new ChoiceView("individual", "Individual")],
            CheckAction: "Check requirements",
            CanCheck: canCheck,
            StartAction: "Start registration",
            CanStart: canStart,
            Notice: notice,
            Unrepeatable: null,
            Failed: failed,
            Working: false,
            Requirements: requirements ?? new RequirementsView.Idle(),
            ListStatus: listStatus ?? new LoadStatus.Ready(),
            StatusCaption: filings is { Length: > 0 } ? "Statuses are refreshed when this list loads." : null,
            Empty: filings is { Length: > 0 } ? null : new EmptyView("Nothing filed yet", "Start one."),
            Gates: [new GateView("Your approvals", "DE")],
            Filings: filings ?? []);

    private static (RegistrationsViewModel Model, RecordingSink Sink) Attached()
    {
        var model = new RegistrationsViewModel();
        var (context, sink) = Pages.Context();
        model.Attach(context);
        return (model, sink);
    }

    private static NumberAdminAction Action(UiEvent sent) =>
        Assert.IsType<MarketplaceAction.Numbers>(Assert.IsType<UiEvent.Marketplace>(sent).Action).Action;

    [Fact]
    public void TheFormGoesToTheCoreWholeAndItsButtonsFollowTheCore()
    {
        var (model, sink) = Attached();
        model.Show(Tab());
        Assert.True(model.Member);
        Assert.Equal(["Local", "Mobile"], model.NumberTypes);
        Assert.Equal(0, model.NumberTypeIndex);
        Assert.Equal(0, model.EndUserIndex);
        Assert.False(model.CheckRequirementsCommand.CanExecute(null));
        Assert.True(model.ListLoad.ShowEmpty);
        Assert.Single(model.Gates);
        Assert.Empty(sink.Sent);

        model.Country = "de";
        Assert.Equal(new NumberAdminAction.EditRegistration(Form("de")), Action(sink.Sent[^1]));
        model.EndUserIndex = 1;
        Assert.Equal(new NumberAdminAction.EditRegistration(Form("de", endUserType: "individual")), Action(sink.Sent[^1]));

        // The core's echo of what was sent leaves the fields as typed.
        model.Show(Tab(form: Form("de", endUserType: "individual"), canCheck: true, canStart: true));
        Assert.Equal("de", model.Country);
        model.CheckRequirementsCommand.Execute(null);
        model.StartCommand.Execute(null);
        Assert.Equal(new NumberAdminAction.CheckRequirements(), Action(sink.Sent[^2]));
        Assert.Equal(new NumberAdminAction.StartRegistration(), Action(sink.Sent[^1]));

        // A started draft empties the core's form, and the fields with it.
        model.Show(Tab(form: Form("de", endUserType: "individual")));
        var sent = sink.Sent.Count;
        model.Show(Tab(form: Form()));
        Assert.Equal(string.Empty, model.Country);
        Assert.Equal(0, model.EndUserIndex);
        Assert.Equal(sent, sink.Sent.Count);
    }

    [Fact]
    public void TheRequirementsShowEachWayTheLookupEnds()
    {
        var (model, sink) = Attached();
        model.Show(Tab(requirements: new RequirementsView.Loading("What this country asks for")));
        Assert.True(model.ShowRequirements && model.RequirementsLoading);

        model.Show(Tab(requirements: new RequirementsView.Failed("What this country asks for", new FailureView("The service did not answer.", null, true))));
        Assert.Equal("The service did not answer.", model.RequirementsFailure);
        Assert.True(model.HasRequirementsFailure && !model.HasRequirementsHeading);
        Assert.True(model.CanRetryRequirements);
        model.RetryRequirementsCommand.Execute(null);
        Assert.Equal(new NumberAdminAction.CheckRequirements(), Action(sink.Sent[^1]));

        model.Show(Tab(requirements: new RequirementsView.NoRegulation("What this country asks for", "US · No registration required", "Nothing to file.", "Not purchasable.")));
        Assert.Equal("US · No registration required", model.RequirementsHeading);
        Assert.Equal("Not purchasable.", model.RequirementsNote);
        Assert.Empty(model.Requirements);

        model.Show(Tab(requirements: new RequirementsView.Ready("What this country asks for", "Germany: Local - Business", null, [new RequirementRowView("Proof of address", "Documents · address")])));
        Assert.Equal("Germany: Local - Business", model.RequirementsHeading);
        Assert.Equal(string.Empty, model.RequirementsFailure);
        Assert.Equal(new RequirementItem("Proof of address", "Documents · address"), Assert.Single(model.Requirements));
        model.RetryRequirementsCommand.Execute(null);
        Assert.Single(sink.Sent);

        model.Show(Tab());
        Assert.False(model.ShowRequirements);
    }

    [Fact]
    public void ADraftOffersItsDetailsDocumentsAndFilingOnlyWhenTheCoreDoes()
    {
        var (model, sink) = Attached();
        model.Show(Tab(filings: [Draft(documents: [new FilingDocumentView("RD-1", "proof_of_address", "Stored, not yet filed", "Remove")]), Refused()]));
        Assert.Equal(2, model.Filings.Count);
        var draft = model.Filings[0];
        Assert.True(draft.Fileable && draft.CanFile && draft.HasDetails && draft.HasDocuments);
        Assert.Equal("Upload Proof of address", Assert.Single(draft.Uploads).Label);
        var refused = model.Filings[1];
        Assert.True(refused.Refused && refused.HasReasons && !refused.Fileable && !refused.HasFriendlyName);

        // Each detail goes with the draft's others; the list keeps the field
        // while the core echoes it.
        draft.Details[0].Value = "Acme GmbH";
        var edit = Assert.IsType<NumberAdminAction.EditRegistrationDetails>(Action(sink.Sent[^1]));
        Assert.Equal("BU-1", edit.BundleId);
        Assert.Equal(["Acme GmbH", string.Empty], edit.Details.Select(detail => detail.Value));
        model.Show(Tab(filings: [Draft(details: [new DetailFieldView("business_name", "Acme"), new DetailFieldView("vat_number", string.Empty)], documents: [new FilingDocumentView("RD-1", "proof_of_address", "Stored, not yet filed", "Remove")]), Refused()]));
        Assert.Same(draft, model.Filings[0]);
        Assert.Equal("Acme GmbH", model.Filings[0].Details[0].Value);

        model.Remove(draft.Documents[0]);
        Assert.Equal(new NumberAdminAction.RemoveRegistrationDocument("BU-1", "RD-1"), Action(sink.Sent[^1]));
        model.AskFile(draft);
        Assert.Equal(new NumberAdminAction.AskSubmitRegistration("BU-1"), Action(sink.Sent[^1]));
        var file = new PickedFileView("lease.pdf", 9, [0x25, 0x50, 0x44, 0x46, 0x2d]);
        model.Picked(draft.Uploads[0], [new PickedFile("lease.pdf", file)]);
        var upload = Assert.IsType<NumberAdminAction.UploadRegistrationDocument>(Action(sink.Sent[^1]));
        Assert.Equal(("BU-1", "proof_of_address"), (upload.BundleId, upload.RequirementName));
        var sent = sink.Sent.Count;
        model.Picked(draft.Uploads[0], [new PickedFile("lease.pdf", null)]);
        Assert.True(model.HasFailure);
        Assert.Equal(sent, sink.Sent.Count);
        model.Picked(draft.Uploads[0], []);
        Assert.Equal(sent, sink.Sent.Count);

        // A write on its way, or a filing that may have happened: nothing offered.
        model.Show(Tab(filings: [Draft(armed: false, canFile: false, documents: [new FilingDocumentView("RD-1", "proof_of_address", "Stored, not yet filed", "Remove")])]));
        var waiting = model.Filings[0];
        Assert.NotSame(draft, waiting);
        Assert.False(RegistrationsViewModel.MayPick(waiting.Uploads[0]));
        model.Remove(waiting.Documents[0]);
        model.AskFile(waiting);
        model.Picked(waiting.Uploads[0], [new PickedFile("lease.pdf", file)]);
        Assert.Equal(sent, sink.Sent.Count);
    }

    [Fact]
    public void AFailedWriteReadsAsOneAndAFailedListOffersItsReread()
    {
        var (model, sink) = Attached();
        model.Show(Tab(notice: "Filed. The carrier is reviewing it."));
        Assert.True(model.HasNotice && !model.HasFailure);
        model.Show(Tab(notice: "The service did not answer.", failed: true));
        Assert.True(model.HasFailure && !model.HasNotice);

        model.Show(Tab(listStatus: new LoadStatus.Failed(new FailureView("Busy.", null, true), "Could not load this workspace's registrations.")));
        Assert.True(model.ListLoad.Failed && model.ListLoad.CanRetry);
        model.ListLoad.RetryCommand.Execute(null);
        Assert.Equal(new NumberAdminAction.ReloadRegistrations(), Action(sink.Sent[^1]));
    }

    [Fact]
    public void TheTabShowsThroughTheAdminOnlyWhileItIsOpen()
    {
        var model = new NumberAdminViewModel();
        var (context, _) = Pages.Context();
        model.Attach(context);
        model.Show(N.Admin(registrations: Tab(member: false)));
        Assert.True(model.OnRegistrations);
        Assert.Equal("Registrations", model.RegistrationsTab);
        Assert.False(model.Registrations.Member);
        model.Show(N.Admin());
        Assert.False(model.OnRegistrations);
    }
}
