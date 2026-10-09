//! The members and their roles, and the workspace's name, as District AI for
//! Linux shows them (`pages/members.rs`).
//!
//! Two role rules on one section, both the core's. Adding, changing and
//! removing members is for an agency member only; renaming the workspace is for
//! an agency or a client member; the section itself is closed to a viewer
//! (`Capabilities::allows`). A client sees who belongs here, and why they cannot
//! change it.
//!
//! An address is shown exactly as the service stores it (lower case), in the
//! list and in the removal question, and nothing here logs one. Adding someone
//! sends no invitation: the core says so, and the note says it under "Add".
//! Removing someone asks first. Two refusals are the service's words and offer
//! no retry: an address that is already a member, and a change that would leave
//! the workspace with no agency member.

use district_core::{
    Capabilities, Event, MemberList, MembersAction as Write, MembersEvent, MembersSection, Model,
    Route, SignedIn, WorkspaceSection, WorkspacesState, member_role_label,
};
use district_model::{MemberRole, WorkspaceMember};
use serde::Serialize;

use super::{SaveNoticeView, SectionStatus, save_notice};
use crate::screen::ScreenView;
use crate::views::humanize;

/// Whether this version has the area's screens.
pub(crate) const BUILT: bool = true;

/// The section's heading: its row's title in the hub, which opens it.
pub const TITLE: &str = "Members";
/// The heading of a failed read of the members.
pub const FAILED_TITLE: &str = "Could not load the members";
/// The note for a member who may not change who belongs here.
pub const AGENCY_ONLY: &str =
    "Only an agency member can add members, change their roles or remove them.";
/// The add form's heading.
pub const ADD_HEADING: &str = "Add a member";
/// The address box's label.
pub const EMAIL_LABEL: &str = "Email address";
/// The role picker's label.
pub const ROLE_LABEL: &str = "Role";
/// The add button.
pub const ADD_ACTION: &str = "Add";
/// A member's remove button.
pub const REMOVE_FROM_WORKSPACE: &str = "Remove from this workspace";
/// The removal question's button that keeps the member.
pub const CANCEL: &str = "Cancel";
/// The rename form's heading.
pub const RENAME_HEADING: &str = "The workspace's name";
/// The label of the name now.
pub const CURRENT_NAME: &str = "Now";
/// The new name box's label.
pub const NEW_NAME: &str = "New name";
/// The rename button.
pub const RENAME_ACTION: &str = "Rename the workspace";
/// A role this app does not know, which the core treats as no access.
pub const UNKNOWN_ROLE: &str = "A role this app does not know, which it treats as no access.";

/// The members section.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct MembersView {
    /// The heading, [`TITLE`].
    pub title: String,
    /// Where the list stands; the section shows only when ready.
    pub status: SectionStatus,
    /// The members, in the service's order.
    pub members: Vec<MemberRowView>,
    /// Whether a change of a member is on its way (show a progress ring).
    pub changing: bool,
    /// [`AGENCY_ONLY`], for a member who may not manage members.
    pub read_only_note: Option<String>,
    /// How the last change ended, until dismissed.
    pub notice: Option<SaveNoticeView>,
    /// The roles, in the order offered, each with what it may do.
    pub roles: Vec<RoleChoiceView>,
    /// Adding a member: only for an agency member.
    pub add: Option<AddMemberView>,
    /// Renaming the workspace: for an agency or a client member.
    pub rename: Option<RenameView>,
    /// "Remove this member?", while it is asked.
    pub confirm_remove: Option<RemoveQuestionView>,
}

/// A role, as the section offers it.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash, Serialize, uniffi::Enum)]
pub enum MemberRoleView {
    /// Full access, members included.
    Agency,
    /// Everyday use.
    Client,
    /// Read only.
    Viewer,
}

impl From<MemberRoleView> for MemberRole {
    fn from(role: MemberRoleView) -> Self {
        match role {
            MemberRoleView::Agency => Self::Agency,
            MemberRoleView::Client => Self::Client,
            MemberRoleView::Viewer => Self::Viewer,
        }
    }
}

impl From<MemberRole> for MemberRoleView {
    fn from(role: MemberRole) -> Self {
        match role {
            MemberRole::Agency => Self::Agency,
            MemberRole::Client => Self::Client,
            MemberRole::Viewer => Self::Viewer,
        }
    }
}

/// The roles, in the order offered.
const ROLES: [MemberRole; 3] = [MemberRole::Agency, MemberRole::Client, MemberRole::Viewer];

/// One role to choose, with what it may do.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct RoleChoiceView {
    /// The role.
    pub role: MemberRoleView,
    /// Its name.
    pub label: String,
    /// What it may do.
    pub description: String,
}

/// One member.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct MemberRowView {
    /// The address, as the service stores it. It names the member.
    pub email: String,
    /// Their role's name and what it may do.
    pub role_line: String,
    /// Their role, when it is one of the three.
    pub role: Option<MemberRoleView>,
    /// Whether their role can be changed and they can be removed now: an
    /// agency member, with nothing on its way.
    pub can_change: bool,
    /// [`REMOVE_FROM_WORKSPACE`].
    pub remove_label: String,
}

/// The add form.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct AddMemberView {
    /// [`ADD_HEADING`].
    pub heading: String,
    /// [`EMAIL_LABEL`].
    pub email_label: String,
    /// The address, as typed.
    pub email: String,
    /// [`ROLE_LABEL`].
    pub role_label: String,
    /// The role it will get.
    pub role: MemberRoleView,
    /// Whether the form can be edited: nothing on its way.
    pub editable: bool,
    /// Whether "Add" works: an address, and nothing on its way.
    pub can_add: bool,
    /// Why the last "Add" was refused here: not an address.
    pub rejected: Option<String>,
    /// That adding someone sends no invitation, in the core's words.
    pub note: String,
    /// [`ADD_ACTION`].
    pub add_label: String,
}

/// The rename form.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct RenameView {
    /// [`RENAME_HEADING`].
    pub heading: String,
    /// [`CURRENT_NAME`].
    pub current_label: String,
    /// The name now: the one the service stored at the last rename, else the
    /// switcher's.
    pub current: String,
    /// [`NEW_NAME`].
    pub new_label: String,
    /// The new name, as typed.
    pub new_name: String,
    /// Whether the box can be edited: nothing on its way.
    pub editable: bool,
    /// Whether "Rename the workspace" works.
    pub can_rename: bool,
    /// Whether a rename is on its way (show a progress ring).
    pub renaming: bool,
    /// [`RENAME_ACTION`].
    pub rename_label: String,
}

/// "Remove this member?"
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct RemoveQuestionView {
    /// The member, as listed.
    pub email: String,
    /// The heading.
    pub title: String,
    /// What removing them does.
    pub body: String,
    /// The button that removes them.
    pub remove_label: String,
    /// The button that keeps them: the default.
    pub cancel_label: String,
}

/// Something the member did on the members section.
#[derive(Clone, Debug, PartialEq, Eq, uniffi::Enum)]
pub enum MembersAction {
    /// Open the members section.
    Open,
    /// The address to add changed: what the box holds now.
    EditEmail {
        /// The text.
        email: String,
    },
    /// The role to add with changed.
    SetRole {
        /// The role.
        role: MemberRoleView,
    },
    /// Add the address. Agency only.
    Add,
    /// Give a listed member another role. Agency only.
    ChangeRole {
        /// The member, as listed.
        email: String,
        /// The role.
        role: MemberRoleView,
    },
    /// Ask before removing a listed member. Agency only.
    AskRemove {
        /// The member, as listed.
        email: String,
    },
    /// Remove them, after the question.
    ConfirmRemove,
    /// Keep them.
    CancelRemove,
    /// The new name changed: what the box holds now.
    EditName {
        /// The text.
        name: String,
    },
    /// Rename the workspace. Agency and client.
    Rename,
    /// Put the notice away.
    DismissNotice,
}

/// The core events `action` is, in the order the core is to hear them.
pub(crate) fn events(action: MembersAction) -> Vec<Event> {
    let members = Event::Members;
    vec![match action {
        MembersAction::Open => Event::Navigate(Route::Workspace(WorkspaceSection::Members)),
        MembersAction::EditEmail { email } => members(MembersEvent::EditEmail(email)),
        MembersAction::SetRole { role } => members(MembersEvent::SetRole(role.into())),
        MembersAction::Add => members(MembersEvent::Add),
        MembersAction::ChangeRole { email, role } => members(MembersEvent::ChangeRole {
            email,
            role: role.into(),
        }),
        MembersAction::AskRemove { email } => members(MembersEvent::AskRemove { email }),
        MembersAction::ConfirmRemove => members(MembersEvent::ConfirmRemove),
        MembersAction::CancelRemove => members(MembersEvent::CancelRemove),
        MembersAction::EditName { name } => members(MembersEvent::EditName(name)),
        MembersAction::Rename => members(MembersEvent::Rename),
        MembersAction::DismissNotice => members(MembersEvent::DismissNotice),
    }]
}

/// The page of the members section, for a signed-in model.
pub(crate) fn screen(_model: &Model, signed_in: &SignedIn) -> ScreenView {
    let name = match &signed_in.workspaces {
        WorkspacesState::Ready(workspaces) => workspaces.active().name.as_str(),
        _ => "",
    };
    ScreenView::Members {
        view: members_view(signed_in.members.as_ref(), &signed_in.capabilities(), name),
    }
}

/// The role a member holds, when it is one of the three: stored as free text,
/// in any case.
fn stored_role(raw: &str) -> Option<MemberRole> {
    ROLES
        .into_iter()
        .find(|role| raw.trim().eq_ignore_ascii_case(role.as_str()))
}

/// The line under a member: their role's name and what it may do.
fn role_line(raw: &str) -> String {
    match stored_role(raw) {
        Some(role) => {
            let (name, does) = member_role_label(role);
            format!("{name} \u{b7} {does}")
        }
        None => format!("{} \u{b7} {UNKNOWN_ROLE}", humanize(raw)),
    }
}

/// The section `section` holds, for a member with `capabilities`, in the
/// workspace named `name`; being read when there is no section yet.
fn members_view(
    section: Option<&MembersSection>,
    capabilities: &Capabilities,
    name: &str,
) -> MembersView {
    let manage = capabilities.can_manage_members;
    let busy = section.is_some_and(MembersSection::busy);
    let last = section.and_then(|s| s.last_write);
    let (status, listed): (SectionStatus, &[WorkspaceMember]) = match section.map(|s| &s.members) {
        None | Some(MemberList::Loading) => (SectionStatus::Loading, &[]),
        Some(MemberList::Failed(failure)) => (
            SectionStatus::Failed {
                title: FAILED_TITLE.to_owned(),
                failure: failure.into(),
            },
            &[],
        ),
        Some(MemberList::Ready(members)) => (SectionStatus::Ready, members),
    };
    MembersView {
        title: TITLE.to_owned(),
        status,
        members: listed
            .iter()
            .map(|member| MemberRowView {
                email: member.email.clone(),
                role_line: role_line(&member.role),
                role: stored_role(&member.role).map(MemberRoleView::from),
                can_change: manage && !busy,
                remove_label: REMOVE_FROM_WORKSPACE.to_owned(),
            })
            .collect(),
        changing: busy && last != Some(Write::Rename),
        read_only_note: (!manage).then(|| AGENCY_ONLY.to_owned()),
        notice: section.and_then(|s| save_notice(&s.write)),
        roles: ROLES
            .into_iter()
            .map(|role| {
                let (label, description) = member_role_label(role);
                RoleChoiceView {
                    role: role.into(),
                    label: label.to_owned(),
                    description: description.to_owned(),
                }
            })
            .collect(),
        add: section.filter(|_| manage).map(|s| AddMemberView {
            heading: ADD_HEADING.to_owned(),
            email_label: EMAIL_LABEL.to_owned(),
            email: s.email.clone(),
            role_label: ROLE_LABEL.to_owned(),
            role: s.role.into(),
            editable: !busy,
            can_add: s.can_add(),
            rejected: s
                .add_rejected
                .then(|| MembersSection::ADD_REJECTED.to_owned()),
            note: MembersSection::NO_INVITATION.to_owned(),
            add_label: ADD_ACTION.to_owned(),
        }),
        rename: section
            .filter(|_| capabilities.can_rename_workspace)
            .map(|s| RenameView {
                heading: RENAME_HEADING.to_owned(),
                current_label: CURRENT_NAME.to_owned(),
                current: s.stored_name.clone().unwrap_or_else(|| name.to_owned()),
                new_label: NEW_NAME.to_owned(),
                new_name: s.new_name.clone(),
                editable: !busy,
                can_rename: s.can_rename(),
                renaming: busy && last == Some(Write::Rename),
                rename_label: RENAME_ACTION.to_owned(),
            }),
        confirm_remove: section.and_then(|s| s.confirming.as_ref()).map(|email| {
            RemoveQuestionView {
                email: email.clone(),
                title: MembersSection::REMOVE_TITLE.to_owned(),
                body: MembersSection::remove_body(email),
                remove_label: MembersSection::REMOVE_ACTION.to_owned(),
                cancel_label: CANCEL.to_owned(),
            }
        }),
    }
}

#[cfg(test)]
impl MembersView {
    /// A view for the boundary's own tests (a screen's trip to C# and back).
    pub(crate) fn sample() -> Self {
        members_view(None, &Capabilities::for_role(Some("agency")), "Example")
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn a_role_is_named_or_said_to_be_unknown() {
        assert!(role_line("AGENCY ").starts_with("Agency \u{b7} Full access"));
        assert_eq!(role_line("owner"), format!("Owner \u{b7} {UNKNOWN_ROLE}"));
        assert_eq!(stored_role("Viewer"), Some(MemberRole::Viewer));
        for role in ROLES {
            assert_eq!(MemberRole::from(MemberRoleView::from(role)), role);
        }
    }

    #[test]
    fn a_section_not_read_yet_is_loading() {
        let view = MembersView::sample();
        assert_eq!(view.status, SectionStatus::Loading);
        assert!(view.members.is_empty() && view.add.is_none() && view.rename.is_none());
        assert_eq!(view.read_only_note, None);
    }
}
