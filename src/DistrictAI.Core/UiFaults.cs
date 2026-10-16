using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace DistrictAI.Core;

/// <summary>
/// An exception that escapes work on the UI thread ends the app: WinUI fails
/// fast from the dispatcher with only an error code (a stowed E_FAIL), and the
/// exception itself is gone from the crash report. Before it goes, its whole
/// account is written to the app's trace (<see cref="Trace"/>): its type,
/// HResult, message and stack, and every inner exception the same way. It is
/// never swallowed: <see cref="Run"/> throws it on, as it was.
/// </summary>
public static class UiFaults
{
    /// <summary>
    /// Runs <paramref name="work"/>; an exception it throws is reported as
    /// escaping <paramref name="where"/>, then thrown on unchanged.
    /// </summary>
    public static void Run(string where, Action work)
    {
        ArgumentNullException.ThrowIfNull(work);
        try
        {
            work();
        }
        catch (Exception exception)
        {
            Report(where, exception);
            throw;
        }
    }

    /// <summary>Writes <paramref name="exception"/>'s whole account to the trace, as escaping <paramref name="where"/>.</summary>
    public static void Report(string where, Exception exception) => Trace.TraceError(Describe(where, exception));

    /// <summary>
    /// <paramref name="exception"/>'s account: a line naming <paramref name="where"/>,
    /// then the exception and each inner one (all of an aggregate's), indented
    /// by depth, each with its type, HResult, message and stack.
    /// </summary>
    public static string Describe(string where, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"District AI: an exception escaped {where}");
        Append(text, exception, 0);
        return text.ToString();
    }

    private static void Append(StringBuilder text, Exception exception, int depth)
    {
        var indent = new string(' ', depth * 2);
        text.AppendLine();
        text.Append(CultureInfo.InvariantCulture, $"{indent}{(depth == 0 ? string.Empty : "inner: ")}{exception.GetType().FullName}, HResult 0x{exception.HResult:X8}: {exception.Message}");
        if (exception.StackTrace is { } stack)
        {
            foreach (var line in stack.Split('\n'))
            {
                text.AppendLine();
                text.Append(indent).Append("  ").Append(line.TrimEnd('\r'));
            }
        }
        IEnumerable<Exception> inner = exception is AggregateException aggregate
            ? aggregate.InnerExceptions
            : exception.InnerException is { } one ? [one] : [];
        foreach (var each in inner)
        {
            Append(text, each, depth + 1);
        }
    }
}

/// <summary>
/// The UI thread's <see cref="SynchronizationContext"/> with every callback
/// posted to it run through <see cref="UiFaults.Run"/>: an <c>async void</c>
/// method's continuation, and the exception such a method throws (which .NET
/// posts here to be thrown), are reported before they end the app. What runs,
/// where and in what order is <paramref name="inner"/>'s.
/// </summary>
public sealed class ReportingSynchronizationContext(SynchronizationContext inner) : SynchronizationContext
{
    private const string Where = "a continuation on the UI thread";

    private readonly SynchronizationContext _inner = inner ?? throw new ArgumentNullException(nameof(inner));

    /// <inheritdoc/>
    public override void Post(SendOrPostCallback d, object? state) =>
        _inner.Post(posted => UiFaults.Run(Where, () => d(posted)), state);

    /// <inheritdoc/>
    public override void Send(SendOrPostCallback d, object? state) =>
        _inner.Send(sent => UiFaults.Run(Where, () => d(sent)), state);

    /// <inheritdoc/>
    public override SynchronizationContext CreateCopy() => new ReportingSynchronizationContext(_inner.CreateCopy());
}
