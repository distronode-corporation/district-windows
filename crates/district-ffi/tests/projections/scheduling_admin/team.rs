//! The team (src/scheduling_admin/team.rs): the members beside the
//! scheduler's users, who left while still a host, the teams, a team's name,
//! its hosts, and an account's archive.
//!
//! The scheduler's users are the fixture's: Contract Member, a host, and
//! Former Host, archived. The workspace's members are the fixture's three,
//! with Contract Member added as its agency unless a case says otherwise, so
//! that only the stranded case has someone who left while still a host.

use district_core::SchedulingAdminEvent;
use district_core::scheduling_admin::TeamEvent;
use district_ffi::scheduling_admin::team::{SchedulingTeamView, TeamAction};
use district_ffi::workflows::RunTone;

use super::*;

/// This area's snapshot cases.
pub(crate) fn cases() -> Vec<Case> {
    vec![
        (
            "scheduling-admin-team-loading",
            open(signed_in(), SchedulingAdminSection::Team),
        ),
        ("scheduling-admin-team", team(signed_in())),
        (
            "scheduling-admin-team-stranded",
            read_with(signed_in(), Ok(contracts::json("district-members.json"))),
        ),
        (
            "scheduling-admin-team-roles-unread",
            read_with(signed_in(), Err(server_error())),
        ),
        (
            "scheduling-admin-team-failed",
            users_read(
                open(signed_in(), SchedulingAdminSection::Team),
                Err(server_error()),
            ),
        ),
        (
            "scheduling-admin-team-teams-failed",
            teams_read(
                members_read(
                    users_read(
                        open(signed_in(), SchedulingAdminSection::Team),
                        Ok(contracts::json("district-scheduling-users.json")),
                    ),
                    Ok(members()),
                ),
                Err(server_error()),
            ),
        ),
        ("scheduling-admin-team-viewer", team(viewer())),
        ("scheduling-admin-team-create-open", creating(signed_in())),
        (
            "scheduling-admin-team-create-name-missing",
            creating(signed_in()).ui(event(TeamAction::SaveName)),
        ),
        (
            "scheduling-admin-team-create-saving",
            create_saving(signed_in()),
        ),
        ("scheduling-admin-team-created", created(signed_in())),
        ("scheduling-admin-team-rename-open", renaming(signed_in())),
        (
            "scheduling-admin-team-delete-asked",
            renaming(signed_in()).ui(event(TeamAction::AskDelete)),
        ),
        (
            "scheduling-admin-team-delete-refused",
            delete_answer(
                renaming(signed_in())
                    .ui(event(TeamAction::AskDelete))
                    .ui(event(TeamAction::ConfirmDelete)),
                Err(server_error()),
            ),
        ),
        (
            "scheduling-admin-team-hosts-loading",
            team(signed_in()).ui(event(TeamAction::OpenMembers {
                team_id: SALES.to_owned(),
            })),
        ),
        ("scheduling-admin-team-hosts", hosts(signed_in(), SALES)),
        ("scheduling-admin-team-hosts-picked", picked(signed_in())),
        ("scheduling-admin-team-host-added", added(signed_in())),
        (
            "scheduling-admin-team-priority-invalid",
            priority(signed_in(), "first").ui(event(TeamAction::SavePriority {
                user_id: ROTATION.to_owned(),
            })),
        ),
        (
            "scheduling-admin-team-priority-saving",
            priority_saving(signed_in()),
        ),
        (
            "scheduling-admin-team-remove-asked",
            remove_asked(signed_in()),
        ),
        ("scheduling-admin-team-host-removed", removed(signed_in())),
        (
            "scheduling-admin-team-archive-counting",
            archiving(signed_in()),
        ),
        (
            "scheduling-admin-team-archive-blocked",
            upcoming(
                archiving(signed_in()),
                Ok(contracts::json("district-scheduling-user-upcoming.json")),
            ),
        ),
        (
            "scheduling-admin-team-archive-asked",
            archive_open(signed_in()).ui(event(TeamAction::AskArchive)),
        ),
        ("scheduling-admin-team-archived", archived(signed_in())),
        (
            "scheduling-admin-team-archive-refused",
            archive_answer(archive_confirmed(signed_in()), Err(server_error())),
        ),
    ]
}

/// The fixture's team with two hosts.
const SALES: &str = "team-contract-1";

/// The fixture's team with none.
const SUPPORT: &str = "team-contract-2";

/// The member's own scheduler account: a host, in Sales.
const CONTRACT: &str = "sched-user-contract";

/// Sales's second host, at routing priority 5.
const ROTATION: &str = "sched-user-rotation";

fn event(action: TeamAction) -> UiEvent {
    UiEvent::SchedulingAdmin {
        action: SchedulingAdminAction::Team { action },
    }
}

/// The workspace's members, with Contract Member as its agency.
fn members() -> Value {
    let mut value = contracts::json("district-members.json");
    value["members"]
        .as_array_mut()
        .expect("the fixture lists members")
        .push(json!({
            "email": "Contract@Example.com",
            "role": "agency",
            "createdAt": "2026-08-15T14:00:00.000Z"
        }));
    value
}

/// The newest read of the section's users, answered with `result`.
fn users_read(session: Session, result: Result<Value, ApiError>) -> Session {
    answer_admin(
        session,
        |r| {
            matches!(
                r,
                SchedulingAdminRequest::Users {
                    include_archived: Some(true)
                }
            )
        },
        result,
    )
}

/// The newest read of the workspace's members, answered with `result`.
fn members_read(session: Session, result: Result<Value, ApiError>) -> Session {
    session.answer(
        |e| matches!(e, Effect::LoadMembers { .. }),
        |ticket| Event::MembersLoaded {
            ticket,
            result: result.map(|value| contracts::decode("members", value)),
        },
    )
}

/// The newest read of the teams, answered with `result`.
fn teams_read(session: Session, result: Result<Value, ApiError>) -> Session {
    answer_admin(
        session,
        |r| matches!(r, SchedulingAdminRequest::Teams),
        result,
    )
}

/// The section's three reads answered, the workspace's members with
/// `workspace`.
fn read_with(session: Session, workspace: Result<Value, ApiError>) -> Session {
    let session = users_read(
        open(session, SchedulingAdminSection::Team),
        Ok(contracts::json("district-scheduling-users.json")),
    );
    teams_read(
        members_read(session, workspace),
        Ok(contracts::json("district-scheduling-teams.json")),
    )
}

/// The section read again after a write: users, members and teams.
fn reread(session: Session) -> Session {
    let session = users_read(
        session,
        Ok(contracts::json("district-scheduling-users.json")),
    );
    teams_read(
        members_read(session, Ok(members())),
        Ok(contracts::json("district-scheduling-teams.json")),
    )
}

/// The team, read.
fn team(session: Session) -> Session {
    read_with(session, Ok(members()))
}

/// The create form, open.
fn creating(session: Session) -> Session {
    team(session).ui(event(TeamAction::OpenCreate))
}

/// "Front desk" typed and the create pressed.
fn create_saving(session: Session) -> Session {
    creating(session)
        .ui(event(TeamAction::EditName {
            name: "  Front desk ".to_owned(),
        }))
        .ui(event(TeamAction::SaveName))
}

/// The team created, and the section read again.
fn created(session: Session) -> Session {
    reread(answer_admin(
        create_saving(session),
        |r| matches!(r, SchedulingAdminRequest::CreateTeam { .. }),
        Ok(contracts::json("district-scheduling-team.json")),
    ))
}

/// Support's name, open.
fn renaming(session: Session) -> Session {
    team(session).ui(event(TeamAction::OpenEditor {
        team_id: SUPPORT.to_owned(),
    }))
}

/// The delete, answered with `result`.
fn delete_answer(session: Session, result: Result<Value, ApiError>) -> Session {
    answer_admin(
        session,
        |r| matches!(r, SchedulingAdminRequest::DeleteTeam { .. }),
        result,
    )
}

/// The dialog's read of the people who could be added, answered with
/// `result`.
fn dialog_users(session: Session, result: Result<Value, ApiError>) -> Session {
    answer_admin(
        session,
        |r| {
            matches!(
                r,
                SchedulingAdminRequest::Users {
                    include_archived: None
                }
            )
        },
        result,
    )
}

/// The hosts of `team_id`, open, with the people who could be added read.
fn hosts(session: Session, team_id: &str) -> Session {
    dialog_users(
        team(session).ui(event(TeamAction::OpenMembers {
            team_id: team_id.to_owned(),
        })),
        Ok(contracts::json("district-scheduling-users.json")),
    )
}

/// Support's hosts, with Contract Member picked.
fn picked(session: Session) -> Session {
    hosts(session, SUPPORT).ui(event(TeamAction::Pick {
        user_id: CONTRACT.to_owned(),
    }))
}

/// A team write, answered with `result`.
fn team_write(session: Session, result: Result<Value, ApiError>) -> Session {
    answer_admin(
        session,
        |r| {
            matches!(
                r,
                SchedulingAdminRequest::AddTeamMember { .. }
                    | SchedulingAdminRequest::PatchTeamMember { .. }
                    | SchedulingAdminRequest::Team { .. }
            )
        },
        result,
    )
}

/// Contract Member added to Support, and the section read again.
fn added(session: Session) -> Session {
    let mut support = contracts::json("district-scheduling-team.json");
    support["data"]["member_count"] = json!(1);
    support["data"]["members"] = json!([{
        "id": CONTRACT,
        "name": "Contract Member",
        "email": "contract@example.com",
        "routing_priority": 0,
        "archived": false
    }]);
    reread(team_write(
        picked(session).ui(event(TeamAction::AddMember)),
        Ok(support),
    ))
}

/// Sales's hosts, with Rotation Host's priority typed as `value`.
fn priority(session: Session, value: &str) -> Session {
    hosts(session, SALES).ui(event(TeamAction::EditPriority {
        user_id: ROTATION.to_owned(),
        value: value.to_owned(),
    }))
}

/// Rotation Host's priority 2 saved.
fn priority_saving(session: Session) -> Session {
    priority(session, "2").ui(event(TeamAction::SavePriority {
        user_id: ROTATION.to_owned(),
    }))
}

/// Rotation Host's removal, asked about.
fn remove_asked(session: Session) -> Session {
    hosts(session, SALES).ui(event(TeamAction::AskRemove {
        user_id: ROTATION.to_owned(),
    }))
}

/// Rotation Host removed: the team read again answers it, and the section is
/// read again.
fn removed(session: Session) -> Session {
    let session = answer_admin(
        remove_asked(session).ui(event(TeamAction::ConfirmRemove)),
        |r| matches!(r, SchedulingAdminRequest::RemoveTeamMember { .. }),
        Ok(contracts::json("district-scheduling-ok.json")),
    );
    let mut sales = contracts::json("district-scheduling-teams.json")["data"]["items"][0].clone();
    sales["member_count"] = json!(1);
    sales["members"]
        .as_array_mut()
        .expect("Sales has hosts")
        .truncate(1);
    reread(team_write(
        session,
        Ok(json!({ "ok": true, "data": sales })),
    ))
}

/// Contract Member's archive, open: their upcoming bookings being counted.
fn archiving(session: Session) -> Session {
    team(session).ui(event(TeamAction::OpenArchive {
        user_id: CONTRACT.to_owned(),
    }))
}

/// Their upcoming bookings, answered with `result`.
fn upcoming(session: Session, result: Result<Value, ApiError>) -> Session {
    answer_admin(
        session,
        |r| matches!(r, SchedulingAdminRequest::UpcomingBookings { .. }),
        result,
    )
}

/// No upcoming booking.
fn none_upcoming() -> Value {
    json!({ "ok": true, "data": { "items": [] } })
}

/// The archive, with nothing in the way.
fn archive_open(session: Session) -> Session {
    upcoming(archiving(session), Ok(none_upcoming()))
}

/// The archive asked once more, and confirmed.
fn archive_confirmed(session: Session) -> Session {
    archive_open(session)
        .ui(event(TeamAction::AskArchive))
        .ui(event(TeamAction::ConfirmArchive))
}

/// The archive, answered with `result`.
fn archive_answer(session: Session, result: Result<Value, ApiError>) -> Session {
    answer_admin(
        session,
        |r| matches!(r, SchedulingAdminRequest::ArchiveUser { .. }),
        result,
    )
}

/// The account archived, and the section read again.
fn archived(session: Session) -> Session {
    reread(archive_answer(
        archive_confirmed(session),
        Ok(contracts::json("district-scheduling-user-archive.json")),
    ))
}

fn team_view(session: &Session) -> SchedulingTeamView {
    let SchedulingAdminPage::Team { view } = admin_view(session).page else {
        panic!("not the team");
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

/// The members are the workspace's beside the scheduler's users, joined by
/// address: each with their role in the core's words and where they stand,
/// Archive account offered for a live scheduler account; the teams with how
/// many hosts each has, Edit, Hosts and Create team. A viewer sees the same
/// and is offered none of the writes.
#[test]
fn the_members_and_the_teams_show_with_their_writes() {
    let session = team(signed_in());
    assert_eq!(
        route(&session),
        Route::SchedulingAdmin(SchedulingSection::Team)
    );
    let page = admin_view(&session);
    assert_eq!(page.title, "Team");
    assert!(
        page.sections
            .iter()
            .any(|s| s.section == SchedulingAdminSection::Team && s.selected)
    );
    let view = team_view(&session);
    assert_eq!(view.status, district_ffi::LoadStatus::Ready);
    assert_eq!(view.stranded, None);
    assert_eq!(view.members_degraded, None);
    let rows: Vec<[&str; 4]> = view
        .members
        .iter()
        .map(|m| [m.name.as_str(), &m.subtitle, &m.state_label, &m.email])
        .collect();
    assert_eq!(
        rows,
        [
            [
                "founder",
                "founder@example.com · Agency",
                "Not set up yet",
                "founder@example.com"
            ],
            [
                "operator",
                "operator@example.com · Client",
                "Not set up yet",
                "operator@example.com"
            ],
            [
                "auditor",
                "auditor@example.com · Viewer",
                "Not set up yet",
                "auditor@example.com"
            ],
            [
                "Contract Member",
                "Contract@Example.com · Agency",
                "Host",
                "Contract@Example.com"
            ],
            [
                "Former Host",
                "gone@example.com · Removed from the workspace",
                "Archived",
                "gone@example.com"
            ],
        ]
    );
    let offered: Vec<Option<&str>> = view
        .members
        .iter()
        .map(|m| m.archive_user_id.as_deref())
        .collect();
    assert_eq!(offered, [None, None, None, Some(CONTRACT), None]);
    assert_eq!(view.members[3].tone, RunTone::Success);
    assert_eq!(view.members[4].tone, RunTone::Neutral);
    assert_eq!(
        [
            view.stranded_heading.as_str(),
            &view.members_heading,
            &view.members_empty,
            &view.archive_label,
            &view.teams_heading,
            &view.teams_subtitle,
            &view.teams_empty,
            &view.create_label,
            &view.edit_label,
            &view.hosts_label,
        ],
        [
            "Someone who has left still has bookings",
            "Members",
            "Nobody else is in this workspace yet.",
            "Archive account",
            "Teams",
            "A team rotates bookings through its hosts, in routing-priority order.",
            "No teams yet.",
            "Create team",
            "Edit",
            "Hosts"
        ]
    );
    assert_eq!(view.teams_status, district_ffi::LoadStatus::Ready);
    let teams: Vec<[&str; 3]> = view
        .teams
        .iter()
        .map(|t| [t.id.as_str(), &t.name, &t.member_count])
        .collect();
    assert_eq!(
        teams,
        [
            [SALES, "Sales", "2 members"],
            [SUPPORT, "Support", "0 members"]
        ]
    );
    assert!(view.offers_writes && view.offers_create);
    assert_eq!((view.editor, view.hosts, view.archive), (None, None, None));

    // A viewer sees the same people and teams, and none of the writes; the
    // core opens none of their dialogs either.
    let viewer = team(viewer());
    let view = team_view(&viewer);
    assert_eq!(view.members.len(), 5);
    assert_eq!(view.teams.len(), 2);
    assert!(!view.offers_writes && !view.offers_create);
    assert!(view.members.iter().all(|m| m.archive_user_id.is_none()));
    let tried = viewer
        .ui(event(TeamAction::OpenCreate))
        .ui(event(TeamAction::OpenArchive {
            user_id: CONTRACT.to_owned(),
        }));
    let view = team_view(&tried);
    assert_eq!((view.editor, view.archive), (None, None));
    // Back to the booking pages.
    assert_eq!(route(&session.ui(UiEvent::Back)), Route::Scheduling);
}

/// Someone who left while still a host is named at the top; when the
/// workspace's members could not be read, no role is shown, nobody is called
/// stranded, and the section says so; a failed read of the users is a status
/// page, and of the teams a line in their card.
#[test]
fn who_left_is_named_and_unread_roles_are_never_an_accusation() {
    let stranded = team_view(&read_with(
        signed_in(),
        Ok(contracts::json("district-members.json")),
    ));
    assert_eq!(
        stranded.stranded.as_deref(),
        Some(
            "Contract Member is no longer in this workspace but still holds upcoming bookings, so the scheduler kept their account open. Reassign or cancel those bookings, then archive the account."
        )
    );
    assert_eq!(
        stranded.members[3].subtitle,
        "contract@example.com · Removed from the workspace"
    );

    let unread = team_view(&read_with(signed_in(), Err(server_error())));
    assert_eq!(unread.status, district_ffi::LoadStatus::Ready);
    assert_eq!(unread.stranded, None);
    assert_eq!(
        unread.members_degraded.as_deref(),
        Some("The workspace's members could not be read, so roles are not shown.")
    );
    let subtitles: Vec<&str> = unread.members.iter().map(|m| m.subtitle.as_str()).collect();
    assert_eq!(subtitles, ["contract@example.com", "gone@example.com"]);
    assert_eq!(unread.members[0].archive_user_id.as_deref(), Some(CONTRACT));

    let loading = team_view(&open(signed_in(), SchedulingAdminSection::Team));
    assert_eq!(loading.status, district_ffi::LoadStatus::Loading);
    assert_eq!(loading.teams_status, district_ffi::LoadStatus::Loading);
    assert!(loading.members.is_empty() && loading.teams.is_empty());
    assert!(!loading.offers_create && loading.offers_writes);
    // The users read, the members still on their way: still loading.
    let half = users_read(
        open(signed_in(), SchedulingAdminSection::Team),
        Ok(contracts::json("district-scheduling-users.json")),
    );
    assert_eq!(team_view(&half).status, district_ffi::LoadStatus::Loading);

    let failed = team_view(&users_read(
        open(signed_in(), SchedulingAdminSection::Team),
        Err(server_error()),
    ));
    assert!(matches!(
        failed.status,
        district_ffi::LoadStatus::Failed { .. }
    ));
    assert!(failed.members.is_empty());
    // Try again (the window's Refresh) reads the section again.
    let again = users_read(
        open(signed_in(), SchedulingAdminSection::Team),
        Err(server_error()),
    )
    .ui(UiEvent::Refresh);
    assert!(sent(&again, |r| matches!(
        r,
        SchedulingAdminRequest::Users {
            include_archived: Some(true)
        }
    )));
    assert_eq!(team_view(&again).status, district_ffi::LoadStatus::Loading);

    let teams_failed = team_view(&teams_read(
        members_read(half, Ok(members())),
        Err(server_error()),
    ));
    assert_eq!(teams_failed.status, district_ffi::LoadStatus::Ready);
    assert!(matches!(
        teams_failed.teams_status,
        district_ffi::LoadStatus::Failed { .. }
    ));
    assert!(teams_failed.teams.is_empty() && !teams_failed.offers_create);

    // Nobody at all: the members say so.
    let empty = team_view(&teams_read(
        members_read(
            users_read(
                open(signed_in(), SchedulingAdminSection::Team),
                Ok(json!({ "ok": true, "data": [] })),
            ),
            Ok(json!({ "success": true, "members": [] })),
        ),
        Ok(json!({ "ok": true, "data": { "items": [] } })),
    ));
    assert!(empty.members.is_empty() && empty.teams.is_empty());
    assert!(empty.offers_create);
}

/// A team is created from a name checked before anything is sent; the
/// dialog stays open saying it was created, and the section is read again.
/// A team's name is renamed in the same dialog, whose way out is Done, and
/// its delete is apart from the rename and asked once more.
#[test]
fn a_team_is_created_renamed_and_its_delete_is_asked_once_more() {
    let open = team_view(&creating(signed_in()))
        .editor
        .expect("the create form");
    assert_eq!(open.dialog.title, "New team");
    assert_eq!(open.dialog.confirm, "Create team");
    assert_eq!(open.dialog.close, "Cancel");
    assert!(open.dialog.can_confirm && !open.dialog.destructive && open.editable);
    assert_eq!(
        [
            open.hint.as_str(),
            &open.name_label,
            &open.name_placeholder,
            &open.name,
        ],
        [
            "A team rotates bookings through its hosts, in routing-priority order.",
            "Name",
            "Front desk",
            ""
        ]
    );
    assert_eq!(open.slug_hint, None);
    assert!(!open.shows_delete && !open.can_delete);

    // Checked before anything is sent: empty, then too long.
    let missing = creating(signed_in()).ui(event(TeamAction::SaveName));
    assert!(!sent(&missing, |r| matches!(
        r,
        SchedulingAdminRequest::CreateTeam { .. }
    )));
    assert_eq!(
        team_view(&missing)
            .editor
            .expect("open")
            .name_error
            .as_deref(),
        Some("Give the team a name.")
    );
    let long = creating(signed_in())
        .ui(event(TeamAction::EditName {
            name: "x".repeat(201),
        }))
        .ui(event(TeamAction::SaveName));
    assert_eq!(
        team_view(&long).editor.expect("open").name_error.as_deref(),
        Some("That name is longer than 200 characters.")
    );
    let retyped = long.ui(event(TeamAction::EditName {
        name: "Sales".to_owned(),
    }));
    assert_eq!(team_view(&retyped).editor.expect("open").name_error, None);

    let saving = create_saving(signed_in());
    assert!(sent(&saving, |r| matches!(
        r,
        SchedulingAdminRequest::CreateTeam { name, slug: None } if name == "Front desk"
    )));
    let open = team_view(&saving).editor.expect("open");
    assert!(open.dialog.working && !open.dialog.can_confirm && !open.editable);

    let done = created(signed_in());
    let view = team_view(&done);
    let open = view.editor.expect("still open");
    assert_eq!(open.notice.as_deref(), Some("Team created"));
    assert!(open.dialog.can_confirm);
    assert_eq!(view.teams_status, district_ffi::LoadStatus::Ready);
    let closed = team_view(&done.ui(event(TeamAction::CloseDialog)));
    assert_eq!(closed.editor, None);

    let refused = answer_admin(
        create_saving(signed_in()),
        |r| matches!(r, SchedulingAdminRequest::CreateTeam { .. }),
        Err(server_error()),
    );
    let open = team_view(&refused).editor.expect("still open");
    assert!(open.dialog.failure.is_some() && open.editable);
    assert_eq!(open.name, "  Front desk ", "the name is kept");
    // A change puts the failure away.
    let retyped = refused.ui(event(TeamAction::EditName {
        name: "Desk".to_owned(),
    }));
    assert_eq!(
        team_view(&retyped).editor.expect("open").dialog.failure,
        None
    );

    // Renaming Support: the dialog is the team's, Done its way out.
    let session = renaming(signed_in());
    let open = team_view(&session).editor.expect("the team's name");
    assert_eq!(open.dialog.title, "Support");
    assert_eq!(open.dialog.confirm, "Save name");
    assert_eq!(open.dialog.close, "Done");
    assert_eq!(open.name, "Support");
    assert_eq!(
        open.slug_hint.as_deref(),
        Some("The booking-page address cannot be changed here.")
    );
    assert!(open.shows_delete && open.can_delete);
    assert_eq!(open.delete_label, "Delete team");
    assert_eq!(open.delete, None);
    let renamed = reread(answer_admin(
        session
            .ui(event(TeamAction::EditName {
                name: "Support desk".to_owned(),
            }))
            .ui(event(TeamAction::SaveName)),
        |r| {
            matches!(
                r,
                SchedulingAdminRequest::PatchTeam { id, name: Some(name), .. }
                    if id == SUPPORT && name == "Support desk"
            )
        },
        Ok(contracts::json("district-scheduling-team.json")),
    ));
    assert_eq!(
        team_view(&renamed).editor.expect("open").notice.as_deref(),
        Some("Team renamed")
    );

    // The delete, asked once more, then kept; then confirmed.
    let asked = renaming(signed_in()).ui(event(TeamAction::AskDelete));
    let ask = team_view(&asked)
        .editor
        .expect("open")
        .delete
        .expect("asked");
    assert_eq!(ask.title, "Delete Support?");
    assert_eq!(
        ask.body,
        "Event types that route to this team stop rotating through it."
    );
    assert_eq!(
        (ask.confirm.as_str(), ask.cancel.as_str()),
        ("Delete team", "Keep team")
    );
    let kept = asked.ui(event(TeamAction::CancelDelete));
    assert_eq!(team_view(&kept).editor.expect("open").delete, None);
    let deleting = kept
        .ui(event(TeamAction::AskDelete))
        .ui(event(TeamAction::ConfirmDelete));
    assert!(sent(&deleting, |r| matches!(
        r,
        SchedulingAdminRequest::DeleteTeam { id } if id == SUPPORT
    )));
    let open = team_view(&deleting).editor.expect("open");
    assert!(!open.can_delete && open.delete.is_none() && open.dialog.working);
    let deleted = reread(delete_answer(
        deleting,
        Ok(contracts::json("district-scheduling-ok.json")),
    ));
    assert_eq!(
        team_view(&deleted).editor.expect("open").notice.as_deref(),
        Some("Team deleted")
    );
    let refused = delete_answer(
        renaming(signed_in())
            .ui(event(TeamAction::AskDelete))
            .ui(event(TeamAction::ConfirmDelete)),
        Err(server_error()),
    );
    let open = team_view(&refused).editor.expect("open");
    assert!(open.dialog.failure.is_some() && open.can_delete);
}

/// A team's hosts, from the team as answered: each with their priority, a
/// priority that is not a whole number refused before anything is sent;
/// the people who could be added are read for the dialog, one picked and
/// added by its confirm; a removal is asked once more; each write says what
/// it came to and the section is read again.
#[test]
fn a_teams_hosts_are_added_reordered_and_removed_asked_once_more() {
    let loading = team_view(&team(signed_in()).ui(event(TeamAction::OpenMembers {
        team_id: SALES.to_owned(),
    })))
    .hosts
    .expect("open");
    assert_eq!(loading.users_status, district_ffi::LoadStatus::Loading);
    assert!(loading.candidates.is_empty() && loading.add_none.is_none());
    assert_eq!(loading.hosts.len(), 2, "the team as answered");

    let session = hosts(signed_in(), SALES);
    let open = team_view(&session).hosts.expect("open");
    assert_eq!(open.dialog.title, "Sales");
    assert_eq!(open.dialog.confirm, "Add member");
    assert_eq!(open.dialog.close, "Done");
    assert!(!open.dialog.can_confirm, "nobody picked");
    assert_eq!(open.team_id, SALES);
    let rows: Vec<[&str; 4]> = open
        .hosts
        .iter()
        .map(|h| [h.user_id.as_str(), &h.name, &h.email, &h.priority])
        .collect();
    assert_eq!(
        rows,
        [
            [CONTRACT, "Contract Member", "contract@example.com", "0"],
            [ROTATION, "Rotation Host", "rotation@example.com", "5"]
        ]
    );
    assert_eq!(
        [
            open.hint.as_str(),
            &open.empty,
            &open.priority_label,
            &open.save_priority_label,
            &open.remove_label,
            &open.add_label,
            &open.add_placeholder,
        ],
        [
            "A lower routing priority is offered a booking first.",
            "No hosts yet. Nothing routes through this team until one is added.",
            "Routing priority",
            "Save priority",
            "Remove",
            "Host",
            "Choose a host"
        ]
    );
    // Everyone not archived is in Sales already.
    assert!(open.candidates.is_empty());
    assert_eq!(
        open.add_none.as_deref(),
        Some("Every host is already in this team.")
    );
    assert!(open.editable && open.remove.is_none());

    // Support has nobody: Contract Member can be added, once picked.
    let support = team_view(&hosts(signed_in(), SUPPORT)).hosts.expect("open");
    assert!(support.hosts.is_empty() && support.add_none.is_none());
    let candidates: Vec<(&str, &str, bool)> = support
        .candidates
        .iter()
        .map(|c| (c.value.as_str(), c.label.as_str(), c.selected))
        .collect();
    assert_eq!(candidates, [(CONTRACT, "Contract Member", false)]);
    let chosen = team_view(&picked(signed_in())).hosts.expect("open");
    assert!(chosen.candidates[0].selected && chosen.dialog.can_confirm);
    let adding = picked(signed_in()).ui(event(TeamAction::AddMember));
    assert!(sent(&adding, |r| matches!(
        r,
        SchedulingAdminRequest::AddTeamMember { id, user_id, routing_priority: None }
            if id == SUPPORT && user_id == CONTRACT
    )));
    let open = team_view(&adding).hosts.expect("open");
    assert!(open.dialog.working && !open.dialog.can_confirm && !open.editable);
    let done = added(signed_in());
    let view = team_view(&done);
    let open = view.hosts.expect("still open");
    assert_eq!(open.notice.as_deref(), Some("Host added to the team"));
    assert_eq!(open.hosts.len(), 1, "the team as answered");
    assert_eq!(view.teams_status, district_ffi::LoadStatus::Ready);
    // Add pressed with nobody picked says why.
    let nobody = hosts(signed_in(), SUPPORT).ui(event(TeamAction::AddMember));
    assert_eq!(
        team_view(&nobody)
            .hosts
            .expect("open")
            .dialog
            .failure
            .map(|f| f.message)
            .as_deref(),
        Some("Choose a host to add.")
    );

    // A priority that is not a whole number is refused before anything is
    // sent; a change puts the refusal away.
    let invalid = priority(signed_in(), "first").ui(event(TeamAction::SavePriority {
        user_id: ROTATION.to_owned(),
    }));
    assert!(!sent(&invalid, |r| matches!(
        r,
        SchedulingAdminRequest::PatchTeamMember { .. }
    )));
    let open = team_view(&invalid).hosts.expect("open");
    assert_eq!(
        open.priority_error.as_deref(),
        Some("A routing priority is a whole number, zero or more.")
    );
    assert_eq!(open.hosts[1].priority, "first", "as typed");
    let retyped = invalid.ui(event(TeamAction::EditPriority {
        user_id: ROTATION.to_owned(),
        value: "3".to_owned(),
    }));
    assert_eq!(
        team_view(&retyped).hosts.expect("open").priority_error,
        None
    );
    let saving = priority_saving(signed_in());
    assert!(sent(&saving, |r| matches!(
        r,
        SchedulingAdminRequest::PatchTeamMember { user_id, routing_priority: 2, .. }
            if user_id == ROTATION
    )));
    let saved = reread(team_write(
        saving,
        Ok(contracts::json("district-scheduling-teams.json")["data"]["items"][0].clone())
            .map(|team| json!({ "ok": true, "data": team })),
    ));
    let open = team_view(&saved).hosts.expect("open");
    assert_eq!(open.notice.as_deref(), Some("Routing priority saved"));
    assert_eq!(open.hosts[1].priority, "5", "the team as answered");
    let refused = team_write(priority_saving(signed_in()), Err(server_error()));
    let open = team_view(&refused).hosts.expect("open");
    assert!(open.dialog.failure.is_some() && open.editable);
    assert_eq!(open.hosts[1].priority, "2", "the priority is kept");

    // A removal is asked once more, and kept; then confirmed.
    let asked = remove_asked(signed_in());
    let ask = team_view(&asked)
        .hosts
        .expect("open")
        .remove
        .expect("asked");
    assert_eq!(ask.title, "Remove this host?");
    assert_eq!(
        ask.body,
        "Bookings stop routing to them through this team. Their routing priority is not kept."
    );
    assert_eq!(
        (ask.confirm.as_str(), ask.cancel.as_str()),
        ("Remove", "Keep them")
    );
    let kept = asked.ui(event(TeamAction::CancelRemove));
    assert_eq!(team_view(&kept).hosts.expect("open").remove, None);
    let removing = remove_asked(signed_in()).ui(event(TeamAction::ConfirmRemove));
    assert!(sent(&removing, |r| matches!(
        r,
        SchedulingAdminRequest::RemoveTeamMember { id, user_id }
            if id == SALES && user_id == ROTATION
    )));
    let open = team_view(&removing).hosts.expect("open");
    assert!(open.remove.is_none() && open.dialog.working);
    let gone = removed(signed_in());
    let open = team_view(&gone).hosts.expect("open");
    assert_eq!(open.notice.as_deref(), Some("Host removed from the team"));
    assert_eq!(open.hosts.len(), 1);
    let refused = answer_admin(
        remove_asked(signed_in()).ui(event(TeamAction::ConfirmRemove)),
        |r| matches!(r, SchedulingAdminRequest::RemoveTeamMember { .. }),
        Err(server_error()),
    );
    let open = team_view(&refused).hosts.expect("open");
    assert!(open.dialog.failure.is_some() && open.hosts.len() == 2);

    // A read of the people who could be added that failed says why.
    let failed = dialog_users(
        team(signed_in()).ui(event(TeamAction::OpenMembers {
            team_id: SUPPORT.to_owned(),
        })),
        Err(server_error()),
    );
    let open = team_view(&failed).hosts.expect("open");
    assert!(matches!(
        open.users_status,
        district_ffi::LoadStatus::Failed { .. }
    ));
    assert!(open.candidates.is_empty() && open.add_none.is_none());
}

/// An account is archived only once its upcoming bookings were read and
/// there are none: until then the dialog says why, and lists each booking in
/// the way. The archive is asked once more, its confirm destructive; once it
/// lands the dialog says the account is closed and offers it no more. A
/// refusal the scheduler does not name names every reason.
#[test]
fn an_account_is_archived_once_nothing_stands_in_the_way() {
    let counting = team_view(&archiving(signed_in())).archive.expect("open");
    assert_eq!(counting.dialog.title, "Archive Contract Member's account?");
    assert_eq!(
        counting.dialog.body,
        "They can no longer sign in, they are skipped in routing, and their event types stop taking bookings. The account is kept."
    );
    assert_eq!(counting.dialog.confirm, "Archive account");
    assert_eq!(counting.dialog.close, "Keep account");
    assert!(counting.dialog.destructive && !counting.dialog.can_confirm);
    assert_eq!(counting.status, district_ffi::LoadStatus::Loading);
    assert_eq!(
        counting.blocked.as_deref(),
        Some("Checking their upcoming bookings…")
    );

    let blocked = team_view(&upcoming(
        archiving(signed_in()),
        Ok(contracts::json("district-scheduling-user-upcoming.json")),
    ))
    .archive
    .expect("open");
    assert!(!blocked.dialog.can_confirm);
    assert_eq!(
        blocked.blocked.as_deref(),
        Some("Reassign or cancel their upcoming bookings first.")
    );
    let rows: Vec<[&str; 4]> = blocked
        .bookings
        .iter()
        .map(|b| [b.id.as_str(), &b.event_type, &b.attendee, &b.start_at])
        .collect();
    assert_eq!(
        rows,
        [[
            "bk-contract-3",
            "Site visit",
            "Dana Booker",
            "2026-09-18T15:00:00Z"
        ]]
    );
    // A count that failed is not "none": still no archive.
    let failed = team_view(&upcoming(archiving(signed_in()), Err(server_error())))
        .archive
        .expect("open");
    assert!(matches!(
        failed.status,
        district_ffi::LoadStatus::Failed { .. }
    ));
    assert!(!failed.dialog.can_confirm && failed.bookings.is_empty());

    let open = team_view(&archive_open(signed_in())).archive.expect("open");
    assert!(open.dialog.can_confirm && open.blocked.is_none() && open.ask.is_none());
    let asked = archive_open(signed_in()).ui(event(TeamAction::AskArchive));
    let view = team_view(&asked).archive.expect("open");
    assert!(!view.dialog.can_confirm, "asked once, not twice");
    let ask = view.ask.expect("asked");
    assert_eq!(ask.title, "Archive Contract Member's account?");
    assert_eq!(
        (ask.confirm.as_str(), ask.cancel.as_str()),
        ("Archive account", "Keep account")
    );
    let kept = asked.ui(event(TeamAction::CancelArchive));
    assert_eq!(team_view(&kept).archive.expect("open").ask, None);

    let confirmed = archive_confirmed(signed_in());
    assert!(sent(&confirmed, |r| matches!(
        r,
        SchedulingAdminRequest::ArchiveUser { id } if id == CONTRACT
    )));
    let view = team_view(&confirmed).archive.expect("open");
    assert!(view.dialog.working && view.ask.is_none());

    let done = archived(signed_in());
    let view = team_view(&done).archive.expect("still open");
    assert_eq!(
        view.notice.as_deref(),
        Some("Contract Member's scheduler account is closed")
    );
    assert!(!view.dialog.can_confirm, "archived: not offered again");
    let closed = team_view(&done.ui(event(TeamAction::CloseDialog)));
    assert_eq!(closed.archive, None);

    let refused = team_view(&archive_answer(
        archive_confirmed(signed_in()),
        Err(server_error()),
    ))
    .archive
    .expect("open");
    assert!(refused.dialog.failure.is_some() && refused.dialog.can_confirm);
    assert_eq!(refused.notice, None);
    let unnamed = team_view(&archive_answer(
        archive_confirmed(signed_in()),
        Err(ApiError::Conflict(ErrorDetail::default())),
    ))
    .archive
    .expect("open");
    assert_eq!(
        unnamed.dialog.failure.map(|f| f.message).as_deref(),
        Some(
            "That account could not be archived. It may still hold upcoming bookings, it may be the workspace owner, or it may be archived already."
        )
    );
}

#[test]
fn each_action_is_its_core_event() {
    use TeamAction as A;
    use TeamEvent as E;
    let s = |v: &str| v.to_owned();
    let to_core = |e| Event::SchedulingAdmin(SchedulingAdminEvent::Team(e));
    for (sent, event_sent) in [
        (A::OpenCreate, E::OpenCreate),
        (A::OpenEditor { team_id: s("t") }, E::OpenEditor(s("t"))),
        (A::EditName { name: s("n") }, E::EditName(s("n"))),
        (A::SaveName, E::SaveName),
        (A::AskDelete, E::AskDelete),
        (A::CancelDelete, E::CancelDelete),
        (A::ConfirmDelete, E::ConfirmDelete),
        (A::OpenMembers { team_id: s("t") }, E::OpenMembers(s("t"))),
        (A::Pick { user_id: s("u") }, E::Pick(s("u"))),
        (A::AddMember, E::AddMember),
        (
            A::EditPriority {
                user_id: s("u"),
                value: s("2"),
            },
            E::EditPriority {
                user_id: s("u"),
                value: s("2"),
            },
        ),
        (A::SavePriority { user_id: s("u") }, E::SavePriority(s("u"))),
        (A::AskRemove { user_id: s("u") }, E::AskRemove(s("u"))),
        (A::CancelRemove, E::CancelRemove),
        (A::ConfirmRemove, E::ConfirmRemove),
        (A::OpenArchive { user_id: s("u") }, E::OpenArchive(s("u"))),
        (A::AskArchive, E::AskArchive),
        (A::CancelArchive, E::CancelArchive),
        (A::ConfirmArchive, E::ConfirmArchive),
        (A::CloseDialog, E::CloseDialog),
    ] {
        assert_eq!(event(sent).events(), [to_core(event_sent)]);
    }
}
