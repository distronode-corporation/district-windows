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

mod calls_live;
mod core;
mod events;
mod host;
mod identity;
mod link;
mod screen;
mod shell;
mod store;

pub use crate::calls_live::{
    ActiveCallView, DialerView, FailureView, IncomingRingView, active_call_view, dialer_view,
    incoming_ring_view,
};
pub use crate::core::{Core, StartConfig, StartError};
pub use crate::events::{PowerChange, UiEvent};
pub use crate::host::{NotificationActionView, NotificationView, UiHost};
pub use crate::identity::{PRODUCT, client_identity};
pub use crate::link::{LinkKind, link_kind};
pub use crate::screen::{RouteView, ScreenView, SessionScreen, screen_view};
pub use crate::shell::{SessionPhase, ShellView, TabView, shell_view};
pub use crate::store::{CredentialStore, MemoryVault, OUTBOX_MAX_BYTES, Vault};

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
}
