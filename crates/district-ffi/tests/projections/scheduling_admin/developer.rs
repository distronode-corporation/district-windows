//! Developer (src/scheduling_admin/developer.rs): the API keys, the connected
//! apps and the MCP address, the webhooks and their deliveries, making a key
//! and adding or changing a webhook, and every removal, asked once more.
//!
//! The key a create answers and a new webhook's signing secret are the
//! fixtures', read as the tests run: no snapshot, and no `Debug`, may hold
//! either.

use district_core::SchedulingAdminEvent;
use district_core::scheduling_admin::{DeveloperEvent, DeveloperTab};
use district_ffi::scheduling_admin::developer::{
    DeveloperAction, DeveloperTabView, SchedulingDeveloperView,
};
use district_ffi::workflows::RunTone;

use super::*;

/// This area's snapshot cases.
pub(crate) fn cases() -> Vec<Case> {
    vec![
        (
            "scheduling-admin-developer-loading",
            open(signed_in(), SchedulingAdminSection::Developer),
        ),
        ("scheduling-admin-developer", developer(signed_in())),
        ("scheduling-admin-developer-viewer", developer(viewer())),
        (
            "scheduling-admin-developer-keys-failed",
            keys_read(opened(signed_in()), Err(server_error())),
        ),
        (
            "scheduling-admin-developer-keys-empty",
            keys_read(opened(signed_in()), Ok(items(json!([])))),
        ),
        (
            "scheduling-admin-developer-key-create-open",
            developer(signed_in()).ui(event(DeveloperAction::OpenCreateKey)),
        ),
        (
            "scheduling-admin-developer-key-name-missing",
            developer(signed_in())
                .ui(event(DeveloperAction::OpenCreateKey))
                .ui(event(DeveloperAction::CreateKey)),
        ),
        (
            "scheduling-admin-developer-key-creating",
            creating_key(signed_in()),
        ),
        ("scheduling-admin-developer-key-made", made_key(signed_in())),
        (
            "scheduling-admin-developer-key-copied",
            made_key(signed_in()).ui(event(DeveloperAction::KeyCopied)),
        ),
        (
            "scheduling-admin-developer-key-create-refused",
            key_answer(creating_key(signed_in()), Err(server_error())),
        ),
        (
            "scheduling-admin-developer-key-revoke-asked",
            revoke_asked(signed_in()),
        ),
        (
            "scheduling-admin-developer-key-revoking",
            revoke_asked(signed_in()).ui(event(DeveloperAction::ConfirmRemoval)),
        ),
        (
            "scheduling-admin-developer-key-revoke-refused",
            removal_answer(
                revoke_asked(signed_in()).ui(event(DeveloperAction::ConfirmRemoval)),
                Err(server_error()),
            ),
        ),
        ("scheduling-admin-developer-apps", apps(signed_in())),
        (
            "scheduling-admin-developer-apps-no-host",
            apps_with(signed_in(), no_host()),
        ),
        (
            "scheduling-admin-developer-app-revoke-asked",
            apps(signed_in()).ui(event(DeveloperAction::AskRevokeApp {
                connection_id: RAYCAST.to_owned(),
            })),
        ),
        ("scheduling-admin-developer-webhooks", webhooks(signed_in())),
        (
            "scheduling-admin-developer-deliveries-loading",
            deliveries_open(signed_in()),
        ),
        (
            "scheduling-admin-developer-deliveries",
            deliveries(signed_in()),
        ),
        (
            "scheduling-admin-developer-deliveries-failed",
            deliveries_read(deliveries_open(signed_in()), Err(server_error())),
        ),
        (
            "scheduling-admin-developer-webhook-add-open",
            adding(signed_in()),
        ),
        (
            "scheduling-admin-developer-webhook-url-refused",
            adding(signed_in())
                .ui(event(DeveloperAction::EditWebhookUrl {
                    url: "http://hooks.example.com/in".to_owned(),
                }))
                .ui(event(DeveloperAction::ToggleEvent {
                    event: "booking.created".to_owned(),
                }))
                .ui(event(DeveloperAction::SaveWebhook)),
        ),
        (
            "scheduling-admin-developer-webhook-added",
            added(signed_in()),
        ),
        (
            "scheduling-admin-developer-webhook-edit-open",
            editing(signed_in()),
        ),
        (
            "scheduling-admin-developer-webhook-saved",
            saved(signed_in()),
        ),
        (
            "scheduling-admin-developer-webhook-save-refused",
            webhook_answer(saving(signed_in()), Err(server_error())),
        ),
        (
            "scheduling-admin-developer-webhook-delete-asked",
            webhooks(signed_in()).ui(event(DeveloperAction::AskDeleteWebhook {
                webhook_id: RECORDINGS.to_owned(),
            })),
        ),
    ]
}

/// The fixture's key that was used.
const ZAPIER: &str = "key-contract-1";

/// The fixture's key never used.
const UNUSED: &str = "key-contract-2";

/// The fixture's app that was used.
const RAYCAST: &str = "oauth-contract-1";

/// The fixture's webhook that is on, with deliveries.
const BOOKINGS: &str = "wh-contract-1";

/// The fixture's webhook that is off.
const RECORDINGS: &str = "wh-contract-2";

/// The key a create answers, as the fixture has it.
fn minted_key() -> String {
    contracts::json("district-scheduling-api-key-created.json")["data"]["key"]
        .as_str()
        .expect("the fixture has a key")
        .to_owned()
}

/// The signing secret an add answers, as the fixture has it.
fn minted_webhook_secret() -> String {
    contracts::json("district-scheduling-webhook-created.json")["data"]["secret"]
        .as_str()
        .expect("the fixture has a secret")
        .to_owned()
}

fn event(action: DeveloperAction) -> UiEvent {
    UiEvent::SchedulingAdmin {
        action: SchedulingAdminAction::Developer { action },
    }
}

/// A list answer holding `list`.
fn items(list: Value) -> Value {
    json!({ "ok": true, "data": { "items": list } })
}

/// The status a scheduler with no public address answers.
fn no_host() -> district_model::SchedulingStatusResponse {
    let mut value = contracts::json("district-scheduling-status-ready.json");
    value["tenant"]["publicHost"] = json!("");
    contracts::decode("status without a host", value)
}

/// Developer opened: the status read afresh and the member's profile read,
/// the keys on their way.
fn opened(session: Session) -> Session {
    me(status(open(session, SchedulingAdminSection::Developer)))
}

/// The newest read of the keys, answered with `result`.
fn keys_read(session: Session, result: Result<Value, ApiError>) -> Session {
    answer_admin(
        session,
        |r| matches!(r, SchedulingAdminRequest::ApiKeys),
        result,
    )
}

/// Developer, its keys read.
fn developer(session: Session) -> Session {
    keys_read(
        opened(session),
        Ok(contracts::json("district-scheduling-api-keys.json")),
    )
}

/// The key form, a name typed and the create pressed.
fn creating_key(session: Session) -> Session {
    developer(session)
        .ui(event(DeveloperAction::OpenCreateKey))
        .ui(event(DeveloperAction::EditKeyName {
            name: " Zapier ".to_owned(),
        }))
        .ui(event(DeveloperAction::CreateKey))
}

/// The create, answered with `result`.
fn key_answer(session: Session, result: Result<Value, ApiError>) -> Session {
    answer_admin(
        session,
        |r| matches!(r, SchedulingAdminRequest::CreateApiKey { .. }),
        result,
    )
}

/// The key made, and the keys read again.
fn made_key(session: Session) -> Session {
    keys_read(
        key_answer(
            creating_key(session),
            Ok(contracts::json("district-scheduling-api-key-created.json")),
        ),
        Ok(contracts::json("district-scheduling-api-keys.json")),
    )
}

/// The unused key's revoke, asked about.
fn revoke_asked(session: Session) -> Session {
    developer(session).ui(event(DeveloperAction::AskRevokeKey {
        key_id: UNUSED.to_owned(),
    }))
}

/// A removal, answered with `result`.
fn removal_answer(session: Session, result: Result<Value, ApiError>) -> Session {
    answer_admin(
        session,
        |r| {
            matches!(
                r,
                SchedulingAdminRequest::DeleteApiKey { .. }
                    | SchedulingAdminRequest::DeleteOauthConnection { .. }
                    | SchedulingAdminRequest::DeleteWebhook { .. }
            )
        },
        result,
    )
}

/// The apps tab, its status answered with `status`, and the apps read.
fn apps_with(session: Session, status: district_model::SchedulingStatusResponse) -> Session {
    let session = me(open(session, SchedulingAdminSection::Developer).answer(
        |e| matches!(e, Effect::LoadSchedulingStatus { .. }),
        |ticket| Event::SchedulingStatusLoaded {
            ticket,
            result: Ok(status),
        },
    ));
    let session = keys_read(
        session,
        Ok(contracts::json("district-scheduling-api-keys.json")),
    )
    .ui(event(DeveloperAction::SelectTab {
        tab: DeveloperTabView::Apps,
    }));
    answer_admin(
        session,
        |r| matches!(r, SchedulingAdminRequest::OauthConnections),
        Ok(contracts::json(
            "district-scheduling-oauth-connections.json",
        )),
    )
}

/// The apps tab, read, the scheduler at its public address.
fn apps(session: Session) -> Session {
    apps_with(
        session,
        contracts::read("district-scheduling-status-ready.json"),
    )
}

/// The webhooks tab, read.
fn webhooks(session: Session) -> Session {
    answer_admin(
        developer(session).ui(event(DeveloperAction::SelectTab {
            tab: DeveloperTabView::Webhooks,
        })),
        |r| matches!(r, SchedulingAdminRequest::Webhooks),
        Ok(contracts::json("district-scheduling-webhooks.json")),
    )
}

/// The first webhook's deliveries, opened.
fn deliveries_open(session: Session) -> Session {
    webhooks(session).ui(event(DeveloperAction::ToggleDeliveries {
        webhook_id: BOOKINGS.to_owned(),
    }))
}

/// The newest read of deliveries, answered with `result`.
fn deliveries_read(session: Session, result: Result<Value, ApiError>) -> Session {
    answer_admin(
        session,
        |r| matches!(r, SchedulingAdminRequest::WebhookDeliveries { .. }),
        result,
    )
}

/// The first webhook's deliveries, read.
fn deliveries(session: Session) -> Session {
    deliveries_read(
        deliveries_open(session),
        Ok(contracts::json(
            "district-scheduling-webhook-deliveries.json",
        )),
    )
}

/// The add form, open.
fn adding(session: Session) -> Session {
    webhooks(session).ui(event(DeveloperAction::OpenCreateWebhook))
}

/// A webhook's save, answered with `result`.
fn webhook_answer(session: Session, result: Result<Value, ApiError>) -> Session {
    answer_admin(
        session,
        |r| {
            matches!(
                r,
                SchedulingAdminRequest::CreateWebhook { .. }
                    | SchedulingAdminRequest::PatchWebhook { .. }
            )
        },
        result,
    )
}

/// The webhooks read again after a write.
fn webhooks_again(session: Session) -> Session {
    answer_admin(
        session,
        |r| matches!(r, SchedulingAdminRequest::Webhooks),
        Ok(contracts::json("district-scheduling-webhooks.json")),
    )
}

/// A webhook added, its signing secret shown once, the webhooks read again.
fn added(session: Session) -> Session {
    let typed = adding(session)
        .ui(event(DeveloperAction::EditWebhookUrl {
            url: "https://hooks.example.com/bookings".to_owned(),
        }))
        .ui(event(DeveloperAction::ToggleEvent {
            event: "booking.created".to_owned(),
        }))
        .ui(event(DeveloperAction::SaveWebhook));
    webhooks_again(webhook_answer(
        typed,
        Ok(contracts::json("district-scheduling-webhook-created.json")),
    ))
}

/// The first webhook's form, open.
fn editing(session: Session) -> Session {
    webhooks(session).ui(event(DeveloperAction::OpenEditWebhook {
        webhook_id: BOOKINGS.to_owned(),
    }))
}

/// A rescheduled booking added to what the first webhook sends, and saved.
fn saving(session: Session) -> Session {
    editing(session)
        .ui(event(DeveloperAction::ToggleEvent {
            event: "booking.rescheduled".to_owned(),
        }))
        .ui(event(DeveloperAction::SaveWebhook))
}

/// The save landed, and the webhooks read again.
fn saved(session: Session) -> Session {
    webhooks_again(webhook_answer(
        saving(session),
        Ok(contracts::json("district-scheduling-ok.json")),
    ))
}

fn developer_view(session: &Session) -> SchedulingDeveloperView {
    let SchedulingAdminPage::Developer { view } = admin_view(session).page else {
        panic!("not Developer");
    };
    view
}

/// Whether a request `pick` finds is on its way.
fn sent(session: &Session, pick: fn(&SchedulingAdminRequest) -> bool) -> bool {
    session
        .pending
        .iter()
        .any(|e| matches!(e, Effect::SchedulingAdmin { request, .. } if pick(request)))
}

/// The keys tab: each key's name and when it was made and last used, in the
/// member's zone; Create key and Revoke for a member who may change things,
/// neither for a viewer; the tabs, the keys one selected.
#[test]
fn the_keys_show_with_their_writes() {
    let session = developer(signed_in());
    assert_eq!(
        route(&session),
        Route::SchedulingAdmin(SchedulingSection::Developer)
    );
    let page = admin_view(&session);
    assert_eq!(page.title, "Developer");
    assert!(
        page.sections
            .iter()
            .any(|s| s.section == SchedulingAdminSection::Developer && s.selected)
    );
    let view = developer_view(&session);
    assert_eq!(view.times_in, "Times in America/Toronto");
    assert_eq!(view.tab, DeveloperTabView::Keys);
    let tabs: Vec<(&str, bool)> = view
        .tabs
        .iter()
        .map(|t| (t.label.as_str(), t.selected))
        .collect();
    assert_eq!(
        tabs,
        [
            ("API keys", true),
            ("Connected apps", false),
            ("Webhooks", false)
        ]
    );
    assert_eq!(view.keys_status, district_ffi::LoadStatus::Ready);
    let keys: Vec<[&str; 3]> = view
        .keys
        .iter()
        .map(|k| [k.id.as_str(), &k.name, &k.line])
        .collect();
    assert_eq!(keys.len(), 2);
    assert_eq!(keys[0][..2], [ZAPIER, "Zapier"]);
    assert!(keys[0][2].starts_with("Created ") && keys[0][2].contains("Last used "));
    assert!(keys[1][2].ends_with("Last used Never"), "{}", keys[1][2]);
    assert_eq!(
        [
            view.keys_heading.as_str(),
            &view.create_key_label,
            &view.revoke_key_label,
            &view.revoke_app_label,
            &view.add_webhook_label,
            &view.edit_webhook_label,
            &view.delete_webhook_label,
            &view.deliveries_label,
        ],
        [
            "Your API keys",
            "Create key",
            "Revoke key",
            "Revoke access",
            "Add webhook",
            "Edit",
            "Delete webhook",
            "Deliveries"
        ]
    );
    assert!(view.offers_writes && !view.removing);
    assert_eq!((view.removal, view.removal_failure), (None, None));
    assert_eq!((view.key_dialog, view.webhook_dialog), (None, None));
    // The other tabs are not read until shown.
    assert_eq!(view.apps_status, district_ffi::LoadStatus::Loading);
    assert!(!sent(&session, |r| matches!(
        r,
        SchedulingAdminRequest::OauthConnections | SchedulingAdminRequest::Webhooks
    )));

    // A viewer reads the same, and is offered and opens none of the writes.
    let viewer = developer(viewer());
    let view = developer_view(&viewer);
    assert_eq!(view.keys.len(), 2);
    assert!(!view.offers_writes);
    let tried =
        viewer
            .ui(event(DeveloperAction::OpenCreateKey))
            .ui(event(DeveloperAction::AskRevokeKey {
                key_id: ZAPIER.to_owned(),
            }));
    let view = developer_view(&tried);
    assert_eq!((view.key_dialog, view.removal), (None, None));
    assert_eq!(route(&session.ui(UiEvent::Back)), Route::Scheduling);
}

/// A tab's failed read is a status of its own, never an empty list, and its
/// own Refresh reads it alone; an empty list says so.
#[test]
fn a_tab_that_failed_is_read_again_alone() {
    let loading = developer_view(&open(signed_in(), SchedulingAdminSection::Developer));
    assert_eq!(loading.keys_status, district_ffi::LoadStatus::Loading);
    assert_eq!(loading.times_in, "Times in UTC");
    let failed = keys_read(opened(signed_in()), Err(server_error()));
    let view = developer_view(&failed);
    assert!(matches!(
        view.keys_status,
        district_ffi::LoadStatus::Failed { .. }
    ));
    assert!(view.keys.is_empty());
    let again = failed.ui(event(DeveloperAction::Refresh));
    assert!(sent(&again, |r| matches!(
        r,
        SchedulingAdminRequest::ApiKeys
    )));
    assert_eq!(
        developer_view(&again).keys_status,
        district_ffi::LoadStatus::Loading
    );
    let empty = developer_view(&keys_read(opened(signed_in()), Ok(items(json!([])))));
    assert_eq!(empty.keys_status, district_ffi::LoadStatus::Ready);
    assert!(empty.keys.is_empty());
    assert_eq!(empty.keys_empty, "No API keys yet.");
}

/// A key is made from a name checked before anything is sent; it is shown
/// once in its dialog, which then offers nothing but Copy and Done, and the
/// keys are read again. The key reaches no snapshot and no `Debug`.
#[test]
fn a_key_is_made_and_shown_once() {
    let open = developer_view(&developer(signed_in()).ui(event(DeveloperAction::OpenCreateKey)))
        .key_dialog
        .expect("open");
    assert_eq!(open.dialog.title, "Create key");
    assert_eq!(open.dialog.confirm, "Create key");
    assert_eq!(open.dialog.close, "Cancel");
    assert!(!open.dialog.can_confirm, "no name yet");
    assert_eq!(open.name_label, "Name");
    assert!(open.editable && open.minted.is_none());

    let missing = developer(signed_in())
        .ui(event(DeveloperAction::OpenCreateKey))
        .ui(event(DeveloperAction::CreateKey));
    assert!(!sent(&missing, |r| matches!(
        r,
        SchedulingAdminRequest::CreateApiKey { .. }
    )));
    assert_eq!(
        developer_view(&missing)
            .key_dialog
            .expect("open")
            .name_error
            .as_deref(),
        Some("Give the key a name.")
    );

    let creating = creating_key(signed_in());
    assert!(sent(&creating, |r| matches!(
        r,
        SchedulingAdminRequest::CreateApiKey { name } if name == "Zapier"
    )));
    let open = developer_view(&creating).key_dialog.expect("open");
    assert!(open.dialog.working && !open.dialog.can_confirm && !open.editable);

    let made = made_key(signed_in());
    let open = developer_view(&made).key_dialog.expect("still open");
    assert_eq!(open.dialog.title, "Your key for Zapier");
    assert_eq!(open.dialog.confirm, "", "nothing left to create");
    assert_eq!(open.dialog.close, "Done");
    assert!(!open.dialog.can_confirm);
    let shown = open.minted.clone().expect("shown once");
    assert_eq!(shown.value, minted_key().as_bytes());
    assert_eq!(shown.warning, "Copy it now. It is not shown again.");
    assert_eq!(shown.copy_label, "Copy key");
    assert_eq!(shown.note, None);
    assert!(!format!("{open:?}").contains(&minted_key()));
    assert!(format!("{shown:?}").contains("<redacted>"));
    let json = serde_json::to_string(&screen_view(&made.model)).unwrap();
    assert!(!json.contains(&minted_key()), "{json}");
    let copied = developer_view(&made.ui(event(DeveloperAction::KeyCopied)));
    assert_eq!(
        copied
            .key_dialog
            .expect("open")
            .minted
            .expect("shown")
            .copy_label,
        "Copied"
    );
    let closed = developer_view(&made_key(signed_in()).ui(event(DeveloperAction::CloseDialog)));
    assert_eq!(closed.key_dialog, None);

    let refused = developer_view(&key_answer(creating_key(signed_in()), Err(server_error())))
        .key_dialog
        .expect("still open");
    assert!(refused.dialog.failure.is_some() && refused.minted.is_none());
    assert_eq!(refused.name, " Zapier ", "the name is kept");
}

/// A key's revoke is asked once more, naming it, its confirm destructive;
/// meanwhile every revoke waits; a refusal is said on the page, and the next
/// question puts it away; a revoke that landed reads the keys again.
#[test]
fn a_revoke_is_asked_once_more() {
    let asked = revoke_asked(signed_in());
    let ask = developer_view(&asked).removal.expect("asked");
    assert_eq!(ask.title, "Revoke Unused?");
    assert_eq!(ask.body, "Anything using it stops working now.");
    assert_eq!(ask.confirm, "Revoke key");
    assert_eq!(ask.close, "Cancel");
    assert!(ask.destructive && ask.can_confirm);
    let kept = developer_view(&asked.ui(event(DeveloperAction::CancelRemoval)));
    assert_eq!(kept.removal, None);

    let revoking = revoke_asked(signed_in()).ui(event(DeveloperAction::ConfirmRemoval));
    assert!(sent(&revoking, |r| matches!(
        r,
        SchedulingAdminRequest::DeleteApiKey { id } if id == UNUSED
    )));
    let view = developer_view(&revoking);
    assert!(view.removing && view.removal.is_none());
    let again = developer_view(
        &revoke_asked(signed_in())
            .ui(event(DeveloperAction::ConfirmRemoval))
            .ui(event(DeveloperAction::AskRevokeKey {
                key_id: ZAPIER.to_owned(),
            })),
    );
    assert_eq!(again.removal, None, "one at a time");

    let landed = removal_answer(revoking, Ok(contracts::json("district-scheduling-ok.json")));
    assert!(sent(&landed, |r| matches!(
        r,
        SchedulingAdminRequest::ApiKeys
    )));
    assert!(!developer_view(&landed).removing);

    let refused = removal_answer(
        revoke_asked(signed_in()).ui(event(DeveloperAction::ConfirmRemoval)),
        Err(server_error()),
    );
    let view = developer_view(&refused);
    assert!(view.removal_failure.is_some() && !view.removing);
    let asked_again = developer_view(&refused.ui(event(DeveloperAction::AskRevokeKey {
        key_id: ZAPIER.to_owned(),
    })));
    assert_eq!(asked_again.removal_failure, None);
    assert_eq!(asked_again.removal.expect("asked").title, "Revoke Zapier?");
}

/// The apps tab: the MCP address and the header with a placeholder, never a
/// key, or what the section says while the scheduler has no address; each
/// app with when it last reached the bookings, and its revoke asked once
/// more.
#[test]
fn the_apps_show_beside_the_mcp_address() {
    let session = apps(signed_in());
    let view = developer_view(&session);
    assert_eq!(view.tab, DeveloperTabView::Apps);
    assert!(view.tabs[1].selected);
    let mcp = view.mcp.clone().expect("an address");
    assert_eq!(
        [mcp.url_label.as_str(), &mcp.url, &mcp.auth_label, &mcp.auth],
        [
            "MCP URL",
            "https://book.example.com/mcp",
            "Authorization header",
            "Bearer <your API key>"
        ]
    );
    assert_eq!(view.apps_status, district_ffi::LoadStatus::Ready);
    let apps: Vec<(&str, &str)> = view
        .apps
        .iter()
        .map(|a| (a.id.as_str(), a.name.as_str()))
        .collect();
    assert_eq!(
        apps,
        [
            (RAYCAST, "Raycast"),
            ("oauth-contract-2", "Internal script")
        ]
    );
    assert_eq!(view.apps[1].last_used, "Never");
    assert!(!view.mcp_note.is_empty() && !view.apps_heading.is_empty());

    let bare = developer_view(&apps_with(signed_in(), no_host()));
    assert_eq!(bare.mcp, None);
    assert!(!bare.mcp_no_host.is_empty());

    let asked = session.ui(event(DeveloperAction::AskRevokeApp {
        connection_id: RAYCAST.to_owned(),
    }));
    let ask = developer_view(&asked).removal.expect("asked");
    assert_eq!(ask.title, "Revoke Raycast?");
    assert_eq!(ask.confirm, "Revoke access");
    assert!(ask.destructive);
    let revoking = asked.ui(event(DeveloperAction::ConfirmRemoval));
    assert!(sent(&revoking, |r| matches!(
        r,
        SchedulingAdminRequest::DeleteOauthConnection { id } if id == RAYCAST
    )));
}

/// The webhooks tab: each webhook's address, the events it sends and whether
/// it is on; its deliveries open under it, read the first time, each with
/// when it was tried, its event and what it came to; a failed read says why;
/// pressed again they close.
#[test]
fn a_webhooks_deliveries_open_under_it() {
    let view = developer_view(&webhooks(signed_in()));
    let rows: Vec<(&str, &str, &str, RunTone, bool)> = view
        .webhooks
        .iter()
        .map(|w| {
            (
                w.id.as_str(),
                w.events.as_str(),
                w.state_label.as_str(),
                w.tone,
                w.open,
            )
        })
        .collect();
    assert_eq!(
        rows,
        [
            (
                BOOKINGS,
                "booking.created, booking.cancelled",
                "On",
                RunTone::Success,
                false
            ),
            (
                RECORDINGS,
                "recording.completed",
                "Off",
                RunTone::Neutral,
                false
            ),
        ]
    );
    assert_eq!(view.deliveries, None);

    let loading = deliveries_open(signed_in());
    assert!(sent(&loading, |r| matches!(
        r,
        SchedulingAdminRequest::WebhookDeliveries { id } if id == BOOKINGS
    )));
    let view = developer_view(&loading);
    assert!(view.webhooks[0].open);
    let open = view.deliveries.expect("open");
    assert_eq!(open.webhook_id, BOOKINGS);
    assert_eq!(open.status, district_ffi::LoadStatus::Loading);

    let read = deliveries(signed_in());
    let open = developer_view(&read).deliveries.expect("open");
    let outcomes: Vec<(&str, &str, &str, RunTone)> = open
        .rows
        .iter()
        .map(|d| (d.id.as_str(), d.event.as_str(), d.outcome.as_str(), d.tone))
        .collect();
    // The fixture's first delivery says "delivered", which the core's words
    // do not know: shown as it came.
    assert_eq!(
        outcomes,
        [
            (
                "whd-contract-1",
                "booking.created",
                "delivered",
                RunTone::Neutral
            ),
            (
                "whd-contract-2",
                "recording.completed",
                "No answer",
                RunTone::Danger
            ),
        ]
    );
    assert!(!open.rows[0].when.is_empty());
    assert_eq!(open.empty, "Nothing delivered yet.");
    let closed = developer_view(&read.ui(event(DeveloperAction::ToggleDeliveries {
        webhook_id: BOOKINGS.to_owned(),
    })));
    assert_eq!(closed.deliveries, None);
    assert!(!closed.webhooks[0].open);

    let failed = developer_view(&deliveries_read(
        deliveries_open(signed_in()),
        Err(server_error()),
    ));
    assert!(matches!(
        failed.deliveries.expect("open").status,
        district_ffi::LoadStatus::Failed { .. }
    ));
}

/// Adding a webhook: its address and its events checked before anything is
/// sent; a person's own data left out until chosen, and marked; once added,
/// its signing secret shown once and nowhere else. Changing one: its address
/// fixed, a save that says what it came to, a refusal that keeps the form.
#[test]
fn a_webhook_is_added_and_changed() {
    let open = developer_view(&adding(signed_in()))
        .webhook_dialog
        .expect("open");
    assert_eq!(open.dialog.title, "Add webhook");
    assert_eq!(open.dialog.confirm, "Add webhook");
    assert!(!open.dialog.can_confirm, "no event yet");
    assert!(open.creating);
    assert_eq!(open.url_label, "Endpoint URL");
    assert_eq!(open.url_hint, "Where each delivery is posted. https only.");
    assert_eq!(open.events.len(), 7);
    assert!(open.events.iter().all(|e| !e.on && e.mark.is_none()));
    let personal: Vec<(&str, bool)> = open
        .fields
        .iter()
        .filter(|f| f.mark.is_some())
        .map(|f| (f.value.as_str(), f.on))
        .collect();
    assert_eq!(
        personal,
        [
            ("host_name", true),
            ("host_email", true),
            ("attendee_name", false),
            ("attendee_email", false),
            ("attendee_timezone", false),
            ("answers", false)
        ]
    );
    assert_eq!(open.fields[0].mark, None);
    assert_eq!(open.fields[12].mark.as_deref(), Some("Personal data"));

    let refused = |url: &str| {
        let session = adding(signed_in())
            .ui(event(DeveloperAction::EditWebhookUrl {
                url: url.to_owned(),
            }))
            .ui(event(DeveloperAction::ToggleEvent {
                event: "booking.created".to_owned(),
            }))
            .ui(event(DeveloperAction::SaveWebhook));
        assert!(!sent(&session, |r| matches!(
            r,
            SchedulingAdminRequest::CreateWebhook { .. }
        )));
        developer_view(&session)
            .webhook_dialog
            .expect("open")
            .url_error
    };
    assert_eq!(
        refused("http://hooks.example.com/in").as_deref(),
        Some("The URL has to start with https://.")
    );
    assert_eq!(
        refused("https://192.168.0.4/in").as_deref(),
        Some("That address is not reachable from the internet. Use a public https URL.")
    );
    let chosen = developer_view(&adding(signed_in()).ui(event(DeveloperAction::ToggleEvent {
        event: "booking.reminder".to_owned(),
    })))
    .webhook_dialog
    .expect("open");
    assert!(chosen.events[3].on && chosen.dialog.can_confirm);

    let made = added(signed_in());
    let open = developer_view(&made).webhook_dialog.expect("still open");
    assert_eq!(open.dialog.title, "Your signing secret");
    assert_eq!(open.dialog.confirm, "");
    assert_eq!(open.dialog.close, "Done");
    let shown = open.minted.clone().expect("shown once");
    assert_eq!(shown.value, minted_webhook_secret().as_bytes());
    assert_eq!(shown.copy_label, "Copy signing secret");
    assert_eq!(
        shown.note.as_deref(),
        Some(
            "Every delivery to https://hooks.contract.test/bookings is signed with it, in the X-Calnode-Signature header."
        )
    );
    assert!(!format!("{open:?}").contains(&minted_webhook_secret()));
    let json = serde_json::to_string(&screen_view(&made.model)).unwrap();
    assert!(!json.contains(&minted_webhook_secret()), "{json}");

    // Changing the first webhook: its address fixed, its form as stored.
    let open = developer_view(&editing(signed_in()))
        .webhook_dialog
        .expect("open");
    assert_eq!(open.dialog.title, "Edit webhook");
    assert_eq!(open.dialog.confirm, "Save changes");
    assert!(!open.creating);
    assert_eq!(open.url, "https://hooks.contract.test/bookings");
    assert_eq!(
        open.url_hint,
        "The URL cannot be changed. Delete this webhook and add another to move it."
    );
    assert_eq!(open.dropped, None);
    let on: Vec<&str> = open
        .events
        .iter()
        .filter(|e| e.on)
        .map(|e| e.value.as_str())
        .collect();
    assert_eq!(on, ["booking.created", "booking.cancelled"]);
    let pending = saving(signed_in());
    assert!(sent(&pending, |r| matches!(
        r,
        SchedulingAdminRequest::PatchWebhook { id, events: Some(events), .. }
            if id == BOOKINGS && events.len() == 3
    )));
    let open = developer_view(&pending).webhook_dialog.expect("open");
    assert!(open.dialog.working && !open.editable);
    assert_eq!(open.dialog.confirm, "Saving…");
    let done = developer_view(&saved(signed_in()))
        .webhook_dialog
        .expect("still open");
    assert_eq!(done.notice.as_deref(), Some("Webhook updated"));
    let refused = developer_view(&webhook_answer(saving(signed_in()), Err(server_error())))
        .webhook_dialog
        .expect("still open");
    assert!(refused.dialog.failure.is_some() && refused.editable);
    assert!(refused.events[2].on, "the change is kept");

    // Deleting a webhook is asked once more.
    let asked = webhooks(signed_in()).ui(event(DeveloperAction::AskDeleteWebhook {
        webhook_id: RECORDINGS.to_owned(),
    }));
    let ask = developer_view(&asked).removal.expect("asked");
    assert_eq!(ask.title, "Delete this webhook?");
    assert_eq!(ask.confirm, "Delete webhook");
    assert!(ask.destructive);
    let deleting = asked.ui(event(DeveloperAction::ConfirmRemoval));
    assert!(sent(&deleting, |r| matches!(
        r,
        SchedulingAdminRequest::DeleteWebhook { id } if id == RECORDINGS
    )));
}

/// No projection ever holds a key or a signing secret: every snapshot
/// case's screen and shell, the ones that made them among them.
#[test]
fn no_projection_holds_a_key_or_a_secret() {
    let cases = crate::cases();
    for made in [
        "scheduling-admin-developer-key-made",
        "scheduling-admin-developer-webhook-added",
    ] {
        assert!(cases.iter().any(|(name, _)| *name == made), "{made}");
    }
    let (key, secret) = (minted_key(), minted_webhook_secret());
    for (name, session) in cases {
        let screen = serde_json::to_string(&screen_view(&session.model)).unwrap();
        let shell = serde_json::to_string(&district_ffi::shell_view(&session.model)).unwrap();
        for value in [&key, &secret] {
            assert!(!screen.contains(value.as_str()), "{name}: {screen}");
            assert!(!shell.contains(value.as_str()), "{name}: {shell}");
        }
    }
}

#[test]
fn each_action_is_its_core_event() {
    use DeveloperAction as A;
    use DeveloperEvent as E;
    let s = |v: &str| v.to_owned();
    let to_core = |e| Event::SchedulingAdmin(SchedulingAdminEvent::Developer(e));
    for (sent, event_sent) in [
        (
            A::SelectTab {
                tab: DeveloperTabView::Keys,
            },
            E::SelectTab(DeveloperTab::Keys),
        ),
        (
            A::SelectTab {
                tab: DeveloperTabView::Apps,
            },
            E::SelectTab(DeveloperTab::Apps),
        ),
        (
            A::SelectTab {
                tab: DeveloperTabView::Webhooks,
            },
            E::SelectTab(DeveloperTab::Webhooks),
        ),
        (A::Refresh, E::Refresh),
        (
            A::ToggleDeliveries { webhook_id: s("w") },
            E::ToggleDeliveries(s("w")),
        ),
        (A::OpenCreateKey, E::OpenCreateKey),
        (A::EditKeyName { name: s("n") }, E::EditKeyName(s("n"))),
        (A::CreateKey, E::CreateKey),
        (A::KeyCopied, E::KeyCopied),
        (A::AskRevokeKey { key_id: s("k") }, E::AskRevokeKey(s("k"))),
        (
            A::AskRevokeApp {
                connection_id: s("a"),
            },
            E::AskRevokeApp(s("a")),
        ),
        (
            A::AskDeleteWebhook { webhook_id: s("w") },
            E::AskDeleteWebhook(s("w")),
        ),
        (A::CancelRemoval, E::CancelRemoval),
        (A::ConfirmRemoval, E::ConfirmRemoval),
        (A::OpenCreateWebhook, E::OpenCreateWebhook),
        (
            A::OpenEditWebhook { webhook_id: s("w") },
            E::OpenEditWebhook(s("w")),
        ),
        (A::EditWebhookUrl { url: s("u") }, E::EditWebhookUrl(s("u"))),
        (A::ToggleEvent { event: s("e") }, E::ToggleEvent(s("e"))),
        (A::ToggleField { field: s("f") }, E::ToggleField(s("f"))),
        (A::SaveWebhook, E::SaveWebhook),
        (A::CloseDialog, E::CloseDialog),
    ] {
        assert_eq!(event(sent).events(), [to_core(event_sent)]);
    }
    // The tab goes back the way it came.
    for tab in DeveloperTab::ALL {
        assert_eq!(DeveloperTab::from(DeveloperTabView::from(tab)), tab);
    }
}
