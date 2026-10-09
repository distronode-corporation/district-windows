using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DistrictAI.Core.Ffi;

namespace DistrictAI.ViewModels.Calls;

/// <summary>
/// The live transcript on the call strip, copied from the core's
/// <see cref="TranscriptView"/>: the lines said so far with who said them,
/// where the transcript stands, and once the call is over, the full one.
/// </summary>
/// <remarks>
/// Two rules are the page's, and live here so they can be tested:
/// <list type="bullet">
/// <item>
/// <b>Following.</b> The panel keeps the newest line in view while the member
/// is at the end of it. Once they scroll up to read, new lines do not move
/// them; scrolling back to the end follows again. The page reports where the
/// member is (<see cref="Scrolled"/>) and scrolls when asked
/// (<see cref="FollowRequested"/>).
/// </item>
/// <item>
/// <b>Announcing.</b> Narrator hears a line once, when it is final, as a
/// polite announcement (<see cref="Announced"/>); the revisions of a line
/// still being heard are not read out. A batch of lines that arrive final at
/// once (the transcript caught up after a reconnection) is read as its last
/// <see cref="MostAnnouncedAtOnce"/>, not all of it.
/// </item>
/// </list>
/// </remarks>
public sealed partial class TranscriptViewModel : ObservableObject
{
    /// <summary>The most lines read out from one change of the transcript.</summary>
    public const int MostAnnouncedAtOnce = 3;

    private readonly HashSet<string> _announced = [];
    private bool _following = true;

    /// <summary>Asks the page to bring the newest line into view.</summary>
    internal event EventHandler? FollowRequested;

    /// <summary>A line for Narrator to read, politely: "Caller: I'd like to book a cleaning."</summary>
    internal event EventHandler<string>? Announced;

    /// <summary>Whether the call has a live transcript to show.</summary>
    [ObservableProperty]
    public partial bool IsShown { get; set; }

    /// <summary>
    /// Whether the panel is open. It starts open, and the member's choice
    /// stays for the rest of the call and the next.
    /// </summary>
    [ObservableProperty]
    public partial bool IsExpanded { get; set; } = true;

    /// <summary>The heading: "Live transcript".</summary>
    [ObservableProperty]
    public partial string Heading { get; set; } = string.Empty;

    /// <summary>Where it stands: "Connecting.", "Live", "Reconnecting.", "Call ended", or why there is none.</summary>
    [ObservableProperty]
    public partial string Status { get; set; } = string.Empty;

    /// <summary>Where it stands, for the page's choices.</summary>
    [ObservableProperty]
    public partial TranscriptPhase Phase { get; set; }

    /// <summary>The lines, in order.</summary>
    public ObservableCollection<TranscriptLineItem> Lines { get; } = [];

    /// <summary>What shows while live with no line yet, or empty.</summary>
    [ObservableProperty]
    public partial string Waiting { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="Waiting"/>.</summary>
    [ObservableProperty]
    public partial bool HasWaiting { get; set; }

    /// <summary>The note that earlier lines are missing, or empty.</summary>
    [ObservableProperty]
    public partial string Incomplete { get; set; } = string.Empty;

    /// <summary>Whether there is an <see cref="Incomplete"/>.</summary>
    [ObservableProperty]
    public partial bool HasIncomplete { get; set; }

    /// <summary>The full transcript, once read after the call, or empty.</summary>
    [ObservableProperty]
    public partial string FullText { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="FullText"/>.</summary>
    [ObservableProperty]
    public partial bool HasFullText { get; set; }

    /// <summary>What shows instead of the full transcript: loading, none, or why not; or empty.</summary>
    [ObservableProperty]
    public partial string FullNote { get; set; } = string.Empty;

    /// <summary>Whether there is a <see cref="FullNote"/>.</summary>
    [ObservableProperty]
    public partial bool HasFullNote { get; set; }

    /// <summary>Whether the full transcript is being read.</summary>
    [ObservableProperty]
    public partial bool FullLoading { get; set; }

    /// <summary>Whether new lines keep the newest in view: the member is at the end.</summary>
    public bool Following => _following;

    /// <summary>Draws <paramref name="transcript"/>, or hides the panel when the call has none.</summary>
    internal void Show(TranscriptView? transcript)
    {
        IsShown = transcript is not null;
        if (transcript is null)
        {
            // The next call starts at the end, with nothing read out yet.
            _announced.Clear();
            _following = true;
            Lines.Clear();
            return;
        }
        Heading = transcript.Heading;
        Status = transcript.Status;
        Phase = transcript.Phase;
        Waiting = transcript.Waiting ?? string.Empty;
        HasWaiting = Waiting.Length > 0;
        Incomplete = transcript.Incomplete ?? string.Empty;
        HasIncomplete = Incomplete.Length > 0;
        var full = transcript.Full;
        FullText = full?.Text ?? string.Empty;
        HasFullText = FullText.Length > 0;
        FullNote = full?.Note ?? string.Empty;
        HasFullNote = FullNote.Length > 0;
        FullLoading = full?.Loading ?? false;

        var lines = transcript.Lines.Select(TranscriptLineItem.From).ToList();
        var changed = lines.Count != Lines.Count || !lines.SequenceEqual(Lines);
        Display.Sync(Lines, lines);
        Announce(lines);
        if (changed && _following)
        {
            FollowRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// The member scrolled the lines: <paramref name="atEnd"/> says whether
    /// the newest is in view. Up to read, new lines leave them where they are;
    /// back at the end, they follow again.
    /// </summary>
    internal void Scrolled(bool atEnd) => _following = atEnd;

    private void Announce(List<TranscriptLineItem> lines)
    {
        var fresh = lines.Where(line => line.IsFinal && !_announced.Contains(line.Id)).ToList();
        foreach (var line in fresh)
        {
            _announced.Add(line.Id);
        }
        foreach (var line in fresh.Skip(Math.Max(0, fresh.Count - MostAnnouncedAtOnce)))
        {
            Announced?.Invoke(this, line.Spoken);
        }
    }
}

/// <summary>One line of the live transcript.</summary>
/// <param name="Id">The line's id, the same across its revisions.</param>
/// <param name="Speaker">Who said it.</param>
/// <param name="Text">What was said.</param>
/// <param name="IsFinal">Whether it is settled; one that is not is drawn as provisional.</param>
/// <param name="Note">"Interrupted", under an assistant line that was cut off; or empty.</param>
public sealed record TranscriptLineItem(string Id, string Speaker, string Text, bool IsFinal, string Note)
{
    /// <summary>Whether the line is still being heard.</summary>
    public bool IsInterim => !IsFinal;

    /// <summary>Whether there is a <see cref="Note"/>.</summary>
    public bool HasNote => Note.Length > 0;

    /// <summary>What Narrator reads for the line, and announces once it is final.</summary>
    public string Spoken => HasNote ? $"{Speaker}: {Text} ({Note})" : $"{Speaker}: {Text}";

    internal static TranscriptLineItem From(TranscriptLineView line) =>
        new(line.Id, line.Speaker, line.Text, line.IsFinal, line.Note ?? string.Empty);
}
