//! The team: who left the workspace while still holding bookings, named at
//! the top; the members (the workspace's members beside the scheduler's own
//! users, each with their address, their workspace role and where they stand
//! with the scheduler, and Archive account for a live scheduler account); and
//! the teams (each with how many hosts it has, Edit and Hosts, and Create
//! team), as District AI for iPhone's `SchedulingTeamView` and its team
//! editor, team members and user archive sheets.
//!
//! The core decides what is offered, and this projects it: every write needs
//! a member who may change things, and a team's own writes need the teams
//! read (adding, reordering and removing hosts each rewrite a rotation, so
//! their dialog works from the team as last answered). When the workspace's
//! members could not be read, no role is shown and the section says so,
//! rather than calling every host removed.
//!
//! A dialog is open while the core holds it, and stays open saying what its
//! last write came to: a team's name (create, rename, and apart from the
//! rename, delete), its hosts (add one, save a routing priority, remove one),
//! and an account's archive, offered once its upcoming bookings were read and
//! there are none. Deleting a team, removing a host and archiving an account
//! are each asked once more inside their dialog, the confirm never the
//! default.
//!
//! No value here is a secret: the addresses are the workspace's own, which
//! its members see on the web dashboard.

use district_core::scheduling_admin::{
    ArchiveDialog, MemberRow, TeamDialog, TeamEditor, TeamEvent, TeamMembersDialog, TeamSection,
    WriteState,
};
use district_core::{Event, SchedulingAdmin, SchedulingAdminEvent};
use district_model::{SchedulingTeam, SchedulingUpcomingBooking};
use serde::Serialize;

use super::bookings::ChoiceRowView;
use super::event_types::SchedulingAskView;
use super::{DialogView, SchedulingAdminPage, dialog_view, landed, load_status};
use crate::views::LoadStatus;
use crate::workflows::RunTone;

/// Whether this version has the section's pages.
pub(crate) const BUILT: bool = true;

/// The team.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct SchedulingTeamView {
    /// Where the read of the members stands: the scheduler's users and the
    /// workspace's members, both. A failed one is a status page, never an
    /// empty list.
    pub status: LoadStatus,
    /// The stranded notice's heading.
    pub stranded_heading: String,
    /// Who left the workspace while still a host, and what to do; `None` when
    /// nobody did, or when the workspace's members could not be read.
    pub stranded: Option<String>,
    /// The members' heading.
    pub members_heading: String,
    /// That the workspace's members could not be read, so no role is shown;
    /// `None` when they were.
    pub members_degraded: Option<String>,
    /// The people, once read.
    pub members: Vec<TeamMemberRowView>,
    /// Said when the members are read and there are none.
    pub members_empty: String,
    /// "Archive account", on the rows that offer it.
    pub archive_label: String,
    /// The teams' heading.
    pub teams_heading: String,
    /// What a team is.
    pub teams_subtitle: String,
    /// Where the read of the teams stands; a failure is said in their card.
    pub teams_status: LoadStatus,
    /// The teams, once read.
    pub teams: Vec<TeamRowView>,
    /// Said when the teams are read and there are none.
    pub teams_empty: String,
    /// Whether Edit and Hosts are offered on each team: the member may change
    /// things.
    pub offers_writes: bool,
    /// Whether "Create team" is offered: the member may change things and the
    /// teams were read.
    pub offers_create: bool,
    /// "Create team" (`TeamAction::OpenCreate`).
    pub create_label: String,
    /// "Edit", on each team (`TeamAction::OpenEditor`).
    pub edit_label: String,
    /// "Hosts", on each team (`TeamAction::OpenMembers`).
    pub hosts_label: String,
    /// A team's name, while its dialog is open.
    pub editor: Option<TeamEditorView>,
    /// A team's hosts, while their dialog is open.
    pub hosts: Option<TeamHostsView>,
    /// An account's archive, while its dialog is open.
    pub archive: Option<TeamArchiveView>,
}

/// One person: a workspace member, a scheduler user, or both.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct TeamMemberRowView {
    /// Their address, which keys the row.
    pub email: String,
    /// Their name: the scheduler's, else the address before its `@`.
    pub name: String,
    /// Their address, and their workspace role unless the members could not
    /// be read: "ann@example.com · Agency".
    pub subtitle: String,
    /// Where they stand with the scheduler: "Host", "Not set up yet",
    /// "Archived".
    pub state_label: String,
    /// Its tone.
    pub tone: RunTone,
    /// Their scheduler account, when Archive account is offered for it
    /// (`TeamAction::OpenArchive`): a member who may change things, and a
    /// live account.
    pub archive_user_id: Option<String>,
}

/// One team.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct TeamRowView {
    /// Its id, which its Edit and Hosts name.
    pub id: String,
    /// Its name.
    pub name: String,
    /// How many hosts it has: "2 members".
    pub member_count: String,
}

/// A team's name: creating a team, or renaming one and, apart from the
/// rename, deleting it. Its save is the dialog's confirm; the dialog stays
/// open saying what the save came to.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct TeamEditorView {
    /// Its title ("New team", or the team's name), the save ("Create team",
    /// "Save name"), the way out ("Cancel", or "Done" for a team) and why the
    /// last write failed.
    pub dialog: DialogView,
    /// What a team is.
    pub hint: String,
    /// "Name".
    pub name_label: String,
    /// The name's placeholder.
    pub name_placeholder: String,
    /// The name, as the core holds it.
    pub name: String,
    /// Why the name cannot be sent, before anything was sent.
    pub name_error: Option<String>,
    /// That a team's booking-page address cannot change here; `None` on a
    /// create.
    pub slug_hint: Option<String>,
    /// Whether "Delete team" shows: a team, not a create.
    pub shows_delete: bool,
    /// "Delete team" (`TeamAction::AskDelete`).
    pub delete_label: String,
    /// Whether it can be pressed: nothing is on its way.
    pub can_delete: bool,
    /// Whether the name can be changed: nothing is on its way.
    pub editable: bool,
    /// What the last write came to ("Team renamed"), once it landed.
    pub notice: Option<String>,
    /// The delete, while it is being asked about: "Delete Sales?".
    pub delete: Option<SchedulingAskView>,
}

/// A team's hosts: each with their routing priority, Save priority and
/// Remove (asked once more), and the people who could be added, one to pick,
/// added by the dialog's confirm. Every control saves on its own, so the way
/// out is "Done".
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct TeamHostsView {
    /// Its title (the team's name), the add ("Add member", offered once
    /// someone is picked), "Done" and why the last write failed.
    pub dialog: DialogView,
    /// That a lower routing priority is offered a booking first.
    pub hint: String,
    /// The team's id, which its hosts' actions are about.
    pub team_id: String,
    /// The hosts, as the team was last answered, with their priorities as
    /// typed.
    pub hosts: Vec<TeamHostRowView>,
    /// Said when the team has no host.
    pub empty: String,
    /// "Routing priority".
    pub priority_label: String,
    /// "Save priority", on each host.
    pub save_priority_label: String,
    /// "Remove", on each host.
    pub remove_label: String,
    /// Why a priority cannot be sent, before anything was sent.
    pub priority_error: Option<String>,
    /// Where the read of the people who could be added stands.
    pub users_status: LoadStatus,
    /// "Host", over the people who could be added.
    pub add_label: String,
    /// "Choose a host".
    pub add_placeholder: String,
    /// The people who could be added (not archived, not in the team), the
    /// one picked selected.
    pub candidates: Vec<ChoiceRowView>,
    /// Said when they were read and everyone is in the team already.
    pub add_none: Option<String>,
    /// Whether the hosts and the choice can be changed: nothing is on its
    /// way.
    pub editable: bool,
    /// What the last write came to ("Host added to the team"), once it
    /// landed.
    pub notice: Option<String>,
    /// A host's removal, while it is being asked about.
    pub remove: Option<SchedulingAskView>,
}

/// One host of a team.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct TeamHostRowView {
    /// Their scheduler user id.
    pub user_id: String,
    /// Their name, or their address for a blank one.
    pub name: String,
    /// Their address.
    pub email: String,
    /// Their routing priority, as typed.
    pub priority: String,
}

/// Archiving a scheduler account: what archiving does, why it cannot be done
/// yet, and each upcoming booking that stands in the way. Its confirm asks
/// once more; the dialog stays open saying the account is closed.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct TeamArchiveView {
    /// Its title ("Archive Ann's account?"), what archiving does, the confirm
    /// ("Archive account", destructive, offered once the upcoming bookings
    /// were read and there are none, and until the account is archived),
    /// "Keep account" and why the archive failed.
    pub dialog: DialogView,
    /// Where the read of the upcoming bookings stands.
    pub status: LoadStatus,
    /// Why it cannot be archived yet: still counting, or bookings to move
    /// first.
    pub blocked: Option<String>,
    /// The upcoming bookings in the way.
    pub bookings: Vec<UpcomingBookingView>,
    /// What the archive came to, once it landed.
    pub notice: Option<String>,
    /// The archive, while it is asked about once more.
    pub ask: Option<SchedulingAskView>,
}

/// An upcoming booking that stands in the way of an archive.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct UpcomingBookingView {
    /// Its id.
    pub id: String,
    /// Its event type's name.
    pub event_type: String,
    /// Who booked it.
    pub attendee: String,
    /// When it starts, as the scheduler sent it (ISO 8601).
    pub start_at: String,
}

/// Something the member did on the team.
#[derive(Clone, Debug, PartialEq, Eq, uniffi::Enum)]
pub enum TeamAction {
    /// Open the create form.
    OpenCreate,
    /// Open a team's name.
    OpenEditor {
        /// The team's id.
        team_id: String,
    },
    /// The name changed.
    EditName {
        /// The name, as typed.
        name: String,
    },
    /// Create the team, or save its name.
    SaveName,
    /// Ask about deleting the team.
    AskDelete,
    /// Keep it.
    CancelDelete,
    /// Delete it.
    ConfirmDelete,
    /// Open a team's hosts.
    OpenMembers {
        /// The team's id.
        team_id: String,
    },
    /// Pick someone to add.
    Pick {
        /// Their scheduler user id.
        user_id: String,
    },
    /// Add the one picked.
    AddMember,
    /// A host's routing priority changed.
    EditPriority {
        /// Their scheduler user id.
        user_id: String,
        /// The priority, as typed.
        value: String,
    },
    /// Save a host's routing priority.
    SavePriority {
        /// Their scheduler user id.
        user_id: String,
    },
    /// Ask about removing a host.
    AskRemove {
        /// Their scheduler user id.
        user_id: String,
    },
    /// Keep them.
    CancelRemove,
    /// Remove them.
    ConfirmRemove,
    /// Open the archive of a person's scheduler account.
    OpenArchive {
        /// Their scheduler user id.
        user_id: String,
    },
    /// Ask about the archive once more.
    AskArchive,
    /// Keep the account.
    CancelArchive,
    /// Archive it.
    ConfirmArchive,
    /// Close the dialog open.
    CloseDialog,
}

/// The core events `action` is.
pub(crate) fn events(action: TeamAction) -> Vec<Event> {
    use TeamAction as A;
    use TeamEvent as E;
    let event = match action {
        A::OpenCreate => E::OpenCreate,
        A::OpenEditor { team_id } => E::OpenEditor(team_id),
        A::EditName { name } => E::EditName(name),
        A::SaveName => E::SaveName,
        A::AskDelete => E::AskDelete,
        A::CancelDelete => E::CancelDelete,
        A::ConfirmDelete => E::ConfirmDelete,
        A::OpenMembers { team_id } => E::OpenMembers(team_id),
        A::Pick { user_id } => E::Pick(user_id),
        A::AddMember => E::AddMember,
        A::EditPriority { user_id, value } => E::EditPriority { user_id, value },
        A::SavePriority { user_id } => E::SavePriority(user_id),
        A::AskRemove { user_id } => E::AskRemove(user_id),
        A::CancelRemove => E::CancelRemove,
        A::ConfirmRemove => E::ConfirmRemove,
        A::OpenArchive { user_id } => E::OpenArchive(user_id),
        A::AskArchive => E::AskArchive,
        A::CancelArchive => E::CancelArchive,
        A::ConfirmArchive => E::ConfirmArchive,
        A::CloseDialog => E::CloseDialog,
    };
    vec![Event::SchedulingAdmin(SchedulingAdminEvent::Team(event))]
}

/// The team's page, for a member who may change things when `can_change`.
/// Before the section is entered, its reads are on their way.
pub(crate) fn page(admin: &SchedulingAdmin, can_change: bool) -> SchedulingAdminPage {
    let fresh = TeamSection::default();
    SchedulingAdminPage::Team {
        view: view(admin.team.as_ref().unwrap_or(&fresh), can_change),
    }
}

fn view(section: &TeamSection, can_change: bool) -> SchedulingTeamView {
    let [
        stranded_heading,
        members_heading,
        degraded,
        members_empty,
        teams_heading,
        teams_subtitle,
        teams_empty,
        create,
        edit,
        hosts_label,
        archive_label,
    ] = TeamSection::labels();
    let members = section.members();
    let (mut editor, mut hosts, mut archive) = (None, None, None);
    match &section.dialog {
        Some(TeamDialog::Editor(open)) => editor = Some(editor_view(open)),
        Some(TeamDialog::Members(open)) => hosts = Some(hosts_view(open)),
        Some(TeamDialog::Archive(open)) => archive = Some(archive_view(open)),
        None => {}
    }
    SchedulingTeamView {
        status: load_status(&members),
        stranded_heading: stranded_heading.to_owned(),
        stranded: section.stranded_notice(),
        members_heading: members_heading.to_owned(),
        members_degraded: section.members_degraded().then(|| degraded.to_owned()),
        members: members
            .value()
            .map(Vec::as_slice)
            .unwrap_or_default()
            .iter()
            .map(|row| member_row(section, row, can_change))
            .collect(),
        members_empty: members_empty.to_owned(),
        archive_label: archive_label.to_owned(),
        teams_heading: teams_heading.to_owned(),
        teams_subtitle: teams_subtitle.to_owned(),
        teams_status: load_status(&section.teams),
        teams: section
            .teams
            .value()
            .map(Vec::as_slice)
            .unwrap_or_default()
            .iter()
            .map(team_row)
            .collect(),
        teams_empty: teams_empty.to_owned(),
        offers_writes: can_change,
        offers_create: can_change && section.teams.value().is_some(),
        create_label: create.to_owned(),
        edit_label: edit.to_owned(),
        hosts_label: hosts_label.to_owned(),
        editor,
        hosts,
        archive,
    }
}

/// A person as the members' list shows them.
fn member_row(section: &TeamSection, row: &MemberRow, can_change: bool) -> TeamMemberRowView {
    let (state_label, tone) = row.state.badge();
    TeamMemberRowView {
        email: row.email.clone(),
        name: row.name.clone(),
        subtitle: section.member_subtitle(row),
        state_label: state_label.to_owned(),
        tone: RunTone::from(tone),
        archive_user_id: row
            .scheduler_user_id
            .clone()
            .filter(|_| TeamSection::offers_archive(row, can_change)),
    }
}

/// A team as its list shows it.
fn team_row(team: &SchedulingTeam) -> TeamRowView {
    TeamRowView {
        id: team.id.clone(),
        name: team.name.clone(),
        member_count: TeamSection::member_count(team),
    }
}

/// Who a host or a user is called: their name, or their address for a blank
/// one.
fn person(name: &str, email: &str) -> String {
    if name.trim().is_empty() { email } else { name }.to_owned()
}

/// A team's name.
fn editor_view(editor: &TeamEditor) -> TeamEditorView {
    let [
        hint,
        name_label,
        name_placeholder,
        slug_hint,
        delete_body,
        delete_confirm,
        delete_keep,
    ] = TeamEditor::labels();
    let busy = editor.write.busy();
    let title = editor.title();
    let renaming = editor.team_id.is_some();
    TeamEditorView {
        dialog: dialog_view(
            [
                title.as_str(),
                "",
                editor.submit_label(),
                editor.close_label(),
            ],
            &editor.write,
            true,
            false,
        ),
        hint: hint.to_owned(),
        name_label: name_label.to_owned(),
        name_placeholder: name_placeholder.to_owned(),
        name: editor.name.clone(),
        name_error: editor.name_rejected.map(str::to_owned),
        slug_hint: renaming.then(|| slug_hint.to_owned()),
        shows_delete: renaming,
        delete_label: delete_confirm.to_owned(),
        can_delete: editor.can_delete(),
        editable: !busy,
        notice: landed(&editor.write),
        delete: editor.confirming_delete.then(|| SchedulingAskView {
            title: editor.delete_title(),
            body: delete_body.to_owned(),
            confirm: delete_confirm.to_owned(),
            cancel: delete_keep.to_owned(),
        }),
    }
}

/// A team's hosts.
fn hosts_view(dialog: &TeamMembersDialog) -> TeamHostsView {
    let [
        hint,
        empty,
        add,
        close,
        add_label,
        add_placeholder,
        add_none,
        priority_label,
        save_priority,
        remove,
        remove_title,
        remove_body,
        remove_keep,
    ] = TeamMembersDialog::labels();
    let busy = dialog.write.busy();
    let addable = dialog.addable();
    TeamHostsView {
        dialog: dialog_view(
            [dialog.team.name.as_str(), "", add, close],
            &dialog.write,
            dialog.picked.is_some(),
            false,
        ),
        hint: hint.to_owned(),
        team_id: dialog.team.id.clone(),
        hosts: dialog
            .members()
            .iter()
            .map(|member| TeamHostRowView {
                user_id: member.id.clone(),
                name: person(&member.name, &member.email),
                email: member.email.clone(),
                priority: dialog.priority(&member.id).to_owned(),
            })
            .collect(),
        empty: empty.to_owned(),
        priority_label: priority_label.to_owned(),
        save_priority_label: save_priority.to_owned(),
        remove_label: remove.to_owned(),
        priority_error: dialog
            .priority_rejected
            .as_ref()
            .map(|(_, why)| (*why).to_owned()),
        users_status: load_status(&dialog.users),
        add_label: add_label.to_owned(),
        add_placeholder: add_placeholder.to_owned(),
        candidates: addable
            .iter()
            .map(|user| ChoiceRowView {
                value: user.id.clone(),
                label: person(&user.name, &user.email),
                selected: dialog.picked.as_deref() == Some(user.id.as_str()),
            })
            .collect(),
        add_none: (dialog.users.value().is_some() && addable.is_empty())
            .then(|| add_none.to_owned()),
        editable: !busy,
        notice: landed(&dialog.write),
        remove: dialog.removing.as_ref().map(|_| SchedulingAskView {
            title: remove_title.to_owned(),
            body: remove_body.to_owned(),
            confirm: remove.to_owned(),
            cancel: remove_keep.to_owned(),
        }),
    }
}

/// An account's archive. Once it landed it is not offered again: the
/// account is archived.
fn archive_view(dialog: &ArchiveDialog) -> TeamArchiveView {
    let [body, confirm, keep] = ArchiveDialog::labels();
    let title = dialog.title();
    let archived = matches!(dialog.write, WriteState::Done(_));
    let rows: &[SchedulingUpcomingBooking] = dialog
        .upcoming
        .value()
        .map(Vec::as_slice)
        .unwrap_or_default();
    TeamArchiveView {
        dialog: dialog_view(
            [title.as_str(), body, confirm, keep],
            &dialog.write,
            dialog.can_archive() && !dialog.confirming && !archived,
            true,
        ),
        status: load_status(&dialog.upcoming),
        blocked: dialog.blocked_reason().map(str::to_owned),
        bookings: rows
            .iter()
            .map(|booking| UpcomingBookingView {
                id: booking.id.clone(),
                event_type: booking.event_type_name.clone(),
                attendee: booking.attendee_name.clone(),
                start_at: booking.start_at.clone(),
            })
            .collect(),
        notice: landed(&dialog.write),
        ask: dialog.confirming.then(|| SchedulingAskView {
            title: title.clone(),
            body: body.to_owned(),
            confirm: confirm.to_owned(),
            cancel: keep.to_owned(),
        }),
    }
}
