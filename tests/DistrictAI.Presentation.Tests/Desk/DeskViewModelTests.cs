using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels;
using DistrictAI.ViewModels.Desk;
using Xunit;

namespace DistrictAI.Presentation.Tests.Desk;

public sealed class DeskViewModelTests
{
    private static readonly DeskFilterView[] _noCounts =
    [
        new(DeskFilter.All, "Every ticket", true),
        new(DeskFilter.Open, "Open", false),
        new(DeskFilter.Waiting, "Waiting on the customer", false),
        new(DeskFilter.Resolved, "Resolved", false),
    ];

    private static readonly DeskView _base = new(
        Title: "Help desk",
        Status: V.Loading,
        Off: null,
        Filters: _noCounts,
        CanFilter: false,
        Rows: [],
        Empty: null,
        NoneMatching: null,
        Refreshing: false,
        Submitted: null,
        CanStart: false,
        CanOpenSettings: true,
        Compose: null);

    private static DeskRowView Row(string id = "t-1", DeskStatus? status = DeskStatus.Open, string label = "Open", string requester = "Ada") =>
        new(id, "Reschedule", "T-41", requester, label, status, "2020-01-02T03:04:05Z");

    private static DeskView Listed() => _base with
    {
        Status = V.Ready,
        CanFilter = true,
        CanStart = true,
        Filters =
        [
            new(DeskFilter.All, "Every ticket (2)", true),
            new(DeskFilter.Open, "Open (1)", false),
            new(DeskFilter.Waiting, "Waiting on the customer (1)", false),
            new(DeskFilter.Resolved, "Resolved (0)", false),
        ],
        Rows = [Row(), Row("t-2", DeskStatus.Waiting, "Waiting", "No customer details")],
    };

    private static DeskComposeView Compose(bool canSubmit = false, bool submitting = false, FailureView? failure = null, string subject = "") =>
        new(
            Title: "New ticket",
            SubmitLabel: "Raise",
            Subject: subject,
            Message: string.Empty,
            RequesterName: string.Empty,
            RequesterEmail: string.Empty,
            RequesterPhone: string.Empty,
            CustomerNote: "Optional.",
            SubjectMax: 200,
            MessageMax: 10_000,
            Needs: canSubmit ? null : "A subject of 3 to 200 characters, and a message.",
            CanSubmit: canSubmit,
            Submitting: submitting,
            Failure: failure);

    [Fact]
    public void LoadingFailedAndEmpty()
    {
        var model = new DeskViewModel();
        model.Show(_base);
        Assert.Same(_base, model.View);
        Assert.Equal("Help desk", model.Title);
        Assert.True(model.Load.Loading);
        Assert.False(model.ShowQueue);
        Assert.False(model.CanFilter);
        Assert.Equal(["Every ticket", "Open", "Waiting on the customer", "Resolved"], model.FilterLabels);

        model.Show(_base with { Status = V.Failed(V.Failure("Offline.", retryable: true), "Could not load the help desk") });
        Assert.True(model.Load.Failed);
        Assert.True(model.Load.CanRetry);
        Assert.Equal("Could not load the help desk", model.Load.FailureTitle);

        model.Show(_base with { Status = V.Ready, CanFilter = true, Empty = new EmptyView("No tickets yet", "Tickets your customers raise will appear here.") });
        Assert.True(model.ShowQueue);
        Assert.True(model.Load.ShowEmpty);
        Assert.Equal("No tickets yet", model.Load.EmptyTitle);
        Assert.False(model.HasRows);
    }

    [Fact]
    public void ADeskThatIsOffOffersToTurnItOnOnce()
    {
        var (context, sink) = Pages.Context();
        var model = new DeskViewModel();
        model.Attach(context);
        var off = new DeskOffView("The help desk is off", "While the desk is off, no tickets are recorded.", "Turn on the help desk", true, false, null);
        model.Show(_base with { Status = V.Ready, Off = off });

        Assert.True(model.IsOff);
        Assert.False(model.ShowQueue);
        Assert.False(model.Load.ShowEmpty);
        Assert.Equal(("The help desk is off", "Turn on the help desk"), (model.OffTitle, model.TurnOnLabel));
        model.TurnOnCommand.Execute(null);
        model.TurnOnCommand.Execute(null);
        Assert.Equal([new UiEvent.Desk(new DeskAction.TurnOn())], sink.Sent);

        model.Show(_base with { Status = V.Ready, Off = off with { CanTurnOn = true, Failure = V.Failure("Could not turn it on.") } });
        Assert.True(model.HasEnableFailure);
        Assert.Equal("Could not turn it on.", model.EnableFailure);
        Assert.True(model.TurnOnCommand.CanExecute(null));
    }

    [Fact]
    public void ItListsTheQueueAndSendsTheFilterThePersonPicks()
    {
        var (context, sink) = Pages.Context();
        var model = new DeskViewModel();
        model.Attach(context);
        model.Show(Listed());

        Assert.True(model.ShowQueue);
        Assert.True(model.HasRows);
        Assert.Equal(0, model.FilterIndex);
        Assert.Equal("Every ticket (2)", model.FilterLabels[0]);
        var first = model.Rows[0];
        Assert.Equal("T-41 · Ada · " + Display.When("2020-01-02T03:04:05Z"), first.Line);
        Assert.True(first.IsOpen);
        Assert.False(first.IsQuiet);
        Assert.True(model.Rows[1].IsQuiet);
        Assert.Equal("Reschedule, Open, " + first.Line, first.AccessibleName);
        Assert.Equal(first.AccessibleName, first.ToString());
        Assert.Empty(sink.Sent);

        model.FilterIndex = 2;
        model.FilterIndex = 9;
        model.OpenTicket(first);
        Assert.Equal(
            [
                new UiEvent.Desk(new DeskAction.Filter(DeskFilter.Waiting)),
                new UiEvent.Desk(new DeskAction.OpenTicket("t-1")),
            ],
            sink.Sent);

        model.Show(Listed() with { Rows = [], NoneMatching = "No tickets with this status." });
        Assert.True(model.HasNoneMatching);
        Assert.False(model.HasRows);
        Assert.Equal(2, sink.Sent.Count);
    }

    [Fact]
    public void RaisingATicketSendsTheWholeFormAndSubmitsOnce()
    {
        var (context, sink) = Pages.Context();
        var model = new DeskViewModel();
        model.Attach(context);
        model.Show(Listed());
        model.StartTicketCommand.Execute(null);
        model.Subject = "Ignored: no form is open.";

        model.Show(Listed() with { CanStart = false, Compose = Compose() });
        Assert.True(model.Composing);
        Assert.False(model.StartTicketCommand.CanExecute(null));
        Assert.True(model.HasNeeds);
        Assert.Equal((200, 10_000), (model.SubjectMax, model.MessageMax));
        Assert.Equal("Raise", model.SubmitLabel);
        Assert.False(model.SubmitTicketCommand.CanExecute(null));

        model.Subject = "Invoice question";
        model.RequesterEmail = "ada@example.com";
        Assert.Equal(
            new UiEvent.Desk(new DeskAction.EditTicket("Invoice question", string.Empty, string.Empty, "ada@example.com", string.Empty)),
            sink.Sent[^1]);

        // What the core gives back is what was typed: the box is not written over.
        model.Show(Listed() with { CanStart = false, Compose = Compose(canSubmit: true, subject: "Invoice question") });
        Assert.Equal("Invoice question", model.Subject);
        model.SubmitTicketCommand.Execute(null);
        model.SubmitTicketCommand.Execute(null);
        Assert.Single(sink.Sent, e => e == new UiEvent.Desk(new DeskAction.SubmitTicket()));

        model.Show(Listed() with { CanStart = false, Compose = Compose(submitting: true, subject: "Invoice question") });
        Assert.False(model.CanEdit);
        Assert.False(model.CancelTicketCommand.CanExecute(null));

        model.Show(Listed() with { CanStart = false, Compose = Compose(canSubmit: true, failure: V.Failure("Subject is too short."), subject: "Invoice question") });
        Assert.True(model.HasComposeFailure);
        Assert.Equal("Subject is too short.", model.ComposeFailure);
        model.CancelTicketCommand.Execute(null);
        Assert.Equal(new UiEvent.Desk(new DeskAction.CancelTicket()), sink.Sent[^1]);

        model.Show(Listed() with { Submitted = "Ticket T-41 is open." });
        Assert.False(model.Composing);
        Assert.Equal(string.Empty, model.Subject);
        Assert.True(model.HasSubmitted);
        Assert.Equal("Ticket T-41 is open.", model.Submitted);
        model.DismissSubmittedCommand.Execute(null);
        model.OpenSettingsCommand.Execute(null);
        Assert.Equal(new UiEvent.Desk(new DeskAction.DismissSubmitted()), sink.Sent[^2]);
        Assert.Equal(new UiEvent.Desk(new DeskAction.OpenSettings()), sink.Sent[^1]);
    }

    [Fact]
    public void ARoleThatMayNotUseTheDeskIsOfferedNothing()
    {
        var (context, sink) = Pages.Context();
        var model = new DeskViewModel();
        model.Attach(context);
        model.Show(Listed() with { CanStart = false, CanOpenSettings = false });
        Assert.False(model.StartTicketCommand.CanExecute(null));
        Assert.False(model.OpenSettingsCommand.CanExecute(null));
        model.Show(_base with { Status = V.Ready, CanOpenSettings = false, Off = new DeskOffView("Off", "Body", "Turn on", false, false, null) });
        Assert.False(model.TurnOnCommand.CanExecute(null));
        Assert.Empty(sink.Sent);
    }
}
