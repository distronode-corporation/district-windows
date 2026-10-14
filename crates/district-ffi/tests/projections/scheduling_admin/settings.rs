//! The settings (src/scheduling_admin/settings.rs): the four tabs and what
//! each read, and the booking page's, the assistant's, the profile's and the
//! notifications' dialogs, with the images that save at once.
//!
//! The booking page, the assistant and the member's profile are the
//! fixtures': Contract Agency with a logo and a banner, an assistant that is
//! on with one instruction, and Contract Member in America/Toronto. The
//! profile's dialog lists every time zone the core knows, which depends on
//! the time zone database it runs with, so it is checked by tests here and
//! never pinned in a snapshot.

use district_core::SchedulingAdminEvent;
use district_core::scheduling_admin::{
    BrandingForm, ImageFile, ProfileForm, SettingsEvent, SettingsTab,
};
use district_ffi::LoadStatus;
use district_ffi::composer::PickedFileView;
use district_ffi::scheduling_admin::settings::{
    SchedulingImageTarget, SchedulingSettingsAction, SchedulingSettingsTab, SchedulingSettingsView,
    SettingsBrandingForm, SettingsProfileForm, scheduling_image_pick,
};
use district_model::SchedulingUploadTarget;

use super::*;

/// This area's snapshot cases.
pub(crate) fn cases() -> Vec<Case> {
    vec![
        (
            "scheduling-admin-settings-loading",
            open(signed_in(), SchedulingAdminSection::Settings),
        ),
        ("scheduling-admin-settings", booking_page(signed_in())),
        (
            "scheduling-admin-settings-failed",
            branding_read(
                open(signed_in(), SchedulingAdminSection::Settings),
                Err(server_error()),
            ),
        ),
        ("scheduling-admin-settings-viewer", booking_page(viewer())),
        (
            "scheduling-admin-settings-assistant",
            assistant(signed_in()),
        ),
        ("scheduling-admin-settings-profile", profile(signed_in())),
        (
            "scheduling-admin-settings-notifications",
            notifications(signed_in()),
        ),
        (
            "scheduling-admin-settings-branding-open",
            branding_open(signed_in()),
        ),
        (
            "scheduling-admin-settings-branding-invalid",
            branding_open(signed_in())
                .ui(event(SchedulingSettingsAction::EditBranding {
                    form: branding_form(10),
                }))
                .ui(event(SchedulingSettingsAction::SaveBranding)),
        ),
        (
            "scheduling-admin-settings-branding-saved",
            branding_saved(signed_in()),
        ),
        (
            "scheduling-admin-settings-logo-refused",
            branding_open(signed_in()).ui(event(SchedulingSettingsAction::UploadImage {
                target: SchedulingImageTarget::Logo,
                file: picked(b"plain text, not an image"),
            })),
        ),
        (
            "scheduling-admin-settings-logo-removing",
            logo_removing(signed_in()),
        ),
        (
            "scheduling-admin-settings-logo-removed",
            logo_removed(signed_in()),
        ),
        (
            "scheduling-admin-settings-notifications-open",
            notifications_open(signed_in()),
        ),
        (
            "scheduling-admin-settings-notifications-saved",
            notifications_saved(signed_in()),
        ),
        (
            "scheduling-admin-settings-assistant-open",
            assistant_open(signed_in()),
        ),
        (
            "scheduling-admin-settings-assistant-saved",
            assistant_saved(signed_in()),
        ),
    ]
}

/// A PNG's first bytes, which the sniffing reads as a PNG.
const PNG: &[u8] = &[0x89, b'P', b'N', b'G', 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0];

fn event(action: SchedulingSettingsAction) -> UiEvent {
    UiEvent::SchedulingAdmin {
        action: SchedulingAdminAction::Settings { action },
    }
}

fn picked(bytes: &[u8]) -> PickedFileView {
    PickedFileView {
        file_name: "logo.png".to_owned(),
        size: u64::try_from(bytes.len()).expect("small"),
        bytes: bytes.to_vec(),
    }
}

/// The booking page's form as the fixture reads, with the logo `height`
/// pixels high.
fn branding_form(height: i64) -> SettingsBrandingForm {
    SettingsBrandingForm {
        business_name: "Contract Agency".to_owned(),
        logo_height: height,
        logo_opacity: 100,
        banner_opacity: 80,
        privacy_url: String::new(),
        terms_url: "https://www.contract.test/terms".to_owned(),
        fallback_locale: "en".to_owned(),
    }
}

/// The newest request `pick` finds, answered with the fixture `name`, or
/// the failure.
fn answer(
    session: Session,
    pick: fn(&SchedulingAdminRequest) -> bool,
    result: Result<&str, ApiError>,
) -> Session {
    answer_admin(session, pick, result.map(contracts::json))
}

/// The booking page's read, answered with `result`.
fn branding_read(session: Session, result: Result<Value, ApiError>) -> Session {
    answer_admin(
        session,
        |r| matches!(r, SchedulingAdminRequest::Branding),
        result,
    )
}

/// The settings, on the booking page, read.
fn booking_page(session: Session) -> Session {
    branding_read(
        open(session, SchedulingAdminSection::Settings),
        Ok(contracts::json("district-scheduling-branding.json")),
    )
}

/// The assistant's tab, read.
fn assistant(session: Session) -> Session {
    answer(
        booking_page(session).ui(event(SchedulingSettingsAction::SelectTab {
            tab: SchedulingSettingsTab::Assistant,
        })),
        |r| matches!(r, SchedulingAdminRequest::Llm),
        Ok("district-scheduling-llm.json"),
    )
}

/// The profile's tab, read.
fn profile(session: Session) -> Session {
    answer(
        booking_page(session).ui(event(SchedulingSettingsAction::SelectTab {
            tab: SchedulingSettingsTab::Profile,
        })),
        |r| matches!(r, SchedulingAdminRequest::Me),
        Ok("district-scheduling-me.json"),
    )
}

/// The notifications' tab, after the profile's read, which it shares.
fn notifications(session: Session) -> Session {
    profile(session).ui(event(SchedulingSettingsAction::SelectTab {
        tab: SchedulingSettingsTab::Notifications,
    }))
}

/// The booking page's dialog, open.
fn branding_open(session: Session) -> Session {
    booking_page(session).ui(event(SchedulingSettingsAction::OpenEditor {
        tab: SchedulingSettingsTab::BookingPage,
    }))
}

/// The logo made 40 pixels high and the booking page saved.
fn branding_saved(session: Session) -> Session {
    answer(
        branding_open(session)
            .ui(event(SchedulingSettingsAction::EditBranding {
                form: branding_form(40),
            }))
            .ui(event(SchedulingSettingsAction::SaveBranding)),
        |r| matches!(r, SchedulingAdminRequest::PatchBranding(_)),
        Ok("district-scheduling-branding.json"),
    )
}

/// The logo's removal, on its way.
fn logo_removing(session: Session) -> Session {
    branding_open(session).ui(event(SchedulingSettingsAction::RemoveImage {
        target: SchedulingImageTarget::Logo,
    }))
}

/// The logo removed, and the booking page read again without it.
fn logo_removed(session: Session) -> Session {
    let mut branding = contracts::json("district-scheduling-branding.json");
    branding["data"]["logo_url"] = json!("");
    branding_read(
        answer(
            logo_removing(session),
            |r| matches!(r, SchedulingAdminRequest::DeleteLogo),
            Ok("district-scheduling-ok.json"),
        ),
        Ok(branding),
    )
}

/// The notifications' dialog, open.
fn notifications_open(session: Session) -> Session {
    notifications(session).ui(event(SchedulingSettingsAction::OpenEditor {
        tab: SchedulingSettingsTab::Notifications,
    }))
}

/// The reschedule switched on and the notifications saved.
fn notifications_saved(session: Session) -> Session {
    answer(
        notifications_open(session)
            .ui(event(SchedulingSettingsAction::SetNotification {
                field: "notify_reschedule".to_owned(),
                on: true,
            }))
            .ui(event(SchedulingSettingsAction::SaveNotifications)),
        |r| matches!(r, SchedulingAdminRequest::PatchMe(_)),
        Ok("district-scheduling-me.json"),
    )
}

/// The assistant's dialog, open.
fn assistant_open(session: Session) -> Session {
    assistant(session).ui(event(SchedulingSettingsAction::OpenEditor {
        tab: SchedulingSettingsTab::Assistant,
    }))
}

/// The assistant switched off and saved.
fn assistant_saved(session: Session) -> Session {
    let mut llm = contracts::json("district-scheduling-llm.json");
    llm["data"]["enabled"] = json!(false);
    answer_admin(
        assistant_open(session)
            .ui(event(SchedulingSettingsAction::EditAssistant {
                enabled: false,
                instructions: "Always list the agreed next steps first.".to_owned(),
            }))
            .ui(event(SchedulingSettingsAction::SaveAssistant)),
        |r| matches!(r, SchedulingAdminRequest::PatchLlm { .. }),
        Ok(llm),
    )
}

/// The profile's dialog, open.
fn profile_open(session: Session) -> Session {
    profile(session).ui(event(SchedulingSettingsAction::OpenEditor {
        tab: SchedulingSettingsTab::Profile,
    }))
}

fn settings_view(session: &Session) -> SchedulingSettingsView {
    let SchedulingAdminPage::Settings { view } = admin_view(session).page else {
        panic!("not the settings");
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

/// Each tab reads what it shows the first time it shows, as rows, with Edit
/// once read; a viewer is offered Edit on their own profile and
/// notifications only.
#[test]
fn each_tab_shows_what_it_read() {
    let loading = settings_view(&open(signed_in(), SchedulingAdminSection::Settings));
    assert_eq!(loading.status, LoadStatus::Loading);
    assert!(loading.groups.is_empty() && !loading.offers_edit);
    let labels: Vec<&str> = loading.tabs.iter().map(|t| t.label.as_str()).collect();
    assert_eq!(
        labels,
        [
            "Booking page",
            "Booking assistant",
            "Your profile",
            "Your notifications"
        ]
    );
    assert!(loading.tabs[0].selected && !loading.tabs[1].selected);

    let page = settings_view(&booking_page(signed_in()));
    assert_eq!(page.status, LoadStatus::Ready);
    assert!(page.offers_edit);
    assert_eq!(page.edit_label, "Edit");
    let rows: Vec<(&str, &str)> = page.groups[0]
        .rows
        .iter()
        .map(|r| (r.label.as_str(), r.value.as_str()))
        .collect();
    assert_eq!(
        rows,
        [
            ("Business name", "Contract Agency"),
            ("Logo", "Set"),
            ("Banner", "Set"),
            ("Privacy policy link", "Not set"),
            ("Terms link", "https://www.contract.test/terms"),
            ("Page language", "English"),
        ]
    );

    let assistant = settings_view(&assistant(signed_in()));
    assert!(assistant.tabs[1].selected);
    assert!(assistant.groups[0].description.is_some());
    assert_eq!(assistant.groups[0].rows[0].value, "On");
    assert_eq!(
        assistant.groups[0].rows[1].value,
        "Always list the agreed next steps first."
    );

    let profile = settings_view(&profile(signed_in()));
    let values: Vec<&str> = profile.groups[0]
        .rows
        .iter()
        .map(|r| r.value.as_str())
        .collect();
    assert_eq!(values[0], "Contract Member");
    assert_eq!(values[1], "America/Toronto");

    // The notifications share the profile's read: nothing more is asked.
    let session = notifications(signed_in());
    assert!(!sent(&session, |r| matches!(r, SchedulingAdminRequest::Me)));
    let notifications = settings_view(&session);
    assert_eq!(notifications.groups.len(), 2);
    assert_eq!(notifications.groups[0].rows.len(), 4);
    assert_eq!(notifications.groups[1].rows.len(), 3);
    assert!(notifications.offers_edit);

    // A viewer: the workspace's tabs offer no Edit, their own do.
    assert!(!settings_view(&booking_page(viewer())).offers_edit);
    let viewer_profile = answer(
        booking_page(viewer()).ui(event(SchedulingSettingsAction::SelectTab {
            tab: SchedulingSettingsTab::Profile,
        })),
        |r| matches!(r, SchedulingAdminRequest::Me),
        Ok("district-scheduling-me.json"),
    );
    assert!(settings_view(&viewer_profile).offers_edit);

    // A failed read is a status page, and Refresh reads the tab again.
    let failed = branding_read(
        open(signed_in(), SchedulingAdminSection::Settings),
        Err(server_error()),
    );
    assert!(matches!(
        settings_view(&failed).status,
        LoadStatus::Failed { .. }
    ));
    assert!(settings_view(&failed).groups.is_empty());
    let again = failed.ui(event(SchedulingSettingsAction::Refresh));
    assert!(sent(&again, |r| matches!(
        r,
        SchedulingAdminRequest::Branding
    )));
    assert_eq!(settings_view(&again).status, LoadStatus::Loading);
}

/// The booking page's dialog: its fields as read, a refusal before anything
/// is sent, the save, and the images, each saving on its own and refused
/// when they are not an image.
#[test]
fn the_booking_page_saves_and_its_images_save_at_once() {
    let open = settings_view(&branding_open(signed_in()))
        .branding
        .expect("open");
    assert_eq!(open.dialog.title, "Booking page");
    assert!(open.dialog.can_confirm && !open.dialog.destructive);
    assert_eq!(open.business_name, "Contract Agency");
    assert_eq!(
        (open.logo_height, open.logo_opacity, open.banner_opacity),
        (48, 100, 80)
    );
    assert_eq!((open.logo_height_min, open.logo_height_max), (16, 64));
    assert_eq!((open.opacity_min, open.opacity_max), (20, 100));
    let locales: Vec<(&str, &str, bool)> = open
        .locales
        .iter()
        .map(|c| (c.value.as_str(), c.label.as_str(), c.selected))
        .collect();
    assert_eq!(
        locales,
        [("en", "English", true), ("fr", "Français", false)]
    );
    assert!(open.logo.shows_remove && open.logo.enabled && !open.logo.working);
    assert_eq!(open.logo.value, "Set");
    assert_eq!(open.logo.choose_label, "Choose image");
    assert_eq!(open.logo.remove_ask.title, "Logo");
    assert_eq!(open.logo.remove_ask.confirm, "Remove");
    assert_eq!(open.logo.remove_ask.cancel, "Cancel");
    assert_eq!(open.banner.target, SchedulingImageTarget::Banner);

    let invalid = settings_view(
        &branding_open(signed_in())
            .ui(event(SchedulingSettingsAction::EditBranding {
                form: branding_form(10),
            }))
            .ui(event(SchedulingSettingsAction::SaveBranding)),
    )
    .branding
    .expect("open");
    assert!(invalid.error.is_some());

    let saving = branding_open(signed_in())
        .ui(event(SchedulingSettingsAction::EditBranding {
            form: branding_form(40),
        }))
        .ui(event(SchedulingSettingsAction::SaveBranding));
    assert!(sent(&saving, |r| matches!(
        r,
        SchedulingAdminRequest::PatchBranding(patch) if patch.logo_height == 40
    )));
    let working = settings_view(&saving).branding.expect("open");
    assert!(working.dialog.working && !working.editable && !working.logo.enabled);

    let saved = settings_view(&branding_saved(signed_in()))
        .branding
        .expect("open");
    assert!(saved.notice.is_some() && saved.editable);

    // A file that is not an image is refused before anything is sent.
    let refused = branding_open(signed_in()).ui(event(SchedulingSettingsAction::UploadImage {
        target: SchedulingImageTarget::Logo,
        file: picked(b"plain text, not an image"),
    }));
    assert!(!sent(&refused, |r| matches!(
        r,
        SchedulingAdminRequest::Upload(_)
    )));
    assert!(
        settings_view(&refused)
            .branding
            .expect("open")
            .logo
            .failure
            .is_some()
    );

    // An image picked goes as its sniffed type.
    let uploading = branding_open(signed_in()).ui(event(SchedulingSettingsAction::UploadImage {
        target: SchedulingImageTarget::Banner,
        file: picked(PNG),
    }));
    assert!(sent(&uploading, |r| matches!(
        r,
        SchedulingAdminRequest::Upload(upload) if upload.mime_type == "image/png"
            && upload.target == SchedulingUploadTarget::Banner
    )));
    let view = settings_view(&uploading).branding.expect("open");
    assert!(view.banner.working && !view.logo.working && !view.logo.enabled);

    let unreadable = settings_view(&branding_open(signed_in()).ui(event(
        SchedulingSettingsAction::ImageUnreadable {
            target: SchedulingImageTarget::Logo,
        },
    )))
    .branding
    .expect("open");
    assert!(unreadable.logo.failure.is_some());

    // The removal lands, and the page is read again without the logo.
    assert!(sent(&logo_removing(signed_in()), |r| matches!(
        r,
        SchedulingAdminRequest::DeleteLogo
    )));
    let removed_session = logo_removed(signed_in());
    let removed = settings_view(&removed_session);
    let dialog = removed.branding.as_ref().expect("open");
    assert!(dialog.logo.notice.is_some());
    assert!(!dialog.logo.shows_remove);
    assert_eq!(dialog.logo.value, "Not set");

    // Closing puts the dialog away.
    let closed = removed_session.ui(event(SchedulingSettingsAction::CloseDialog));
    assert!(settings_view(&closed).branding.is_none());
}

/// The profile's dialog: the fields as read, every time zone with the
/// member's chosen, the formats by name, and the picture.
#[test]
fn the_profile_saves_with_its_choices() {
    let open = settings_view(&profile_open(signed_in()))
        .profile
        .expect("open");
    assert_eq!(open.name, "Contract Member");
    let chosen: Vec<&str> = open
        .timezones
        .iter()
        .filter(|c| c.selected)
        .map(|c| c.value.as_str())
        .collect();
    assert_eq!(chosen, ["America/Toronto"]);
    assert!(open.timezones.len() > 100);
    let formats: Vec<(&str, bool)> = open
        .time_formats
        .iter()
        .map(|c| (c.value.as_str(), c.selected))
        .collect();
    assert_eq!(formats, [("12h", false), ("24h", true)]);
    assert_eq!(open.week_starts.len(), 7);
    assert_eq!(open.week_starts[0].value, "0");
    assert!(open.week_starts[1].selected);
    assert_eq!(open.week_starts[1].label, "Monday");
    assert!(
        open.date_formats
            .iter()
            .any(|c| c.value == "ymd" && c.selected)
    );
    assert_eq!(open.avatar.target, SchedulingImageTarget::Avatar);
    assert!(open.avatar.shows_remove);

    let form = SettingsProfileForm {
        name: String::new(),
        timezone: "America/Toronto".to_owned(),
        time_format: "12h".to_owned(),
        week_start: 0,
        date_format: "mdy".to_owned(),
    };
    let refused = settings_view(
        &profile_open(signed_in())
            .ui(event(SchedulingSettingsAction::EditProfile {
                form: form.clone(),
            }))
            .ui(event(SchedulingSettingsAction::SaveProfile)),
    )
    .profile
    .expect("open");
    assert!(refused.error.is_some());

    let saving = profile_open(signed_in())
        .ui(event(SchedulingSettingsAction::EditProfile {
            form: SettingsProfileForm {
                name: "Ada".to_owned(),
                ..form
            },
        }))
        .ui(event(SchedulingSettingsAction::SaveProfile));
    assert!(sent(&saving, |r| matches!(
        r,
        SchedulingAdminRequest::PatchMe(patch) if patch.name.as_deref() == Some("Ada")
            && patch.week_start == Some(0)
    )));
    let saved = settings_view(&answer(
        saving,
        |r| matches!(r, SchedulingAdminRequest::PatchMe(_)),
        Ok("district-scheduling-me.json"),
    ))
    .profile
    .expect("open");
    assert!(saved.notice.is_some());

    let removing = profile_open(signed_in()).ui(event(SchedulingSettingsAction::RemoveImage {
        target: SchedulingImageTarget::Avatar,
    }));
    assert!(sent(&removing, |r| matches!(
        r,
        SchedulingAdminRequest::DeleteAvatar
    )));
    let view = settings_view(&removing).profile.expect("open");
    assert!(view.avatar.working && !view.editable && !view.dialog.can_confirm);
}

/// The notifications' switches, in their two groups, saved whole; the
/// assistant's save offered once something changed.
#[test]
fn the_notifications_and_the_assistant_save() {
    let open = settings_view(&notifications_open(signed_in()))
        .notifications
        .expect("open");
    assert_eq!(open.groups.len(), 2);
    let reschedule = open.groups[0]
        .switches
        .iter()
        .find(|s| s.field == "notify_reschedule")
        .expect("listed");
    assert!(!reschedule.on);
    let switched = settings_view(&notifications_open(signed_in()).ui(event(
        SchedulingSettingsAction::SetNotification {
            field: "notify_reschedule".to_owned(),
            on: true,
        },
    )))
    .notifications
    .expect("open");
    assert!(
        switched.groups[0]
            .switches
            .iter()
            .any(|s| s.field == "notify_reschedule" && s.on)
    );
    let saved = settings_view(&notifications_saved(signed_in()))
        .notifications
        .expect("open");
    assert!(saved.notice.is_some());

    let assistant = settings_view(&assistant_open(signed_in()))
        .assistant
        .expect("open");
    assert!(assistant.enabled && !assistant.dialog.can_confirm);
    let changed = settings_view(&assistant_open(signed_in()).ui(event(
        SchedulingSettingsAction::EditAssistant {
            enabled: false,
            instructions: "Always list the agreed next steps first.".to_owned(),
        },
    )))
    .assistant
    .expect("open");
    assert!(changed.dialog.can_confirm && !changed.enabled);
    let saved = settings_view(&assistant_saved(signed_in()))
        .assistant
        .expect("open");
    assert!(saved.notice.is_some() && !saved.enabled && !saved.dialog.can_confirm);
}

/// The pick offers the scheduler's image types, one file, read one byte
/// past the largest it takes.
#[test]
fn the_image_pick_offers_the_schedulers_types() {
    let pick = scheduling_image_pick();
    assert_eq!(pick.extensions, [".jpg", ".jpeg", ".png", ".gif", ".webp"]);
    assert_eq!(pick.max_files, 1);
    assert_eq!(pick.read_cap, 5 * 1024 * 1024 + 1);
}

#[test]
fn each_action_is_its_core_event() {
    use SchedulingSettingsAction as A;
    use SettingsEvent as E;
    let s = |v: &str| v.to_owned();
    let to_core = |e| Event::SchedulingAdmin(SchedulingAdminEvent::Settings(e));
    let profile = SettingsProfileForm {
        name: s("Ada"),
        timezone: s("UTC"),
        time_format: s("24h"),
        week_start: 1,
        date_format: s("ymd"),
    };
    for (sent, event_sent) in [
        (
            A::SelectTab {
                tab: SchedulingSettingsTab::Profile,
            },
            E::SelectTab(SettingsTab::Profile),
        ),
        (A::Refresh, E::Refresh),
        (
            A::OpenEditor {
                tab: SchedulingSettingsTab::BookingPage,
            },
            E::OpenBranding,
        ),
        (
            A::OpenEditor {
                tab: SchedulingSettingsTab::Assistant,
            },
            E::OpenAssistant,
        ),
        (
            A::OpenEditor {
                tab: SchedulingSettingsTab::Profile,
            },
            E::OpenProfile,
        ),
        (
            A::OpenEditor {
                tab: SchedulingSettingsTab::Notifications,
            },
            E::OpenNotifications,
        ),
        (
            A::EditProfile { form: profile },
            E::EditProfile(ProfileForm {
                name: s("Ada"),
                timezone: s("UTC"),
                time_format: s("24h"),
                week_start: 1,
                date_format: s("ymd"),
            }),
        ),
        (A::SaveProfile, E::SaveProfile),
        (
            A::SetNotification {
                field: s("notify_reminder"),
                on: false,
            },
            E::SetNotification {
                field: s("notify_reminder"),
                on: false,
            },
        ),
        (A::SaveNotifications, E::SaveNotifications),
        (
            A::EditBranding {
                form: branding_form(40),
            },
            E::EditBranding(BrandingForm {
                business_name: s("Contract Agency"),
                logo_height: 40,
                logo_opacity: 100,
                banner_opacity: 80,
                privacy_url: String::new(),
                terms_url: s("https://www.contract.test/terms"),
                fallback_locale: s("en"),
            }),
        ),
        (A::SaveBranding, E::SaveBranding),
        (
            A::UploadImage {
                target: SchedulingImageTarget::Avatar,
                file: picked(PNG),
            },
            E::UploadImage {
                target: SchedulingUploadTarget::Avatar,
                file: ImageFile {
                    mime_type: s("image/png"),
                    bytes: PNG.to_vec(),
                },
            },
        ),
        (
            A::ImageUnreadable {
                target: SchedulingImageTarget::Banner,
            },
            E::ImageUnreadable(SchedulingUploadTarget::Banner),
        ),
        (
            A::RemoveImage {
                target: SchedulingImageTarget::Logo,
            },
            E::RemoveImage(SchedulingUploadTarget::Logo),
        ),
        (
            A::EditAssistant {
                enabled: true,
                instructions: s("Be brief."),
            },
            E::EditAssistant {
                enabled: true,
                instructions: s("Be brief."),
            },
        ),
        (A::SaveAssistant, E::SaveAssistant),
        (A::CloseDialog, E::CloseDialog),
    ] {
        assert_eq!(event(sent).events(), [to_core(event_sent)]);
    }
}

/// A language the scheduler no longer offers is kept, first, as it is,
/// rather than shown as another.
#[test]
fn a_language_no_longer_offered_is_kept() {
    let mut branding = contracts::json("district-scheduling-branding.json");
    branding["data"]["fallback_locale"] = json!("de");
    let session = branding_read(
        open(signed_in(), SchedulingAdminSection::Settings),
        Ok(branding),
    )
    .ui(event(SchedulingSettingsAction::OpenEditor {
        tab: SchedulingSettingsTab::BookingPage,
    }));
    let locales = settings_view(&session).branding.expect("open").locales;
    let chosen: Vec<(&str, &str, bool)> = locales
        .iter()
        .map(|c| (c.value.as_str(), c.label.as_str(), c.selected))
        .collect();
    assert_eq!(
        chosen,
        [
            ("de", "de", true),
            ("en", "English", false),
            ("fr", "Français", false)
        ]
    );
}
