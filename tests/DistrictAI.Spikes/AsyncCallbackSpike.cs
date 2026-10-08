using System.Diagnostics;
using DistrictAI.Spikes.Ffi;

namespace DistrictAI.Spikes;

/// <summary>What one run of a check measured.</summary>
/// <param name="Name">The check.</param>
/// <param name="Passed">Whether it met every bound.</param>
/// <param name="Detail">The numbers, for the job summary.</param>
public sealed record SpikeResult(string Name, bool Passed, string Detail);

/// <summary>
/// W2 spike 2: a UniFFI async call that awaits a C# async callback 10,000
/// times with no deadlock and a steady handle count, and a clean shutdown
/// while callbacks are in flight.
/// </summary>
public static class AsyncCallbackSpike
{
    /// <summary>How long a check may take before it counts as a deadlock.</summary>
    public static readonly TimeSpan Deadline = TimeSpan.FromMinutes(2);

    /// <summary>The most the process's handle count may grow over a run.</summary>
    public const int HandleSlack = 64;

    /// <summary>Answers <c>value + 1</c> after yielding, so every callback really is asynchronous.</summary>
    private sealed class YieldingEcho : EchoHost
    {
        public async Task<ulong> Echo(ulong value)
        {
            await Task.Yield();
            return value + 1;
        }
    }

    /// <summary>Answers after a short delay, and counts the callbacks still running.</summary>
    private sealed class SlowEcho : EchoHost
    {
        private int _running;
        private long _completed;

        public int Running => Volatile.Read(ref _running);

        public long Completed => Interlocked.Read(ref _completed);

        public async Task<ulong> Echo(ulong value)
        {
            Interlocked.Increment(ref _running);
            try
            {
                await Task.Delay(1 + (int)(value % 5));
                return value + 1;
            }
            finally
            {
                Interlocked.Decrement(ref _running);
                Interlocked.Increment(ref _completed);
            }
        }
    }

    /// <summary>
    /// The process's open handles: Windows handles, or file descriptors on Linux.
    /// </summary>
    public static int HandleCount()
    {
        if (OperatingSystem.IsLinux())
        {
            return Directory.GetFiles("/proc/self/fd").Length;
        }
        using var self = Process.GetCurrentProcess();
        return self.HandleCount;
    }

    private static void Settle()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    /// <summary>
    /// <paramref name="count"/> round trips one after another, then the same
    /// again split over <paramref name="parallel"/> concurrent calls.
    /// </summary>
    public static async Task<SpikeResult> RoundTrips(ulong count = 10_000, int parallel = 8)
    {
        var host = new YieldingEcho();
        await DistrictSpikes.RoundTrips(host, 100).WaitAsync(Deadline);
        Settle();
        var before = HandleCount();

        var clock = Stopwatch.StartNew();
        var done = await DistrictSpikes.RoundTrips(host, count).WaitAsync(Deadline);
        var sequential = clock.Elapsed;

        clock.Restart();
        var each = count / (ulong)parallel;
        var totals = await Task.WhenAll(Enumerable.Range(0, parallel)
            .Select(_ => DistrictSpikes.RoundTrips(host, each))).WaitAsync(Deadline);
        var concurrent = clock.Elapsed;

        Settle();
        var after = HandleCount();
        var passed = done == count && totals.All(t => t == each) && after - before <= HandleSlack;
        return new SpikeResult(
            "async callback round trips",
            passed,
            $"{done} sequential round trips in {sequential.TotalMilliseconds:F0} ms " +
            $"({sequential.TotalMicroseconds / count:F1} us each); {each * (ulong)parallel} more over " +
            $"{parallel} concurrent calls in {concurrent.TotalMilliseconds:F0} ms; handles {before} -> {after} " +
            $"(allowed growth {HandleSlack})");
    }

    /// <summary>
    /// <paramref name="tasks"/> loops of callbacks kept in flight, stopped
    /// mid-await; then every C# callback must still finish, nothing may throw
    /// unobserved, and round trips must still work afterwards.
    /// </summary>
    public static async Task<SpikeResult> ShutdownWhileInFlight(uint tasks = 64)
    {
        var unobserved = 0;
        void Count(object? sender, UnobservedTaskExceptionEventArgs args) => Interlocked.Increment(ref unobserved);
        TaskScheduler.UnobservedTaskException += Count;
        try
        {
            Settle();
            var before = HandleCount();
            var host = new SlowEcho();
            var clock = Stopwatch.StartNew();
            FloodReport stopped;
            using (var flood = new Flood(host, tasks))
            {
                await Task.Delay(500);
                var inFlight = host.Running;
                stopped = await flood.Stop(2_000).WaitAsync(Deadline);
                var stopTime = clock.Elapsed;
                // The callbacks Rust stopped waiting for still run to their end
                // in C#; their answers go nowhere, and must not crash anything.
                var drain = Stopwatch.StartNew();
                while (host.Running > 0 && drain.Elapsed < TimeSpan.FromSeconds(10))
                {
                    await Task.Delay(10);
                }
                var after = await DistrictSpikes.RoundTrips(new YieldingEcho(), 1_000).WaitAsync(Deadline);
                Settle();
                var handlesAfter = HandleCount();
                var passed = host.Running == 0 && after == 1_000 && Volatile.Read(ref unobserved) == 0
                    && stopped.Answered > 0 && handlesAfter - before <= HandleSlack;
                return new SpikeResult(
                    "shutdown with callbacks in flight",
                    passed,
                    $"{tasks} loops; {inFlight} callbacks running when stopped; {stopped.Started} started, " +
                    $"{stopped.Answered} answered before the stop; stopped {stopTime.TotalMilliseconds:F0} ms after start; " +
                    $"C# callbacks still running after drain: {host.Running}; unobserved exceptions: {Volatile.Read(ref unobserved)}; " +
                    $"1,000 round trips afterwards: {after}; handles {before} -> {handlesAfter}");
            }
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= Count;
        }
    }

    /// <summary>Appends <paramref name="result"/> to the file <c>SPIKE_RESULTS</c> names, if any.</summary>
    public static void Record(SpikeResult result)
    {
        var path = Environment.GetEnvironmentVariable("SPIKE_RESULTS");
        if (!string.IsNullOrEmpty(path))
        {
            File.AppendAllText(path, $"| {result.Name} | {(result.Passed ? "pass" : "FAIL")} | {result.Detail} |{Environment.NewLine}");
        }
    }
}
