using DistrictAI.Core.Ffi;

namespace DistrictAI.ViewModels.Hq;

/// <summary>
/// A change proposed by voice, as its card shows it: the service's sentence,
/// the note under it, and Cancel and Confirm. The same card on District HQ and
/// in the voice dock. Every word is the core's.
/// </summary>
public sealed record HqVoiceCardItem
{
    /// <summary>The proposal: what Confirm and Cancel name.</summary>
    public string ProposalId { get; init; } = string.Empty;

    /// <summary>The heading.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>The service's sentence saying what the change would do.</summary>
    public string Summary { get; init; } = string.Empty;

    /// <summary>What to do with it or what is being done; after a failed confirmation, that the change may have been made. Or empty.</summary>
    public string Note { get; init; } = string.Empty;

    /// <summary>Whether there is a <see cref="Note"/>.</summary>
    public bool HasNote => Note.Length > 0;

    /// <summary>Why the last click failed, or empty.</summary>
    public string Failure { get; init; } = string.Empty;

    /// <summary>Whether there is a <see cref="Failure"/>.</summary>
    public bool HasFailure => Failure.Length > 0;

    /// <summary>Whether a click is being sent for it.</summary>
    public bool IsBusy { get; init; }

    /// <summary>Confirm's words.</summary>
    public string ConfirmLabel { get; init; } = string.Empty;

    /// <summary>Cancel's words.</summary>
    public string CancelLabel { get; init; } = string.Empty;

    /// <summary>Whether Confirm works now.</summary>
    public bool CanConfirm { get; init; }

    /// <summary>Whether Cancel works now.</summary>
    public bool CanCancel { get; init; }

    /// <summary>Whether its Confirm is the suggested action: the first card's only.</summary>
    public bool IsSuggested { get; init; }

    /// <summary>Whether its Confirm is an ordinary button: every card's but the first.</summary>
    public bool IsConfirmPlain => !IsSuggested;

    internal static HqVoiceCardItem From(HqVoiceCardView card) => new()
    {
        ProposalId = card.ProposalId,
        Title = card.Title,
        Summary = card.Summary,
        Note = card.Note ?? string.Empty,
        Failure = Display.Failure(card.Failure),
        IsBusy = card.Busy,
        ConfirmLabel = card.ConfirmLabel,
        CancelLabel = card.CancelLabel,
        CanConfirm = card.CanConfirm,
        CanCancel = card.CanCancel,
        IsSuggested = card.Suggested,
    };
}
