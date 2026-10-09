using System.Diagnostics;
using System.Net;
using FlaUI.Core.Definitions;
using Microsoft.Win32;

namespace DistrictAI.UiTests.Live;

/// <summary>Booking pages' hand-off, a room, and the themes.</summary>
internal sealed partial class LiveWalk
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    /// <summary>
    /// Manage on the web: the app opens the hand-off's start link; the walk
    /// answers it as a browser would (a GET, redirects off) and opens the
    /// districtai://handoff answer; the app then opens the signed-in link,
    /// which is checked by host and path only, and never opened or logged.
    /// </summary>
    private Outcome Scheduling(Check check)
    {
        _ = Ui.Go(App, "Booking pages", "Booking pages");
        var mark = Urls.Mark();
        Ui.Press(App, "Manage on the web");
        var clock = Stopwatch.StartNew();
        var start = Urls.WaitFor(mark, uri => LaunchedUrls.IsOurs(uri, "/dashboard/handoff/start"), TimeSpan.FromSeconds(10), "the hand-off's start link");
        var startMark = Urls.Mark();

        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { Timeout = TimeSpan.FromSeconds(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) DistrictAI-win-smoke");
        using var response = http.GetAsync(start).GetAwaiter().GetResult();
        var status = (int)response.StatusCode;
        Check.Expect(
            response.StatusCode is HttpStatusCode.Found or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.Moved,
            $"the start link answered {status}, not a redirect");
        var location = response.Headers.Location;
        Check.Expect(
            location is { IsAbsoluteUri: true } && location.Scheme == "districtai" && location.Host == "handoff",
            "the start link's redirect is not a districtai://handoff link");
        using (Process.Start(new ProcessStartInfo(location!.OriginalString) { UseShellExecute = true }))
        {
        }
        var answeredIn = clock.Elapsed;
        InstalledApp.Log($"answered the hand-off in {answeredIn.TotalSeconds:F1} s");
        // The core waits 10 s for the answer before it asks for an unbound link.
        Check.Expect(answeredIn < TimeSpan.FromSeconds(9), $"the hand-off was answered after {answeredIn.TotalSeconds:F0} s, past the core's wait");

        _ = Urls.WaitFor(
            startMark,
            uri => LaunchedUrls.IsOurs(uri, "/dashboard/handoff") && !uri.AbsolutePath.StartsWith("/dashboard/handoff/start", StringComparison.Ordinal),
            TimeSpan.FromSeconds(30),
            "the signed-in booking pages link (/dashboard/handoff)");
        Check.Expect(App.TryFind(null, "Booking pages problem") is null, "\"Booking pages problem\" shows");
        check.Shot();
        return Outcome.Pass("Manage on the web opened the start link; its redirect came back as districtai://handoff; the app then opened www.distronode.com/dashboard/handoff (not opened, not logged)");
    }

    /// <summary>
    /// A probe: join the run's room, copy its guest link (read from the
    /// clipboard, its host checked, never logged or shown), leave; the
    /// meeting is in the list. Joining needs audio devices, which a server
    /// may hang on.
    /// </summary>
    private Outcome Rooms(Check check)
    {
        if (Config.RoomName is not { Length: > 0 })
        {
            throw new PreconditionException("live-config.json names no roomName");
        }
        _ = Ui.Go(App, "Meeting rooms", "Meeting rooms");
        Ui.Type(App, "Room name", Config.RoomName);
        Ui.Press(App, "Join");
        _ = Ui.Text(App, "In the room.", TimeSpan.FromSeconds(30));
        _ = Native.TakeClipboardText();
        Ui.Press(App, "Copy guest link");
        _ = Ui.Text(App, "Guest link copied.");
        var copied = Native.TakeClipboardText();
        var host = Uri.TryCreate(copied?.Trim(), UriKind.Absolute, out var link) ? link.Host : null;
        Ui.Press(App, "Leave the room");
        Check.Expect(Wait.Until(() => !Shows(App, ControlType.Button, "Leave the room"), Ui.Step), "still in the room after Leave the room");
        Check.Expect(string.Equals(host, "www.distronode.com", StringComparison.OrdinalIgnoreCase), $"the guest link is not on www.distronode.com (host {host ?? "none"})");
        _ = Ui.Row(App, Config.RoomName, Ui.Step);
        check.Shot();
        return Outcome.Pass("joined the room, copied a guest link on www.distronode.com (not logged), left; the meeting is listed");
    }

    /// <summary>
    /// A probe: Overview, Analytics and Billing saved in the light theme, the
    /// dark one and a high-contrast one, for a reviewer; the theme put back.
    /// </summary>
    private Outcome Themes(Check check)
    {
        using var key = Registry.CurrentUser.CreateSubKey(PersonalizeKey, writable: true);
        var original = key.GetValue("AppsUseLightTheme") as int? ?? 1;
        var seen = new List<string>();
        try
        {
            foreach (var (name, light) in new[] { ("light", 1), ("dark", 0) })
            {
                key.SetValue("AppsUseLightTheme", light, RegistryValueKind.DWord);
                Native.BroadcastSettingChange("ImmersiveColorSet");
                Thread.Sleep(TimeSpan.FromSeconds(3));
                ShootThemePages(check);
                seen.Add(name);
            }
            try
            {
                ApplyTheme(@"C:\Windows\Resources\Ease of Access Themes\hc1.theme");
                ShootThemePages(check);
                seen.Add("high contrast (hc1)");
            }
            catch (PreconditionException missing)
            {
                seen.Add($"not high contrast ({missing.Message})");
            }
        }
        finally
        {
            ApplyTheme(@"C:\Windows\Resources\Themes\aero.theme");
            key.SetValue("AppsUseLightTheme", original, RegistryValueKind.DWord);
            Native.BroadcastSettingChange("ImmersiveColorSet");
        }
        return Outcome.Probe($"Overview, Analytics and Billing saved in {string.Join(", ", seen)}; for review");
    }

    private void ShootThemePages(Check check)
    {
        foreach (var (entry, heading) in new[] { ("Overview", Config.WorkspaceName), ("Analytics", "Analytics"), ("Billing", "Billing") })
        {
            _ = Ui.Go(App, entry, heading);
            check.Shot();
        }
    }

    /// <summary>Applies a .theme file as a double-click does, then closes the Settings window it opens.</summary>
    private static void ApplyTheme(string path)
    {
        if (!File.Exists(path))
        {
            throw new PreconditionException($"no {Path.GetFileName(path)} on this computer");
        }
        using (Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }))
        {
        }
        Thread.Sleep(TimeSpan.FromSeconds(8));
        foreach (var settings in Process.GetProcessesByName("SystemSettings"))
        {
            using (settings)
            {
                try
                {
                    settings.Kill();
                }
                catch (InvalidOperationException)
                {
                    // Already gone.
                }
            }
        }
        InstalledApp.Log($"applied {Path.GetFileName(path)}");
    }
}
