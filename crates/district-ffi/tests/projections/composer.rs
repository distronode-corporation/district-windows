//! The conversation's reply box (src/composer.rs).

use district_core::{Event, ThreadEvent};
use district_ffi::composer::ComposerAction;
use district_ffi::{ScreenView, UiEvent, screen_view};

use super::Case;
use super::inbox::{inbox_loaded, open_thread};

/// This area's snapshot cases: none until it is built.
pub(crate) fn cases() -> Vec<Case> {
    Vec::new()
}

/// Until its packet builds it, a conversation has no reply box. (The reply
/// box has no navigation entry, so the pane is not its to pin: nav.rs's.)
#[test]
fn unbuilt_a_thread_has_no_reply_box() {
    let session = open_thread(inbox_loaded());
    let ScreenView::Thread { view } = screen_view(&session.model) else {
        panic!("a thread opens");
    };
    assert_eq!(view.composer, None);
}

#[test]
fn each_action_is_its_core_event() {
    assert_eq!(
        UiEvent::Composer {
            action: ComposerAction::Edit {
                text: "On my way.".to_owned(),
            },
        }
        .events(),
        [Event::Thread(ThreadEvent::Compose("On my way.".to_owned()))]
    );
    assert_eq!(
        UiEvent::Composer {
            action: ComposerAction::Send,
        }
        .events(),
        [Event::Thread(ThreadEvent::Send)]
    );
}
