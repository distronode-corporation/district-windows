using DistrictAI.Core.Ffi;
using Xunit;

namespace DistrictAI.Core.Tests;

/// <summary>A UI thread the test drains by hand.</summary>
internal sealed class ManualDispatcher : IUiDispatcher
{
    private readonly Queue<Action> _queue = new();
    private readonly Lock _lock = new();

    public bool Accepting { get; set; } = true;

    public int Pending
    {
        get
        {
            lock (_lock)
            {
                return _queue.Count;
            }
        }
    }

    public bool TryEnqueue(Action work)
    {
        lock (_lock)
        {
            if (!Accepting)
            {
                return false;
            }
            _queue.Enqueue(work);
            return true;
        }
    }

    public void Drain()
    {
        while (true)
        {
            Action? work;
            lock (_lock)
            {
                if (!_queue.TryDequeue(out work))
                {
                    return;
                }
            }
            work();
        }
    }
}

/// <summary>A browser that records each page and takes it.</summary>
internal sealed class RecordingBrowser : IBrowser
{
    private readonly List<string> _opened = [];
    private readonly Lock _lock = new();

    public IReadOnlyList<string> Opened
    {
        get
        {
            lock (_lock)
            {
                return [.. _opened];
            }
        }
    }

    public Task<bool> OpenAsync(string url)
    {
        lock (_lock)
        {
            _opened.Add(url);
        }
        return Task.FromResult(true);
    }
}

public sealed class CoreHostTests : IDisposable
{
    /// <summary>Credentials of their own, apart from any installed copy of the app.</summary>
    private const string Namespace = "DistrictAI.Core.Tests_0";

    private readonly string _dataDir = Path.Combine(Path.GetTempPath(), "district-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dataDir))
        {
            Directory.Delete(_dataDir, recursive: true);
        }
    }

    private static async Task Eventually(string what, Func<bool> done)
    {
        for (var i = 0; i < 500 && !done(); i++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
        Assert.True(done(), $"timed out waiting for {what}");
    }

    [Fact]
    public async Task BeforeStartingTheWindowReadsTheStartUpState()
    {
        await using var host = new CoreHost(new ManualDispatcher(), new RecordingBrowser());
        var snapshot = host.Current;
        Assert.Equal(0UL, snapshot.Revision);
        Assert.Equal(SessionPhase.Restoring, snapshot.Shell.Phase);
        var session = Assert.IsType<ScreenView.Session>(snapshot.Screen);
        Assert.Equal("Resuming your session.", session.View.Body);
    }

    [Fact]
    public async Task TheRealCoreSignsOutRendersOnceAndOpensTheSignInPage()
    {
        var dispatcher = new ManualDispatcher();
        var browser = new RecordingBrowser();
        await using var host = new CoreHost(dispatcher, browser);
        var rendered = new List<CoreSnapshot>();
        host.Changed += (_, snapshot) => rendered.Add(snapshot);

        host.Start(_dataDir, "0.1.0", "Test PC", Namespace);
        // Nothing in the store: the start-up check signs out without a request.
        await Eventually("signed out", () => host.Current.Shell.Phase == SessionPhase.SignedOut);
        await Eventually("a render queued", () => dispatcher.Pending > 0);
        // However many changes arrived, one render is queued at a time.
        Assert.Equal(1, dispatcher.Pending);
        dispatcher.Drain();
        var last = Assert.Single(rendered);
        Assert.Equal(SessionPhase.SignedOut, last.Shell.Phase);
        var welcome = Assert.IsType<ScreenView.Session>(last.Screen);
        Assert.Equal("Welcome to District AI", welcome.View.Title);
        Assert.True(welcome.View.SignIn);

        // Signing in asks the host to open the service's sign-in page.
        host.Send(new UiEvent.SignIn());
        await Eventually("the browser asked", () => browser.Opened.Count == 1);
        Assert.StartsWith("https://www.distronode.com/", browser.Opened[0], StringComparison.Ordinal);
        await Eventually("waiting for the browser", () =>
            host.Current.Screen is ScreenView.Session { View.Cancel: true });

        Assert.Equal(LinkKind.Handoff, host.OpenLink("districtai://handoff?n=1"));
        Assert.Equal(LinkKind.Unknown, host.OpenLink("https://example.com/"));

        host.Send(new UiEvent.CancelSignIn());
        await Eventually("signed out again", () => host.Current.Shell.Phase == SessionPhase.SignedOut);
        // Signed out, nothing rings here, so a sleep has nothing to wait for.
        await host.SuspendAsync().WaitAsync(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);
        host.Resume();
        Assert.Equal(SessionPhase.SignedOut, host.Current.Shell.Phase);
        Assert.True(File.Exists(Path.Combine(_dataDir, "device-id")));
    }

    [Fact]
    public async Task SleepingAndWakingBeforeTheStartAndAfterTheEndDoNothing()
    {
        var host = new CoreHost(new ManualDispatcher(), new RecordingBrowser());
        var cancel = TestContext.Current.CancellationToken;
        // Not started: the core has nothing to wait for.
        await host.SuspendAsync().WaitAsync(TimeSpan.FromMilliseconds(500), cancel);
        host.Resume();
        host.Start(_dataDir, "0.1.0", null, Namespace);
        await Eventually("signed out", () => host.Current.Shell.Phase == SessionPhase.SignedOut);
        await host.DisposeAsync();
        // Disposed: a notification that arrives late reaches nothing.
        await host.SuspendAsync().WaitAsync(TimeSpan.FromMilliseconds(500), cancel);
        host.Resume();
    }

    [Fact]
    public async Task ARenderTheUiThreadRefusedIsQueuedByTheNextChange()
    {
        var dispatcher = new ManualDispatcher { Accepting = false };
        await using var host = new CoreHost(dispatcher, new RecordingBrowser());
        var rendered = 0;
        host.Changed += (_, _) => rendered++;
        host.Start(_dataDir, "0.1.0", null, Namespace);
        await Eventually("signed out", () => host.Current.Shell.Phase == SessionPhase.SignedOut);
        Assert.Equal(0, dispatcher.Pending);

        dispatcher.Accepting = true;
        host.Send(new UiEvent.SignIn());
        await Eventually("a render queued", () => dispatcher.Pending > 0);
        // Signing in changes the snapshot twice (opening the browser, then
        // waiting for it, both with Cancel); drained after the second, one
        // render draws both.
        await Eventually("waiting for the browser", () =>
            host.Current.Screen is ScreenView.Session { View.Body: var body }
            && body.StartsWith("Waiting", StringComparison.Ordinal));
        dispatcher.Drain();
        Assert.Equal(1, rendered);
        // Rendering an unchanged snapshot again draws nothing.
        host.StateChanged(host.Current.Revision);
        dispatcher.Drain();
        Assert.Equal(1, rendered);
    }

    [Fact]
    public async Task StartingTwiceIsRefusedAndShuttingDownTwiceIsNot()
    {
        var host = new CoreHost(new ManualDispatcher(), new RecordingBrowser());
        host.Start(_dataDir, "0.1.0", null, Namespace);
        Assert.Throws<StartException.AlreadyStarted>(() => host.Start(_dataDir, "0.1.0", null, Namespace));
        await host.DisposeAsync();
        await host.DisposeAsync();
    }

    [Fact]
    public async Task WhatTheCoreAsksOfTheWindowIsRaisedOnTheUiThreadInOrder()
    {
        var dispatcher = new ManualDispatcher();
        await using var host = new CoreHost(dispatcher, new RecordingBrowser());
        var raised = new List<string>();
        host.NotificationRequested += (_, n) => raised.Add($"notify {n.Id} {n.Title} {n.Urgent} {string.Join(",", n.Actions.Select(a => a.Label + "=" + a.ActionId))}");
        host.NotificationWithdrawn += (_, id) => raised.Add($"withdraw {id}");
        host.RingtoneStarted += (_, _) => raised.Add("ring");
        host.RingtoneStopped += (_, _) => raised.Add("quiet");
        host.PresentWindowRequested += (_, _) => raised.Add("present");

        // As the core calls them, from its own threads.
        var ringing = new NotificationView(
            "call:call-1", "Incoming call", "Transferred from your AI receptionist.", true,
            [new NotificationActionView("Answer", "answer"), new NotificationActionView("Decline", "decline")]);
        await Task.Run(() =>
        {
            host.Notify(ringing);
            host.StartRingtone();
            host.PresentWindow();
            host.StopRingtone();
            host.Withdraw("call:call-1");
        }, TestContext.Current.CancellationToken);
        Assert.Empty(raised);
        Assert.Equal(5, dispatcher.Pending);

        dispatcher.Drain();
        Assert.Equal(
            [
                "notify call:call-1 Incoming call True Answer=answer,Decline=decline",
                "ring",
                "present",
                "quiet",
                "withdraw call:call-1",
            ],
            raised);
    }

    [Fact]
    public async Task WithNobodyListeningOrNoUiThreadNothingBreaks()
    {
        var dispatcher = new ManualDispatcher();
        await using var host = new CoreHost(dispatcher, new RecordingBrowser());
        host.Notify(new NotificationView("message:m-1", "New message", "Open District AI to read it.", false, []));
        host.Withdraw("message:m-1");
        host.StartRingtone();
        host.StopRingtone();
        host.PresentWindow();
        dispatcher.Drain();

        var raised = 0;
        host.RingtoneStarted += (_, _) => raised++;
        dispatcher.Accepting = false;
        host.StartRingtone();
        dispatcher.Drain();
        Assert.Equal(0, raised);
    }

    [Fact]
    public async Task ActivatingANotificationTheCoreNeverShowedDoesNothing()
    {
        await using var host = new CoreHost(new ManualDispatcher(), new RecordingBrowser());
        // Before the core starts, and for an id it never showed.
        host.ActivateNotification("call:call-1", "answer");
        host.Start(_dataDir, "0.1.0", null, Namespace);
        await Eventually("signed out", () => host.Current.Shell.Phase == SessionPhase.SignedOut);
        host.ActivateNotification("call:call-1", null);
        host.ActivateNotification("message:m-1", "decline");
        Assert.Equal(SessionPhase.SignedOut, host.Current.Shell.Phase);
    }

    [Fact]
    public void TheHostNeedsADispatcherAndABrowser()
    {
        Assert.Throws<ArgumentNullException>(() => new CoreHost(null!, new RecordingBrowser()));
        Assert.Throws<ArgumentNullException>(() => new CoreHost(new ManualDispatcher(), null!));
    }

    [Theory]
    [InlineData("districtai://auth?code=c&state=s", LinkKind.Auth)]
    [InlineData("districtai://handoff", LinkKind.Handoff)]
    [InlineData("districtai://elsewhere", LinkKind.Unknown)]
    public void TheLinkKindIsTheCores(string uri, LinkKind kind) =>
        Assert.Equal(kind, DistrictFfi.LinkKind(uri));
}
