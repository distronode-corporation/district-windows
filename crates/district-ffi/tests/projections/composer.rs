//! The conversation's reply box (src/composer.rs).

use district_core::{Event, ThreadEvent};
use district_ffi::composer::ComposerAction;
use district_ffi::{ScreenView, UiEvent, screen_view};

use super::inbox::{inbox_loaded, open_thread};
use super::{Case, offered};

/// This area's snapshot cases: none until it is built.
pub(crate) fn cases() -> Vec<Case> {
    Vec::new()
}

/// Until its packet builds it, a conversation has no reply box, and the
/// navigation pane is 1.0's.
#[test]
fn unbuilt_a_thread_has_no_reply_box() {
    let session = open_thread(inbox_loaded());
    let ScreenView::Thread { view } = screen_view(&session.model) else {
        panic!("a thread opens");
    };
    assert_eq!(view.composer, None);
    assert_eq!(offered(&session).len(), 5 + super::BUILT.len());
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
