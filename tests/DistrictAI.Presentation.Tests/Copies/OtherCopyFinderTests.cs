using DistrictAI.ViewModels.Copies;
using Xunit;

namespace DistrictAI.Presentation.Tests.Copies;

public sealed class OtherCopyFinderTests
{
    private const string Store = "DistronodeCorporation.42101E4C5A5B6_m7gk2x0xkz9fa";
    private const string GitHub = "Distronode.DistrictAI.GitHub_4a1yh7x8ec0qe";

    /// <summary>Windows' answer, as a test gives it.</summary>
    private sealed class FakeHandlers(Func<string, IReadOnlyList<string>> answer) : ISchemeHandlers
    {
        public List<string> Asked { get; } = [];

        public Task<IReadOnlyList<string>> FamilyNamesForAsync(string scheme, CancellationToken cancellationToken)
        {
            Asked.Add(scheme);
            return Task.FromResult(answer(scheme));
        }
    }

    [Fact]
    public async Task TheOtherCopyIsFoundOnceAndThisOneLeftOut()
    {
        var handlers = new FakeHandlers(_ => [Store, GitHub, GitHub.ToUpperInvariant(), string.Empty, " "]);
        var others = await OtherCopyFinder.OthersAsync(handlers, Store, TestContext.Current.CancellationToken);
        Assert.Equal([GitHub], others);
        Assert.Equal(["districtai"], handlers.Asked);
    }

    [Fact]
    public async Task AloneNothingIsFound()
    {
        var others = await OtherCopyFinder.OthersAsync(new FakeHandlers(_ => [Store.ToLowerInvariant()]), Store, TestContext.Current.CancellationToken);
        Assert.Empty(others);
    }

    [Fact]
    public async Task AQuestionWindowsCouldNotAnswerHoldsNothing()
    {
        var failing = new FakeHandlers(_ => throw new InvalidOperationException("no answer"));
        Assert.Empty(await OtherCopyFinder.OthersAsync(failing, Store, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ACancelledQuestionIsCancelled()
    {
        var cancelled = new FakeHandlers(_ => throw new OperationCanceledException());
        await Assert.ThrowsAsync<OperationCanceledException>(() => OtherCopyFinder.OthersAsync(cancelled, Store, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task NothingNullIsTaken()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => OtherCopyFinder.OthersAsync(null!, Store, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentNullException>(() => OtherCopyFinder.OthersAsync(new FakeHandlers(_ => []), null!, TestContext.Current.CancellationToken));
    }
}
