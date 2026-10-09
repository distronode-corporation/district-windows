//! The Members section (src/settings/members.rs).

use district_api::{ApiError, ErrorDetail};
use district_core::{Effect, Event, MembersEvent, Route, WorkspaceSection};
use district_ffi::settings::SectionStatus;
use district_ffi::settings::members::{MemberRoleView, MembersAction, MembersView};
use district_ffi::{ScreenView, UiEvent, screen_view};
use district_model::MemberRole;
use serde_json::{Value, json};

use super::super::{
    Case, Session, contracts, route, server_error, signed_in, signed_in_to, viewer,
};

/// This area's snapshot cases.
pub(crate) fn cases() -> Vec<Case> {
    vec![
        ("members-loading", open(signed_in())),
        ("members-agency", ready(signed_in())),
        (
            "members-failed",
            listed(open(signed_in()), Err(server_error())),
        ),
        ("members-client", ready(as_client())),
        (
            "members-add-typed",
            typed(ready(signed_in()), "New.Person@Example.com "),
        ),
        (
            "members-add-not-an-address",
            act(
                typed(ready(signed_in()), "not an address"),
                MembersAction::Add,
            ),
        ),
        ("members-adding", adding()),
        ("members-added", written(adding(), Ok(()))),
        (
            "members-add-refused-already-a-member",
            written(adding(), Err(refusal("district-member-duplicate.json"))),
        ),
        ("members-role-changing", demoting()),
        (
            "members-role-refused-last-agency",
            written(demoting(), Err(refusal("district-member-last-agency.json"))),
        ),
        ("members-confirm-remove", asked()),
        (
            "members-removing",
            act(asked(), MembersAction::ConfirmRemove),
        ),
        ("members-renamed", renamed(Ok("district-rename.json"))),
        ("members-rename-failed", renamed(Err(()))),
    ]
}

fn act(session: Session, action: MembersAction) -> Session {
    session.ui(UiEvent::Members { action })
}

fn open(session: Session) -> Session {
    act(session, MembersAction::Open)
}

fn listed(session: Session, result: Result<Value, ApiError>) -> Session {
    session.answer(
        |e| matches!(e, Effect::LoadMembers { .. }),
        |ticket| Event::MembersLoaded {
            ticket,
            result: result.map(|value| contracts::decode("members", value)),
        },
    )
}

fn members() -> Value {
    contracts::json("district-members.json")
}

fn ready(session: Session) -> Session {
    listed(open(session), Ok(members()))
}

/// Signed in to one workspace as a client: may rename it, not manage members.
fn as_client() -> Session {
    signed_in_to(Ok(contracts::decode(
        "one workspace",
        json!({
            "success": true,
            "workspaces": [{
                "id": "ws-1",
                "name": "Example Dental",
                "region": "us",
                "role": "client",
                "subscriptionTier": "VoicePro"
            }],
            "total": 1,
            "limit": 100,
            "offset": 0,
            "degradedRegions": [],
            "inactiveCount": 0,
            "defaultWorkspaceId": "ws-1"
        }),
    )))
}

fn typed(session: Session, email: &str) -> Session {
    act(
        session,
        MembersAction::EditEmail {
            email: email.to_owned(),
        },
    )
}

/// An address added as a viewer, on its way.
fn adding() -> Session {
    let session = act(
        typed(ready(signed_in()), "New.Person@Example.com "),
        MembersAction::SetRole {
            role: MemberRoleView::Viewer,
        },
    );
    act(session, MembersAction::Add)
}

/// The founder, the only agency member, made a client: on its way.
fn demoting() -> Session {
    act(
        ready(signed_in()),
        MembersAction::ChangeRole {
            email: "founder@example.com".to_owned(),
            role: MemberRoleView::Client,
        },
    )
}

fn asked() -> Session {
    act(
        ready(signed_in()),
        MembersAction::AskRemove {
            email: "auditor@example.com".to_owned(),
        },
    )
}

/// The service's refusal in `fixture`, as the API client reads a 409.
fn refusal(fixture: &str) -> ApiError {
    let body = contracts::json(fixture);
    ApiError::Conflict(ErrorDetail {
        message: body["error"].as_str().map(str::to_owned),
        code: body["code"].as_str().map(str::to_owned),
        degraded_regions: Vec::new(),
    })
}

/// A change of a member answered `result`, and the list read again after it.
fn written(session: Session, result: Result<(), ApiError>) -> Session {
    session
        .answer(
            |e| matches!(e, Effect::WriteMember { .. }),
            |ticket| Event::SettingsWritten { ticket, result },
        )
        .answer(
            |e| matches!(e, Effect::LoadMembers { .. }),
            |ticket| Event::MembersLoaded {
                ticket,
                result: Ok(contracts::read("district-members.json")),
            },
        )
}

/// The workspace renamed: answered with the fixture, or failed.
fn renamed(result: Result<&str, ()>) -> Session {
    let session = act(
        act(
            ready(signed_in()),
            MembersAction::EditName {
                name: "  Renamed Workspace ".to_owned(),
            },
        ),
        MembersAction::Rename,
    );
    session.answer(
        |e| matches!(e, Effect::RenameWorkspace { .. }),
        |ticket| Event::WorkspaceRenamed {
            ticket,
            result: result.map(contracts::read).map_err(|()| server_error()),
        },
    )
}

fn view(session: &Session) -> MembersView {
    match screen_view(&session.model) {
        ScreenView::Members { view } => view,
        other => panic!("not the members: {other:?}"),
    }
}

#[test]
fn each_action_is_its_core_event() {
    let members = |event| Event::Members(event);
    for (action, event) in [
        (
            MembersAction::Open,
            Event::Navigate(Route::Workspace(WorkspaceSection::Members)),
        ),
        (
            MembersAction::EditEmail {
                email: "a@example.com".to_owned(),
            },
            members(MembersEvent::EditEmail("a@example.com".to_owned())),
        ),
        (
            MembersAction::SetRole {
                role: MemberRoleView::Agency,
            },
            members(MembersEvent::SetRole(MemberRole::Agency)),
        ),
        (MembersAction::Add, members(MembersEvent::Add)),
        (
            MembersAction::ChangeRole {
                email: "a@example.com".to_owned(),
                role: MemberRoleView::Viewer,
            },
            members(MembersEvent::ChangeRole {
                email: "a@example.com".to_owned(),
                role: MemberRole::Viewer,
            }),
        ),
        (
            MembersAction::AskRemove {
                email: "a@example.com".to_owned(),
            },
            members(MembersEvent::AskRemove {
                email: "a@example.com".to_owned(),
            }),
        ),
        (
            MembersAction::ConfirmRemove,
            members(MembersEvent::ConfirmRemove),
        ),
        (
            MembersAction::CancelRemove,
            members(MembersEvent::CancelRemove),
        ),
        (
            MembersAction::EditName {
                name: "N".to_owned(),
            },
            members(MembersEvent::EditName("N".to_owned())),
        ),
        (MembersAction::Rename, members(MembersEvent::Rename)),
        (
            MembersAction::DismissNotice,
            members(MembersEvent::DismissNotice),
        ),
    ] {
        assert_eq!(UiEvent::Members { action }.events(), [event]);
    }
}

/// An agency member manages members and renames; each listed address is the
/// service's own.
#[test]
fn an_agency_member_manages_members() {
    let shown = view(&ready(signed_in()));
    assert_eq!(shown.status, SectionStatus::Ready);
    assert_eq!(shown.title, "Members");
    let emails: Vec<&str> = shown.members.iter().map(|m| m.email.as_str()).collect();
    assert_eq!(
        emails,
        [
            "founder@example.com",
            "operator@example.com",
            "auditor@example.com"
        ]
    );
    assert!(shown.members.iter().all(|m| m.can_change));
    assert!(shown.add.is_some() && shown.rename.is_some());
    assert_eq!(shown.read_only_note, None);
    assert!(
        shown.add.unwrap().note.contains("no invitation"),
        "adding sends no invitation, and says so"
    );
}

/// A client sees who belongs here and may rename, never change a member: the
/// core refuses even if asked.
#[test]
fn a_client_reads_the_members_and_renames() {
    let session = ready(as_client());
    let shown = view(&session);
    assert!(shown.read_only_note.is_some());
    assert!(shown.members.iter().all(|m| !m.can_change));
    assert!(shown.add.is_none());
    assert!(shown.rename.is_some());
    let before = session.pending.len();
    let refused = act(
        session,
        MembersAction::AskRemove {
            email: "auditor@example.com".to_owned(),
        },
    );
    assert_eq!(view(&refused).confirm_remove, None);
    assert_eq!(refused.pending.len(), before);
}

/// A viewer is not offered the section, and the core keeps it from them.
#[test]
fn a_viewer_never_reaches_the_members() {
    let session = open(viewer());
    assert_ne!(route(&session), Route::Workspace(WorkspaceSection::Members));
    assert!(!matches!(
        screen_view(&session.model),
        ScreenView::Members { .. }
    ));
}

/// The address is sent as the service stores it, and the two refusals are the
/// service's words with no retry.
#[test]
fn an_add_goes_trimmed_and_a_refusal_says_why() {
    let session = adding();
    let sent = session
        .pending
        .iter()
        .find_map(|effect| match effect {
            Effect::WriteMember { write, .. } => Some(format!("{write:?}")),
            _ => None,
        })
        .expect("the add is on its way");
    assert!(sent.contains("new.person@example.com"), "{sent}");
    assert!(view(&session).changing);
    let refused = view(&written(
        adding(),
        Err(refusal("district-member-duplicate.json")),
    ));
    let notice = refused.notice.expect("a notice");
    assert!(!notice.saved);
    assert_eq!(
        notice.message,
        "That email is already a member of this workspace"
    );
    let last = view(&written(
        demoting(),
        Err(refusal("district-member-last-agency.json")),
    ));
    assert!(last.notice.unwrap().message.contains("last agency member"));
    let added = view(&written(adding(), Ok(())));
    assert_eq!(added.add.unwrap().email, "", "the box empties once added");
}

/// Removing asks first; Cancel keeps the member, and nothing is sent.
#[test]
fn removing_asks_first() {
    let question = view(&asked()).confirm_remove.expect("asked");
    assert_eq!(question.email, "auditor@example.com");
    assert!(
        question
            .body
            .starts_with("auditor@example.com loses access")
    );
    let kept = act(asked(), MembersAction::CancelRemove);
    assert_eq!(view(&kept).confirm_remove, None);
    assert!(
        !kept
            .pending
            .iter()
            .any(|e| matches!(e, Effect::WriteMember { .. }))
    );
}

#[test]
fn a_rename_shows_the_stored_name() {
    let rename = view(&renamed(Ok("district-rename.json"))).rename.unwrap();
    assert_eq!(rename.current, "Renamed Workspace");
    assert_eq!(rename.new_name, "");
    let failed = view(&renamed(Err(())));
    assert_eq!(failed.rename.unwrap().current, "Example Dental");
    assert!(!failed.notice.unwrap().saved);
}
