using System.Diagnostics;

namespace DistrictAI.UiTests;

/// <summary>Bounded polling: every wait in these tests has a deadline.</summary>
internal static class Wait
{
    /// <summary>How often a condition is checked again.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// The first non-null answer of <paramref name="probe"/> within
    /// <paramref name="timeout"/>. At the deadline, fails with
    /// <paramref name="what"/> and what <paramref name="describe"/> says was there.
    /// </summary>
    public static T For<T>(Func<T?> probe, TimeSpan timeout, string what, Func<string> describe)
        where T : class
    {
        var clock = Stopwatch.StartNew();
        Exception? last = null;
        while (true)
        {
            try
            {
                if (probe() is { } found)
                {
                    InstalledApp.Log(FormattableString.Invariant($"found {what} after {clock.ElapsedMilliseconds} ms"));
                    return found;
                }
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                // UI Automation throws while a window is being made or torn down.
                last = error;
            }
            if (clock.Elapsed >= timeout)
            {
                var because = last is null ? string.Empty : $" (last error: {last.GetType().Name}: {last.Message})";
                throw new TimeoutException(FormattableString.Invariant(
                    $"No {what} within {timeout.TotalSeconds} s{because}. UI Automation held:{Environment.NewLine}{describe()}"));
            }
            Thread.Sleep(Interval);
        }
    }

    /// <summary>Whether <paramref name="condition"/> held at some point within <paramref name="timeout"/>.</summary>
    public static bool Until(Func<bool> condition, TimeSpan timeout)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            if (clock.Elapsed >= timeout)
            {
                return false;
            }
            Thread.Sleep(Interval);
        }
        return true;
    }
}
