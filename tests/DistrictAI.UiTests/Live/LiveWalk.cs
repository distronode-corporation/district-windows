using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

namespace DistrictAI.UiTests.Live;

/// <summary>What a check came to, and why.</summary>
internal sealed record Outcome(CheckResult Result, string Detail)
{
    public static Outcome Pass(string detail) => new(CheckResult.Pass, detail);

    public static Outcome Fail(string detail) => new(CheckResult.Fail, detail);

    public static Outcome NotAutomated(string detail) => new(CheckResult.NotAutomated, detail);

    public static Outcome Probe(string detail) => new(CheckResult.Probe, detail);
}

/// <summary>A failed expectation inside a check: its message is the check's detail.</summary>
internal sealed class CheckFailedException(string message) : Exception(message);

/// <summary>A missing precondition inside a check: NOT_AUTOMATED, with the reason.</summary>
internal sealed class PreconditionException(string message) : Exception(message);

/// <summary>
/// One run of the live walk against the signed-in app on the win-smoke VM:
/// its folder (DISTRICTAI_LIVE_DIR), the harness's config, the results file,
/// the deadline (DISTRICTAI_LIVE_DEADLINE), the crash watch and the app.
/// Each check runs through <see cref="Run"/>: its own time cap, try/catch,
/// a crash check after, and the app started again when it ended.
/// </summary>
internal sealed partial class LiveWalk : IDisposable
{
    /// <summary>How long before the deadline the walk stops starting checks, to sign out and write its results.</summary>
    public static readonly TimeSpan Reserve = TimeSpan.FromSeconds(120);

    private static readonly TimeSpan _startTimeout = TimeSpan.FromSeconds(90);

    private readonly CrashWatch _crashes = new();
    private readonly StreamWriter? _log;

    private LiveWalk(string folder)
    {
        Folder = folder;
        Shots = Path.Combine(folder, "screenshots");
        Directory.CreateDirectory(Shots);
        Config = LiveConfig.Read(Path.Combine(folder, "live-config.json"));
        Results = LiveResults.Open(Path.Combine(folder, "live-results.json"));
        Urls = new LaunchedUrls(Path.Combine(folder, "launched-urls.jsonl"));
        Deadline = ReadDeadline();
        _log = new StreamWriter(new FileStream(Path.Combine(folder, "live-walk.log"), FileMode.Append, FileAccess.Write, FileShare.Read)) { AutoFlush = true };
        InstalledApp.LogSink = line =>
        {
            lock (_log)
            {
                _log.WriteLine(line);
            }
        };
    }

    /// <summary>The live walk's folder: config in, results, screenshots and the log out.</summary>
    public string Folder { get; }

    /// <summary>Where screenshots go: screenshots/&lt;id&gt;-&lt;n&gt;.png.</summary>
    public string Shots { get; }

    /// <summary>The harness's live-config.json.</summary>
    public LiveConfig Config { get; }

    /// <summary>live-results.json.</summary>
    public LiveResults Results { get; }

    /// <summary>The URL shim's record of what the app opened in a browser.</summary>
    public LaunchedUrls Urls { get; }

    /// <summary>When the harness ends the walk.</summary>
    public DateTimeOffset Deadline { get; }

    /// <summary>The app being walked.</summary>
    public InstalledApp App { get; private set; } = null!;

    /// <summary>The app's main window's handle.</summary>
    public nint Handle { get; private set; }

    /// <summary>Time left to start checks in: the deadline less <see cref="Reserve"/>.</summary>
    public TimeSpan TimeLeft => Deadline - Reserve - DateTimeOffset.UtcNow;

    /// <summary>
    /// The walk in DISTRICTAI_LIVE_DIR, attached to the app the harness
    /// started (DISTRICTAI_LIVE_PID, or the one District AI process running),
    /// or the app started when none runs. When there is no app to walk,
    /// <paramref name="firstCheck"/> is recorded FAIL with why.
    /// </summary>
    public static LiveWalk Start(string firstCheck)
    {
        var folder = Environment.GetEnvironmentVariable("DISTRICTAI_LIVE_DIR");
        if (folder is not { Length: > 0 } || !Directory.Exists(folder))
        {
            throw new InvalidOperationException("DISTRICTAI_LIVE_DIR must name the live walk's folder (the harness makes it)");
        }
        var walk = new LiveWalk(folder);
        InstalledApp.Log(FormattableString.Invariant($"live walk in {folder}, run {walk.Config.RunId}, {walk.TimeLeft.TotalMinutes:F1} min to start checks in"));
        var running = InstalledApp.RunningProcessIds();
        var wanted = int.TryParse(Environment.GetEnvironmentVariable("DISTRICTAI_LIVE_PID"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var pid) ? pid : 0;
        try
        {
            if (wanted != 0 && running.Contains(wanted))
            {
                walk.Adopt(InstalledApp.Attach(wanted));
            }
            else if (running.Length == 1)
            {
                walk.Adopt(InstalledApp.Attach(running[0]));
            }
            else
            {
                walk.Relaunch("no single District AI process to attach to");
            }
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // No app to walk: the results still say so.
            InstalledApp.Log(error.ToString());
            _ = walk.Results.Record(firstCheck, CheckResult.Fail, $"the app's window did not show: {FirstPart(error.Message)}");
            walk.Finish();
            walk.Dispose();
            throw;
        }
        return walk;
    }

    /// <summary>
    /// Runs check <paramref name="id"/>: not started past the deadline
    /// (NOT_AUTOMATED), capped at <paramref name="cap"/> (or the time left),
    /// every exception its FAIL (or NOT_AUTOMATED for a missing
    /// precondition), and the crash record read after. With
    /// <paramref name="probe"/>, the app ending or the cap running out is
    /// PROBE rather than FAIL. The app is started again if it ended. Returns
    /// the record.
    /// </summary>
    public CheckRecord Run(string id, TimeSpan cap, Func<Check, Outcome> body, bool probe = false)
    {
        if (TimeLeft <= TimeSpan.Zero)
        {
            return Results.Record(id, CheckResult.NotAutomated, "not started: the walk's deadline was near");
        }
        if (!App.IsRunning)
        {
            Relaunch("it was not running before this check");
        }
        var limit = cap < TimeLeft ? cap : TimeLeft;
        InstalledApp.Log(FormattableString.Invariant($"--- {id} (cap {limit.TotalSeconds:F0} s)"));
        var clock = Stopwatch.StartNew();
        var check = new Check(this, id);
        using var cancel = new CancellationTokenSource(limit);
        var task = Task.Run(() =>
        {
            Wait.Token = cancel.Token;
            return body(check);
        });
        Outcome outcome;
        var stuck = false;
        var broke = false;
        try
        {
            if (task.Wait(limit + TimeSpan.FromSeconds(5)))
            {
                outcome = task.Result;
            }
            else
            {
                cancel.Cancel();
                stuck = true;
                outcome = new(probe ? CheckResult.Probe : CheckResult.Fail, FormattableString.Invariant($"ran past its {limit.TotalSeconds:F0} s cap"));
            }
        }
        catch (AggregateException error) when (error.InnerException is { } inner)
        {
            outcome = From(inner, limit, probe);
            broke = inner is not PreconditionException;
        }
        var crashes = _crashes.New();
        if (crashes.Length > 0 || !App.IsRunning || stuck)
        {
            var why = crashes.Length > 0 ? $"the app crashed ({crashes[0]})" : !App.IsRunning ? "the app ended" : "the check hung";
            outcome = new(probe ? CheckResult.Probe : CheckResult.Fail, $"{why}; {outcome.Detail}");
            Relaunch(why);
            if (stuck)
            {
                _ = task.Wait(TimeSpan.FromSeconds(15));
            }
        }
        else if (broke)
        {
            // A check that stopped part-way can leave a dialog or a form open:
            // the next starts from a fresh app (the session is kept).
            Relaunch($"{id} stopped part-way");
        }
        InstalledApp.Log(FormattableString.Invariant($"--- {id}: {LiveResults.Wire(outcome.Result)} in {clock.Elapsed.TotalSeconds:F0} s: {outcome.Detail}"));
        return Results.Record(id, outcome.Result, outcome.Detail, clock.Elapsed);
    }

    /// <summary>
    /// Ends every District AI process and starts the package again as the
    /// Start menu does (shell:AppsFolder); its session is in Credential
    /// Manager, so it comes back signed in.
    /// </summary>
    public void Relaunch(string why)
    {
        InstalledApp.Log($"starting the app again: {why}");
        App?.Dispose();
        InstalledApp.StopAll();
        using (Process.Start(new ProcessStartInfo($@"shell:AppsFolder\{InstalledApp.Aumid}") { UseShellExecute = true }))
        {
        }
        var pid = Wait.For(
            () => InstalledApp.RunningProcessIds() is [var only] ? (object)only : null,
            _startTimeout,
            "a District AI process",
            () => $"District AI processes: [{string.Join(", ", InstalledApp.RunningProcessIds())}]");
        Adopt(InstalledApp.Attach((int)pid));
    }

    /// <summary>Takes <paramref name="app"/> as the app walked: its window found, its client area 1920x1080.</summary>
    private void Adopt(InstalledApp app)
    {
        App = app;
        var window = app.MainWindow(_startTimeout);
        Handle = window.Properties.NativeWindowHandle.Value;
        try
        {
            Walk.SizeClient(Handle, 1920, 1080);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // Screenshots come out another size; the walk goes on.
            InstalledApp.Log($"the client area could not be made 1920x1080: {error.Message}");
        }
    }

    /// <summary>Marks the walk finished and writes the results.</summary>
    public void Finish() => Results.Finish();

    public void Dispose()
    {
        App?.Dispose();
        InstalledApp.LogSink = null;
        _log?.Dispose();
    }

    /// <summary>
    /// What an exception makes of a check: a missing precondition is
    /// NOT_AUTOMATED, a refused press always FAIL (the walk itself is wrong),
    /// anything else FAIL, or PROBE for a probe.
    /// </summary>
    private static Outcome From(Exception error, TimeSpan limit, bool probe)
    {
        var failed = probe ? CheckResult.Probe : CheckResult.Fail;
        return error switch
        {
            PreconditionException missing => Outcome.NotAutomated(missing.Message),
            RefusedPressException refused => Outcome.Fail(refused.Message),
            CheckFailedException expected => new(failed, expected.Message),
            OperationCanceledException => new(failed, FormattableString.Invariant($"ran past its {limit.TotalSeconds:F0} s cap")),
            TimeoutException timeout => new(failed, FirstPart(timeout.Message)),
            _ => new(failed, $"{error.GetType().Name}: {FirstPart(error.Message)}"),
        };
    }

    /// <summary>A wait's message up to its UI Automation dump (which goes to the log, not the result).</summary>
    private static string FirstPart(string message)
    {
        var dump = message.IndexOf(" UI Automation held:", StringComparison.Ordinal);
        if (dump > 0)
        {
            InstalledApp.Log(message);
            return message[..dump];
        }
        return message;
    }

    private static DateTimeOffset ReadDeadline()
    {
        if (long.TryParse(Environment.GetEnvironmentVariable("DISTRICTAI_LIVE_DEADLINE"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
        {
            return DateTimeOffset.FromUnixTimeSeconds(seconds);
        }
        // No deadline given: half an hour, the harness's run.
        return DateTimeOffset.UtcNow.AddMinutes(30);
    }

    /// <summary>Writes <paramref name="value"/> as JSON to <paramref name="path"/> whole (a temporary file moved over it).</summary>
    public static void WriteJson(string path, object value)
    {
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(value));
        File.Move(temporary, path, overwrite: true);
    }
}

/// <summary>A running check: its id, its screenshots, and the walk it is part of.</summary>
internal sealed class Check(LiveWalk walk, string id)
{
    private int _shots;

    /// <summary>The checklist id.</summary>
    public string Id { get; } = id;

    /// <summary>The walk.</summary>
    public LiveWalk Walk { get; } = walk;

    /// <summary>The app.</summary>
    public InstalledApp App => Walk.App;

    /// <summary>
    /// Saves the main window's client area as screenshots/&lt;id&gt;-&lt;n&gt;.png
    /// and lists it on the check, unless a link shows in the window: then
    /// nothing is saved. Never fails the check.
    /// </summary>
    public void Shot()
    {
        try
        {
            if (App.TryMainWindow() is not { } window)
            {
                return;
            }
            if (ShowsALink(window))
            {
                InstalledApp.Log($"{Id}: a link shows in the window, so no screenshot");
                return;
            }
            var name = FormattableString.Invariant($"{Id}-{++_shots}.png");
            UiTests.Walk.Save(Walk.Handle, Path.Combine(Walk.Shots, name), clientOnly: true);
            Walk.Results.For(Id).Screenshots.Add(name);
        }
        catch (Exception error) when (error is not OutOfMemoryException and not OperationCanceledException)
        {
            InstalledApp.Log($"{Id}: the screenshot failed: {error.Message}");
        }
    }

    /// <summary>Saves another of the app's windows (the checkout window) the same way.</summary>
    public void ShotOf(AutomationElement window)
    {
        try
        {
            if (ShowsALink(window))
            {
                InstalledApp.Log($"{Id}: a link shows in that window, so no screenshot");
                return;
            }
            var name = FormattableString.Invariant($"{Id}-{++_shots}.png");
            UiTests.Walk.Save(window.Properties.NativeWindowHandle.Value, Path.Combine(Walk.Shots, name), clientOnly: true);
            Walk.Results.For(Id).Screenshots.Add(name);
        }
        catch (Exception error) when (error is not OutOfMemoryException and not OperationCanceledException)
        {
            InstalledApp.Log($"{Id}: the screenshot failed: {error.Message}");
        }
    }

    /// <summary>Fails the check with <paramref name="detail"/> unless <paramref name="condition"/> holds.</summary>
    public static void Expect(bool condition, string detail)
    {
        if (!condition)
        {
            throw new CheckFailedException(detail);
        }
    }

    private static bool ShowsALink(AutomationElement window) =>
        window.FindAllDescendants(cf => cf.ByControlType(ControlType.Text).Or(cf.ByControlType(ControlType.Edit)).Or(cf.ByControlType(ControlType.Hyperlink)))
            .Where(element => !element.Properties.IsOffscreen.ValueOrDefault)
            .Any(element => InstalledApp.Redact(UiTests.Walk.NameOf(element)) != UiTests.Walk.NameOf(element)
                || (element.Patterns.Value.IsSupported && element.Patterns.Value.Pattern.Value.ValueOrDefault is { } value && InstalledApp.Redact(value) != value));
}
