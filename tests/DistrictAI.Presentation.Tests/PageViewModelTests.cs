using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels;
using Xunit;

namespace DistrictAI.Presentation.Tests;

public sealed class OverviewViewModelTests
{
    private static OverviewView View(
        LoadStatus? status = null,
        CallRowView[]? recent = null,
        string? recentEmpty = null,
        string? badge = null,
        FinishSetupView? finishSetup = null) =>
        new(status ?? V.Ready, "Bravo", [new FactView("Calls", "12")], recent ?? [], recentEmpty, badge, finishSetup, false, null);

    [Fact]
    public void ItShowsTheWorkspaceItsNumbersAndTheFinishSetupCard()
    {
        var overview = new OverviewViewModel();
        overview.Show(View(recent: [V.CallRow()], badge: "Read-only access", finishSetup: new FinishSetupView("Finish setting up", "On the web.", "Open")));

        Assert.True(overview.Load.Ready);
        Assert.Equal("Bravo", overview.WorkspaceName);
        Assert.Equal("Read-only access", overview.ReadOnlyBadge);
        Assert.True(overview.HasReadOnlyBadge);
        Assert.True(overview.HasFinishSetup);
        Assert.Equal("Finish setting up", overview.FinishSetupTitle);
        Assert.Equal("On the web.", overview.FinishSetupBody);
        Assert.Equal("Open", overview.FinishSetupAction);
        Assert.Equal([new FactItem("Calls", "12")], overview.Metrics);
        Assert.Single(overview.RecentCalls);
        Assert.False(overview.NoRecentCalls);
    }

    [Fact]
    public void NoBadgeNoCardAndNoCallsYet()
    {
        var overview = new OverviewViewModel();
        overview.Show(View(recentEmpty: "No calls yet."));

        Assert.Equal(string.Empty, overview.ReadOnlyBadge);
        Assert.False(overview.HasReadOnlyBadge);
        Assert.False(overview.HasFinishSetup);
        Assert.Equal(string.Empty, overview.FinishSetupTitle);
        Assert.Equal(string.Empty, overview.FinishSetupBody);
        Assert.Equal(string.Empty, overview.FinishSetupAction);
        Assert.Equal("No calls yet.", overview.RecentCallsEmpty);
        Assert.True(overview.NoRecentCalls);
    }

    [Fact]
    public void NoCallsSaysNothingUntilLoadedOrWithNothingToSay()
    {
        var overview = new OverviewViewModel();
        overview.Show(View(status: V.Loading, recentEmpty: "No calls yet."));
        Assert.False(overview.NoRecentCalls);

        overview.Show(View());
        Assert.Equal(string.Empty, overview.RecentCallsEmpty);
        Assert.False(overview.NoRecentCalls);
    }

    [Fact]
    public void ItOpensACallAndFinishSetup()
    {
        var overview = new OverviewViewModel();
        overview.OpenCall(new CallRowItem("call-9", "Alex", string.Empty, string.Empty, string.Empty));
        overview.FinishSetupCommand.Execute(null);

        var (context, sink) = Pages.Context();
        overview.Attach(context);
        overview.OpenCall(new CallRowItem("call-9", "Alex", string.Empty, string.Empty, string.Empty));
        overview.FinishSetupCommand.Execute(null);
        overview.Load.RetryCommand.Execute(null);

        Assert.Equal([new UiEvent.OpenCall("call-9"), new UiEvent.OpenFinishSetup(), new UiEvent.Refresh()], sink.Sent);
    }
}

public sealed class SignInViewModelTests
{
    [Fact]
    public void ItCopiesTheSessionScreen()
    {
        var signIn = new SignInViewModel();
        Assert.Equal("District AI", signIn.Title);

        signIn.Show(new SessionScreen("Sign in", "Use your browser.", Busy: true, SignIn: true, Retry: true, RetrySignOut: true, Cancel: true, Error: "Refused.", CreateAccount: null));
        Assert.Equal("Sign in", signIn.Title);
        Assert.Equal("Use your browser.", signIn.Body);
        Assert.True(signIn.Busy);
        Assert.Equal("Refused.", signIn.Error);
        Assert.True(signIn.HasError);
        Assert.True(signIn.CanSignIn);
        Assert.True(signIn.CanRetry);
        Assert.True(signIn.CanRetrySignOut);
        Assert.True(signIn.CanCancel);

        signIn.Show(new SessionScreen("Sign in", string.Empty, false, false, false, false, false, null, null));
        Assert.Equal(string.Empty, signIn.Error);
        Assert.False(signIn.HasError);
    }

    [Fact]
    public void EachButtonIsTheEventItNames()
    {
        var signIn = new SignInViewModel();
        signIn.SignInCommand.Execute(null);
        signIn.RetryCommand.Execute(null);
        signIn.RetrySignOutCommand.Execute(null);
        signIn.CancelCommand.Execute(null);

        var sink = new RecordingSink();
        signIn.Attach(sink);
        signIn.SignInCommand.Execute(null);
        signIn.RetryCommand.Execute(null);
        signIn.RetrySignOutCommand.Execute(null);
        signIn.CancelCommand.Execute(null);

        Assert.Equal([new UiEvent.SignIn(), new UiEvent.RetryRestore(), new UiEvent.RetrySignOut(), new UiEvent.CancelSignIn()], sink.Sent);
    }
}

public sealed class DevicesViewModelTests
{
    private static readonly DeviceRowView _row = new("d-1", "Desk PC", "Windows", "Signed in recently", true, null);

    private static DevicesView View(
        LoadStatus? status = null,
        DeviceRowView[]? rows = null,
        bool busy = false,
        ConfirmView? confirming = null,
        FailureView? failure = null,
        bool nothingRevoked = false,
        string? nothingRevokedNote = null) =>
        new(status ?? V.Ready, rows ?? [_row], busy, confirming, failure, nothingRevoked, false, null, nothingRevokedNote);

    [Fact]
    public void ItListsTheDevicesAndOffersSignOutEverywhere()
    {
        var devices = new DevicesViewModel();
        devices.Show(View());

        Assert.Single(devices.Rows);
        Assert.True(devices.Rows[0].CanSignOut);
        Assert.False(devices.Busy);
        Assert.True(devices.EverywhereEnabled);
        Assert.Equal(string.Empty, devices.Notice);
        Assert.False(devices.HasNotice);
        Assert.False(devices.Confirming);
        Assert.Equal(string.Empty, devices.ConfirmQuestion);
        Assert.Equal(string.Empty, devices.ConfirmAction);
    }

    [Fact]
    public void SignOutEverywhereWaitsForTheListAndForNothingElseToBeUnderWay()
    {
        var devices = new DevicesViewModel();
        devices.Show(View(status: V.Loading));
        Assert.False(devices.EverywhereEnabled);

        devices.Show(View(busy: true));
        Assert.True(devices.Busy);
        Assert.False(devices.EverywhereEnabled);
        Assert.False(devices.Rows[0].CanSignOut);

        devices.Show(View(rows: []));
        Assert.False(devices.EverywhereEnabled);
    }

    [Fact]
    public void TheNoticeIsTheFailureOrThatThereWasNothingToSignOut()
    {
        var devices = new DevicesViewModel();
        devices.Show(View(failure: V.Failure("Could not sign out."), nothingRevoked: true, nothingRevokedNote: "Already signed out."));
        Assert.Equal("Could not sign out.", devices.Notice);
        Assert.True(devices.HasNotice);

        devices.Show(View(nothingRevoked: true, nothingRevokedNote: "Already signed out."));
        Assert.Equal("Already signed out.", devices.Notice);

        devices.Show(View(nothingRevoked: true));
        Assert.Equal(string.Empty, devices.Notice);
        Assert.False(devices.HasNotice);
    }

    [Fact]
    public void TheQuestionBeforeASignOut()
    {
        var devices = new DevicesViewModel();
        devices.Show(View(confirming: new ConfirmView("Sign out Desk PC?", "Sign out")));
        Assert.True(devices.Confirming);
        Assert.Equal("Sign out Desk PC?", devices.ConfirmQuestion);
        Assert.Equal("Sign out", devices.ConfirmAction);
    }

    [Fact]
    public void WhatItSends()
    {
        var devices = new DevicesViewModel();
        var row = DeviceRowItem.From(_row, busy: false);
        devices.Show(View());
        devices.AskSignOut(row);
        devices.Answer(true);
        devices.SignOutEverywhereCommand.Execute(null);
        devices.DismissNoticesCommand.Execute(null);

        var (context, sink) = Pages.Context();
        devices.Attach(context);
        devices.Show(View());
        devices.AskSignOut(row);
        devices.Answer(true);
        devices.Answer(false);
        devices.SignOutEverywhereCommand.Execute(null);
        // Disabled at once: a second click is not a second question.
        devices.SignOutEverywhereCommand.Execute(null);
        devices.DismissNoticesCommand.Execute(null);
        devices.Load.RetryCommand.Execute(null);

        Assert.Equal(
            [
                new UiEvent.AskSignOutDevice("d-1"),
                new UiEvent.ConfirmDevices(),
                new UiEvent.CancelDevices(),
                new UiEvent.AskSignOutEverywhere(),
                new UiEvent.DismissDevicesNotices(),
                new UiEvent.Refresh(),
            ],
            sink.Sent);
    }

    [Fact]
    public void NothingIsAskedWhileASignOutIsUnderWay()
    {
        var (context, sink) = Pages.Context();
        var devices = new DevicesViewModel();
        devices.Attach(context);
        devices.Show(View(busy: true));
        devices.AskSignOut(DeviceRowItem.From(_row, busy: true));
        Assert.Empty(sink.Sent);
    }
}

public sealed class UnavailableViewModelTests
{
    [Fact]
    public void ItShowsWhatItIsToldAndGoesBack()
    {
        var page = new UnavailableViewModel();
        page.Show("Not in this version yet", "Use the web dashboard.");
        Assert.Equal("Not in this version yet", page.Title);
        Assert.Equal("Use the web dashboard.", page.Body);
        page.BackCommand.Execute(null);

        var (context, sink) = Pages.Context();
        page.Attach(context);
        page.BackCommand.Execute(null);
        Assert.Equal([new UiEvent.Back()], sink.Sent);
    }
}

public sealed class ContactsViewModelTests
{
    [Fact]
    public void ItListsTheContactsAndTheirCount()
    {
        var contacts = new ContactsViewModel();
        contacts.Show(new ContactsView(V.Ready, [new ContactRowView("c-1", "Alex", string.Empty)], null, "1 contact", V.Paging(canLoadMore: true, refreshing: true), false, null));

        Assert.Equal([new ContactRowItem("c-1", "Alex", string.Empty)], contacts.Rows);
        Assert.Equal("1 contact", contacts.TotalLabel);
        Assert.True(contacts.HasTotalLabel);
        Assert.True(contacts.Load.Refreshing);
        Assert.True(contacts.Paging.LoadMoreEnabled);

        contacts.Show(new ContactsView(V.Ready, [], new EmptyView("No contacts", string.Empty), null, V.Paging(refreshFailure: V.Failure("Offline.")), false, null));
        Assert.Equal(string.Empty, contacts.TotalLabel);
        Assert.False(contacts.HasTotalLabel);
        Assert.True(contacts.Load.ShowEmpty);
        Assert.True(contacts.Load.HasRefreshFailure);
    }

    [Fact]
    public void ItOpensAContactAndLoadsMore()
    {
        var contacts = new ContactsViewModel();
        contacts.OpenContact(new ContactRowItem("c-1", "Alex", string.Empty));

        var (context, sink) = Pages.Context();
        contacts.Attach(context);
        contacts.Show(new ContactsView(V.Ready, [], null, null, V.Paging(canLoadMore: true), false, null));
        contacts.OpenContact(new ContactRowItem("c-1", "Alex", string.Empty));
        contacts.Paging.LoadMoreCommand.Execute(null);
        contacts.Load.RetryCommand.Execute(null);

        Assert.Equal([new UiEvent.OpenContact("c-1"), new UiEvent.LoadMoreContacts(), new UiEvent.Refresh()], sink.Sent);
    }
}

public sealed class ContactDetailViewModelTests
{
    private static ContactDetailView View(
        string? createdAt = null,
        string? updatedAt = null,
        FactView[]? numberFacts = null,
        string? research = null,
        AiTextView? dossier = null,
        FailureView? failure = null,
        ReportAvailability report = ReportAvailability.Hidden,
        string? phone = null) =>
        new("c-1", V.Ready, "Alex", "+1 212 555 0100", [new FactView("Company", "Example")], numberFacts ?? [], createdAt, updatedAt, research, dossier, failure, report, phone, false, null, null, null, null);

    [Fact]
    public void ItShowsTheContactAndTheDossier()
    {
        var (context, _) = Pages.Context(callsAvailable: true);
        var detail = new ContactDetailViewModel();
        detail.Attach(context);
        detail.Show(
            View(
                createdAt: "2020-01-02T03:04:05Z",
                updatedAt: "2021-01-02T03:04:05Z",
                numberFacts: [new FactView("Line type", "Mobile")],
                research: "Complete",
                dossier: new AiTextView("AI dossier", "Runs a bakery."),
                failure: V.Failure("Research failed."),
                report: ReportAvailability.InApp,
                phone: "+12125550100"),
            reportSending: false);

        Assert.Equal("c-1", detail.ContactId);
        Assert.Equal(ReportAvailability.InApp, detail.Report);
        Assert.Equal("Alex", detail.Name);
        Assert.True(detail.Load.Ready);
        Assert.Equal("+1 212 555 0100", detail.Reach);
        Assert.True(detail.HasReach);
        Assert.Equal(
            [
                new FactItem("Company", "Example"),
                new FactItem("Added", Display.When("2020-01-02T03:04:05Z")),
                new FactItem("Last changed", Display.When("2021-01-02T03:04:05Z")),
            ],
            detail.Facts);
        Assert.Equal([new FactItem("Line type", "Mobile")], detail.NumberFacts);
        Assert.True(detail.HasNumberFacts);
        Assert.Equal("Complete", detail.ResearchStatus);
        Assert.True(detail.HasResearchStatus);
        Assert.True(detail.HasDossier);
        Assert.Equal("AI dossier", detail.DossierLabel);
        Assert.Equal("Runs a bakery.", detail.DossierText);
        Assert.True(detail.HasFailure);
        Assert.Equal("Research failed.", detail.FailureMessage);
        Assert.Equal("Report", detail.ReportLabel);
        Assert.True(detail.ReportVisible);
        Assert.True(detail.ReportEnabled);
        Assert.Equal("+12125550100", detail.PhoneNumber);
        Assert.True(detail.CanCall);
        Assert.Equal(new ReportTarget.Contact("c-1"), detail.Target());
    }

    [Fact]
    public void AContactWithLittleKnown()
    {
        var detail = new ContactDetailViewModel();
        detail.Show(new ContactDetailView("c-2", V.Ready, "Sam", string.Empty, [], [], null, "not a time", null, null, null, ReportAvailability.Hidden, null, false, null, null, null, null), reportSending: true);

        Assert.False(detail.HasReach);
        Assert.Empty(detail.Facts);
        Assert.False(detail.HasNumberFacts);
        Assert.Equal(string.Empty, detail.ResearchStatus);
        Assert.False(detail.HasResearchStatus);
        Assert.False(detail.HasDossier);
        Assert.Equal(string.Empty, detail.DossierLabel);
        Assert.Equal(string.Empty, detail.DossierText);
        Assert.False(detail.HasFailure);
        Assert.False(detail.ReportVisible);
        Assert.False(detail.ReportEnabled);
        Assert.Equal(string.Empty, detail.PhoneNumber);
        Assert.False(detail.CanCall);
    }

    [Fact]
    public void CallIsOfferedOnlyWithANumberInABuildThatCanCall()
    {
        var detail = new ContactDetailViewModel();
        // Not attached: no build to ask.
        detail.Show(View(phone: "+12125550100"), false);
        Assert.False(detail.CanCall);

        var (context, sink) = Pages.Context(callsAvailable: false);
        detail.Attach(context);
        detail.Show(View(phone: "+12125550100"), false);
        Assert.False(detail.CanCall);

        context.CallsAvailable = true;
        detail.Show(View(), false);
        Assert.False(detail.CanCall);
    }

    [Fact]
    public void CallDialsTheContact()
    {
        var detail = new ContactDetailViewModel();
        detail.CallCommand.Execute(null);

        var (context, sink) = Pages.Context(callsAvailable: true);
        detail.Attach(context);
        detail.Show(View(phone: "+12125550100"), false);
        detail.CallCommand.Execute(null);
        detail.Load.RetryCommand.Execute(null);
        Assert.Equal([new UiEvent.CallNumber("+12125550100"), new UiEvent.Refresh()], sink.Sent);
    }
}

public sealed class CallsViewModelTests
{
    [Fact]
    public void ItListsTheCallsAndOffersPlaceACallInABuildThatCanCall()
    {
        var calls = new CallsViewModel();
        calls.Show(new CallsView(V.Ready, [V.CallRow()], null, V.Paging()));
        Assert.Single(calls.Rows);
        Assert.False(calls.CanPlaceCall);

        var (context, _) = Pages.Context(callsAvailable: true);
        calls.Attach(context);
        calls.Show(new CallsView(V.Ready, [], new EmptyView("No calls yet", string.Empty), V.Paging(refreshing: true)));
        Assert.Empty(calls.Rows);
        Assert.True(calls.Load.ShowEmpty);
        Assert.True(calls.Load.Refreshing);
        Assert.True(calls.CanPlaceCall);
    }

    [Fact]
    public void ItOpensACallPlacesOneAndLoadsMore()
    {
        var calls = new CallsViewModel();
        var row = CallRowItem.From(V.CallRow());
        calls.OpenCall(row);
        calls.PlaceCallCommand.Execute(null);

        var (context, sink) = Pages.Context();
        calls.Attach(context);
        calls.Show(new CallsView(V.Ready, [V.CallRow()], null, V.Paging(canLoadMore: true)));
        calls.OpenCall(row);
        calls.PlaceCallCommand.Execute(null);
        calls.Paging.LoadMoreCommand.Execute(null);
        calls.Load.RetryCommand.Execute(null);

        Assert.Equal([new UiEvent.OpenCall("call-1"), new UiEvent.OpenDialer(), new UiEvent.LoadMoreCalls(), new UiEvent.Refresh()], sink.Sent);
    }
}

public sealed class CallDetailViewModelTests
{
    private static CallDetailView View(
        string? startedAt = null,
        string? duration = null,
        FactView[]? followUp = null,
        FactView[]? analysis = null,
        AiTextView? summary = null,
        TranscriptState? transcript = null,
        ReportAvailability report = ReportAvailability.Hidden,
        string? callback = null) =>
        new("call-1", V.Ready, "Alex", startedAt, duration, [new FactView("Direction", "Inbound")], summary, analysis ?? [], followUp ?? [], transcript ?? new TranscriptState.Loading(), null, report, callback);

    [Fact]
    public void ItShowsTheCall()
    {
        var (context, _) = Pages.Context(callsAvailable: true);
        var detail = new CallDetailViewModel();
        detail.Attach(context);
        detail.Show(
            View(
                startedAt: "2020-01-02T03:04:05Z",
                duration: "1m 5s",
                followUp: [new FactView("Email sent to", "alex@example.com")],
                analysis: [new FactView("Sentiment", "Positive")],
                summary: new AiTextView("AI summary", "Booked."),
                transcript: new TranscriptState.Ready("Hello."),
                report: ReportAvailability.OnWeb,
                callback: "+12125550100"),
            reportSending: false);

        Assert.Equal("call-1", detail.CallId);
        Assert.Equal(ReportAvailability.OnWeb, detail.Report);
        Assert.Equal("Alex", detail.Title);
        Assert.Equal([new FactItem("Direction", "Inbound")], detail.Facts);
        Assert.Equal([new FactItem("Sentiment", "Positive")], detail.Analysis);
        Assert.Equal([new FactItem("Email sent to", "alex@example.com")], detail.FollowUp);
        Assert.True(detail.HasFollowUp);
        Assert.Equal(Display.When("2020-01-02T03:04:05Z") + " \u00B7 1m 5s", detail.Subtitle);
        Assert.True(detail.HasSubtitle);
        Assert.True(detail.HasAnalysis);
        Assert.True(detail.HasSummary);
        Assert.Equal("AI summary", detail.SummaryLabel);
        Assert.Equal("Booked.", detail.SummaryText);
        Assert.False(detail.TranscriptLoading);
        Assert.Equal("Hello.", detail.TranscriptText);
        Assert.True(detail.HasTranscriptText);
        Assert.False(detail.HasTranscriptMessage);
        Assert.False(detail.CanRetryTranscript);
        Assert.Equal("Report on the web", detail.ReportLabel);
        Assert.True(detail.ReportVisible);
        Assert.True(detail.ReportEnabled);
        Assert.Equal("+12125550100", detail.CallbackNumber);
        Assert.True(detail.CanCall);
        Assert.Equal(new ReportTarget.Call("call-1"), detail.Target());
    }

    [Fact]
    public void ACallWithLittleKnown()
    {
        var detail = new CallDetailViewModel();
        detail.Show(View(), reportSending: true);

        Assert.Equal(string.Empty, detail.Subtitle);
        Assert.False(detail.HasSubtitle);
        Assert.False(detail.HasFollowUp);
        Assert.False(detail.HasAnalysis);
        Assert.False(detail.HasSummary);
        Assert.Equal(string.Empty, detail.SummaryLabel);
        Assert.Equal(string.Empty, detail.SummaryText);
        Assert.True(detail.TranscriptLoading);
        Assert.False(detail.ReportVisible);
        Assert.False(detail.ReportEnabled);
        Assert.Equal(string.Empty, detail.CallbackNumber);
        Assert.False(detail.CanCall);

        detail.Show(View(duration: "1m 5s"), false);
        Assert.Equal("1m 5s", detail.Subtitle);
    }

    [Fact]
    public void EachTranscriptState()
    {
        var detail = new CallDetailViewModel();

        detail.Show(View(transcript: new TranscriptState.Absent("No transcript for this call.")), false);
        Assert.Equal(string.Empty, detail.TranscriptText);
        Assert.False(detail.HasTranscriptText);
        Assert.Equal("No transcript for this call.", detail.TranscriptMessage);
        Assert.True(detail.HasTranscriptMessage);
        Assert.False(detail.CanRetryTranscript);

        detail.Show(View(transcript: new TranscriptState.Failed(V.Failure("Could not load it.", retryable: true))), false);
        Assert.Equal("Could not load it.", detail.TranscriptMessage);
        Assert.True(detail.CanRetryTranscript);

        detail.Show(View(transcript: new TranscriptState.Failed(V.Failure("Could not load it."))), false);
        Assert.False(detail.CanRetryTranscript);

        // A state a later core adds: nothing.
        detail.Show(View(transcript: new TranscriptState()), false);
        Assert.False(detail.TranscriptLoading);
        Assert.False(detail.HasTranscriptText);
        Assert.False(detail.HasTranscriptMessage);
    }

    [Fact]
    public void CallIsOfferedOnlyWithANumberInABuildThatCanCall()
    {
        var detail = new CallDetailViewModel();
        detail.Show(View(callback: "+12125550100"), false);
        Assert.False(detail.CanCall);

        var (context, _) = Pages.Context(callsAvailable: false);
        detail.Attach(context);
        detail.Show(View(callback: "+12125550100"), false);
        Assert.False(detail.CanCall);
    }

    [Fact]
    public void CallAndTryAgain()
    {
        var detail = new CallDetailViewModel();
        detail.CallCommand.Execute(null);
        detail.RetryTranscriptCommand.Execute(null);

        var (context, sink) = Pages.Context(callsAvailable: true);
        detail.Attach(context);
        detail.Show(View(callback: "+12125550100"), false);
        detail.CallCommand.Execute(null);
        detail.RetryTranscriptCommand.Execute(null);
        detail.Load.RetryCommand.Execute(null);
        Assert.Equal([new UiEvent.CallNumber("+12125550100"), new UiEvent.Refresh(), new UiEvent.Refresh()], sink.Sent);
    }
}

public sealed class ThreadViewModelTests
{
    private static readonly TimelineItemView _note = new("e-1", new TimelineKind.Note("Assigned"), null, ReportAvailability.InApp);

    private static ThreadView View(
        LoadStatus? status = null,
        TimelineItemView[]? items = null,
        bool hasMore = false,
        bool loadingOlder = false,
        FailureView? olderFailure = null) =>
        new("t-1", "Alex", status ?? V.Ready, items ?? [_note], hasMore, loadingOlder, olderFailure, false, null, "Reply on the web.", null);

    [Fact]
    public void ItShowsTheConversation()
    {
        var thread = new ThreadViewModel();
        thread.Show(View(hasMore: true), reportSending: false);

        Assert.Equal("t-1", thread.ThreadKey);
        Assert.Equal("Alex", thread.Title);
        Assert.Equal("Reply on the web.", thread.ReadOnlyNote);
        Assert.Single(thread.Items);
        Assert.True(thread.Items[0].ReportEnabled);
        Assert.True(thread.HasMore);
        Assert.False(thread.LoadingOlder);
        Assert.True(thread.CanLoadOlder);
        Assert.False(thread.HasOlderFailure);
        Assert.Equal(string.Empty, thread.OlderFailure);
        Assert.Equal(new ReportTarget.ThreadEvent("t-1", "e-1"), thread.TargetFor(thread.Items[0]));
    }

    [Fact]
    public void AnEmptyConversationSaysSo()
    {
        var thread = new ThreadViewModel();
        thread.Show(View(items: []), reportSending: true);
        Assert.True(thread.Load.ShowEmpty);
        Assert.Equal("No messages in this conversation yet.", thread.Load.EmptyTitle);
    }

    [Fact]
    public void OlderMessages()
    {
        var thread = new ThreadViewModel();
        thread.Show(View(status: V.Loading, hasMore: true), false);
        Assert.False(thread.HasMore);

        thread.Show(View(hasMore: true, loadingOlder: true, olderFailure: V.Failure("Could not load older messages.")), true);
        Assert.False(thread.Items[0].ReportEnabled);
        Assert.True(thread.LoadingOlder);
        Assert.False(thread.CanLoadOlder);
        Assert.True(thread.HasOlderFailure);
        Assert.Equal("Could not load older messages.", thread.OlderFailure);
    }

    [Fact]
    public void OneClickIsOneRequestForOlderMessages()
    {
        var thread = new ThreadViewModel();
        thread.Show(View(hasMore: true), false);
        thread.LoadOlderCommand.Execute(null);

        var (context, sink) = Pages.Context();
        thread.Attach(context);
        thread.Show(View(hasMore: true), false);
        thread.LoadOlderCommand.Execute(null);
        thread.LoadOlderCommand.Execute(null);
        thread.Load.RetryCommand.Execute(null);
        Assert.Equal([new UiEvent.LoadOlder(), new UiEvent.Refresh()], sink.Sent);
    }
}

public sealed class InboxViewModelTests
{
    private static InboxView View(
        LoadStatus? status = null,
        ThreadRowView[]? threads = null,
        string? partialNote = null,
        SearchView? search = null) =>
        new(status ?? V.Ready, threads ?? [V.ThreadRow()], null, partialNote, false, null, search ?? V.Search(), "Reply on the web.");

    [Fact]
    public void ItShowsTheConversations()
    {
        var inbox = new InboxViewModel();
        Assert.True(inbox.ShowStatus);
        inbox.Show(View(partialNote: "Older conversations are left out."));

        Assert.Single(inbox.Threads);
        Assert.Equal("Reply on the web.", inbox.ReadOnlyNote);
        Assert.Equal("Older conversations are left out.", inbox.PartialNote);
        Assert.True(inbox.HasPartialNote);
        Assert.False(inbox.SearchActive);
        Assert.True(inbox.ShowThreads);
        Assert.True(inbox.ShowStatus);
        Assert.Equal(string.Empty, inbox.SearchNote);
        Assert.False(inbox.HasSearchNote);

        inbox.Show(View(status: V.Loading));
        Assert.Equal(string.Empty, inbox.PartialNote);
        Assert.False(inbox.HasPartialNote);
        Assert.False(inbox.ShowThreads);
    }

    [Fact]
    public void ASearchShowsItsResultsInsteadOfTheConversations()
    {
        var inbox = new InboxViewModel();
        inbox.Show(View(search: V.Search("invoice", active: true, hits: [V.Hit()])));

        Assert.Equal("invoice", inbox.Query);
        Assert.True(inbox.SearchActive);
        Assert.False(inbox.ShowThreads);
        Assert.False(inbox.ShowStatus);
        Assert.Single(inbox.Hits);
        Assert.Equal(string.Empty, inbox.SearchNote);
        Assert.False(inbox.HasSearchNote);
    }

    [Fact]
    public void ARunningOrFailedSearchHasNoNote()
    {
        var inbox = new InboxViewModel();
        inbox.Show(View(search: V.Search("in", active: true, running: true)));
        Assert.True(inbox.SearchRunning);
        Assert.Equal(string.Empty, inbox.SearchNote);

        inbox.Show(View(search: V.Search("invoice", active: true, failure: V.Failure("Search failed."))));
        Assert.True(inbox.HasSearchFailure);
        Assert.Equal("Search failed.", inbox.SearchFailure);
        Assert.Equal(string.Empty, inbox.SearchNote);
    }

    [Fact]
    public void TooShortAQuery()
    {
        var inbox = new InboxViewModel();
        inbox.Show(View(search: V.Search(" ab ", active: true, minQueryLength: 3)));
        Assert.Equal("Type at least 3 characters to search.", inbox.SearchNote);
        Assert.True(inbox.HasSearchNote);
    }

    [Fact]
    public void NoMatches()
    {
        var inbox = new InboxViewModel();
        inbox.Show(View(search: V.Search("invoice", active: true, empty: new EmptyView("No matches", "Try other words."))));
        Assert.Equal("No matches" + Environment.NewLine + "Try other words.", inbox.SearchNote);

        inbox.Show(View(search: V.Search("invoice", active: true, empty: new EmptyView("No matches", string.Empty))));
        Assert.Equal("No matches", inbox.SearchNote);

        inbox.Show(View(search: V.Search("invoice", active: true, empty: new EmptyView(string.Empty, "Try other words."))));
        Assert.Equal("Try other words.", inbox.SearchNote);

        inbox.Show(View(search: V.Search("invoice", active: true)));
        Assert.Equal(string.Empty, inbox.SearchNote);
        Assert.False(inbox.HasSearchNote);
    }

    [Fact]
    public void OnlyTheFirstMatches()
    {
        var inbox = new InboxViewModel();
        inbox.Show(View(search: V.Search("invoice", active: true, hits: [V.Hit()], truncated: true, truncatedNote: "Only the newest matches.")));
        Assert.Equal("Only the newest matches.", inbox.SearchNote);

        inbox.Show(View(search: V.Search("invoice", active: true, hits: [V.Hit()], truncated: true)));
        Assert.Equal(string.Empty, inbox.SearchNote);
    }

    [Fact]
    public void TypingSearchesAndClearingStops()
    {
        var inbox = new InboxViewModel();
        inbox.Search("in");

        var (context, sink) = Pages.Context();
        inbox.Attach(context);
        inbox.Search("inv");
        // The same text again (the core's echo): nothing new.
        inbox.Search("inv");
        inbox.Search(string.Empty);
        inbox.Load.RetryCommand.Execute(null);

        Assert.Equal([new UiEvent.Search("inv"), new UiEvent.ClearSearch(), new UiEvent.Refresh()], sink.Sent);
        Assert.Equal(string.Empty, inbox.Query);
    }

    [Fact]
    public void OnlyWhatCanOpenIsOpened()
    {
        var inbox = new InboxViewModel();
        inbox.OpenThread(ThreadRowItem.From(V.ThreadRow("t-1")));
        inbox.OpenHit(SearchHitItem.From(V.Hit("t-1")));

        var (context, sink) = Pages.Context();
        inbox.Attach(context);
        inbox.OpenThread(ThreadRowItem.From(V.ThreadRow("t-1")));
        inbox.OpenThread(ThreadRowItem.From(V.ThreadRow("t-2", canOpen: false)));
        inbox.OpenHit(SearchHitItem.From(V.Hit("t-3")));
        inbox.OpenHit(SearchHitItem.From(V.Hit("t-4", canOpen: false)));

        Assert.Equal([new UiEvent.OpenThread("t-1"), new UiEvent.OpenThread("t-3")], sink.Sent);
    }
}

public sealed class AccountViewModelTests
{
    private static AccountView View(string? name = null, string? email = null, bool? ring = null, string? ringMessage = null, bool? purchases = null) =>
        new("1.2.0", "dev-1", "user-1", email, name, "Sign out of this computer.", "Every signed-in device.", "On the web.", ring, "Ring on this computer", "Calls ring here.", ringMessage,
            purchases, "Purchases on this computer", "Signs you in afresh each time.", "Sign in every time", "Off");

    [Fact]
    public void ThePurchasesRowShowsOnceReadAndSendsOnlyWhatTheUserChanges()
    {
        var account = new AccountViewModel(new FakeStartupTask());
        var (context, sink) = Pages.Context();
        account.Attach(context);
        account.Show(View());
        Assert.False(account.PurchasesVisible);

        // The core's value, written back: not a change to send.
        account.Show(View(purchases: true));
        Assert.True(account.PurchasesVisible);
        Assert.True(account.PurchasesOn);
        Assert.Equal("Purchases on this computer", account.PurchasesLabel);
        Assert.Equal("Signs you in afresh each time.", account.PurchasesBody);
        Assert.Equal("Sign in every time", account.PurchasesOnLabel);
        Assert.Equal("Off", account.PurchasesOffLabel);
        account.PurchasesOn = false;
        account.Show(View(purchases: false));
        account.PurchasesOn = true;

        Assert.Equal(
            [new UiEvent.Billing(new BillingAction.SetPurchases(false)), new UiEvent.Billing(new BillingAction.SetPurchases(true))],
            sink.Sent);
    }

    [Fact]
    public void ItNeedsAStartupTask()
    {
        Assert.Throws<ArgumentNullException>(() => new AccountViewModel(null!));
    }

    [Fact]
    public void ItShowsTheAccount()
    {
        var account = new AccountViewModel(new FakeStartupTask());
        account.Show(View(name: "Alex", email: "alex@example.com", ring: true, ringMessage: "No microphone."));

        Assert.Equal("Alex", account.Name);
        Assert.True(account.HasName);
        Assert.Equal("alex@example.com", account.Email);
        Assert.True(account.HasEmail);
        Assert.Equal("user-1", account.UserId);
        Assert.Equal("Version 1.2.0", account.AppVersion);
        Assert.Equal("dev-1", account.DeviceId);
        Assert.Equal("Every signed-in device.", account.DevicesCaption);
        Assert.Equal("Sign out of this computer.", account.SignOutCaption);
        Assert.Equal("On the web.", account.DeleteAccountCaption);
        Assert.True(account.RingVisible);
        Assert.True(account.RingOn);
        Assert.Equal("Ring on this computer", account.RingLabel);
        Assert.Equal("Calls ring here.", account.RingBody);
        Assert.Equal("No microphone.", account.RingMessage);
        Assert.True(account.HasRingMessage);

        account.Show(View());
        Assert.Equal(string.Empty, account.Name);
        Assert.False(account.HasName);
        Assert.Equal(string.Empty, account.Email);
        Assert.False(account.HasEmail);
        Assert.False(account.RingVisible);
        Assert.False(account.RingOn);
        Assert.Equal(string.Empty, account.RingMessage);
        Assert.False(account.HasRingMessage);
    }

    [Fact]
    public void TheRingSwitchSendsOnlyWhatTheUserChanges()
    {
        var account = new AccountViewModel(new FakeStartupTask());
        account.RingOn = true;

        var (context, sink) = Pages.Context();
        account.Attach(context);
        // The core's value, written back: not a change to send.
        account.Show(View(ring: true));
        account.RingOn = false;

        Assert.Equal([new UiEvent.SetRingOnThisComputer(false)], sink.Sent);
    }

    [Fact]
    public void TheRowsSendTheirEvents()
    {
        var account = new AccountViewModel(new FakeStartupTask());
        account.OpenDevicesCommand.Execute(null);
        account.SignOutCommand.Execute(null);
        account.DeleteAccountCommand.Execute(null);

        var (context, sink) = Pages.Context();
        account.Attach(context);
        account.OpenDevicesCommand.Execute(null);
        account.SignOutCommand.Execute(null);
        account.DeleteAccountCommand.Execute(null);

        Assert.Equal([new UiEvent.OpenDevices(), new UiEvent.SignOut(), new UiEvent.DeleteAccount()], sink.Sent);
    }

    [Fact]
    public async Task StartAtSignInIsReadEachTimeThePageOpens()
    {
        var startup = new FakeStartupTask { Setting = new(Enabled: true, CanChange: true, Message: null) };
        var account = new AccountViewModel(startup);
        Assert.False(account.StartupSwitchEnabled);

        await account.LoadStartupAsync();
        Assert.True(account.StartupEnabled);
        Assert.True(account.StartupSwitchEnabled);
        Assert.Equal(string.Empty, account.StartupMessage);
        Assert.False(account.HasStartupMessage);

        startup.Setting = new(Enabled: false, CanChange: false, Message: "Turned off by your organisation.");
        await account.LoadStartupAsync();
        Assert.False(account.StartupEnabled);
        Assert.False(account.StartupSwitchEnabled);
        Assert.Equal("Turned off by your organisation.", account.StartupMessage);
        Assert.True(account.HasStartupMessage);
        Assert.Equal(2, startup.Gets);
    }

    [Fact]
    public async Task OneReadOrChangeAtATime()
    {
        var startup = new FakeStartupTask();
        var account = new AccountViewModel(startup);
        var pending = new TaskCompletionSource<StartupSetting>();
        startup.Pending = pending;

        var first = account.LoadStartupAsync();
        Assert.False(account.StartupSwitchEnabled);
        // While the first read is under way, a second read and a change are ignored.
        await account.LoadStartupAsync();
        await account.SetStartupAsync(true);
        Assert.Equal(1, startup.Gets);
        Assert.Empty(startup.Sets);

        pending.SetResult(new(Enabled: false, CanChange: true, Message: null));
        await first;
        Assert.True(account.StartupSwitchEnabled);
    }

    [Fact]
    public async Task TheSwitchChangesTheSettingWhenItMayAndItIsAChange()
    {
        var startup = new FakeStartupTask();
        var account = new AccountViewModel(startup);

        // Before it has been read, it cannot be changed.
        await account.SetStartupAsync(true);
        Assert.Empty(startup.Sets);

        await account.LoadStartupAsync();
        // Already off.
        await account.SetStartupAsync(false);
        Assert.Empty(startup.Sets);

        await account.SetStartupAsync(true);
        Assert.Equal([true], startup.Sets);
        Assert.True(account.StartupEnabled);
        Assert.True(account.StartupSwitchEnabled);

        // Windows answers with the setting as it really is.
        var pending = new TaskCompletionSource<StartupSetting>();
        startup.Pending = pending;
        var change = account.SetStartupAsync(false);
        Assert.False(account.StartupSwitchEnabled);
        pending.SetResult(new(Enabled: true, CanChange: false, Message: "Turned on by your organisation."));
        await change;
        Assert.True(account.StartupEnabled);
        Assert.False(account.StartupSwitchEnabled);
        Assert.Equal("Turned on by your organisation.", account.StartupMessage);
    }

    [Fact]
    public async Task AFailedReadStillGivesTheSwitchBack()
    {
        var startup = new FakeStartupTask();
        var account = new AccountViewModel(startup);
        await account.LoadStartupAsync();

        var pending = new TaskCompletionSource<StartupSetting>();
        startup.Pending = pending;
        var read = account.LoadStartupAsync();
        pending.SetException(new InvalidOperationException("no package"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => read);
        Assert.True(account.StartupSwitchEnabled);

        pending = new TaskCompletionSource<StartupSetting>();
        startup.Pending = pending;
        var change = account.SetStartupAsync(true);
        pending.SetException(new InvalidOperationException("no package"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => change);
        Assert.True(account.StartupSwitchEnabled);
    }
}
