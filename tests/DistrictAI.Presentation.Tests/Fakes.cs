using DistrictAI.Core;
using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels;

namespace DistrictAI.Presentation.Tests;

/// <summary>Stands in for the core: keeps what the view models send.</summary>
internal sealed class RecordingSink : ICoreSink
{
    public List<UiEvent> Sent { get; } = [];

    public void Send(UiEvent uiEvent) => Sent.Add(uiEvent);
}

/// <summary>Stands in for the user's browser: keeps the pages it is asked to open.</summary>
internal sealed class RecordingBrowser : IBrowser
{
    public List<string> Opened { get; } = [];

    public Task<bool> OpenAsync(string url)
    {
        Opened.Add(url);
        return Task.FromResult(true);
    }
}

/// <summary>A <see cref="PageContext"/> over a recorder, and the recorder.</summary>
internal static class Pages
{
    public static (PageContext Context, RecordingSink Sink) Context(bool callsAvailable = false)
    {
        var sink = new RecordingSink();
        var context = new PageContext(sink, new RecordingBrowser()) { CallsAvailable = callsAvailable };
        return (context, sink);
    }
}

/// <summary>
/// A clock that moves only when a test says so, and timers that fire, on the
/// test's thread, as it moves past them.
/// </summary>
internal sealed class ManualTime(DateTimeOffset start) : TimeProvider
{
    private readonly List<ManualTimer> _timers = [];

    public DateTimeOffset Now { get; private set; } = start;

    public int TimersCreated => _timers.Count;

    public override DateTimeOffset GetUtcNow() => Now;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        timer.Change(dueTime, period);
        _timers.Add(timer);
        return timer;
    }

    /// <summary>Moves the clock on by <paramref name="by"/>, firing each timer due on the way, in order.</summary>
    public void Advance(TimeSpan by)
    {
        var until = Now + by;
        while (true)
        {
            var next = _timers.Where(t => t.Due is { } due && due <= until).OrderBy(t => t.Due).FirstOrDefault();
            if (next is null)
            {
                break;
            }
            Now = next.Due!.Value;
            next.Fire();
        }
        Now = until;
    }

    private sealed class ManualTimer(ManualTime time, TimerCallback callback, object? state) : ITimer
    {
        private TimeSpan _period = Timeout.InfiniteTimeSpan;

        public DateTimeOffset? Due { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            Due = dueTime == Timeout.InfiniteTimeSpan ? null : time.Now + dueTime;
            _period = period;
            return true;
        }

        public void Fire()
        {
            Due = _period == Timeout.InfiniteTimeSpan ? null : Due + _period;
            callback(state);
        }

        public void Dispose() => Due = null;

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}

/// <summary>A synchronization context that only queues what is posted to it, until the test runs it.</summary>
internal sealed class QueueingContext : SynchronizationContext
{
    private readonly Queue<(SendOrPostCallback Work, object? State)> _posted = new();

    public int Pending => _posted.Count;

    public override void Post(SendOrPostCallback d, object? state) => _posted.Enqueue((d, state));

    public void RunAll()
    {
        while (_posted.TryDequeue(out var item))
        {
            item.Work(item.State);
        }
    }

    /// <summary>Runs <paramref name="body"/> with this as the thread's context, then puts the old one back.</summary>
    public void Within(Action body)
    {
        var previous = Current;
        SetSynchronizationContext(this);
        try
        {
            body();
        }
        finally
        {
            SetSynchronizationContext(previous);
        }
    }
}

/// <summary>Start at sign-in, answered by the test.</summary>
internal sealed class FakeStartupTask : IStartupTask
{
    public StartupSetting Setting { get; set; } = new(Enabled: false, CanChange: true, Message: null);

    /// <summary>When set, the next call waits for it instead of answering at once.</summary>
    public TaskCompletionSource<StartupSetting>? Pending { get; set; }

    public int Gets { get; private set; }

    public List<bool> Sets { get; } = [];

    public Task<StartupSetting> GetAsync()
    {
        Gets++;
        return Answer();
    }

    public Task<StartupSetting> SetAsync(bool enabled)
    {
        Sets.Add(enabled);
        Setting = Setting with { Enabled = enabled };
        return Answer();
    }

    private Task<StartupSetting> Answer()
    {
        if (Pending is { } pending)
        {
            Pending = null;
            return pending.Task;
        }
        return Task.FromResult(Setting);
    }
}

/// <summary>The core's view records, with defaults, so each test names only what it is about.</summary>
internal static class V
{
    public static readonly LoadStatus Loading = new LoadStatus.Loading();
    public static readonly LoadStatus Ready = new LoadStatus.Ready();

    public static FailureView Failure(string message = "Something went wrong.", string? regions = null, bool retryable = false) =>
        new(message, regions, retryable);

    public static LoadStatus Failed(FailureView? failure = null, string title = "Could not load this") =>
        new LoadStatus.Failed(failure ?? Failure(), title);

    public static PagingView Paging(
        bool canLoadMore = false,
        bool loadingMore = false,
        FailureView? moreFailure = null,
        bool refreshing = false,
        FailureView? refreshFailure = null) =>
        new(canLoadMore, loadingMore, moreFailure, refreshing, refreshFailure);

    public static CallRowView CallRow(string id = "call-1", string title = "Alex", string detail = "Inbound", string? summary = null, string startedAt = "2020-01-02T03:04:05Z") =>
        new(id, title, detail, summary, startedAt, Inbound: true, Missed: false);

    public static ThreadRowView ThreadRow(string key = "t-1", bool canOpen = true, bool unread = false, string? lastAt = null) =>
        new(key, "Alex", "Hello", "Text message", unread, lastAt, canOpen);

    public static SearchHitView Hit(string key = "t-1", bool canOpen = true, string? at = null) =>
        new(key, "Alex", "the match", at, canOpen);

    public static SearchView Search(
        string query = "",
        bool active = false,
        bool running = false,
        SearchHitView[]? hits = null,
        bool truncated = false,
        FailureView? failure = null,
        uint minQueryLength = 3,
        EmptyView? empty = null,
        string? truncatedNote = null) =>
        new(query, active, running, hits ?? [], truncated, failure, minQueryLength, empty, truncatedNote);

    public static ActiveCallView Call(
        string peer = "+1 212 555 0100",
        string state = "Calling.",
        string? connectedAt = null,
        bool muted = false,
        bool canHangUp = true,
        string? ended = null,
        FailureView? failure = null,
        bool microphoneDenied = false,
        bool canMute = true,
        string? endedNote = null,
        string? mediaNotice = null,
        TranscriptView? transcript = null) =>
        new(null, peer, state, connectedAt, muted, canHangUp, ended, failure, microphoneDenied, Outbound: true, canMute, endedNote, mediaNotice, transcript);
}
