using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels;
using DistrictAI.ViewModels.Blocked;
using Xunit;

namespace DistrictAI.Presentation.Tests.Blocked;

public sealed class BlockedViewModelTests
{
    private static BlockedRowView Row(string id = "c-1", string name = "Ada", string? phone = "+1 416 555 0142", string? at = null, bool unblocking = false) =>
        new(id, name, phone, at, unblocking);

    private static BlockedView View(
        BlockedRowView[]? rows = null,
        LoadStatus? status = null,
        EmptyView? empty = null,
        bool canUnblock = true,
        UnblockQuestionView? confirming = null,
        FailureView? failure = null) =>
        new("Blocked callers", status ?? V.Ready, rows ?? [], empty, canUnblock, confirming, failure);

    [Fact]
    public void ItListsTheCallersWithTheirNumberAndWhen()
    {
        var model = new BlockedViewModel();
        var view = View([Row(at: "2020-01-02T03:04:05Z"), Row("c-2", "+1 416 555 0181", phone: null, unblocking: true)]);
        model.Show(view);

        Assert.Same(view, model.View);
        Assert.Equal("Blocked callers", model.Title);
        Assert.True(model.Load.Ready);
        var first = model.Rows[0];
        Assert.Equal("+1 416 555 0142 · Blocked " + Display.When("2020-01-02T03:04:05Z"), first.Detail);
        Assert.True(first.HasDetail);
        Assert.True(first.ShowUnblock);
        Assert.Equal("Unblock Ada", first.UnblockName);
        Assert.Equal("Ada, " + first.Detail, first.AccessibleName);
        Assert.Equal(first.AccessibleName, first.ToString());
        var second = model.Rows[1];
        Assert.Equal(string.Empty, second.Detail);
        Assert.False(second.HasDetail);
        Assert.False(second.ShowUnblock);
        Assert.Equal("+1 416 555 0181, unblocking", second.AccessibleName);
    }

    [Fact]
    public void EmptyFailedAndAViewer()
    {
        var model = new BlockedViewModel();
        model.Show(View(empty: new EmptyView("No blocked callers", "Callers you block are listed here.")));
        Assert.True(model.Load.ShowEmpty);
        Assert.Equal("No blocked callers", model.Load.EmptyTitle);

        model.Show(View(status: V.Failed(V.Failure("Offline.", retryable: true), "Could not load blocked callers")));
        Assert.True(model.Load.Failed);
        Assert.True(model.Load.CanRetry);

        model.Show(View([Row()], canUnblock: false));
        Assert.False(model.Rows[0].ShowUnblock);
    }

    [Fact]
    public void UnblockAsksOnceThenAnswersTheCore()
    {
        var (context, sink) = Pages.Context();
        var model = new BlockedViewModel();
        model.Attach(context);
        model.Show(View([Row(), Row("c-2", "Grace", unblocking: true)]));

        model.AskUnblock(model.Rows[1]);
        model.AskUnblock(model.Rows[0]);
        model.AskUnblock(model.Rows[0]);
        model.Show(View(
            [Row(), Row("c-2", "Grace", unblocking: true)],
            confirming: new UnblockQuestionView("c-1", "Ada", "Unblock this caller?", "Unblock")));
        Assert.True(model.Confirming);
        Assert.Equal(("Ada", "Unblock this caller?", "Unblock"), (model.ConfirmName, model.ConfirmQuestion, model.ConfirmAction));
        model.Answer(true);
        model.Answer(false);
        model.Show(View([Row()], failure: V.Failure("Could not unblock.")));
        Assert.False(model.Confirming);
        Assert.True(model.HasFailure);
        Assert.Equal("Could not unblock.", model.Failure);
        model.DismissFailureCommand.Execute(null);
        model.Send(new BlockedAction.Open());

        Assert.Equal(
            [
                new UiEvent.Blocked(new BlockedAction.AskUnblock("c-1")),
                new UiEvent.Blocked(new BlockedAction.ConfirmUnblock()),
                new UiEvent.Blocked(new BlockedAction.CancelUnblock()),
                new UiEvent.Blocked(new BlockedAction.DismissFailure()),
                new UiEvent.Blocked(new BlockedAction.Open()),
            ],
            sink.Sent);
    }

    [Fact]
    public void NothingIsSentBeforeThePageIsAttached()
    {
        var model = new BlockedViewModel();
        model.Show(View([Row()]));
        model.AskUnblock(model.Rows[0]);
        model.Answer(true);
        Assert.False(model.HasFailure);
    }
}
