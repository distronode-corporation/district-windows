//! The boundary District AI for Windows calls: one [`Core`] object over the
//! shared Rust core (district-core-rust), exported to C# through UniFFI.
//!
//! # Shape
//!
//! The core is Elm-shaped: a `Model` takes events and returns effects, and an
//! `EffectRunner` runs each effect against the outside world and turns its
//! result into the next event. Here a single actor task owns the model, as the
//! GTK main thread does on Linux. After each update it swaps in a new snapshot
//! of what the window shows and calls [`UiHost::state_changed`] with the new
//! revision. The C# side coalesces those calls onto its UI thread and pulls
//! [`Core::shell`] and [`Core::screen`] once per batch.
//!
//! # Projections, not derives
//!
//! What C# reads is written here ([`ShellView`], [`ScreenView`]), not derived on
//! the core's types: UniFFI records need public fields, which the core's model
//! does not have, and derives would freeze hundreds of core types into a C#
//! ABI. Each projection matches the core's enums exhaustively, with no catch-all
//! arm, so a core bump that adds a state fails this crate's build instead of
//! drifting. The JSON snapshot tests pin what each projection produces.
//!
//! # What runs where
//!
//! | Trait | Here |
//! |---|---|
//! | DistrictApi, Auth, LiveUpdates, Presence, Clock, CallEngine | The core's own implementations, with [`client_identity`] |
//! | SessionStore | Credential Manager on Windows ([`CredentialStore`]), memory elsewhere |
//! | Settings, device id, refresh marker | `district-host` files in the data directory the app names (the MSIX `LocalState`) |
//! | UrlOpener | [`UiHost::open_url`], in C# |
//! | Notifier, RingSurface | [`UiHost::notify`], [`UiHost::start_ringtone`] and the rest, in C#; a toast's activation comes back through [`Core::activate_notification`] |
//!
//! # Logging
//!
//! Nothing here logs, and `log`'s `max_level_warn` is on for the whole build
//! (see Cargo.toml); `tests::log_ceiling` pins it.

uniffi::setup_scaffolding!();

// The areas of 1.0.
mod account;
mod calls;
mod calls_live;
mod contacts;
mod inbox;
mod overview;
mod report;

// The areas of 2.0, one module each (CONTRIBUTING.md, "Areas"). Public, so the
// packet that builds one adds its types without editing this file.
pub mod analytics;
pub mod billing;
pub mod blocked;
pub mod composer;
pub mod desk;
pub mod hq;
pub mod marketplace;
pub mod rooms;
pub mod scheduling;
pub mod settings;
pub mod support;
pub mod workflows;

// What the areas of 2.0 share, each filled by its own packet.
mod chart;
mod checkout;
mod copies;
mod files;
mod guard;
mod palette;
mod push;
mod rich_text;
mod transcript;

// The boundary itself.
mod core;
mod events;
mod host;
mod identity;
mod link;
mod nav;
mod screen;
mod shell;
mod store;
mod views;

pub use crate::account::{AccountView, ConfirmView, DeviceRowView, DevicesView};
pub use crate::calls::{
    CALL_FAILED_TITLE, CallDetailView, CallRowView, CallsView, TranscriptState,
};
pub use crate::calls_live::{
    ActiveCallView, DialerView, IncomingRingView, active_call_view, dialer_view, incoming_ring_view,
};
pub use crate::contacts::{
    CONTACT_FAILED_TITLE, ContactDetailView, ContactFormInput, ContactFormView,
    ContactQuestionView, ContactRowView, ContactWritesView, ContactsAction, ContactsView,
};
pub use crate::core::{Core, StartConfig, StartError};
pub use crate::events::UiEvent;
pub use crate::host::{NotificationActionView, NotificationView, UiHost};
pub use crate::identity::{PRODUCT, client_identity};
pub use crate::inbox::{
    InboxView, SearchHitView, SearchView, THREAD_FAILED_TITLE, ThreadRowView, ThreadView,
    TimelineItemView, TimelineKind,
};
pub use crate::link::{LinkKind, link_kind};
pub use crate::nav::{NavDestination, NavEntryView, NavGroupView, NavSection, NavView};
pub use crate::overview::{FinishSetupView, NO_RECENT_CALLS, OVERVIEW_FAILED_TITLE, OverviewView};
pub use crate::report::{
    DRAFT_OPEN, NO_NOTE, NOTE_LIMIT, PREAMBLE, REPORT_SUBJECT, ReportStatus, ui_events,
};
pub use crate::screen::{
    ScreenView, SessionScreen, UNAVAILABLE_BODY, UNAVAILABLE_TITLE, screen_view,
};
pub use crate::shell::{
    LiveBannerView, SessionPhase, ShellView, TabView, WorkspaceEntryView, WorkspaceSwitcherView,
    shell_view,
};
pub use crate::store::{CredentialStore, MemoryVault, OUTBOX_MAX_BYTES, Vault};
pub use crate::views::{
    AI_DOSSIER, AI_SUMMARY, AiTextView, EmptyView, FactView, FailureView, LoadStatus, PagingView,
    REPORT_WEB_URL, ReportAvailability, ReportTarget,
};

#[cfg(windows)]
pub use crate::store::WindowsVault;

#[cfg(test)]
mod tests {
    /// `log`'s ceiling is warn for the whole build: every `info!`, `debug!` and
    /// `trace!` in every crate linked into the DLL is compiled out. If this
    /// fails, the `max_level_warn` feature has gone from Cargo.toml, and the
    /// telemetry token (tungstenite, at trace) and a call's room key
    /// (libwebrtc, at debug) could reach a logger again.
    #[test]
    fn log_ceiling() {
        assert_eq!(log::STATIC_MAX_LEVEL, log::LevelFilter::Warn);
    }

    /// The Windows build links the LiveKit call engine, so the core rings,
    /// dials and answers; the Linux build, which only DistrictAI.Core.Tests
    /// load, has the engine that joins nothing. If this fails on Windows,
    /// district-call's `livekit` feature has gone from Cargo.toml.
    #[test]
    fn calls_are_available_exactly_on_windows() {
        assert_eq!(district_call::CALLS_AVAILABLE, cfg!(windows));
    }
}
