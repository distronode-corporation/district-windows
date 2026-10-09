using FlaUI.Core.Definitions;

namespace DistrictAI.UiTests.Live;

/// <summary>The walk's order: one list, each check after the one before, the sign-outs always.</summary>
internal sealed partial class LiveWalk
{
    /// <summary>What a check gets unless it says otherwise.</summary>
    public static readonly TimeSpan DefaultCap = TimeSpan.FromSeconds(120);

    /// <summary>The checks that need the owner signed in, in the order they run.</summary>
    private static readonly string[] _ownerChecks =
    [
        "areas", "analytics", "numbers", "contacts-writes", "contacts-blocked", "studio-save", "studio-discard", "desk",
        "workflows", "hq", "support-report", "billing-view", "billing-confirm", "billing-manage", "scheduling", "rooms", "themes",
    ];

    /// <summary>The whole walk: the owner, then the viewer, then the login with no workspace.</summary>
    public void RunAll()
    {
        // A session left from an earlier run is not this run's sign-in.
        _ = Guard("signing out what was signed in before the walk", ToSignInPage);

        var owner = Run("signin", DefaultCap + CallbackTimeout, OwnerSignIn);
        if (owner.Result == "PASS")
        {
            Run("areas", TimeSpan.FromMinutes(5), Areas);
            Run("analytics", DefaultCap, Analytics);
            Run("numbers", DefaultCap, Numbers);
            Run("contacts-writes", TimeSpan.FromMinutes(3), ContactsWrites);
            Run("contacts-blocked", DefaultCap, ContactsBlocked);
            ContactsDelete();
            Run("studio-save", TimeSpan.FromMinutes(5), StudioSave);
            Run("studio-discard", DefaultCap, StudioDiscard);
            Run("desk", TimeSpan.FromMinutes(4), Desk);
            Run("workflows", DefaultCap, Workflows);
            Run("hq", TimeSpan.FromMinutes(3), Hq);
            Run("support-report", DefaultCap, SupportReport);
            Run("billing-view", TimeSpan.FromMinutes(3), BillingView);
            Run("billing-confirm", DefaultCap, BillingConfirm);
            Run("billing-manage", DefaultCap, BillingManage, probe: true);
            Run("scheduling", DefaultCap, Scheduling);
            Run("rooms", TimeSpan.FromSeconds(60), Rooms, probe: true);
            Run("themes", TimeSpan.FromMinutes(4), Themes, probe: true);
        }
        else
        {
            Skip(_ownerChecks, "the owner did not sign in (see signin)");
        }
        SignOutAfter("signin");

        if (Guard("the sign-in page before the viewer", ToSignInPage))
        {
            var viewer = Run("studio-viewer", TimeSpan.FromMinutes(4), StudioViewer);
            SignOutAfter(viewer.Result == "NOT_AUTOMATED" ? null : "studio-viewer");
        }
        else
        {
            Skip(["studio-viewer"], "the app could not be brought back to the sign-in page");
        }

        if (Guard("the sign-in page before the no-workspace login", ToSignInPage))
        {
            var overview = Run("nows-overview", DefaultCap + CallbackTimeout, NowsOverview);
            if (overview.Result != "NOT_AUTOMATED" && !OnSignInPage())
            {
                Run("nows-off", DefaultCap, NowsOff);
            }
            else
            {
                Skip(["nows-off"], "the no-workspace login did not sign in (see nows-overview)");
            }
            SignOutAfter("nows-overview");
        }
        else
        {
            Skip(["nows-overview", "nows-off"], "the app could not be brought back to the sign-in page");
        }
    }

    /// <summary>Records each of <paramref name="ids"/> not yet recorded as NOT_AUTOMATED, with <paramref name="why"/>.</summary>
    private void Skip(IEnumerable<string> ids, string why)
    {
        foreach (var id in ids)
        {
            if (Results.Checks.All(check => check.Id != id))
            {
                _ = Results.Record(id, CheckResult.NotAutomated, why);
            }
        }
    }

    /// <summary>
    /// Turns a PASS of <paramref name="id"/> into a FAIL with <paramref name="why"/>
    /// added: a later part of the same check (its clean-up) went wrong.
    /// </summary>
    private void Amend(string id, string why)
    {
        var check = Results.For(id);
        if (check.Result is "PASS" or "PROBE")
        {
            check.Result = "FAIL";
        }
        check.Detail = LiveResults.Clean($"{check.Detail}; then {why}");
        Results.Write();
    }

    /// <summary>
    /// Runs a step that is no check of its own (signing out, getting back
    /// to the sign-in page), capped at a minute; false when it failed.
    /// </summary>
    private bool Guard(string what, Action step)
    {
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try
        {
            if (!App.IsRunning)
            {
                Relaunch($"it was not running before {what}");
            }
            var task = Task.Run(() =>
            {
                Wait.Token = cancel.Token;
                step();
            });
            if (task.Wait(TimeSpan.FromSeconds(65)))
            {
                return true;
            }
            cancel.Cancel();
            InstalledApp.Log($"{what}: ran past a minute");
        }
        catch (AggregateException error)
        {
            InstalledApp.Log($"{what} failed: {error.InnerException?.Message}");
        }
        if (!App.IsRunning)
        {
            Relaunch($"it ended during {what}");
        }
        return false;
    }

    /// <summary>Signs out (always, past the deadline too); a failure turns <paramref name="id"/>'s PASS into a FAIL.</summary>
    private void SignOutAfter(string? id)
    {
        if (!Guard("signing out", ToSignInPage) && id is not null)
        {
            Amend(id, "signing out did not reach the sign-in page");
        }
    }

    /// <summary>
    /// Gets the app to its sign-in page: signed out when signed in; a sign-in
    /// left waiting cancelled; started again when it shows neither.
    /// </summary>
    private void ToSignInPage()
    {
        if (OnSignInPage())
        {
            return;
        }
        if (SignedIn())
        {
            SignOut();
            return;
        }
        if (App.TryFind(ControlType.Button, "Cancel") is { } cancel)
        {
            UiTests.Walk.Activate(cancel);
            if (Wait.Until(OnSignInPage, TimeSpan.FromSeconds(15)))
            {
                return;
            }
        }
        Relaunch("it showed neither the sign-in page nor a signed-in page");
        _ = Wait.Until(() => OnSignInPage() || SignedIn(), TimeSpan.FromSeconds(30));
        if (!OnSignInPage())
        {
            SignOut();
        }
    }

    /// <summary>Whether the app is signed in now: its navigation pane shows.</summary>
    private bool SignedIn() => App.TryMainWindow() is { } window && UiTests.Walk.NavEntries(window).Length > 0;

    private Outcome OwnerSignIn(Check check)
    {
        if (Config.WorkspaceName is not { Length: > 0 })
        {
            throw new PreconditionException("live-config.json names no workspaceName");
        }
        SignIn("owner");
        _ = UiTests.Walk.Heading(App, Config.WorkspaceName, SignedInTimeout);
        Ui.Loaded(App);
        check.Shot();
        return Outcome.Pass("signed in through the broker (the live code exchange); the overview shows the QA workspace's name");
    }

    /// <summary>Whether a control of <paramref name="type"/> named <paramref name="name"/> is on screen now.</summary>
    private static bool Shows(InstalledApp app, ControlType type, string name) => app.TryFind(type, name) is not null;
}
