using System.Diagnostics;
using FlaUI.Core.Definitions;

namespace DistrictAI.UiTests.Live;

/// <summary>The viewer, the login with no workspace, and two copies installed at once.</summary>
internal sealed partial class LiveWalk
{
    /// <summary>The settings sections a viewer may read (the core's role.rs); the others are not offered.</summary>
    private static readonly string[] _viewerSections = ["Call handling", "Knowledge", "Messaging accounts"];

    /// <summary>How a control that changes something starts its name; a viewer must be offered none enabled.</summary>
    private static readonly string[] _writePrefixes =
    [
        "Save", "Add", "Delete", "Remove", "Rename", "Edit", "Make", "Replace", "Choose a plan", "Manage billing",
        "Turn on", "New ticket", "Raise", "Check these keys", "Read a text file", "Pause campaign", "Resume campaign",
    ];

    /// <summary>The GitHub flavour's package name (Package.GitHub.appxmanifest; district-ffi copies.rs).</summary>
    private const string GitHubPackageName = "Distronode.DistrictAI.GitHub";

    /// <summary>
    /// The viewer: the workspace is offered read-only, and no page the walk
    /// visits offers a control that changes something.
    /// </summary>
    private Outcome StudioViewer(Check check)
    {
        SignIn("viewer");
        _ = UiTests.Walk.Heading(App, Config.WorkspaceName, SignedInTimeout);
        _ = Ui.Text(App, "Read-only access");
        check.Shot();
        var found = new List<string>();
        void Look(string page)
        {
            var writes = Ui.Buttons(App).Where(name => _writePrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal))).Distinct().ToArray();
            if (writes.Length > 0)
            {
                found.Add($"{page}: {string.Join(", ", writes)}");
            }
        }

        Look("Overview");
        _ = Ui.Go(App, "Workspace settings", "Workspace settings");
        Check.Expect(
            Ui.Stays(() => Ui.TryRow(App, "Persona. ") is not null, TimeSpan.FromSeconds(2)),
            "the viewer is offered Persona");
        foreach (var section in _viewerSections)
        {
            OpenSection(section);
            Look(section);
        }
        foreach (var entry in new[] { "Contacts", "Billing", "Workflows" })
        {
            if (Ui.Offers(App, entry))
            {
                _ = Ui.Go(App, entry, entry);
                Look(entry);
            }
        }
        return found.Count == 0
            ? Outcome.Pass($"signed in as the viewer: \"Read-only access\", and no write control on Overview, {string.Join(", ", _viewerSections)}, Contacts, Billing or Workflows")
            : Outcome.Fail($"write controls offered to a viewer: {string.Join("; ", found)}");
    }

    /// <summary>The login with no workspace: "No workspace found", with Choose a plan and no Manage billing.</summary>
    private Outcome NowsOverview(Check check)
    {
        SignIn("nows");
        _ = UiTests.Walk.Heading(App, "No workspace found", SignedInTimeout);
        _ = Ui.Text(App, "Choose a plan to set one up.");
        _ = Ui.Find(App, ControlType.Button, "Choose a plan");
        Check.Expect(!Shows(App, ControlType.Button, "Manage billing"), "the no-workspace overview offers \"Manage billing\"");
        check.Shot();
        return Outcome.Pass("signed in with no workspace: \"No workspace found\", \"Choose a plan\", and no \"Manage billing\"");
    }

    /// <summary>The login with no workspace, Purchases Off: the contact-support words and no plans; then set back.</summary>
    private Outcome NowsOff(Check check)
    {
        _ = Ui.Go(App, "Account", "Account");
        Check.Expect(Ui.IsOn(App, PurchasesToggle), "Purchases on this computer was already Off");
        Ui.SetToggle(App, PurchasesToggle, false);
        try
        {
            UiTests.Walk.Open(App, Ui.Entry(App, "Overview"));
            _ = Ui.Text(App, "Please contact support.");
            Check.Expect(Ui.Stays(() => Shows(App, ControlType.Button, "Choose a plan"), TimeSpan.FromSeconds(3)), "\"Choose a plan\" shows with Purchases Off");
            check.Shot();
        }
        finally
        {
            _ = Ui.Go(App, "Account", "Account");
            Ui.SetToggle(App, PurchasesToggle, true);
        }
        return Outcome.Pass("with Purchases Off the no-workspace overview says to contact support and offers no plans; set back to \"Sign in every time\"");
    }

    /// <summary>
    /// Both copies installed: the Store copy's window names it and its
    /// sign-in page holds browser sign-in with "Install one, not both"; the
    /// GitHub copy's window names it too.
    /// </summary>
    public void TwoCopies() =>
        Run("two-copies", TimeSpan.FromMinutes(4), check =>
        {
            var github = GitHubFamily() ?? throw new PreconditionException("the GitHub copy is not installed");
            Check.Expect(Native.IsPackageInstalled(InstalledApp.Family), "the Store copy is not installed");
            if (!OnSignInPage())
            {
                SignOut();
            }
            // Started again, so it reads the copies installed now.
            Relaunch("both copies are installed now");
            _ = Ui.Find(App, null, "Install one, not both", TimeSpan.FromSeconds(60));
            var title = UiTests.Walk.NameOf(App.TryMainWindow()!);
            Check.Expect(title == "District AI (Microsoft Store copy)", $"the Store copy's window is titled \"{title}\"");
            Check.Expect(Ui.Stays(() => OnSignInPage(), TimeSpan.FromSeconds(3)), "\"Sign in with your browser\" is offered while both are installed");
            _ = Ui.Text(App, "This is the Microsoft Store copy of District AI.");
            check.Shot();

            var before = InstalledApp.RunningProcessIds();
            using (Process.Start(new ProcessStartInfo($@"shell:AppsFolder\{github}!App") { UseShellExecute = true }))
            {
            }
            var pid = (int)Wait.For(
                () => InstalledApp.RunningProcessIds().Except(before).Select(id => (object)id).FirstOrDefault(),
                TimeSpan.FromSeconds(90),
                "the GitHub copy's process",
                () => $"District AI processes: [{string.Join(", ", InstalledApp.RunningProcessIds())}]");
            using var other = InstalledApp.Attach(pid);
            try
            {
                var window = other.MainWindow(TimeSpan.FromSeconds(90));
                _ = other.Find(null, "Install one, not both", TimeSpan.FromSeconds(60));
                var otherTitle = UiTests.Walk.NameOf(window);
                Check.Expect(otherTitle == "District AI (GitHub copy)", $"the GitHub copy's window is titled \"{otherTitle}\"");
                check.ShotOf(window);
            }
            finally
            {
                try
                {
                    using var process = Process.GetProcessById(pid);
                    process.Kill();
                }
                catch (Exception error) when (error is ArgumentException or InvalidOperationException)
                {
                    // Already gone.
                }
            }
            return Outcome.Pass("both installed: each window's title names its copy, and the sign-in page holds browser sign-in under \"Install one, not both\"");
        });

    /// <summary>The GitHub copy's package family, when it is installed: DISTRICTAI_PACKAGE_FAMILY_GITHUB, or what Get-AppxPackage says.</summary>
    private static string? GitHubFamily()
    {
        if (Environment.GetEnvironmentVariable("DISTRICTAI_PACKAGE_FAMILY_GITHUB") is { Length: > 0 } given)
        {
            return Native.IsPackageInstalled(given) ? given : null;
        }
        var start = new ProcessStartInfo("powershell.exe")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-Command", $"(Get-AppxPackage -Name '{GitHubPackageName}').PackageFamilyName" })
        {
            start.ArgumentList.Add(argument);
        }
        using var process = Process.Start(start);
        if (process is null)
        {
            return null;
        }
        var family = process.StandardOutput.ReadToEnd().Trim();
        _ = process.WaitForExit(30_000);
        return family.Length > 0 ? family.Split('\n')[0].Trim() : null;
    }
}
