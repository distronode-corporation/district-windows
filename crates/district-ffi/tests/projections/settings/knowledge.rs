//! The Knowledge section (src/settings/knowledge.rs).

use district_core::{Event, Route, WorkspaceSection};
use district_ffi::UiEvent;
use district_ffi::settings::knowledge::KnowledgeAction;

use super::super::{Case, assert_unbuilt, signed_in};

/// This area's snapshot cases: none until it is built.
pub(crate) fn cases() -> Vec<Case> {
    Vec::new()
}

/// Until its packet builds it, the area's screens are unavailable and the
/// navigation pane does not offer it.
#[test]
fn unbuilt_it_is_unavailable_and_not_offered() {
    assert_unbuilt(
        &signed_in().ui(UiEvent::Knowledge {
            action: KnowledgeAction::Open,
        }),
        Route::Workspace(WorkspaceSection::Knowledge),
    );
}

#[test]
fn each_action_is_its_core_event() {
    assert_eq!(
        UiEvent::Knowledge {
            action: KnowledgeAction::Open,
        }
        .events(),
        [Event::Navigate(Route::Workspace(
            WorkspaceSection::Knowledge
        ))]
    );
}
