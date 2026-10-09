using System.Diagnostics;
using System.Globalization;
using System.Xml.Linq;

namespace DistrictAI.UiTests.Live;

/// <summary>
/// Windows Error Reporting's record of the app ending badly, across the whole
/// walk: Application log events 1000 (Application Error) and 1001 (Windows
/// Error Reporting) that name DistrictAI, and the stowed-exception code
/// 0xc000027b a XAML fail-fast leaves. Read with wevtutil, so the tests take
/// no dependency for it.
/// </summary>
internal sealed class CrashWatch
{
    private readonly DateTime _since = DateTime.UtcNow;
    private readonly HashSet<string> _seen = [];

    /// <summary>The crash records that appeared since the last call (or the walk's start), one line each.</summary>
    public string[] New()
    {
        var found = new List<string>();
        foreach (var record in Read())
        {
            if (_seen.Add(record))
            {
                found.Add(record);
            }
        }
        return [.. found];
    }

    private string[] Read()
    {
        var since = _since.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
        var query = $"*[System[(EventID=1000 or EventID=1001) and TimeCreated[@SystemTime>='{since}']]]";
        var start = new ProcessStartInfo("wevtutil.exe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in new[] { "qe", "Application", $"/q:{query}", "/f:xml", "/c:50" })
        {
            start.ArgumentList.Add(argument);
        }
        try
        {
            using var process = Process.Start(start);
            if (process is null)
            {
                return [];
            }
            var output = process.StandardOutput.ReadToEnd();
            _ = process.WaitForExit(15_000);
            var document = XDocument.Parse($"<Events>{output}</Events>");
            return [.. document.Root!.Elements()
                .Select(Describe)
                .Where(line => line.Contains("DistrictAI", StringComparison.OrdinalIgnoreCase)
                    || line.Contains("c000027b", StringComparison.OrdinalIgnoreCase))];
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or System.Xml.XmlException or InvalidOperationException)
        {
            InstalledApp.Log($"reading the crash record failed: {error.Message}");
            return [];
        }
    }

    private static string Describe(XElement record)
    {
        XNamespace ns = "http://schemas.microsoft.com/win/2004/08/events/event";
        var system = record.Element(ns + "System");
        var id = system?.Element(ns + "EventID")?.Value ?? "?";
        var time = system?.Element(ns + "TimeCreated")?.Attribute("SystemTime")?.Value ?? "?";
        var data = string.Join(" ", record.Descendants(ns + "Data").Select(datum => datum.Value).Where(value => value.Length > 0));
        return $"event {id} at {time}: {data}";
    }
}
