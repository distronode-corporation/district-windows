//! What the receptionist may do on a call, and outside research on contacts
//! (District Studio's Skills), as District AI for Linux shows it
//! (`pages/tools.rs`).
//!
//! Two parts, two saves. The tools are a switch for every tool the core can
//! name and for every stored id it cannot (kept as it came, with a note), saved
//! together: the core sends the list read with the switches applied, and a
//! workspace that never stored a list starts from the core's defaults. Outside
//! research is a switch of its own, saved alone, and sent only when the member
//! moved it. Leaving with either part changed and not saved asks first
//! (crate::guard). Only a member who may change the workspace reaches this
//! section: the core keeps it from a viewer (`Capabilities::allows`), and
//! refuses a viewer's edits too.

use district_core::{
    CapabilityRow, Event, Model, Route, SaveState, SignedIn, ToolsEvent, ToolsSection,
    WorkspaceSection,
};
use serde::Serialize;

use super::{SaveNoticeView, SectionStatus, save_notice, section_status};
use crate::screen::ScreenView;

/// Whether this version has the area's screens.
pub(crate) const BUILT: bool = true;

/// The section's heading: its row's title in the hub, which opens it.
pub const TITLE: &str = "Skills";
/// The tools' heading, as the Linux app words it.
pub const TOOLS_HEADING: &str = "On a call";
/// What the tools are, and what saving them does, as the Linux app words it.
pub const TOOLS_NOTE: &str =
    "What the receptionist may do. Saving replaces the stored list with these switches.";
/// The tools' save button.
pub const SAVE_TOOLS: &str = "Save skills";
/// The research part's heading, as the Linux app words it.
pub const RESEARCH_HEADING: &str = "Research contacts";
/// The research part's save button, as the Linux app words it.
pub const SAVE_RESEARCH: &str = "Save research setting";

/// The Skills section.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct ToolsView {
    /// The heading, [`TITLE`].
    pub title: String,
    /// Where the page stands; the form shows only when [`SectionStatus::Ready`].
    /// A save of either part that landed and was not read back says so here.
    pub status: SectionStatus,
    /// The tools' heading, [`TOOLS_HEADING`].
    pub tools_heading: String,
    /// What the tools are, [`TOOLS_NOTE`].
    pub tools_note: String,
    /// A switch per tool: those the core names, in its order, then each stored
    /// id it has no name for, in the stored order. Empty until read.
    pub tools: Vec<ToolRowView>,
    /// Whether the switches work: read, and no save on its way.
    pub editable: bool,
    /// How the last tools save ended, until dismissed or a switch moves.
    pub tools_notice: Option<SaveNoticeView>,
    /// Whether the tools save is on its way (show a progress ring).
    pub tools_saving: bool,
    /// Whether the tools' Save works: a switch differs from what is stored, and
    /// nothing is on its way.
    pub can_save_tools: bool,
    /// The tools' save button, [`SAVE_TOOLS`].
    pub save_tools_label: String,
    /// The research part's heading, [`RESEARCH_HEADING`].
    pub research_heading: String,
    /// The research switch's title, in the core's words.
    pub research_title: String,
    /// What it does, in the core's words.
    pub research_body: String,
    /// Whether it is on: the member's switch, else what is stored, else off.
    pub research_enabled: bool,
    /// How the last research save ended, until dismissed or the switch moves.
    pub research_notice: Option<SaveNoticeView>,
    /// Whether the research save is on its way.
    pub research_saving: bool,
    /// Whether its Save works.
    pub can_save_research: bool,
    /// Its save button, [`SAVE_RESEARCH`].
    pub save_research_label: String,
}

/// One tool's switch.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct ToolRowView {
    /// The tool's id, as stored, which a toggle names.
    pub id: String,
    /// Its name, or its id when the core has none for it.
    pub label: String,
    /// Whether the core names it; a stored id it does not is kept as it came.
    pub known: bool,
    /// Whether it is on, the member's switch included.
    pub enabled: bool,
    /// A line under the switch, in the core's words, when there is one: an
    /// unnamed tool, or the fallback transfer without a support number.
    pub note: Option<String>,
}

/// Something the member did on the Skills section.
#[derive(Clone, Debug, PartialEq, Eq, uniffi::Enum)]
pub enum ToolsAction {
    /// Open the Skills section.
    Open,
    /// Turn one tool on or off.
    Toggle {
        /// The tool's id.
        id: String,
        /// On or off.
        enabled: bool,
    },
    /// Turn outside research on or off.
    SetResearch {
        /// On or off.
        enabled: bool,
    },
    /// Save the tools.
    SaveTools,
    /// Save the research switch.
    SaveResearch,
    /// Put both save notices away.
    DismissNotices,
}

/// The core events `action` is, in the order the core is to hear them.
pub(crate) fn events(action: ToolsAction) -> Vec<Event> {
    vec![match action {
        ToolsAction::Open => Event::Navigate(Route::Workspace(WorkspaceSection::Tools)),
        ToolsAction::Toggle { id, enabled } => Event::Tools(ToolsEvent::Toggle { id, enabled }),
        ToolsAction::SetResearch { enabled } => Event::Tools(ToolsEvent::SetEnrichment(enabled)),
        ToolsAction::SaveTools => Event::Tools(ToolsEvent::SaveTools),
        ToolsAction::SaveResearch => Event::Tools(ToolsEvent::SaveEnrichment),
        ToolsAction::DismissNotices => Event::Tools(ToolsEvent::DismissNotices),
    }]
}

/// The page of the Skills section, for a signed-in model.
pub(crate) fn screen(_model: &Model, signed_in: &SignedIn) -> ScreenView {
    ScreenView::Tools {
        view: tools_view(signed_in.tools.as_ref()),
    }
}

/// The section `section` holds; being read when there is none yet.
fn tools_view(section: Option<&ToolsSection>) -> ToolsView {
    let status = section.map_or(SectionStatus::Loading, |s| {
        // Whichever save landed without its read back says so, as on Linux.
        let save = [&s.enrichment_save, &s.tools_save]
            .into_iter()
            .find(|save| matches!(save, SaveState::SavedButStale(_)))
            .unwrap_or(&s.tools_save);
        section_status(&s.config, save)
    });
    ToolsView {
        title: TITLE.to_owned(),
        status,
        tools_heading: TOOLS_HEADING.to_owned(),
        tools_note: TOOLS_NOTE.to_owned(),
        tools: section.map_or_else(Vec::new, |s| s.rows().iter().map(tool_row).collect()),
        editable: section.is_some_and(ToolsSection::editable),
        tools_notice: section.and_then(|s| save_notice(&s.tools_save)),
        tools_saving: section.is_some_and(|s| s.tools_save.is_busy()),
        can_save_tools: section.is_some_and(ToolsSection::can_save_tools),
        save_tools_label: SAVE_TOOLS.to_owned(),
        research_heading: RESEARCH_HEADING.to_owned(),
        research_title: ToolsSection::ENRICHMENT_TITLE.to_owned(),
        research_body: ToolsSection::ENRICHMENT_BODY.to_owned(),
        research_enabled: section.is_some_and(ToolsSection::enrichment_enabled),
        research_notice: section.and_then(|s| save_notice(&s.enrichment_save)),
        research_saving: section.is_some_and(|s| s.enrichment_save.is_busy()),
        can_save_research: section.is_some_and(ToolsSection::can_save_enrichment),
        save_research_label: SAVE_RESEARCH.to_owned(),
    }
}

/// The switch of `row`.
fn tool_row(row: &CapabilityRow) -> ToolRowView {
    ToolRowView {
        id: row.id.clone(),
        label: row.label.unwrap_or(row.id.as_str()).to_owned(),
        known: row.label.is_some(),
        enabled: row.enabled,
        note: row.note.map(str::to_owned),
    }
}

#[cfg(test)]
impl ToolsView {
    /// A view for the boundary's own tests (a screen's trip to C# and back).
    pub(crate) fn sample() -> Self {
        tools_view(None)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn a_section_not_read_yet_is_loading() {
        let view = tools_view(None);
        assert_eq!(view.status, SectionStatus::Loading);
        assert!(view.tools.is_empty());
        assert!(!view.editable && !view.can_save_tools && !view.can_save_research);
        assert_eq!(view.tools_notice, None);
    }

    #[test]
    fn an_unnamed_tool_shows_its_id() {
        let row = tool_row(&CapabilityRow {
            id: "transfer_to_creator".to_owned(),
            label: None,
            enabled: true,
            note: Some(ToolsSection::UNKNOWN_TOOL),
        });
        assert_eq!(row.label, "transfer_to_creator");
        assert!(!row.known && row.enabled);
        assert_eq!(row.note.as_deref(), Some(ToolsSection::UNKNOWN_TOOL));
    }
}
