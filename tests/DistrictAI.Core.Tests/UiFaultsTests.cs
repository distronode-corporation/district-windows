using System.Diagnostics;
using Xunit;

namespace DistrictAI.Core.Tests;

/// <summary>Keeps what is written to the trace while it is listening.</summary>
internal sealed class TraceRecorder : TraceListener
{
    private readonly List<string> _lines = [];
    private readonly Lock _lock = new();

    public TraceRecorder() => Trace.Listeners.Add(this);

    /// <summary>Everything written that names <paramref name="marker"/>.</summary>
    public string[] Naming(string marker)
    {
        lock (_lock)
        {
            return [.. _lines.Where(line => line.Contains(marker, StringComparison.Ordinal))];
        }
    }

    public override void Write(string? message) => WriteLine(message);

    public override void WriteLine(string? message)
    {
        lock (_lock)
        {
            _lines.Add(message ?? string.Empty);
        }
    }

    protected override void Dispose(bool disposing)
    {
        Trace.Listeners.Remove(this);
        base.Dispose(disposing);
    }
}

/// <summary>Runs what is posted to it at once, on the caller's thread.</summary>
internal sealed class InlineContext : SynchronizationContext
{
    public int Posted { get; private set; }

    public override void Post(SendOrPostCallback d, object? state)
    {
        Posted++;
        d(state);
    }

    public override void Send(SendOrPostCallback d, object? state) => d(state);

    public override SynchronizationContext CreateCopy() => new InlineContext();
}

public sealed class UiFaultsTests
{
    private static Exception Thrown(Func<Exception> make)
    {
        try
        {
            throw make();
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    [Fact]
    public void AnAccountNamesEachExceptionItsHResultMessageAndStack()
    {
        var inner = Thrown(() => new FormatException("the inner one"));
        var outer = Thrown(() => new InvalidOperationException("the outer one", inner));
        var text = UiFaults.Describe("drawing a page", outer);
        Assert.StartsWith("District AI: an exception escaped drawing a page", text, StringComparison.Ordinal);
        Assert.Contains("System.InvalidOperationException, HResult 0x80131509: the outer one", text, StringComparison.Ordinal);
        Assert.Contains("  inner: System.FormatException, HResult 0x80131537: the inner one", text, StringComparison.Ordinal);
        Assert.Contains(nameof(Thrown), text, StringComparison.Ordinal);

        // All of an aggregate's, each in turn; one never thrown has no stack.
        var aggregate = new AggregateException("both", new ArgumentException("first"), new TimeoutException("second"));
        var lines = UiFaults.Describe("a task", aggregate).Split(Environment.NewLine);
        Assert.Equal(
            [
                "District AI: an exception escaped a task",
                "System.AggregateException, HResult 0x80131500: both (first) (second)",
                "  inner: System.ArgumentException, HResult 0x80070057: first",
                "  inner: System.TimeoutException, HResult 0x80131505: second",
            ],
            lines);
        Assert.Throws<ArgumentNullException>(() => UiFaults.Describe("x", null!));
        Assert.Throws<ArgumentNullException>(() => UiFaults.Run("x", null!));
    }

    [Fact]
    public void WorkThatThrowsIsReportedAndThrownOnUnchanged()
    {
        using var trace = new TraceRecorder();
        var marker = Guid.NewGuid().ToString("N");
        var thrown = new InvalidOperationException(marker);
        var caught = Assert.Throws<InvalidOperationException>(() => UiFaults.Run("a test", () => throw thrown));
        Assert.Same(thrown, caught);
        var reported = Assert.Single(trace.Naming(marker));
        Assert.Contains("an exception escaped a test", reported, StringComparison.Ordinal);

        // Work that does not throw writes nothing.
        var ran = false;
        UiFaults.Run(marker, () => ran = true);
        Assert.True(ran);
        Assert.Single(trace.Naming(marker));
    }

    [Fact]
    public void AContinuationThatThrowsIsReportedAndThrownOnByTheUiThreadsContext()
    {
        Assert.Throws<ArgumentNullException>(() => new ReportingSynchronizationContext(null!));
        using var trace = new TraceRecorder();
        var inner = new InlineContext();
        var context = new ReportingSynchronizationContext(inner);
        var marker = Guid.NewGuid().ToString("N");

        object? seen = null;
        context.Post(state => seen = state, "posted");
        context.Send(state => seen = state, "sent");
        Assert.Equal("sent", seen);
        Assert.Equal(1, inner.Posted);

        // .NET posts an async void method's exception to the context to be thrown.
        Assert.Throws<InvalidOperationException>(() => context.Post(_ => throw new InvalidOperationException(marker), null));
        Assert.Throws<InvalidOperationException>(() => context.Send(_ => throw new InvalidOperationException(marker), null));
        Assert.Equal(2, trace.Naming(marker).Length);
        Assert.Contains("an exception escaped a continuation on the UI thread", trace.Naming(marker)[0], StringComparison.Ordinal);
        Assert.IsType<ReportingSynchronizationContext>(context.CreateCopy());
    }
}
