using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels.Calls;
using Xunit;

namespace DistrictAI.Presentation.Tests.Calls;

public sealed class TranscriptViewModelTests
{
    private static TranscriptLineView Line(string id, string text, bool isFinal = true, string speaker = "Caller", string? note = null) =>
        new(id, speaker, text, isFinal, note);

    private static TranscriptView Live(params TranscriptLineView[] lines) =>
        new("Live transcript", TranscriptPhase.Live, "Live", lines, lines.Length == 0 ? "Nothing has been said yet." : null, null, null);

    /// <summary>A panel, with what it asked to follow and announce recorded.</summary>
    private static (TranscriptViewModel Panel, List<string> Said, Func<int> Follows) Panel()
    {
        var panel = new TranscriptViewModel();
        var said = new List<string>();
        var follows = 0;
        panel.Announced += (_, line) => said.Add(line);
        panel.FollowRequested += (_, _) => follows++;
        return (panel, said, () => follows);
    }

    [Fact]
    public void ItShowsTheLinesAndWhereTheTranscriptStands()
    {
        var (panel, _, _) = Panel();
        panel.Show(Live(
            Line("a", "Good afternoon.", speaker: "Ava", note: "Interrupted"),
            Line("b", "I'd like to book", isFinal: false)));

        Assert.True(panel.IsShown);
        Assert.True(panel.IsExpanded);
        Assert.Equal(("Live transcript", "Live", TranscriptPhase.Live), (panel.Heading, panel.Status, panel.Phase));
        Assert.Equal(["a", "b"], panel.Lines.Select(line => line.Id));
        Assert.Equal("Ava: Good afternoon. (Interrupted)", panel.Lines[0].Spoken);
        Assert.True(panel.Lines[0].HasNote);
        Assert.True(panel.Lines[1].IsInterim);
        Assert.Equal("Caller: I'd like to book", panel.Lines[1].Spoken);
        Assert.False(panel.HasWaiting);
        Assert.False(panel.HasIncomplete);
        Assert.False(panel.HasFullText);
        Assert.False(panel.HasFullNote);
    }

    [Fact]
    public void OnlyAFinalLineIsAnnouncedAndOnlyOnce()
    {
        var (panel, said, _) = Panel();
        panel.Show(Live(Line("b", "I'd like", isFinal: false)));
        panel.Show(Live(Line("b", "I'd like to book", isFinal: false)));
        Assert.Empty(said);

        panel.Show(Live(Line("b", "I'd like to book a cleaning.")));
        panel.Show(Live(Line("b", "I'd like to book a cleaning."), Line("c", "Of", isFinal: false, speaker: "Ava")));
        Assert.Equal(["Caller: I'd like to book a cleaning."], said);
    }

    [Fact]
    public void ACatchUpIsReadAsItsLastFewLines()
    {
        var (panel, said, _) = Panel();
        panel.Show(Live([.. Enumerable.Range(1, 5).Select(n => Line($"l{n}", $"Line {n}"))]));
        Assert.Equal(TranscriptViewModel.MostAnnouncedAtOnce, said.Count);
        Assert.Equal(["Caller: Line 3", "Caller: Line 4", "Caller: Line 5"], said);
    }

    [Fact]
    public void NewLinesAreFollowedUntilTheMemberScrollsUp()
    {
        var (panel, _, follows) = Panel();
        panel.Show(Live(Line("a", "One")));
        Assert.Equal(1, follows());
        Assert.True(panel.Following);

        // The same lines again: nothing to follow.
        panel.Show(Live(Line("a", "One")));
        Assert.Equal(1, follows());

        panel.Scrolled(atEnd: false);
        panel.Show(Live(Line("a", "One"), Line("b", "Two")));
        Assert.False(panel.Following);
        Assert.Equal(1, follows());

        panel.Scrolled(atEnd: true);
        panel.Show(Live(Line("a", "One"), Line("b", "Two"), Line("c", "Three")));
        Assert.Equal(2, follows());

        // An interim line revised in place is new text to follow.
        panel.Show(Live(Line("a", "One"), Line("b", "Two"), Line("c", "Three, four")));
        Assert.Equal(3, follows());
    }

    [Fact]
    public void TheNextCallStartsAtTheEndWithNothingRead()
    {
        var (panel, said, follows) = Panel();
        panel.IsExpanded = false;
        panel.Show(Live(Line("a", "One")));
        panel.Scrolled(atEnd: false);
        panel.Show(null);
        Assert.False(panel.IsShown);
        Assert.Empty(panel.Lines);
        Assert.True(panel.Following);

        panel.Show(Live(Line("a", "One")));
        Assert.Equal(["Caller: One", "Caller: One"], said);
        Assert.Equal(2, follows());
        // The member's choice to fold it away stays.
        Assert.False(panel.IsExpanded);
    }

    [Fact]
    public void WaitingIncompleteAndTheFullTranscriptShow()
    {
        var (panel, _, _) = Panel();
        panel.Show(Live() with { Incomplete = "Earlier lines will appear in the full transcript after the call." });
        Assert.True(panel.HasWaiting);
        Assert.Equal("Nothing has been said yet.", panel.Waiting);
        Assert.True(panel.HasIncomplete);

        panel.Show(Live(Line("a", "One")) with
        {
            Phase = TranscriptPhase.Ended,
            Status = "Call ended",
            Full = new FullTranscriptView(true, null, "Loading the full transcript."),
        });
        Assert.True(panel.FullLoading);
        Assert.True(panel.HasFullNote);
        Assert.False(panel.HasFullText);

        panel.Show(Live(Line("a", "One")) with
        {
            Phase = TranscriptPhase.Ended,
            Full = new FullTranscriptView(false, "Agent: Good afternoon.", null),
        });
        Assert.False(panel.FullLoading);
        Assert.False(panel.HasFullNote);
        Assert.True(panel.HasFullText);
        Assert.Equal("Agent: Good afternoon.", panel.FullText);
    }

    [Fact]
    public void TheCallBarCarriesTheTranscript()
    {
        var bar = new CallBarViewModel();
        bar.Show(V.Call(transcript: Live(Line("a", "One"))));
        Assert.True(bar.Transcript.IsShown);
        bar.Show(V.Call());
        Assert.False(bar.Transcript.IsShown);
        bar.Show(null);
        Assert.False(bar.Transcript.IsShown);
    }
}
