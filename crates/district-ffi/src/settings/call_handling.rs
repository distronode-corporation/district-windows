//! Who answers an inbound call, and whether this member is rung (District
//! Studio's Call handling), as District AI for Linux's call handling page
//! shows it (`district-app/src/pages/call_handling.rs`).
//!
//! Two settings of two scopes, read apart and saved apart. Who answers and how
//! long the devices ring are the workspace's, chosen here and saved with a
//! button, sending only what changed; whether the member is rung is their own,
//! and the switch sends at once. Each is read on its own and fails on its own.
//! A viewer reads both and is offered no control.
//!
//! The question the core asks before a save that replaces a list
//! ([`QuestionView`]) is shared with the routing rules and the transfer
//! directory, which use it from here; the rest of what the sections share is
//! the settings kit's (`settings/mod.rs`).

use district_core::{
    AvailabilityView as AvailabilityRead, CallHandlingEvent, CallHandlingSection,
    CallHandlingView as HandlingRead, Capabilities, ConfigLoad, Event, Model, Route, SignedIn,
    WorkspaceSection, call_handling_mode_body, call_handling_mode_label,
};
use district_model::{CallHandlingMode, MAX_APP_RING_SECONDS, MIN_APP_RING_SECONDS};
use serde::Serialize;

use super::{SaveNoticeView, save_notice};
use crate::screen::ScreenView;
use crate::views::LoadStatus;

/// Whether this version has the area's screens. The packet that builds them
/// sets it; until then [`crate::nav::built`] says no for its routes.
pub(crate) const BUILT: bool = true;

/// The section's heading: the hub row's title, which opens it.
pub const CALL_HANDLING_TITLE: &str = "Call handling";
/// The heading over who answers.
pub const HANDLING_HEADING: &str = "Who answers";
/// What that part is for.
pub const HANDLING_NOTE: &str = "For every member of this workspace.";
/// The heading of a stored mode this app does not know.
pub const UNKNOWN_MODE_TITLE: &str = "A setting this app does not know";
/// The ring's label.
pub const RING_LABEL: &str = "How long your devices ring";
/// The heading over the member's own availability.
pub const AVAILABILITY_HEADING: &str = "Your availability";
/// What that part is for.
pub const AVAILABILITY_NOTE: &str =
    "Whether your own devices ring for this workspace's calls. A change is sent at once.";
/// The switch's label.
pub const AVAILABILITY_LABEL: &str = "Ring me for calls";
/// The heading of a failed availability read.
pub const AVAILABILITY_FAILED_TITLE: &str = "Could not read whether you are rung";

/// The modes, in the order they are offered.
const MODES: [CallHandlingMode; 3] = [
    CallHandlingMode::AiFirst,
    CallHandlingMode::AiThenApp,
    CallHandlingMode::AppFirst,
];

/// A ring, in words.
pub(crate) fn ring_words(seconds: i64) -> String {
    format!("{seconds} seconds")
}

/// The question the core asks before a save that replaces a whole list.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct QuestionView {
    /// The question's heading.
    pub title: String,
    /// What saving does.
    pub body: String,
    /// The confirming button's label.
    pub action: String,
    /// Whether saving removes everything (the confirming button is then not
    /// the default).
    pub destructive: bool,
}

/// Who answers, as the member chooses it.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash, Serialize, uniffi::Enum)]
pub enum CallHandlingChoice {
    /// The receptionist answers.
    AiFirst,
    /// The receptionist answers, then the devices ring.
    AiThenApp,
    /// The devices ring first.
    AppFirst,
}

impl CallHandlingChoice {
    fn mode(self) -> CallHandlingMode {
        match self {
            Self::AiFirst => CallHandlingMode::AiFirst,
            Self::AiThenApp => CallHandlingMode::AiThenApp,
            Self::AppFirst => CallHandlingMode::AppFirst,
        }
    }

    fn of(mode: CallHandlingMode) -> Self {
        match mode {
            CallHandlingMode::AiFirst => Self::AiFirst,
            CallHandlingMode::AiThenApp => Self::AiThenApp,
            CallHandlingMode::AppFirst => Self::AppFirst,
        }
    }
}

/// One way of answering, as offered.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct CallHandlingModeView {
    /// Which (`CallHandlingAction::SelectMode`).
    pub mode: CallHandlingChoice,
    /// Its name.
    pub label: String,
    /// What it does.
    pub body: String,
    /// Whether it is the one on screen.
    pub selected: bool,
}

/// The member's own availability.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct AvailabilityPanelView {
    /// The heading, [`AVAILABILITY_HEADING`].
    pub heading: String,
    /// What it is for, [`AVAILABILITY_NOTE`].
    pub note: String,
    /// Where its read stands, on its own: a failed read offers a retry and no
    /// switch.
    pub status: LoadStatus,
    /// The switch's label, [`AVAILABILITY_LABEL`].
    pub label: String,
    /// Whether the member's devices ring for this workspace.
    pub available: bool,
    /// Whether the switch shows: read, with no reason the member cannot be
    /// rung.
    pub show_switch: bool,
    /// Whether the switch works: shown, the member may change things, and no
    /// change on its way.
    pub can_toggle: bool,
    /// Why the member cannot be made available, in the core's words, in place
    /// of the switch.
    pub blocked: Option<String>,
    /// Whether a change is on its way (show a progress ring).
    pub changing: bool,
    /// How the last change ended, until dismissed.
    pub notice: Option<SaveNoticeView>,
}

/// The call handling section.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct CallHandlingView {
    /// The heading, [`CALL_HANDLING_TITLE`].
    pub title: String,
    /// The heading over who answers, [`HANDLING_HEADING`].
    pub handling_heading: String,
    /// What that part is for, [`HANDLING_NOTE`].
    pub handling_note: String,
    /// Where the read of who answers stands. A failed read offers a retry and
    /// no control.
    pub status: LoadStatus,
    /// The ways of answering offered, in order, once read. A viewer is shown
    /// only the one in force.
    pub modes: Vec<CallHandlingModeView>,
    /// A stored mode this app does not know, as the core keeps it, under
    /// [`UNKNOWN_MODE_TITLE`].
    pub unknown_mode: Option<String>,
    /// The ring's label, [`RING_LABEL`].
    pub ring_label: String,
    /// The hint under it: the range the service accepts.
    pub ring_hint: String,
    /// How long the devices ring, in seconds, as the form has it.
    pub ring_seconds: i64,
    /// The same, in words ("20 seconds").
    pub ring_words: String,
    /// The shortest ring the service accepts.
    pub ring_min: i64,
    /// The longest.
    pub ring_max: i64,
    /// Whether the member's role may change call handling. A viewer reads.
    pub can_change: bool,
    /// Whether the mode and the ring can be changed now: read, the member may,
    /// and no save on its way.
    pub can_edit: bool,
    /// Whether "Save" works: something changed and nothing is on its way.
    pub can_save: bool,
    /// Whether a save is on its way (show a progress ring).
    pub saving: bool,
    /// How the last save ended, until dismissed or the form changes.
    pub notice: Option<SaveNoticeView>,
    /// What a viewer is told, in place of the controls.
    pub viewer_note: Option<String>,
    /// The member's own availability.
    pub availability: AvailabilityPanelView,
}

impl Default for CallHandlingView {
    /// The section before anything is read.
    fn default() -> Self {
        Self {
            title: CALL_HANDLING_TITLE.to_owned(),
            handling_heading: HANDLING_HEADING.to_owned(),
            handling_note: HANDLING_NOTE.to_owned(),
            status: LoadStatus::Loading,
            modes: Vec::new(),
            unknown_mode: None,
            ring_label: RING_LABEL.to_owned(),
            ring_hint: CallHandlingSection::RING_HINT.to_owned(),
            ring_seconds: MIN_APP_RING_SECONDS,
            ring_words: ring_words(MIN_APP_RING_SECONDS),
            ring_min: MIN_APP_RING_SECONDS,
            ring_max: MAX_APP_RING_SECONDS,
            can_change: false,
            can_edit: false,
            can_save: false,
            saving: false,
            notice: None,
            viewer_note: None,
            availability: AvailabilityPanelView {
                heading: AVAILABILITY_HEADING.to_owned(),
                note: AVAILABILITY_NOTE.to_owned(),
                status: LoadStatus::Loading,
                label: AVAILABILITY_LABEL.to_owned(),
                available: false,
                show_switch: false,
                can_toggle: false,
                blocked: None,
                changing: false,
                notice: None,
            },
        }
    }
}

/// Something the member did on the call handling section.
#[derive(Clone, Debug, PartialEq, Eq, uniffi::Enum)]
pub enum CallHandlingAction {
    /// Open the call handling section.
    Open,
    /// Choose who answers. Saved with `Save`.
    SelectMode {
        /// Which.
        mode: CallHandlingChoice,
    },
    /// Choose how long the devices ring; the core moves it into the range the
    /// service accepts. Saved with `Save`.
    SetRingSeconds {
        /// In seconds.
        seconds: i64,
    },
    /// Save what changed, for every member.
    Save,
    /// Be rung for this workspace's calls, or not. Sent at once.
    SetAvailable {
        /// Whether.
        available: bool,
    },
    /// Put the notices away.
    DismissNotices,
}

/// The core events `action` is, in the order the core is to hear them.
pub(crate) fn events(action: CallHandlingAction) -> Vec<Event> {
    vec![match action {
        CallHandlingAction::Open => {
            Event::Navigate(Route::Workspace(WorkspaceSection::CallHandling))
        }
        CallHandlingAction::SelectMode { mode } => {
            Event::CallHandling(CallHandlingEvent::SelectMode(mode.mode()))
        }
        CallHandlingAction::SetRingSeconds { seconds } => {
            Event::CallHandling(CallHandlingEvent::SetRingSeconds(seconds))
        }
        CallHandlingAction::Save => Event::CallHandling(CallHandlingEvent::Save),
        CallHandlingAction::SetAvailable { available } => {
            Event::CallHandling(CallHandlingEvent::SetAvailable(available))
        }
        CallHandlingAction::DismissNotices => {
            Event::CallHandling(CallHandlingEvent::DismissNotices)
        }
    }]
}

/// The section as the core holds it, for a member with `capabilities`.
pub(crate) fn call_handling_view(
    section: &CallHandlingSection,
    capabilities: &Capabilities,
) -> CallHandlingView {
    let can_change = capabilities.can_change;
    let mut view = CallHandlingView {
        can_change,
        viewer_note: (!can_change).then(|| CallHandlingSection::VIEWER.to_owned()),
        availability: availability_view(section, can_change),
        ..CallHandlingView::default()
    };
    match &section.handling {
        HandlingRead::Loading => {}
        HandlingRead::Failed(why) => {
            view.status = LoadStatus::failed(ConfigLoad::FAILED_TITLE, why);
        }
        HandlingRead::Ready(stored) => {
            let mode = section.mode();
            view.status = LoadStatus::Ready;
            view.modes = MODES
                .into_iter()
                .filter(|offered| can_change || mode == Some(*offered))
                .map(|offered| CallHandlingModeView {
                    mode: CallHandlingChoice::of(offered),
                    label: call_handling_mode_label(offered).to_owned(),
                    body: call_handling_mode_body(offered).to_owned(),
                    selected: mode == Some(offered),
                })
                .collect();
            view.unknown_mode = mode
                .is_none()
                .then(|| format!("Stored as \"{}\".", stored.call_handling));
            let seconds = section.ring_seconds().unwrap_or(stored.app_ring_seconds);
            view.ring_seconds = seconds;
            view.ring_words = ring_words(seconds);
            view.can_edit = can_change && section.editable();
            view.can_save = can_change && section.can_save();
            view.saving = section.save.is_busy();
            view.notice = save_notice(&section.save);
        }
    }
    view
}

fn availability_view(section: &CallHandlingSection, can_change: bool) -> AvailabilityPanelView {
    let mut view = CallHandlingView::default().availability;
    match &section.availability {
        AvailabilityRead::Loading => {}
        AvailabilityRead::Failed(why) => {
            view.status = LoadStatus::failed(AVAILABILITY_FAILED_TITLE, why);
        }
        AvailabilityRead::Ready(_) => {
            let blocked = section.availability_blocked();
            view.status = LoadStatus::Ready;
            view.available = section.available_for_calls();
            view.show_switch = blocked.is_none();
            view.can_toggle = can_change && section.can_toggle_availability();
            view.blocked = blocked.map(str::to_owned);
            view.changing = section.availability_save.is_busy();
            view.notice = save_notice(&section.availability_save);
        }
    }
    view
}

/// The page of the call handling section, for a signed-in model. Until the
/// core has opened the section, it is being read.
pub(crate) fn screen(_model: &Model, signed_in: &SignedIn) -> ScreenView {
    ScreenView::CallHandling {
        view: signed_in
            .call_handling
            .as_ref()
            .map_or_else(CallHandlingView::default, |section| {
                call_handling_view(section, &signed_in.capabilities())
            }),
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn a_ring_reads_in_seconds_and_each_choice_is_its_mode() {
        assert_eq!(ring_words(20), "20 seconds");
        for mode in MODES {
            assert_eq!(CallHandlingChoice::of(mode).mode(), mode);
        }
    }
}
