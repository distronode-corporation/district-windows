#if DISTRICT_SPIKES
using System.Diagnostics;
using Windows.Storage;

namespace DistrictAI.Spikes;

/// <summary>
/// The spike build's log: one line per event, in LocalState\spikes.log, which
/// the CI harness reads. Only in a build made with -p:DistrictSpikes=true;
/// the shipped app has none of this.
/// </summary>
internal static class SpikeLog
{
    private static readonly Lock Gate = new();

    public static string Path => System.IO.Path.Combine(ApplicationData.Current.LocalFolder.Path, "spikes.log");

    public static void Write(string line)
    {
        var text = $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()} pid={Environment.ProcessId} {line}{Environment.NewLine}";
        lock (Gate)
        {
            for (var attempt = 0; attempt < 20; attempt++)
            {
                try
                {
                    using var file = new FileStream(Path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                    using var writer = new StreamWriter(file);
                    writer.Write(text);
                    return;
                }
                catch (IOException)
                {
                    Thread.Sleep(10);
                }
            }
        }
    }

    /// <summary>How long this process has been running, in milliseconds.</summary>
    public static long Uptime()
    {
        using var self = Process.GetCurrentProcess();
        return (long)(DateTime.Now - self.StartTime).TotalMilliseconds;
    }
}
#endif
