using DistrictAI.Core;
using DistrictAI.Core.Ffi;
using DistrictAI.Spikes;

// W2 spike 6. Exits 0 only if every check passes under Native AOT.
var results = new List<SpikeResult>
{
    await RealCore(),
    await AsyncCallbackSpike.RoundTrips(),
    await AsyncCallbackSpike.ShutdownWhileInFlight(),
};
foreach (var result in results)
{
    Console.WriteLine($"{(result.Passed ? "pass" : "FAIL")}: {result.Name}: {result.Detail}");
    AsyncCallbackSpike.Record(result with { Name = "AOT: " + result.Name });
}
return results.All(r => r.Passed) ? 0 : 1;

// The app's own path through the bindings: start, a projection, a sign-in
// that calls the async C# browser callback, a link, and shutdown.
static async Task<SpikeResult> RealCore()
{
    var dataDir = Path.Combine(Path.GetTempPath(), "district-aot-" + Guid.NewGuid().ToString("N"));
    var browser = new Browser();
    var host = new CoreHost(new Inline(), browser);
    try
    {
        host.Start(dataDir, "0.1.0", "AOT probe", "DistrictAI.AotProbe_0");
        var signedOut = await Until(() => host.Current.Shell.Phase == SessionPhase.SignedOut);
        host.Send(new UiEvent.SignIn());
        var opened = await Until(() => browser.Opened is not null);
        var link = host.OpenLink("districtai://handoff?n=1");
        var passed = signedOut && opened && link == LinkKind.Handoff
            && browser.Opened!.StartsWith("https://www.distronode.com/", StringComparison.Ordinal);
        return new SpikeResult("real core through DistrictAI.Core", passed,
            $"signed out: {signedOut}; browser asked: {opened}; handoff link: {link}; revision {host.Current.Revision}");
    }
    finally
    {
        await host.DisposeAsync();
        if (Directory.Exists(dataDir))
        {
            Directory.Delete(dataDir, recursive: true);
        }
    }
}

static async Task<bool> Until(Func<bool> done)
{
    for (var i = 0; i < 500 && !done(); i++)
    {
        await Task.Delay(10);
    }
    return done();
}

internal sealed class Inline : IUiDispatcher
{
    public bool TryEnqueue(Action work)
    {
        ThreadPool.QueueUserWorkItem(_ => work());
        return true;
    }
}

internal sealed class Browser : IBrowser
{
    public string? Opened { get; private set; }

    public Task<bool> OpenAsync(string url)
    {
        Opened = url;
        return Task.FromResult(true);
    }
}
