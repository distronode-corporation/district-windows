//! The staff the receptionist can transfer a call to: the transfer directory,
//! as District AI for Linux's directory page shows it
//! (`district-app/src/pages/directory.rs`).
//!
//! Each stored entry is edited one key at a time and keeps every other key it
//! holds; the entry being added needs a name and a number; and the save, which
//! replaces the whole directory, asks first what it will do. A directory stored
//! in a shape this build cannot carry whole is said to be so, with no control.
//!
//! An entry holds a phone number: it is shown as the core gives it, grouped for
//! reading in the line under the entry, and never written anywhere else.

use district_core::{
    Capabilities, DirectoryEvent, DirectoryField, DirectorySection, Event, Model, Route, SignedIn,
    WorkspaceSection, format_phone_number,
};
use district_model::DirectoryEntry;
use serde::Serialize;

use super::call_handling::QuestionView;
use super::{SaveNoticeView, SectionStatus, save_notice, section_status};
use crate::screen::ScreenView;
use crate::views::EmptyView;

/// Whether this version has the area's screens. The packet that builds them
/// sets it; until then [`crate::nav::built`] says no for its routes.
pub(crate) const BUILT: bool = true;

/// The section's heading: the hub row's title, which opens it.
pub const DIRECTORY_TITLE: &str = "Transfer directory";
/// What the section says under its heading.
pub const DIRECTORY_INTRO: &str = "Who the receptionist can put a live caller through to. \
    Saving replaces the whole directory.";

/// An entry's heading: its name, or that it has none.
pub(crate) fn entry_title(entry: &DirectoryEntry) -> String {
    match entry.name().trim() {
        "" => "No name".to_owned(),
        name => name.to_owned(),
    }
}

/// The line under an entry: its number grouped for reading, or that it has
/// none.
pub(crate) fn entry_line(entry: &DirectoryEntry) -> String {
    match entry.phone_number().trim() {
        "" => "No phone number, so nobody can be put through".to_owned(),
        number => format_phone_number(number),
    }
}

/// What the directory says about entries that lack a name or a number.
pub(crate) fn incomplete_note(count: usize) -> Option<String> {
    match count {
        0 => None,
        1 => Some(
            "One entry lacks a name or a number. It is kept, but the receptionist cannot \
             put anyone through to it."
                .to_owned(),
        ),
        n => Some(format!(
            "{n} entries lack a name or a number. They are kept, but the receptionist cannot \
             put anyone through to them."
        )),
    }
}

/// A field of a directory entry the member edits.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash, Serialize, uniffi::Enum)]
pub enum DirectoryEntryField {
    /// The person's name.
    Name,
    /// The number to put a caller through to.
    PhoneNumber,
}

impl DirectoryEntryField {
    fn field(self) -> DirectoryField {
        match self {
            Self::Name => DirectoryField::Name,
            Self::PhoneNumber => DirectoryField::PhoneNumber,
        }
    }
}

/// One entry of the directory.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct DirectoryEntryView {
    /// Its position, which its edits and its removal name.
    pub index: u32,
    /// Its heading: the name, or "No name".
    pub title: String,
    /// The line under it: the number grouped for reading, or that it has
    /// none.
    pub line: String,
    /// The name, as the form has it.
    pub name: String,
    /// The number, as the form has it.
    pub phone_number: String,
}

/// The transfer directory section.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct DirectoryView {
    /// The heading, [`DIRECTORY_TITLE`].
    pub title: String,
    /// What it says under the heading, [`DIRECTORY_INTRO`].
    pub intro: String,
    /// Where the page stands. No list is offered before the settings are
    /// read: a list not built from them could only save over them.
    pub status: SectionStatus,
    /// A directory stored in a shape this app cannot change whole: said in
    /// place of the list, with no control. Not a failure, and a retry cannot
    /// help.
    pub unmodellable: Option<EmptyView>,
    /// The entries, in order: the edited list, else the stored one.
    pub entries: Vec<DirectoryEntryView>,
    /// What to say when there is nobody.
    pub empty: Option<EmptyView>,
    /// How many entries lack a name or a number, when some do.
    pub incomplete: Option<String>,
    /// The name of the entry being added.
    pub new_name: String,
    /// The number of the entry being added.
    pub new_phone_number: String,
    /// Why the last "Add" did nothing: a name and a number are both needed.
    pub add_rejected: Option<String>,
    /// Whether the list can be changed: read, editable here, the member may,
    /// and no save on its way.
    pub can_edit: bool,
    /// Whether "Save" works: the list differs from the stored one.
    pub can_save: bool,
    /// Whether a save is on its way (show a progress ring).
    pub saving: bool,
    /// How the last save ended, until dismissed or the list changes.
    pub notice: Option<SaveNoticeView>,
    /// The core's question before the save, while it asks.
    pub confirming: Option<QuestionView>,
}

impl Default for DirectoryView {
    /// The section before anything is read.
    fn default() -> Self {
        Self {
            title: DIRECTORY_TITLE.to_owned(),
            intro: DIRECTORY_INTRO.to_owned(),
            status: SectionStatus::Loading,
            unmodellable: None,
            entries: Vec::new(),
            empty: None,
            incomplete: None,
            new_name: String::new(),
            new_phone_number: String::new(),
            add_rejected: None,
            can_edit: false,
            can_save: false,
            saving: false,
            notice: None,
            confirming: None,
        }
    }
}

/// Something the member did on the transfer directory section.
#[derive(Clone, Debug, PartialEq, Eq, uniffi::Enum)]
pub enum DirectoryAction {
    /// Open the transfer directory section.
    Open,
    /// The new entry's name changed.
    EditNewName {
        /// As typed.
        name: String,
    },
    /// The new entry's number changed.
    EditNewPhoneNumber {
        /// As typed.
        number: String,
    },
    /// Add the new entry to the list. Nothing is saved yet.
    Add,
    /// Change one field of one entry, keeping its other keys.
    Edit {
        /// The entry's position.
        index: u32,
        /// Which field.
        field: DirectoryEntryField,
        /// The new text.
        value: String,
    },
    /// Take one entry off the list. Nothing is saved yet.
    Remove {
        /// The entry's position.
        index: u32,
    },
    /// "Save": the core asks first.
    Save,
    /// Answer the question yes.
    ConfirmSave,
    /// Answer it no.
    CancelSave,
    /// Put the save's notice away.
    DismissNotice,
}

/// The core events `action` is, in the order the core is to hear them.
pub(crate) fn events(action: DirectoryAction) -> Vec<Event> {
    vec![match action {
        DirectoryAction::Open => Event::Navigate(Route::Workspace(WorkspaceSection::Directory)),
        DirectoryAction::EditNewName { name } => {
            Event::Directory(DirectoryEvent::EditNewName(name))
        }
        DirectoryAction::EditNewPhoneNumber { number } => {
            Event::Directory(DirectoryEvent::EditNewPhoneNumber(number))
        }
        DirectoryAction::Add => Event::Directory(DirectoryEvent::Add),
        DirectoryAction::Edit {
            index,
            field,
            value,
        } => Event::Directory(DirectoryEvent::Edit {
            index: index as usize,
            field: field.field(),
            value,
        }),
        DirectoryAction::Remove { index } => {
            Event::Directory(DirectoryEvent::Remove(index as usize))
        }
        DirectoryAction::Save => Event::Directory(DirectoryEvent::Save),
        DirectoryAction::ConfirmSave => Event::Directory(DirectoryEvent::ConfirmSave),
        DirectoryAction::CancelSave => Event::Directory(DirectoryEvent::CancelSave),
        DirectoryAction::DismissNotice => Event::Directory(DirectoryEvent::DismissNotice),
    }]
}

/// The section as the core holds it, for a member with `capabilities`.
pub(crate) fn directory_view(
    section: &DirectorySection,
    capabilities: &Capabilities,
) -> DirectoryView {
    let mut view = DirectoryView {
        status: section_status(&section.config, &section.save),
        confirming: section.confirmation().map(|confirm| QuestionView {
            title: confirm.title().to_owned(),
            body: confirm.body(),
            action: confirm.action().to_owned(),
            destructive: confirm.entries == 0,
        }),
        ..DirectoryView::default()
    };
    if view.status != SectionStatus::Ready {
        return view;
    }
    if section.unmodellable() {
        view.unmodellable = Some(EmptyView::new(
            DirectorySection::UNMODELLABLE_TITLE,
            DirectorySection::UNMODELLABLE_BODY,
        ));
        return view;
    }
    let entries = section.entries();
    view.entries = entries
        .iter()
        .enumerate()
        .map(|(index, entry)| DirectoryEntryView {
            index: u32::try_from(index).unwrap_or(u32::MAX),
            title: entry_title(entry),
            line: entry_line(entry),
            name: entry.name().to_owned(),
            phone_number: entry.phone_number().to_owned(),
        })
        .collect();
    view.empty = entries
        .is_empty()
        .then(|| EmptyView::new(DirectorySection::EMPTY_TITLE, DirectorySection::EMPTY_BODY));
    view.incomplete = incomplete_note(section.incomplete_count());
    view.new_name.clone_from(&section.new_name);
    view.new_phone_number.clone_from(&section.new_phone_number);
    view.add_rejected = section
        .add_rejected
        .then(|| DirectorySection::ADD_REJECTED.to_owned());
    view.can_edit = capabilities.can_change && section.editable();
    view.can_save = capabilities.can_change && section.can_save();
    view.saving = section.save.is_busy();
    view.notice = save_notice(&section.save);
    view
}

/// The page of the transfer directory section, for a signed-in model. Until
/// the core has opened the section, it is being read.
pub(crate) fn screen(_model: &Model, signed_in: &SignedIn) -> ScreenView {
    ScreenView::Directory {
        view: signed_in
            .directory
            .as_ref()
            .map_or_else(DirectoryView::default, |section| {
                directory_view(section, &signed_in.capabilities())
            }),
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn an_entry_reads_by_its_name_and_its_number() {
        let entry = DirectoryEntry::new("Ops desk", "+14165550177");
        assert_eq!(entry_title(&entry), "Ops desk");
        assert_eq!(entry_line(&entry), "+1 416 555 0177");
        let blank = DirectoryEntry::new(" ", "");
        assert_eq!(entry_title(&blank), "No name");
        assert!(entry_line(&blank).starts_with("No phone number"));
        assert_eq!(incomplete_note(0), None);
        assert!(incomplete_note(1).unwrap().starts_with("One entry"));
        assert!(incomplete_note(2).unwrap().starts_with("2 entries"));
    }
}
