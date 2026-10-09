using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels;
using Xunit;

namespace DistrictAI.Presentation.Tests.Copies;

// With another copy of the app installed, the sign-in page holds browser
// sign-in, says why, and says which copy this is (the core's CopiesView).
public sealed class SignInHeldTests
{
    private static readonly SessionScreen _signedOut =
        new("Sign in", "Use your browser.", false, SignIn: true, false, false, false, null, null);

    private static readonly CopiesView _both = new(
        CopyFlavour.Store,
        BothInstalled: true,
        SignInHeld: true,
        Title: "Install one, not both",
        Message: "District AI is installed twice on this computer.",
        ThisCopyLine: "This is the Microsoft Store copy of District AI.",
        WindowTitle: "District AI (Microsoft Store copy)");

    private static readonly CopiesView _alone =
        new(CopyFlavour.Store, false, false, null, null, null, "District AI");

    [Fact]
    public void WithBothInstalledSignInIsHeldAndSaysWhy()
    {
        var signIn = new SignInViewModel();
        var sink = new RecordingSink();
        signIn.Attach(sink);
        signIn.Show(_signedOut);
        Assert.True(signIn.CanSignIn);

        signIn.ShowCopies(_both);
        Assert.False(signIn.CanSignIn);
        Assert.True(signIn.SignInHeld);
        Assert.Equal("Install one, not both", signIn.HeldTitle);
        Assert.Equal("District AI is installed twice on this computer.", signIn.HeldMessage);
        Assert.True(signIn.HasCopyLine);
        Assert.Equal("This is the Microsoft Store copy of District AI.", signIn.CopyLine);

        // A later screen offering sign-in stays held, and a stale press sends nothing.
        signIn.Show(_signedOut);
        Assert.False(signIn.CanSignIn);
        signIn.SignInCommand.Execute(null);
        Assert.Empty(sink.Sent);
    }

    [Fact]
    public void AloneNothingIsHeld()
    {
        var signIn = new SignInViewModel();
        var sink = new RecordingSink();
        signIn.Attach(sink);
        signIn.ShowCopies(_alone);
        signIn.Show(_signedOut);
        Assert.True(signIn.CanSignIn);
        Assert.False(signIn.SignInHeld);
        Assert.False(signIn.HasCopyLine);
        Assert.Equal(string.Empty, signIn.HeldMessage);
        signIn.SignInCommand.Execute(null);
        Assert.Equal([new UiEvent.SignIn()], sink.Sent);
        Assert.Throws<ArgumentNullException>(() => signIn.ShowCopies(null!));
    }
}
