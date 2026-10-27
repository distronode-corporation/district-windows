using DistrictAI.Core.Ffi;
using DistrictAI.L10n;

namespace DistrictAI.ViewModels.Hq;

/// <summary>
/// One line of the District HQ conversation: the member's question, an
/// answer (rich text, with Report), the app's note on a confirmed change, or
/// words spoken in a voice session (plain text, dimmed until final).
/// </summary>
public sealed record HqMessageItem
{
    /// <summary>Where it is in the conversation, from 0.</summary>
    public int Index { get; init; }

    /// <summary>The member's question, typed or spoken: drawn as the member's bubble.</summary>
    public bool IsQuestion { get; init; }

    /// <summary>Words spoken in a voice session, the member's or District HQ's.</summary>
    public bool IsSpoken { get; init; }

    /// <summary>What District HQ said aloud: plain text in the answer's place, with no Report.</summary>
    public bool IsSpokenAnswer { get; init; }

    /// <summary>Spoken words still being transcribed, which may change.</summary>
    public bool IsInterim { get; init; }

    /// <summary>How opaque the words are: dimmed until they are final.</summary>
    public double TextOpacity => IsInterim ? 0.6 : 1.0;

    /// <summary>District HQ's answer.</summary>
    public bool IsAnswer { get; init; }

    /// <summary>The app's note on a confirmed change.</summary>
    public bool IsNote { get; init; }

    /// <summary>A question's or a note's words; an answer's, without their styles.</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>An answer's paragraphs; empty for anything else.</summary>
    public IReadOnlyList<RichParagraph> Paragraphs { get; init; } = [];

    /// <summary>A note saying the change was made.</summary>
    public bool IsAppliedNote { get; init; }

    /// <summary>A note asking the member to look: the change was not made, or another was.</summary>
    public bool IsWarningNote { get; init; }

    /// <summary>How Report is offered on an answer.</summary>
    public ReportAvailability Report { get; init; } = ReportAvailability.Hidden;

    /// <summary>The Report button's words, or empty for none.</summary>
    public string ReportLabel => Display.ReportLabel(Report);

    /// <summary>Whether there is a Report button.</summary>
    public bool ReportVisible => ReportLabel.Length > 0;

    /// <summary>Whether it can be pressed now: not while a report is being sent.</summary>
    public bool ReportEnabled { get; init; }

    /// <summary>What a screen reader says for the line: who said it, then what.</summary>
    public string AccessibleName =>
        IsQuestion ? (IsSpoken ? S.HqYouSaid(Text) : S.HqYouAsked(Text))
        : IsSpokenAnswer ? S.HqAssistantSaid(Text)
        : IsAnswer ? S.HqAnswered(Text)
        : Text;

    /// <summary>
    /// The same line when it says the same thing. An answer's paragraphs are
    /// made afresh for every snapshot, so they are compared by the text they
    /// came from, and a line drawn once is kept rather than drawn again.
    /// </summary>
    public bool Equals(HqMessageItem? other) =>
        other is not null
        && Index == other.Index
        && IsQuestion == other.IsQuestion
        && IsAnswer == other.IsAnswer
        && IsNote == other.IsNote
        && IsSpoken == other.IsSpoken
        && IsSpokenAnswer == other.IsSpokenAnswer
        && IsInterim == other.IsInterim
        && Text == other.Text
        && IsAppliedNote == other.IsAppliedNote
        && IsWarningNote == other.IsWarningNote
        && Report == other.Report
        && ReportEnabled == other.ReportEnabled
        && (!IsAnswer || Paragraphs.Count == other.Paragraphs.Count);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Index, Text, Report, ReportEnabled);

    internal static HqMessageItem From(HqMessageView message, bool reportEnabled) => message switch
    {
        HqMessageView.Question question => new HqMessageItem
        {
            Index = (int)question.Index,
            IsQuestion = true,
            Text = question.Text,
        },
        HqMessageView.Answer answer => FromAnswer(answer, reportEnabled),
        HqMessageView.Spoken spoken => new HqMessageItem
        {
            Index = (int)spoken.Index,
            IsSpoken = true,
            IsQuestion = spoken.Member,
            IsSpokenAnswer = !spoken.Member,
            IsInterim = !spoken.IsFinal,
            Text = spoken.Text,
        },
        HqMessageView.Note note => new HqMessageItem
        {
            Index = (int)note.Index,
            IsNote = true,
            Text = note.Text,
            IsAppliedNote = note.Applied,
            IsWarningNote = !note.Applied,
        },
        // A line a later core adds: shown as nothing until this build knows it.
        _ => new HqMessageItem { Index = -1 },
    };

    private static HqMessageItem FromAnswer(HqMessageView.Answer answer, bool reportEnabled)
    {
        var paragraphs = RichText.Layout(answer.Text);
        return new HqMessageItem
        {
            Index = (int)answer.Index,
            IsAnswer = true,
            Text = RichText.Plain(paragraphs),
            Paragraphs = paragraphs,
            Report = answer.Report,
            ReportEnabled = reportEnabled,
        };
    }
}
