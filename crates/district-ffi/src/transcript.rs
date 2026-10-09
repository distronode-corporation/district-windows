//! The live transcript of the call on this desktop, as the call strip shows
//! it: the lines said so far with who said them, the line still being heard
//! marked as provisional, where the transcript stands, and once the call is
//! over, the full transcript the service keeps.
//!
//! Every word is the core's ([`LiveTranscript`]'s constants, its `status()`
//! and `speaker_label()`), except [`UNAVAILABLE`], for the one state the
//! core leaves to the app: no live transcript for this call.

use district_core::{FinalTranscript, LiveTranscript, LiveTranscriptPhase};
use serde::Serialize;

/// What shows when no live transcript can be shown for the call. The core
/// has no words for this state (its `status()` is `None`); the call log has
/// the transcript once the call is over.
pub const UNAVAILABLE: &str =
    "No live transcript for this call. Its transcript is in the call log after the call.";

/// The live transcript of the call on this desktop.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct TranscriptView {
    /// The heading: "Live transcript".
    pub heading: String,
    /// Where it stands.
    pub phase: TranscriptPhase,
    /// The line under the heading: "Connecting.", "Live", "Reconnecting.",
    /// "Call ended"; or, when no live transcript can be shown, [`UNAVAILABLE`].
    pub status: String,
    /// The lines said so far, in order.
    pub lines: Vec<TranscriptLineView>,
    /// What shows while live with no line yet: "Nothing has been said yet."
    pub waiting: Option<String>,
    /// The note when earlier lines are missing from the live transcript: they
    /// appear in the full transcript after the call.
    pub incomplete: Option<String>,
    /// The full transcript, once the live one has ended.
    pub full: Option<FullTranscriptView>,
}

/// Where the live transcript stands.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash, Serialize, uniffi::Enum)]
pub enum TranscriptPhase {
    /// Asked for; the first lines have not arrived ("Connecting."). This can
    /// last up to 30 seconds: the service holds the request until the first
    /// line.
    Connecting,
    /// Lines arrive as they are spoken.
    Live,
    /// The assistant stopped with an error and may be replaced; the lines
    /// shown stay.
    Reconnecting,
    /// The call, or its transcription, has ended; the lines shown stay.
    Ended,
    /// No live transcript can be shown for this call.
    Unavailable,
}

/// One line of the transcript.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct TranscriptLineView {
    /// The line's id, stable across its revisions: what C# keeps its place
    /// and its announcement by.
    pub id: String,
    /// Who said it: "Caller", the assistant's persona name or "Assistant",
    /// or "Other speaker".
    pub speaker: String,
    /// What was said: for an interrupted assistant line, what was actually
    /// played.
    pub text: String,
    /// Whether the line is settled. A line that is not is still being heard:
    /// drawn as provisional, replaced in place by its later revisions, and not
    /// announced until it is final.
    pub is_final: bool,
    /// "Interrupted", under an assistant line that was cut off.
    pub note: Option<String>,
}

/// The full transcript the service keeps, read once the live one has ended.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct FullTranscriptView {
    /// Whether it is being read: "Loading the full transcript."
    pub loading: bool,
    /// The transcript, once read.
    pub text: Option<String>,
    /// What shows instead of it: loading, none for this call, or why it could
    /// not be read.
    pub note: Option<String>,
}

/// The view of `transcript`.
pub(crate) fn transcript_view(transcript: &LiveTranscript) -> TranscriptView {
    let phase = match transcript.phase() {
        LiveTranscriptPhase::Subscribing => TranscriptPhase::Connecting,
        LiveTranscriptPhase::Live => TranscriptPhase::Live,
        LiveTranscriptPhase::Reconnecting => TranscriptPhase::Reconnecting,
        LiveTranscriptPhase::Ended(_) => TranscriptPhase::Ended,
        LiveTranscriptPhase::Unavailable(_) => TranscriptPhase::Unavailable,
    };
    let lines: Vec<TranscriptLineView> = transcript
        .lines()
        .into_iter()
        .map(|segment| TranscriptLineView {
            id: segment.segment_id.clone(),
            speaker: LiveTranscript::speaker_label(segment).to_owned(),
            text: segment.text.clone(),
            is_final: segment.is_final,
            note: segment
                .interrupted
                .then(|| LiveTranscript::INTERRUPTED.to_owned()),
        })
        .collect();
    TranscriptView {
        heading: LiveTranscript::HEADING.to_owned(),
        phase,
        status: transcript.status().unwrap_or(UNAVAILABLE).to_owned(),
        waiting: (phase == TranscriptPhase::Live && lines.is_empty())
            .then(|| LiveTranscript::WAITING.to_owned()),
        lines,
        incomplete: (!transcript.is_complete()).then(|| LiveTranscript::INCOMPLETE.to_owned()),
        full: full_view(transcript.final_transcript()),
    }
}

/// The full transcript, or `None` while it has not been asked for.
fn full_view(full: &FinalTranscript) -> Option<FullTranscriptView> {
    let view = |loading, text: Option<&str>, note: Option<&str>| FullTranscriptView {
        loading,
        text: text.map(str::to_owned),
        note: note.map(str::to_owned),
    };
    match full {
        FinalTranscript::NotRequested => None,
        FinalTranscript::Fetching { .. } => {
            Some(view(true, None, Some(LiveTranscript::LOADING_FULL)))
        }
        FinalTranscript::Loaded(text) => Some(view(false, Some(text), None)),
        FinalTranscript::Empty => Some(view(false, None, Some(LiveTranscript::NO_TRANSCRIPT))),
        FinalTranscript::Failed(failure) => Some(view(false, None, Some(&failure.message))),
    }
}
