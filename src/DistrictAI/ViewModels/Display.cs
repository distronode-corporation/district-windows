using System.Collections.ObjectModel;
using System.Globalization;
using DistrictAI.Core.Ffi;

namespace DistrictAI.ViewModels;

/// <summary>The few things the pages format themselves; every other string arrives from the core ready to show.</summary>
internal static class Display
{
    /// <summary>The words on a Report button for <paramref name="availability"/>, or empty when there is none.</summary>
    internal static string ReportLabel(ReportAvailability availability) => availability switch
    {
        ReportAvailability.InApp => "Report",
        ReportAvailability.OnWeb => "Report on the web",
        ReportAvailability.Hidden => string.Empty,
        // An availability a later core adds: no button until this build knows it.
        _ => string.Empty,
    };

    /// <summary>
    /// An ISO 8601 UTC time from the core, in this computer's time zone and the
    /// user's culture: the time alone when it is today, the date otherwise.
    /// Empty for no time, or one this build cannot read.
    /// </summary>
    internal static string When(string? iso) => When(iso, DateTimeOffset.Now);

    /// <summary><see cref="When(string?)"/> as of <paramref name="now"/>.</summary>
    internal static string When(string? iso, DateTimeOffset now)
    {
        if (string.IsNullOrEmpty(iso)
            || !DateTimeOffset.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at))
        {
            return string.Empty;
        }
        var local = at.ToLocalTime();
        var culture = CultureInfo.CurrentCulture;
        return local.Date == now.ToLocalTime().Date
            ? local.ToString("t", culture)
            : local.ToString("d", culture);
    }

    /// <summary>A failure as one block of text: the message, and the regions line under it when there is one.</summary>
    internal static string Failure(FailureView? failure)
    {
        if (failure is null)
        {
            return string.Empty;
        }
        return string.IsNullOrEmpty(failure.RegionsLine)
            ? failure.Message
            : failure.Message + Environment.NewLine + failure.RegionsLine;
    }

    /// <summary>
    /// Makes <paramref name="target"/> equal <paramref name="source"/>, touching
    /// only the rows that changed, so a list keeps its scroll position and
    /// keyboard focus across the core's snapshots.
    /// </summary>
    internal static void Sync<T>(ObservableCollection<T> target, IReadOnlyList<T> source)
    {
        for (var i = 0; i < source.Count; i++)
        {
            if (i >= target.Count)
            {
                target.Add(source[i]);
            }
            else if (!EqualityComparer<T>.Default.Equals(target[i], source[i]))
            {
                target[i] = source[i];
            }
        }
        while (target.Count > source.Count)
        {
            target.RemoveAt(target.Count - 1);
        }
    }
}
