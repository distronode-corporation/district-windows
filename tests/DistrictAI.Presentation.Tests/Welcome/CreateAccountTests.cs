using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels;
using Xunit;

namespace DistrictAI.Presentation.Tests.Welcome;

// The welcome screen's "Create an account": the same browser sign-in, whose
// page offers the account, and held like sign-in while another copy of the
// app is installed (G1b).
public sealed class CreateAccountTests
{
    private static readonly CreateAccountView _offer = new(
        "Create an account",
        "Your browser opens the District AI sign-in page, which offers Create an account.");

    private static SessionScreen Welcome(CreateAccountView? offer) =>
        new("Welcome to District AI", "Sign in with your District AI account.", false, SignIn: true, false, false, false, null, offer);

    private static readonly CopiesView _both = new(
        CopyFlavour.GitHub, true, true, "Install one, not both", "Installed twice.", "This is the GitHub copy of District AI.", "District AI (GitHub copy)");

    [Fact]
    public void ItStartsTheBrowserSignIn()
    {
        var signIn = new SignInViewModel();
        var sink = new RecordingSink();
        signIn.Attach(sink);
        signIn.Show(Welcome(_offer));
        Assert.True(signIn.OffersCreateAccount);
        Assert.True(signIn.CanCreateAccount);
        Assert.Equal("Create an account", signIn.CreateAccountLabel);
        Assert.StartsWith("Your browser opens", signIn.CreateAccountNote, StringComparison.Ordinal);

        signIn.CreateAccountCommand.Execute(null);
        Assert.Equal([new UiEvent.SignIn()], sink.Sent);
    }

    [Fact]
    public void WithBothCopiesInstalledItIsHeldButStillShown()
    {
        var signIn = new SignInViewModel();
        var sink = new RecordingSink();
        signIn.Attach(sink);
        signIn.ShowCopies(_both);
        signIn.Show(Welcome(_offer));
        Assert.True(signIn.OffersCreateAccount);
        Assert.False(signIn.CanCreateAccount);
        Assert.False(signIn.CanSignIn);
        signIn.CreateAccountCommand.Execute(null);
        Assert.Empty(sink.Sent);
    }

    [Fact]
    public void WhereSignInIsNotOfferedNeitherIsAnAccount()
    {
        var signIn = new SignInViewModel();
        signIn.Show(Welcome(_offer));
        signIn.Show(new SessionScreen("Signing in", "Waiting for your browser.", true, false, false, false, true, null, null));
        Assert.False(signIn.OffersCreateAccount);
        Assert.False(signIn.CanCreateAccount);
        Assert.Equal(string.Empty, signIn.CreateAccountLabel);
        Assert.Equal(string.Empty, signIn.CreateAccountNote);
    }
}
