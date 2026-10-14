//! The scheduler's settings: four tabs, the booking page (its name, images,
//! links and language), the booking assistant, the member's own scheduler
//! profile and picture, and their email notifications, as District AI for
//! iPhone's `SchedulingSettingsView` and its branding, assistant, profile and
//! notifications sheets.
//!
//! Only the tab showing is read, the first time it shows; the profile and the
//! notifications share one read. Each tab shows what was read as rows, and an
//! Edit that opens its dialog. The booking page and the assistant are the
//! workspace's, so their Edit needs a member who may change things; the
//! profile and the notifications are the member's own, so every member has
//! theirs. A dialog is open while the core holds it, and stays open saying
//! what its save came to.
//!
//! Images (the booking page's logo and banner, the member's picture) save as
//! soon as one is chosen or removed. C# opens the file chooser with
//! [`scheduling_image_pick`] and hands the bytes over; the core checks the
//! type, sniffed from the bytes, and the size, and says what it refused. The
//! window asks a removal once more, as on iPhone, its confirm never the
//! default.
//!
//! No value here is a secret: the profile is the member's own, and the
//! booking page is public.

use district_core::scheduling_admin::{
    AssistantEditor, BrandingEditor, BrandingForm, DATE_FORMATS, IMAGE_MAX_BYTES, IMAGE_TYPES,
    ImageFile, NotificationsEditor, ProfileEditor, ProfileForm, SectionLoad, SettingsDialog,
    SettingsEvent, SettingsSection, SettingsTab, TIME_FORMATS, WriteState, date_format_label,
    locale_label, notification_groups, notification_label, notification_value, set_label,
    time_format_label, week_start_label,
};
use district_core::{Event, SchedulingAdmin, SchedulingAdminEvent};
use district_l10n::scheduling_strings as ss;
use district_model::{
    SchedulingBranding, SchedulingLlmSettings, SchedulingMe, SchedulingUploadTarget,
};
use serde::Serialize;

use super::bookings::ChoiceRowView;
use super::event_types::SchedulingAskView;
use super::{DialogView, SchedulingAdminPage, dialog_view, landed, load_status};
use crate::files::{FilePickView, PickedFileView, picked_attachment};
use crate::l10n::l;
use crate::views::{FailureView, LoadStatus, failure};

/// Whether this version has the section's pages.
pub(crate) const BUILT: bool = true;

/// The pick for a booking page image or the member's picture: the
/// scheduler's image types, one file, read one byte past the largest it
/// takes, so the core sees a file is too large and says so.
#[uniffi::export]
pub fn scheduling_image_pick() -> FilePickView {
    FilePickView {
        read_cap: u64::try_from(IMAGE_MAX_BYTES + 1).unwrap_or(u64::MAX),
        ..crate::files::pick(&IMAGE_TYPES, 1)
    }
}

/// A tab of the settings.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash, Serialize, uniffi::Enum)]
pub enum SchedulingSettingsTab {
    /// The booking page's branding.
    BookingPage,
    /// The booking assistant.
    Assistant,
    /// The member's profile.
    Profile,
    /// The member's notifications.
    Notifications,
}

impl SchedulingSettingsTab {
    fn core(self) -> SettingsTab {
        match self {
            Self::BookingPage => SettingsTab::BookingPage,
            Self::Assistant => SettingsTab::Assistant,
            Self::Profile => SettingsTab::Profile,
            Self::Notifications => SettingsTab::Notifications,
        }
    }

    fn of(tab: SettingsTab) -> Self {
        match tab {
            SettingsTab::BookingPage => Self::BookingPage,
            SettingsTab::Assistant => Self::Assistant,
            SettingsTab::Profile => Self::Profile,
            SettingsTab::Notifications => Self::Notifications,
        }
    }
}

/// An image the settings hold.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash, Serialize, uniffi::Enum)]
pub enum SchedulingImageTarget {
    /// The booking page's logo.
    Logo,
    /// Its banner.
    Banner,
    /// The member's own picture.
    Avatar,
}

impl SchedulingImageTarget {
    fn core(self) -> SchedulingUploadTarget {
        match self {
            Self::Logo => SchedulingUploadTarget::Logo,
            Self::Banner => SchedulingUploadTarget::Banner,
            Self::Avatar => SchedulingUploadTarget::Avatar,
        }
    }
}

/// The settings.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct SchedulingSettingsView {
    /// The four tabs, the one showing selected.
    pub tabs: Vec<SettingsTabView>,
    /// Where the read of the tab showing stands. A failed one is a status
    /// page, never an empty tab.
    pub status: LoadStatus,
    /// What the tab showing read, as groups of rows; none until it is read.
    pub groups: Vec<SchedulingSettingsGroupView>,
    /// Whether Edit is offered on the tab showing: it is read, and it is the
    /// member's own or the member may change things.
    pub offers_edit: bool,
    /// "Edit" (`SchedulingSettingsAction::OpenEditor`).
    pub edit_label: String,
    /// The profile's dialog, while it is open.
    pub profile: Option<SettingsProfileView>,
    /// The notifications' dialog, while it is open.
    pub notifications: Option<SettingsNotificationsView>,
    /// The booking page's dialog, while it is open.
    pub branding: Option<SettingsBrandingView>,
    /// The assistant's dialog, while it is open.
    pub assistant: Option<SettingsAssistantView>,
}

/// One tab.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct SettingsTabView {
    /// Which.
    pub tab: SchedulingSettingsTab,
    /// "Booking page".
    pub label: String,
    /// Whether it is showing.
    pub selected: bool,
}

/// A group of rows: its heading, the sentence under it, and what was read.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct SchedulingSettingsGroupView {
    /// "Booking page", "As an attendee".
    pub heading: String,
    /// What the group is about; `None` for a group with no sentence.
    pub description: Option<String>,
    /// Its rows.
    pub rows: Vec<SchedulingSettingsRowView>,
}

/// One row: a label and what was read for it.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct SchedulingSettingsRowView {
    /// "Business name".
    pub label: String,
    /// "Contract Agency", "Not set", "On".
    pub value: String,
}

/// An image in a dialog: whether there is one, Choose image, and Remove
/// (asked once more by the window). It saves as soon as it is chosen or
/// removed, and says what that came to beside it.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct SettingsImageView {
    /// Which.
    pub target: SchedulingImageTarget,
    /// "Logo".
    pub label: String,
    /// "Set" or "Not set".
    pub value: String,
    /// "Choose image" (`SchedulingSettingsAction::UploadImage`).
    pub choose_label: String,
    /// "Remove".
    pub remove_label: String,
    /// Whether Remove shows: there is an image.
    pub shows_remove: bool,
    /// Whether Choose image and Remove can be pressed: nothing in the dialog
    /// is on its way.
    pub enabled: bool,
    /// Whether this image's upload or removal is on its way.
    pub working: bool,
    /// What its last upload or removal came to ("Logo uploaded").
    pub notice: Option<String>,
    /// Why its last upload or removal failed, or the file was refused.
    pub failure: Option<FailureView>,
    /// The removal's question, which the window asks before it sends
    /// `SchedulingSettingsAction::RemoveImage`: the image's name, that it saves at
    /// once, Remove and Cancel.
    pub remove_ask: SchedulingAskView,
}

/// The member's profile: their name, time zone, time and date formats, the
/// day their week starts on, and their picture. The save is the dialog's
/// confirm.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct SettingsProfileView {
    /// Its title, the save, "Cancel" and why the save failed.
    pub dialog: DialogView,
    /// "Name".
    pub name_label: String,
    /// The name, as the core holds it.
    pub name: String,
    /// "Time zone".
    pub timezone_label: String,
    /// Every time zone, the one chosen selected.
    pub timezones: Vec<ChoiceRowView>,
    /// "Time format".
    pub time_format_label: String,
    /// 12-hour and 24-hour, the one chosen selected.
    pub time_formats: Vec<ChoiceRowView>,
    /// "Week starts on".
    pub week_start_label: String,
    /// The seven days, Sunday first as the scheduler counts them, the one
    /// chosen selected.
    pub week_starts: Vec<ChoiceRowView>,
    /// "Date format".
    pub date_format_label: String,
    /// The date formats, the one chosen selected.
    pub date_formats: Vec<ChoiceRowView>,
    /// Why the fields cannot be sent, before anything was sent.
    pub error: Option<String>,
    /// Whether the fields can be changed: nothing is on its way.
    pub editable: bool,
    /// What the save came to ("Profile saved").
    pub notice: Option<String>,
    /// The types and size an image may be.
    pub image_hint: String,
    /// The member's picture.
    pub avatar: SettingsImageView,
}

/// The member's email notifications, as an attendee and as a host. The
/// save is the dialog's confirm.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct SettingsNotificationsView {
    /// Its title, the save, "Cancel" and why the save failed.
    pub dialog: DialogView,
    /// The switches, in their two groups.
    pub groups: Vec<NotificationGroupView>,
    /// Whether the switches can be changed: nothing is on its way.
    pub editable: bool,
    /// What the save came to.
    pub notice: Option<String>,
}

/// The notifications of one kind: as an attendee, or as a host.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct NotificationGroupView {
    /// "As an attendee".
    pub heading: String,
    /// What they are.
    pub description: String,
    /// Its switches.
    pub switches: Vec<NotificationSwitchView>,
}

/// One notification's switch.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct NotificationSwitchView {
    /// The scheduler's name for it (`notify_confirmation`), which keys it.
    pub field: String,
    /// "Booking confirmed".
    pub label: String,
    /// Whether it is on, as the core holds it.
    pub on: bool,
}

/// The booking page: its business name, the logo's height and opacity, the
/// banner's opacity, the privacy and terms links, the language it falls back
/// to, and its two images. The save is the dialog's confirm.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct SettingsBrandingView {
    /// Its title, the save, "Cancel" and why the save failed.
    pub dialog: DialogView,
    /// "Business name".
    pub business_name_label: String,
    /// What it is for.
    pub business_name_hint: String,
    /// The business name, as the core holds it.
    pub business_name: String,
    /// "Logo height".
    pub logo_height_label: String,
    /// Its range, in pixels.
    pub logo_height_hint: String,
    /// The logo's height, in pixels.
    pub logo_height: i64,
    /// The least height.
    pub logo_height_min: i64,
    /// The greatest height.
    pub logo_height_max: i64,
    /// "Logo opacity".
    pub logo_opacity_label: String,
    /// "Banner opacity".
    pub banner_opacity_label: String,
    /// An opacity's range, in percent.
    pub opacity_hint: String,
    /// The logo's opacity, in percent.
    pub logo_opacity: i64,
    /// The banner's opacity, in percent.
    pub banner_opacity: i64,
    /// The least opacity.
    pub opacity_min: i64,
    /// The greatest opacity.
    pub opacity_max: i64,
    /// "Privacy policy".
    pub privacy_label: String,
    /// The privacy policy's link, or empty.
    pub privacy_url: String,
    /// "Terms".
    pub terms_label: String,
    /// The terms' link, or empty.
    pub terms_url: String,
    /// That a link is a full address, or left empty.
    pub legal_url_hint: String,
    /// "Language".
    pub locale_label: String,
    /// What the language is for.
    pub locale_hint: String,
    /// The languages the page can fall back to, the one chosen selected.
    pub locales: Vec<ChoiceRowView>,
    /// Why the fields cannot be sent, before anything was sent.
    pub error: Option<String>,
    /// Whether the fields can be changed: nothing is on its way.
    pub editable: bool,
    /// What the save came to.
    pub notice: Option<String>,
    /// That the images save as soon as they are chosen or removed.
    pub images_note: String,
    /// The types and size an image may be.
    pub image_hint: String,
    /// The logo.
    pub logo: SettingsImageView,
    /// The banner.
    pub banner: SettingsImageView,
}

/// The booking assistant: on or off, and its extra instructions. The save is
/// the dialog's confirm, offered once something changed.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct SettingsAssistantView {
    /// Its title, the save, "Cancel" and why the save failed.
    pub dialog: DialogView,
    /// What the assistant does.
    pub hint: String,
    /// The switch's label.
    pub toggle_label: String,
    /// Whether it is on, as the core holds it.
    pub enabled: bool,
    /// "Extra instructions".
    pub instructions_label: String,
    /// What they are for.
    pub instructions_hint: String,
    /// The empty box's placeholder.
    pub instructions_placeholder: String,
    /// The instructions, as the core holds them.
    pub instructions: String,
    /// Why they cannot be sent, before anything was sent.
    pub error: Option<String>,
    /// Whether the fields can be changed: nothing is on its way.
    pub editable: bool,
    /// What the save came to.
    pub notice: Option<String>,
}

/// The profile's fields, as the member left them.
#[derive(Clone, Debug, PartialEq, Eq, uniffi::Record)]
pub struct SettingsProfileForm {
    /// The name.
    pub name: String,
    /// The IANA time zone.
    pub timezone: String,
    /// `12h` or `24h`.
    pub time_format: String,
    /// The day the week starts on, 0 for Sunday.
    pub week_start: i64,
    /// `dmy`, `mdy` or `ymd`.
    pub date_format: String,
}

/// The booking page's fields, as the member left them.
#[derive(Clone, Debug, PartialEq, Eq, uniffi::Record)]
pub struct SettingsBrandingForm {
    /// The business name.
    pub business_name: String,
    /// The logo's height in pixels.
    pub logo_height: i64,
    /// The logo's opacity in percent.
    pub logo_opacity: i64,
    /// The banner's opacity in percent.
    pub banner_opacity: i64,
    /// The privacy policy's link, or empty.
    pub privacy_url: String,
    /// The terms' link, or empty.
    pub terms_url: String,
    /// The language the page falls back to.
    pub fallback_locale: String,
}

/// Something the member did on the settings.
#[derive(Clone, Debug, PartialEq, Eq, uniffi::Enum)]
pub enum SchedulingSettingsAction {
    /// Show a tab, read the first time.
    SelectTab {
        /// Which.
        tab: SchedulingSettingsTab,
    },
    /// Read the tab showing again.
    Refresh,
    /// Open the dialog of `tab`.
    OpenEditor {
        /// The tab whose Edit was pressed.
        tab: SchedulingSettingsTab,
    },
    /// The profile's fields changed.
    EditProfile {
        /// The whole form.
        form: SettingsProfileForm,
    },
    /// Save the profile.
    SaveProfile,
    /// A notification switched.
    SetNotification {
        /// Which (`notify_confirmation`).
        field: String,
        /// On or off.
        on: bool,
    },
    /// Save the notifications.
    SaveNotifications,
    /// The booking page's fields changed.
    EditBranding {
        /// The whole form.
        form: SettingsBrandingForm,
    },
    /// Save the booking page.
    SaveBranding,
    /// Upload an image the member picked.
    UploadImage {
        /// Which.
        target: SchedulingImageTarget,
        /// The file, as C# read it; its type is sniffed from its bytes.
        file: PickedFileView,
    },
    /// The image picked could not be read.
    ImageUnreadable {
        /// Which.
        target: SchedulingImageTarget,
    },
    /// Remove an image, once the window asked.
    RemoveImage {
        /// Which.
        target: SchedulingImageTarget,
    },
    /// The assistant's fields changed.
    EditAssistant {
        /// Whether it is on.
        enabled: bool,
        /// The extra instructions.
        instructions: String,
    },
    /// Save the assistant.
    SaveAssistant,
    /// Close the dialog open.
    CloseDialog,
}

/// The core events `action` is.
pub(crate) fn events(action: SchedulingSettingsAction) -> Vec<Event> {
    use SchedulingSettingsAction as A;
    use SettingsEvent as E;
    let event = match action {
        A::SelectTab { tab } => E::SelectTab(tab.core()),
        A::Refresh => E::Refresh,
        A::OpenEditor { tab } => match tab {
            SchedulingSettingsTab::BookingPage => E::OpenBranding,
            SchedulingSettingsTab::Assistant => E::OpenAssistant,
            SchedulingSettingsTab::Profile => E::OpenProfile,
            SchedulingSettingsTab::Notifications => E::OpenNotifications,
        },
        A::EditProfile { form } => E::EditProfile(ProfileForm {
            name: form.name,
            timezone: form.timezone,
            time_format: form.time_format,
            week_start: form.week_start,
            date_format: form.date_format,
        }),
        A::SaveProfile => E::SaveProfile,
        A::SetNotification { field, on } => E::SetNotification { field, on },
        A::SaveNotifications => E::SaveNotifications,
        A::EditBranding { form } => E::EditBranding(BrandingForm {
            business_name: form.business_name,
            logo_height: form.logo_height,
            logo_opacity: form.logo_opacity,
            banner_opacity: form.banner_opacity,
            privacy_url: form.privacy_url,
            terms_url: form.terms_url,
            fallback_locale: form.fallback_locale,
        }),
        A::SaveBranding => E::SaveBranding,
        A::UploadImage { target, file } => {
            let picked = picked_attachment(file);
            E::UploadImage {
                target: target.core(),
                file: ImageFile {
                    mime_type: picked.mime_type,
                    bytes: picked.bytes,
                },
            }
        }
        A::ImageUnreadable { target } => E::ImageUnreadable(target.core()),
        A::RemoveImage { target } => E::RemoveImage(target.core()),
        A::EditAssistant {
            enabled,
            instructions,
        } => E::EditAssistant {
            enabled,
            instructions,
        },
        A::SaveAssistant => E::SaveAssistant,
        A::CloseDialog => E::CloseDialog,
    };
    vec![Event::SchedulingAdmin(SchedulingAdminEvent::Settings(
        event,
    ))]
}

/// The settings' page, for a member who may change things when
/// `can_change`. Before the section is entered, its first tab's read is on
/// its way.
pub(crate) fn page(admin: &SchedulingAdmin, can_change: bool) -> SchedulingAdminPage {
    let fresh = SettingsSection::default();
    SchedulingAdminPage::Settings {
        view: view(admin.settings.as_ref().unwrap_or(&fresh), can_change),
    }
}

fn view(section: &SettingsSection, can_change: bool) -> SchedulingSettingsView {
    let (status, groups) = match section.tab {
        SettingsTab::BookingPage => read(section.branding.as_ref(), branding_groups),
        SettingsTab::Assistant => read(section.assistant.as_ref(), assistant_groups),
        SettingsTab::Profile => read(section.profile.as_ref(), profile_groups),
        SettingsTab::Notifications => read(section.profile.as_ref(), notification_rows),
    };
    let own = matches!(
        section.tab,
        SettingsTab::Profile | SettingsTab::Notifications
    );
    let (mut profile, mut notifications, mut branding, mut assistant) = (None, None, None, None);
    match &section.dialog {
        Some(SettingsDialog::Profile(editor)) => profile = Some(profile_view(editor)),
        Some(SettingsDialog::Notifications(editor)) => {
            notifications = Some(notifications_view(editor));
        }
        Some(SettingsDialog::Branding(editor)) => branding = Some(branding_view(editor)),
        Some(SettingsDialog::Assistant(editor)) => assistant = Some(assistant_view(editor)),
        None => {}
    }
    SchedulingSettingsView {
        tabs: SettingsTab::ALL
            .into_iter()
            .map(|tab| SettingsTabView {
                tab: SchedulingSettingsTab::of(tab),
                label: tab.label().to_owned(),
                selected: tab == section.tab,
            })
            .collect(),
        offers_edit: status == LoadStatus::Ready && (own || can_change),
        status,
        groups,
        edit_label: ss::common_edit(l()).to_owned(),
        profile,
        notifications,
        branding,
        assistant,
    }
}

/// Where a tab's read stands (one not asked yet is on its way), and its
/// groups once read.
fn read<T>(
    load: Option<&SectionLoad<T>>,
    groups: fn(&T) -> Vec<SchedulingSettingsGroupView>,
) -> (LoadStatus, Vec<SchedulingSettingsGroupView>) {
    match load {
        None => (LoadStatus::Loading, Vec::new()),
        Some(load) => (
            load_status(load),
            load.value().map(groups).unwrap_or_default(),
        ),
    }
}

/// `value`, or "Not set" when there is none.
fn or_not_set(value: &str) -> String {
    if value.trim().is_empty() {
        ss::settings_not_set(l()).to_owned()
    } else {
        value.to_owned()
    }
}

/// "On" or "Off".
fn on_off(on: bool) -> String {
    let [.., on_label, off_label] = SettingsSection::labels();
    (if on { on_label } else { off_label }).to_owned()
}

fn row(label: &str, value: String) -> SchedulingSettingsRowView {
    SchedulingSettingsRowView {
        label: label.to_owned(),
        value,
    }
}

fn group(
    heading: &str,
    description: &str,
    rows: Vec<SchedulingSettingsRowView>,
) -> SchedulingSettingsGroupView {
    SchedulingSettingsGroupView {
        heading: heading.to_owned(),
        description: (!description.is_empty()).then(|| description.to_owned()),
        rows,
    }
}

/// The booking page as read: its name, its images, its links and its
/// language.
fn branding_groups(branding: &SchedulingBranding) -> Vec<SchedulingSettingsGroupView> {
    vec![group(
        SettingsTab::BookingPage.label(),
        "",
        vec![
            row(
                ss::settings_branding_business_name(l()),
                or_not_set(&branding.business_name),
            ),
            row(
                ss::settings_branding_logo(l()),
                set_label(&branding.logo_url).to_owned(),
            ),
            row(
                ss::settings_branding_banner(l()),
                set_label(&branding.banner_url).to_owned(),
            ),
            row(
                ss::settings_branding_privacy(l()),
                or_not_set(&branding.privacy_url),
            ),
            row(
                ss::settings_branding_terms(l()),
                or_not_set(&branding.terms_url),
            ),
            row(
                ss::settings_branding_locale(l()),
                locale_label(
                    &branding.fallback_locale,
                    branding.supported_locales.as_deref(),
                ),
            ),
        ],
    )]
}

/// The assistant as read: on or off, and its extra instructions when it has
/// some.
fn assistant_groups(llm: &SchedulingLlmSettings) -> Vec<SchedulingSettingsGroupView> {
    let [hint, enabled, instructions, ..] = SettingsSection::labels();
    let mut rows = vec![row(enabled, on_off(llm.enabled))];
    if !llm.extra_instructions.trim().is_empty() {
        rows.push(row(instructions, llm.extra_instructions.clone()));
    }
    vec![group(SettingsTab::Assistant.label(), hint, rows)]
}

/// The member's profile as read.
fn profile_groups(me: &SchedulingMe) -> Vec<SchedulingSettingsGroupView> {
    vec![group(
        SettingsTab::Profile.label(),
        "",
        vec![
            row(ss::settings_profile_name(l()), or_not_set(&me.name)),
            row(ss::settings_profile_timezone(l()), or_not_set(&me.timezone)),
            row(
                ss::settings_profile_time_format(l()),
                time_format_label(&me.time_format),
            ),
            row(
                ss::settings_profile_week_start(l()),
                week_start_label(me.week_start),
            ),
            row(
                ss::settings_profile_date_format(l()),
                date_format_label(&me.date_format),
            ),
        ],
    )]
}

/// The member's notifications as read, as an attendee and as a host.
fn notification_rows(me: &SchedulingMe) -> Vec<SchedulingSettingsGroupView> {
    notification_groups()
        .into_iter()
        .map(|(heading, description, fields)| {
            let rows = fields
                .iter()
                .map(|field| {
                    row(
                        notification_label(field),
                        on_off(notification_value(field, me)),
                    )
                })
                .collect();
            group(heading, description, rows)
        })
        .collect()
}

/// An image of a dialog: `url` is the image's address, empty for none, and
/// `state` its upload or removal; `enabled` when nothing in the dialog is on
/// its way.
fn image_view(
    target: SchedulingImageTarget,
    label: &str,
    url: &str,
    state: &WriteState,
    enabled: bool,
) -> SettingsImageView {
    let remove = ss::settings_image_remove(l());
    SettingsImageView {
        target,
        label: label.to_owned(),
        value: set_label(url).to_owned(),
        choose_label: ss::settings_image_choose(l()).to_owned(),
        remove_label: remove.to_owned(),
        shows_remove: !url.is_empty(),
        enabled,
        working: state.busy(),
        notice: landed(state),
        failure: failure(state.failure()),
        remove_ask: SchedulingAskView {
            title: label.to_owned(),
            body: ss::settings_image_immediate(l()).to_owned(),
            confirm: remove.to_owned(),
            cancel: ss::common_cancel(l()).to_owned(),
        },
    }
}

/// `values` as choices, each read by `label`, `chosen` selected.
fn choices<T: PartialEq>(
    values: impl IntoIterator<Item = T>,
    chosen: &T,
    value: impl Fn(&T) -> String,
    label: impl Fn(&T) -> String,
) -> Vec<ChoiceRowView> {
    values
        .into_iter()
        .map(|v| ChoiceRowView {
            value: value(&v),
            label: label(&v),
            selected: v == *chosen,
        })
        .collect()
}

/// The member's profile.
fn profile_view(editor: &ProfileEditor) -> SettingsProfileView {
    let [
        title,
        name_label,
        timezone_label,
        time_format_label_text,
        week_start_label_text,
        date_format_label_text,
        avatar_label,
        image_hint,
        _choose,
        _remove,
        save,
    ] = ProfileEditor::labels();
    let form = &editor.form;
    let busy = editor.busy();
    SettingsProfileView {
        dialog: dialog_view(
            [title, "", save, ss::common_cancel(l())],
            &editor.write,
            !editor.avatar.busy(),
            false,
        ),
        name_label: name_label.to_owned(),
        name: form.name.clone(),
        timezone_label: timezone_label.to_owned(),
        timezones: choices(
            editor.timezone_options(),
            &form.timezone,
            String::clone,
            String::clone,
        ),
        time_format_label: time_format_label_text.to_owned(),
        time_formats: choices(
            TIME_FORMATS.map(str::to_owned),
            &form.time_format,
            String::clone,
            |v| time_format_label(v),
        ),
        week_start_label: week_start_label_text.to_owned(),
        week_starts: choices(0..7, &form.week_start, i64::to_string, |v| {
            week_start_label(*v)
        }),
        date_format_label: date_format_label_text.to_owned(),
        date_formats: choices(
            DATE_FORMATS.map(str::to_owned),
            &form.date_format,
            String::clone,
            |v| date_format_label(v),
        ),
        error: editor.rejected.map(str::to_owned),
        editable: !busy,
        notice: landed(&editor.write),
        image_hint: image_hint.to_owned(),
        avatar: image_view(
            SchedulingImageTarget::Avatar,
            avatar_label,
            editor.avatar_url().unwrap_or_default(),
            &editor.avatar,
            !busy,
        ),
    }
}

/// The member's notifications.
fn notifications_view(editor: &NotificationsEditor) -> SettingsNotificationsView {
    let [title, save] = NotificationsEditor::labels();
    SettingsNotificationsView {
        dialog: dialog_view(
            [title, "", save, ss::common_cancel(l())],
            &editor.write,
            true,
            false,
        ),
        groups: notification_groups()
            .into_iter()
            .map(|(heading, description, fields)| NotificationGroupView {
                heading: heading.to_owned(),
                description: description.to_owned(),
                switches: fields
                    .iter()
                    .map(|field| NotificationSwitchView {
                        field: (*field).to_owned(),
                        label: notification_label(field).to_owned(),
                        on: editor.value(field),
                    })
                    .collect(),
            })
            .collect(),
        editable: !editor.write.busy(),
        notice: landed(&editor.write),
    }
}

/// The booking page.
fn branding_view(editor: &BrandingEditor) -> SettingsBrandingView {
    let [
        title,
        business_name_label,
        business_name_hint,
        logo_height_label,
        logo_height_hint,
        logo_opacity_label,
        banner_opacity_label,
        opacity_hint,
        privacy_label,
        terms_label,
        legal_url_hint,
        locale_label_text,
        locale_hint,
        images_note,
        save,
    ] = BrandingEditor::labels();
    let form = &editor.form;
    let busy = editor.busy();
    let options = editor.locale_options();
    let chosen = form.fallback_locale.trim();
    let mut locales: Vec<ChoiceRowView> = options
        .iter()
        .map(|option| ChoiceRowView {
            value: option.code.clone(),
            label: locale_label(&option.code, Some(options)),
            selected: option.code == chosen,
        })
        .collect();
    if !chosen.is_empty() && !locales.iter().any(|choice| choice.selected) {
        // A language the scheduler no longer offers: kept, first, as it is.
        locales.insert(
            0,
            ChoiceRowView {
                value: chosen.to_owned(),
                label: chosen.to_owned(),
                selected: true,
            },
        );
    }
    let branding = &editor.branding;
    SettingsBrandingView {
        dialog: dialog_view(
            [title, "", save, ss::common_cancel(l())],
            &editor.write,
            !editor.logo.busy() && !editor.banner.busy(),
            false,
        ),
        business_name_label: business_name_label.to_owned(),
        business_name_hint: business_name_hint.to_owned(),
        business_name: form.business_name.clone(),
        logo_height_label: logo_height_label.to_owned(),
        logo_height_hint: logo_height_hint.to_owned(),
        logo_height: form.logo_height,
        logo_height_min: *district_core::scheduling_admin::LOGO_HEIGHTS.start(),
        logo_height_max: *district_core::scheduling_admin::LOGO_HEIGHTS.end(),
        logo_opacity_label: logo_opacity_label.to_owned(),
        banner_opacity_label: banner_opacity_label.to_owned(),
        opacity_hint: opacity_hint.to_owned(),
        logo_opacity: form.logo_opacity,
        banner_opacity: form.banner_opacity,
        opacity_min: *district_core::scheduling_admin::OPACITIES.start(),
        opacity_max: *district_core::scheduling_admin::OPACITIES.end(),
        privacy_label: privacy_label.to_owned(),
        privacy_url: form.privacy_url.clone(),
        terms_label: terms_label.to_owned(),
        terms_url: form.terms_url.clone(),
        legal_url_hint: legal_url_hint.to_owned(),
        locale_label: locale_label_text.to_owned(),
        locale_hint: locale_hint.to_owned(),
        locales,
        error: editor.rejected.clone(),
        editable: !busy,
        notice: landed(&editor.write),
        images_note: images_note.to_owned(),
        image_hint: ss::settings_image_hint(l()).to_owned(),
        logo: image_view(
            SchedulingImageTarget::Logo,
            ss::settings_branding_logo(l()),
            &branding.logo_url,
            &editor.logo,
            !busy,
        ),
        banner: image_view(
            SchedulingImageTarget::Banner,
            ss::settings_branding_banner(l()),
            &branding.banner_url,
            &editor.banner,
            !busy,
        ),
    }
}

/// The booking assistant.
fn assistant_view(editor: &AssistantEditor) -> SettingsAssistantView {
    let [
        title,
        hint,
        toggle,
        instructions_label,
        instructions_hint,
        placeholder,
        save,
    ] = AssistantEditor::labels();
    SettingsAssistantView {
        dialog: dialog_view(
            [title, "", save, ss::common_cancel(l())],
            &editor.write,
            editor.is_dirty(),
            false,
        ),
        hint: hint.to_owned(),
        toggle_label: toggle.to_owned(),
        enabled: editor.enabled,
        instructions_label: instructions_label.to_owned(),
        instructions_hint: instructions_hint.to_owned(),
        instructions_placeholder: placeholder.to_owned(),
        instructions: editor.instructions.clone(),
        error: editor.rejected.map(str::to_owned),
        editable: !editor.write.busy(),
        notice: landed(&editor.write),
    }
}
