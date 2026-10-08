//! Workflows (src/workflows.rs).

use district_core::{Event, Route, WorkflowsEvent};
use district_ffi::UiEvent;
use district_ffi::workflows::WorkflowsAction;

use super::{Case, assert_unbuilt, signed_in};

/// This area's snapshot cases: none until it is built.
pub(crate) fn cases() -> Vec<Case> {
    Vec::new()
}

/// Until its packet builds it, the area's screens are unavailable and the
/// navigation pane does not offer it.
#[test]
fn unbuilt_it_is_unavailable_and_not_offered() {
    assert_unbuilt(
        &signed_in().ui(UiEvent::Workflows {
            action: WorkflowsAction::Open,
        }),
        Route::Workflows,
    );
}

#[test]
fn each_action_is_its_core_event() {
    assert_eq!(
        UiEvent::Workflows {
            action: WorkflowsAction::Open,
        }
        .events(),
        [Event::Navigate(Route::Workflows)]
    );
    assert_eq!(
        UiEvent::Workflows {
            action: WorkflowsAction::DismissToggleFailure,
        }
        .events(),
        [Event::Workflows(WorkflowsEvent::DismissToggleFailure)]
    );
}
