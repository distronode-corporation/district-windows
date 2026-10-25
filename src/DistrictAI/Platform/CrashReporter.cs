using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using DistrictAI.Core;
using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels;
using Sentry;
using Sentry.Protocol;

namespace DistrictAI.Platform;

/// <summary>
/// What the user's answer about crash reports drives (<see cref="CrashReports"/>),
/// on both sides of the app, from one answer:
/// <list type="bullet">
/// <item>The Rust side: the answer goes to district_ffi's
/// <see cref="DistrictFfi.CrashReportsApply"/>, which hands it to the core's
/// district-crash, so a panic in district_ffi.dll is reported.</item>
/// <item>The .NET side: the Sentry .NET SDK, set up only when the Rust side
/// accepted the same DSN with consent, and closed (sending what it had
/// queued) as soon as the answer is Denied. It reports unhandled exceptions
/// (the SDK's own AppDomain.UnhandledException and WinUI
/// Application.UnhandledException handlers, and <see cref="UiFaults.Reporter"/>
/// for what escapes work on the UI thread, which WinUI ends the app over
/// without reaching either) with no personal data, no breadcrumbs, no
/// sessions, no assembly list and nothing written to disk, and every report
/// passes through <see cref="CrashScrub.Event"/> before it is sent.</item>
/// </list>
/// The DSN and the commit are the build's: DistrictAI.csproj stamps them on
/// this assembly from WINDOWS_SENTRY_DSN and DISTRICT_AI_BUILD in the
/// environment of the build, which only release.yml sets, and only for a tag.
/// Every other build has no DSN, so <see cref="CrashReports.Available"/> is
/// false and nothing here is ever set up.
/// </summary>
internal sealed class CrashReporter : ICrashReporter, IDisposable
{
    /// <summary>The assembly metadata key DistrictAI.csproj writes the DSN under.</summary>
    internal const string DsnKey = "DistrictAI.CrashReportsDsn";

    /// <summary>The assembly metadata key DistrictAI.csproj writes the commit's first twelve characters under.</summary>
    internal const string BuildKey = "DistrictAI.Build";

    // How long quitting, or turning reports off, waits for what is queued.
    private const uint FlushMilliseconds = 2000;

    // How long an exception about to end the app waits for its report.
    private static readonly TimeSpan _crashFlush = TimeSpan.FromSeconds(2);

    private IDisposable? _dotnet;
    private Session? _session;

    /// <summary>What this build carries, for an app at <paramref name="version"/> (the package's).</summary>
    public static CrashSettings BuildSettings(string version)
    {
        var metadata = typeof(CrashReporter).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().ToList();
        string? Value(string key) => metadata.FirstOrDefault(entry => string.Equals(entry.Key, key, StringComparison.Ordinal))?.Value;
        return CrashReports.SettingsFor(Value(DsnKey), version, Value(BuildKey));
    }

    /// <inheritdoc/>
    public void Apply(CrashSettings settings, CrashConsent consent)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var status = CrashStatus.OffNoConsent;
        try
        {
            status = DistrictFfi.CrashReportsApply(settings, consent);
        }
        catch (CrashReportsException error)
        {
            Trace.TraceWarning($"District AI: crash reports are off: {error.Message}");
        }
        CloseDotnet();
        if (status != CrashStatus.On || settings.Dsn is not { } dsn)
        {
            return;
        }
        var session = new Session();
        try
        {
            _dotnet = SentrySdk.Init(options => Configure(options, settings, dsn, session));
            _session = session;
            UiFaults.Reporter = CaptureFault;
        }
        catch (ArgumentException error)
        {
            // A DSN the Rust side took and the .NET SDK does not: the .NET side stays off.
            Trace.TraceWarning($"District AI: crash reports from .NET are off: {error.Message}");
        }
    }

    /// <summary>As the app quits: the .NET side closed, then the Rust side's queue given its two seconds.</summary>
    public void Dispose()
    {
        CloseDotnet();
        _ = DistrictFfi.CrashReportsFlush(FlushMilliseconds);
    }

    // Closes the .NET side, which sends what it had already queued. The SDK's
    // WinUI handler stays attached to the Application after a close and
    // captures through the hub it was given "even if the hub has been
    // disabled" (its source says so), so the session's BeforeSend drops
    // anything captured from now on, whatever path it takes.
    private void CloseDotnet()
    {
        UiFaults.Reporter = null;
        if (_session is { } session)
        {
            session.Open = false;
        }
        _session = null;
        _dotnet?.Dispose();
        _dotnet = null;
    }

    // An exception escaping work on the UI thread, which is about to end the
    // app: reported as unhandled, and given its two seconds to leave before
    // WinUI fails fast. UiFaults catches anything this throws.
    private static void CaptureFault(Exception exception)
    {
        exception.Data[Mechanism.HandledKey] = false;
        exception.Data[Mechanism.MechanismKey] = "DistrictAI.UiFaults";
        _ = SentrySdk.CaptureException(exception);
        SentrySdk.Flush(_crashFlush);
    }

    // Set by hand rather than left to the SDK's defaults, which read a DSN, a
    // release and an environment from the environment, start a session at
    // start up and keep an installation id on disk.
    private static void Configure(SentryOptions options, CrashSettings settings, string dsn, Session session)
    {
        options.Dsn = dsn;
        options.Release = settings.Release;
        options.Environment = settings.Environment;
        // A desktop app: one scope for the whole process.
        options.IsGlobalModeEnabled = true;
        options.SendDefaultPii = false;
        options.IsEnvironmentUser = false;
        // Nothing is sent at start up or on a clean quit.
        options.AutoSessionTracking = false;
        options.SendClientReports = false;
        // Nothing kept on disk: no offline cache, no installation id file.
        options.CacheDirectoryPath = null;
        options.DisableFileWrite = true;
        // Only the error and where it happened: no breadcrumbs, no list of the
        // loaded assemblies, nothing about HTTP requests or metrics.
        options.MaxBreadcrumbs = 0;
        options.ReportAssembliesMode = ReportAssembliesMode.None;
        options.CaptureFailedRequests = false;
        options.DisableDiagnosticSourceIntegration();
        options.DisableSystemDiagnosticsMetricsIntegration();
        options.SetBeforeSend(report => session.Open ? Scrubbed(report) : null);
    }

    /// <summary>
    /// <paramref name="report"/> as it may leave the machine: written out as
    /// JSON, scrubbed by <see cref="CrashScrub.Event"/> (the core's rules), and
    /// read back. A report that does not survive that is dropped, never sent
    /// unscrubbed: the SDK sends the original when this throws, so nothing may
    /// escape it.
    /// </summary>
    private static SentryEvent? Scrubbed(SentryEvent report)
    {
        try
        {
            using var buffer = new MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer))
            {
                // No diagnostic logger: the SDK's own is off.
                report.WriteTo(writer, null);
            }
            if (JsonNode.Parse(buffer.ToArray()) is not JsonObject json)
            {
                return null;
            }
            CrashScrub.Event(json);
            using var document = JsonDocument.Parse(json.ToJsonString());
            return SentryEvent.FromJson(document.RootElement);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            Trace.TraceWarning($"District AI: a crash report was dropped: it could not be scrubbed ({error.GetType().Name})");
            return null;
        }
    }

    // One SDK set up for one Granted answer; closed for good once the answer
    // changes. Read on whatever thread a report is captured on.
    private sealed class Session
    {
        private volatile bool _open = true;

        public bool Open
        {
            get => _open;
            set => _open = value;
        }
    }
}
