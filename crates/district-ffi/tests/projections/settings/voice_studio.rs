//! The VoiceStudio section (src/settings/voice_studio.rs).

use district_core::{Event, Route, WorkspaceSection};
use district_ffi::UiEvent;
use district_ffi::settings::voice_studio::VoiceStudioAction;

use super::super::{Case, assert_unbuilt, signed_in};

/// This area's snapshot cases.
pub(crate) fn cases() -> Vec<Case> {
    vec![(
        "signed-in-voice-studio",
        signed_in().ui(UiEvent::VoiceStudio {
            action: VoiceStudioAction::Open,
        }),
    )]
}

/// Until its packet builds it, the area's screens are unavailable and the
/// navigation pane does not offer it.
#[test]
fn unbuilt_it_is_unavailable_and_not_offered() {
    assert_unbuilt(
        &signed_in().ui(UiEvent::VoiceStudio {
            action: VoiceStudioAction::Open,
        }),
        Route::Workspace(WorkspaceSection::VoiceStudio),
    );
}

#[test]
fn each_action_is_its_core_event() {
    assert_eq!(
        UiEvent::VoiceStudio {
            action: VoiceStudioAction::Open,
        }
        .events(),
        [Event::Navigate(Route::Workspace(
            WorkspaceSection::VoiceStudio
        ))]
    );
}
