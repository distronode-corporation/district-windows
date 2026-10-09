//! What the receptionist says, and the language it speaks (District Studio's
//! Persona), as District AI for Linux shows it (`pages/persona.rs`,
//! `pages/audition.rs`).
//!
//! Its three texts are editable once the settings are read; its language and
//! answer length only as the options read for the workspace's region offer
//! them (the core refuses anything else); the stored engine is named, and
//! changed in Voice Studio. A save sends only what changed, and leaving with
//! changes not saved asks first (crate::guard). The audition is a real, billed
//! call, started only by Start in its own dialog, which says so before
//! anything starts. Only a member who may change the workspace reaches this
//! section: the core keeps it from a viewer (`Capabilities::allows`), and
//! refuses a viewer's edits too.

use district_core::{
    Event, MediaConnection, MediaOwner, MediaSession, Model, PersonaEngineEdit, PersonaEvent,
    PersonaOptionsLoad, PersonaPreview, PersonaSection, PersonaText, Route, SignedIn,
    WorkspaceSection,
};
use district_model::PersonaEngineOption;
use serde::Serialize;

use super::{
    PickerView, SaveNoticeView, SectionStatus, picker, save_notice, section_status, unlisted,
};
use crate::screen::ScreenView;
use crate::views::FailureView;

/// Whether this version has the area's screens.
pub(crate) const BUILT: bool = true;

/// The section's heading: its row's title in the hub, which opens it.
pub const TITLE: &str = "Persona";
/// The texts' heading.
pub const TEXTS_HEADING: &str = "What it says";
/// The language half's heading.
pub const ENGINE_HEADING: &str = "Language and answers";
/// The line while the options are being read.
pub const OPTIONS_LOADING: &str = "Reading the languages this workspace may use.";
/// The button that opens Voice Studio.
pub const OPEN_STUDIO: &str = "Open Voice Studio";
/// The button that opens the audition dialog.
pub const PREVIEW_ACTION: &str = "Try this receptionist";
/// The save button.
pub const SAVE_ACTION: &str = "Save";
/// The audition's Start, which places the billed call.
pub const START_ACTION: &str = "Start";
/// The audition's Stop.
pub const STOP_ACTION: &str = "Stop";
/// The line while the audition's call is being asked for.
pub const STARTING: &str = "Starting the call.";
/// The line while its room is being joined.
pub const CONNECTING: &str = "Connecting to your receptionist.";
/// The line while it is on.
pub const CONNECTED: &str = "On the call with your receptionist.";
/// The line once it ended.
pub const ENDED: &str = "The audition ended.";
/// The line while another audition must wait.
pub const COOLING: &str = "Another audition can start in a few seconds.";

/// The persona section.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct PersonaView {
    /// The heading, [`TITLE`].
    pub title: String,
    /// Where the page stands; the form shows only when [`SectionStatus::Ready`].
    pub status: SectionStatus,
    /// The texts' heading, [`TEXTS_HEADING`].
    pub texts_heading: String,
    /// The receptionist's name, as on screen.
    pub name: String,
    /// Its opening line.
    pub greeting: String,
    /// How it behaves.
    pub personality: String,
    /// Whether the texts can be edited: read, and nothing on its way.
    pub text_editable: bool,
    /// The line under the texts: clearing a box saves it empty.
    pub clear_hint: String,
    /// The language half's heading, [`ENGINE_HEADING`].
    pub engine_heading: String,
    /// The language half, once the settings and the options are both read.
    pub engine: Option<PersonaEngineView>,
    /// Whether the language half can be edited.
    pub engine_editable: bool,
    /// Why the language half is not shown: being read, or read only because
    /// the options could not be read.
    pub engine_note: Option<String>,
    /// Whether "Try again" is offered for the options (`UiEvent::Refresh`).
    pub can_retry_options: bool,
    /// Fitting a chain of the member's own to a new language, after a save.
    pub refit_line: Option<String>,
    /// How the last save ended, until dismissed or the form changes.
    pub notice: Option<SaveNoticeView>,
    /// Whether a save is on its way (show a progress ring).
    pub saving: bool,
    /// Whether "Save" works: something changed, and nothing is on its way.
    pub can_save: bool,
    /// The save button, [`SAVE_ACTION`].
    pub save_label: String,
    /// Whether "Try this receptionist" is offered: it needs the options.
    pub can_preview: bool,
    /// Whether it can be pressed now: no audition dialog open.
    pub preview_enabled: bool,
    /// The audition button, [`PREVIEW_ACTION`].
    pub preview_label: String,
    /// The audition dialog, while it is open.
    pub audition: Option<AuditionView>,
}

/// The language half.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct PersonaEngineView {
    /// The stored engine's name, changed in Voice Studio.
    pub engine_label: String,
    /// Where the engine and the voice are changed.
    pub studio_hint: String,
    /// The button that opens Voice Studio, [`OPEN_STUDIO`].
    pub studio_label: String,
    /// The languages the stored engine speaks.
    pub language: PickerView,
    /// The answer lengths it offers.
    pub response_length: PickerView,
}

/// The audition dialog: a real, billed call to the receptionist with the form
/// on screen.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct AuditionView {
    /// The heading.
    pub title: String,
    /// What it is, said before anything starts: a real call, billed.
    pub billed_note: String,
    /// Where it stands, when there is something to say.
    pub state: Option<String>,
    /// Whether it is being started or joined (show a progress ring).
    pub waiting: bool,
    /// Why it could not start or was not joined.
    pub failure: Option<FailureView>,
    /// Whether Start shows.
    pub show_start: bool,
    /// Whether Start works now (not while another must wait).
    pub can_start: bool,
    /// Whether Stop shows.
    pub show_stop: bool,
    /// [`COOLING`], while another audition must wait.
    pub cooling: Option<String>,
    /// [`START_ACTION`].
    pub start_label: String,
    /// [`STOP_ACTION`].
    pub stop_label: String,
}

/// Something the member did on the persona section.
#[derive(Clone, Debug, PartialEq, Eq, uniffi::Enum)]
pub enum PersonaAction {
    /// Open the persona section.
    Open,
    /// A text changed: what the box holds now.
    EditText {
        /// Which.
        field: PersonaField,
        /// The text.
        value: String,
    },
    /// A language was chosen.
    ChooseLanguage {
        /// Its value.
        value: String,
    },
    /// An answer length was chosen.
    ChooseResponseLength {
        /// Its value.
        value: String,
    },
    /// Save what changed.
    Save,
    /// Put the save notice away.
    DismissSaveNotice,
    /// Open Voice Studio, where the engine and voice are changed.
    OpenVoiceStudio,
    /// Open the audition dialog. Nothing is asked for yet.
    OpenPreview,
    /// Start the audition: a billed call.
    StartPreview,
    /// Stop it.
    StopPreview,
    /// Close the dialog, stopping any audition.
    ClosePreview,
}

/// One of the persona's texts.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash, Serialize, uniffi::Enum)]
pub enum PersonaField {
    /// The receptionist's name.
    Name,
    /// Its opening line.
    Greeting,
    /// How it behaves.
    Personality,
}

impl From<PersonaField> for PersonaText {
    fn from(field: PersonaField) -> Self {
        match field {
            PersonaField::Name => Self::Name,
            PersonaField::Greeting => Self::Greeting,
            PersonaField::Personality => Self::Personality,
        }
    }
}

/// The core events `action` is, in the order the core is to hear them.
pub(crate) fn events(action: PersonaAction) -> Vec<Event> {
    let persona = Event::Persona;
    vec![match action {
        PersonaAction::Open => Event::Navigate(Route::Workspace(WorkspaceSection::Persona)),
        PersonaAction::EditText { field, value } => persona(PersonaEvent::EditText {
            field: field.into(),
            value,
        }),
        PersonaAction::ChooseLanguage { value } => {
            persona(PersonaEvent::Engine(PersonaEngineEdit::Language(value)))
        }
        PersonaAction::ChooseResponseLength { value } => persona(PersonaEvent::Engine(
            PersonaEngineEdit::ResponseLength(value),
        )),
        PersonaAction::Save => persona(PersonaEvent::Save),
        PersonaAction::DismissSaveNotice => persona(PersonaEvent::DismissSaveNotice),
        PersonaAction::OpenVoiceStudio => {
            Event::Navigate(Route::Workspace(WorkspaceSection::VoiceStudio))
        }
        PersonaAction::OpenPreview => persona(PersonaEvent::OpenPreview),
        PersonaAction::StartPreview => persona(PersonaEvent::StartPreview),
        PersonaAction::StopPreview => persona(PersonaEvent::StopPreview),
        PersonaAction::ClosePreview => persona(PersonaEvent::ClosePreview),
    }]
}

/// The page of the persona section, for a signed-in model.
pub(crate) fn screen(_model: &Model, signed_in: &SignedIn) -> ScreenView {
    let media = signed_in
        .media
        .as_ref()
        .filter(|media| media.owner == MediaOwner::Audition);
    ScreenView::Persona {
        view: signed_in.persona.as_ref().map_or_else(
            || persona_view(None, None),
            |section| persona_view(Some(section), media),
        ),
    }
}

/// The section `section` holds, with the audition's call `media`; being read
/// when there is no section yet.
fn persona_view(section: Option<&PersonaSection>, media: Option<&MediaSession>) -> PersonaView {
    let text = |field| section.map_or("", |s| s.value(field)).to_owned();
    let engine = section.and_then(|s| s.engine().map(|values| (s, values)));
    let failure = match section.map(|s| &s.options) {
        Some(PersonaOptionsLoad::Failed(failure)) => Some(failure),
        _ => None,
    };
    let engine_note = match (engine.is_some(), failure) {
        (true, _) => None,
        (false, Some(failure)) => Some(format!(
            "{} {}",
            PersonaSection::ENGINE_READ_ONLY,
            FailureView::from(failure).message
        )),
        (false, None) => Some(OPTIONS_LOADING.to_owned()),
    };
    PersonaView {
        title: TITLE.to_owned(),
        status: section.map_or(SectionStatus::Loading, |s| {
            section_status(&s.config, &s.save)
        }),
        texts_heading: TEXTS_HEADING.to_owned(),
        name: text(PersonaText::Name),
        greeting: text(PersonaText::Greeting),
        personality: text(PersonaText::Personality),
        text_editable: section.is_some_and(PersonaSection::text_editable),
        clear_hint: PersonaSection::CLEAR_HINT.to_owned(),
        engine_heading: ENGINE_HEADING.to_owned(),
        engine: engine.map(|(s, values)| PersonaEngineView {
            engine_label: engine_label(s.engines(), &values.model_id),
            studio_hint: PersonaSection::STUDIO_HINT.to_owned(),
            studio_label: OPEN_STUDIO.to_owned(),
            language: picker(s.languages(), &values.language),
            response_length: picker(s.response_lengths(), &values.response_length),
        }),
        engine_editable: section.is_some_and(PersonaSection::engine_editable),
        engine_note,
        can_retry_options: engine.is_none() && failure.is_some_and(|f| f.retryable),
        refit_line: section
            .and_then(|s| s.refit.as_ref())
            .map(district_core::PersonaRefit::line),
        notice: section.and_then(|s| save_notice(&s.save)),
        saving: section.is_some_and(|s| s.save.is_busy()),
        can_save: section.is_some_and(PersonaSection::can_save),
        save_label: SAVE_ACTION.to_owned(),
        can_preview: section.is_some_and(PersonaSection::can_preview),
        preview_enabled: section.is_some_and(|s| s.preview.is_none()),
        preview_label: PREVIEW_ACTION.to_owned(),
        audition: section.and_then(|s| {
            s.preview
                .as_ref()
                .map(|preview| audition_view(s, preview, media.map(|m| m.connection)))
        }),
    }
}

/// The label of the stored engine `id`: its own when the options list it, else
/// the id as stored.
fn engine_label(engines: &[PersonaEngineOption], id: &str) -> String {
    engines
        .iter()
        .find(|engine| engine.id == id)
        .map_or_else(|| unlisted(id), |engine| engine.label.clone())
}

/// Where the audition's call stands, in words: `connection` is `None` until
/// the call engine has a session for it.
fn connection_words(connection: Option<MediaConnection>) -> &'static str {
    match connection {
        None | Some(MediaConnection::Connecting) => CONNECTING,
        Some(MediaConnection::Connected) => CONNECTED,
        Some(MediaConnection::Reconnecting) => MediaSession::RECONNECTING,
    }
}

/// The dialog for `preview`, with where its call stands.
fn audition_view(
    section: &PersonaSection,
    preview: &PersonaPreview,
    connection: Option<MediaConnection>,
) -> AuditionView {
    let (state, waiting, failure, start) = match preview {
        PersonaPreview::Idle => (None, false, None, true),
        PersonaPreview::Minting => (Some(STARTING), true, None, false),
        PersonaPreview::Ready(_) => (
            Some(connection_words(connection)),
            connection != Some(MediaConnection::Connected),
            None,
            false,
        ),
        PersonaPreview::Failed(failure) => (None, false, Some(failure.into()), true),
        PersonaPreview::Ended => (Some(ENDED), false, None, true),
    };
    AuditionView {
        title: PersonaSection::PREVIEW_TITLE.to_owned(),
        billed_note: PersonaSection::PREVIEW_BILLED.to_owned(),
        state: state.map(str::to_owned),
        waiting,
        failure,
        show_start: start,
        can_start: section.can_start_preview(),
        show_stop: !start,
        cooling: (start && section.preview_cooling).then(|| COOLING.to_owned()),
        start_label: START_ACTION.to_owned(),
        stop_label: STOP_ACTION.to_owned(),
    }
}

#[cfg(test)]
impl PersonaView {
    /// A view for the boundary's own tests (a screen's trip to C# and back).
    pub(crate) fn sample() -> Self {
        persona_view(None, None)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn a_section_not_read_yet_is_loading() {
        let view = persona_view(None, None);
        assert_eq!(view.status, SectionStatus::Loading);
        assert!(!view.text_editable && !view.can_save && !view.can_preview);
        assert_eq!(view.engine_note.as_deref(), Some(OPTIONS_LOADING));
        assert_eq!(view.audition, None);
    }

    #[test]
    fn an_unknown_engine_is_named_as_stored() {
        assert_eq!(engine_label(&[], "custom-pipeline"), "custom-pipeline");
        assert_eq!(engine_label(&[], ""), super::super::NOT_CHOSEN);
    }

    #[test]
    fn a_joined_audition_says_how_its_call_stands() {
        assert_eq!(connection_words(None), CONNECTING);
        assert_eq!(
            connection_words(Some(MediaConnection::Connecting)),
            CONNECTING
        );
        assert_eq!(
            connection_words(Some(MediaConnection::Connected)),
            CONNECTED
        );
        assert_eq!(
            connection_words(Some(MediaConnection::Reconnecting)),
            MediaSession::RECONNECTING
        );
    }
}
