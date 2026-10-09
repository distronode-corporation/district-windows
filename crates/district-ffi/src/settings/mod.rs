//! The workspace settings hub, and below it one module per section.
//!
//! The hub lists the core's rows ([`settings_rows`]) for the member's role,
//! District Studio's group first as the core orders them, with the core's note
//! under them ([`settings_note`]). Each row opens its section's route; a
//! section whose packet has not built it shows "Not in this version yet"
//! there. Each section's views, actions and projection are in its own file.

use district_core::{
    Capabilities, ConfigLoad, Event, Model, Route, SaveState, SettingsGroup, SignedIn,
    WorkspaceSection, settings_note, settings_rows,
};
use district_model::PersonaLabelledValue;
use serde::Serialize;

use crate::screen::ScreenView;
use crate::views::FailureView;

pub mod call_handling;
pub mod directory;
pub mod knowledge;
pub mod members;
pub mod messaging;
pub mod numbers;
pub mod persona;
pub mod routing;
pub mod tools;
pub mod voice_studio;

// What the sections share (the settings kit): how a section's page stands
// after its read and its save, the notice a save leaves, and a picker whose
// choices come from the core. The Linux app's settings_kit.rs and
// save_notice.rs, as views.

/// The heading of a section whose save landed and whose settings could not be
/// read back. Not a failure: the save is done.
pub const STALE_TITLE: &str = "Saved";
/// The button that reads a section's settings again, after such a save.
pub const READ_AGAIN: &str = "Read them again";
/// What a picker shows for a stored value that is empty.
pub const NOT_CHOSEN: &str = "Not chosen";

/// Where a section's page stands: being read, failed, saved but not read
/// back, or the form.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Enum)]
pub enum SectionStatus {
    /// Being read: a progress ring, no form.
    Loading,
    /// The read failed: the reason and, when it could help, "Try again"
    /// (`UiEvent::Refresh`). No form.
    Failed {
        /// The heading.
        title: String,
        /// Why.
        failure: FailureView,
    },
    /// A save landed and reading it back failed: "Saved", what to do, and a
    /// read ([`READ_AGAIN`], `UiEvent::Refresh`), never a save.
    Stale {
        /// [`STALE_TITLE`].
        title: String,
        /// The core's words.
        body: String,
        /// [`READ_AGAIN`].
        action: String,
    },
    /// Read: the form shows.
    Ready,
}

/// The page of a section that edits the settings row, from its read and its
/// save.
pub(crate) fn section_status(load: &ConfigLoad, save: &SaveState) -> SectionStatus {
    match (load, save) {
        (ConfigLoad::Loading, _) => SectionStatus::Loading,
        (ConfigLoad::Failed(_), SaveState::SavedButStale(_)) => SectionStatus::Stale {
            title: STALE_TITLE.to_owned(),
            body: SaveState::SAVED_STALE.to_owned(),
            action: READ_AGAIN.to_owned(),
        },
        (ConfigLoad::Failed(failure), _) => SectionStatus::Failed {
            title: ConfigLoad::FAILED_TITLE.to_owned(),
            failure: failure.into(),
        },
        (ConfigLoad::Ready(_), _) => SectionStatus::Ready,
    }
}

/// How a section's last save ended, until it is dismissed or the form changes.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct SaveNoticeView {
    /// "Saved.", or why the save failed (the edits are kept).
    pub message: String,
    /// Whether it saved, rather than failed.
    pub saved: bool,
}

/// The notice for `save`, when there is one. A save that landed and could not
/// be read back is not one: the page says so instead ([`SectionStatus::Stale`]).
pub(crate) fn save_notice(save: &SaveState) -> Option<SaveNoticeView> {
    match save {
        SaveState::Saved => Some(SaveNoticeView {
            message: SaveState::SAVED.to_owned(),
            saved: true,
        }),
        SaveState::Failed(failure) => Some(SaveNoticeView {
            message: FailureView::from(failure).message,
            saved: false,
        }),
        SaveState::Idle | SaveState::Saving | SaveState::SavedButStale(_) => None,
    }
}

/// One choice of a picker.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct ChoiceView {
    /// What is sent when it is chosen.
    pub value: String,
    /// What it reads.
    pub label: String,
}

/// A picker whose choices come from the core, showing a stored value the
/// choices do not list as it is stored.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct PickerView {
    /// The choices, in the core's order.
    pub choices: Vec<ChoiceView>,
    /// The value chosen now.
    pub selected: String,
    /// What the chosen value reads: its choice's label, else the value as
    /// stored, else [`NOT_CHOSEN`].
    pub selected_label: String,
}

/// What the picker shows for `value` when no choice lists it.
pub(crate) fn unlisted(value: &str) -> String {
    if value.trim().is_empty() {
        NOT_CHOSEN.to_owned()
    } else {
        value.to_owned()
    }
}

/// The picker of `list`, with `selected` chosen.
pub(crate) fn picker(list: &[PersonaLabelledValue], selected: &str) -> PickerView {
    PickerView {
        choices: list
            .iter()
            .map(|choice| ChoiceView {
                value: choice.value.clone(),
                label: choice.label.clone(),
            })
            .collect(),
        selected: selected.to_owned(),
        selected_label: list
            .iter()
            .find(|choice| choice.value == selected)
            .map_or_else(|| unlisted(selected), |choice| choice.label.clone()),
    }
}

/// Whether this version has the hub.
pub(crate) const BUILT: bool = true;

/// The hub's heading, as the navigation pane names it.
pub const SETTINGS_TITLE: &str = "Workspace settings";

/// The workspace settings hub: the sections the member's role may open, in
/// groups, and the note under them.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct SettingsHubView {
    /// The heading, [`SETTINGS_TITLE`].
    pub title: String,
    /// The groups, District Studio's first, each with its rows in the core's
    /// order. A group with no row for this role is left out.
    pub groups: Vec<SettingsGroupView>,
    /// The note under the list: what is changed on the website, or for a
    /// viewer, that these are the settings they may read.
    pub note: String,
}

/// One group of the hub, under its heading.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct SettingsGroupView {
    /// The heading ("District Studio", "Workspace").
    pub heading: String,
    /// Its rows, in order.
    pub rows: Vec<SettingsRowView>,
}

/// One row of the hub: a section to open.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct SettingsRowView {
    /// The section it opens (`SettingsAction::OpenSection`).
    pub section: SettingsSection,
    /// Its title.
    pub title: String,
    /// What the section holds.
    pub subtitle: String,
}

/// A section of the workspace settings, as a hub row opens it.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash, Serialize, uniffi::Enum)]
pub enum SettingsSection {
    /// The receptionist's persona (District Studio's Persona).
    Persona,
    /// Its voice (District Studio's Voice, Voice Studio).
    VoiceStudio,
    /// Who answers an inbound call.
    CallHandling,
    /// The call routing rules.
    Routing,
    /// The transfer directory.
    Directory,
    /// What it may do on a call (District Studio's Skills).
    Tools,
    /// The knowledge base.
    Knowledge,
    /// The messaging accounts.
    Messaging,
    /// The members.
    Members,
    /// The phone numbers, which open the phone numbers screen.
    Numbers,
}

impl SettingsSection {
    /// The core's section, and its route.
    pub(crate) fn section(self) -> WorkspaceSection {
        match self {
            Self::Persona => WorkspaceSection::Persona,
            Self::VoiceStudio => WorkspaceSection::VoiceStudio,
            Self::CallHandling => WorkspaceSection::CallHandling,
            Self::Routing => WorkspaceSection::Routing,
            Self::Directory => WorkspaceSection::Directory,
            Self::Tools => WorkspaceSection::Tools,
            Self::Knowledge => WorkspaceSection::Knowledge,
            Self::Messaging => WorkspaceSection::Messaging,
            Self::Members => WorkspaceSection::Members,
            Self::Numbers => WorkspaceSection::Numbers,
        }
    }

    /// The row of `section`; `None` for the hub itself, which is no row.
    fn of(section: WorkspaceSection) -> Option<Self> {
        Some(match section {
            WorkspaceSection::Hub => return None,
            WorkspaceSection::Persona => Self::Persona,
            WorkspaceSection::VoiceStudio => Self::VoiceStudio,
            WorkspaceSection::CallHandling => Self::CallHandling,
            WorkspaceSection::Routing => Self::Routing,
            WorkspaceSection::Directory => Self::Directory,
            WorkspaceSection::Tools => Self::Tools,
            WorkspaceSection::Knowledge => Self::Knowledge,
            WorkspaceSection::Messaging => Self::Messaging,
            WorkspaceSection::Members => Self::Members,
            WorkspaceSection::Numbers => Self::Numbers,
        })
    }
}

/// Something the member did on the workspace settings hub.
#[derive(Clone, Debug, PartialEq, Eq, uniffi::Enum)]
pub enum SettingsAction {
    /// Open the hub.
    Open,
    /// Open a section from its row.
    OpenSection {
        /// Which.
        section: SettingsSection,
    },
}

/// The core events `action` is, in the order the core is to hear them.
pub(crate) fn events(action: SettingsAction) -> Vec<Event> {
    vec![match action {
        SettingsAction::Open => Event::Navigate(Route::Workspace(WorkspaceSection::Hub)),
        SettingsAction::OpenSection { section } => {
            Event::Navigate(Route::Workspace(section.section()))
        }
    }]
}

/// The page of the hub, for a signed-in model.
pub(crate) fn screen(_model: &Model, signed_in: &SignedIn) -> ScreenView {
    ScreenView::WorkspaceSettings {
        view: hub_view(&signed_in.capabilities()),
    }
}

/// The hub for a member with `capabilities`.
fn hub_view(capabilities: &Capabilities) -> SettingsHubView {
    let mut groups: Vec<(SettingsGroup, SettingsGroupView)> = Vec::new();
    for row in settings_rows(capabilities) {
        let Some(section) = SettingsSection::of(row.section) else {
            continue;
        };
        let view = SettingsRowView {
            section,
            title: row.title.to_owned(),
            subtitle: row.subtitle.to_owned(),
        };
        match groups.last_mut() {
            Some((group, rows)) if *group == row.group => rows.rows.push(view),
            _ => groups.push((
                row.group,
                SettingsGroupView {
                    heading: row.group.heading().to_owned(),
                    rows: vec![view],
                },
            )),
        }
    }
    SettingsHubView {
        title: SETTINGS_TITLE.to_owned(),
        groups: groups.into_iter().map(|(_, group)| group).collect(),
        note: settings_note(capabilities).to_owned(),
    }
}

#[cfg(test)]
impl SettingsHubView {
    /// A hub with every part filled in, for the boundary's own tests.
    pub(crate) fn sample() -> Self {
        hub_view(&Capabilities::for_role(Some("agency")))
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    /// Every section the core has but the hub is a row, and goes back to the
    /// same section.
    #[test]
    fn each_row_opens_its_own_section() {
        for section in WorkspaceSection::ALL {
            match SettingsSection::of(section) {
                Some(row) => assert_eq!(row.section(), section),
                None => assert_eq!(section, WorkspaceSection::Hub),
            }
        }
    }

    #[test]
    fn the_hub_groups_the_core_s_rows_studio_first() {
        let agency = hub_view(&Capabilities::for_role(Some("agency")));
        let headings: Vec<&str> = agency.groups.iter().map(|g| g.heading.as_str()).collect();
        assert_eq!(headings, ["District Studio", "Workspace"]);
        assert_eq!(agency.groups[0].rows[0].section, SettingsSection::Persona);
        let rows: usize = agency.groups.iter().map(|g| g.rows.len()).sum();
        assert_eq!(
            rows,
            settings_rows(&Capabilities::for_role(Some("agency"))).len()
        );
        let viewer = hub_view(&Capabilities::for_role(Some("viewer")));
        assert_ne!(viewer.note, agency.note);
        let sections: Vec<SettingsSection> = viewer
            .groups
            .iter()
            .flat_map(|g| g.rows.iter().map(|r| r.section))
            .collect();
        assert_eq!(
            sections,
            [
                SettingsSection::CallHandling,
                SettingsSection::Knowledge,
                SettingsSection::Messaging
            ]
        );
    }
}
