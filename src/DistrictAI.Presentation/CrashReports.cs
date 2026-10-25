using DistrictAI.Core.Ffi;

namespace DistrictAI.ViewModels;

/// <summary>
/// Where the user's answer about crash reports is kept. The app's is the
/// package's local settings (<c>CrashAnswerStore</c>); the tests' is a fake.
/// </summary>
public interface ICrashAnswerStore
{
    /// <summary>The answer kept, or null until the user has given one (or when what is kept is not an answer).</summary>
    CrashConsent? Read();

    /// <summary>Keeps <paramref name="consent"/> as the answer; false when it could not be kept.</summary>
    bool Write(CrashConsent consent);
}

/// <summary>
/// What an answer drives. The app's (<c>CrashReporter</c>) hands it to the
/// Rust side through <see cref="DistrictFfi.CrashReportsApply"/> and starts or
/// stops the Sentry .NET SDK; the tests' records it.
/// </summary>
public interface ICrashReporter
{
    /// <summary>Turns reporting on (only with <see cref="CrashConsent.Granted"/> and a DSN) or off.</summary>
    void Apply(CrashSettings settings, CrashConsent consent);
}

/// <summary>
/// Crash reports, off until the user says yes. One per app: the window asks
/// the question (<see cref="ShouldAsk"/>) once, the first time it shows, and
/// the account page's "Send crash reports" switch reads and changes the
/// answer. Every change goes to the reporter at once, so both the Rust side
/// and the .NET side follow one answer.
/// <list type="bullet">
/// <item>A build with no DSN (built from source, a fork's, any CI build that
/// is not a release) is not <see cref="Available"/>: it never asks, shows no
/// switch, and its reporter is never set up.</item>
/// <item>No answer kept is no answer, so a new user and a user upgrading from
/// a version that never asked are both asked once. "Don't send" is the
/// question's default and what closing it answers.</item>
/// <item>An answer that cannot be kept still holds until the app quits.</item>
/// </list>
/// </summary>
public sealed class CrashReports
{
    /// <summary>The app as reports name it, before the version.</summary>
    public const string AppId = "com.distronode.DistrictAI";

    /// <summary>The environment reports are filed under.</summary>
    public const string ReportEnvironment = "production";

    private readonly ICrashAnswerStore _store;
    private readonly ICrashReporter _reporter;
    private CrashConsent? _answer;

    /// <summary>Crash reports for a build described by <paramref name="settings"/>, the answer kept in <paramref name="store"/>.</summary>
    public CrashReports(CrashSettings settings, ICrashAnswerStore store, ICrashReporter reporter)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(reporter);
        Settings = settings;
        _store = store;
        _reporter = reporter;
        _answer = store.Read();
    }

    /// <summary>Raised after the answer changes, from the question or the switch.</summary>
    public event EventHandler? Changed;

    /// <summary>What reports are about: the DSN, the release and the environment.</summary>
    public CrashSettings Settings { get; }

    /// <summary>Whether this build can send reports at all: it carries a DSN.</summary>
    public bool Available => Settings.Dsn is not null;

    /// <summary>The user's answer, or null until they have given one.</summary>
    public CrashConsent? Answer => _answer;

    /// <summary>Whether the answer is to send reports.</summary>
    public bool Sending => _answer == CrashConsent.Granted;

    /// <summary>Whether the window should ask: a build that can send, and no answer yet.</summary>
    public bool ShouldAsk => Available && _answer is null;

    /// <summary>
    /// The settings for a build that carries <paramref name="dsn"/> (blank is
    /// none) and was built from commit <paramref name="build"/> (its first
    /// twelve characters; <c>local</c> when blank): the release is
    /// <c>com.distronode.DistrictAI@version+build</c>, where
    /// <paramref name="version"/> is the package's, as the account page and the
    /// service see it.
    /// </summary>
    public static CrashSettings SettingsFor(string? dsn, string version, string? build) =>
        new(Present(dsn), $"{AppId}@{version}+{Present(build) ?? "local"}", ReportEnvironment);

    /// <summary>Hands the answer kept (Denied while there is none) to the reporter. Called once, as the app starts.</summary>
    public void Start() => _reporter.Apply(Settings, _answer ?? CrashConsent.Denied);

    /// <summary>The question was answered: "Send reports" when <paramref name="send"/>, else "Don't send" or closed.</summary>
    public void Answered(bool send) => Set(send ? CrashConsent.Granted : CrashConsent.Denied);

    /// <summary>Keeps <paramref name="consent"/> as the answer and applies it at once.</summary>
    public void Set(CrashConsent consent)
    {
        _answer = consent;
        // Not kept is still applied, and holds until the app quits.
        _ = _store.Write(consent);
        _reporter.Apply(Settings, consent);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static string? Present(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
