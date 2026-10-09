using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels;
using DistrictAI.ViewModels.Desk;
using Xunit;

namespace DistrictAI.Presentation.Tests.Desk;

public sealed class DeskTicketViewModelTests
{
    private static readonly DeskTicketView _loading = new(
        Title: "Ticket",
        TicketId: "t-1",
        Status: V.Loading,
        ReferenceLine: string.Empty,
        Statuses: [],
        CanChangeStatus: false,
        StatusChanging: false,
        StatusFailure: null,
        Details: [],
        CreatedAt: string.Empty,
        ResolvedAt: null,
        Messages: [],
        RefreshFailure: null,
        Reply: string.Empty,
        CanWriteReply: false,
        CanReply: false,
        Sending: false,
        SendFailure: null,
        NotifiedNote: null);

    private static DeskStatusChoiceView[] Choices(DeskStatus current) =>
    [
        new(DeskStatus.Open, "Open", current == DeskStatus.Open),
        new(DeskStatus.Waiting, "Waiting on the customer", current == DeskStatus.Waiting),
        new(DeskStatus.Resolved, "Resolved", current == DeskStatus.Resolved),
    ];

    private static DeskTicketView Ready(string reply = "", bool canReply = false) => _loading with
    {
        Title = "Reschedule Thursday's appointment",
        Status = V.Ready,
        ReferenceLine = "T-41 · Open",
        Statuses = Choices(DeskStatus.Open),
        CanChangeStatus = true,
        Details = [new FactView("Name", "Ada"), new FactView("Came in by", "Voice call")],
        CreatedAt = "2020-01-02T03:04:05Z",
        Messages =
        [
            new DeskMessageView("m-1", "Customer", "Can we move it?", "2020-01-02T03:04:05Z", false),
            new DeskMessageView("m-2", "Your team", "Moved.", "2020-01-02T04:04:05Z", true),
        ],
        Reply = reply,
        CanWriteReply = true,
        CanReply = canReply,
    };

    [Fact]
    public void LoadingAndFailed()
    {
        var model = new DeskTicketViewModel();
        model.Show(_loading);
        Assert.Same(_loading, model.View);
        Assert.True(model.Load.Loading);
        Assert.Equal("Ticket", model.Title);
        Assert.Empty(model.Statuses);
        Assert.Equal(string.Empty, model.DatesLine);

        model.Show(_loading with { Status = V.Failed(V.Failure("Ticket not found."), "Could not load this ticket") });
        Assert.True(model.Load.Failed);
        Assert.False(model.Load.CanRetry);
        Assert.Equal("Could not load this ticket", model.Load.FailureTitle);

        model.Show(Ready() with { RefreshFailure = V.Failure("Offline.", retryable: true) });
        Assert.True(model.Load.HasRefreshFailure);
        Assert.True(model.Load.CanRetryRefresh);
    }

    [Fact]
    public void ItShowsTheCustomerTheConversationAndTheStatus()
    {
        var model = new DeskTicketViewModel();
        model.Show(Ready() with { ResolvedAt = "2020-01-03T03:04:05Z" });

        Assert.Equal("Reschedule Thursday's appointment", model.Title);
        Assert.Equal("T-41 · Open", model.ReferenceLine);
        Assert.Equal(
            "Raised " + Display.When("2020-01-02T03:04:05Z") + " · Resolved " + Display.When("2020-01-03T03:04:05Z"),
            model.DatesLine);
        Assert.Equal([new FactItem("Name", "Ada"), new FactItem("Came in by", "Voice call")], model.Details);
        var customer = model.Messages[0];
        Assert.True(customer.FromOthers);
        Assert.Equal("Customer · " + Display.When("2020-01-02T03:04:05Z"), customer.Meta);
        Assert.Equal("Customer, Can we move it?, " + customer.When, customer.AccessibleName);
        Assert.Equal(customer.AccessibleName, customer.ToString());
        Assert.True(model.Messages[1].FromTeam);
        var open = model.Statuses[0];
        Assert.True(open.Selected);
        Assert.False(open.Enabled);
        Assert.Equal("Open, current status", open.AccessibleName);
        Assert.Equal(open.AccessibleName, open.ToString());
        Assert.True(model.Statuses[2].Enabled);
        Assert.Equal("Mark as Resolved", model.Statuses[2].AccessibleName);
    }

    [Fact]
    public void AStatusChangeIsSentOnceAndNeverForTheTicketsOwn()
    {
        var (context, sink) = Pages.Context();
        var model = new DeskTicketViewModel();
        model.Attach(context);
        model.Show(Ready());

        model.SetStatus(model.Statuses[0]);
        model.SetStatus(model.Statuses[2]);
        model.SetStatus(model.Statuses[1]);
        Assert.Equal([new UiEvent.Desk(new DeskAction.SetStatus(DeskStatus.Resolved))], sink.Sent);
        Assert.All(model.Statuses, item => Assert.False(item.Enabled));

        model.Show(Ready() with { CanChangeStatus = false, StatusChanging = true });
        Assert.True(model.StatusChanging);
        model.Show(Ready() with { StatusFailure = V.Failure("Could not change the status.") });
        Assert.True(model.HasFailure);
        Assert.Equal("Could not change the status.", model.Failure);
        model.DismissFailuresCommand.Execute(null);
        Assert.Equal(new UiEvent.Desk(new DeskAction.DismissTicketFailures()), sink.Sent[^1]);
    }

    [Fact]
    public void TheReplyIsTypedSentOnceAndSaysWhetherTheCustomerWasEmailed()
    {
        var (context, sink) = Pages.Context();
        var model = new DeskTicketViewModel();
        model.Attach(context);
        model.Show(Ready());
        Assert.False(model.SendReplyCommand.CanExecute(null));

        model.Reply = "Moved to Tuesday.";
        Assert.Equal([new UiEvent.Desk(new DeskAction.EditReply("Moved to Tuesday."))], sink.Sent);
        model.Show(Ready("Moved to Tuesday.", canReply: true));
        Assert.Equal("Moved to Tuesday.", model.Reply);
        model.SendReplyCommand.Execute(null);
        model.SendReplyCommand.Execute(null);
        Assert.Single(sink.Sent, e => e == new UiEvent.Desk(new DeskAction.SendReply()));

        model.Show(Ready("Moved to Tuesday.") with { Sending = true, CanWriteReply = false });
        Assert.True(model.Sending);
        Assert.False(model.CanWriteReply);

        model.Show(Ready("Moved to Tuesday.", canReply: true) with { SendFailure = V.Failure("Could not send.") });
        Assert.Equal("Could not send.", model.Failure);
        Assert.Equal("Moved to Tuesday.", model.Reply);

        // Sent: the core empties the box, which this box did not type.
        var count = sink.Sent.Count;
        model.Show(Ready() with { NotifiedNote = "The customer was emailed your reply." });
        Assert.Equal(string.Empty, model.Reply);
        Assert.Equal(count, sink.Sent.Count);
        Assert.True(model.HasNotifiedNote);
        Assert.Equal("The customer was emailed your reply.", model.NotifiedNote);
    }

    [Fact]
    public void AnotherTicketStartsItsReplyBoxAfresh()
    {
        var model = new DeskTicketViewModel();
        model.Show(Ready("Draft for one."));
        Assert.Equal("Draft for one.", model.Reply);
        model.Show(Ready() with { TicketId = "t-2" });
        Assert.Equal(string.Empty, model.Reply);
    }

    [Fact]
    public void ARoleThatMayNotUseTheDeskIsOfferedNothing()
    {
        var (context, sink) = Pages.Context();
        var model = new DeskTicketViewModel();
        model.Attach(context);
        model.Show(Ready() with { CanChangeStatus = false, CanWriteReply = false, CanReply = false });
        Assert.All(model.Statuses, item => Assert.False(item.Enabled));
        model.SetStatus(model.Statuses[2]);
        Assert.False(model.CanWriteReply);
        Assert.False(model.SendReplyCommand.CanExecute(null));
        Assert.Empty(sink.Sent);
    }
}
