//! The inbox and one conversation (src/inbox.rs).

use super::*;

/// This area's snapshot cases.
pub(crate) fn cases() -> Vec<Case> {
    [inbox_cases(), thread_cases()]
        .into_iter()
        .flatten()
        .collect()
}

pub(crate) fn inbox(session: Session) -> Session {
    session.ui(UiEvent::Navigate {
        destination: NavDestination::Inbox,
    })
}

pub(crate) fn conversations(session: Session, value: Value) -> Session {
    session.answer(
        |e| matches!(e, Effect::LoadConversations { .. }),
        |ticket| Event::ConversationsLoaded {
            ticket,
            result: Ok(contracts::decode("conversations", value)),
        },
    )
}

pub(crate) fn search_hits() -> Value {
    json!({
        "success": true,
        "limit": 1,
        "results": [{
            "messageId": "msg_1",
            "key": "contact:contact_contract_1",
            "threadKey": "contact:contact_contract_1",
            "counterpart": "+14165550142",
            "kind": "contact",
            "contactId": "contact_contract_1",
            "contactName": "Contract Test Caller",
            "contactEmail": null,
            "body": "Here is the photo\n of the roof.",
            "subject": null,
            "direction": "inbound",
            "messageType": "sms",
            "createdAt": "2026-08-15T14:10:00.000Z"
        }]
    })
}

pub(crate) fn searched(session: Session, query: &str, answer: Result<Value, ApiError>) -> Session {
    session
        .ui(UiEvent::Search {
            query: query.to_owned(),
        })
        .answer(
            |e| matches!(e, Effect::Wait { .. }),
            |ticket| Event::WaitOver { ticket },
        )
        .answer(
            |e| matches!(e, Effect::SearchMessages { .. }),
            |ticket| Event::SearchLoaded {
                ticket,
                result: answer.map(|value| contracts::decode("search", value)),
            },
        )
}

pub(crate) fn inbox_loaded() -> Session {
    conversations(
        inbox(signed_in()),
        contracts::json("district-conversations.json"),
    )
}

pub(crate) fn inbox_cases() -> Vec<Case> {
    let mut partial = contracts::json("district-conversations.json");
    partial["scanned"] = json!(500);
    let none = with(search_hits(), "results", json!([]));
    vec![
        ("inbox-loading", inbox(signed_in())),
        ("inbox-loaded", inbox_loaded()),
        (
            "inbox-empty-partial",
            conversations(
                inbox(signed_in()),
                with(partial, "conversations", json!([])),
            ),
        ),
        (
            "inbox-failed",
            inbox(signed_in()).answer(
                |e| matches!(e, Effect::LoadConversations { .. }),
                |ticket| Event::ConversationsLoaded {
                    ticket,
                    result: Err(server_error()),
                },
            ),
        ),
        ("inbox-refreshing", inbox_loaded().ui(UiEvent::Refresh)),
        (
            "inbox-refresh-failed",
            inbox_loaded().ui(UiEvent::Refresh).answer(
                |e| matches!(e, Effect::LoadConversations { .. }),
                |ticket| Event::ConversationsLoaded {
                    ticket,
                    result: Err(server_error()),
                },
            ),
        ),
        (
            "inbox-search-hits",
            searched(inbox_loaded(), "roof", Ok(search_hits())),
        ),
        (
            "inbox-search-none",
            searched(inbox_loaded(), "gutter", Ok(none)),
        ),
        (
            "inbox-search-failed",
            searched(inbox_loaded(), "roof", Err(server_error())),
        ),
        (
            "inbox-search-running",
            inbox_loaded().ui(UiEvent::Search {
                query: "roof".to_owned(),
            }),
        ),
        (
            "inbox-search-cleared",
            searched(inbox_loaded(), "roof", Ok(search_hits())).ui(UiEvent::ClearSearch),
        ),
    ]
}

pub(crate) fn with_unread(session: Session) -> Session {
    session.answer(
        |e| matches!(e, Effect::LoadUnreadCount { .. }),
        |ticket| Event::UnreadCountLoaded {
            ticket,
            result: Ok(unread_count()),
        },
    )
}

pub(crate) fn open_thread(session: Session) -> Session {
    with_unread(conversations(
        inbox(session),
        contracts::json("district-conversations.json"),
    ))
    .ui(UiEvent::OpenThread {
        thread_key: "contact:contact_contract_1".to_owned(),
    })
}

pub(crate) fn timeline(session: Session, name: &str) -> Session {
    session.answer(
        |e| matches!(e, Effect::LoadTimeline { .. }),
        |ticket| Event::TimelineLoaded {
            ticket,
            result: Ok(contracts::read(name)),
        },
    )
}

pub(crate) fn timeline_failed(session: Session) -> Session {
    session.answer(
        |e| matches!(e, Effect::LoadTimeline { .. }),
        |ticket| Event::TimelineLoaded {
            ticket,
            result: Err(server_error()),
        },
    )
}

pub(crate) fn thread_cases() -> Vec<Case> {
    let paged = || timeline(open_thread(signed_in()), "district-timeline-page.json");
    vec![
        ("thread-loading-marked-read", open_thread(signed_in())),
        (
            "thread-loaded",
            timeline(open_thread(signed_in()), "district-timeline.json"),
        ),
        (
            "thread-viewer",
            timeline(open_thread(viewer()), "district-timeline.json"),
        ),
        ("thread-failed", timeline_failed(open_thread(signed_in()))),
        ("thread-loading-older", paged().ui(UiEvent::LoadOlder)),
        (
            "thread-older-failed",
            timeline_failed(paged().ui(UiEvent::LoadOlder)),
        ),
        (
            "thread-refresh-failed",
            timeline_failed(paged().ui(UiEvent::Refresh)),
        ),
        (
            "thread-unreadable-key-refused",
            open_thread(signed_in()).ui(UiEvent::OpenThread {
                thread_key: "fax:123".to_owned(),
            }),
        ),
    ]
}

/// Opening a thread marks it read, and that is the core's own doing: the
/// unread count drops by the thread's two at once, with no event from C#.
#[test]
fn opening_a_thread_marks_it_read_in_the_core() {
    let before = with_unread(inbox_loaded());
    assert_eq!(shell_view(&before.model, false).unread, 3);
    let after = before.ui(UiEvent::OpenThread {
        thread_key: "contact:contact_contract_1".to_owned(),
    });
    assert_eq!(shell_view(&after.model, false).unread, 1);
    assert!(
        after
            .pending
            .iter()
            .any(|effect| matches!(effect, Effect::MarkRead { .. }))
    );
    assert!(matches!(
        screen_view(&after.model),
        ScreenView::Thread { .. }
    ));
}
