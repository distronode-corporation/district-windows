using System.Diagnostics;
using System.Text.Json;
using FlaUI.Core.Definitions;

namespace DistrictAI.UiTests.Live;

/// <summary>
/// Signing in and out through the harness's broker. The walk presses "Sign
/// in with your browser"; the URL shim records the authorize link and starts
/// no browser; the walk writes the link's PKCE challenge and state to
/// signin-request-&lt;login&gt;.json; the harness has the service mint a code
/// for that login and writes signin-callback-&lt;login&gt;.json
/// <c>{"callbackUrl":"districtai://auth?code=...&amp;state=..."}</c>; the walk
/// opens it as a browser would, and the app exchanges the code with the
/// verifier only it holds. The walk never sees a password, and never logs a
/// link.
/// </summary>
internal sealed partial class LiveWalk
{
    /// <summary>The sign-in page's button (Views/SignInPage.xaml).</summary>
    public const string SignInButton = "Sign in with your browser";

    /// <summary>The sign-in page's heading after a sign-out (the core's session.rs).</summary>
    public const string SignedOutTitle = "You are signed out.";

    /// <summary>The first-run sign-in page's heading (district-ffi screen.rs, WELCOME_TITLE).</summary>
    public const string WelcomeTitle = "Welcome to District AI";

    /// <summary>How long the harness has to answer a sign-in request.</summary>
    public static readonly TimeSpan CallbackTimeout = TimeSpan.FromSeconds(120);

    /// <summary>How long the app has to show its signed-in page after the callback.</summary>
    public static readonly TimeSpan SignedInTimeout = TimeSpan.FromSeconds(60);

    /// <summary>Whether the app shows its sign-in page now.</summary>
    public bool OnSignInPage() => App.TryFind(ControlType.Button, SignInButton) is not null;

    /// <summary>
    /// Signs in as <paramref name="login"/> through the broker, from the
    /// sign-in page, and returns once the app has its session (its pane
    /// shows). What the signed-in page must show is the caller's to check.
    /// </summary>
    public void SignIn(string login)
    {
        if (!Config.Offers(login))
        {
            throw new PreconditionException($"the harness offers no \"{login}\" login (live-config.json logins)");
        }
        if (!OnSignInPage())
        {
            throw new CheckFailedException($"not on the sign-in page before signing in as {login}");
        }
        var request = Path.Combine(Folder, $"signin-request-{login}.json");
        var callback = Path.Combine(Folder, $"signin-callback-{login}.json");
        File.Delete(callback);
        var mark = Urls.Mark();
        Ui.Press(App, SignInButton);
        var authorize = Urls.WaitFor(mark, uri => LaunchedUrls.IsOurs(uri, "/auth/native"), TimeSpan.FromSeconds(30), "the sign-in link (/auth/native)");
        var challenge = LaunchedUrls.Query(authorize, "code_challenge");
        var state = LaunchedUrls.Query(authorize, "state");
        Check.Expect(challenge is not null && Challenge().IsMatch(challenge), "the sign-in link's code_challenge is not 43 to 128 base64url characters");
        Check.Expect(state is { Length: >= 1 and <= 256 } && state.All(c => c is >= ' ' and <= '~'), "the sign-in link's state is not 1 to 256 printable ASCII characters");
        WriteJson(request, new Dictionary<string, string> { ["login"] = login, ["challenge"] = challenge!, ["state"] = state! });
        InstalledApp.Log($"sign-in request for {login} written; waiting for the harness");

        var reply = Wait.For(
            () => File.Exists(callback) ? ReadCallback(callback) : null,
            CallbackTimeout,
            $"signin-callback-{login}.json from the harness",
            () => $"no {Path.GetFileName(callback)} in {Folder}");
        File.Delete(callback);
        File.Delete(request);
        if (reply.Error is { } error)
        {
            // The harness could not mint a code (a refused login, say): this login is done.
            throw new CheckFailedException($"the harness could not sign {login} in: {InstalledApp.Redact(error)}");
        }
        var answer = reply.Answer!;
        Check.Expect(
            answer.Scheme == "districtai" && answer.Host == "auth" && LaunchedUrls.Query(answer, "state") == state,
            "the harness's callback is not a districtai://auth answer to this request");
        using (Process.Start(new ProcessStartInfo(answer.OriginalString) { UseShellExecute = true }))
        {
        }
        InstalledApp.Log($"opened the sign-in answer for {login}");
        _ = Wait.For(
            () => !OnSignInPage() && App.TryMainWindow() is { } window && UiTests.Walk.NavEntries(window).Length > 0 ? (object)true : null,
            SignedInTimeout,
            $"the app signed in as {login}",
            App.Describe);
        SignedInAs = login;
    }

    /// <summary>The login the last sign-in completed for (null before any, and after a sign-out).</summary>
    public string? SignedInAs { get; private set; }

    /// <summary>What the harness answered: the districtai://auth link, or why it could not.</summary>
    private sealed record Callback(Uri? Answer, string? Error);

    /// <summary>A PKCE challenge as the harness accepts it.</summary>
    [System.Text.RegularExpressions.GeneratedRegex("^[A-Za-z0-9_-]{43,128}$")]
    private static partial System.Text.RegularExpressions.Regex Challenge();

    /// <summary>Signs out from Account (it asks nothing first), and waits for the sign-in page.</summary>
    public void SignOut()
    {
        if (OnSignInPage())
        {
            return;
        }
        _ = Ui.Go(App, "Account", "Account");
        Ui.Press(App, "Sign out");
        // Should a later version ask first, its own "Sign out" confirms.
        if (Wait.Until(() => OnSignInPage() || Ui.HasDialog(App, "Sign out"), TimeSpan.FromSeconds(10)) && !OnSignInPage())
        {
            Ui.PressIn(App, Ui.Dialog(App, "Sign out"), "Sign out");
        }
        _ = Wait.For(() => OnSignInPage() ? (object)true : null, TimeSpan.FromSeconds(45), "the sign-in page after Sign out", App.Describe);
        SignedInAs = null;
        InstalledApp.Log("signed out");
    }

    private static Callback? ReadCallback(string path)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.TryGetProperty("error", out var error))
            {
                return new Callback(null, error.GetString() is { Length: > 0 } why ? why : "no reason given");
            }
            return document.RootElement.TryGetProperty("callbackUrl", out var url)
                && url.GetString() is { Length: > 0 } text
                && Uri.TryCreate(text, UriKind.Absolute, out var uri) ? new Callback(uri, null) : null;
        }
        catch (Exception error) when (error is JsonException or IOException)
        {
            // Still being written.
            return null;
        }
    }
}
