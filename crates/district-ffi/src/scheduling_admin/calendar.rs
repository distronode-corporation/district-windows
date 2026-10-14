//! The member's own calendars: a card per connected account (its provider,
//! its address, which of its calendars are checked for conflicts and which
//! receives new bookings) with Choose calendars and Disconnect, asked once
//! more; and the card that connects another, with Zoom's line when the
//! scheduler can connect it, a Connect button per provider the scheduler has
//! credentials for, and Connect a CalDAV calendar, offered whether or not it
//! has any; as District AI for iPhone's `SchedulingCalendarView`, its
//! `SchedulingCalendarPickerSheet` and its `SchedulingCaldavConnectSheet`.
//!
//! Every write here touches only the member's own calendars, so a viewer may
//! make them too: someone who cannot connect a calendar cannot be booked.
//!
//! Connecting Google or Microsoft leaves the app, as the booking pages'
//! "Manage on the web" does: the core asks the scheduler for a single-use
//! sign-in and its runner opens the provider's consent page in the browser at
//! once ([`UiHost::open_url`](crate::UiHost::open_url)). The link never
//! reaches these views. The browser comes back to the web dashboard, never to
//! the app, so once the page is open the section says to finish there and
//! offers Refresh, which reads what is connected.
//!
//! The CalDAV form's app password goes from the window to the core inside
//! the form ([`CaldavFormView`], whose `Debug` leaves it out) and never comes
//! back: no view here holds it, and once the account is connected the core
//! empties it.

use district_core::scheduling_admin::{
    CaldavDialog, CaldavField, CaldavForm, CaldavPreset, CalendarDialog, CalendarEvent,
    CalendarPicker, CalendarSection, WriteState,
};
use district_core::{Event, SchedulingAdmin, SchedulingAdminEvent};
use district_model::SchedulingSecret;
use serde::Serialize;

use super::bookings::ChoiceRowView;
use super::{DialogView, SchedulingAdminPage, dialog_view, landed, load_status};
use crate::views::{FactView, FailureView, LoadStatus, failure};

/// Whether this version has the section's pages.
pub(crate) const BUILT: bool = true;

/// The calendars.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct SchedulingCalendarView {
    /// Where the read of the connections stands: a failed one is a status
    /// page, never "none connected".
    pub status: LoadStatus,
    /// The connected accounts, once read.
    pub connections: Vec<CalendarConnectionView>,
    /// "Account", the cards' first row.
    pub account_label: String,
    /// "Checked for conflicts", the second.
    pub conflicts_label: String,
    /// "Receives bookings", the third.
    pub destination_label: String,
    /// "Choose calendars", on each card (`CalendarAction::OpenCalendars`).
    pub choose_label: String,
    /// "Disconnect", on each card (`CalendarAction::AskDisconnect`).
    pub disconnect_label: String,
    /// Whether a Disconnect can be pressed: none is on its way.
    pub can_disconnect: bool,
    /// The connect card's heading, "Connecting a calendar".
    pub connect_heading: String,
    /// The connect card's sentence: none set up on this scheduler, none
    /// connected yet, or another can be added. Empty until read.
    pub sentence: String,
    /// Zoom, connected or not, when the scheduler can connect it.
    pub zoom: Option<FactView>,
    /// A Connect button per provider the scheduler has credentials for: the
    /// provider sent (`CalendarAction::Connect`), and "Connect Google
    /// Calendar".
    pub providers: Vec<ChoiceRowView>,
    /// Under the Connect buttons: finish in the browser and refresh, or that
    /// no provider can be connected here.
    pub connect_note: String,
    /// Whether the note shows: not when it says what the sentence says.
    pub shows_connect_note: bool,
    /// Whether a sign-in is on its way (the Connect buttons wait, beside
    /// progress). One at a time.
    pub connecting: bool,
    /// Whether a provider's consent page was opened in the browser and the
    /// section has not been read since: Refresh is offered.
    pub awaiting_browser: bool,
    /// Why the last connect failed, until it is put away
    /// (`CalendarAction::DismissConnectFailure`).
    pub connect_failure: Option<FailureView>,
    /// "Connect a CalDAV calendar", which opens its form
    /// (`CalendarAction::OpenCaldav`).
    pub caldav_label: String,
    /// Whether a disconnect is on its way.
    pub disconnecting: bool,
    /// Why the last disconnect failed, until it is put away
    /// (`CalendarAction::DismissDisconnect`).
    pub disconnect_failure: Option<FailureView>,
    /// That no calendar receives new bookings now, when the account
    /// disconnected was the one that did; until it is put away
    /// (`CalendarAction::DismissDisconnect`).
    pub disconnect_notice: Option<String>,
    /// The disconnect, while it is being asked about: its question names the
    /// account, its confirm is destructive, its way out keeps it connected.
    pub disconnect: Option<DialogView>,
    /// One account's calendars, while they are open.
    pub picker: Option<CalendarPickerView>,
    /// The CalDAV form, while it is open.
    pub caldav: Option<CaldavView>,
}

/// One connected account.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct CalendarConnectionView {
    /// Its id, which its actions name.
    pub id: String,
    /// Its provider's name, the card's heading: "Google Calendar".
    pub provider: String,
    /// Its address.
    pub account: String,
    /// Which of its calendars are checked for conflicts: their names, "None",
    /// or, while its calendars could not be read, "Checked" or "Not checked".
    pub conflicts: String,
    /// Which receives new bookings: a calendar's name, "This account", or "No
    /// calendar in this account".
    pub destination: String,
}

/// One account's calendars: which are checked for conflicts, and which
/// receives new bookings. Saved whole by the dialog's confirm, offered once
/// they are read; the dialog stays open saying what the save came to.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct CalendarPickerView {
    /// Its title ("Calendars in ada@example.com"), the save ("Save changes",
    /// "Saving…"), the way out and why the save failed.
    pub dialog: DialogView,
    /// The account, which a read that failed is asked for again by
    /// (`CalendarAction::OpenCalendars`).
    pub connection_id: String,
    /// Where the read of its calendars stands.
    pub status: LoadStatus,
    /// "Loading calendars…".
    pub loading_label: String,
    /// "Checked for conflicts", over a switch per calendar.
    pub conflicts_label: String,
    /// What checking one does.
    pub conflicts_hint: String,
    /// "Receives bookings", over the calendars that can.
    pub destination_label: String,
    /// What that does.
    pub destination_hint: String,
    /// Every calendar, with its switch; none until read.
    pub calendars: Vec<CalendarRowView>,
    /// The calendars that can receive bookings (every one not read-only), the
    /// one that does selected.
    pub destinations: Vec<ChoiceRowView>,
    /// Whether the choices can be changed: read, and no save on its way.
    pub editable: bool,
    /// What the save came to ("Calendars saved"), once it landed.
    pub notice: Option<String>,
}

/// One calendar of an account.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct CalendarRowView {
    /// Its id, which its switch names (`CalendarAction::SetCheckConflicts`).
    pub id: String,
    /// Its name.
    pub name: String,
    /// Whether it is checked for conflicts.
    pub checks_conflicts: bool,
}

/// The CalDAV form: where the calendar lives, the server's address for a
/// place that needs it, the user name and an app password. Its connect is the
/// dialog's confirm; the dialog stays open saying which account connected.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct CaldavView {
    /// Its title ("Connect a CalDAV calendar"), the connect ("Connect
    /// calendar", "Connecting…"), the way out and why the connect failed.
    pub dialog: DialogView,
    /// "Where the calendar lives".
    pub preset_label: String,
    /// The places, by their wire value, the one chosen selected.
    pub presets: Vec<ChoiceRowView>,
    /// Where to get an app password there.
    pub preset_note: String,
    /// Whether the server's address shows: Nextcloud or another server.
    pub shows_server_url: bool,
    /// "Server URL".
    pub server_url_label: String,
    /// What it is.
    pub server_url_hint: String,
    /// "Username".
    pub username_label: String,
    /// "App password".
    pub password_label: String,
    /// That it is an app password, not the account's.
    pub password_hint: String,
    /// The server's address, as the core holds it: what was typed, and empty
    /// again once an account connected.
    pub server_url: String,
    /// The user name, the same way.
    pub username: String,
    /// Why the server's address was refused, before anything was sent.
    pub server_url_error: Option<String>,
    /// Why the user name was.
    pub username_error: Option<String>,
    /// Why the app password was.
    pub password_error: Option<String>,
    /// Whether the fields can be changed: no connect is on its way.
    pub editable: bool,
    /// What the last connect came to: "Calendar connected: ada@example.com".
    pub notice: Option<String>,
}

/// The CalDAV form's fields as the window holds them, what a change to the
/// form sends back whole (`CalendarAction::EditCaldav`). The app password is
/// left out of its `Debug`.
#[derive(Clone, PartialEq, Eq, uniffi::Record)]
pub struct CaldavFormView {
    /// Where the calendar lives, by its wire value: `icloud`, `fastmail`,
    /// `nextcloud` or `custom`.
    pub preset: String,
    /// The server's address.
    pub server_url: String,
    /// The user name.
    pub username: String,
    /// The app password.
    pub app_password: String,
}

impl std::fmt::Debug for CaldavFormView {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("CaldavFormView")
            .field("preset", &self.preset)
            .field("server_url", &self.server_url)
            .field("username", &self.username)
            .field("app_password", &"<redacted>")
            .finish()
    }
}

impl From<CaldavFormView> for CaldavForm {
    fn from(form: CaldavFormView) -> Self {
        Self {
            // A place this version does not list is the first.
            preset: CaldavPreset::ALL
                .into_iter()
                .find(|preset| preset.wire() == form.preset)
                .unwrap_or_default(),
            server_url: form.server_url,
            username: form.username,
            app_password: SchedulingSecret::new(form.app_password),
        }
    }
}

/// Something the member did on the calendars. Refresh, which reads what is
/// connected once the browser is done, is the window's own
/// (`UiEvent::Refresh`).
#[derive(Clone, Debug, PartialEq, Eq, uniffi::Enum)]
pub enum CalendarAction {
    /// Connect Google or Microsoft through its consent page in the browser.
    Connect {
        /// The provider, as `providers` lists it.
        provider: String,
    },
    /// Put away why the connect failed.
    DismissConnectFailure,
    /// Ask about disconnecting an account.
    AskDisconnect {
        /// The account's id.
        connection_id: String,
    },
    /// Keep it connected.
    CancelDisconnect,
    /// Disconnect it.
    ConfirmDisconnect,
    /// Put away what the last disconnect came to.
    DismissDisconnect,
    /// Open an account's calendars, or read them again.
    OpenCalendars {
        /// The account's id.
        connection_id: String,
    },
    /// A calendar is checked for conflicts, or not.
    SetCheckConflicts {
        /// The calendar's id.
        calendar_id: String,
        /// Whether.
        on: bool,
    },
    /// A calendar receives new bookings, and no other does.
    ChooseDestination {
        /// The calendar's id.
        calendar_id: String,
    },
    /// Save the calendars.
    SaveCalendars,
    /// Open the CalDAV form.
    OpenCaldav,
    /// The CalDAV form's fields changed.
    EditCaldav {
        /// The whole form, as it holds now.
        form: CaldavFormView,
    },
    /// Connect the CalDAV account.
    ConnectCaldav,
    /// Close the dialog open.
    CloseDialog,
}

/// The core events `action` is.
pub(crate) fn events(action: CalendarAction) -> Vec<Event> {
    use CalendarAction as A;
    use CalendarEvent as E;
    let event = match action {
        A::Connect { provider } => E::Connect(provider),
        A::DismissConnectFailure => E::DismissConnectFailure,
        A::AskDisconnect { connection_id } => E::AskDisconnect(connection_id),
        A::CancelDisconnect => E::CancelDisconnect,
        A::ConfirmDisconnect => E::ConfirmDisconnect,
        A::DismissDisconnect => E::DismissDisconnect,
        A::OpenCalendars { connection_id } => E::OpenCalendars(connection_id),
        A::SetCheckConflicts { calendar_id, on } => E::SetCheckConflicts { calendar_id, on },
        A::ChooseDestination { calendar_id } => E::ChooseDestination(calendar_id),
        A::SaveCalendars => E::SaveCalendars,
        A::OpenCaldav => E::OpenCaldav,
        A::EditCaldav { form } => E::EditCaldav(form.into()),
        A::ConnectCaldav => E::ConnectCaldav,
        A::CloseDialog => E::CloseDialog,
    };
    vec![Event::SchedulingAdmin(SchedulingAdminEvent::Calendar(
        event,
    ))]
}

/// The calendars' page. Before the section is entered, its reads are on
/// their way. Every write is offered whatever the member's role.
pub(crate) fn page(admin: &SchedulingAdmin) -> SchedulingAdminPage {
    let fresh = CalendarSection::default();
    SchedulingAdminPage::Calendar {
        view: view(admin.calendar.as_ref().unwrap_or(&fresh)),
    }
}

fn view(section: &CalendarSection) -> SchedulingCalendarView {
    let [
        account,
        conflicts,
        destination,
        connect_heading,
        choose,
        disconnect,
        disconnect_confirm,
        keep,
        last_destination,
    ] = CalendarSection::labels();
    let (connect, gone) = (&section.connect, &section.disconnect);
    let sentence = section.sentence().unwrap_or_default();
    let note = section.connect_note();
    let (mut picker, mut caldav) = (None, None);
    match &section.dialog {
        Some(CalendarDialog::Calendars(open)) => picker = Some(picker_view(open)),
        Some(CalendarDialog::Caldav(open)) => caldav = Some(caldav_view(open)),
        None => {}
    }
    SchedulingCalendarView {
        status: load_status(&section.status),
        connections: section
            .connections()
            .into_iter()
            .map(|card| CalendarConnectionView {
                id: card.id,
                provider: card.provider,
                account: card.account,
                conflicts: card.conflicts,
                destination: card.destination,
            })
            .collect(),
        account_label: account.to_owned(),
        conflicts_label: conflicts.to_owned(),
        destination_label: destination.to_owned(),
        choose_label: choose.to_owned(),
        disconnect_label: disconnect.to_owned(),
        can_disconnect: !gone.busy,
        connect_heading: connect_heading.to_owned(),
        sentence: sentence.to_owned(),
        zoom: section.zoom_line().map(|(label, value)| FactView {
            label: label.to_owned(),
            value: value.to_owned(),
        }),
        providers: section
            .offered_providers()
            .into_iter()
            .map(|(provider, label)| ChoiceRowView {
                value: provider.to_owned(),
                label,
                selected: false,
            })
            .collect(),
        connect_note: note.to_owned(),
        // A scheduler that can connect nothing says so once, in the sentence.
        shows_connect_note: sentence != note,
        connecting: connect.busy,
        awaiting_browser: connect.opened.is_some() && !connect.busy,
        connect_failure: failure(connect.failure.as_ref()),
        caldav_label: CaldavDialog::labels()[0].to_owned(),
        disconnecting: gone.busy,
        disconnect_failure: failure(gone.failure.as_ref()),
        disconnect_notice: gone
            .removed_destination
            .then(|| last_destination.to_owned()),
        disconnect: section.disconnect_prompt().map(|prompt| {
            dialog_view(
                [prompt.as_str(), disconnect_confirm, disconnect, keep],
                &WriteState::Idle,
                !gone.busy,
                true,
            )
        }),
        picker,
        caldav,
    }
}

/// One account's calendars.
fn picker_view(picker: &CalendarPicker) -> CalendarPickerView {
    let [
        conflicts_label,
        conflicts_hint,
        destination_label,
        destination_hint,
        loading,
        save,
        saving,
        cancel,
    ] = CalendarPicker::labels();
    let busy = picker.write.busy();
    let title = picker.title();
    let destination = picker.destination();
    CalendarPickerView {
        dialog: dialog_view(
            [title.as_str(), "", if busy { saving } else { save }, cancel],
            &picker.write,
            picker.can_save(),
            false,
        ),
        connection_id: picker.connection.id.clone(),
        status: load_status(&picker.rows),
        loading_label: loading.to_owned(),
        conflicts_label: conflicts_label.to_owned(),
        conflicts_hint: conflicts_hint.to_owned(),
        destination_label: destination_label.to_owned(),
        destination_hint: destination_hint.to_owned(),
        calendars: picker
            .rows
            .value()
            .map(Vec::as_slice)
            .unwrap_or_default()
            .iter()
            .map(|row| CalendarRowView {
                id: row.id.clone(),
                name: row.name.clone(),
                checks_conflicts: row.check_conflicts == Some(true),
            })
            .collect(),
        destinations: picker
            .destination_choices()
            .into_iter()
            .map(|row| ChoiceRowView {
                value: row.id.clone(),
                label: row.name.clone(),
                selected: destination == Some(row.id.as_str()),
            })
            .collect(),
        editable: picker.can_save(),
        notice: landed(&picker.write),
    }
}

/// The CalDAV form, without its app password.
fn caldav_view(caldav: &CaldavDialog) -> CaldavView {
    let [
        title,
        preset_label,
        server_url_label,
        server_url_hint,
        username_label,
        password_label,
        password_hint,
        connect,
        connecting,
        cancel,
    ] = CaldavDialog::labels();
    let busy = caldav.write.busy();
    let form = &caldav.form;
    let refused = |field: CaldavField| {
        caldav
            .refused
            .filter(|(refused, _)| *refused == field)
            .map(|(_, why)| why.to_owned())
    };
    CaldavView {
        dialog: dialog_view(
            [title, "", if busy { connecting } else { connect }, cancel],
            &caldav.write,
            true,
            false,
        ),
        preset_label: preset_label.to_owned(),
        presets: CaldavPreset::ALL
            .into_iter()
            .map(|preset| ChoiceRowView {
                value: preset.wire().to_owned(),
                label: preset.label().to_owned(),
                selected: preset == form.preset,
            })
            .collect(),
        preset_note: form.preset.note().to_owned(),
        shows_server_url: form.preset.needs_server_url(),
        server_url_label: server_url_label.to_owned(),
        server_url_hint: server_url_hint.to_owned(),
        username_label: username_label.to_owned(),
        password_label: password_label.to_owned(),
        password_hint: password_hint.to_owned(),
        server_url: form.server_url.clone(),
        username: form.username.clone(),
        server_url_error: refused(CaldavField::ServerUrl),
        username_error: refused(CaldavField::Username),
        password_error: refused(CaldavField::AppPassword),
        editable: !busy,
        notice: caldav.connected_line().or_else(|| landed(&caldav.write)),
    }
}
