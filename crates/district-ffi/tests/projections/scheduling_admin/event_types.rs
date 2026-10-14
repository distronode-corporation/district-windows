//! The event types (src/scheduling_admin/event_types.rs): the list and its
//! create form, and one event type with its editor, state actions, hosts and
//! booking questions.

use district_core::SchedulingAdminEvent;
use district_core::scheduling_admin::{EventTypeEvent, EventTypesEvent};
use district_ffi::scheduling_admin::event_types::{
    EventTypeAction, EventTypeFormView, EventTypesAction, QuestionFormView,
    SchedulingEventTypeView, SchedulingEventTypesView,
};

use super::*;

/// This area's snapshot cases.
pub(crate) fn cases() -> Vec<Case> {
    vec![
        (
            "scheduling-admin-event-types-loading",
            open(signed_in(), SchedulingAdminSection::EventTypes),
        ),
        ("scheduling-admin-event-types", listed(signed_in())),
        (
            "scheduling-admin-event-types-failed",
            list_read(context(signed_in()), Err(server_error())),
        ),
        (
            "scheduling-admin-event-types-empty",
            list_read(
                context(signed_in()),
                Ok(json!({"ok": true, "data": {"items": []}})),
            ),
        ),
        ("scheduling-admin-event-types-viewer", listed(viewer())),
        (
            "scheduling-admin-event-type-create-open",
            creating(signed_in()),
        ),
        (
            "scheduling-admin-event-type-create-name-missing",
            creating(signed_in()).ui(list(EventTypesAction::Save)),
        ),
        (
            "scheduling-admin-event-type-loading",
            listed(signed_in()).ui(opened("phone-consultation")),
        ),
        ("scheduling-admin-event-type", event_type(signed_in())),
        (
            "scheduling-admin-event-type-hosts-failed",
            event_type_with(signed_in(), Err(server_error())),
        ),
        ("scheduling-admin-event-type-viewer", event_type(viewer())),
        (
            "scheduling-admin-event-type-editor-open",
            editing(signed_in()),
        ),
        (
            "scheduling-admin-event-type-editor-invalid",
            editing(signed_in())
                .ui(detail(EventTypeAction::EditForm {
                    form: EventTypeFormView {
                        interval: String::new(),
                        ..edited_form(&editing(signed_in()))
                    },
                }))
                .ui(detail(EventTypeAction::Save)),
        ),
        (
            "scheduling-admin-event-type-editor-refused",
            answer_admin(
                editing(signed_in()).ui(detail(EventTypeAction::Save)),
                |r| matches!(r, SchedulingAdminRequest::PatchEventType { .. }),
                Err(server_error()),
            ),
        ),
        (
            "scheduling-admin-event-type-actions-open",
            event_type(signed_in()).ui(detail(EventTypeAction::OpenActions)),
        ),
        (
            "scheduling-admin-event-type-turned-off",
            turned_off(signed_in()),
        ),
        (
            "scheduling-admin-event-type-delete-asked",
            event_type(signed_in())
                .ui(detail(EventTypeAction::OpenActions))
                .ui(detail(EventTypeAction::AskDelete)),
        ),
        (
            "scheduling-admin-event-type-hosts-loading",
            event_type(signed_in()).ui(detail(EventTypeAction::OpenHosts)),
        ),
        (
            "scheduling-admin-event-type-hosts-open",
            hosting(signed_in()),
        ),
        (
            "scheduling-admin-event-type-questions-open",
            asking(signed_in()),
        ),
        (
            "scheduling-admin-event-type-question-form",
            question_form(signed_in()),
        ),
        (
            "scheduling-admin-event-type-question-delete-asked",
            asking(signed_in()).ui(detail(EventTypeAction::AskDeleteQuestion {
                id: "q-contract-1".to_owned(),
            })),
        ),
    ]
}

fn list(action: EventTypesAction) -> UiEvent {
    UiEvent::SchedulingAdmin {
        action: SchedulingAdminAction::EventTypes { action },
    }
}

fn detail(action: EventTypeAction) -> UiEvent {
    UiEvent::SchedulingAdmin {
        action: SchedulingAdminAction::EventType { action },
    }
}

fn opened(slug: &str) -> UiEvent {
    action(SchedulingAdminAction::OpenEventType {
        slug: slug.to_owned(),
    })
}

/// The list opened, with the status its links need read.
fn context(session: Session) -> Session {
    status(open(session, SchedulingAdminSection::EventTypes))
}

/// The list's read, answered with `result`.
fn list_read(session: Session, result: Result<Value, ApiError>) -> Session {
    answer_admin(
        session,
        |r| matches!(r, SchedulingAdminRequest::EventTypes),
        result,
    )
}

/// The list, read.
fn listed(session: Session) -> Session {
    list_read(
        context(session),
        Ok(contracts::json("district-scheduling-event-types.json")),
    )
}

fn creating(session: Session) -> Session {
    listed(session).ui(list(EventTypesAction::OpenCreate))
}

/// One event type, opened from the list, its hosts read as `hosts` says and
/// the rest answered.
fn event_type_with(session: Session, hosts: Result<Value, ApiError>) -> Session {
    let session = listed(session).ui(opened("phone-consultation"));
    let session = answer_admin(
        session,
        |r| matches!(r, SchedulingAdminRequest::EventType { .. }),
        Ok(contracts::json("district-scheduling-event-type.json")),
    );
    let session = answer_admin(
        session,
        |r| matches!(r, SchedulingAdminRequest::Hosts { .. }),
        hosts,
    );
    answer_admin(
        session,
        |r| matches!(r, SchedulingAdminRequest::Questions { .. }),
        Ok(contracts::json("district-scheduling-questions.json")),
    )
}

fn event_type(session: Session) -> Session {
    event_type_with(
        session,
        Ok(contracts::json("district-scheduling-hosts.json")),
    )
}

fn editing(session: Session) -> Session {
    event_type(session).ui(detail(EventTypeAction::OpenEditor))
}

/// The editor's form, as the core holds it.
fn edited_form(session: &Session) -> EventTypeFormView {
    event_type_view(session).editor.expect("the editor").form
}

/// The state actions, the event type turned off.
fn turned_off(session: Session) -> Session {
    let mut off = contracts::json("district-scheduling-event-type.json");
    off["data"]["is_active"] = json!(false);
    answer_admin(
        event_type(session)
            .ui(detail(EventTypeAction::OpenActions))
            .ui(detail(EventTypeAction::ToggleActive)),
        |r| matches!(r, SchedulingAdminRequest::PatchEventType { .. }),
        Ok(off),
    )
}

/// The hosts dialog, its hosts and the users read: one user who is not a
/// host yet can be added; the archived one cannot.
fn hosting(session: Session) -> Session {
    let mut users = contracts::json("district-scheduling-users.json");
    users["data"].as_array_mut().unwrap().push(json!({
        "id": "sched-user-2",
        "email": "sam@example.com",
        "name": "",
        "is_admin": false,
        "is_owner": false,
        "role": "member",
        "archived": false
    }));
    let session = answer_admin(
        event_type(session).ui(detail(EventTypeAction::OpenHosts)),
        |r| matches!(r, SchedulingAdminRequest::Hosts { .. }),
        Ok(contracts::json("district-scheduling-hosts.json")),
    );
    answer_admin(
        session,
        |r| matches!(r, SchedulingAdminRequest::Users { .. }),
        Ok(users),
    )
}

/// The questions dialog, its questions read.
fn asking(session: Session) -> Session {
    answer_admin(
        event_type(session).ui(detail(EventTypeAction::OpenQuestions)),
        |r| matches!(r, SchedulingAdminRequest::Questions { .. }),
        Ok(contracts::json("district-scheduling-questions.json")),
    )
}

/// A new select question, with one choice.
fn question_form(session: Session) -> Session {
    asking(session)
        .ui(detail(EventTypeAction::AddQuestion))
        .ui(detail(EventTypeAction::EditQuestionForm {
            form: QuestionFormView {
                id: None,
                label: "Which office?".to_owned(),
                kind: "select".to_owned(),
                options: Vec::new(),
                required: true,
                position: "2".to_owned(),
            },
        }))
        .ui(detail(EventTypeAction::AddOption {
            option: " Toronto ".to_owned(),
        }))
}

fn list_view(session: &Session) -> SchedulingEventTypesView {
    let SchedulingAdminPage::EventTypes { view } = admin_view(session).page else {
        panic!("not the event types");
    };
    view
}

fn event_type_view(session: &Session) -> SchedulingEventTypeView {
    let SchedulingAdminPage::EventType { view } = admin_view(session).page else {
        panic!("not an event type");
    };
    view
}

/// The list's rows say what each is, and only a bookable one with an address
/// has a link; the create is offered to a member who may change things.
#[test]
fn the_list_says_what_each_event_type_is() {
    let view = list_view(&listed(signed_in()));
    assert_eq!(view.rows.len(), 2);
    let first = &view.rows[0];
    assert_eq!(first.name, "Phone consultation");
    assert_eq!(first.subtitle, "30 min · Phone");
    assert_eq!(first.state_label, "Active");
    assert_eq!(first.tone, district_ffi::workflows::RunTone::Success);
    assert!(first.booking_page.ends_with("/phone-consultation"));
    let second = &view.rows[1];
    assert_eq!(second.state_label, "Archived");
    assert_eq!(second.booking_page, "Not public");
    assert!(view.offers_create);
    assert_eq!(view.create_label, "Create event type");
    assert!(!list_view(&listed(viewer())).offers_create);
    // Not over a list that was not read.
    let failed = list_view(&list_read(context(signed_in()), Err(server_error())));
    assert!(!failed.offers_create);
    assert!(failed.rows.is_empty());
}

/// The create form is the short one; its booking page is made from the name
/// and avoids the slugs listed; a create that lands closes it and reads the
/// list again.
#[test]
fn a_create_makes_its_booking_page_from_the_name() {
    let session = creating(signed_in());
    let editor = list_view(&session).create.expect("open");
    assert!(editor.creating);
    assert_eq!(editor.dialog.title, "Create event type");
    assert_eq!(editor.form.duration, "30");
    assert_eq!(editor.booking_page, "event-type");
    assert_eq!(editor.duration_shown, "30 min");
    let offered: Vec<&str> = editor.locations.iter().map(|c| c.value.as_str()).collect();
    assert_eq!(offered, ["livekit", "phone", "in_person", "link"]);
    assert!(editor.locations[0].selected);
    let form = EventTypeFormView {
        name: "Phone consultation".to_owned(),
        duration: "ten".to_owned(),
        ..editor.form
    };
    let session = session.ui(list(EventTypesAction::EditForm { form: form.clone() }));
    let editor = list_view(&session).create.expect("open");
    assert_eq!(editor.booking_page, "phone-consultation-2");
    assert_eq!(editor.duration_shown, "Not set");
    let refused = list_view(&session.ui(list(EventTypesAction::Save)));
    assert_eq!(
        refused.create.expect("open").validation.as_deref(),
        Some("The duration has to be a whole number of minutes, at least 1.")
    );
    let session = creating(signed_in())
        .ui(list(EventTypesAction::EditForm {
            form: EventTypeFormView {
                duration: "45".to_owned(),
                ..form
            },
        }))
        .ui(list(EventTypesAction::Save));
    let saving = list_view(&session).create.expect("open");
    assert!(saving.dialog.working && !saving.dialog.can_confirm);
    assert_eq!(saving.dialog.confirm, "Saving…");
    let created = answer_admin(
        session,
        |r| matches!(r, SchedulingAdminRequest::CreateEventType { .. }),
        Ok(contracts::json("district-scheduling-event-type.json")),
    );
    let view = list_view(&created);
    assert_eq!(view.create, None);
    assert_eq!(view.status, district_ffi::LoadStatus::Loading);
    let closed = list_view(&creating(signed_in()).ui(list(EventTypesAction::CloseEditor)));
    assert_eq!(closed.create, None);
}

/// One event type: its details, its hosts in the core's words, its questions
/// in the order the booker sees them, and the writes for a member who may
/// change things; a failed hosts read blanks only its own card.
#[test]
fn one_event_type_shows_its_details_hosts_and_questions() {
    let view = event_type_view(&event_type(signed_in()));
    assert_eq!(view.name, "Phone consultation");
    assert_eq!(view.status, district_ffi::LoadStatus::Ready);
    assert!(view.offers_writes);
    assert_eq!(
        [
            view.edit_label.as_str(),
            &view.manage_label,
            &view.hosts_label,
            &view.questions_label
        ],
        ["Edit", "Manage", "Hosts", "Questions"]
    );
    let facts: Vec<&str> = view.facts.iter().map(|f| f.label.as_str()).collect();
    assert!(facts.contains(&"Location"));
    let hosts: Vec<(&str, &str)> = view
        .hosts
        .iter()
        .map(|h| (h.label.as_str(), h.value.as_str()))
        .collect();
    assert_eq!(
        hosts,
        [
            ("Contract Member", "Required"),
            ("Rotation Host", "Rotation · archived")
        ]
    );
    let questions: Vec<&str> = view.questions.iter().map(|q| q.value.as_str()).collect();
    assert_eq!(questions, ["Select · required", "Text"]);
    assert!(!event_type_view(&event_type(viewer())).offers_writes);
    let failed = event_type_view(&event_type_with(signed_in(), Err(server_error())));
    assert!(matches!(
        failed.hosts_status,
        district_ffi::LoadStatus::Failed { .. }
    ));
    assert!(failed.hosts.is_empty());
    assert_eq!(failed.questions.len(), 2);
    // Before its reads, the slug names it.
    let loading = event_type_view(&listed(signed_in()).ui(opened("phone-consultation")));
    assert_eq!(loading.name, "phone-consultation");
    assert!(!loading.offers_writes);
    // Back to the list.
    let back = event_type(signed_in()).ui(UiEvent::Back);
    assert_eq!(
        route(&back),
        Route::SchedulingAdmin(SchedulingSection::EventTypes)
    );
}

/// The editor is the whole form; a refusal of the form is said before
/// anything is sent, a refused save keeps the editor and says why, and a save
/// that lands closes it and reads the event type again.
#[test]
fn the_editor_saves_the_whole_form() {
    let session = editing(signed_in());
    let editor = event_type_view(&session).editor.expect("open");
    assert!(!editor.creating);
    assert_eq!(editor.dialog.title, "Edit event type");
    assert_eq!(editor.booking_page, "phone-consultation");
    assert_eq!(editor.location_value_label, None);
    let form = EventTypeFormView {
        location: "phone".to_owned(),
        buffer_before: "-1".to_owned(),
        ..editor.form
    };
    let session = session.ui(detail(EventTypeAction::EditForm { form: form.clone() }));
    let editor = event_type_view(&session).editor.expect("open");
    assert_eq!(editor.location_value_label.as_deref(), Some("Phone number"));
    let refused = event_type_view(&session.ui(detail(EventTypeAction::Save)));
    assert_eq!(
        refused.editor.expect("open").validation.as_deref(),
        Some("Has to be a whole number, 0 or more.")
    );
    let failed = event_type_view(&answer_admin(
        editing(signed_in()).ui(detail(EventTypeAction::Save)),
        |r| matches!(r, SchedulingAdminRequest::PatchEventType { .. }),
        Err(server_error()),
    ));
    let editor = failed.editor.expect("still open");
    assert!(editor.dialog.failure.is_some() && editor.dialog.can_confirm);
    let saved = answer_admin(
        editing(signed_in()).ui(detail(EventTypeAction::Save)),
        |r| matches!(r, SchedulingAdminRequest::PatchEventType { .. }),
        Ok(contracts::json("district-scheduling-event-type.json")),
    );
    let view = event_type_view(&saved);
    assert_eq!(view.editor, None);
    assert_eq!(view.status, district_ffi::LoadStatus::Loading);
    let closed = event_type_view(&editing(signed_in()).ui(detail(EventTypeAction::CloseDialog)));
    assert_eq!(closed.editor, None);
}

/// The state actions: one at a time, what each came to said until put away,
/// a test email sent to the member, and the delete asked once more, which
/// goes back to the list.
#[test]
fn the_state_actions_say_what_they_came_to() {
    let open = event_type_view(&event_type(signed_in()).ui(detail(EventTypeAction::OpenActions)));
    let actions = open.actions.expect("open");
    assert_eq!(actions.dialog.title, "Phone consultation");
    assert_eq!(actions.dialog.confirm, "");
    assert_eq!(actions.toggle_label, "Turn off");
    assert_eq!(actions.archive_label, "Archive");
    let kinds: Vec<&str> = actions
        .test_emails
        .iter()
        .map(|c| c.value.as_str())
        .collect();
    assert_eq!(
        kinds,
        ["confirmation", "cancellation", "reschedule", "reminder"]
    );
    assert!(actions.can_act && actions.delete.is_none());
    let working = event_type_view(
        &event_type(signed_in())
            .ui(detail(EventTypeAction::OpenActions))
            .ui(detail(EventTypeAction::ToggleArchived)),
    );
    assert!(!working.actions.expect("open").can_act);
    let session = turned_off(signed_in());
    let actions = event_type_view(&session).actions.expect("still open");
    assert_eq!(actions.notice.as_deref(), Some("Event type turned off"));
    assert_eq!(actions.toggle_label, "Turn on");
    let away = event_type_view(&session.ui(detail(EventTypeAction::DismissNotice)));
    assert_eq!(away.actions.expect("open").notice, None);
    let emailed = answer_admin(
        event_type(signed_in())
            .ui(detail(EventTypeAction::OpenActions))
            .ui(detail(EventTypeAction::SendTestEmail {
                kind: "reminder".to_owned(),
            })),
        |r| matches!(r, SchedulingAdminRequest::SendTestEmail { .. }),
        Ok(contracts::json("district-scheduling-test-email.json")),
    );
    let notice = event_type_view(&emailed).actions.and_then(|a| a.notice);
    assert!(notice.is_some_and(|n| n.contains("contract@example.com")));
    let asked = event_type(signed_in())
        .ui(detail(EventTypeAction::OpenActions))
        .ui(detail(EventTypeAction::AskDelete));
    let ask = event_type_view(&asked)
        .actions
        .and_then(|a| a.delete)
        .expect("asked");
    assert_eq!(ask.title, "Delete event type?");
    assert_eq!(ask.confirm, "Delete event type");
    let kept = event_type_view(&asked.ui(detail(EventTypeAction::CancelDelete)));
    assert!(kept.actions.expect("open").delete.is_none());
    let deleted = answer_admin(
        event_type(signed_in())
            .ui(detail(EventTypeAction::OpenActions))
            .ui(detail(EventTypeAction::AskDelete))
            .ui(detail(EventTypeAction::ConfirmDelete)),
        |r| matches!(r, SchedulingAdminRequest::DeleteEventType { .. }),
        Ok(contracts::json("district-scheduling-ok.json")),
    );
    assert_eq!(
        route(&deleted),
        Route::SchedulingAdmin(SchedulingSection::EventTypes)
    );
}

/// The hosts open on a read, with no save before it lands; who could be
/// added is listed; the rotation shows for round robin only; a save needs a
/// host and whole priorities, then puts the hosts and the routing and closes.
#[test]
fn the_hosts_are_saved_whole_once_read() {
    let loading = event_type_view(&event_type(signed_in()).ui(detail(EventTypeAction::OpenHosts)));
    let dialog = loading.hosts_dialog.expect("open");
    assert_eq!(dialog.status, district_ffi::LoadStatus::Loading);
    assert!(!dialog.dialog.can_confirm);
    let session = hosting(signed_in());
    let dialog = event_type_view(&session).hosts_dialog.expect("open");
    assert!(dialog.dialog.can_confirm);
    assert_eq!(dialog.hosts.len(), 2);
    assert_eq!(dialog.hosts[1].role, "rotation");
    let candidates: Vec<(&str, &str)> = dialog
        .candidates
        .iter()
        .map(|c| (c.value.as_str(), c.label.as_str()))
        .collect();
    assert_eq!(candidates, [("sched-user-2", "sam@example.com")]);
    // The event type routes round robin.
    assert!(dialog.shows_rotation);
    assert!(dialog.routing_modes[1].selected);
    assert!(dialog.rotations[2].selected);
    let roles: Vec<&str> = dialog.roles.iter().map(|r| r.label.as_str()).collect();
    assert_eq!(roles, ["Required", "Rotation", "Optional"]);
    let session = session
        .ui(detail(EventTypeAction::SetRoutingMode {
            mode: "fixed".to_owned(),
        }))
        .ui(detail(EventTypeAction::SetRotation {
            rotation: "even".to_owned(),
        }))
        .ui(detail(EventTypeAction::AddHost {
            user_id: "sched-user-2".to_owned(),
        }))
        .ui(detail(EventTypeAction::SetHostRole {
            user_id: "sched-user-2".to_owned(),
            role: "optional".to_owned(),
        }))
        .ui(detail(EventTypeAction::SetHostPriority {
            user_id: "sched-user-2".to_owned(),
            priority: "x".to_owned(),
        }))
        .ui(detail(EventTypeAction::RemoveHost {
            user_id: "sched-user-rotation".to_owned(),
        }));
    let dialog = event_type_view(&session).hosts_dialog.expect("open");
    assert!(!dialog.shows_rotation);
    let added = dialog.hosts.last().expect("added");
    assert_eq!(
        (
            added.name.as_str(),
            added.role.as_str(),
            added.priority.as_str()
        ),
        ("sam@example.com", "optional", "x")
    );
    assert!(dialog.candidates.is_empty());
    let session = session.ui(detail(EventTypeAction::SaveHosts));
    let refused = event_type_view(&session).hosts_dialog.expect("open");
    assert_eq!(
        refused.validation.as_deref(),
        Some("A host's priority has to be a whole number, 0 or more.")
    );
    let session = session
        .ui(detail(EventTypeAction::SetHostPriority {
            user_id: "sched-user-2".to_owned(),
            priority: "1".to_owned(),
        }))
        .ui(detail(EventTypeAction::SaveHosts));
    let saving = event_type_view(&session).hosts_dialog.expect("open");
    assert!(saving.dialog.working);
    assert_eq!(saving.dialog.confirm, "Saving…");
    let put = answer_admin(
        session,
        |r| matches!(r, SchedulingAdminRequest::SetHosts { .. }),
        Ok(contracts::json("district-scheduling-hosts.json")),
    );
    let routed = answer_admin(
        put,
        |r| matches!(r, SchedulingAdminRequest::PatchEventType { .. }),
        Ok(contracts::json("district-scheduling-event-type.json")),
    );
    assert_eq!(event_type_view(&routed).hosts_dialog, None);
    let failed = answer_admin(
        hosting(signed_in()).ui(detail(EventTypeAction::SaveHosts)),
        |r| matches!(r, SchedulingAdminRequest::SetHosts { .. }),
        Err(server_error()),
    );
    let dialog = event_type_view(&failed).hosts_dialog.expect("still open");
    assert!(dialog.dialog.failure.is_some());
    let read_failed = answer_admin(
        event_type(signed_in()).ui(detail(EventTypeAction::OpenHosts)),
        |r| matches!(r, SchedulingAdminRequest::Hosts { .. }),
        Err(server_error()),
    );
    let dialog = event_type_view(&read_failed).hosts_dialog.expect("open");
    assert!(matches!(
        dialog.status,
        district_ffi::LoadStatus::Failed { .. }
    ));
    assert!(!dialog.dialog.can_confirm);
}

/// The questions: the list in order, one question's form in its place, a
/// select's choices added and taken off through the core, a save that says
/// so, and a delete asked once more.
#[test]
fn the_questions_are_added_edited_and_deleted() {
    let dialog = event_type_view(&asking(signed_in()))
        .questions_dialog
        .expect("open");
    assert_eq!(dialog.questions.len(), 2);
    assert_eq!(dialog.questions[0].id, "q-contract-1");
    assert!(dialog.can_add && dialog.form.is_none());
    assert_eq!(dialog.add_label, "Add question");
    let session = question_form(signed_in());
    let form = event_type_view(&session)
        .questions_dialog
        .and_then(|q| q.form)
        .expect("the form");
    assert_eq!(form.title, "Add question");
    assert_eq!(form.save_label, "Add question");
    assert!(form.shows_options);
    assert_eq!(form.form.options, ["Toronto"]);
    assert!(form.kinds[1].selected);
    let session = session.ui(detail(EventTypeAction::RemoveOption {
        option: "Toronto".to_owned(),
    }));
    let session = session.ui(detail(EventTypeAction::SaveQuestion));
    let refused = event_type_view(&session)
        .questions_dialog
        .and_then(|q| q.form)
        .expect("the form");
    assert_eq!(
        refused.validation.as_deref(),
        Some("A Select question needs at least one option.")
    );
    let saved = answer_admin(
        question_form(signed_in()).ui(detail(EventTypeAction::SaveQuestion)),
        |r| matches!(r, SchedulingAdminRequest::CreateQuestion { .. }),
        Ok(contracts::json("district-scheduling-question.json")),
    );
    let dialog = event_type_view(&saved).questions_dialog.expect("open");
    assert_eq!(dialog.form, None);
    assert_eq!(dialog.notice.as_deref(), Some("Question added"));
    let editing = asking(signed_in()).ui(detail(EventTypeAction::EditQuestion {
        id: "q-contract-2".to_owned(),
    }));
    let form = event_type_view(&editing)
        .questions_dialog
        .and_then(|q| q.form)
        .expect("the form");
    assert_eq!(form.title, "Edit question");
    assert_eq!(form.save_label, "Save question");
    assert!(!form.shows_options);
    let cancelled = event_type_view(&editing.ui(detail(EventTypeAction::CancelQuestion)));
    assert_eq!(cancelled.questions_dialog.expect("open").form, None);
    let asked = asking(signed_in()).ui(detail(EventTypeAction::AskDeleteQuestion {
        id: "q-contract-1".to_owned(),
    }));
    let ask = event_type_view(&asked)
        .questions_dialog
        .and_then(|q| q.delete)
        .expect("asked");
    assert_eq!(ask.confirm, "Delete question");
    let kept = event_type_view(&asked.ui(detail(EventTypeAction::CancelDeleteQuestion)));
    assert!(kept.questions_dialog.expect("open").delete.is_none());
    let deleting = asking(signed_in())
        .ui(detail(EventTypeAction::AskDeleteQuestion {
            id: "q-contract-1".to_owned(),
        }))
        .ui(detail(EventTypeAction::ConfirmDeleteQuestion));
    assert!(
        !event_type_view(&deleting)
            .questions_dialog
            .expect("open")
            .can_add
    );
    let failed = answer_admin(
        deleting,
        |r| matches!(r, SchedulingAdminRequest::DeleteQuestion { .. }),
        Err(server_error()),
    );
    let dialog = event_type_view(&failed).questions_dialog.expect("open");
    assert!(dialog.dialog.failure.is_some());
}

#[test]
fn each_action_is_its_core_event() {
    let form = EventTypeFormView {
        name: "n".to_owned(),
        description: String::new(),
        duration: "30".to_owned(),
        interval: "30".to_owned(),
        location: "livekit".to_owned(),
        location_value: String::new(),
        is_active: true,
        is_public: true,
        show_taken_slots: false,
        buffer_before: "0".to_owned(),
        buffer_after: "0".to_owned(),
        min_notice: "0".to_owned(),
        max_future_days: "0".to_owned(),
        max_active_bookings: "0".to_owned(),
    };
    let core_form = district_core::scheduling_admin::EventTypeForm {
        name: "n".to_owned(),
        ..district_core::scheduling_admin::EventTypeForm::default()
    };
    let to_list = |e| Event::SchedulingAdmin(SchedulingAdminEvent::EventTypes(e));
    for (sent, event) in [
        (
            EventTypesAction::OpenCreate,
            to_list(EventTypesEvent::OpenCreate),
        ),
        (
            EventTypesAction::EditForm { form: form.clone() },
            to_list(EventTypesEvent::EditForm(Box::new(core_form.clone()))),
        ),
        (EventTypesAction::Save, to_list(EventTypesEvent::Save)),
        (
            EventTypesAction::CloseEditor,
            to_list(EventTypesEvent::CloseEditor),
        ),
    ] {
        assert_eq!(list(sent).events(), [event]);
    }
    let question = QuestionFormView {
        id: Some("q".to_owned()),
        label: "l".to_owned(),
        kind: "text".to_owned(),
        options: Vec::new(),
        required: false,
        position: "0".to_owned(),
    };
    let core_question = district_core::scheduling_admin::QuestionForm {
        id: Some("q".to_owned()),
        label: "l".to_owned(),
        kind: "text".to_owned(),
        position: "0".to_owned(),
        ..district_core::scheduling_admin::QuestionForm::default()
    };
    let s = |v: &str| v.to_owned();
    let to_detail = |e| Event::SchedulingAdmin(SchedulingAdminEvent::EventType(e));
    use EventTypeAction as A;
    use EventTypeEvent as E;
    for (sent, event) in [
        (A::OpenEditor, E::OpenEditor),
        (A::EditForm { form }, E::EditForm(core_form)),
        (A::Save, E::Save),
        (A::OpenActions, E::OpenActions),
        (A::ToggleActive, E::ToggleActive),
        (A::ToggleArchived, E::ToggleArchived),
        (
            A::SendTestEmail {
                kind: s("reminder"),
            },
            E::SendTestEmail(s("reminder")),
        ),
        (A::AskDelete, E::AskDelete),
        (A::CancelDelete, E::CancelDelete),
        (A::ConfirmDelete, E::ConfirmDelete),
        (A::OpenHosts, E::OpenHosts),
        (
            A::SetRoutingMode { mode: s("fixed") },
            E::SetRoutingMode(s("fixed")),
        ),
        (
            A::SetRotation {
                rotation: s("even"),
            },
            E::SetRotation(s("even")),
        ),
        (A::AddHost { user_id: s("u") }, E::AddHost(s("u"))),
        (A::RemoveHost { user_id: s("u") }, E::RemoveHost(s("u"))),
        (
            A::SetHostRole {
                user_id: s("u"),
                role: s("optional"),
            },
            E::SetHostRole {
                user_id: s("u"),
                role: s("optional"),
            },
        ),
        (
            A::SetHostPriority {
                user_id: s("u"),
                priority: s("2"),
            },
            E::SetHostPriority {
                user_id: s("u"),
                priority: s("2"),
            },
        ),
        (A::SaveHosts, E::SaveHosts),
        (A::OpenQuestions, E::OpenQuestions),
        (A::AddQuestion, E::AddQuestion),
        (A::EditQuestion { id: s("q") }, E::EditQuestion(s("q"))),
        (
            A::EditQuestionForm { form: question },
            E::EditQuestionForm(core_question),
        ),
        (A::AddOption { option: s("o") }, E::AddOption(s("o"))),
        (A::RemoveOption { option: s("o") }, E::RemoveOption(s("o"))),
        (A::CancelQuestion, E::CancelQuestion),
        (A::SaveQuestion, E::SaveQuestion),
        (
            A::AskDeleteQuestion { id: s("q") },
            E::AskDeleteQuestion(s("q")),
        ),
        (A::CancelDeleteQuestion, E::CancelDeleteQuestion),
        (A::ConfirmDeleteQuestion, E::ConfirmDeleteQuestion),
        (A::DismissNotice, E::DismissNotice),
        (A::CloseDialog, E::CloseDialog),
    ] {
        assert_eq!(detail(sent).events(), [to_detail(event)]);
    }
}
