using Xunit;

namespace DistrictAI.Spikes.Tests;

/// <summary>W2 spike 2, as tests: each fails with the numbers it measured.</summary>
public sealed class AsyncCallbackSpikeTests(ITestOutputHelper output)
{
    [Fact]
    public async Task TenThousandAsyncCallbacksWithNoDeadlockAndSteadyHandles()
    {
        var result = await AsyncCallbackSpike.RoundTrips();
        output.WriteLine(result.Detail);
        AsyncCallbackSpike.Record(result);
        Assert.True(result.Passed, result.Detail);
    }

    [Fact]
    public async Task ACleanShutdownWhileCallbacksAreInFlight()
    {
        var result = await AsyncCallbackSpike.ShutdownWhileInFlight();
        output.WriteLine(result.Detail);
        AsyncCallbackSpike.Record(result);
        Assert.True(result.Passed, result.Detail);
    }
}
