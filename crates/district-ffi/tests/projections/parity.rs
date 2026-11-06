//! App parity: reply channels, Messenger and Instagram, the conversation's
//! owner, the WhatsApp templates a closed window offers and the Messenger and
//! Instagram reply window (src/composer.rs, src/inbox.rs).

use district_core::ThreadEvent;
use district_ffi::composer::{AssignmentView, ComposerAction, ComposerView, MetaView};

use super::inbox::{conversations, inbox, timeline, with_unread};
use super::*;

/// The contact's thread: email, text and WhatsApp, in the service's order.
const THREAD: &str = "contact:contact_contract_1";
/// The Messenger customer's thread, and the Instagram customer's.
const MESSENGER: &str = "addr:fb:5550101";
const INSTAGRAM: &str = "addr:ig:5550102";

/// This area's snapshot cases.
pub(crate) fn cases() -> Vec<Case> {
    vec![
        ("parity-inbox-meta-owners", meta_inbox()),
        ("parity-thread-channels-owner", owner_picked()),
        ("parity-thread-template-filled", template_filled()),
        (
            "parity-thread-meta-human-agent",
            meta_window(MESSENGER, "district-meta-window-human-agent.json"),
        ),
        (
            "parity-thread-meta-closed",
            meta_window(INSTAGRAM, "district-meta-window-closed.json"),
        ),
    ]
}

/// The inbox as the service answers the app: the reply options fixture, each
/// row with its owner.
fn meta_list() -> Value {
    let mut list = contracts::json("district-conversations-reply-options.json");
    for row in list["conversations"].as_array_mut().unwrap() {
        let owned = row["threadKey"] == json!(THREAD);
        row["assigneeEmail"] = if owned {
            json!("contract@example.com")
        } else {
            Value::Null
        };
        row["assignedToMe"] = json!(false);
    }
    list
}

fn meta_inbox() -> Session {
    with_unread(conversations(inbox(signed_in()), meta_list()))
}

fn opened() -> Session {
    timeline(
        meta_inbox().ui(UiEvent::OpenThread {
            thread_key: THREAD.to_owned(),
        }),
        "district-timeline.json",
    )
}

fn act(session: Session, action: ComposerAction) -> Session {
    session.ui(UiEvent::Composer { action })
}

/// The thread with the members read for the owner picker.
fn with_members() -> Session {
    opened().answer(
        |e| matches!(e, Effect::LoadMembers { .. }),
        |ticket| Event::MembersLoaded {
            ticket,
            result: Ok(contracts::read("district-members.json")),
        },
    )
}

fn owner_picked() -> Session {
    with_members()
}

/// WhatsApp chosen, its window closed, the template chosen and filled.
fn template_filled() -> Session {
    let session = act(
        with_members(),
        ComposerAction::ChooseChannel {
            channel: "whatsapp".to_owned(),
        },
    )
    .answer(
        |e| matches!(e, Effect::LoadWhatsAppWindow { .. }),
        |ticket| Event::WhatsAppWindowLoaded {
            ticket,
            result: Ok(contracts::read("district-whatsapp-window-closed.json")),
        },
    )
    .answer(
        |e| matches!(e, Effect::LoadWhatsAppTemplates { .. }),
        |ticket| Event::WhatsAppTemplatesLoaded {
            ticket,
            result: Ok(contracts::read("district-whatsapp-templates.json")),
        },
    );
    let session = act(
        session,
        ComposerAction::ChooseTemplate {
            id: "wa_tpl_reminder".to_owned(),
        },
    );
    let session = act(
        session,
        ComposerAction::TemplateVariable {
            index: 0,
            value: "Ada".to_owned(),
        },
    );
    act(
        session,
        ComposerAction::TemplateVariable {
            index: 1,
            value: "Thursday".to_owned(),
        },
    )
}

/// The Meta thread `key` open, its history read and a reply typed, its
/// window not read yet.
fn meta_opened(session: Session, key: &str) -> Session {
    let session = timeline(
        session.ui(UiEvent::OpenThread {
            thread_key: key.to_owned(),
        }),
        "district-timeline.json",
    );
    act(
        session,
        ComposerAction::Compose {
            text: "Thanks for writing".to_owned(),
        },
    )
}

/// The newest window read pending answered with the fixture `name`.
fn window_read(session: Session, name: &'static str) -> Session {
    session.answer(
        |e| matches!(e, Effect::LoadMetaWindow { .. }),
        |ticket| Event::MetaWindowLoaded {
            ticket,
            result: Ok(contracts::read(name)),
        },
    )
}

/// The Meta thread `key` with its window read as the fixture `name`.
fn meta_window(key: &str, name: &'static str) -> Session {
    window_read(meta_opened(meta_inbox(), key), name)
}

/// The oldest window read pending, taken out unanswered, and its ticket.
fn take_oldest_window_read(session: &mut Session) -> Ticket {
    let index = session
        .pending
        .iter()
        .position(|e| matches!(e, Effect::LoadMetaWindow { .. }))
        .expect("a window read pending");
    session.pending.remove(index).ticket().expect("a ticket")
}

/// The reply box and its Meta side.
fn meta(session: &Session) -> (ComposerView, MetaView) {
    let (composer, _) = thread(session);
    let meta = composer.meta.clone().expect("the Meta window");
    (composer, meta)
}

fn thread(session: &Session) -> (ComposerView, AssignmentView) {
    let ScreenView::Thread { view } = screen_view(&session.model) else {
        panic!("a thread is open");
    };
    (
        view.composer.expect("the reply box"),
        view.assignment.expect("the owner"),
    )
}

#[test]
fn each_parity_action_is_its_core_event() {
    let one = |action| UiEvent::Composer { action }.events();
    assert_eq!(
        one(ComposerAction::ChooseChannel {
            channel: "messenger".to_owned()
        }),
        [Event::Thread(ThreadEvent::ChooseChannel(
            "messenger".to_owned()
        ))]
    );
    assert_eq!(
        one(ComposerAction::Assign {
            assignee: Some("a@example.com".to_owned())
        }),
        [Event::Thread(ThreadEvent::Assign(Some(
            "a@example.com".to_owned()
        )))]
    );
    assert_eq!(
        one(ComposerAction::Assign { assignee: None }),
        [Event::Thread(ThreadEvent::Assign(None))]
    );
    assert_eq!(
        one(ComposerAction::DismissAssignFailure),
        [Event::Thread(ThreadEvent::DismissAssignFailure)]
    );
    assert_eq!(
        one(ComposerAction::ChooseTemplate { id: "t".to_owned() }),
        [Event::Thread(ThreadEvent::ChooseTemplate("t".to_owned()))]
    );
    assert_eq!(
        one(ComposerAction::TemplateVariable {
            index: 2,
            value: "v".to_owned()
        }),
        [Event::Thread(ThreadEvent::TemplateVariable(
            2,
            "v".to_owned()
        ))]
    );
    assert_eq!(
        one(ComposerAction::SendTemplate),
        [Event::Thread(ThreadEvent::SendTemplate)]
    );
}

#[test]
fn every_read_asks_for_meta_channels_and_owners() {
    let session = inbox(signed_in());
    assert!(session.pending.iter().any(|e| matches!(
        e,
        Effect::LoadConversations { query, .. } if query.meta_channels && query.assignments
    )));
}

#[test]
fn meta_rows_are_labelled_and_owned_rows_say_who() {
    let ScreenView::Inbox { view } = screen_view(&meta_inbox().model) else {
        panic!("the inbox");
    };
    let label = |key: &str| {
        view.threads
            .iter()
            .find(|row| row.thread_key == key)
            .map(|row| (row.channel_label.clone(), row.assignee.clone()))
            .unwrap()
    };
    assert_eq!(label("addr:fb:5550101"), ("Messenger".to_owned(), None));
    assert_eq!(label("addr:ig:5550102"), ("Instagram".to_owned(), None));
    assert_eq!(
        label(THREAD).1.as_deref(),
        Some("Assigned to contract@example.com")
    );
}

#[test]
fn the_box_offers_the_services_channels_and_the_picker_its_members() {
    let (composer, owner) = thread(&with_members());
    let channels: Vec<(&str, &str, bool)> = composer
        .channels
        .iter()
        .map(|c| (c.channel.as_str(), c.label.as_str(), c.selected))
        .collect();
    assert_eq!(
        channels,
        [
            ("email", "Email", true),
            ("sms", "Text message", false),
            ("whatsapp", "WhatsApp", false)
        ]
    );
    assert!(composer.can_choose_channel && !composer.show_attach);
    let options: Vec<Option<&str>> = owner.options.iter().map(|o| o.email.as_deref()).collect();
    assert_eq!(
        options,
        [
            None,
            Some("founder@example.com"),
            Some("operator@example.com"),
            Some("contract@example.com")
        ]
    );
    assert_eq!(owner.options[0].label, "Unassigned");
    assert_eq!(owner.selected, 3);
    assert!(owner.show_picker && owner.can_assign);
    assert_eq!(owner.line, "Assigned to contract@example.com");
}

#[test]
fn an_assignment_goes_once_and_the_picker_waits_for_it() {
    let session = act(
        with_members(),
        ComposerAction::Assign {
            assignee: Some("founder@example.com".to_owned()),
        },
    );
    let (_, owner) = thread(&session);
    assert!(owner.saving && !owner.can_assign);
    assert_eq!(
        session
            .pending
            .iter()
            .filter(|e| matches!(e, Effect::AssignConversation { .. }))
            .count(),
        1
    );
}

#[test]
fn a_closed_window_offers_the_approved_template_filled() {
    let (composer, _) = thread(&template_filled());
    let whatsapp = composer.whatsapp.expect("WhatsApp");
    assert_eq!(
        whatsapp.window_closed.as_deref(),
        Some("Window closed: WhatsApp only allows a template now")
    );
    assert!(!composer.can_send && !composer.can_draft_reply);
    let picker = whatsapp.templates.expect("the picker");
    assert_eq!(picker.message, None);
    assert_eq!(picker.options.len(), 1);
    assert_eq!(picker.options[0].label, "appointment_reminder (en_US)");
    assert_eq!(picker.chosen.as_deref(), Some("wa_tpl_reminder"));
    assert_eq!(picker.variables.len(), 2);
    assert_eq!(picker.variables[0].label, "Variable {{1}}");
    assert_eq!(picker.variables[1].required, None);
    assert_eq!(
        picker.preview.as_deref(),
        Some("Hi Ada, this is a reminder of your appointment on Thursday.")
    );
    assert!(picker.can_send && !picker.sending);
}

#[test]
fn a_template_variable_left_empty_is_required() {
    let session = act(
        template_filled(),
        ComposerAction::TemplateVariable {
            index: 1,
            value: " ".to_owned(),
        },
    );
    let (composer, _) = thread(&session);
    let picker = composer.whatsapp.unwrap().templates.unwrap();
    assert_eq!(
        picker.variables[1].required.as_deref(),
        Some("Fill in this variable")
    );
    assert!(!picker.can_send);
}

#[test]
fn templates_loading_failed_or_none_say_so() {
    let window_closed = act(
        with_members(),
        ComposerAction::ChooseChannel {
            channel: "whatsapp".to_owned(),
        },
    )
    .answer(
        |e| matches!(e, Effect::LoadWhatsAppWindow { .. }),
        |ticket| Event::WhatsAppWindowLoaded {
            ticket,
            result: Ok(contracts::read("district-whatsapp-window-closed.json")),
        },
    );
    let message = |session: &Session| {
        thread(session)
            .0
            .whatsapp
            .unwrap()
            .templates
            .unwrap()
            .message
    };
    assert_eq!(
        message(&window_closed).as_deref(),
        Some("Loading templates")
    );
    let failed = window_closed.answer(
        |e| matches!(e, Effect::LoadWhatsAppTemplates { .. }),
        |ticket| Event::WhatsAppTemplatesLoaded {
            ticket,
            result: Err(server_error()),
        },
    );
    assert_eq!(
        message(&failed).as_deref(),
        Some("The templates could not be loaded. Try again in a moment.")
    );
}

#[test]
fn no_approved_template_says_so() {
    let mut templates = contracts::json("district-whatsapp-templates.json");
    templates["templates"][0]["status"] = json!("pending");
    let session = act(
        with_members(),
        ComposerAction::ChooseChannel {
            channel: "whatsapp".to_owned(),
        },
    )
    .answer(
        |e| matches!(e, Effect::LoadWhatsAppWindow { .. }),
        |ticket| Event::WhatsAppWindowLoaded {
            ticket,
            result: Ok(contracts::read("district-whatsapp-window-closed.json")),
        },
    )
    .answer(
        |e| matches!(e, Effect::LoadWhatsAppTemplates { .. }),
        |ticket| Event::WhatsAppTemplatesLoaded {
            ticket,
            result: Ok(contracts::decode("templates", templates)),
        },
    );
    let picker = thread(&session).0.whatsapp.unwrap().templates.unwrap();
    assert!(picker.options.is_empty());
    assert_eq!(
        picker.message.as_deref(),
        Some(
            "No approved template for this number yet. Refresh the templates in Messaging settings."
        )
    );
}

#[test]
fn an_open_window_says_nothing() {
    let session = act(
        with_members(),
        ComposerAction::ChooseChannel {
            channel: "whatsapp".to_owned(),
        },
    )
    .answer(
        |e| matches!(e, Effect::LoadWhatsAppWindow { .. }),
        |ticket| Event::WhatsAppWindowLoaded {
            ticket,
            result: Ok(contracts::read("district-whatsapp-window.json")),
        },
    );
    let whatsapp = thread(&session).0.whatsapp.unwrap();
    assert_eq!(whatsapp.window_closed, None);
    assert_eq!(whatsapp.templates, None);
}

#[test]
fn a_viewer_sees_the_owner_without_the_picker() {
    let mut list = meta_list();
    list["conversations"][3]["assigneeEmail"] = json!("contract@example.com");
    let session = timeline(
        with_unread(conversations(inbox(viewer()), list)).ui(UiEvent::OpenThread {
            thread_key: THREAD.to_owned(),
        }),
        "district-timeline.json",
    );
    let ScreenView::Thread { view } = screen_view(&session.model) else {
        panic!("a thread");
    };
    assert_eq!(view.composer, None);
    let owner = view.assignment.unwrap();
    assert!(!owner.show_picker && !owner.can_assign);
    assert_eq!(owner.line, "Assigned to contract@example.com");
}

#[test]
fn an_unowned_thread_reads_unassigned_and_attachments_wait_for_sms() {
    let mut list = meta_list();
    list["conversations"][3]["assigneeEmail"] = Value::Null;
    let session = timeline(
        with_unread(conversations(inbox(signed_in()), list)).ui(UiEvent::OpenThread {
            thread_key: THREAD.to_owned(),
        }),
        "district-timeline.json",
    );
    let (composer, owner) = thread(&session);
    assert_eq!(owner.line, "Unassigned");
    assert_eq!(owner.selected, 0);
    assert_eq!(composer.attachments_note, None);
}

#[test]
fn the_human_agent_window_shows_its_badge_and_only_a_person_replies() {
    let (composer, meta) = meta(&meta_window(
        MESSENGER,
        "district-meta-window-human-agent.json",
    ));
    assert_eq!(
        meta.human_agent.as_deref(),
        Some("Human agent reply (7-day window)")
    );
    assert_eq!(meta.window_closed, None);
    assert!(composer.can_send && !composer.can_draft_reply);
    assert_eq!(composer.whatsapp, None);
}

#[test]
fn a_closed_meta_window_warns_and_sends_nothing_but_keeps_the_box() {
    let (composer, meta) = meta(&meta_window(INSTAGRAM, "district-meta-window-closed.json"));
    assert_eq!(meta.human_agent, None);
    assert_eq!(
        meta.window_closed.as_deref(),
        Some(
            "Reply window closed: Messenger and Instagram allow a reply only within 24 hours of \
             the customer's last message (7 days for a human agent reply, when it is turned on). \
             You can reply once they write again."
        )
    );
    assert!(!composer.can_send && !composer.can_draft_reply);
    assert_eq!(composer.text, "Thanks for writing");
}

#[test]
fn an_open_loading_failed_or_unknown_window_blocks_nothing_and_says_nothing() {
    let quiet = MetaView {
        human_agent: None,
        window_closed: None,
    };
    let loading = meta_opened(meta_inbox(), MESSENGER);
    let open = window_read(
        meta_opened(meta_inbox(), MESSENGER),
        "district-meta-window.json",
    );
    let failed = meta_opened(meta_inbox(), INSTAGRAM).answer(
        |e| matches!(e, Effect::LoadMetaWindow { .. }),
        |ticket| Event::MetaWindowLoaded {
            ticket,
            result: Err(server_error()),
        },
    );
    let unknown = meta_opened(meta_inbox(), INSTAGRAM).answer(
        |e| matches!(e, Effect::LoadMetaWindow { .. }),
        |ticket| Event::MetaWindowLoaded {
            ticket,
            result: Ok(contracts::decode(
                "a state this build does not know",
                json!({ "success": true, "window": { "state": "paused", "until": null } }),
            )),
        },
    );
    for (name, session) in [
        ("loading", loading),
        ("open", open),
        ("failed", failed),
        ("unknown", unknown),
    ] {
        let (composer, meta) = meta(&session);
        assert_eq!(meta, quiet, "{name}");
        assert!(composer.can_send && composer.can_draft_reply, "{name}");
    }
}

#[test]
fn a_window_read_for_another_thread_does_not_paint_this_one() {
    // Messenger's read is still on its way when the Instagram thread opens.
    let mut session = meta_opened(meta_opened(meta_inbox(), MESSENGER), INSTAGRAM);
    let channels: Vec<&str> = session
        .pending
        .iter()
        .filter_map(|e| match e {
            Effect::LoadMetaWindow { channel, .. } => Some(channel.as_str()),
            _ => None,
        })
        .collect();
    assert_eq!(channels, ["messenger", "instagram"]);
    let stale = take_oldest_window_read(&mut session);
    let session = session.send(Event::MetaWindowLoaded {
        ticket: stale,
        result: Ok(contracts::read("district-meta-window-closed.json")),
    });
    let (composer, meta) = meta(&session);
    assert_eq!(meta.window_closed, None);
    assert!(composer.can_send && composer.can_draft_reply);
    // Its own read still lands.
    let (composer, meta) = self::meta(&window_read(
        session,
        "district-meta-window-human-agent.json",
    ));
    assert!(meta.human_agent.is_some() && !composer.can_draft_reply);
}

#[test]
fn a_window_read_for_another_workspace_does_not_paint_this_one() {
    let workspace = |id: &str, name: &str| {
        json!({
            "id": id,
            "name": name,
            "region": "us",
            "role": "agency",
            "subscriptionTier": "VoicePro"
        })
    };
    let list = contracts::decode(
        "two workspaces",
        json!({
            "success": true,
            "workspaces": [workspace("ws-1", "Example Dental"), workspace("ws-2", "Example Clinic")],
            "total": 2,
            "limit": 100,
            "offset": 0,
            "degradedRegions": [],
            "inactiveCount": 0,
            "defaultWorkspaceId": "ws-1"
        }),
    );
    let first = meta_opened(
        with_unread(conversations(inbox(signed_in_to(Ok(list))), meta_list())),
        MESSENGER,
    );
    // The same customer's thread, in the other workspace, opens while the
    // first workspace's read is still on its way.
    let second = first.send(Event::SelectWorkspace("ws-2".to_owned()));
    let mut session = meta_opened(conversations(inbox(second), meta_list()), MESSENGER);
    let workspaces: Vec<String> = session
        .pending
        .iter()
        .filter_map(|e| match e {
            Effect::LoadMetaWindow { workspace_id, .. } => Some(workspace_id.clone()),
            _ => None,
        })
        .collect();
    assert_eq!(workspaces, ["ws-1", "ws-2"]);
    let stale = take_oldest_window_read(&mut session);
    let session = session.send(Event::MetaWindowLoaded {
        ticket: stale,
        result: Ok(contracts::read("district-meta-window-closed.json")),
    });
    let (composer, meta) = meta(&session);
    assert_eq!(meta.window_closed, None);
    assert!(composer.can_send);
}

#[test]
fn a_reply_refused_for_a_closed_window_closes_it_here_too() {
    let sent = act(
        meta_window(MESSENGER, "district-meta-window-human-agent.json"),
        ComposerAction::Send,
    );
    let refused = sent.answer(
        |e| matches!(e, Effect::SendMessage { .. }),
        |ticket| Event::MessageSent {
            ticket,
            result: Err(ApiError::Conflict(ErrorDetail {
                code: Some("messenger_window_closed".to_owned()),
                ..ErrorDetail::default()
            })),
        },
    );
    let (composer, meta) = meta(&refused);
    assert_eq!(meta.human_agent, None);
    assert!(meta.window_closed.is_some());
    assert!(!composer.can_send && !composer.can_draft_reply);
}
