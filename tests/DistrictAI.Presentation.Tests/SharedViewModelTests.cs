using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels;
using Xunit;

namespace DistrictAI.Presentation.Tests;

public sealed class PageContextTests
{
    [Fact]
    public async Task ItSendsToTheCoreAndOpensPagesInTheBrowser()
    {
        var sink = new RecordingSink();
        var browser = new RecordingBrowser();
        var context = new PageContext(sink, browser);

        context.Send(new UiEvent.Refresh());
        Assert.True(await context.OpenAsync("https://example.com/"));

        Assert.Equal([new UiEvent.Refresh()], sink.Sent);
        Assert.Equal(["https://example.com/"], browser.Opened);
    }

    [Fact]
    public void ItKeepsWhatThePagesShare()
    {
        var (context, _) = Pages.Context();
        Assert.False(context.ReportSending);
        Assert.False(context.DialogOpen);
        Assert.False(context.CallsAvailable);

        context.ReportSending = true;
        context.DialogOpen = true;
        context.CallsAvailable = true;

        Assert.True(context.ReportSending);
        Assert.True(context.DialogOpen);
        Assert.True(context.CallsAvailable);
    }
}

public sealed class LoadStateViewModelTests
{
    [Fact]
    public void Loading()
    {
        var load = new LoadStateViewModel();
        load.Show(V.Loading, hasRows: false, new EmptyView("Nothing", "Yet"), refreshing: true, refreshFailure: null);

        Assert.True(load.Loading);
        Assert.False(load.Failed);
        Assert.False(load.Ready);
        Assert.False(load.ShowEmpty);
        // A refresh shows only over loaded content.
        Assert.False(load.Refreshing);
        Assert.Equal(string.Empty, load.FailureTitle);
        Assert.Equal(string.Empty, load.FailureMessage);
        Assert.False(load.HasRegionsLine);
        Assert.False(load.CanRetry);
    }

    [Fact]
    public void AFailedLoadThatMayWorkAgain()
    {
        var load = new LoadStateViewModel();
        load.Show(V.Failed(V.Failure("No answer.", "Affected regions: EU", retryable: true), "Could not load this call"), false, null, false, null);

        Assert.True(load.Failed);
        Assert.False(load.Loading);
        Assert.Equal("Could not load this call", load.FailureTitle);
        Assert.Equal("No answer.", load.FailureMessage);
        Assert.Equal("Affected regions: EU", load.RegionsLine);
        Assert.True(load.HasRegionsLine);
        Assert.True(load.CanRetry);
    }

    [Fact]
    public void AFailedLoadWithNoRegionsAndNoRetry()
    {
        var load = new LoadStateViewModel();
        load.Show(V.Failed(V.Failure("No.")), false, null, false, null);
        Assert.Equal(string.Empty, load.RegionsLine);
        Assert.False(load.HasRegionsLine);
        Assert.False(load.CanRetry);
    }

    [Fact]
    public void ReadyAndEmpty()
    {
        var load = new LoadStateViewModel();
        load.Show(V.Ready, hasRows: false, new EmptyView("No calls yet", "They show here."), refreshing: true, refreshFailure: null);

        Assert.True(load.Ready);
        Assert.True(load.ShowEmpty);
        Assert.Equal("No calls yet", load.EmptyTitle);
        Assert.Equal("They show here.", load.EmptyBody);
        Assert.True(load.Refreshing);
        Assert.False(load.HasRefreshFailure);
        Assert.Equal(string.Empty, load.RefreshFailureMessage);
        Assert.False(load.CanRetryRefresh);
    }

    [Fact]
    public void ReadyWithRowsOrWithNothingToSayIsNotEmpty()
    {
        var load = new LoadStateViewModel();
        load.Show(V.Ready, hasRows: true, new EmptyView("No calls yet", string.Empty), false, null);
        Assert.False(load.ShowEmpty);

        load.Show(V.Ready, hasRows: false, empty: null, false, null);
        Assert.False(load.ShowEmpty);
        Assert.Equal(string.Empty, load.EmptyTitle);
        Assert.Equal(string.Empty, load.EmptyBody);
    }

    [Fact]
    public void ARefreshThatFailedOverLoadedContent()
    {
        var load = new LoadStateViewModel();
        load.Show(V.Ready, true, null, false, V.Failure("Offline.", retryable: true));
        Assert.True(load.HasRefreshFailure);
        Assert.Equal("Offline.", load.RefreshFailureMessage);
        Assert.True(load.CanRetryRefresh);

        load.Show(V.Ready, true, null, false, V.Failure("Gone."));
        Assert.False(load.CanRetryRefresh);
    }

    [Fact]
    public void TryAgainAsksTheCoreToRefresh()
    {
        var load = new LoadStateViewModel();
        // Before the page is attached, nothing to send to.
        load.RetryCommand.Execute(null);

        var (context, sink) = Pages.Context();
        load.Attach(context);
        load.RetryCommand.Execute(null);
        Assert.Equal([new UiEvent.Refresh()], sink.Sent);
    }
}

public sealed class PagingViewModelTests
{
    [Fact]
    public void LoadMoreIsOfferedAndEnabledWhileNothingIsOnItsWay()
    {
        var paging = new PagingViewModel(() => new UiEvent.LoadMoreCalls());
        paging.Show(V.Paging(canLoadMore: true));
        Assert.True(paging.CanLoadMore);
        Assert.False(paging.LoadingMore);
        Assert.True(paging.LoadMoreEnabled);
        Assert.False(paging.HasMoreFailure);
        Assert.Equal(string.Empty, paging.MoreFailureMessage);

        paging.Show(V.Paging(canLoadMore: true, loadingMore: true));
        Assert.True(paging.LoadingMore);
        Assert.False(paging.LoadMoreEnabled);

        paging.Show(V.Paging(canLoadMore: false, moreFailure: V.Failure("Could not load more.")));
        Assert.False(paging.LoadMoreEnabled);
        Assert.True(paging.HasMoreFailure);
        Assert.Equal("Could not load more.", paging.MoreFailureMessage);
    }

    [Fact]
    public void OneClickIsOneRequest()
    {
        var (context, sink) = Pages.Context();
        var paging = new PagingViewModel(() => new UiEvent.LoadMoreCalls());
        paging.Attach(context);
        paging.Show(V.Paging(canLoadMore: true));

        paging.LoadMoreCommand.Execute(null);
        paging.LoadMoreCommand.Execute(null);

        Assert.Equal([new UiEvent.LoadMoreCalls()], sink.Sent);
        Assert.False(paging.LoadMoreEnabled);
    }

    [Fact]
    public void LoadMoreDisabledOrUnattachedSendsNothing()
    {
        var paging = new PagingViewModel(() => new UiEvent.LoadMoreCalls());
        paging.Show(V.Paging(canLoadMore: true));
        // Enabled, but not attached yet.
        paging.LoadMoreCommand.Execute(null);
        Assert.False(paging.LoadMoreEnabled);

        var (context, sink) = Pages.Context();
        paging.Attach(context);
        paging.Show(V.Paging(canLoadMore: false));
        paging.LoadMoreCommand.Execute(null);
        Assert.Empty(sink.Sent);
    }
}
